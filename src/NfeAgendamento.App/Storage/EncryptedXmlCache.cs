using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace NfeAgendamento.App.Storage;

public sealed class EncryptedXmlCache
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("NfeAgendamento.XmlCache.v1");
    private readonly string _root;
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _retention;

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

        _root = root;
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _retention = retention;
    }

    public async Task PutAsync(string accessKey, string xml, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(accessKey))
            throw new ArgumentException("Chave de acesso não informada.", nameof(accessKey));
        if (string.IsNullOrWhiteSpace(xml))
            throw new ArgumentException("XML não informado.", nameof(xml));

        Directory.CreateDirectory(_root);
        var path = PathFor(accessKey);
        var entry = new CacheEntry(accessKey, _timeProvider.GetUtcNow(), xml);
        var plainBytes = JsonSerializer.SerializeToUtf8Bytes(entry);
        var protectedBytes = ProtectedData.Protect(plainBytes, Entropy, DataProtectionScope.CurrentUser);

        var temporary = path + ".tmp";
        await File.WriteAllBytesAsync(temporary, protectedBytes, cancellationToken);
        File.Move(temporary, path, overwrite: true);
    }

    public async Task<CacheEntry?> TryGetAsync(string accessKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(accessKey))
            return null;

        var path = PathFor(accessKey);
        if (!File.Exists(path))
            return null;

        try
        {
            var protectedBytes = await File.ReadAllBytesAsync(path, cancellationToken);
            var plainBytes = ProtectedData.Unprotect(protectedBytes, Entropy, DataProtectionScope.CurrentUser);
            var entry = JsonSerializer.Deserialize<CacheEntry>(plainBytes)
                ?? throw new InvalidDataException("Entrada de cache local inválida.");

            if (!string.Equals(entry.AccessKey, accessKey, StringComparison.Ordinal))
                throw new InvalidDataException("A entrada de cache não corresponde à chave solicitada.");

            if (_timeProvider.GetUtcNow() >= entry.StoredAtUtc.Add(_retention))
            {
                TryDelete(path);
                return null;
            }

            return entry;
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
        catch (IOException)
        {
        }
    }
}
