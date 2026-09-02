using System.Security.Cryptography;
using System.Text;

namespace NfeAgendamento.App.SharedQueue;

public sealed class PendingRequestSecretStore
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("NfeAgendamento.SharedQueue.Pending.v1");
    private readonly string _root;

    public PendingRequestSecretStore()
        : this(Path.Combine(AppPaths.StateRoot, "shared-queue", "pending"))
    {
    }

    public PendingRequestSecretStore(string root)
    {
        if (string.IsNullOrWhiteSpace(root))
            throw new ArgumentException("Pasta local de segredos pendentes inválida.", nameof(root));
        _root = Path.GetFullPath(root);
    }

    public async Task SaveAsync(Guid requestId, byte[] key, CancellationToken cancellationToken = default)
    {
        if (requestId == Guid.Empty)
            throw new ArgumentException("Identificador de requisição inválido.", nameof(requestId));
        ArgumentNullException.ThrowIfNull(key);
        if (key.Length != 32)
            throw new CryptographicException("Chave de sessão inválida.");

        Directory.CreateDirectory(_root);
        var protectedBytes = ProtectedData.Protect(key, Entropy, DataProtectionScope.CurrentUser);
        var path = PathFor(requestId);
        var temporary = path + ".tmp";
        try
        {
            await File.WriteAllBytesAsync(temporary, protectedBytes, cancellationToken);
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
            CryptographicOperations.ZeroMemory(protectedBytes);
        }
    }

    public async Task<byte[]> LoadAsync(Guid requestId, CancellationToken cancellationToken = default)
    {
        var path = PathFor(requestId);
        var protectedBytes = await File.ReadAllBytesAsync(path, cancellationToken);
        try
        {
            var key = ProtectedData.Unprotect(protectedBytes, Entropy, DataProtectionScope.CurrentUser);
            if (key.Length != 32)
            {
                CryptographicOperations.ZeroMemory(key);
                throw new CryptographicException("Chave pendente inválida.");
            }
            return key;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(protectedBytes);
        }
    }

    public void Delete(Guid requestId)
    {
        var path = PathFor(requestId);
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    internal string PathForTesting(Guid requestId) => PathFor(requestId);

    private string PathFor(Guid requestId)
    {
        if (requestId == Guid.Empty)
            throw new ArgumentException("Identificador de requisição inválido.", nameof(requestId));
        return Path.Combine(_root, $"{requestId:N}.key");
    }
}
