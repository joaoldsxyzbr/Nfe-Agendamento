using NfeAgendamento.App.Fiscal;
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
}
