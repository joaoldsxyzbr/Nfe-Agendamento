using NfeAgendamento.App.Certificates;
using NfeAgendamento.App.Fiscal;
using NfeAgendamento.App.Security;
using NfeAgendamento.App.Storage;

namespace NfeAgendamento.App;

internal static class Program
{
    [STAThread]
    private static async Task Main(string[] args)
    {
        ApplicationConfiguration.Initialize();

        var builder = WebApplication.CreateBuilder(args);
        LocalHost.Configure(builder, args);
        builder.Services.AddSingleton<CsrfTokenService>();
        builder.Services.AddSingleton<LocalSessionService>();
        builder.Services.AddSingleton<CertificateService>();
        builder.Services.AddSingleton<EncryptedXmlCache>();
        builder.Services.AddSingleton<FiscalCooldownStore>();
        builder.Services.AddSingleton<FiscalOperationGate>();
        builder.Services.AddSingleton<FiscalRequestCoordinator>();
        builder.Services.AddScoped<NfeLookupService>();
        builder.Services.AddScoped<INfeDistributionTransport>(sp =>
        {
            var certificates = sp.GetRequiredService<CertificateService>();
            var current = certificates.GetCurrentSelectionWithCertificate();
            var ufAutor = certificates.GetCurrentAuthorityState();
            var identity = CertificateIdentityReader.Read(current.Certificate, ufAutor);
            return new NfeDistributionTransport(current.Certificate, identity.Cnpj, identity.UfAutor);
        });

        var app = builder.Build();
        app.UseMiddleware<LocalRequestSecurityMiddleware>();
        app.UseDefaultFiles();
        app.UseStaticFiles();

        app.MapGet("/api/bootstrap", (CsrfTokenService csrf) =>
            Results.Ok(new { csrfToken = csrf.CurrentToken, lanMode = LocalHost.IsLanMode, accessUrl = LocalHost.GetBrowserUrl(args) }));
        app.MapGet("/api/auth/status", (HttpContext context, LocalSessionService sessions) =>
            Results.Ok(new { configured = sessions.IsConfigured, authenticated = sessions.IsAuthenticated(context.Request.Cookies[LocalSessionService.CookieName]) }));
        app.MapPost("/api/auth/setup", (AuthRequest? request, HttpContext context, LocalSessionService sessions) =>
        {
            if (context.Connection.RemoteIpAddress is null || !System.Net.IPAddress.IsLoopback(context.Connection.RemoteIpAddress))
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            if (request is null)
                return Results.BadRequest(new { message = "Informe a senha numérica." });
            try
            {
                sessions.Configure(request.Password);
                return Results.NoContent();
            }
            catch (ArgumentException ex) { return Results.BadRequest(new { message = ex.Message }); }
            catch (InvalidOperationException ex) { return Results.Conflict(new { message = ex.Message }); }
        });
        app.MapPost("/api/auth/login", (AuthRequest? request, HttpContext context, LocalSessionService sessions) =>
        {
            if (request is null || !sessions.Verify(request.Password))
                return Results.Json(new { message = "Senha inválida." }, statusCode: StatusCodes.Status401Unauthorized);
            var token = sessions.CreateSession();
            context.Response.Cookies.Append(LocalSessionService.CookieName, token, new CookieOptions
            {
                HttpOnly = true,
                SameSite = SameSiteMode.Lax,
                Secure = false,
                MaxAge = TimeSpan.FromHours(8),
                Path = "/"
            });
            return Results.NoContent();
        });
        app.MapPost("/api/auth/logout", (HttpContext context, LocalSessionService sessions) =>
        {
            sessions.Revoke(context.Request.Cookies[LocalSessionService.CookieName]);
            context.Response.Cookies.Delete(LocalSessionService.CookieName);
            return Results.NoContent();
        });
        app.MapGet("/api/certificates", (CertificateService certificates) =>
            Results.Ok(certificates.ListValidCertificates()));
        app.MapGet("/api/certificate/current", async (CertificateService certificates, CancellationToken cancellationToken) =>
        {
            var current = await certificates.GetCurrentAsync(cancellationToken);
            return current is null
                ? Results.NoContent()
                : Results.Ok(new { current.Thumbprint, current.Subject, current.NotAfter, ufAutor = certificates.GetCurrentAuthorityState() });
        });
        app.MapPost("/api/certificate/select", async (CertificateSelectRequest? request, CertificateService certificates, CancellationToken cancellationToken) =>
        {
            if (request is null || string.IsNullOrWhiteSpace(request.Thumbprint) || string.IsNullOrWhiteSpace(request.UfAutor))
                return Results.BadRequest(new { status = "invalid_certificate", message = "Selecione o certificado e informe a UF autora." });
            try
            {
                await certificates.SelectAsync(request.Thumbprint, request.UfAutor, cancellationToken);
                return Results.NoContent();
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
            {
                return Results.BadRequest(new { status = "invalid_certificate", message = ex.Message });
            }
        });
        app.MapPost("/api/nfe/lookup", async (LookupRequest? request, HttpContext context, CancellationToken cancellationToken) =>
        {
            if (request is null || !AccessKeyValidator.IsValid(request.AccessKey))
                return Results.BadRequest(new { status = "invalid_key", message = "Informe uma chave NF-e válida com 44 dígitos." });
            try
            {
                var lookup = context.RequestServices.GetRequiredService<NfeLookupService>();
                var result = await lookup.LookupAsync(request.AccessKey, cancellationToken);
                return result.Status switch
                {
                    NfeLookupStatus.Found => Results.Text(result.Xml!, "application/xml; charset=utf-8"),
                    NfeLookupStatus.NotFound => Results.Json(new { status = "not_found", message = result.Message, cStat = result.CStat }, statusCode: 404),
                    NfeLookupStatus.ManifestationRequired => Results.Json(new { status = "manifestation_required", message = result.Message, cStat = result.CStat }, statusCode: 409),
                    NfeLookupStatus.Blocked => BlockedResult(result),
                    _ => Results.Json(new { status = "network_error", message = result.Message ?? "Não foi possível concluir a consulta." }, statusCode: 502)
                };
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { status = "invalid_key", message = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return Results.Json(new { status = "configuration_error", message = ex.Message }, statusCode: 409);
            }
        });

        try
        {
            await app.StartAsync();
        }
        catch (IOException)
        {
            MessageBox.Show(
                "A porta local 17345 já está em uso. Feche a outra instância do NFe Agendamento ou o programa que está usando essa porta.",
                "NFe Agendamento",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return;
        }

        var browserUrl = LocalHost.GetBrowserUrl(args);
        using var networkName = LocalHost.IsLanMode ? NetworkNameService.Start() : null;
        OpenBrowser(browserUrl);
        Application.Run(new TrayApplicationContext(browserUrl));
        await app.StopAsync();
    }

    private static void OpenBrowser(string url)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch
        {
        }
    }

    private static IResult BlockedResult(NfeLookupResult result)
    {
        var response = Results.Json(new { status = "consumo_indevido", message = result.Message, cStat = result.CStat, blockedUntilUtc = result.BlockedUntilUtc }, statusCode: StatusCodes.Status429TooManyRequests);
        return new HeaderResult(response, result.BlockedUntilUtc);
    }

    private sealed class HeaderResult(IResult inner, DateTimeOffset? blockedUntilUtc) : IResult
    {
        public async Task ExecuteAsync(HttpContext httpContext)
        {
            if (blockedUntilUtc is { } until)
            {
                var seconds = Math.Max(1, (int)Math.Ceiling((until - DateTimeOffset.UtcNow).TotalSeconds));
                httpContext.Response.Headers.RetryAfter = seconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
            await inner.ExecuteAsync(httpContext);
        }
    }
}

internal sealed record CertificateSelectRequest(string Thumbprint, string UfAutor);
internal sealed record LookupRequest(string AccessKey);
internal sealed record AuthRequest(string Password);
