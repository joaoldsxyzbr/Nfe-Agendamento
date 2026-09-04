using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NfeAgendamento.App.SharedQueue;

namespace NfeAgendamento.App.Storage;

public sealed class EncryptedXmlCache
{
    private const int MaxSharedBytes = 16 * 1024 * 1024;
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("NfeAgendamento.XmlCache.v1");
    private static readonly byte[] SharedAssociatedData = Encoding.UTF8.GetBytes("nfe-agendamento:xml-cache:v1");
    private readonly string _root;
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _retention;
    private readonly CandidateStateStore? _candidateState;
    private readonly bool _sharedMode;

    public EncryptedXmlCache()
        : this(AppPaths.CacheRoot, TimeProvider.System, TimeSpan.FromHours(24))
    {
    }

    public EncryptedXmlCache(string root, TimeProvider timeProvider, TimeSpan retention)
    {
        if (string.IsNullOrWhiteSpace(root))
            throw new ArgumentException("Diretório do cache inválido.", nameof(root));
        if (retention <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(retention));

        _root = Path.GetFullPath(root);
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _retention = retention;
    }

    public EncryptedXmlCache(SharedQueuePaths paths, CandidateStateStore candidateState)
        : this(paths, candidateState, TimeProvider.System, TimeSpan.FromHours(24))
    {
    }

    public EncryptedXmlCache(
        SharedQueuePaths paths,
        CandidateStateStore candidateState,
        TimeProvider timeProvider,
        TimeSpan retention)
    {
        ArgumentNullException.ThrowIfNull(paths);
        if (retention <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(retention));

        _root = paths.CacheDirectory;
        _candidateState = candidateState ?? throw new ArgumentNullException(nameof(candidateState));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _retention = retention;
        _sharedMode = true;
    }

    public async Task PutAsync(string accessKey, string xml, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(accessKey))
            throw new ArgumentException("Chave de acesso não informada.", nameof(accessKey));
        if (string.IsNullOrWhiteSpace(xml))
            throw new ArgumentException("XML não informado.", nameof(xml));

        Directory.CreateDirectory(_root);
        SharedQueueFileIO.EnsureNotReparsePoint(_root);
        var path = PathFor(accessKey);
        var entry = new CacheEntry(accessKey, _timeProvider.GetUtcNow(), xml);
        var plainBytes = JsonSerializer.SerializeToUtf8Bytes(entry);

        if (_sharedMode)
        {
            var groupKey = LoadGroupKey();
            byte[]? bytes = null;
            try
            {
                var envelope = CandidateBundleStore.Protect(groupKey, plainBytes, SharedAssociatedData);
                bytes = JsonSerializer.SerializeToUtf8Bytes(envelope);
                if (bytes.Length > MaxSharedBytes)
                    throw new InvalidDataException("Entrada de cache compartilhado excede o limite permitido.");

                var temporary = path + $".{Guid.NewGuid():N}.tmp";
                try
                {
                    await SharedQueueFileIO.WriteAtomicAsync(
                        temporary,
                        path,
                        bytes,
                        MaxSharedBytes,
                        overwrite: true,
                        cancellationToken);
                }
                finally
                {
                    TryDelete(temporary);
                }
            }
            finally
            {
                CryptographicOperations.ZeroMemory(groupKey);
                CryptographicOperations.ZeroMemory(plainBytes);
                if (bytes is not null) CryptographicOperations.ZeroMemory(bytes);
            }
            return;
        }

        var protectedBytes = ProtectedData.Protect(plainBytes, Entropy, DataProtectionScope.CurrentUser);
        try
        {
            var temporary = path + ".tmp";
            await File.WriteAllBytesAsync(temporary, protectedBytes, cancellationToken);
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plainBytes);
            CryptographicOperations.ZeroMemory(protectedBytes);
        }
    }

    public async Task<CacheEntry?> TryGetAsync(string accessKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(accessKey))
            return null;

        var path = PathFor(accessKey);
        if (!File.Exists(path))
            return null;

        if (_sharedMode)
            return await TryGetSharedAsync(accessKey, path, cancellationToken);

        try
        {
            var protectedBytes = await File.ReadAllBytesAsync(path, cancellationToken);
            byte[]? plainBytes = null;
            try
            {
                plainBytes = ProtectedData.Unprotect(protectedBytes, Entropy, DataProtectionScope.CurrentUser);
                return ValidateEntry(accessKey, path, plainBytes);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(protectedBytes);
                if (plainBytes is not null) CryptographicOperations.ZeroMemory(plainBytes);
            }
        }
        catch (InvalidDataException)
        {
            TryDelete(path);
            return null;
        }
        catch (Exception ex) when (ex is CryptographicException or JsonException)
        {
            TryDelete(path);
            return null;
        }
    }

    public async Task PurgeExpiredAsync(CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(_root))
            return;

        string[] files;
        try
        {
            SharedQueueFileIO.EnsureNotReparsePoint(_root);
            files = Directory.EnumerateFiles(_root, "*.bin", SearchOption.TopDirectoryOnly).ToArray();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DirectoryNotFoundException or InvalidDataException)
        {
            return;
        }

        byte[]? groupKey = null;
        try
        {
            if (_sharedMode)
            {
                try
                {
                    groupKey = LoadGroupKey();
                }
                catch (InvalidOperationException)
                {
                    return;
                }
            }

            foreach (var path in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var entry = _sharedMode
                        ? ReadSharedEntryForMaintenance(path, groupKey!)
                        : await ReadLocalEntryForMaintenanceAsync(path, cancellationToken);

                    if (IsExpired(entry))
                        TryDelete(path);
                }
                catch (Exception ex) when (ex is IOException
                    or UnauthorizedAccessException
                    or CryptographicException
                    or JsonException
                    or InvalidDataException
                    or ArgumentOutOfRangeException)
                {
                    TryDelete(path);
                }
            }
        }
        finally
        {
            if (groupKey is not null)
                CryptographicOperations.ZeroMemory(groupKey);
        }
    }

    private Task<CacheEntry?> TryGetSharedAsync(
        string accessKey,
        string path,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var groupKey = LoadGroupKey();
        byte[]? bytes = null;
        byte[]? plainBytes = null;
        try
        {
            SharedQueueFileIO.EnsureNotReparsePoint(_root);
            SharedQueueFileIO.EnsureNotReparsePoint(path);
            bytes = SharedQueueFileIO.ReadAllBytes(path, MaxSharedBytes);
            ProtectedGroupEnvelope envelope;
            try
            {
                envelope = JsonSerializer.Deserialize<ProtectedGroupEnvelope>(bytes)
                    ?? throw new CryptographicException("Entrada de cache compartilhado inválida.");
            }
            catch (JsonException ex)
            {
                throw new CryptographicException("Entrada de cache compartilhado inválida.", ex);
            }

            plainBytes = CandidateBundleStore.Unprotect(groupKey, envelope, SharedAssociatedData);
            return Task.FromResult<CacheEntry?>(ValidateEntry(accessKey, path, plainBytes));
        }
        catch (InvalidDataException)
        {
            TryDelete(path);
            return Task.FromResult<CacheEntry?>(null);
        }
        catch (Exception ex) when (ex is CryptographicException or JsonException)
        {
            TryDelete(path);
            return Task.FromResult<CacheEntry?>(null);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(groupKey);
            if (bytes is not null) CryptographicOperations.ZeroMemory(bytes);
            if (plainBytes is not null) CryptographicOperations.ZeroMemory(plainBytes);
        }
    }

    private CacheEntry ReadSharedEntryForMaintenance(string path, byte[] groupKey)
    {
        byte[]? bytes = null;
        byte[]? plainBytes = null;
        try
        {
            SharedQueueFileIO.EnsureNotReparsePoint(path);
            bytes = SharedQueueFileIO.ReadAllBytes(path, MaxSharedBytes);
            var envelope = JsonSerializer.Deserialize<ProtectedGroupEnvelope>(bytes)
                ?? throw new CryptographicException("Entrada de cache compartilhado inválida.");
            plainBytes = CandidateBundleStore.Unprotect(groupKey, envelope, SharedAssociatedData);
            return DeserializeEntry(plainBytes);
        }
        finally
        {
            if (bytes is not null) CryptographicOperations.ZeroMemory(bytes);
            if (plainBytes is not null) CryptographicOperations.ZeroMemory(plainBytes);
        }
    }

    private async Task<CacheEntry> ReadLocalEntryForMaintenanceAsync(
        string path,
        CancellationToken cancellationToken)
    {
        SharedQueueFileIO.EnsureNotReparsePoint(path);
        var info = new FileInfo(path);
        if (info.Length <= 0 || info.Length > MaxSharedBytes)
            throw new InvalidDataException("Entrada de cache local excede o limite permitido.");

        var protectedBytes = await File.ReadAllBytesAsync(path, cancellationToken);
        byte[]? plainBytes = null;
        try
        {
            plainBytes = ProtectedData.Unprotect(protectedBytes, Entropy, DataProtectionScope.CurrentUser);
            return DeserializeEntry(plainBytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(protectedBytes);
            if (plainBytes is not null) CryptographicOperations.ZeroMemory(plainBytes);
        }
    }

    private CacheEntry? ValidateEntry(string accessKey, string path, byte[] plainBytes)
    {
        var entry = DeserializeEntry(plainBytes);

        if (!string.Equals(entry.AccessKey, accessKey, StringComparison.Ordinal))
            throw new InvalidDataException("A entrada de cache não corresponde à chave solicitada.");

        if (IsExpired(entry))
        {
            TryDelete(path);
            return null;
        }

        return entry;
    }

    private static CacheEntry DeserializeEntry(byte[] plainBytes) =>
        JsonSerializer.Deserialize<CacheEntry>(plainBytes)
        ?? throw new InvalidDataException("Entrada de cache inválida.");

    private bool IsExpired(CacheEntry entry) =>
        _timeProvider.GetUtcNow() >= entry.StoredAtUtc.Add(_retention);

    private byte[] LoadGroupKey() =>
        _candidateState!.Load()
        ?? throw new InvalidOperationException("Este PC ainda não possui o estado seguro do grupo para acessar o cache fiscal.");

    private string PathFor(string accessKey)
    {
        var hash = SHA256.HashData(Encoding.ASCII.GetBytes(accessKey));
        return Path.Combine(_root, $"{Convert.ToHexString(hash)}.bin");
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
