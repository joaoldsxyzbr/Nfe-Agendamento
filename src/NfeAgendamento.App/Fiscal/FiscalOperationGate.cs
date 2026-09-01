namespace NfeAgendamento.App.Fiscal;

public sealed class FiscalOperationGate
{
    public const int DefaultMaxPendingOperations = 12;

    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private readonly int _maxPendingOperations;
    private int _pendingOperations;

    public FiscalOperationGate(int maxPendingOperations = DefaultMaxPendingOperations)
    {
        if (maxPendingOperations < 1)
            throw new ArgumentOutOfRangeException(nameof(maxPendingOperations));

        _maxPendingOperations = maxPendingOperations;
    }

    public int PendingOperations => Volatile.Read(ref _pendingOperations);
    public int MaxPendingOperations => _maxPendingOperations;

    public async Task<FiscalOperationLease> EnterAsync(CancellationToken cancellationToken = default)
    {
        if (!TryReserve())
            throw new FiscalQueueFullException(_maxPendingOperations);

        try
        {
            await _semaphore.WaitAsync(cancellationToken);
            return new FiscalOperationLease(this);
        }
        catch
        {
            ReleaseReservation();
            throw;
        }
    }

    private bool TryReserve()
    {
        while (true)
        {
            var current = Volatile.Read(ref _pendingOperations);
            if (current >= _maxPendingOperations)
                return false;

            if (Interlocked.CompareExchange(ref _pendingOperations, current + 1, current) == current)
                return true;
        }
    }

    internal void Exit()
    {
        _semaphore.Release();
        ReleaseReservation();
    }

    private void ReleaseReservation() => Interlocked.Decrement(ref _pendingOperations);
}

public sealed class FiscalOperationLease : IDisposable
{
    private FiscalOperationGate? _gate;

    internal FiscalOperationLease(FiscalOperationGate gate)
    {
        _gate = gate;
    }

    public void Dispose()
    {
        Interlocked.Exchange(ref _gate, null)?.Exit();
    }
}

public sealed class FiscalQueueFullException : InvalidOperationException
{
    public FiscalQueueFullException(int capacity)
        : base($"A fila fiscal atingiu o limite de {capacity} operações. Tente novamente em alguns segundos.")
    {
        Capacity = capacity;
    }

    public int Capacity { get; }
}
