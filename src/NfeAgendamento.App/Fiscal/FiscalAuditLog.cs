using System.Security;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace NfeAgendamento.App.Fiscal;

public sealed class FiscalAuditLog
{
    public const long DefaultMaxFileBytes = 2 * 1024 * 1024;

    private readonly string _path;
    private readonly long _maxFileBytes;
    private readonly SemaphoreSlim _sync = new(1, 1);

    public FiscalAuditLog()
        : this(Path.Combine(AppPaths.LocalDataRoot, "logs", "fiscal-audit.jsonl"))
    {
    }

    public FiscalAuditLog(string path, long maxFileBytes = DefaultMaxFileBytes)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Caminho de auditoria inválido.", nameof(path));
        if (maxFileBytes < 128)
            throw new ArgumentOutOfRangeException(nameof(maxFileBytes));

        _path = path;
        _maxFileBytes = maxFileBytes;
    }

    public async Task RecordAsync(
        string accessKey,
        NfeLookupResult result,
        TimeSpan duration,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(result);

        try
        {
            await _sync.WaitAsync(cancellationToken);
            try
            {
                var record = new
                {
                    timestampUtc = DateTimeOffset.UtcNow,
                    keyFingerprint = Fingerprint(accessKey),
                    status = result.Status.ToString(),
                    cStat = result.CStat,
                    fromCache = result.FromCache,
                    durationMs = Math.Max(0, (long)Math.Round(duration.TotalMilliseconds))
                };

                var line = JsonSerializer.Serialize(record) + Environment.NewLine;
                var directory = Path.GetDirectoryName(_path);
                if (!string.IsNullOrWhiteSpace(directory))
                    Directory.CreateDirectory(directory);

                RotateIfNeeded(Encoding.UTF8.GetByteCount(line));
                await File.AppendAllTextAsync(_path, line, Encoding.UTF8, cancellationToken);
            }
            finally
            {
                _sync.Release();
            }
        }
        catch (Exception ex) when (ex is IOException
            or UnauthorizedAccessException
            or NotSupportedException
            or SecurityException
            or JsonException)
        {
            // Auditoria é auxiliar e nunca pode impedir a operação fiscal.
        }
    }

    private void RotateIfNeeded(int nextRecordBytes)
    {
        if (!File.Exists(_path))
            return;

        var length = new FileInfo(_path).Length;
        if (length + nextRecordBytes <= _maxFileBytes)
            return;

        var backup = _path + ".1";
        if (File.Exists(backup))
            File.Delete(backup);

        File.Move(_path, backup);
    }

    private static string Fingerprint(string accessKey)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(accessKey ?? string.Empty));
        return Convert.ToHexString(bytes)[..12];
    }
}
