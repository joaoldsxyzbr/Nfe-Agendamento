using System.Security.Cryptography;
using NfeAgendamento.App.Fiscal;
using NfeAgendamento.App.SharedQueue;
using Xunit;

namespace NfeAgendamento.App.Tests;

public sealed class SharedFiscalCooldownTests
{
    [Fact]
    public async Task New_leader_observes_existing_656_block()
    {
        var root = Path.Combine(Path.GetTempPath(), "nfe-cooldown-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var paths = new SharedQueuePaths(root);
            paths.InitializeAsCentral();
            var key = RandomNumberGenerator.GetBytes(32);
            var firstState = new CandidateStateStore(Path.Combine(root, "first.bin"));
            var secondState = new CandidateStateStore(Path.Combine(root, "second.bin"));
            firstState.Save(key);
            secondState.Save(key);
            var first = new FiscalCooldownStore(paths, firstState);
            var second = new FiscalCooldownStore(paths, secondState);
            var now = DateTimeOffset.UtcNow;

            await first.BlockFor656Async(now);
            var ex = await Assert.ThrowsAsync<FiscalCooldownException>(() => second.EnsureAllowedAsync(now.AddMinutes(10)));

            Assert.Equal(now.AddHours(1), ex.BlockedUntilUtc);
            CryptographicOperations.ZeroMemory(key);
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }
    }
}
