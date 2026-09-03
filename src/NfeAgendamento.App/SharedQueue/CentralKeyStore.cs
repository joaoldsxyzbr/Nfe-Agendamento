using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NfeAgendamento.App.Certificates;

namespace NfeAgendamento.App.SharedQueue;

public sealed record ClusterIdentityBinding(string CertificateThumbprint, string AuthorityState);

internal sealed record ClusterIdentityEnvelope(
    int Version,
    string CertificateThumbprint,
    string AuthorityState,
    byte[] WrappedStateKey,
    byte[] Nonce,
    byte[] Tag,
    byte[] Ciphertext);

public sealed class CentralKeyStore
{
    private const int ClusterVersion = 1;
    private const int ClusterMaxBytes = 64 * 1024;
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("NfeAgendamento.SharedQueue.CentralKey.v1");
    private static readonly byte[] ClusterAssociatedData = Encoding.UTF8.GetBytes("NfeAgendamento.SharedQueue.ClusterIdentity.v1");
    private readonly object _sync = new();
    private readonly string _path;
    private readonly SharedQueuePaths? _paths;
    private readonly CertificateService? _certificates;
    private readonly CentralStateService? _legacyState;

    public CentralKeyStore()
        : this(Path.Combine(AppPaths.StateRoot, "shared-queue", "central-private-key.bin"))
    {
    }

    public CentralKeyStore(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Caminho local da chave da Central inválido.", nameof(path));
        _path = Path.GetFullPath(path);
    }

    public CentralKeyStore(
        SharedQueuePaths paths,
        CertificateService certificates,
        CentralStateService legacyState,
        string? legacyPath = null)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _certificates = certificates ?? throw new ArgumentNullException(nameof(certificates));
        _legacyState = legacyState ?? throw new ArgumentNullException(nameof(legacyState));
        _path = Path.GetFullPath(legacyPath ?? Path.Combine(AppPaths.StateRoot, "shared-queue", "central-private-key.bin"));
    }

    public bool ClusterIdentityExists => _paths is not null && File.Exists(_paths.ClusterIdentityPath);

    public byte[] GetOrCreatePublicKey()
    {
        using var rsa = OpenPrivateKey();
        return rsa.ExportSubjectPublicKeyInfo();
    }

    public byte[] ExportPrivateKeyPkcs8()
    {
        using var rsa = OpenPrivateKey();
        return rsa.ExportPkcs8PrivateKey();
    }

    public ClusterIdentityBinding GetClusterBinding()
    {
        if (_paths is null)
            throw new InvalidOperationException("A identidade compartilhada não está habilitada neste armazenamento.");
        var envelope = ReadClusterEnvelope();
        return new ClusterIdentityBinding(envelope.CertificateThumbprint, envelope.AuthorityState);
    }

    public RSA OpenPrivateKey()
    {
        lock (_sync)
        {
            var privateBytes = _paths is null
                ? LoadOrCreatePrivateBytes()
                : LoadClusterPrivateBytes();
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
    }

    internal byte[] OpenClusterStateKey()
    {
        lock (_sync)
        {
            if (_paths is null || _certificates is null)
                throw new InvalidOperationException("A identidade compartilhada não está habilitada.");
            EnsureClusterIdentity();
            var envelope = ReadClusterEnvelope();
            using var certificate = GetClusterCertificate(envelope);
            using var rsa = certificate.GetRSAPrivateKey()
                ?? throw new InvalidOperationException("O certificado A1 do grupo não possui chave RSA privada disponível.");
            try
            {
                var key = rsa.Decrypt(envelope.WrappedStateKey, RSAEncryptionPadding.OaepSHA256);
                CandidateStateStore.ValidateGroupKey(key);
                return key;
            }
            catch (CryptographicException ex)
            {
                throw new InvalidOperationException("O certificado A1 deste PC não consegue abrir a identidade compartilhada.", ex);
            }
        }
    }

    private byte[] LoadClusterPrivateBytes()
    {
        EnsureClusterIdentity();
        var envelope = ReadClusterEnvelope();
        var stateKey = OpenClusterStateKey();
        var plain = new byte[envelope.Ciphertext.Length];
        try
        {
            using var aes = new AesGcm(stateKey, 16);
            aes.Decrypt(envelope.Nonce, envelope.Ciphertext, envelope.Tag, plain, ClusterAssociatedData);
            return plain;
        }
        catch (CryptographicException ex)
        {
            CryptographicOperations.ZeroMemory(plain);
            throw new InvalidOperationException("A identidade compartilhada da fila está inválida ou foi adulterada.", ex);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(stateKey);
        }
    }

    private void EnsureClusterIdentity()
    {
        if (_paths is null || _certificates is null || _legacyState is null)
            return;
        if (File.Exists(_paths.ClusterIdentityPath))
            return;
        if (!_legacyState.IsConfiguredAsCentral)
            throw new InvalidOperationException("A identidade compartilhada ainda não foi inicializada pelo PC Central anterior.");
        if (!_paths.ValidateForClient())
            throw new InvalidOperationException("A pasta compartilhada não está disponível para inicializar a identidade do grupo.");

        var (certificate, _) = _certificates.GetCurrentSelectionWithCertificate();
        using (certificate)
        {
            var authorityState = _certificates.GetCurrentAuthorityState();
            if (string.IsNullOrWhiteSpace(authorityState))
                throw new InvalidOperationException("A UF autora do certificado A1 não está configurada.");
            using var publicRsa = certificate.GetRSAPublicKey()
                ?? throw new InvalidOperationException("O certificado A1 selecionado não possui chave RSA.");

            var privateBytes = LoadOrCreatePrivateBytes();
            var stateKey = RandomNumberGenerator.GetBytes(32);
            var nonce = RandomNumberGenerator.GetBytes(12);
            var tag = new byte[16];
            var cipher = new byte[privateBytes.Length];
            byte[]? wrappedKey = null;
            byte[]? serialized = null;
            try
            {
                using (var aes = new AesGcm(stateKey, 16))
                    aes.Encrypt(nonce, privateBytes, cipher, tag, ClusterAssociatedData);
                wrappedKey = publicRsa.Encrypt(stateKey, RSAEncryptionPadding.OaepSHA256);
                var envelope = new ClusterIdentityEnvelope(
                    ClusterVersion,
                    NormalizeThumbprint(certificate.Thumbprint),
                    authorityState.Trim(),
                    wrappedKey,
                    nonce,
                    tag,
                    cipher);
                serialized = JsonSerializer.SerializeToUtf8Bytes(envelope);
                if (serialized.Length > ClusterMaxBytes)
                    throw new InvalidDataException("A identidade compartilhada excede o limite permitido.");

                var temporary = _paths.ClusterIdentityTemporaryPath(Guid.NewGuid());
                try
                {
                    SharedQueueFileIO.WriteAtomicAsync(
                        temporary,
                        _paths.ClusterIdentityPath,
                        serialized,
                        ClusterMaxBytes,
                        overwrite: false,
                        CancellationToken.None).GetAwaiter().GetResult();
                }
                catch (IOException) when (File.Exists(_paths.ClusterIdentityPath))
                {
                }
                finally
                {
                    TryDelete(temporary);
                }
            }
            finally
            {
                CryptographicOperations.ZeroMemory(privateBytes);
                CryptographicOperations.ZeroMemory(stateKey);
                if (wrappedKey is not null) CryptographicOperations.ZeroMemory(wrappedKey);
                CryptographicOperations.ZeroMemory(nonce);
                CryptographicOperations.ZeroMemory(tag);
                CryptographicOperations.ZeroMemory(cipher);
                if (serialized is not null) CryptographicOperations.ZeroMemory(serialized);
            }
        }
    }

    private ClusterIdentityEnvelope ReadClusterEnvelope()
    {
        if (_paths is null || !File.Exists(_paths.ClusterIdentityPath))
            throw new InvalidOperationException("A identidade compartilhada da fila ainda não foi inicializada.");
        var bytes = SharedQueueFileIO.ReadAllBytes(_paths.ClusterIdentityPath, ClusterMaxBytes);
        try
        {
            var envelope = JsonSerializer.Deserialize<ClusterIdentityEnvelope>(bytes)
                ?? throw new InvalidDataException("Identidade compartilhada vazia.");
            if (envelope.Version != ClusterVersion
                || string.IsNullOrWhiteSpace(envelope.CertificateThumbprint)
                || string.IsNullOrWhiteSpace(envelope.AuthorityState)
                || envelope.WrappedStateKey is null || envelope.WrappedStateKey.Length == 0
                || envelope.Nonce is null || envelope.Nonce.Length != 12
                || envelope.Tag is null || envelope.Tag.Length != 16
                || envelope.Ciphertext is null || envelope.Ciphertext.Length == 0)
            {
                throw new InvalidDataException("Formato da identidade compartilhada inválido.");
            }
            return envelope;
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("Identidade compartilhada inválida.", ex);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }

    private System.Security.Cryptography.X509Certificates.X509Certificate2 GetClusterCertificate(ClusterIdentityEnvelope envelope)
    {
        try
        {
            return _certificates!.GetByThumbprint(envelope.CertificateThumbprint);
        }
        catch (InvalidOperationException ex)
        {
            throw new InvalidOperationException(
                $"Este PC não possui o certificado A1 exigido pela identidade compartilhada ({envelope.CertificateThumbprint}).",
                ex);
        }
    }

    private byte[] LoadOrCreatePrivateBytes()
    {
        if (File.Exists(_path))
        {
            var protectedBytes = File.ReadAllBytes(_path);
            try
            {
                return ProtectedData.Unprotect(protectedBytes, Entropy, DataProtectionScope.CurrentUser);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(protectedBytes);
            }
        }

        using var rsa = RSA.Create(2048);
        var privateBytes = rsa.ExportPkcs8PrivateKey();
        var protectedNew = ProtectedData.Protect(privateBytes, Entropy, DataProtectionScope.CurrentUser);
        try
        {
            var directory = Path.GetDirectoryName(_path);
            if (string.IsNullOrWhiteSpace(directory))
                throw new InvalidOperationException("Diretório local da chave da Central inválido.");

            Directory.CreateDirectory(directory);
            var temporary = _path + ".tmp";
            File.WriteAllBytes(temporary, protectedNew);
            File.Move(temporary, _path, overwrite: true);
            return privateBytes.ToArray();
        }
        finally
        {
            CryptographicOperations.ZeroMemory(privateBytes);
            CryptographicOperations.ZeroMemory(protectedNew);
        }
    }

    private static string NormalizeThumbprint(string thumbprint) =>
        string.Concat((thumbprint ?? string.Empty).Where(character => !char.IsWhiteSpace(character))).ToUpperInvariant();

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }
}
