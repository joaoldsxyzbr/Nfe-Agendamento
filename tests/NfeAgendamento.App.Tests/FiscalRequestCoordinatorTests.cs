using NfeAgendamento.App.Fiscal;
using Xunit;

namespace NfeAgendamento.App.Tests;

public sealed class FiscalRequestCoordinatorTests
{
    [Fact]
    public async Task Same_access_key_shares_one_in_flight_operation()
    {
        var coordinator = new FiscalRequestCoordinator();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;

        async Task<NfeLookupResult> Operation()
        {
            Interlocked.Increment(ref calls);
            entered.TrySetResult();
            await release.Task;
            return NfeLookupResult.NotFound("não encontrada");
        }

        var first = coordinator.ExecuteAsync("key-1", Operation);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var second = coordinator.ExecuteAsync("key-1", Operation);
        release.TrySetResult();

        var results = await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(1, calls);
        Assert.All(results, result => Assert.Equal(NfeLookupStatus.NotFound, result.Status));
    }

    [Fact]
    public async Task Different_access_keys_can_execute_independently()
    {
        var coordinator = new FiscalRequestCoordinator();
        var bothEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var active = 0;
        var maxActive = 0;

        async Task<NfeLookupResult> Operation()
        {
            var now = Interlocked.Increment(ref active);
            UpdateMaximum(ref maxActive, now);
            if (Volatile.Read(ref active) >= 2)
                bothEntered.TrySetResult();

            await release.Task;
            Interlocked.Decrement(ref active);
            return NfeLookupResult.NotFound("não encontrada");
        }

        var first = coordinator.ExecuteAsync("key-1", Operation);
        var second = coordinator.ExecuteAsync("key-2", Operation);

        await bothEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        release.TrySetResult();
        await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(2));

        Assert.True(maxActive >= 2);
    }

    [Fact]
    public async Task Completed_operation_is_removed_and_can_run_again()
    {
        var coordinator = new FiscalRequestCoordinator();
        var calls = 0;

        Task<NfeLookupResult> Operation()
        {
            Interlocked.Increment(ref calls);
            return Task.FromResult(NfeLookupResult.NotFound("não encontrada"));
        }

        await coordinator.ExecuteAsync("key-1", Operation);
        await coordinator.ExecuteAsync("key-1", Operation);

        Assert.Equal(2, calls);
    }

    private static void UpdateMaximum(ref int target, int candidate)
    {
        int current;
        do
        {
            current = Volatile.Read(ref target);
            if (candidate <= current)
                return;
        }
        while (Interlocked.CompareExchange(ref target, candidate, current) != current);
    }
}
