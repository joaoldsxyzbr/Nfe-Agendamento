using System.Net;
using Microsoft.AspNetCore.Http;

namespace NfeAgendamento.App.Security;

public sealed class LocalRequestSecurityMiddleware
{
    private static readonly HashSet<string> AllowedHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "127.0.0.1:17345",
        "localhost:17345",
        "nfeagendamento.local:17345"
    };

    private static readonly HashSet<string> AllowedOrigins = new(StringComparer.OrdinalIgnoreCase)
    {
        "http://127.0.0.1:17345",
        "http://localhost:17345"
    };

    private readonly RequestDelegate _next;
    private readonly CsrfTokenService _csrf;
    private readonly LocalSessionService _sessions;

    public LocalRequestSecurityMiddleware(RequestDelegate next, CsrfTokenService csrf, LocalSessionService? sessions = null)
    {
        _next = next ?? throw new ArgumentNullException(nameof(next));
        _csrf = csrf ?? throw new ArgumentNullException(nameof(csrf));
        _sessions = sessions ?? new LocalSessionService();
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var isLoopback = context.Connection.RemoteIpAddress is not null
            && IPAddress.IsLoopback(context.Connection.RemoteIpAddress);
        if (!isLoopback && !LocalHost.IsLanMode)
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        if (!AllowedHosts.Contains(context.Request.Host.Value)
            && !(LocalHost.IsLanMode && IsLanHost(context.Request.Host)))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        var origin = context.Request.Headers.Origin.ToString();
        if (!string.IsNullOrWhiteSpace(origin) && !IsAllowedOrigin(origin, context.Request.Host))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        if (IsMutating(context.Request.Method))
        {
            if (context.Request.ContentLength is > LocalHost.MaxRequestBodyBytes)
            {
                context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
                return;
            }

            if (!_csrf.Validate(context.Request.Headers["X-CSRF-Token"].ToString()))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }
        }

        if (!isLoopback && RequiresAuthentication(context.Request))
        {
            var session = context.Request.Cookies[LocalSessionService.CookieName];
            if (!_sessions.IsAuthenticated(session))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }
        }

        await _next(context);
    }

    private static bool IsLanHost(HostString host) =>
        host.Port == 17345 && !string.IsNullOrWhiteSpace(host.Host);

    private static bool IsAllowedOrigin(string origin, HostString requestHost)
    {
        if (AllowedOrigins.Contains(origin))
            return true;

        return LocalHost.IsLanMode
            && Uri.TryCreate(origin, UriKind.Absolute, out var parsed)
            && string.Equals(parsed.Scheme, "http", StringComparison.OrdinalIgnoreCase)
            && string.Equals(parsed.Host, requestHost.Host, StringComparison.OrdinalIgnoreCase)
            && parsed.Port == 17345;
    }

    private static bool RequiresAuthentication(HttpRequest request) =>
        request.Path.StartsWithSegments("/api")
        && !request.Path.StartsWithSegments("/api/bootstrap")
        && !request.Path.StartsWithSegments("/api/auth");

    private static bool IsMutating(string method) =>
        HttpMethods.IsPost(method)
        || HttpMethods.IsPut(method)
        || HttpMethods.IsPatch(method)
        || HttpMethods.IsDelete(method);
}
