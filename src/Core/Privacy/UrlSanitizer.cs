using System.Collections.Specialized;
using System.Text.RegularExpressions;
using System.Web;

namespace LocalActivityTracker.Core.Privacy;

/// <summary>
/// Enforces URL privacy by stripping authentication tokens, passwords, keys, and credentials from query parameters and fragments.
/// </summary>
public sealed class UrlSanitizer : IUrlSanitizer
{
    private static readonly HashSet<string> SensitiveQueryParams = new(StringComparer.OrdinalIgnoreCase)
    {
        "token",
        "auth",
        "authtoken",
        "auth_token",
        "bearer",
        "password",
        "pass",
        "pwd",
        "secret",
        "client_secret",
        "private_key",
        "code",
        "access_token",
        "api_key",
        "apikey",
        "session",
        "session_id",
        "sessionid",
        "sessid",
        "sig",
        "signature",
        "key",
        "credential",
        "credentials",
        "jwt",
        "id_token",
        "refresh_token",
        "oauth_token",
        "oauth_verifier",
        "state",
        "ticket",
        "hash",
        "samlresponse",
        "relaystate",
        "assertion",
        "x-amz-signature",
        "x-amz-credential",
        "x-amz-security-token",
        "stripe_token",
        "csrf",
        "xsrf_token",
        "verification_code",
        "cvv",
        "ssn",
        "pin"
    };

    private static readonly string[] RestrictedSchemes =
    {
        "chrome:",
        "chrome-extension:",
        "edge:",
        "about:",
        "data:",
        "file:",
        "javascript:"
    };

    public SanitizedUrlResult Sanitize(string? rawUrl)
    {
        if (string.IsNullOrWhiteSpace(rawUrl))
        {
            return new SanitizedUrlResult(string.Empty, string.Empty, string.Empty, string.Empty, true);
        }

        var trimmedUrl = rawUrl.Trim();

        // Check restricted schemes
        foreach (var scheme in RestrictedSchemes)
        {
            if (trimmedUrl.StartsWith(scheme, StringComparison.OrdinalIgnoreCase))
            {
                var cleanSchemeUrl = trimmedUrl.Split('?')[0].Split('#')[0];
                var name = scheme.TrimEnd(':');
                return new SanitizedUrlResult(trimmedUrl, cleanSchemeUrl, name, name, true);
            }
        }

        if (!Uri.TryCreate(trimmedUrl, UriKind.Absolute, out var uri))
        {
            var fallback = trimmedUrl.Split('?')[0].Split('#')[0];
            return new SanitizedUrlResult(trimmedUrl, fallback, "unknown", "unknown", false);
        }

        var builder = new UriBuilder(uri);

        // 1. Strip embedded username and password from authority (e.g. user:pass@host)
        builder.UserName = string.Empty;
        builder.Password = string.Empty;

        // 2. Strip sensitive query parameters
        if (!string.IsNullOrEmpty(builder.Query))
        {
            var query = HttpUtility.ParseQueryString(builder.Query);
            var keysToRemove = query.AllKeys
                .Where(k => !string.IsNullOrEmpty(k) && SensitiveQueryParams.Contains(k))
                .ToList();

            foreach (var key in keysToRemove)
            {
                query.Remove(key);
            }

            builder.Query = query.ToString();
        }

        // 3. Strip fragments containing access tokens or auth info
        if (!string.IsNullOrEmpty(builder.Fragment))
        {
            var lowerFrag = builder.Fragment.ToLowerInvariant();
            if (lowerFrag.Contains("access_token=") || lowerFrag.Contains("token=") || lowerFrag.Contains("id_token="))
            {
                builder.Fragment = string.Empty;
            }
        }

        var hostname = uri.Host.ToLowerInvariant();
        var domain = ExtractRootDomain(hostname);
        var finalSanitized = builder.Uri.ToString();

        // UriBuilder adds default port or trailing slash in some cases; clean up if original didn't have it
        if (!trimmedUrl.EndsWith('/') && finalSanitized.EndsWith('/'))
        {
            var pathAndQuery = builder.Uri.PathAndQuery;
            if (pathAndQuery == "/")
            {
                finalSanitized = finalSanitized.TrimEnd('/');
            }
        }

        return new SanitizedUrlResult(trimmedUrl, finalSanitized, domain, hostname, false);
    }

    private static string ExtractRootDomain(string hostname)
    {
        if (string.IsNullOrWhiteSpace(hostname))
        {
            return string.Empty;
        }

        var parts = hostname.Split('.');
        if (parts.Length <= 2)
        {
            return hostname;
        }

        return string.Join('.', parts.TakeLast(2));
    }
}
