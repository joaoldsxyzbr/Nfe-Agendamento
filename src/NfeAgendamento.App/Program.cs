using NfeAgendamento.App.Certificates;
using NfeAgendamento.App.Fiscal;
using NfeAgendamento.App.Security;
using NfeAgendamento.App.SharedQueue;
using NfeAgendamento.App.Storage;

namespace NfeAgendamento.App;

internal static class Program
{
    [STAThread]
    private static async Task Main(string[] args)
    {
        ApplicationConfiguration.Initialize();

        var settingsStore = new CentralSettingsStore();
        var centralState = new CentralStateService(settingsStore);

        if (StartupManager.IsEnabled())
            StartupManager.SetEnabled(true);

        // Aceita atalhos antigos sem voltar a expor o servidor na LAN.
        var appArgs = args
            .Where(argument => !string.Equals(argument, "--lan", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        var builder = WebApplication.CreateBuilder(appArgs);
        LocalHost.Configure(builder);
        builder.Services.AddSingleton(settingsStore);
        builder.Services.AddSingleton(centralState);
        builder.Services.AddSingleton<CsrfTokenService>();
        builder.Services.AddSingleton<CertificateService>();
        builder.Services.AddSingleton<EncryptedXmlCache>();
        builder.Services.AddSingleton<FiscalCooldownStore>();
        builder.Services.AddSingleton<FiscalOperationGate>();
        builder.Services.AddSingleton<FiscalRequestCoordinator>();
        builder.Services.AddSingleton<FiscalAuditLog>();
        builder.Services.AddSingleton<SharedQueuePaths>();
        builder.Services.AddSingleton<CentralKeyStore>();
        builder.Services.AddSingleton<PendingRequestSecretStore>();
        builder.Services.AddSingleton<SharedQueueClient>();
        builder.Services.AddSingleton<SharedQueueCentralService>();
        builder.Services.AddHostedService(sp => sp.GetRequiredService<SharedQueueCentralService>());
        builder.Services.AddSingleton<SharedQueueProcessor>();
        builder.Services.AddHostedService<SharedQueueProcessingHostedService>();
        builder.Services.AddScoped<NfeLookupService>();
        builder.Services.AddScoped<LookupDispatchService>();
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

        app.MapGet("/api/bootstrap", (
            CsrfTokenService csrf,
            CentralStateService state,
            SharedQueueCentralService central,
            SharedQueueClient queueClient) =>
        {
            var clientStatus = queueClient.GetStatus();
            return Results.Ok(new
            {
                csrfToken = csrf.CurrentToken,
                configuredAsCentral = state.IsConfiguredAsCentral,
                centralActive = central.IsActive,
                centralOnline = state.IsConfiguredAsCentral ? central.IsActive : clientStatus.CentralOnline,
                shareAvailable = state.IsConfiguredAsCentral ? central.ShareAvailable : clientStatus.ShareAvailable,
                centralId = state.IsConfiguredAsCentral ? Environment.MachineName : clientStatus.CentralId,
                sharedFolder = SharedQueuePaths.DefaultRoot
            });
        });

        app.MapGet("/api/certificates", (CentralStateService state, CertificateService certificates) =>
        {
            if (!state.IsConfiguredAsCentral)
                return Results.Json(new { status = "client_mode", message = "O certificado é administrado somente no PC Central." }, statusCode: StatusCodes.Status403Forbidden);
            return Results.Ok(certificates.ListValidCertificates());
        });

        app.MapGet("/api/certificate/current", async (
            CentralStateService state,
            CertificateService certificates,
            CancellationToken cancellationToken) =>
        {
            if (!state.IsConfiguredAsCentral)
                return Results.Json(new { status = "client_mode", message = "O certificado é administrado somente no PC Central." }, statusCode: StatusCodes.Status403Forbidden);

            var current = await certificates.GetCurrentAsync(cancellationToken);
            return current is null
                ? Results.NoContent()
                : Results.Ok(new { current.Thumbprint, current.Subject, current.NotAfter, ufAutor = certificates.GetCurrentAuthorityState() });
        });

        app.MapPost("/api/certificate/select", async (
            CertificateSelectRequest? request,
            CentralStateService state,
            CertificateService certificates,
            CancellationToken cancellationToken) =>
        {
            if (!state.IsConfiguredAsCentral)
                return Results.Json(new { status = "client_mode", message = "O certificado é administrado somente no PC Central." }, statusCode: StatusCodes.Status403Forbidden);
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
                var lookup = context.RequestServices.GetRequiredService<LookupDispatchService>();
                var result = await lookup.LookupAsync(request.AccessKey, cancellationToken);
                return result.Status switch
                {
                    NfeLookupStatus.Found => Results.Text(result.Xml!, "application/xml; charset=utf-8"),
                    NfeLookupStatus.NotFound => Results.Json(new { status = "not_found", message = result.Message, cStat = result.CStat }, statusCode: 404),
                    NfeLookupStatus.ManifestationRequired => Results.Json(new { status = "manifestation_required", message = result.Message, cStat = result.CStat }, statusCode: 409),
                    NfeLookupStatus.Blocked => BlockedResult(result),
                    NfeLookupStatus.Busy => BusyResult(result),
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

        var centralRuntime = app.Services.GetRequiredService<SharedQueueCentralService>();
        var queueClient = app.Services.GetRequiredService<SharedQueueClient>();
        Application.Run(new TrayApplicationContext(centralState, centralRuntime, queueClient));
        await app.StopAsync();
    }

    private static IResult BlockedResult(NfeLookupResult result)
    {
        var response = Results.Json(new { status = "consumo_indevido", message = result.Message, cStat = result.CStat, blockedUntilUtc = result.BlockedUntilUtc }, statusCode: StatusCodes.Status429TooManyRequests);
        return new HeaderResult(response, result.BlockedUntilUtc);
    }

    private static IResult BusyResult(NfeLookupResult result)
    {
        var response = Results.Json(
            new { status = "fila_ocupada", message = result.Message ?? "A Central está processando muitas consultas. Tente novamente em alguns segundos." },
            statusCode: StatusCodes.Status429TooManyRequests);
        return new RetryAfterResult(response);
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

    private sealed class RetryAfterResult(IResult inner) : IResult
    {
        public async Task ExecuteAsync(HttpContext httpContext)
        {
            httpContext.Response.Headers.RetryAfter = "5";
            await inner.ExecuteAsync(httpContext);
        }
    }
}

internal sealed record CertificateSelectRequest(string Thumbprint, string UfAutor);
internal sealed record LookupRequest(string AccessKey);
