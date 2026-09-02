using NfeAgendamento.App.SharedQueue;
using Xunit;

namespace NfeAgendamento.App.Tests;

public sealed class SharedQueueCentralLeaseTests
{
    [Fact]
    public void Only_one_central_can_hold_the_share_lock()
    {
        var root = Path.Combine(Path.GetTempPath(), "nfe-agendamento-lease-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var paths = new SharedQueuePaths(root);
            paths.InitializeAsCentral();

            using var first = SharedQueueCentralLease.TryAcquire(paths);
            using var second = SharedQueueCentralLease.TryAcquire(paths);

            Assert.NotNull(first);
            Assert.Null(second);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Lease_can_be_acquired_again_after_release()
    {
        var root = Path.Combine(Path.GetTempPath(), "nfe-agendamento-lease-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var paths = new SharedQueuePaths(root);
            paths.InitializeAsCentral();

            var first = SharedQueueCentralLease.TryAcquire(paths);
            Assert.NotNull(first);
            first.Dispose();

            using var second = SharedQueueCentralLease.TryAcquire(paths);
            Assert.NotNull(second);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
