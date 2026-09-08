using NfeAgendamento.App.SharedQueue;

namespace NfeAgendamento.App.Fiscal;

public sealed class FiscalOperationGate
{
    public const int DefaultMaxPendingOperations = 12;
    private static readonly TimeSpan SharedLockRetryDelay = TimeSpan.FromMilliseconds(100);

    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private readonly int _maxPendingOperations;
    private readonly SharedQueuePaths? _sharedPaths;
    private int _pendingOperations;

    public FiscalOperationGate(int maxPendingOperations = DefaultMaxPendingOperations)
    {
        ValidateCapacity(maxPendingOperations);
        _maxPendingOperations = maxPendingOperations;
    }

    public FiscalOperationGate(
        SharedQueuePaths paths,
        int maxPendingOperations = DefaultMaxPendingOperations)
    {
        _sharedPaths = paths ?? throw new ArgumentNullException(nameof(paths));
        ValidateCapacity(maxPendingOperations);
        _maxPendingOperations = maxPendingOperations;
    }

    public int PendingOperations => Volatile.Read(ref _pendingOperations);
    public int MaxPendingOperations => _maxPendingOperations;

    public async Task<FiscalOperationLease> EnterAsync(CancellationToken cancellationToken = default)
    {
        if (!TryReserve())
            throw new FiscalQueueFullException(_maxPendingOperations);

        var localEntered = false;
        FileStream? sharedLease = null;
        try
        {
            await _semaphore.WaitAsync(cancellationToken);
            localEntered = true;

            if (_sharedPaths is not null)
                sharedLease = await AcquireSharedLeaseAsync(_sharedPaths, cancellationToken);

            return new FiscalOperationLease(this, sharedLease);
        }
        catch
        {
            sharedLease?.Dispose();
            if (localEntered)
                _semaphore.Release();
            ReleaseReservation();
            throw;
        }
    }

    private static async Task<FileStream> AcquireSharedLeaseAsync(
        SharedQueuePaths paths,
        CancellationToken cancellationToken)
    {
        if (!paths.ValidateForClient())
            throw new FiscalQueueUnavailableException(paths.Root);

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                SharedQueueFileIO.EnsureNotReparsePoint(paths.StatusDirectory);
                if (File.Exists(paths.FiscalLockPath))
                    SharedQueueFileIO.EnsureNotReparsePoint(paths.FiscalLockPath);

                return new FileStream(
                    paths.FiscalLockPath,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None,
                    bufferSize: 1,
                    FileOptions.WriteThrough);
            }
            catch (IOException)
            {
                if (!paths.ValidateForClient())
                    throw new FiscalQueueUnavailableException(paths.Root);

                await Task.Delay(SharedLockRetryDelay, cancellationToken);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException
                or DirectoryNotFoundException
                or InvalidDataException
                or NotSupportedException)
            {
                throw new FiscalQueueUnavailableException(paths.Root, ex);
            }
        }
    }

    private static void ValidateCapacity(int maxPendingOperations)
    {
        if (maxPendingOperations < 1)
            throw new ArgumentOutOfRangeException(nameof(maxPendingOperations));
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
    private IDisposable? _sharedLease;

    internal FiscalOperationLease(FiscalOperationGate gate, IDisposable? sharedLease = null)
    {
        _gate = gate;
        _sharedLease = sharedLease;
    }

    public void Dispose()
    {
        Interlocked.Exchange(ref _sharedLease, null)?.Dispose();
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

public sealed class FiscalQueueUnavailableException : InvalidOperationException
{
    public FiscalQueueUnavailableException(string sharedRoot, Exception? innerException = null)
        : base($"A pasta compartilhada '{sharedRoot}' não está disponível. Nenhuma consulta fiscal foi iniciada.", innerException)
    {
        SharedRoot = sharedRoot;
    }

    public string SharedRoot { get; }
}
