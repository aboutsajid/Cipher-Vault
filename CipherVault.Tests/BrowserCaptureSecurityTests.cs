using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using CipherVault.UI.Services;
using Xunit;

namespace CipherVault.Tests;

public sealed class BrowserCaptureSecurityTests
{
    private const string ExtensionOrigin = "chrome-extension://abcdefghijklmnop";

    [Fact]
    public async Task AutofillQuery_RejectsNonExtensionOrigin()
    {
        int port = GetAvailablePort();
        using BrowserCaptureService _ = CreateRunningService(port);

        using HttpClient client = new HttpClient();
        using HttpRequestMessage request = CreateJsonPost($"http://127.0.0.1:{port}/autofill/query", new
        {
            url = "https://example.com",
            limit = 1
        }, "https://evil.example", includeTrustedClientHeader: true, sessionToken: null);

        using HttpResponseMessage response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task AutofillQuery_RejectsMissingClientHeader()
    {
        int port = GetAvailablePort();
        using BrowserCaptureService _ = CreateRunningService(port);

        using HttpClient client = new HttpClient();
        using HttpRequestMessage request = CreateJsonPost($"http://127.0.0.1:{port}/autofill/query", new
        {
            url = "https://example.com",
            limit = 1
        }, ExtensionOrigin, includeTrustedClientHeader: false, sessionToken: null);

        using HttpResponseMessage response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task AutofillQuery_RejectsMissingSessionToken()
    {
        int port = GetAvailablePort();
        using BrowserCaptureService _ = CreateRunningService(port);

        using HttpClient client = new HttpClient();
        using HttpRequestMessage request = CreateJsonPost($"http://127.0.0.1:{port}/autofill/query", new
        {
            url = "https://example.com",
            limit = 1
        }, ExtensionOrigin, includeTrustedClientHeader: true, sessionToken: null);

        using HttpResponseMessage response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task AutofillQuery_RejectsWrongSessionToken()
    {
        int port = GetAvailablePort();
        using BrowserCaptureService _ = CreateRunningService(port);

        using HttpClient client = new HttpClient();
        await FetchSessionTokenAsync(client, port);

        using HttpRequestMessage request = CreateJsonPost($"http://127.0.0.1:{port}/autofill/query", new
        {
            url = "https://example.com",
            limit = 1
        }, ExtensionOrigin, includeTrustedClientHeader: true, sessionToken: "BAD_TOKEN");

        using HttpResponseMessage response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task AutofillQuery_RejectsExpiredSessionToken()
    {
        int port = GetAvailablePort();
        DateTime nowUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        using BrowserCaptureService service = new BrowserCaptureService(port, TimeSpan.FromMinutes(1), () => nowUtc);
        service.AutofillQueryHandler = _ => Task.FromResult<IReadOnlyList<BrowserAutofillCredential>>(Array.Empty<BrowserAutofillCredential>());
        service.Start();

        using HttpClient client = new HttpClient();
        string token = await FetchSessionTokenAsync(client, port);
        nowUtc = nowUtc.AddMinutes(2);

        using HttpRequestMessage request = CreateJsonPost($"http://127.0.0.1:{port}/autofill/query", new
        {
            url = "https://example.com",
            limit = 1
        }, ExtensionOrigin, includeTrustedClientHeader: true, sessionToken: token);

        using HttpResponseMessage response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task AutofillQuery_AllowsTrustedExtensionRequest()
    {
        int port = GetAvailablePort();
        using BrowserCaptureService _ = CreateRunningService(port);

        using HttpClient client = new HttpClient();
        string token = await FetchSessionTokenAsync(client, port);

        using HttpRequestMessage request = CreateJsonPost($"http://127.0.0.1:{port}/autofill/query", new
        {
            url = "https://example.com",
            limit = 1
        }, ExtensionOrigin, includeTrustedClientHeader: true, sessionToken: token);

        using HttpResponseMessage response = await client.SendAsync(request);
        string json = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(ExtensionOrigin, response.Headers.GetValues("Access-Control-Allow-Origin").Single());

        using JsonDocument doc = JsonDocument.Parse(json);
        Assert.True(doc.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal(1, doc.RootElement.GetProperty("entries").GetArrayLength());
    }

    [Fact]
    public async Task AutofillQuery_ClampsLimitTo12()
    {
        int port = GetAvailablePort();
        using BrowserCaptureService _ = CreateRunningService(port, credentialCount: 20);

        using HttpClient client = new HttpClient();
        string token = await FetchSessionTokenAsync(client, port);

        using HttpRequestMessage request = CreateJsonPost($"http://127.0.0.1:{port}/autofill/query", new
        {
            url = "https://example.com",
            limit = 99
        }, ExtensionOrigin, includeTrustedClientHeader: true, sessionToken: token);

        using HttpResponseMessage response = await client.SendAsync(request);
        string json = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using JsonDocument doc = JsonDocument.Parse(json);
        Assert.Equal(12, doc.RootElement.GetProperty("entries").GetArrayLength());
    }

    [Fact]
    public async Task Capture_RejectsMissingSessionToken()
    {
        int port = GetAvailablePort();
        using BrowserCaptureService _ = CreateRunningService(port);

        using HttpClient client = new HttpClient();
        using HttpRequestMessage request = CreateJsonPost($"http://127.0.0.1:{port}/capture", new
        {
            title = "Example",
            username = "alice",
            password = "secret",
            url = "https://example.com",
            sourceBrowser = "chrome"
        }, ExtensionOrigin, includeTrustedClientHeader: true, sessionToken: null);

        using HttpResponseMessage response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task OptionsPreflight_AllowsTrustedOriginAndClient()
    {
        int port = GetAvailablePort();
        using BrowserCaptureService _ = CreateRunningService(port);

        using HttpClient client = new HttpClient();
        using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Options, $"http://127.0.0.1:{port}/autofill/query");
        request.Headers.TryAddWithoutValidation("Origin", ExtensionOrigin);
        request.Headers.TryAddWithoutValidation("Access-Control-Request-Method", "POST");
        request.Headers.TryAddWithoutValidation("X-CipherVault-Client", "BrowserExtension");

        using HttpResponseMessage response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(ExtensionOrigin, response.Headers.GetValues("Access-Control-Allow-Origin").Single());
    }

    private static BrowserCaptureService CreateRunningService(int port, int credentialCount = 1)
    {
        BrowserCaptureService service = new BrowserCaptureService(port);
        service.AutofillQueryHandler = _ =>
        {
            IReadOnlyList<BrowserAutofillCredential> entries = Enumerable
                .Range(1, credentialCount)
                .Select(i => new BrowserAutofillCredential
                {
                    Title = $"Example {i}",
                    Username = "alice",
                    Password = "secret",
                    Url = "https://example.com",
                    Favorite = i == 1
                })
                .ToList();

            return Task.FromResult(entries);
        };
        service.Start();
        return service;
    }

    private static HttpRequestMessage CreateJsonPost(string url, object payload, string origin, bool includeTrustedClientHeader, string? sessionToken)
    {
        HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = JsonContent.Create(payload)
        };

        request.Headers.TryAddWithoutValidation("Origin", origin);
        if (includeTrustedClientHeader)
            request.Headers.TryAddWithoutValidation("X-CipherVault-Client", "BrowserExtension");

        if (!string.IsNullOrWhiteSpace(sessionToken))
            request.Headers.TryAddWithoutValidation("X-CipherVault-Session", sessionToken);

        return request;
    }

    private static async Task<string> FetchSessionTokenAsync(HttpClient client, int port)
    {
        using HttpRequestMessage tokenRequest = CreateJsonPost($"http://127.0.0.1:{port}/session/token", new { }, ExtensionOrigin, includeTrustedClientHeader: true, sessionToken: null);
        using HttpResponseMessage tokenResponse = await client.SendAsync(tokenRequest);
        string json = await tokenResponse.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, tokenResponse.StatusCode);

        using JsonDocument doc = JsonDocument.Parse(json);
        Assert.True(doc.RootElement.GetProperty("ok").GetBoolean());

        string token = doc.RootElement.GetProperty("token").GetString()!;
        Assert.False(string.IsNullOrWhiteSpace(token));
        return token;
    }

    private static int GetAvailablePort()
    {
        TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}



