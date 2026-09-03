using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace NfeAgendamento.App.SharedQueue;

public sealed record CandidateBundlePayload(byte[] GroupStateKey, byte[] CentralPublicKeySha256);

internal sealed record ProtectedGroupEnvelope(
    int Version,
    byte[] Nonce,
    byte[] Tag,
    byte[] Ciphertext);

public sealed class CandidateStateStore
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("NfeAgendamento.SharedQueue.CandidateState.v1");
    private readonly object _sync = new();
    private readonly string _path;

    public CandidateStateStore()
        : this(Path.Combine(AppPaths.StateRoot, "shared-queue", "candidate-state.bin"))
    {
    }

    public CandidateStateStore(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Caminho do estado de candidatura inválido.", nameof(path));
        _path = Path.GetFullPath(path);
    }

    public bool IsReady => Load() is { Length: 32 } key && ZeroAndReturnTrue(key);

    public byte[]? Load()
    {
        lock (_sync)
        {
            if (!File.Exists(_path))
                return null;

            byte[]? protectedBytes = null;
            try
            {
                protectedBytes = File.ReadAllBytes(_path);
                var plain = ProtectedData.Unprotect(protectedBytes, Entropy, DataProtectionScope.CurrentUser);
                if (plain.Length != 32)
                {
                    CryptographicOperations.ZeroMemory(plain);
                    return null;
                }
                return plain;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException)
            {
                return null;
            }
            finally
            {
                if (protectedBytes is not null)
                    CryptographicOperations.ZeroMemory(protectedBytes);
            }
        }
    }

    public void Save(byte[] groupStateKey)
    {
        ValidateGroupKey(groupStateKey);
        lock (_sync)
        {
            var protectedBytes = ProtectedData.Protect(groupStateKey, Entropy, DataProtectionScope.CurrentUser);
            try
            {
                var directory = Path.GetDirectoryName(_path)
                    ?? throw new InvalidOperationException("Diretório do estado de candidatura inválido.");
                Directory.CreateDirectory(directory);
                var temporary = _path + $".{Guid.NewGuid():N}.tmp";
                try
                {
                    using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    {
                        stream.Write(protectedBytes);
                        stream.Flush(flushToDisk: true);
                    }
                    File.Move(temporary, _path, overwrite: true);
                }
                finally
                {
                    TryDelete(temporary);
                }
            }
            finally
            {
                CryptographicOperations.ZeroMemory(protectedBytes);
            }
        }
    }

    private static bool ZeroAndReturnTrue(byte[] key)
    {
        CryptographicOperations.ZeroMemory(key);
        return true;
    }

    internal static void ValidateGroupKey(byte[] key)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (key.Length != 32)
            throw new CryptographicException("Chave de estado do grupo inválida.");
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }
}

public sealed class CandidateBundleStore
{
    private const int Version = 1;
    private const int MaxBytes = 16 * 1024;
    private static readonly byte[] KeyContext = Encoding.UTF8.GetBytes("nfe-agendamento:candidate-bundle:v1");
    private readonly SharedQueuePaths _paths;

    public CandidateBundleStore(SharedQueuePaths paths)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
    }

    public async Task WriteAsync(
        Guid clientId,
        byte[] clientSecret,
        CandidateBundlePayload payload,
        CancellationToken cancellationToken = default)
    {
        ValidateClientId(clientId);
        ValidateClientSecret(clientSecret);
        ValidatePayload(payload);

        var key = HMACSHA256.HashData(clientSecret, KeyContext);
        var plain = JsonSerializer.SerializeToUtf8Bytes(payload);
        try
        {
            var envelope = Protect(key, plain, CandidateAssociatedData(clientId));
            var bytes = JsonSerializer.SerializeToUtf8Bytes(envelope);
            if (bytes.Length > MaxBytes)
                throw new InvalidDataException("Pacote de candidatura excede o limite permitido.");

            var target = _paths.CandidateBundlePath(clientId);
            var temporary = _paths.CandidateBundleTemporaryPath(clientId, Guid.NewGuid());
            try
            {
                await SharedQueueFileIO.WriteAtomicAsync(
                    temporary,
                    target,
                    bytes,
                    MaxBytes,
                    overwrite: true,
                    cancellationToken);
            }
            finally
            {
                TryDelete(temporary);
                CryptographicOperations.ZeroMemory(bytes);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(plain);
        }
    }

    public CandidateBundlePayload Read(Guid clientId, byte[] clientSecret)
    {
        ValidateClientId(clientId);
        ValidateClientSecret(clientSecret);
        var path = _paths.CandidateBundlePath(clientId);
        var bytes = SharedQueueFileIO.ReadAllBytes(path, MaxBytes);
        var key = HMACSHA256.HashData(clientSecret, KeyContext);
        byte[]? plain = null;
        try
        {
            var envelope = JsonSerializer.Deserialize<ProtectedGroupEnvelope>(bytes)
                ?? throw new CryptographicException("Pacote de candidatura inválido.");
            plain = Unprotect(key, envelope, CandidateAssociatedData(clientId));
            var payload = JsonSerializer.Deserialize<CandidateBundlePayload>(plain)
                ?? throw new CryptographicException("Conteúdo do pacote de candidatura inválido.");
            ValidatePayload(payload);
            return payload with
            {
                GroupStateKey = payload.GroupStateKey.ToArray(),
                CentralPublicKeySha256 = payload.CentralPublicKeySha256.ToArray()
            };
        }
        catch (JsonException ex)
        {
            throw new CryptographicException("Pacote de candidatura inválido.", ex);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
            CryptographicOperations.ZeroMemory(key);
            if (plain is not null) CryptographicOperations.ZeroMemory(plain);
        }
    }

    private static void ValidatePayload(CandidateBundlePayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        CandidateStateStore.ValidateGroupKey(payload.GroupStateKey);
        if (payload.CentralPublicKeySha256 is null || payload.CentralPublicKeySha256.Length != 32)
            throw new CryptographicException("Fingerprint da identidade do grupo inválida.");
    }

    private static void ValidateClientId(Guid clientId)
    {
        if (clientId == Guid.Empty) throw new ArgumentException("Cliente inválido.", nameof(clientId));
    }

    private static void ValidateClientSecret(byte[] clientSecret)
    {
        ArgumentNullException.ThrowIfNull(clientSecret);
        if (clientSecret.Length != 32) throw new CryptographicException("Segredo do cliente inválido.");
    }

    private static byte[] CandidateAssociatedData(Guid clientId) =>
        Encoding.UTF8.GetBytes($"nfe-agendamento:candidate:{Version}:{clientId:N}");

    internal static ProtectedGroupEnvelope Protect(byte[] key, byte[] plain, byte[] associatedData)
    {
        CandidateStateStore.ValidateGroupKey(key);
        var nonce = RandomNumberGenerator.GetBytes(12);
        var tag = new byte[16];
        var cipher = new byte[plain.Length];
        using var aes = new AesGcm(key, 16);
        aes.Encrypt(nonce, plain, cipher, tag, associatedData);
        return new ProtectedGroupEnvelope(Version, nonce, tag, cipher);
    }

    internal static byte[] Unprotect(byte[] key, ProtectedGroupEnvelope envelope, byte[] associatedData)
    {
        CandidateStateStore.ValidateGroupKey(key);
        if (envelope.Version != Version
            || envelope.Nonce is null || envelope.Nonce.Length != 12
            || envelope.Tag is null || envelope.Tag.Length != 16
            || envelope.Ciphertext is null || envelope.Ciphertext.Length == 0)
        {
            throw new CryptographicException("Envelope protegido inválido.");
        }

        var plain = new byte[envelope.Ciphertext.Length];
        try
        {
            using var aes = new AesGcm(key, 16);
            aes.Decrypt(envelope.Nonce, envelope.Ciphertext, envelope.Tag, plain, associatedData);
            return plain;
        }
        catch
        {
            CryptographicOperations.ZeroMemory(plain);
            throw;
        }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }
}

public sealed class SharedGroupIdentityStore
{
    private const int MaxBytes = 32 * 1024;
    private static readonly byte[] AssociatedData = Encoding.UTF8.GetBytes("nfe-agendamento:group-identity:v1");
    private readonly SharedQueuePaths _paths;

    public SharedGroupIdentityStore(SharedQueuePaths paths)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
    }

    public bool Exists => File.Exists(_paths.GroupIdentityPath);

    public void Initialize(byte[] groupStateKey, byte[] privateKeyPkcs8)
    {
        CandidateStateStore.ValidateGroupKey(groupStateKey);
        ArgumentNullException.ThrowIfNull(privateKeyPkcs8);
        using (var rsa = RSA.Create())
            rsa.ImportPkcs8PrivateKey(privateKeyPkcs8, out _);

        if (Exists)
            return;

        var envelope = CandidateBundleStore.Protect(groupStateKey, privateKeyPkcs8, AssociatedData);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(envelope);
        try
        {
            if (bytes.Length > MaxBytes)
                throw new InvalidDataException("Identidade compartilhada excede o limite permitido.");
            var temporary = _paths.GroupIdentityTemporaryPath(Guid.NewGuid());
            try
            {
                SharedQueueFileIO.WriteAtomicAsync(
                    temporary,
                    _paths.GroupIdentityPath,
                    bytes,
                    MaxBytes,
                    overwrite: false,
                    CancellationToken.None).GetAwaiter().GetResult();
            }
            catch (IOException) when (File.Exists(_paths.GroupIdentityPath))
            {
            }
            finally
            {
                TryDelete(temporary);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }

    public RSA OpenPrivateKey(byte[] groupStateKey)
    {
        var privateBytes = ReadPrivateKey(groupStateKey);
        try
        {
            var rsa = RSA.Create();
            rsa.ImportPkcs8PrivateKey(privateBytes, out _);
            return rsa;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(privateBytes);
        }
    }

    public byte[] GetPublicKey(byte[] groupStateKey)
    {
        using var rsa = OpenPrivateKey(groupStateKey);
        return rsa.ExportSubjectPublicKeyInfo();
    }

    private byte[] ReadPrivateKey(byte[] groupStateKey)
    {
        CandidateStateStore.ValidateGroupKey(groupStateKey);
        var bytes = SharedQueueFileIO.ReadAllBytes(_paths.GroupIdentityPath, MaxBytes);
        try
        {
            var envelope = JsonSerializer.Deserialize<ProtectedGroupEnvelope>(bytes)
                ?? throw new CryptographicException("Identidade compartilhada inválida.");
            return CandidateBundleStore.Unprotect(groupStateKey, envelope, AssociatedData);
        }
        catch (JsonException ex)
        {
            throw new CryptographicException("Identidade compartilhada inválida.", ex);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }
}
