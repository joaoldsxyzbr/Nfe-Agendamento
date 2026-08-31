using NfeAgendamento.App.Certificates;
using NfeAgendamento.App.Security;

namespace NfeAgendamento.App;

internal static class Program
{
    [STAThread]
    private static async Task Main(string[] args)
    {
        ApplicationConfiguration.Initialize();

        var builder = WebApplication.CreateBuilder(args);
        LocalHost.Configure(builder);
        builder.Services.AddSingleton<CsrfTokenService>();
        builder.Services.AddSingleton<CertificateService>();

        var app = builder.Build();
        app.UseMiddleware<LocalRequestSecurityMiddleware>();
        app.UseDefaultFiles();
        app.UseStaticFiles();

        app.MapGet("/api/bootstrap", (CsrfTokenService csrf) =>
            Results.Ok(new { csrfToken = csrf.CurrentToken }));

        app.MapGet("/api/certificates", (CertificateService certificates) =>
            Results.Ok(certificates.ListValidCertificates()));

        app.MapGet("/api/certificate/current", async (
            CertificateService certificates,
            CancellationToken cancellationToken) =>
        {
            var current = await certificates.GetCurrentAsync(cancellationToken);
            return current is null ? Results.NoContent() : Results.Ok(current);
        });

        app.MapPost("/api/certificate/select", async (
            CertificateSelectRequest request,
            CertificateService certificates,
            CancellationToken cancellationToken) =>
        {
            try
            {
                await certificates.SelectAsync(request.Thumbprint, cancellationToken);
                return Results.NoContent();
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
            {
                return Results.BadRequest(new
                {
                    status = "invalid_certificate",
                    message = ex.Message
                });
            }
        });

        await app.RunAsync();
    }
}

internal sealed record CertificateSelectRequest(string Thumbprint);
