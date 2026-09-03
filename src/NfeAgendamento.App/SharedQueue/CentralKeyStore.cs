using System.Security.Cryptography;
using System.Text;

namespace NfeAgendamento.App.SharedQueue;

public sealed class CentralKeyStore
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("NfeAgendamento.SharedQueue.CentralKey.v1");
    private readonly object _sync = new();
    private readonly string _path;
    private readonly CandidateStateStore? _candidateState;
    private readonly SharedGroupIdentityStore? _groupIdentity;

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

    public CentralKeyStore(CandidateStateStore candidateState, SharedGroupIdentityStore groupIdentity)
        : this(Path.Combine(AppPaths.StateRoot, "shared-queue", "central-private-key.bin"))
    {
        _candidateState = candidateState ?? throw new ArgumentNullException(nameof(candidateState));
        _groupIdentity = groupIdentity ?? throw new ArgumentNullException(nameof(groupIdentity));
    }

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

    public RSA OpenPrivateKey()
    {
        lock (_sync)
        {
            var groupKey = _candidateState?.Load();
            if (groupKey is not null && _groupIdentity?.Exists == true)
            {
                try
                {
                    return _groupIdentity.OpenPrivateKey(groupKey);
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(groupKey);
                }
            }

            var privateBytes = LoadOrCreatePrivateBytes();
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
}
