namespace CipherVault.UI.Services;

public sealed class BrowserDomainService
{
    private static readonly string[] MultiLevelTlds =
    {
        "co.uk", "org.uk", "gov.uk", "ac.uk",
        "com.au", "net.au", "org.au",
        "com.pk", "com.br", "co.jp", "co.in"
    };

    public HashSet<string> ParseDomainList(string? rawValue)
    {
        var domains = new HashSet<string>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(rawValue))
            return domains;

        var pieces = rawValue.Split(
            new[] { ',', ';', '\r', '\n', ' ' },
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var piece in pieces)
        {
            var normalized = NormalizeDomain(piece);
            if (!string.IsNullOrWhiteSpace(normalized))
                domains.Add(normalized);
        }

        return domains;
    }

    public bool HostsMatch(string? urlA, string? urlB)
    {
        var hostA = NormalizeDomain(urlA);
        var hostB = NormalizeDomain(urlB);
        return hostA.Length > 0 && hostA == hostB;
    }

    public bool HostsCompatibleForAutofill(string? storedUrl, string? requestedUrl)
    {
        var storedHost = NormalizeDomain(storedUrl);
        var requestedHost = NormalizeDomain(requestedUrl);
        if (storedHost.Length == 0 || requestedHost.Length == 0)
            return false;

        return storedHost == requestedHost
               || storedHost.EndsWith("." + requestedHost, StringComparison.Ordinal)
               || requestedHost.EndsWith("." + storedHost, StringComparison.Ordinal)
               || GetAutofillRootDomain(storedHost) == GetAutofillRootDomain(requestedHost);
    }

    public bool IsDomainOrSubdomainMatch(string host, string candidateDomain)
    {
        if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(candidateDomain))
            return false;

        return host.Equals(candidateDomain, StringComparison.Ordinal)
               || host.EndsWith("." + candidateDomain, StringComparison.Ordinal);
    }

    public string NormalizeDomain(string? urlOrDomain)
    {
        if (string.IsNullOrWhiteSpace(urlOrDomain))
            return string.Empty;

        var value = urlOrDomain.Trim();
        if (Uri.TryCreate(value, UriKind.Absolute, out var uri))
            return uri.Host.Trim().Trim('.').ToLowerInvariant();

        if (Uri.TryCreate($"https://{value}", UriKind.Absolute, out var hostUri))
            return hostUri.Host.Trim().Trim('.').ToLowerInvariant();

        var firstSegment = value.Split('/', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? value;
        return firstSegment.Trim().Trim('.').ToLowerInvariant();
    }

    private static string GetAutofillRootDomain(string host)
    {
        var parts = host.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length <= 2)
            return host;

        string tail2 = string.Join('.', parts[^2], parts[^1]);
        string tail3 = string.Join('.', parts[^3], parts[^2], parts[^1]);

        if (MultiLevelTlds.Contains(tail2, StringComparer.OrdinalIgnoreCase) && parts.Length >= 3)
            return tail3;

        return tail2;
    }
}
