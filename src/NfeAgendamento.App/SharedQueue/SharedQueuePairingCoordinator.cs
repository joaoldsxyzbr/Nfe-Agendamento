namespace NfeAgendamento.App.SharedQueue;

public sealed class SharedQueuePairingCoordinator
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly SharedQueuePairingClient _pairingClient;
    private readonly SharedQueueGroupBootstrapService _groupBootstrap;
    private readonly ClientPairingStore _pairingStore;
    private readonly TimeSpan _importTimeout;
    private readonly TimeSpan _pollInterval;

    public SharedQueuePairingCoordinator(
        SharedQueuePairingClient pairingClient,
        SharedQueueGroupBootstrapService groupBootstrap,
        ClientPairingStore pairingStore)
        : this(
            pairingClient,
            groupBootstrap,
            pairingStore,
            TimeSpan.FromSeconds(5),
            TimeSpan.FromMilliseconds(100))
    {
    }

    public SharedQueuePairingCoordinator(
        SharedQueuePairingClient pairingClient,
        SharedQueueGroupBootstrapService groupBootstrap,
        ClientPairingStore pairingStore,
        TimeSpan importTimeout,
        TimeSpan pollInterval)
    {
        _pairingClient = pairingClient ?? throw new ArgumentNullException(nameof(pairingClient));
        _groupBootstrap = groupBootstrap ?? throw new ArgumentNullException(nameof(groupBootstrap));
        _pairingStore = pairingStore ?? throw new ArgumentNullException(nameof(pairingStore));
        _importTimeout = importTimeout > TimeSpan.Zero
            ? importTimeout
            : throw new ArgumentOutOfRangeException(nameof(importTimeout));
        _pollInterval = pollInterval > TimeSpan.Zero
            ? pollInterval
            : throw new ArgumentOutOfRangeException(nameof(pollInterval));
    }

    public async Task<PairingResult> PairAsync(string code, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_groupBootstrap.IsCandidateReady)
                return new PairingResult(true, "Este PC já está autorizado na fila.");

            if (_pairingStore.IsPaired)
            {
                if (_groupBootstrap.TryImportCandidateBundle())
                    return new PairingResult(true, "PC autorizado na fila com sucesso.");

                _pairingStore.Clear();
            }

            var result = await _pairingClient.PairAsync(code, cancellationToken);
            if (!result.Success)
                return result;

            var deadline = DateTimeOffset.UtcNow + _importTimeout;
            while (DateTimeOffset.UtcNow < deadline)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (_groupBootstrap.TryImportCandidateBundle())
                    return new PairingResult(true, "PC autorizado na fila com sucesso.");

                await Task.Delay(_pollInterval, cancellationToken);
            }

            _pairingStore.Clear();
            return new PairingResult(
                false,
                "A autorização foi recebida, mas a validação segura do grupo não foi concluída. O estado parcial foi descartado; gere um novo código no líder atual e tente novamente.");
        }
        finally
        {
            _gate.Release();
        }
    }
}
