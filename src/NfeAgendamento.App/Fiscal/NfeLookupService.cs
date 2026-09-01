using System.Diagnostics;
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
    private readonly FiscalAuditLog? _audit;

    public NfeLookupService(
        INfeDistributionTransport transport,
        EncryptedXmlCache cache,
        FiscalCooldownStore cooldown,
        Func<TimeSpan, CancellationToken, Task>? delay = null,
        FiscalOperationGate? gate = null,
        FiscalRequestCoordinator? coordinator = null,
        FiscalAuditLog? audit = null)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _cooldown = cooldown ?? throw new ArgumentNullException(nameof(cooldown));
        _delay = delay ?? Task.Delay;
        _gate = gate ?? new FiscalOperationGate();
        _coordinator = coordinator ?? new FiscalRequestCoordinator();
        _audit = audit;
    }

    public async Task<NfeLookupResult> LookupAsync(
        string accessKey,
        CancellationToken cancellationToken = default)
    {
        if (!AccessKeyValidator.IsValid(accessKey))
            throw new ArgumentException("Chave NF-e inválida.", nameof(accessKey));

        return await _coordinator.ExecuteAsync(
            accessKey,
            () => LookupAuditedAsync(accessKey),
            cancellationToken);
    }

    private async Task<NfeLookupResult> LookupAuditedAsync(string accessKey)
    {
        var started = Stopwatch.GetTimestamp();
        var result = await LookupCoreAsync(accessKey);

        if (_audit is not null)
            await _audit.RecordAsync(accessKey, result, Stopwatch.GetElapsedTime(started), CancellationToken.None);

        return result;
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
            catch (InvalidDataException)
            {
                return InvalidFiscalStateResult();
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
                    try
                    {
                        await _cooldown.BlockFor656Async(DateTimeOffset.UtcNow, cancellationToken);
                        var state = await _cooldown.ReadAsync(cancellationToken);
                        return new NfeLookupResult(NfeLookupStatus.Blocked, null, response.CStat, response.Message, false, state.BlockedUntilUtc);
                    }
                    catch (InvalidDataException)
                    {
                        return InvalidFiscalStateResult();
                    }
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

    private static NfeLookupResult InvalidFiscalStateResult() =>
        new(
            NfeLookupStatus.Failed,
            null,
            null,
            "O estado fiscal local não pôde ser validado. Nenhuma nova consulta foi enviada à SEFAZ.",
            false);
}
