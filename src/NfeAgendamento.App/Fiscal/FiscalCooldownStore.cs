using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NfeAgendamento.App.SharedQueue;

namespace NfeAgendamento.App.Fiscal;

public sealed record FiscalCooldownState(DateTimeOffset? BlockedUntilUtc)
{
    public static FiscalCooldownState Empty { get; } = new((DateTimeOffset?)null);
}

public sealed class FiscalCooldownStore
{
    private const int MaxSharedBytes = 16 * 1024;
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("NfeAgendamento.FiscalCooldown.v1");
    private static readonly byte[] SharedAssociatedData = Encoding.UTF8.GetBytes("nfe-agendamento:fiscal-cooldown:v1");
    private readonly string _path;
    private readonly CandidateStateStore? _candidateState;
    private readonly bool _sharedMode;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private DateTimeOffset? _volatileBlockedUntilUtc;

    public FiscalCooldownStore()
        : this(Path.Combine(AppPaths.StateRoot, "fiscal-cooldown.bin"))
    {
    }

    public FiscalCooldownStore(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Caminho do estado fiscal inválido.", nameof(path));
        _path = path;
    }

    public FiscalCooldownStore(SharedQueuePaths paths, CandidateStateStore candidateState)
    {
        ArgumentNullException.ThrowIfNull(paths);
        _candidateState = candidateState ?? throw new ArgumentNullException(nameof(candidateState));
        _path = Path.Combine(paths.StatusDirectory, "fiscal-cooldown.bin");
        _sharedMode = true;
    }

    public async Task<FiscalCooldownState> ReadAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var persisted = await ReadCoreAsync(cancellationToken);
            return MergeWithVolatile(persisted);
        }
        finally
        {
            _gate.Release();
        }
    }

    public Task EnsureAllowedAsync(CancellationToken cancellationToken = default) =>
        EnsureAllowedAsync(DateTimeOffset.UtcNow, cancellationToken);

    public async Task EnsureAllowedAsync(DateTimeOffset nowUtc, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var state = MergeWithVolatile(await ReadCoreAsync(cancellationToken));
            if (state.BlockedUntilUtc is null)
                return;

            var now = nowUtc.ToUniversalTime();
            var blockedUntil = state.BlockedUntilUtc.Value.ToUniversalTime();
            if (now < blockedUntil)
                throw new FiscalCooldownException(blockedUntil);

            _volatileBlockedUntilUtc = null;
            TryDelete(_path);
        }
        finally
        {
            _gate.Release();
        }
    }

    public Task BlockFor656Async(CancellationToken cancellationToken = default) =>
        BlockFor656Async(DateTimeOffset.UtcNow, cancellationToken);

    public async Task BlockFor656Async(DateTimeOffset receivedAtUtc, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var current = MergeWithVolatile(await ReadCoreAsync(cancellationToken));
            var candidate = receivedAtUtc.ToUniversalTime().AddHours(1);
            var blockedUntil = current.BlockedUntilUtc is { } existing && existing.ToUniversalTime() > candidate
                ? existing.ToUniversalTime()
                : candidate;

            _volatileBlockedUntilUtc = blockedUntil;

            try
            {
                await WriteCoreAsync(new FiscalCooldownState(blockedUntil), cancellationToken);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Fail-safe: o processo atual continua bloqueado mesmo se a persistência compartilhada falhar.
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private FiscalCooldownState MergeWithVolatile(FiscalCooldownState persisted)
    {
        if (_volatileBlockedUntilUtc is not { } volatileUntil)
            return persisted;
        if (persisted.BlockedUntilUtc is { } persistedUntil && persistedUntil.ToUniversalTime() > volatileUntil.ToUniversalTime())
            return persisted;
        return new FiscalCooldownState(volatileUntil.ToUniversalTime());
    }

    private async Task<FiscalCooldownState> ReadCoreAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_path))
            return FiscalCooldownState.Empty;

        if (_sharedMode)
        {
            var groupKey = _candidateState!.Load()
                ?? throw new InvalidOperationException("Este PC não possui o estado seguro do grupo para validar o cooldown fiscal.");
            byte[]? bytes = null;
            byte[]? plain = null;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                SharedQueueFileIO.EnsureNotReparsePoint(_path);
                bytes = SharedQueueFileIO.ReadAllBytes(_path, MaxSharedBytes);
                ProtectedGroupEnvelope envelope;
                try
                {
                    envelope = JsonSerializer.Deserialize<ProtectedGroupEnvelope>(bytes)
                        ?? throw new CryptographicException("Estado fiscal compartilhado inválido.");
                }
                catch (JsonException ex)
                {
                    throw new CryptographicException("Estado fiscal compartilhado inválido.", ex);
                }
                plain = CandidateBundleStore.Unprotect(groupKey, envelope, SharedAssociatedData);
                return JsonSerializer.Deserialize<FiscalCooldownState>(plain)
                    ?? throw new InvalidDataException("Estado fiscal compartilhado inválido.");
            }
            catch (Exception ex) when (ex is CryptographicException or JsonException)
            {
                throw new InvalidDataException("O estado fiscal compartilhado não pôde ser validado com segurança.", ex);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(groupKey);
                if (bytes is not null) CryptographicOperations.ZeroMemory(bytes);
                if (plain is not null) CryptographicOperations.ZeroMemory(plain);
            }
        }

        try
        {
            var protectedBytes = await File.ReadAllBytesAsync(_path, cancellationToken);
            var plainBytes = ProtectedData.Unprotect(protectedBytes, Entropy, DataProtectionScope.CurrentUser);
            return JsonSerializer.Deserialize<FiscalCooldownState>(plainBytes)
                ?? throw new InvalidDataException("Estado fiscal local inválido.");
        }
        catch (Exception ex) when (ex is CryptographicException or JsonException)
        {
            throw new InvalidDataException("O estado fiscal local não pôde ser validado com segurança.", ex);
        }
    }

    private async Task WriteCoreAsync(FiscalCooldownState state, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(_path)
            ?? throw new InvalidOperationException("Caminho do estado fiscal inválido.");
        Directory.CreateDirectory(directory);

        if (_sharedMode)
        {
            var groupKey = _candidateState!.Load()
                ?? throw new InvalidOperationException("Este PC não possui o estado seguro do grupo para persistir o cooldown fiscal.");
            var plain = JsonSerializer.SerializeToUtf8Bytes(state);
            byte[]? bytes = null;
            try
            {
                var envelope = CandidateBundleStore.Protect(groupKey, plain, SharedAssociatedData);
                bytes = JsonSerializer.SerializeToUtf8Bytes(envelope);
                var temporary = _path + $".{Guid.NewGuid():N}.tmp";
                try
                {
                    await SharedQueueFileIO.WriteAtomicAsync(temporary, _path, bytes, MaxSharedBytes, true, cancellationToken);
                }
                finally
                {
                    TryDelete(temporary);
                }
            }
            finally
            {
                CryptographicOperations.ZeroMemory(groupKey);
                CryptographicOperations.ZeroMemory(plain);
                if (bytes is not null) CryptographicOperations.ZeroMemory(bytes);
            }
            return;
        }

        var plainBytes = JsonSerializer.SerializeToUtf8Bytes(state);
        var protectedBytes = ProtectedData.Protect(plainBytes, Entropy, DataProtectionScope.CurrentUser);
        try
        {
            var temporary = _path + ".tmp";
            await File.WriteAllBytesAsync(temporary, protectedBytes, cancellationToken);
            File.Move(temporary, _path, overwrite: true);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plainBytes);
            CryptographicOperations.ZeroMemory(protectedBytes);
        }
    }

    private static void TryDelete(string path)
    {
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
}

public sealed class FiscalCooldownException : InvalidOperationException
{
    public FiscalCooldownException(DateTimeOffset blockedUntilUtc)
        : base($"Consultas fiscais bloqueadas temporariamente até {blockedUntilUtc:O}.")
    {
        BlockedUntilUtc = blockedUntilUtc;
    }

    public DateTimeOffset BlockedUntilUtc { get; }
}
