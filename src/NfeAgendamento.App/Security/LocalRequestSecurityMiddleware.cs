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
    private readonly CentralStateService _centralState;

    public LocalRequestSecurityMiddleware(RequestDelegate next, CsrfTokenService csrf, CentralStateService centralState)
    {
        _next = next ?? throw new ArgumentNullException(nameof(next));
        _csrf = csrf ?? throw new ArgumentNullException(nameof(csrf));
        _centralState = centralState ?? throw new ArgumentNullException(nameof(centralState));
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var isLoopback = context.Connection.RemoteIpAddress is not null
            && IPAddress.IsLoopback(context.Connection.RemoteIpAddress);
        if (!isLoopback && !_centralState.IsEnabled)
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        if (!AllowedHosts.Contains(context.Request.Host.Value)
            && !(_centralState.IsEnabled && IsLanHost(context.Request.Host)))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        var origin = context.Request.Headers.Origin.ToString();
        if (!string.IsNullOrWhiteSpace(origin) && !IsAllowedOrigin(origin, context.Request.Host, _centralState.IsEnabled))
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

        await _next(context);
    }

    private static bool IsLanHost(HostString host) =>
        host.Port == LocalHost.Port && !string.IsNullOrWhiteSpace(host.Host);

    private static bool IsAllowedOrigin(string origin, HostString requestHost, bool centralEnabled)
    {
        if (AllowedOrigins.Contains(origin))
            return true;

        return centralEnabled
            && Uri.TryCreate(origin, UriKind.Absolute, out var parsed)
            && string.Equals(parsed.Scheme, "http", StringComparison.OrdinalIgnoreCase)
            && string.Equals(parsed.Host, requestHost.Host, StringComparison.OrdinalIgnoreCase)
            && parsed.Port == LocalHost.Port;
    }

    private static bool IsMutating(string method) =>
        HttpMethods.IsPost(method)
        || HttpMethods.IsPut(method)
        || HttpMethods.IsPatch(method)
        || HttpMethods.IsDelete(method);
}
