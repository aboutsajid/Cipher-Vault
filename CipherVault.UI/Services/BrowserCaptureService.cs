using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CipherVault.UI.Services;

public sealed class BrowserCaptureService : IDisposable
{
    private const int DefaultPort = 47633;
    private const string ExtensionClientHeaderName = "X-CipherVault-Client";
    private const string ExtensionClientHeaderValue = "BrowserExtension";
    private const string SessionTokenHeaderName = "X-CipherVault-Session";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpListener _listener = new();
    private readonly TimeSpan _sessionTokenLifetime;
    private readonly Func<DateTime> _utcNow;
    private readonly object _sessionTokenLock = new();

    private CancellationTokenSource? _cts;
    private Task? _listenLoopTask;
    private bool _disposed;
    private string? _sessionToken;
    private DateTime _sessionTokenExpiresUtc = DateTime.MinValue;

    public int Port { get; }
    public bool IsRunning => _listener.IsListening;
    public string Endpoint => $"http://127.0.0.1:{Port}/capture";

    public event EventHandler<BrowserCredentialCapturedEventArgs>? CredentialCaptured;
    public Func<BrowserAutofillQueryRequest, Task<IReadOnlyList<BrowserAutofillCredential>>>? AutofillQueryHandler { get; set; }

    public BrowserCaptureService(
        int port = DefaultPort,
        TimeSpan? sessionTokenLifetime = null,
        Func<DateTime>? utcNow = null)
    {
        Port = port;
        _sessionTokenLifetime = sessionTokenLifetime.GetValueOrDefault(TimeSpan.FromMinutes(15));
        if (_sessionTokenLifetime <= TimeSpan.Zero)
            _sessionTokenLifetime = TimeSpan.FromMinutes(15);

        _utcNow = utcNow ?? (() => DateTime.UtcNow);

        _listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        _listener.Prefixes.Add($"http://localhost:{port}/");
    }

    public void Start()
    {
        ThrowIfDisposed();
        if (_listener.IsListening) return;

        RotateSessionToken();
        _cts = new CancellationTokenSource();
        _listener.Start();
        _listenLoopTask = Task.Run(() => ListenLoopAsync(_cts.Token));
    }

    public void Stop()
    {
        if (!_listener.IsListening)
        {
            ClearSessionToken();
            return;
        }

        try
        {
            _cts?.Cancel();
            _listener.Stop();
        }
        catch
        {
            // Ignore shutdown errors.
        }
        finally
        {
            _cts?.Dispose();
            _cts = null;
            _listenLoopTask = null;
            ClearSessionToken();
        }
    }

    private async Task ListenLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            HttpListenerContext? context = null;
            try
            {
                context = await _listener.GetContextAsync();
            }
            catch (HttpListenerException) when (token.IsCancellationRequested || !_listener.IsListening)
            {
                break;
            }
            catch (ObjectDisposedException) when (token.IsCancellationRequested)
            {
                break;
            }

            if (context == null) continue;

            _ = Task.Run(async () =>
            {
                try
                {
                    await HandleContextAsync(context, token);
                }
                catch
                {
                    TryClose(context.Response);
                }
            }, token);
        }
    }

    private async Task HandleContextAsync(HttpListenerContext context, CancellationToken token)
    {
        var request = context.Request;
        var response = context.Response;
        string? origin = request.Headers["Origin"];
        bool originAllowed = IsAllowedExtensionOrigin(origin);

        WriteCorsHeaders(response, origin, originAllowed);
        response.Headers["Cache-Control"] = "no-store";

        if (!IsLoopbackRequest(request))
        {
            await WriteJsonAsync(response, HttpStatusCode.Forbidden, new { ok = false, error = "forbidden" }, token);
            return;
        }

        if (request.HttpMethod.Equals("OPTIONS", StringComparison.OrdinalIgnoreCase))
        {
            response.StatusCode = originAllowed
                ? (int)HttpStatusCode.NoContent
                : (int)HttpStatusCode.Forbidden;
            TryClose(response);
            return;
        }

        if (!originAllowed)
        {
            await WriteJsonAsync(response, HttpStatusCode.Forbidden, new { ok = false, error = "origin_not_allowed" }, token);
            return;
        }

        if (!IsTrustedExtensionClient(request))
        {
            await WriteJsonAsync(response, HttpStatusCode.Forbidden, new { ok = false, error = "client_not_allowed" }, token);
            return;
        }

        string path = request.Url?.AbsolutePath ?? "/";

        if (path.Equals("/session/token", StringComparison.OrdinalIgnoreCase))
        {
            await HandleSessionTokenRequestAsync(request, response, token);
            return;
        }

        if (path.Equals("/capture", StringComparison.OrdinalIgnoreCase)
            || path.Equals("/autofill/query", StringComparison.OrdinalIgnoreCase))
        {
            if (!TryAuthorizeSessionRequest(request))
            {
                await WriteJsonAsync(response, HttpStatusCode.Unauthorized, new { ok = false, error = "session_token_invalid_or_expired" }, token);
                return;
            }

            if (path.Equals("/capture", StringComparison.OrdinalIgnoreCase))
            {
                await HandleCaptureRequestAsync(request, response, token);
                return;
            }

            await HandleAutofillQueryAsync(request, response, token);
            return;
        }

        await WriteJsonAsync(response, HttpStatusCode.NotFound, new { ok = false, error = "not_found" }, token);
    }

    private async Task HandleSessionTokenRequestAsync(HttpListenerRequest request, HttpListenerResponse response, CancellationToken token)
    {
        if (!request.HttpMethod.Equals("POST", StringComparison.OrdinalIgnoreCase))
        {
            await WriteJsonAsync(response, HttpStatusCode.MethodNotAllowed, new { ok = false, error = "method_not_allowed" }, token);
            return;
        }

        var tokenEnvelope = GetOrRefreshSessionToken();

        await WriteJsonAsync(response, HttpStatusCode.OK, new
        {
            ok = true,
            token = tokenEnvelope.Token,
            expiresAtUtc = tokenEnvelope.ExpiresUtc.ToString("O"),
            ttlSeconds = Math.Max(1, (int)Math.Floor(_sessionTokenLifetime.TotalSeconds))
        }, token);
    }

    private async Task HandleCaptureRequestAsync(HttpListenerRequest request, HttpListenerResponse response, CancellationToken token)
    {
        if (!request.HttpMethod.Equals("POST", StringComparison.OrdinalIgnoreCase))
        {
            await WriteJsonAsync(response, HttpStatusCode.MethodNotAllowed, new { ok = false, error = "method_not_allowed" }, token);
            return;
        }

        BrowserCaptureRequest? payload = await ReadRequestBodyAsync<BrowserCaptureRequest>(request, token);

        if (payload == null || string.IsNullOrWhiteSpace(payload.Url) || string.IsNullOrWhiteSpace(payload.Password))
        {
            await WriteJsonAsync(response, HttpStatusCode.BadRequest, new { ok = false, error = "invalid_payload" }, token);
            return;
        }

        var captured = new BrowserCredentialCapturedEventArgs
        {
            Title = ResolveTitle(payload.Title, payload.Url),
            Url = Truncate(payload.Url, 1024),
            Username = Truncate(payload.Username, 256),
            Password = Truncate(payload.Password, 512),
            SourceBrowser = Truncate(payload.SourceBrowser, 64),
            CapturedAtUtc = DateTime.UtcNow
        };

        _ = Task.Run(() =>
        {
            try
            {
                CredentialCaptured?.Invoke(this, captured);
            }
            catch
            {
                // Ignore consumer errors and keep listener alive.
            }
        });

        await WriteJsonAsync(response, HttpStatusCode.OK, new { ok = true }, token);
    }

    private async Task HandleAutofillQueryAsync(HttpListenerRequest request, HttpListenerResponse response, CancellationToken token)
    {
        if (!request.HttpMethod.Equals("POST", StringComparison.OrdinalIgnoreCase))
        {
            await WriteJsonAsync(response, HttpStatusCode.MethodNotAllowed, new { ok = false, error = "method_not_allowed" }, token);
            return;
        }

        BrowserAutofillQueryPayload? payload = await ReadRequestBodyAsync<BrowserAutofillQueryPayload>(request, token);
        if (payload == null || string.IsNullOrWhiteSpace(payload.Url))
        {
            await WriteJsonAsync(response, HttpStatusCode.BadRequest, new { ok = false, error = "invalid_payload" }, token);
            return;
        }

        int requestedLimit = payload.Limit.GetValueOrDefault(5);
        int limit = Math.Clamp(requestedLimit, 1, 12);
        var query = new BrowserAutofillQueryRequest
        {
            Url = Truncate(payload.Url, 1024),
            Limit = limit
        };

        IReadOnlyList<BrowserAutofillCredential> resolved = Array.Empty<BrowserAutofillCredential>();
        if (AutofillQueryHandler != null)
        {
            try
            {
                resolved = await AutofillQueryHandler(query);
            }
            catch
            {
                resolved = Array.Empty<BrowserAutofillCredential>();
            }
        }

        var sanitized = resolved
            .Take(limit)
            .Select(entry => new BrowserAutofillCredential
            {
                Title = Truncate(entry.Title, 128),
                Username = Truncate(entry.Username, 256),
                Password = Truncate(entry.Password, 512),
                Url = Truncate(entry.Url, 1024),
                Favorite = entry.Favorite
            })
            .ToList();

        await WriteJsonAsync(response, HttpStatusCode.OK, new
        {
            ok = true,
            entries = sanitized
        }, token);
    }

    private static async Task<T?> ReadRequestBodyAsync<T>(HttpListenerRequest request, CancellationToken token) where T : class
    {
        using var reader = new StreamReader(request.InputStream, request.ContentEncoding ?? Encoding.UTF8, leaveOpen: false);
        var body = await reader.ReadToEndAsync(token);
        return JsonSerializer.Deserialize<T>(body, JsonOptions);
    }

    private static bool IsLoopbackRequest(HttpListenerRequest request)
    {
        return request.RemoteEndPoint != null && IPAddress.IsLoopback(request.RemoteEndPoint.Address);
    }

    private static bool IsAllowedExtensionOrigin(string? origin)
    {
        if (string.IsNullOrWhiteSpace(origin))
            return false;

        if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri))
            return false;

        if (!uri.Scheme.Equals("chrome-extension", StringComparison.OrdinalIgnoreCase)
            && !uri.Scheme.Equals("edge-extension", StringComparison.OrdinalIgnoreCase)
            && !uri.Scheme.Equals("moz-extension", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return !string.IsNullOrWhiteSpace(uri.Host);
    }

    private static bool IsTrustedExtensionClient(HttpListenerRequest request)
    {
        string? headerValue = request.Headers[ExtensionClientHeaderName];
        return string.Equals(headerValue, ExtensionClientHeaderValue, StringComparison.OrdinalIgnoreCase);
    }

    private bool TryAuthorizeSessionRequest(HttpListenerRequest request)
    {
        string? providedToken = request.Headers[SessionTokenHeaderName];
        if (string.IsNullOrWhiteSpace(providedToken))
            return false;

        lock (_sessionTokenLock)
        {
            if (string.IsNullOrWhiteSpace(_sessionToken))
                return false;

            var now = _utcNow();
            if (now >= _sessionTokenExpiresUtc)
            {
                ClearSessionTokenLocked();
                return false;
            }

            if (!FixedTimeEquals(providedToken, _sessionToken))
                return false;

            _sessionTokenExpiresUtc = now.Add(_sessionTokenLifetime);
            return true;
        }
    }

    private (string Token, DateTime ExpiresUtc) GetOrRefreshSessionToken()
    {
        lock (_sessionTokenLock)
        {
            var now = _utcNow();
            if (string.IsNullOrWhiteSpace(_sessionToken) || now >= _sessionTokenExpiresUtc)
            {
                RotateSessionTokenLocked(now);
            }

            return (_sessionToken!, _sessionTokenExpiresUtc);
        }
    }

    private void RotateSessionToken()
    {
        lock (_sessionTokenLock)
        {
            RotateSessionTokenLocked(_utcNow());
        }
    }

    private void ClearSessionToken()
    {
        lock (_sessionTokenLock)
        {
            ClearSessionTokenLocked();
        }
    }

    private void RotateSessionTokenLocked(DateTime now)
    {
        byte[] tokenBytes = RandomNumberGenerator.GetBytes(32);
        try
        {
            _sessionToken = Convert.ToHexString(tokenBytes);
            _sessionTokenExpiresUtc = now.Add(_sessionTokenLifetime);
        }
        finally
        {
            Array.Clear(tokenBytes, 0, tokenBytes.Length);
        }
    }

    private void ClearSessionTokenLocked()
    {
        _sessionToken = null;
        _sessionTokenExpiresUtc = DateTime.MinValue;
    }

    private static bool FixedTimeEquals(string left, string right)
    {
        if (left.Length != right.Length)
            return false;

        int diff = 0;
        for (int i = 0; i < left.Length; i++)
            diff |= left[i] ^ right[i];

        return diff == 0;
    }

    private static string ResolveTitle(string? title, string? url)
    {
        string normalized = Truncate(title, 128);
        if (!string.IsNullOrWhiteSpace(normalized))
            return normalized;

        if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return uri.Host;

        return "Detected Login";
    }

    private static string Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var text = value.Trim();
        return text.Length <= maxLength ? text : text[..maxLength];
    }

    private static void WriteCorsHeaders(HttpListenerResponse response, string? origin, bool allowOrigin)
    {
        if (!allowOrigin || string.IsNullOrWhiteSpace(origin))
            return;

        response.Headers["Access-Control-Allow-Origin"] = origin;
        response.Headers["Access-Control-Allow-Methods"] = "POST, OPTIONS";
        response.Headers["Access-Control-Allow-Headers"] = $"Content-Type, {ExtensionClientHeaderName}, {SessionTokenHeaderName}";
        response.Headers["Access-Control-Max-Age"] = "600";
        response.Headers["Vary"] = "Origin";
    }

    private static async Task WriteJsonAsync(HttpListenerResponse response, HttpStatusCode statusCode, object payload, CancellationToken token)
    {
        try
        {
            byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(payload);
            response.StatusCode = (int)statusCode;
            response.ContentType = "application/json";
            response.ContentEncoding = Encoding.UTF8;
            response.ContentLength64 = bytes.Length;
            await response.OutputStream.WriteAsync(bytes, token);
        }
        catch
        {
            // Ignore response write failures (client disconnected etc.)
        }
        finally
        {
            TryClose(response);
        }
    }

    private static void TryClose(HttpListenerResponse response)
    {
        try
        {
            response.OutputStream.Close();
            response.Close();
        }
        catch
        {
            // Ignore close failures.
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(BrowserCaptureService));
    }

    public void Dispose()
    {
        if (_disposed) return;
        Stop();
        _listener.Close();
        _disposed = true;
    }
}

public sealed class BrowserCredentialCapturedEventArgs : EventArgs
{
    public string Title { get; init; } = string.Empty;
    public string Url { get; init; } = string.Empty;
    public string Username { get; init; } = string.Empty;
    public string Password { get; init; } = string.Empty;
    public string SourceBrowser { get; init; } = string.Empty;
    public DateTime CapturedAtUtc { get; init; }
}

internal sealed class BrowserCaptureRequest
{
    public string? Title { get; init; }
    public string? Url { get; init; }
    public string? Username { get; init; }
    public string? Password { get; init; }
    public string? SourceBrowser { get; init; }
}

public sealed class BrowserAutofillQueryRequest
{
    public string Url { get; init; } = string.Empty;
    public int Limit { get; init; } = 5;
}

public sealed class BrowserAutofillCredential
{
    public string Title { get; init; } = string.Empty;
    public string Username { get; init; } = string.Empty;
    public string Password { get; init; } = string.Empty;
    public string Url { get; init; } = string.Empty;
    public bool Favorite { get; init; }
}

internal sealed class BrowserAutofillQueryPayload
{
    public string? Url { get; init; }
    public int? Limit { get; init; }
}
