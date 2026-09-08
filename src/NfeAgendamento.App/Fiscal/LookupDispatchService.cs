namespace NfeAgendamento.App.Fiscal;

public sealed class LookupDispatchService
{
    private readonly NfeLookupService _lookup;

    public LookupDispatchService(NfeLookupService lookup)
    {
        _lookup = lookup ?? throw new ArgumentNullException(nameof(lookup));
    }

    public Task<NfeLookupResult> LookupAsync(string accessKey, CancellationToken cancellationToken = default)
    {
        if (!AccessKeyValidator.IsValid(accessKey))
            throw new ArgumentException("Chave NF-e inválida.", nameof(accessKey));

        return _lookup.LookupAsync(accessKey, cancellationToken);
    }
}
