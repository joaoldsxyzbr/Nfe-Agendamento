using System.Net;
using Microsoft.AspNetCore.Http;
using NfeAgendamento.App.Certificates;
using NfeAgendamento.App.Security;
using NfeAgendamento.App.Storage;
using Xunit;

namespace NfeAgendamento.App.Tests;

public sealed class AuditHardeningTests
{
    [Fact]
    public async Task Expired_cache_entries_are_purged_without_being_looked_up_again()
    {
        using var temp = new TemporaryDirectory();
        var clock = new TestTimeProvider(DateTimeOffset.Parse("2026-09-04T10:00:00Z"));
        var cache = new EncryptedXmlCache(temp.Path, clock, TimeSpan.FromHours(24));

        await cache.PutAsync("old-entry", "<nfeProc />");
        var oldFile = Assert.Single(Directory.GetFiles(temp.Path, "*.bin"));

        clock.Advance(TimeSpan.FromHours(12));
        await cache.PutAsync("fresh-entry", "<nfeProc />");
        clock.Advance(TimeSpan.FromHours(13));

        var purge = typeof(EncryptedXmlCache).GetMethod(
            "PurgeExpiredAsync",
            new[] { typeof(CancellationToken) });
        Assert.NotNull(purge);

        var task = Assert.IsAssignableFrom<Task>(purge!.Invoke(cache, new object?[] { CancellationToken.None }));
        await task;

        Assert.False(File.Exists(oldFile));
        Assert.NotNull(await cache.TryGetAsync("fresh-entry"));
    }

    [Fact]
    public async Task Allowed_local_requests_receive_defensive_browser_headers()
    {
        var middleware = new LocalRequestSecurityMiddleware(
            next: context =>
            {
                context.Response.StatusCode = StatusCodes.Status200OK;
                return Task.CompletedTask;
            },
            new CsrfTokenService());
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Loopback;
        context.Request.Method = HttpMethods.Get;
        context.Request.Host = HostString.FromUriComponent("127.0.0.1:17345");
        context.Request.Headers.Origin = "http://127.0.0.1:17345";

        await middleware.InvokeAsync(context);

        Assert.Equal("nosniff", context.Response.Headers.XContentTypeOptions.ToString());
        Assert.Equal("DENY", context.Response.Headers.XFrameOptions.ToString());
        Assert.Equal("no-referrer", context.Response.Headers["Referrer-Policy"].ToString());
        Assert.Equal(
            "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; object-src 'none'; base-uri 'none'; frame-ancestors 'none'; form-action 'self'",
            context.Response.Headers.ContentSecurityPolicy.ToString());
    }

    [Fact]
    public void Combined_certificate_state_takes_precedence_over_legacy_uf_file()
    {
        using var temp = new TemporaryDirectory();
        var selectionPath = System.IO.Path.Combine(temp.Path, "certificate-thumbprint.txt");
        File.WriteAllText(selectionPath, "ABCDEF123456|42");
        File.WriteAllText(selectionPath + ".uf", "35");
        var service = new CertificateService(selectionPath);

        Assert.Equal("42", service.GetCurrentAuthorityState());
    }

    private sealed class TestTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan value) => _now = _now.Add(value);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "nfe-agendamento-audit-hardening",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); } catch { }
        }
    }
}
