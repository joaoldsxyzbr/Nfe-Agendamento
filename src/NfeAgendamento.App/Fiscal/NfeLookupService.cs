using System.Net.Http;
using NfeAgendamento.App.Storage;

namespace NfeAgendamento.App.Fiscal;

public enum NfeLookupStatus
{
    Found,
    NotFound,
    ManifestationRequired,
    Blocked,
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
    private readonly SemaphoreSlim _fiscalOperation = new(1, 1);

    public NfeLookupService(
        INfeDistributionTransport transport,
        EncryptedXmlCache cache,
        FiscalCooldownStore cooldown)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _cooldown = cooldown ?? throw new ArgumentNullException(nameof(cooldown));
    }

    public async Task<NfeLookupResult> LookupAsync(
        string accessKey,
        CancellationToken cancellationToken = default)
    {
        if (!AccessKeyValidator.IsValid(accessKey))
            throw new ArgumentException("Chave NF-e inválida.", nameof(accessKey));

        var cached = await _cache.TryGetAsync(accessKey, cancellationToken);
        if (cached is not null)
            return new NfeLookupResult(NfeLookupStatus.Found, cached.Xml, "CACHE", "Documento obtido do cache local.", true);

        await _fiscalOperation.WaitAsync(cancellationToken);
        try
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

            NfeDistributionResponse response;
            try
            {
                response = await _transport.QueryByAccessKeyAsync(accessKey, cancellationToken);
            }
            catch (HttpRequestException ex)
            {
                return new NfeLookupResult(NfeLookupStatus.Failed, null, null, "Falha temporária de comunicação com a SEFAZ.", false);
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return new NfeLookupResult(NfeLookupStatus.Failed, null, null, "A consulta à SEFAZ excedeu o tempo limite.", false);
            }

            if (string.Equals(response.CStat, "656", StringComparison.Ordinal))
            {
                var receivedAt = DateTimeOffset.UtcNow;
                await _cooldown.BlockFor656Async(receivedAt, cancellationToken);
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
        finally
        {
            _fiscalOperation.Release();
        }
    }
}
