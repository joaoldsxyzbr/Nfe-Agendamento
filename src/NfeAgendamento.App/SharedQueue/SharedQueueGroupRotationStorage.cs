using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NfeAgendamento.App.Fiscal;

namespace NfeAgendamento.App.SharedQueue;

public sealed record GroupRotationMarker(
    int Version,
    Guid RotationId,
    Guid RevokedClientId,
    DateTimeOffset CreatedUtc);

public sealed class SharedQueueGroupRotationStorage
{
    private const int MarkerVersion = 1;
    private const int MaxIdentityBytes = 32 * 1024;
    private const int MaxAuthorizedBytes = 256 * 1024;
    private const int MaxCooldownBytes = 16 * 1024;
    private const int MaxMarkerBytes = 16 * 1024;

    private static readonly byte[] IdentityAssociatedData = Encoding.UTF8.GetBytes("nfe-agendamento:group-identity:v1");
    private static readonly byte[] AuthorizedAssociatedData = Encoding.UTF8.GetBytes("nfe-agendamento:authorized-clients:v1");
    private static readonly byte[] CooldownAssociatedData = Encoding.UTF8.GetBytes("nfe-agendamento:fiscal-cooldown:v1");

    private readonly SharedQueuePaths _paths;

    public SharedQueueGroupRotationStorage(SharedQueuePaths paths)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
    }

    public bool HasPending => File.Exists(_paths.RotationMarkerPath);

    public async Task PrepareAsync(
        Guid rotationId,
        byte[] newGroupKey,
        byte[] newPrivateKeyPkcs8,
        IReadOnlyCollection<AuthorizedClientSnapshot> clients,
        FiscalCooldownState cooldown,
        CancellationToken cancellationToken = default)
    {
        if (rotationId == Guid.Empty)
            throw new ArgumentException("Identificador de rotação inválido.", nameof(rotationId));
        CandidateStateStore.ValidateGroupKey(newGroupKey);
        ArgumentNullException.ThrowIfNull(newPrivateKeyPkcs8);
        ArgumentNullException.ThrowIfNull(clients);
        ArgumentNullException.ThrowIfNull(cooldown);

        using (var rsa = RSA.Create())
            rsa.ImportPkcs8PrivateKey(newPrivateKeyPkcs8, out _);

        var snapshots = clients.Select(CloneValidated).ToArray();
        try
        {
            await WriteProtectedAsync(
                _paths.RotationIdentityPreparedPath(rotationId),
                newGroupKey,
                newPrivateKeyPkcs8,
                IdentityAssociatedData,
                MaxIdentityBytes,
                cancellationToken);

            var clientsPlain = JsonSerializer.SerializeToUtf8Bytes(snapshots);
            try
            {
                await WriteProtectedAsync(
                    _paths.RotationAuthorizedPreparedPath(rotationId),
                    newGroupKey,
                    clientsPlain,
                    AuthorizedAssociatedData,
                    MaxAuthorizedBytes,
                    cancellationToken);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(clientsPlain);
            }

            var cooldownPlain = JsonSerializer.SerializeToUtf8Bytes(cooldown);
            try
            {
                await WriteProtectedAsync(
                    _paths.RotationCooldownPreparedPath(rotationId),
                    newGroupKey,
                    cooldownPlain,
                    CooldownAssociatedData,
                    MaxCooldownBytes,
                    cancellationToken);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(cooldownPlain);
            }
        }
        catch
        {
            CleanupPrepared(rotationId);
            throw;
        }
        finally
        {
            ZeroClients(snapshots);
        }
    }

    public byte[] GetPreparedPublicKey(Guid rotationId, byte[] groupKey)
    {
        CandidateStateStore.ValidateGroupKey(groupKey);
        var privateBytes = ReadProtected(
            _paths.RotationIdentityPreparedPath(rotationId),
            groupKey,
            IdentityAssociatedData,
            MaxIdentityBytes);
        try
        {
            using var rsa = RSA.Create();
            rsa.ImportPkcs8PrivateKey(privateBytes, out _);
            return rsa.ExportSubjectPublicKeyInfo();
        }
        finally
        {
            CryptographicOperations.ZeroMemory(privateBytes);
        }
    }

    public void Promote(Guid rotationId)
    {
        var identity = _paths.RotationIdentityPreparedPath(rotationId);
        var authorized = _paths.RotationAuthorizedPreparedPath(rotationId);
        var cooldown = _paths.RotationCooldownPreparedPath(rotationId);

        EnsurePrepared(identity);
        EnsurePrepared(authorized);
        EnsurePrepared(cooldown);

        PromoteOne(authorized, _paths.AuthorizedClientsPath);
        PromoteOne(cooldown, _paths.StatusPath("fiscal-cooldown.bin"));
        PromoteOne(identity, _paths.GroupIdentityPath);
    }

    public async Task WriteMarkerAsync(GroupRotationMarker marker, CancellationToken cancellationToken = default)
    {
        ValidateMarker(marker);
        if (HasPending)
            throw new InvalidOperationException("Já existe uma rotação de confiança pendente.");

        var bytes = JsonSerializer.SerializeToUtf8Bytes(marker);
        try
        {
            await SharedQueueFileIO.WriteAtomicAsync(
                _paths.RotationMarkerTemporaryPath(marker.RotationId),
                _paths.RotationMarkerPath,
                bytes,
                MaxMarkerBytes,
                overwrite: false,
                cancellationToken);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
            TryDelete(_paths.RotationMarkerTemporaryPath(marker.RotationId));
        }
    }

    public GroupRotationMarker? ReadMarker()
    {
        if (!HasPending)
            return null;

        SharedQueueFileIO.EnsureNotReparsePoint(_paths.RotationMarkerPath);
        var bytes = SharedQueueFileIO.ReadAllBytes(_paths.RotationMarkerPath, MaxMarkerBytes);
        try
        {
            var marker = JsonSerializer.Deserialize<GroupRotationMarker>(bytes)
                ?? throw new InvalidDataException("Marcador de rotação inválido.");
            ValidateMarker(marker);
            return marker;
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("Marcador de rotação inválido.", ex);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }

    public void ClearMarker() => TryDelete(_paths.RotationMarkerPath);

    public void CleanupPrepared(Guid rotationId)
    {
        TryDelete(_paths.RotationIdentityPreparedPath(rotationId));
        TryDelete(_paths.RotationAuthorizedPreparedPath(rotationId));
        TryDelete(_paths.RotationCooldownPreparedPath(rotationId));
    }

    public bool PreparedFilesExist(Guid rotationId) =>
        File.Exists(_paths.RotationIdentityPreparedPath(rotationId))
        && File.Exists(_paths.RotationAuthorizedPreparedPath(rotationId))
        && File.Exists(_paths.RotationCooldownPreparedPath(rotationId));

    private static async Task WriteProtectedAsync(
        string target,
        byte[] groupKey,
        byte[] plain,
        byte[] associatedData,
        int maxBytes,
        CancellationToken cancellationToken)
    {
        var envelope = CandidateBundleStore.Protect(groupKey, plain, associatedData);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(envelope);
        var temporary = target + $".{Guid.NewGuid():N}.tmp";
        try
        {
            if (bytes.Length > maxBytes)
                throw new InvalidDataException("Estado preparado excede o limite permitido.");
            await SharedQueueFileIO.WriteAtomicAsync(
                temporary,
                target,
                bytes,
                maxBytes,
                overwrite: true,
                cancellationToken);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
            TryDelete(temporary);
        }
    }

    private static byte[] ReadProtected(
        string path,
        byte[] groupKey,
        byte[] associatedData,
        int maxBytes)
    {
        SharedQueueFileIO.EnsureNotReparsePoint(path);
        var bytes = SharedQueueFileIO.ReadAllBytes(path, maxBytes);
        try
        {
            var envelope = JsonSerializer.Deserialize<ProtectedGroupEnvelope>(bytes)
                ?? throw new CryptographicException("Estado preparado inválido.");
            return CandidateBundleStore.Unprotect(groupKey, envelope, associatedData);
        }
        catch (JsonException ex)
        {
            throw new CryptographicException("Estado preparado inválido.", ex);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }

    private static AuthorizedClientSnapshot CloneValidated(AuthorizedClientSnapshot client)
    {
        ArgumentNullException.ThrowIfNull(client);
        if (client.ClientId == Guid.Empty
            || string.IsNullOrWhiteSpace(client.ClientName)
            || client.Secret is null || client.Secret.Length != 32
            || client.LastSequence < 0)
        {
            throw new CryptographicException("Cliente autorizado inválido para rotação.");
        }
        return client with { Secret = client.Secret.ToArray() };
    }

    private static void ValidateMarker(GroupRotationMarker marker)
    {
        ArgumentNullException.ThrowIfNull(marker);
        if (marker.Version != MarkerVersion
            || marker.RotationId == Guid.Empty
            || marker.RevokedClientId == Guid.Empty
            || marker.CreatedUtc == default)
        {
            throw new InvalidDataException("Marcador de rotação inválido.");
        }
    }

    private static void EnsurePrepared(string path)
    {
        if (!File.Exists(path))
            throw new InvalidOperationException("A rotação preparada está incompleta.");
        SharedQueueFileIO.EnsureNotReparsePoint(path);
    }

    private static void PromoteOne(string prepared, string active)
    {
        SharedQueueFileIO.EnsureNotReparsePoint(prepared);
        if (File.Exists(active))
            SharedQueueFileIO.EnsureNotReparsePoint(active);
        File.Move(prepared, active, overwrite: true);
    }

    private static void ZeroClients(IEnumerable<AuthorizedClientSnapshot> clients)
    {
        foreach (var client in clients)
            if (client.Secret is not null) CryptographicOperations.ZeroMemory(client.Secret);
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
