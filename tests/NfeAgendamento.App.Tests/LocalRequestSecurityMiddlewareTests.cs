using System.Net;
using Microsoft.AspNetCore.Http;
using NfeAgendamento.App.Security;
using Xunit;

namespace NfeAgendamento.App.Tests;

public sealed class LocalRequestSecurityMiddlewareTests
{
    [Theory]
    [InlineData("evil.example", "http://127.0.0.1:17345", 403)]
    [InlineData("127.0.0.1:17345", "https://evil.example", 403)]
    [InlineData("127.0.0.1:17345", "http://127.0.0.1:17345", 200)]
    [InlineData("localhost:17345", "http://localhost:17345", 200)]
    public async Task Security_policy_rejects_unexpected_host_or_origin(
        string host,
        string origin,
        int expectedStatus)
    {
        var csrf = new CsrfTokenService();
        var middleware = CreateMiddleware(csrf, centralEnabled: true);

        var context = CreateContext(HttpMethods.Get, host, origin);
        await middleware.InvokeAsync(context);

        Assert.Equal(expectedStatus, context.Response.StatusCode);
    }

    [Fact]
    public async Task Security_policy_rejects_non_loopback_when_central_is_stopped()
    {
        var csrf = new CsrfTokenService();
        var middleware = CreateMiddleware(csrf, centralEnabled: false);
        var context = CreateContext(HttpMethods.Get, "10.0.0.29:17345", "http://10.0.0.29:17345");
        context.Connection.RemoteIpAddress = IPAddress.Parse("10.0.0.30");

        await middleware.InvokeAsync(context);

        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
    }

    [Fact]
    public async Task Security_policy_allows_non_loopback_when_central_is_active()
    {
        var csrf = new CsrfTokenService();
        var middleware = CreateMiddleware(csrf, centralEnabled: true);
        var context = CreateContext(HttpMethods.Get, "10.0.0.29:17345", "http://10.0.0.29:17345");
        context.Connection.RemoteIpAddress = IPAddress.Parse("10.0.0.30");

        await middleware.InvokeAsync(context);

        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
    }

    [Theory]
    [InlineData(HttpMethods.Get, "/api/certificates")]
    [InlineData(HttpMethods.Get, "/api/certificate/current")]
    [InlineData(HttpMethods.Post, "/api/certificate/select")]
    public async Task Certificate_administration_is_rejected_for_remote_clients(string method, string path)
    {
        var csrf = new CsrfTokenService();
        var middleware = CreateMiddleware(csrf, centralEnabled: true);
        var context = CreateContext(method, "10.0.0.29:17345", "http://10.0.0.29:17345");
        context.Connection.RemoteIpAddress = IPAddress.Parse("10.0.0.30");
        context.Request.Path = path;
        if (HttpMethods.IsPost(method))
        {
            context.Request.ContentLength = 2;
            context.Request.Headers["X-CSRF-Token"] = csrf.CurrentToken;
        }

        await middleware.InvokeAsync(context);

        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
    }

    [Fact]
    public async Task Fiscal_lookup_remains_allowed_for_remote_clients_when_central_is_active()
    {
        var csrf = new CsrfTokenService();
        var middleware = CreateMiddleware(csrf, centralEnabled: true);
        var context = CreateContext(HttpMethods.Post, "10.0.0.29:17345", "http://10.0.0.29:17345");
        context.Connection.RemoteIpAddress = IPAddress.Parse("10.0.0.30");
        context.Request.Path = "/api/nfe/lookup";
        context.Request.ContentLength = 2;
        context.Request.Headers["X-CSRF-Token"] = csrf.CurrentToken;

        await middleware.InvokeAsync(context);

        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
    }

    [Fact]
    public async Task Post_without_csrf_is_rejected()
    {
        var csrf = new CsrfTokenService();
        var middleware = CreateMiddleware(csrf, centralEnabled: true);
        var context = CreateContext(HttpMethods.Post, "127.0.0.1:17345", "http://127.0.0.1:17345");
        context.Request.ContentType = "application/json";
        context.Request.ContentLength = 2;

        await middleware.InvokeAsync(context);

        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
    }

    [Fact]
    public async Task Post_with_valid_csrf_is_allowed()
    {
        var csrf = new CsrfTokenService();
        var middleware = CreateMiddleware(csrf, centralEnabled: true);
        var context = CreateContext(HttpMethods.Post, "127.0.0.1:17345", "http://127.0.0.1:17345");
        context.Request.ContentType = "application/json";
        context.Request.ContentLength = 2;
        context.Request.Headers["X-CSRF-Token"] = csrf.CurrentToken;

        await middleware.InvokeAsync(context);

        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
    }

    [Fact]
    public async Task Oversized_json_post_is_rejected()
    {
        var csrf = new CsrfTokenService();
        var middleware = CreateMiddleware(csrf, centralEnabled: true);
        var context = CreateContext(HttpMethods.Post, "127.0.0.1:17345", "http://127.0.0.1:17345");
        context.Request.ContentType = "application/json";
        context.Request.ContentLength = LocalHost.MaxRequestBodyBytes + 1;
        context.Request.Headers["X-CSRF-Token"] = csrf.CurrentToken;

        await middleware.InvokeAsync(context);

        Assert.Equal(StatusCodes.Status413PayloadTooLarge, context.Response.StatusCode);
    }

    private static LocalRequestSecurityMiddleware CreateMiddleware(CsrfTokenService csrf, bool centralEnabled)
    {
        var path = Path.Combine(Path.GetTempPath(), $"nfe-security-{Guid.NewGuid():N}", "central.json");
        var store = new CentralSettingsStore(path);
        var state = new CentralStateService(store);
        state.SetEnabled(centralEnabled);

        return new LocalRequestSecurityMiddleware(
            next: context =>
            {
                context.Response.StatusCode = StatusCodes.Status200OK;
                return Task.CompletedTask;
            },
            csrf,
            state);
    }

    private static DefaultHttpContext CreateContext(string method, string host, string? origin)
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Loopback;
        context.Request.Method = method;
        context.Request.Host = HostString.FromUriComponent(host);
        if (!string.IsNullOrWhiteSpace(origin))
            context.Request.Headers.Origin = origin;
        return context;
    }
}
