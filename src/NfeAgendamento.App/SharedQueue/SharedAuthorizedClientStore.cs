using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace NfeAgendamento.App.SharedQueue;

public sealed record AuthorizedClientSnapshot(
    Guid ClientId,
    string ClientName,
    byte[] Secret,
    long LastSequence);

public sealed class SharedAuthorizedClientStore
{
    private const int MaxBytes = 256 * 1024;
    private static readonly byte[] AssociatedData = Encoding.UTF8.GetBytes("nfe-agendamento:authorized-clients:v1");
    private readonly object _sync = new();
    private readonly SharedQueuePaths _paths;
    private readonly CandidateStateStore _candidateState;

    public SharedAuthorizedClientStore(SharedQueuePaths paths, CandidateStateStore candidateState)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _candidateState = candidateState ?? throw new ArgumentNullException(nameof(candidateState));
    }

    public int Count
    {
        get
        {
            lock (_sync)
            {
                var clients = LoadUnlocked();
                try { return clients.Count; }
                finally { ZeroClients(clients); }
            }
        }
    }

    public void Authorize(Guid clientId, string clientName, byte[] secret)
    {
        ValidateClient(clientId, clientName, secret);
        lock (_sync)
        {
            var clients = LoadUnlocked();
            try
            {
                var index = clients.FindIndex(item => item.ClientId == clientId);
                var replacement = new AuthorizedClientSnapshot(clientId, clientName.Trim(), secret.ToArray(), 0);
                if (index >= 0)
                {
                    CryptographicOperations.ZeroMemory(clients[index].Secret);
                    clients[index] = replacement;
                }
                else
                {
                    clients.Add(replacement);
                }
                SaveUnlocked(clients);
            }
            finally
            {
                ZeroClients(clients);
            }
        }
    }

    public bool TryAuthenticateAndAdvance(QueueRequestEnvelope envelope, out string error)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        lock (_sync)
        {
            var clients = LoadUnlocked();
            try
            {
                var index = clients.FindIndex(item => item.ClientId == envelope.ClientId);
                if (index < 0)
                {
                    error = "Este PC não está autorizado na fila. Faça o pareamento novamente.";
                    return false;
                }

                var client = clients[index];
                if (envelope.Sequence <= client.LastSequence)
                {
                    error = "Solicitação repetida ou fora de ordem foi bloqueada.";
                    return false;
                }

                if (!SharedQueueCrypto.VerifyClientAuthentication(envelope, client.Secret))
                {
                    error = "Autenticação do cliente inválida.";
                    return false;
                }

                clients[index] = client with { LastSequence = envelope.Sequence };
                SaveUnlocked(clients);
                error = string.Empty;
                return true;
            }
            finally
            {
                ZeroClients(clients);
            }
        }
    }

    public IReadOnlyList<AuthorizedClientSnapshot> Snapshot()
    {
        lock (_sync)
        {
            var clients = LoadUnlocked();
            try
            {
                return clients.Select(Clone).ToArray();
            }
            finally
            {
                ZeroClients(clients);
            }
        }
    }

    public void ReplaceFromLegacy(IEnumerable<AuthorizedClientSnapshot> legacyClients)
    {
        ArgumentNullException.ThrowIfNull(legacyClients);
        lock (_sync)
        {
            if (File.Exists(_paths.AuthorizedClientsPath))
                return;

            var clients = legacyClients.Select(Clone).ToList();
            try
            {
                foreach (var client in clients)
                    ValidateSnapshot(client);
                SaveUnlocked(clients);
            }
            finally
            {
                ZeroClients(clients);
            }
        }
    }

    private List<AuthorizedClientSnapshot> LoadUnlocked()
    {
        if (!File.Exists(_paths.AuthorizedClientsPath))
            return [];

        var key = LoadGroupKey();
        var bytes = SharedQueueFileIO.ReadAllBytes(_paths.AuthorizedClientsPath, MaxBytes);
        byte[]? plain = null;
        try
        {
            ProtectedGroupEnvelope envelope;
            try
            {
                envelope = JsonSerializer.Deserialize<ProtectedGroupEnvelope>(bytes)
                    ?? throw new CryptographicException("Estado compartilhado de clientes inválido.");
            }
            catch (JsonException ex)
            {
                throw new CryptographicException("Estado compartilhado de clientes inválido.", ex);
            }

            plain = CandidateBundleStore.Unprotect(key, envelope, AssociatedData);
            List<AuthorizedClientSnapshot> clients;
            try
            {
                clients = JsonSerializer.Deserialize<List<AuthorizedClientSnapshot>>(plain)
                    ?? throw new CryptographicException("Estado compartilhado de clientes inválido.");
            }
            catch (JsonException ex)
            {
                throw new CryptographicException("Estado compartilhado de clientes inválido.", ex);
            }

            foreach (var client in clients)
                ValidateSnapshot(client);
            return clients;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(bytes);
            if (plain is not null) CryptographicOperations.ZeroMemory(plain);
        }
    }

    private void SaveUnlocked(List<AuthorizedClientSnapshot> clients)
    {
        var key = LoadGroupKey();
        var plain = JsonSerializer.SerializeToUtf8Bytes(clients);
        byte[]? bytes = null;
        try
        {
            var envelope = CandidateBundleStore.Protect(key, plain, AssociatedData);
            bytes = JsonSerializer.SerializeToUtf8Bytes(envelope);
            if (bytes.Length > MaxBytes)
                throw new InvalidDataException("Estado compartilhado de clientes excede o limite permitido.");

            var temporary = _paths.AuthorizedClientsTemporaryPath(Guid.NewGuid());
            try
            {
                SharedQueueFileIO.WriteAtomicAsync(
                    temporary,
                    _paths.AuthorizedClientsPath,
                    bytes,
                    MaxBytes,
                    overwrite: true,
                    CancellationToken.None).GetAwaiter().GetResult();
            }
            finally
            {
                TryDelete(temporary);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(plain);
            if (bytes is not null) CryptographicOperations.ZeroMemory(bytes);
        }
    }

    private byte[] LoadGroupKey() =>
        _candidateState.Load()
        ?? throw new InvalidOperationException("Este PC ainda não possui o estado seguro do grupo da fila.");

    private static void ValidateClient(Guid clientId, string clientName, byte[] secret)
    {
        if (clientId == Guid.Empty)
            throw new ArgumentException("Identidade do cliente inválida.", nameof(clientId));
        if (string.IsNullOrWhiteSpace(clientName))
            throw new ArgumentException("Nome do cliente inválido.", nameof(clientName));
        if (secret is null || secret.Length != 32)
            throw new CryptographicException("Segredo do cliente inválido.");
    }

    private static void ValidateSnapshot(AuthorizedClientSnapshot client)
    {
        ArgumentNullException.ThrowIfNull(client);
        ValidateClient(client.ClientId, client.ClientName, client.Secret);
        if (client.LastSequence < 0)
            throw new CryptographicException("Sequência do cliente inválida.");
    }

    private static AuthorizedClientSnapshot Clone(AuthorizedClientSnapshot client) =>
        client with { Secret = client.Secret.ToArray() };

    private static void ZeroClients(IEnumerable<AuthorizedClientSnapshot> clients)
    {
        foreach (var client in clients)
        {
            if (client.Secret is not null)
                CryptographicOperations.ZeroMemory(client.Secret);
        }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }
}
