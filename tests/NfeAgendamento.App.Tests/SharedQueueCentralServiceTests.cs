using System.Text.Json;
using NfeAgendamento.App.SharedQueue;
using Xunit;

namespace NfeAgendamento.App.Tests;

public sealed class SharedQueueCentralServiceTests
{
    [Fact]
    public async Task Configured_central_acquires_lock_and_publishes_heartbeat()
    {
        var root = NewRoot();
        var share = Path.Combine(root, "share");
        Directory.CreateDirectory(share);
        try
        {
            var state = CreateState(root, "one");
            state.SetConfiguredAsCentral(true);
            var paths = new SharedQueuePaths(share);
            using var service = new SharedQueueCentralService(state, paths, new CentralKeyStore(Path.Combine(root, "one.key")));

            await service.TryActivateOnceAsync();

            Assert.True(service.IsActive);
            Assert.Equal(CentralRuntimeStatus.Active, service.Status);
            Assert.True(File.Exists(paths.StatusPath("heartbeat.json")));
            var heartbeat = JsonSerializer.Deserialize<QueueHeartbeat>(await File.ReadAllTextAsync(paths.StatusPath("heartbeat.json")));
            Assert.NotNull(heartbeat);
            Assert.Equal(SharedQueueCrypto.ProtocolVersion, heartbeat.Version);
            Assert.False(string.IsNullOrWhiteSpace(heartbeat.PublicKeyBase64));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Client_does_not_initialize_or_lock_share()
    {
        var root = NewRoot();
        var share = Path.Combine(root, "share");
        Directory.CreateDirectory(share);
        try
        {
            var state = CreateState(root, "client");
            var paths = new SharedQueuePaths(share);
            using var service = new SharedQueueCentralService(state, paths, new CentralKeyStore(Path.Combine(root, "client.key")));

            await service.TryActivateOnceAsync();

            Assert.False(service.IsActive);
            Assert.Equal(CentralRuntimeStatus.Client, service.Status);
            Assert.False(File.Exists(paths.MarkerPath));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Missing_share_is_reported_without_creating_root()
    {
        var root = NewRoot();
        try
        {
            var share = Path.Combine(root, "missing-share");
            var state = CreateState(root, "one");
            state.SetConfiguredAsCentral(true);
            using var service = new SharedQueueCentralService(state, new SharedQueuePaths(share), new CentralKeyStore(Path.Combine(root, "one.key")));

            await service.TryActivateOnceAsync();

            Assert.Equal(CentralRuntimeStatus.ShareUnavailable, service.Status);
            Assert.False(Directory.Exists(share));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Second_central_is_blocked_until_first_stops()
    {
        var root = NewRoot();
        var share = Path.Combine(root, "share");
        Directory.CreateDirectory(share);
        try
        {
            var stateOne = CreateState(root, "one");
            var stateTwo = CreateState(root, "two");
            stateOne.SetConfiguredAsCentral(true);
            stateTwo.SetConfiguredAsCentral(true);
            var paths = new SharedQueuePaths(share);
            using var first = new SharedQueueCentralService(stateOne, paths, new CentralKeyStore(Path.Combine(root, "one.key")));
            using var second = new SharedQueueCentralService(stateTwo, paths, new CentralKeyStore(Path.Combine(root, "two.key")));

            await first.TryActivateOnceAsync();
            await second.TryActivateOnceAsync();

            Assert.True(first.IsActive);
            Assert.Equal(CentralRuntimeStatus.Conflict, second.Status);

            stateOne.SetConfiguredAsCentral(false);
            await second.TryActivateOnceAsync();
            Assert.True(second.IsActive);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static CentralStateService CreateState(string root, string name) =>
        new(new CentralSettingsStore(Path.Combine(root, $"central-{name}.json")));

    private static string NewRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "nfe-agendamento-central-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }
}
