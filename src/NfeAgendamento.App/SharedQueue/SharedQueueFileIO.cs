namespace NfeAgendamento.App.SharedQueue;

public static class SharedQueueFileIO
{
    public const int MaxMarkerBytes = 256;
    public const int MaxHeartbeatBytes = 32 * 1024;
    public const int MaxRequestBytes = 32 * 1024;
    public const int MaxResponseBytes = 16 * 1024 * 1024;
    public const int MaxPairingBytes = 32 * 1024;

    public static byte[] ReadAllBytes(string path, int maxBytes)
    {
        ValidateLimit(maxBytes);
        using var stream = OpenRead(path, maxBytes);
        var length = checked((int)stream.Length);
        var buffer = new byte[length];
        stream.ReadExactly(buffer);
        return buffer;
    }

    public static async Task<byte[]> ReadAllBytesAsync(
        string path,
        int maxBytes,
        CancellationToken cancellationToken = default)
    {
        ValidateLimit(maxBytes);
        await using var stream = OpenRead(path, maxBytes, useAsync: true);
        var length = checked((int)stream.Length);
        var buffer = new byte[length];
        await stream.ReadExactlyAsync(buffer, cancellationToken);
        return buffer;
    }

    public static async Task WriteAtomicAsync(
        string temporaryPath,
        string targetPath,
        ReadOnlyMemory<byte> content,
        int maxBytes,
        bool overwrite,
        CancellationToken cancellationToken = default)
    {
        ValidateLimit(maxBytes);
        if (content.Length > maxBytes)
            throw new InvalidDataException("Arquivo da fila excede o limite permitido.");

        await using (var stream = new FileStream(
            temporaryPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 16 * 1024,
            FileOptions.Asynchronous | FileOptions.WriteThrough))
        {
            await stream.WriteAsync(content, cancellationToken);
            await stream.FlushAsync(cancellationToken);
        }

        File.Move(temporaryPath, targetPath, overwrite);
    }

    public static bool IsReparsePoint(string path)
    {
        var attributes = File.GetAttributes(path);
        return (attributes & FileAttributes.ReparsePoint) != 0;
    }

    public static void EnsureNotReparsePoint(string path)
    {
        if (IsReparsePoint(path))
            throw new InvalidDataException("A pasta compartilhada contém um redirecionamento de caminho não permitido.");
    }

    private static FileStream OpenRead(string path, int maxBytes, bool useAsync = false)
    {
        if (IsReparsePoint(path))
            throw new InvalidDataException("Arquivo redirecionado não é permitido na fila compartilhada.");

        var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 16 * 1024,
            useAsync);

        if (stream.Length < 0 || stream.Length > maxBytes)
        {
            stream.Dispose();
            throw new InvalidDataException("Arquivo da fila excede o limite permitido.");
        }

        return stream;
    }

    private static void ValidateLimit(int maxBytes)
    {
        if (maxBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxBytes));
    }
}
