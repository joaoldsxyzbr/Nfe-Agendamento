using System.Security.Cryptography;
using NfeAgendamento.App.SharedQueue;
using Xunit;

namespace NfeAgendamento.App.Tests;

public sealed class PairingRobustnessTests
{
    [Fact]
    public async Task Group_pairing_round_trip_imports_the_same_shared_identity()
    {
        using var temp = new TempDirectory();
        var share = Path.Combine(temp.Path, "share");
        Directory.CreateDirectory(share);
        var paths = new SharedQueuePaths(share);
        paths.InitializeAsCentral();

        var groupKey = RandomNumberGenerator.GetBytes(32);
        using var rsa = RSA.Create(2048);
        var privateKey = rsa.ExportPkcs8PrivateKey();
        var groupIdentity = new SharedGroupIdentityStore(paths);
        groupIdentity.Initialize(groupKey, privateKey);

        var leaderCandidate = new CandidateStateStore(Path.Combine(temp.Path, "leader-candidate.bin"));
        leaderCandidate.Save(groupKey);
        var leaderKeys = new CentralKeyStore(leaderCandidate, groupIdentity);
        var authorized = new SharedAuthorizedClientStore(paths, leaderCandidate);
        var codes = new PairingCodeService();
        var processor = new SharedQueueGroupPairingProcessor(
            paths,
            codes,
            authorized,
            leaderKeys,
            leaderCandidate,
            new CandidateBundleStore(paths));

        var clientPairing = new ClientPairingStore(Path.Combine(temp.Path, "client-pairing.bin"));
        var client = new SharedQueuePairingClient(
            paths,
            clientPairing,
            TimeSpan.FromMilliseconds(20),
            TimeSpan.FromSeconds(3));
        var code = codes.Generate();

        var pairingTask = client.PairAsync(code.Code);
        await WaitForPairingRequestAsync(paths.PairingDirectory);
        Assert.True(await processor.ProcessOneAsync());
        var result = await pairingTask;
        Assert.True(result.Success, result.Message);

        var clientCandidate = new CandidateStateStore(Path.Combine(temp.Path, "client-candidate.bin"));
        var clientBootstrap = new SharedQueueGroupBootstrapService(
            paths,
            new CentralStateService(new CentralSettingsStore(Path.Combine(temp.Path, "client-central.json"))),
            new CentralKeyStore(Path.Combine(temp.Path, "client-local-key.bin")),
            new AuthorizedClientStore(Path.Combine(temp.Path, "client-authorized.bin")),
            clientPairing,
            clientCandidate);

        Assert.True(clientBootstrap.TryImportCandidateBundle());
        Assert.True(clientCandidate.IsReady);

        var paired = clientPairing.Load();
        Assert.NotNull(paired);
        var expectedPublic = groupIdentity.GetPublicKey(groupKey);
        Assert.Equal(expectedPublic, paired!.CentralPublicKey);

        CryptographicOperations.ZeroMemory(groupKey);
        CryptographicOperations.ZeroMemory(privateKey);
        CryptographicOperations.ZeroMemory(expectedPublic);
    }

    [Fact]
    public async Task Pairing_code_is_consumed_after_first_successful_authorization()
    {
        using var temp = new TempDirectory();
        var share = Path.Combine(temp.Path, "share");
        Directory.CreateDirectory(share);
        var paths = new SharedQueuePaths(share);
        paths.InitializeAsCentral();

        var groupKey = RandomNumberGenerator.GetBytes(32);
        using var rsa = RSA.Create(2048);
        var privateKey = rsa.ExportPkcs8PrivateKey();
        var groupIdentity = new SharedGroupIdentityStore(paths);
        groupIdentity.Initialize(groupKey, privateKey);
        var leaderCandidate = new CandidateStateStore(Path.Combine(temp.Path, "leader-candidate.bin"));
        leaderCandidate.Save(groupKey);
        var codes = new PairingCodeService();
        var processor = new SharedQueueGroupPairingProcessor(
            paths,
            codes,
            new SharedAuthorizedClientStore(paths, leaderCandidate),
            new CentralKeyStore(leaderCandidate, groupIdentity),
            leaderCandidate,
            new CandidateBundleStore(paths));
        var code = codes.Generate();

        var first = new SharedQueuePairingClient(
            paths,
            new ClientPairingStore(Path.Combine(temp.Path, "client-1.bin")),
            TimeSpan.FromMilliseconds(20),
            TimeSpan.FromSeconds(2));
        var firstTask = first.PairAsync(code.Code);
        await WaitForPairingRequestAsync(paths.PairingDirectory);
        Assert.True(await processor.ProcessOneAsync());
        Assert.True((await firstTask).Success);

        using var cts = new CancellationTokenSource();
        var second = new SharedQueuePairingClient(
            paths,
            new ClientPairingStore(Path.Combine(temp.Path, "client-2.bin")),
            TimeSpan.FromMilliseconds(20),
            TimeSpan.FromSeconds(2));
        var secondTask = second.PairAsync(code.Code, cts.Token);
        await WaitForPairingRequestAsync(paths.PairingDirectory);

        try
        {
            Assert.False(await processor.ProcessOneAsync());
        }
        finally
        {
            cts.Cancel();
            try { await secondTask; } catch (OperationCanceledException) { }
            CryptographicOperations.ZeroMemory(groupKey);
            CryptographicOperations.ZeroMemory(privateKey);
        }
    }

    [Fact]
    public async Task Leader_without_group_state_does_not_consume_pairing_request()
    {
        using var temp = new TempDirectory();
        var share = Path.Combine(temp.Path, "share");
        Directory.CreateDirectory(share);
        var paths = new SharedQueuePaths(share);
        paths.InitializeAsCentral();

        var missingCandidate = new CandidateStateStore(Path.Combine(temp.Path, "missing-candidate.bin"));
        var codes = new PairingCodeService();
        var processor = new SharedQueueGroupPairingProcessor(
            paths,
            codes,
            new SharedAuthorizedClientStore(paths, missingCandidate),
            new CentralKeyStore(Path.Combine(temp.Path, "leader-local-key.bin")),
            missingCandidate,
            new CandidateBundleStore(paths));

        var client = new SharedQueuePairingClient(
            paths,
            new ClientPairingStore(Path.Combine(temp.Path, "client-pairing.bin")),
            TimeSpan.FromMilliseconds(20),
            TimeSpan.FromSeconds(3));
        var code = codes.Generate();
        using var cts = new CancellationTokenSource();

        var pairingTask = client.PairAsync(code.Code, cts.Token);
        var requestPath = await WaitForPairingRequestAsync(paths.PairingDirectory);

        Assert.False(await processor.ProcessOneAsync());
        Assert.True(File.Exists(requestPath));

        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await pairingTask);
    }

    private static async Task<string> WaitForPairingRequestAsync(string directory)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(2);
        while (DateTimeOffset.UtcNow < deadline)
        {
            var path = Directory.EnumerateFiles(directory, "*.pair.req", SearchOption.TopDirectoryOnly).SingleOrDefault();
            if (path is not null)
                return path;
            await Task.Delay(20);
        }

        throw new TimeoutException("Pedido de pareamento não apareceu no compartilhamento.");
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "nfe-agendamento-pairing-robustness", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); } catch { }
        }
    }
}
