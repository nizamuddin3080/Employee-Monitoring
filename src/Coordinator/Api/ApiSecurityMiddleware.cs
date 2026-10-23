using System.Net;

namespace LocalActivityTracker.Coordinator.Api;

/// <summary>
/// Hardened security middleware enforcing Host header validation, Origin checking, CSRF mitigation, capability token authorization, and defense-in-depth headers.
/// Conforms to api-contracts.md Section 4.1 and ADR-011.
/// </summary>
public sealed class ApiSecurityMiddleware
{
    public const string CsrfHeaderName = "X-LAT-Requested-With";
    public const string CsrfHeaderExpectedValue = "LocalActivityTracker";

    private readonly RequestDelegate _next;
    private readonly LocalCapabilityTokenProvider _tokenProvider;
    private readonly HashSet<string> _approvedHosts;
    private readonly HashSet<string> _approvedOrigins;

    public ApiSecurityMiddleware(RequestDelegate next, LocalCapabilityTokenProvider tokenProvider, int port = 48120)
    {
        _next = next;
        _tokenProvider = tokenProvider ?? throw new ArgumentNullException(nameof(tokenProvider));

        _approvedHosts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            $"127.0.0.1:{port}",
            $"localhost:{port}",
            "127.0.0.1:48120",
            "localhost:48120",
            "127.0.0.1",
            "localhost"
        };

        _approvedOrigins = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            $"http://127.0.0.1:{port}",
            $"http://localhost:{port}",
            "http://127.0.0.1:48120",
            "http://localhost:48120",
            "http://127.0.0.1",
            "http://localhost",
            "http://localhost:5173", // Dev Vite server
            "http://127.0.0.1:5173"
        };
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // 1. Loopback Address Verification
        var remoteIp = context.Connection.RemoteIpAddress;
        if (remoteIp != null && !IPAddress.IsLoopback(remoteIp))
        {
            context.Response.StatusCode = (int)HttpStatusCode.Forbidden;
            await context.Response.WriteAsync("Forbidden: Local Activity Tracker binds strictly to local loopback interface.");
            return;
        }

        // 2. Host Header Verification (DNS Rebinding Defense)
        var host = context.Request.Host.Value;
        if (string.IsNullOrWhiteSpace(host) || !_approvedHosts.Contains(host))
        {
            context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
            await context.Response.WriteAsync("Bad Request: Host header rejected. Requests must target 127.0.0.1:48120 or localhost:48120.");
            return;
        }

        // 3. Origin Header Verification (Cross-Site Request Protection)
        if (context.Request.Headers.TryGetValue("Origin", out var originValues))
        {
            var origin = originValues.ToString().Trim();
            if (!string.IsNullOrEmpty(origin) && !_approvedOrigins.Contains(origin))
            {
                context.Response.StatusCode = (int)HttpStatusCode.Forbidden;
                await context.Response.WriteAsync("Forbidden: Origin rejected. Wildcard and untrusted cross-origin requests are prohibited.");
                return;
            }

            // In development mode, allow CORS headers for approved local Vite origin
            if (_approvedOrigins.Contains(origin))
            {
                context.Response.Headers.Append("Access-Control-Allow-Origin", origin);
                context.Response.Headers.Append("Access-Control-Allow-Headers", "Authorization, Content-Type, X-LAT-Requested-With");
                context.Response.Headers.Append("Access-Control-Allow-Methods", "GET, POST, PUT, DELETE, OPTIONS");
            }
        }

        // Handle preflight OPTIONS request
        if (HttpMethods.IsOptions(context.Request.Method))
        {
            context.Response.StatusCode = (int)HttpStatusCode.NoContent;
            return;
        }

        // 4. Defense-in-Depth Security Headers
        context.Response.Headers.Append("X-Content-Type-Options", "nosniff");
        context.Response.Headers.Append("X-Frame-Options", "DENY");
        context.Response.Headers.Append("Referrer-Policy", "no-referrer");

        var path = context.Request.Path.Value ?? string.Empty;

        // 5. CSRF Mitigation on Mutation Requests (POST, PUT, DELETE)
        if (HttpMethods.IsPost(context.Request.Method) ||
            HttpMethods.IsPut(context.Request.Method) ||
            HttpMethods.IsDelete(context.Request.Method))
        {
            if (!context.Request.Headers.TryGetValue(CsrfHeaderName, out var csrfVal) ||
                !csrfVal.ToString().Equals(CsrfHeaderExpectedValue, StringComparison.Ordinal))
            {
                context.Response.StatusCode = (int)HttpStatusCode.Forbidden;
                await context.Response.WriteAsync($"Forbidden: Missing or invalid {CsrfHeaderName} header.");
                return;
            }
        }

        // 6. Capability Bearer Token Authorization for /api/ Endpoints
        if (path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase))
        {
            // Allow token retrieval for local loopback SPA bootstrap
            var isBootstrapEndpoint = path.Equals("/api/auth/token", StringComparison.OrdinalIgnoreCase) ||
                                     path.Equals("/api/auth/bootstrap", StringComparison.OrdinalIgnoreCase);

            if (!isBootstrapEndpoint)
            {
                var authHeader = context.Request.Headers.Authorization.ToString();
                if (!_tokenProvider.ValidateToken(authHeader))
                {
                    context.Response.StatusCode = (int)HttpStatusCode.Unauthorized;
                    await context.Response.WriteAsync("Unauthorized: Missing or invalid capability bearer token.");
                    return;
                }
            }
        }

        await _next(context);
    }
}
