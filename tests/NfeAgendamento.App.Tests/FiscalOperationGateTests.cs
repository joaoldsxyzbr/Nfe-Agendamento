using NfeAgendamento.App.Fiscal;
using NfeAgendamento.App.SharedQueue;
using Xunit;

namespace NfeAgendamento.App.Tests;

public sealed class FiscalOperationGateTests
{
    [Fact]
    public async Task Gate_rejects_new_operation_when_capacity_is_full()
    {
        var gate = new FiscalOperationGate(maxPendingOperations: 2);
        using var first = await gate.EnterAsync();
        var second = gate.EnterAsync();
        await Task.Delay(50);

        Assert.Equal(2, gate.PendingOperations);
        await Assert.ThrowsAsync<FiscalQueueFullException>(() => gate.EnterAsync());

        first.Dispose();
        using var secondLease = await second.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(1, gate.PendingOperations);
    }

    [Fact]
    public async Task Gate_releases_capacity_when_operation_finishes()
    {
        var gate = new FiscalOperationGate(maxPendingOperations: 1);
        var lease = await gate.EnterAsync();

        Assert.Equal(1, gate.PendingOperations);
        lease.Dispose();

        Assert.Equal(0, gate.PendingOperations);
        using var next = await gate.EnterAsync();
        Assert.Equal(1, gate.PendingOperations);
    }

    [Fact]
    public async Task Shared_gate_serializes_different_process_coordinators_using_same_folder()
    {
        using var temp = new TemporaryDirectory();
        var paths = new SharedQueuePaths(temp.Path);
        paths.InitializeForSharedUse();

        var firstGate = new FiscalOperationGate(paths);
        var secondGate = new FiscalOperationGate(paths);
        var firstLease = await firstGate.EnterAsync();
        var secondTask = secondGate.EnterAsync();

        await Task.Delay(150);
        Assert.False(secondTask.IsCompleted);

        firstLease.Dispose();
        using var secondLease = await secondTask.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(1, secondGate.PendingOperations);
    }

    [Fact]
    public async Task Shared_gate_fails_safe_when_shared_folder_is_unavailable()
    {
        using var temp = new TemporaryDirectory();
        var unavailableRoot = Path.Combine(temp.Path, "missing-share");
        var gate = new FiscalOperationGate(new SharedQueuePaths(unavailableRoot));

        await Assert.ThrowsAsync<FiscalQueueUnavailableException>(() => gate.EnterAsync());
        Assert.Equal(0, gate.PendingOperations);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "nfe-fiscal-gate-tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); } catch { }
        }
    }
}
