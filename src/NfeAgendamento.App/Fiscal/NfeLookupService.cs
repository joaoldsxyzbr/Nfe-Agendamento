using System.Net.Http;
using NfeAgendamento.App.Storage;

namespace NfeAgendamento.App.Fiscal;

public enum NfeLookupStatus
{
    Found,
    NotFound,
    ManifestationRequired,
    Blocked,
    Busy,
    Failed
}

public sealed record NfeLookupResult(
    NfeLookupStatus Status,
    string? Xml,
    string? CStat,
    string? Message,
    bool FromCache,
    DateTimeOffset? BlockedUntilUtc = null);

public sealed record NfeDistributionResponse(string CStat, string Message, string? Xml);

public interface INfeDistributionTransport
{
    Task<NfeDistributionResponse> QueryByAccessKeyAsync(
        string accessKey,
        CancellationToken cancellationToken = default);
}

public sealed class NfeLookupService
{
    private readonly INfeDistributionTransport _transport;
    private readonly EncryptedXmlCache _cache;
    private readonly FiscalCooldownStore _cooldown;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly FiscalOperationGate _gate;
    private readonly FiscalRequestCoordinator _coordinator;

    public NfeLookupService(
        INfeDistributionTransport transport,
        EncryptedXmlCache cache,
        FiscalCooldownStore cooldown,
        Func<TimeSpan, CancellationToken, Task>? delay = null,
        FiscalOperationGate? gate = null,
        FiscalRequestCoordinator? coordinator = null)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _cooldown = cooldown ?? throw new ArgumentNullException(nameof(cooldown));
        _delay = delay ?? Task.Delay;
        _gate = gate ?? new FiscalOperationGate();
        _coordinator = coordinator ?? new FiscalRequestCoordinator();
    }

    public async Task<NfeLookupResult> LookupAsync(
        string accessKey,
        CancellationToken cancellationToken = default)
    {
        if (!AccessKeyValidator.IsValid(accessKey))
            throw new ArgumentException("Chave NF-e inválida.", nameof(accessKey));

        return await _coordinator.ExecuteAsync(
            accessKey,
            () => LookupCoreAsync(accessKey),
            cancellationToken);
    }

    private async Task<NfeLookupResult> LookupCoreAsync(string accessKey)
    {
        var cancellationToken = CancellationToken.None;

        var cached = await _cache.TryGetAsync(accessKey, cancellationToken);
        if (cached is not null)
            return new NfeLookupResult(NfeLookupStatus.Found, cached.Xml, "CACHE", "Documento obtido do cache local.", true);

        FiscalOperationLease lease;
        try
        {
            lease = await _gate.EnterAsync(cancellationToken);
        }
        catch (FiscalQueueFullException ex)
        {
            return new NfeLookupResult(NfeLookupStatus.Busy, null, null, ex.Message, false);
        }

        using (lease)
        {
            cached = await _cache.TryGetAsync(accessKey, cancellationToken);
            if (cached is not null)
                return new NfeLookupResult(NfeLookupStatus.Found, cached.Xml, "CACHE", "Documento obtido do cache local.", true);

            try
            {
                await _cooldown.EnsureAllowedAsync(cancellationToken);
            }
            catch (FiscalCooldownException blocked)
            {
                return new NfeLookupResult(NfeLookupStatus.Blocked, null, "656", blocked.Message, false, blocked.BlockedUntilUtc);
            }

            NfeDistributionResponse? response = null;
            for (var attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    response = await _transport.QueryByAccessKeyAsync(accessKey, cancellationToken);
                    break;
                }
                catch (HttpRequestException) when (attempt < 2)
                {
                    await _delay(attempt == 0 ? TimeSpan.FromSeconds(2) : TimeSpan.FromSeconds(5), cancellationToken);
                }
                catch (HttpRequestException)
                {
                    return new NfeLookupResult(NfeLookupStatus.Failed, null, null, "Não foi possível comunicar com a SEFAZ após novas tentativas.", false);
                }
                catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested && attempt < 2)
                {
                    await _delay(attempt == 0 ? TimeSpan.FromSeconds(2) : TimeSpan.FromSeconds(5), cancellationToken);
                }
                catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    return new NfeLookupResult(NfeLookupStatus.Failed, null, null, "A consulta à SEFAZ excedeu o tempo limite.", false);
                }
                catch (InvalidDataException)
                {
                    return new NfeLookupResult(NfeLookupStatus.Failed, null, null, "A resposta recebida da SEFAZ não pôde ser validada com segurança.", false);
                }
            }

            if (response is null)
                return new NfeLookupResult(NfeLookupStatus.Failed, null, null, "Não foi possível comunicar com a SEFAZ após novas tentativas.", false);

            try
            {
                if (string.Equals(response.CStat, "656", StringComparison.Ordinal))
                {
                    await _cooldown.BlockFor656Async(DateTimeOffset.UtcNow, cancellationToken);
                    var state = await _cooldown.ReadAsync(cancellationToken);
                    return new NfeLookupResult(NfeLookupStatus.Blocked, null, response.CStat, response.Message, false, state.BlockedUntilUtc);
                }

                if (string.Equals(response.CStat, "137", StringComparison.Ordinal))
                    return new NfeLookupResult(NfeLookupStatus.NotFound, null, response.CStat, response.Message, false);

                if (string.Equals(response.CStat, "138", StringComparison.Ordinal) && !string.IsNullOrWhiteSpace(response.Xml))
                {
                    await _cache.PutAsync(accessKey, response.Xml, cancellationToken);
                    return new NfeLookupResult(NfeLookupStatus.Found, response.Xml, response.CStat, response.Message, false);
                }

                if (string.Equals(response.CStat, "138", StringComparison.Ordinal))
                    return new NfeLookupResult(NfeLookupStatus.ManifestationRequired, null, response.CStat, "A NF-e foi localizada, mas a SEFAZ não disponibilizou o XML completo nesta consulta.", false);

                return new NfeLookupResult(NfeLookupStatus.Failed, null, response.CStat, response.Message, false);
            }
            catch (InvalidDataException)
            {
                return new NfeLookupResult(NfeLookupStatus.Failed, null, null, "A resposta recebida da SEFAZ não pôde ser validada com segurança.", false);
            }
        }
    }
}
