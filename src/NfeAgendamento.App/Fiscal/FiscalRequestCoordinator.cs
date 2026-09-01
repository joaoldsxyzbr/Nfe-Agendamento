using System.Collections.Concurrent;

namespace NfeAgendamento.App.Fiscal;

public sealed class FiscalRequestCoordinator
{
    private readonly ConcurrentDictionary<string, Lazy<Task<NfeLookupResult>>> _active = new(StringComparer.Ordinal);

    public async Task<NfeLookupResult> ExecuteAsync(
        string accessKey,
        Func<Task<NfeLookupResult>> operation,
        CancellationToken cancellationToken = default)
    {
        var lazy = _active.GetOrAdd(
            accessKey,
            _ => new Lazy<Task<NfeLookupResult>>(operation, LazyThreadSafetyMode.ExecutionAndPublication));
        var sharedTask = lazy.Value;
        _ = sharedTask.ContinueWith(
            _ => _active.TryRemove(new KeyValuePair<string, Lazy<Task<NfeLookupResult>>>(accessKey, lazy)),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

        return await sharedTask.WaitAsync(cancellationToken);
    }
}
