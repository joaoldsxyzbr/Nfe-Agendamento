using System.Net;
using Microsoft.AspNetCore.Http;

namespace NfeAgendamento.App.Security;

public sealed class LocalRequestSecurityMiddleware
{
    private static readonly HashSet<string> AllowedHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "127.0.0.1:17345",
        "localhost:17345"
    };

    private static readonly HashSet<string> AllowedOrigins = new(StringComparer.OrdinalIgnoreCase)
    {
        "http://127.0.0.1:17345",
        "http://localhost:17345"
    };

    private readonly RequestDelegate _next;
    private readonly CsrfTokenService _csrf;

    public LocalRequestSecurityMiddleware(RequestDelegate next, CsrfTokenService csrf)
    {
        _next = next ?? throw new ArgumentNullException(nameof(next));
        _csrf = csrf ?? throw new ArgumentNullException(nameof(csrf));
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var remoteIp = context.Connection.RemoteIpAddress;
        if (remoteIp is null || !IPAddress.IsLoopback(remoteIp))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        if (!AllowedHosts.Contains(context.Request.Host.Value))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        var origin = context.Request.Headers.Origin.ToString();
        if (!string.IsNullOrWhiteSpace(origin) && !AllowedOrigins.Contains(origin))
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

    private static bool IsMutating(string method) =>
        HttpMethods.IsPost(method)
        || HttpMethods.IsPut(method)
        || HttpMethods.IsPatch(method)
        || HttpMethods.IsDelete(method);
}
