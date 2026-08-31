using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace NfeAgendamento.App.Fiscal;

public sealed record FiscalCooldownState(DateTimeOffset? BlockedUntilUtc)
{
    public static FiscalCooldownState Empty { get; } = new(null);
}

public sealed class FiscalCooldownStore
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("NfeAgendamento.FiscalCooldown.v1");
    private readonly string _path;
    private readonly SemaphoreSlim _gate = new(1, 1);

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

    public async Task<FiscalCooldownState> ReadAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            return await ReadCoreAsync(cancellationToken);
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
            var state = await ReadCoreAsync(cancellationToken);
            if (state.BlockedUntilUtc is null)
                return;

            var now = nowUtc.ToUniversalTime();
            var blockedUntil = state.BlockedUntilUtc.Value.ToUniversalTime();
            if (now < blockedUntil)
                throw new FiscalCooldownException(blockedUntil);

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
            var current = await ReadCoreAsync(cancellationToken);
            var candidate = receivedAtUtc.ToUniversalTime().AddHours(1);
            var blockedUntil = current.BlockedUntilUtc is { } existing && existing.ToUniversalTime() > candidate
                ? existing.ToUniversalTime()
                : candidate;

            await WriteCoreAsync(new FiscalCooldownState(blockedUntil), cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<FiscalCooldownState> ReadCoreAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_path))
            return FiscalCooldownState.Empty;

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
            ?? throw new InvalidOperationException("Caminho do estado fiscal local inválido.");
        Directory.CreateDirectory(directory);

        var plainBytes = JsonSerializer.SerializeToUtf8Bytes(state);
        var protectedBytes = ProtectedData.Protect(plainBytes, Entropy, DataProtectionScope.CurrentUser);
        var temporary = _path + ".tmp";
        await File.WriteAllBytesAsync(temporary, protectedBytes, cancellationToken);
        File.Move(temporary, _path, overwrite: true);
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

public sealed class FiscalCooldownException : InvalidOperationException
{
    public FiscalCooldownException(DateTimeOffset blockedUntilUtc)
        : base($"Consultas fiscais bloqueadas temporariamente até {blockedUntilUtc:O}.")
    {
        BlockedUntilUtc = blockedUntilUtc;
    }

    public DateTimeOffset BlockedUntilUtc { get; }
}
