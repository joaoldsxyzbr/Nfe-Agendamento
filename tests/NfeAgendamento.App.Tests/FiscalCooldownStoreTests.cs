using NfeAgendamento.App.Fiscal;
using Xunit;

namespace NfeAgendamento.App.Tests;

public sealed class FiscalCooldownStoreTests
{
    [Fact]
    public async Task BlockFor656_persists_one_hour_across_instances()
    {
        var path = TempFile();
        var now = DateTimeOffset.Parse("2026-08-31T12:00:00Z");
        var first = new FiscalCooldownStore(path);

        await first.BlockFor656Async(now);

        var second = new FiscalCooldownStore(path);
        var state = await second.ReadAsync();
        Assert.Equal(now.AddHours(1), state.BlockedUntilUtc);
    }

    [Fact]
    public async Task EnsureAllowed_throws_before_block_expires_and_allows_afterwards()
    {
        var path = TempFile();
        var now = DateTimeOffset.Parse("2026-08-31T12:00:00Z");
        var store = new FiscalCooldownStore(path);
        await store.BlockFor656Async(now);

        var exception = await Assert.ThrowsAsync<FiscalCooldownException>(() =>
            store.EnsureAllowedAsync(now.AddMinutes(59)));
        Assert.Equal(now.AddHours(1), exception.BlockedUntilUtc);

        await store.EnsureAllowedAsync(now.AddHours(1).AddSeconds(1));
    }

    [Fact]
    public async Task Later_656_extends_existing_block()
    {
        var path = TempFile();
        var now = DateTimeOffset.Parse("2026-08-31T12:00:00Z");
        var store = new FiscalCooldownStore(path);

        await store.BlockFor656Async(now);
        await store.BlockFor656Async(now.AddMinutes(20));

        var state = await store.ReadAsync();
        Assert.Equal(now.AddMinutes(80), state.BlockedUntilUtc);
    }

    private static string TempFile()
    {
        var directory = Path.Combine(Path.GetTempPath(), "NfeAgendamento.Tests", Guid.NewGuid().ToString("N"));
        return Path.Combine(directory, "cooldown.bin");
    }
}
