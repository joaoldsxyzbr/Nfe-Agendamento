using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using NfeAgendamento.App;
using NfeAgendamento.App.Fiscal;
using NfeAgendamento.App.SharedQueue;
using Xunit;

namespace NfeAgendamento.App.Tests;

public sealed class SharedQueueSecurityRegressionTests
{
    private const string AccessKey = "42260912345678000123550010000000011000000015";

    [Fact]
    public void Shared_protocol_requires_pairing_and_authenticated_client_identity()
    {
        Assert.NotNull(typeof(SharedQueuePaths).GetProperty("PairingDirectory"));
        Assert.NotNull(typeof(QueueRequestEnvelope).GetProperty("ClientId"));
        Assert.NotNull(typeof(QueueRequestEnvelope).GetProperty("Sequence"));
        Assert.NotNull(typeof(QueueRequestEnvelope).GetProperty("ClientAuthTag"));
        Assert.NotNull(typeof(SharedQueueClientStatus).GetProperty("IsPaired"));
    }

    [Fact]
    public void Local_interface_exposes_one_time_pairing_flow()
    {
        var program = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Program.cs"));
        var index = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "index.html"));
        var pairing = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "pairing.js"));

        Assert.Contains("/api/pairing/code", program, StringComparison.Ordinal);
        Assert.Contains("/api/pairing/client", program, StringComparison.Ordinal);
        Assert.Contains("id=\"generatePairingCode\"", index, StringComparison.Ordinal);
        Assert.Contains("id=\"pairingCode\"", index, StringComparison.Ordinal);
        Assert.Contains("id=\"pairClient\"", index, StringComparison.Ordinal);
        Assert.Contains("/pairing.js", index, StringComparison.Ordinal);
        Assert.Contains("generatePairingCode", pairing, StringComparison.Ordinal);
        Assert.Contains("pairClient", pairing, StringComparison.Ordinal);
        Assert.Contains("clientPaired", pairing, StringComparison.Ordinal);
    }

    [Fact]
    public void Legacy_central_enabled_aliases_are_removed()
    {
        Assert.Null(typeof(CentralStateService).GetProperty("IsEnabled"));
        Assert.Null(typeof(CentralStateService).GetMethod("SetEnabled"));
    }

    [Fact]
    public async Task Stopping_active_central_removes_its_heartbeat_immediately()
    {
        var root = NewRoot();
        var share = Path.Combine(root, "share");
        Directory.CreateDirectory(share);
        try
        {
            var state = new CentralStateService(new CentralSettingsStore(Path.Combine(root, "central.json")));
            state.SetConfiguredAsCentral(true);
            var paths = new SharedQueuePaths(share);
            using var runtime = new SharedQueueCentralService(state, paths, new CentralKeyStore(Path.Combine(root, "central.key")));

            await runtime.TryActivateOnceAsync();
            Assert.True(File.Exists(paths.StatusPath("heartbeat.json")));

            state.SetConfiguredAsCentral(false);

            Assert.False(File.Exists(paths.StatusPath("heartbeat.json")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Configured_but_inactive_central_never_calls_sefaz_directly()
    {
        var root = NewRoot();
        try
        {
            var share = Path.Combine(root, "missing-share");
            var state = new CentralStateService(new CentralSettingsStore(Path.Combine(root, "central.json")));
            state.SetConfiguredAsCentral(true);
            var paths = new SharedQueuePaths(share);
            using var runtime = new SharedQueueCentralService(state, paths, new CentralKeyStore(Path.Combine(root, "central.key")));
            await runtime.TryActivateOnceAsync();
            Assert.False(runtime.IsActive);

            var client = new SharedQueueClient(paths, new PendingRequestSecretStore(Path.Combine(root, "pending")));
            var services = new ServiceCollection()
                .AddSingleton(runtime)
                .BuildServiceProvider();
            var dispatch = new LookupDispatchService(services, state, client);

            var result = await dispatch.LookupAsync(AccessKey);

            Assert.Equal(NfeLookupStatus.Failed, result.Status);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Oversized_heartbeat_is_rejected_before_being_trusted()
    {
        var root = NewRoot();
        try
        {
            var share = Path.Combine(root, "share");
            Directory.CreateDirectory(share);
            var paths = new SharedQueuePaths(share);
            paths.InitializeAsCentral();
            var heartbeat = new QueueHeartbeat(
                SharedQueueCrypto.ProtocolVersion,
                "CA03",
                DateTimeOffset.UtcNow,
                Convert.ToBase64String(new byte[294]),
                "test");
            var json = JsonSerializer.Serialize(heartbeat) + new string(' ', 128 * 1024);
            File.WriteAllText(paths.StatusPath("heartbeat.json"), json);

            var client = new SharedQueueClient(paths, new PendingRequestSecretStore(Path.Combine(root, "pending")));
            var status = client.GetStatus();

            Assert.False(status.CentralOnline);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Oversized_request_never_reaches_fiscal_service()
    {
        var root = NewRoot();
        try
        {
            var share = Path.Combine(root, "share");
            Directory.CreateDirectory(share);
            var paths = new SharedQueuePaths(share);
            paths.InitializeAsCentral();
            var keyStore = new CentralKeyStore(Path.Combine(root, "central.key"));
            var material = SharedQueueCrypto.CreateClientRequest(Guid.NewGuid(), AccessKey, keyStore.GetOrCreatePublicKey());
            var bytes = JsonSerializer.SerializeToUtf8Bytes(material.Envelope);
            await using (var stream = new FileStream(paths.RequestPath(material.Envelope.RequestId), FileMode.CreateNew, FileAccess.Write))
            {
                await stream.WriteAsync(bytes);
                await stream.WriteAsync(new byte[128 * 1024]);
            }

            var calls = 0;
            var processor = new SharedQueueProcessor(paths, keyStore, (_, _) =>
            {
                Interlocked.Increment(ref calls);
                return Task.FromResult(new NfeLookupResult(NfeLookupStatus.Found, "<xml/>", "138", "ok", false));
            });

            await processor.ProcessOneAsync();

            Assert.Equal(0, calls);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Reparse_point_inside_operational_tree_is_rejected()
    {
        var root = NewRoot();
        var outside = NewRoot();
        try
        {
            var share = Path.Combine(root, "share");
            Directory.CreateDirectory(share);
            var paths = new SharedQueuePaths(share);
            paths.InitializeAsCentral();

            Directory.Delete(paths.QueueDirectory);
            CreateJunction(paths.QueueDirectory, outside);

            Assert.False(paths.ValidateForClient());
        }
        finally
        {
            TryDeleteLink(Path.Combine(root, "share", "fila"));
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
            if (Directory.Exists(outside)) Directory.Delete(outside, recursive: true);
        }
    }

    [Fact]
    public void Startup_menu_does_not_use_recursive_checked_changed_rollback()
    {
        var source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "TrayApplicationContext.cs"));

        Assert.DoesNotContain("startup.CheckedChanged +=", source, StringComparison.Ordinal);
        Assert.Contains("CheckOnClick = false", source, StringComparison.Ordinal);
    }

    private static void CreateJunction(string junction, string target)
    {
        var start = new ProcessStartInfo("cmd.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true
        };
        start.ArgumentList.Add("/c");
        start.ArgumentList.Add("mklink");
        start.ArgumentList.Add("/J");
        start.ArgumentList.Add(junction);
        start.ArgumentList.Add(target);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Não foi possível criar junction de teste.");
        process.WaitForExit();
        Assert.Equal(0, process.ExitCode);
    }

    private static void TryDeleteLink(string path)
    {
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path);
        }
        catch
        {
        }
    }

    private static string NewRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "nfe-agendamento-security-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }
}
