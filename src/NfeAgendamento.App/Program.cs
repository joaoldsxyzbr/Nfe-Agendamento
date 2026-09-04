using System.Security.Cryptography;
using NfeAgendamento.App.Certificates;
using NfeAgendamento.App.Fiscal;
using NfeAgendamento.App.Portal;
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

        var appArgs = args
            .Where(argument => !string.Equals(argument, "--lan", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        var builder = WebApplication.CreateBuilder(appArgs);
        LocalHost.Configure(builder);
        builder.Services.AddSingleton(settingsStore);
        builder.Services.AddSingleton(centralState);
        builder.Services.AddSingleton<CsrfTokenService>();
        builder.Services.AddSingleton<CertificateService>();
        builder.Services.AddSingleton<SharedQueuePaths>();
        builder.Services.AddSingleton<CandidateStateStore>();
        builder.Services.AddSingleton<EncryptedXmlCache>(sp => new EncryptedXmlCache(
            sp.GetRequiredService<SharedQueuePaths>(),
            sp.GetRequiredService<CandidateStateStore>()));
        builder.Services.AddSingleton<PortalNfeFallbackLauncher>();
        builder.Services.AddSingleton<FiscalCooldownStore>();
        builder.Services.AddSingleton<FiscalOperationGate>();
        builder.Services.AddSingleton<FiscalRequestCoordinator>();
        builder.Services.AddSingleton<FiscalAuditLog>();
        builder.Services.AddSingleton<SharedGroupIdentityStore>();
        builder.Services.AddSingleton<CandidateBundleStore>();
        builder.Services.AddSingleton<CentralKeyStore>(sp => new CentralKeyStore(
            sp.GetRequiredService<CandidateStateStore>(),
            sp.GetRequiredService<SharedGroupIdentityStore>()));
        builder.Services.AddSingleton<PendingRequestSecretStore>();
        builder.Services.AddSingleton<ClientPairingStore>();
        builder.Services.AddSingleton<AuthorizedClientStore>();
        builder.Services.AddSingleton<SharedAuthorizedClientStore>();
        builder.Services.AddSingleton<SharedQueueGroupRotationStorage>();
        builder.Services.AddSingleton<SharedQueueGroupRotationService>();
        builder.Services.AddSingleton<SharedQueueGroupBootstrapService>();
        builder.Services.AddSingleton<PairingCodeService>();
        builder.Services.AddSingleton<SharedQueuePairingClient>();
        builder.Services.AddSingleton<SharedQueuePairingCoordinator>();
        builder.Services.AddSingleton<SharedQueueGroupPairingProcessor>();
        builder.Services.AddSingleton<SharedQueueClient>();
        builder.Services.AddSingleton<SharedQueueCentralService>();
        builder.Services.AddHostedService(sp => sp.GetRequiredService<SharedQueueCentralService>());
        builder.Services.AddSingleton<SharedQueueGroupProcessor>();
        builder.Services.AddHostedService<AutomaticSharedQueueProcessingHostedService>();
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
            SharedQueueGroupBootstrapService group,
            SharedQueueClient queueClient) =>
        {
            var clientStatus = queueClient.GetStatus();
            var portalFallbackAvailable = group.IsCandidateReady;
            return Results.Ok(new
            {
                csrfToken = csrf.CurrentToken,
                configuredAsCentral = state.IsConfiguredAsCentral,
                centralActive = central.IsActive,
                leaderStatus = central.Status.ToString().ToLowerInvariant(),
                candidateReady = group.IsCandidateReady,
                clientPaired = group.IsCandidateReady,
                portalFallbackAvailable,
                centralOnline = central.IsActive || clientStatus.CentralOnline,
                shareAvailable = central.ShareAvailable || clientStatus.ShareAvailable,
                centralId = central.IsActive ? Environment.MachineName : clientStatus.CentralId,
                sharedFolder = SharedQueuePaths.DefaultRoot
            });
        });

        app.MapPost("/api/pairing/code", (
            SharedQueueCentralService central,
            PairingCodeService pairingCodes) =>
        {
            if (!central.IsActive)
            {
                return Results.Json(
                    new { status = "leader_inactive", message = "Este PC não está processando a fila neste momento." },
                    statusCode: StatusCodes.Status409Conflict);
            }

            var info = pairingCodes.Generate();
            return Results.Ok(new { code = info.Code, expiresUtc = info.ExpiresUtc });
        });

        app.MapPost("/api/pairing/client", async (
            PairClientRequest? request,
            SharedQueuePairingCoordinator pairing,
            CancellationToken cancellationToken) =>
        {
            if (request is null || string.IsNullOrWhiteSpace(request.Code))
                return Results.BadRequest(new { status = "invalid_pairing_code", message = "Informe o código exibido pelo PC que está processando a fila." });

            var result = await pairing.PairAsync(request.Code, cancellationToken);
            return result.Success
                ? Results.Ok(new { status = "paired", message = result.Message })
                : Results.Json(new { status = "pairing_failed", message = result.Message }, statusCode: StatusCodes.Status409Conflict);
        });

        app.MapGet("/api/pairing/clients", (
            SharedQueueCentralService central,
            SharedAuthorizedClientStore clients) =>
        {
            if (!central.IsActive)
            {
                return Results.Json(
                    new { status = "leader_inactive", message = "Gerencie os PCs autorizados somente no líder atual da fila." },
                    statusCode: StatusCodes.Status409Conflict);
            }

            var snapshot = clients.Snapshot();
            try
            {
                var safeClients = snapshot
                    .Select(item => new
                    {
                        clientId = item.ClientId,
                        clientName = item.ClientName,
                        lastSequence = item.LastSequence,
                        isCurrent = string.Equals(item.ClientName, Environment.MachineName, StringComparison.OrdinalIgnoreCase)
                    })
                    .OrderByDescending(item => item.isCurrent)
                    .ThenBy(item => item.clientName, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                return Results.Ok(new { clients = safeClients });
            }
            finally
            {
                ZeroAuthorizedClients(snapshot);
            }
        });

        app.MapPost("/api/pairing/revoke", async (
            RevokeClientRequest? request,
            SharedQueueCentralService central,
            SharedQueueGroupRotationService rotation,
            CancellationToken cancellationToken) =>
        {
            if (!central.IsActive)
            {
                return Results.Json(
                    new { status = "leader_inactive", message = "Remova PCs autorizados somente no líder atual da fila." },
                    statusCode: StatusCodes.Status409Conflict);
            }
            if (request is null || request.ClientId == Guid.Empty)
                return Results.BadRequest(new { status = "invalid_client", message = "Selecione um PC autorizado válido." });

            var result = await rotation.RevokeAsync(request.ClientId, cancellationToken);
            return result.Success
                ? Results.Ok(new { status = "revoked", message = result.Message })
                : Results.Json(new { status = "revoke_failed", message = result.Message }, statusCode: StatusCodes.Status409Conflict);
        });

        app.MapGet("/api/certificates", (CertificateService certificates) =>
            Results.Ok(certificates.ListValidCertificates()));

        app.MapGet("/api/certificate/current", async (
            CertificateService certificates,
            CancellationToken cancellationToken) =>
        {
            var current = await certificates.GetCurrentAsync(cancellationToken);
            return current is null
                ? Results.NoContent()
                : Results.Ok(new { current.Thumbprint, current.Subject, current.NotAfter, ufAutor = certificates.GetCurrentAuthorityState() });
        });

        app.MapPost("/api/certificate/select", async (
            CertificateSelectRequest? request,
            CertificateService certificates,
            CancellationToken cancellationToken) =>
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

        app.MapGet("/api/nfe/cache/{accessKey}", async (
            string accessKey,
            EncryptedXmlCache cache,
            CancellationToken cancellationToken) =>
        {
            if (!AccessKeyValidator.IsValid(accessKey))
                return Results.BadRequest(new { status = "invalid_key", message = "Informe uma chave NF-e válida com 44 dígitos." });

            try
            {
                var cached = await cache.TryGetAsync(accessKey, cancellationToken);
                return cached is null
                    ? Results.Json(new { status = "cache_miss" }, statusCode: StatusCodes.Status404NotFound)
                    : Results.Ok(new { status = "cache_hit" });
            }
            catch (InvalidOperationException ex)
            {
                return Results.Json(new { status = "configuration_error", message = ex.Message }, statusCode: StatusCodes.Status409Conflict);
            }
        });

        app.MapPost("/api/nfe/portal-fallback", (
            LookupRequest? request,
            SharedQueueGroupBootstrapService group,
            PortalNfeFallbackLauncher launcher) =>
        {
            if (request is null || !AccessKeyValidator.IsValid(request.AccessKey))
                return Results.BadRequest(new { status = "invalid_key", message = "Informe uma chave NF-e válida com 44 dígitos." });

            if (!group.IsCandidateReady)
            {
                return Results.Json(
                    new { status = "portal_not_authorized", message = "Este PC ainda não está autorizado para usar o Portal da NF-e. Autorize-o no grupo e configure o certificado A1 localmente." },
                    statusCode: StatusCodes.Status409Conflict);
            }

            var result = launcher.TryLaunch(request.AccessKey);
            return result.Status switch
            {
                PortalFallbackLaunchStatus.Started => Results.Json(
                    new { status = "started", message = result.Message },
                    statusCode: StatusCodes.Status202Accepted),
                PortalFallbackLaunchStatus.Busy => Results.Json(
                    new { status = "portal_busy", message = result.Message },
                    statusCode: StatusCodes.Status409Conflict),
                PortalFallbackLaunchStatus.RuntimeMissing => Results.Json(
                    new { status = "webview2_missing", message = result.Message },
                    statusCode: StatusCodes.Status409Conflict),
                _ => Results.Json(
                    new { status = "configuration_error", message = result.Message },
                    statusCode: StatusCodes.Status409Conflict)
            };
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
            new { status = "fila_ocupada", message = result.Message ?? "A fila está processando muitas consultas. Tente novamente em alguns segundos." },
            statusCode: StatusCodes.Status429TooManyRequests);
        return new RetryAfterResult(response);
    }

    private static void ZeroAuthorizedClients(IEnumerable<AuthorizedClientSnapshot> clients)
    {
        foreach (var client in clients)
            if (client.Secret is not null)
                CryptographicOperations.ZeroMemory(client.Secret);
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
internal sealed record PairClientRequest(string Code);
internal sealed record RevokeClientRequest(Guid ClientId);
