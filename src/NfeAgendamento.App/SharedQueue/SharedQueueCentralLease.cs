namespace NfeAgendamento.App.SharedQueue;

public sealed class SharedQueueCentralLease : IDisposable
{
    private FileStream? _stream;

    private SharedQueueCentralLease(FileStream stream)
    {
        _stream = stream;
    }

    public static SharedQueueCentralLease? TryAcquire(SharedQueuePaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        if (!paths.ValidateForClient())
            return null;

        try
        {
            var stream = new FileStream(
                paths.StatusPath("central.lock"),
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.None,
                bufferSize: 1,
                FileOptions.WriteThrough);

            stream.SetLength(0);
            using var writer = new StreamWriter(stream, leaveOpen: true);
            writer.Write($"{Environment.MachineName}|{Environment.ProcessId}|{DateTimeOffset.UtcNow:O}");
            writer.Flush();
            stream.Flush(flushToDisk: true);
            stream.Position = 0;
            return new SharedQueueCentralLease(stream);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public void Dispose()
    {
        var stream = Interlocked.Exchange(ref _stream, null);
        stream?.Dispose();
    }
}
