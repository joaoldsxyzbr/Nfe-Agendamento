using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace NfeAgendamento.App.SharedQueue;

public sealed class CandidateIdentityTransitionStore
{
    private const int MaxBytes = 128 * 1024;
    private const int MaxTransitions = 128;
    private static readonly byte[] KeyContext = Encoding.UTF8.GetBytes("nfe-agendamento:candidate-identity-transitions:v1");
    private static readonly byte[] AssociatedDataPrefix = Encoding.UTF8.GetBytes("nfe-agendamento:candidate-identity-transitions:v1:");
    private readonly SharedQueuePaths _paths;

    public CandidateIdentityTransitionStore(SharedQueuePaths paths)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
    }

    public async Task WriteAsync(
        Guid clientId,
        byte[] clientSecret,
        IReadOnlyList<GroupIdentityTransition> transitions,
        CancellationToken cancellationToken = default)
    {
        ValidateClient(clientId, clientSecret);
        ArgumentNullException.ThrowIfNull(transitions);
        if (transitions.Count > MaxTransitions)
            throw new InvalidDataException("A cadeia de rotação excede o limite permitido; este PC precisa ser pareado novamente.");

        foreach (var transition in transitions)
            GroupRotationProof.ValidateTransition(transition);

        var key = HMACSHA256.HashData(clientSecret, KeyContext);
        var plain = JsonSerializer.SerializeToUtf8Bytes(transitions);
        byte[]? bytes = null;
        try
        {
            var envelope = CandidateBundleStore.Protect(key, plain, AssociatedData(clientId));
            bytes = JsonSerializer.SerializeToUtf8Bytes(envelope);
            if (bytes.Length > MaxBytes)
                throw new InvalidDataException("A cadeia de rotação excede o limite permitido.");

            var temporary = _paths.CandidateTransitionTemporaryPath(clientId, Guid.NewGuid());
            try
            {
                await SharedQueueFileIO.WriteAtomicAsync(
                    temporary,
                    _paths.CandidateTransitionPath(clientId),
                    bytes,
                    MaxBytes,
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
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(plain);
            if (bytes is not null) CryptographicOperations.ZeroMemory(bytes);
        }
    }

    public IReadOnlyList<GroupIdentityTransition> Read(Guid clientId, byte[] clientSecret)
    {
        ValidateClient(clientId, clientSecret);
        var path = _paths.CandidateTransitionPath(clientId);
        if (!File.Exists(path))
            return Array.Empty<GroupIdentityTransition>();

        var key = HMACSHA256.HashData(clientSecret, KeyContext);
        var bytes = SharedQueueFileIO.ReadAllBytes(path, MaxBytes);
        byte[]? plain = null;
        try
        {
            var envelope = JsonSerializer.Deserialize<ProtectedGroupEnvelope>(bytes)
                ?? throw new CryptographicException("Cadeia de rotação inválida.");
            plain = CandidateBundleStore.Unprotect(key, envelope, AssociatedData(clientId));
            var transitions = JsonSerializer.Deserialize<GroupIdentityTransition[]>(plain)
                ?? throw new CryptographicException("Cadeia de rotação inválida.");
            if (transitions.Length > MaxTransitions)
                throw new CryptographicException("Cadeia de rotação excede o limite permitido.");
            foreach (var transition in transitions)
                GroupRotationProof.ValidateTransition(transition);
            return transitions.Select(Clone).ToArray();
        }
        catch (JsonException ex)
        {
            throw new CryptographicException("Cadeia de rotação inválida.", ex);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(bytes);
            if (plain is not null) CryptographicOperations.ZeroMemory(plain);
        }
    }

    public void Delete(Guid clientId) => TryDelete(_paths.CandidateTransitionPath(clientId));

    public static void Zero(IEnumerable<GroupIdentityTransition> transitions)
    {
        foreach (var transition in transitions)
        {
            if (transition.PreviousPublicKeySha256 is not null)
                CryptographicOperations.ZeroMemory(transition.PreviousPublicKeySha256);
            if (transition.NewPublicKey is not null)
                CryptographicOperations.ZeroMemory(transition.NewPublicKey);
            if (transition.Signature is not null)
                CryptographicOperations.ZeroMemory(transition.Signature);
        }
    }

    private static GroupIdentityTransition Clone(GroupIdentityTransition transition) =>
        transition with
        {
            PreviousPublicKeySha256 = transition.PreviousPublicKeySha256.ToArray(),
            NewPublicKey = transition.NewPublicKey.ToArray(),
            Signature = transition.Signature.ToArray()
        };

    private static void ValidateClient(Guid clientId, byte[] clientSecret)
    {
        if (clientId == Guid.Empty)
            throw new ArgumentException("Cliente inválido.", nameof(clientId));
        if (clientSecret is null || clientSecret.Length != 32)
            throw new CryptographicException("Segredo do cliente inválido.");
    }

    private static byte[] AssociatedData(Guid clientId)
    {
        var id = Encoding.ASCII.GetBytes(clientId.ToString("N"));
        var data = new byte[AssociatedDataPrefix.Length + id.Length];
        Buffer.BlockCopy(AssociatedDataPrefix, 0, data, 0, AssociatedDataPrefix.Length);
        Buffer.BlockCopy(id, 0, data, AssociatedDataPrefix.Length, id.Length);
        return data;
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
