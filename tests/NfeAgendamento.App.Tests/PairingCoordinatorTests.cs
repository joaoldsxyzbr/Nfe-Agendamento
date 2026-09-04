using System.Security.Cryptography;
using NfeAgendamento.App.SharedQueue;
using Xunit;

namespace NfeAgendamento.App.Tests;

public sealed class PairingCoordinatorTests
{
    [Fact]
    public async Task Failed_group_import_clears_half_paired_local_state()
    {
        using var fixture = PairingFixture.Create(useSharedLeaderIdentity: false);
        var code = fixture.Codes.Generate();

        var pairingTask = fixture.Coordinator.PairAsync(code.Code);
        await fixture.WaitForPairingRequestAsync();
        Assert.True(await fixture.Processor.ProcessOneAsync());

        var result = await pairingTask;

        Assert.False(result.Success);
        Assert.Null(fixture.ClientPairing.Load());
        Assert.False(fixture.ClientCandidate.IsReady);
    }

    [Fact]
    public async Task Duplicate_local_pairing_submissions_collapse_to_one_authorization()
    {
        using var fixture = PairingFixture.Create(useSharedLeaderIdentity: true);
        var code = fixture.Codes.Generate();

        var first = fixture.Coordinator.PairAsync(code.Code);
        var second = fixture.Coordinator.PairAsync(code.Code);
        await fixture.WaitForPairingRequestAsync();
        Assert.True(await fixture.Processor.ProcessOneAsync());

        var firstResult = await first;
        var secondResult = await second;

        Assert.True(firstResult.Success, firstResult.Message);
        Assert.True(secondResult.Success, secondResult.Message);
        Assert.True(fixture.ClientCandidate.IsReady);
        Assert.Equal(1, fixture.Authorized.Count);
        Assert.Single(Directory.EnumerateFiles(fixture.Paths.PairingDirectory, "*.pair.res", SearchOption.TopDirectoryOnly));
    }

    private sealed class PairingFixture : IDisposable
    {
        private readonly TempDirectory _temp;

        private PairingFixture(
            TempDirectory temp,
            SharedQueuePaths paths,
            PairingCodeService codes,
            SharedQueueGroupPairingProcessor processor,
            SharedQueuePairingCoordinator coordinator,
            ClientPairingStore clientPairing,
            CandidateStateStore clientCandidate,
            SharedAuthorizedClientStore authorized,
            byte[] groupKey,
            byte[] privateKey)
        {
            _temp = temp;
            Paths = paths;
            Codes = codes;
            Processor = processor;
            Coordinator = coordinator;
            ClientPairing = clientPairing;
            ClientCandidate = clientCandidate;
            Authorized = authorized;
            GroupKey = groupKey;
            PrivateKey = privateKey;
        }

        public SharedQueuePaths Paths { get; }
        public PairingCodeService Codes { get; }
        public SharedQueueGroupPairingProcessor Processor { get; }
        public SharedQueuePairingCoordinator Coordinator { get; }
        public ClientPairingStore ClientPairing { get; }
        public CandidateStateStore ClientCandidate { get; }
        public SharedAuthorizedClientStore Authorized { get; }
        private byte[] GroupKey { get; }
        private byte[] PrivateKey { get; }

        public static PairingFixture Create(bool useSharedLeaderIdentity)
        {
            var temp = new TempDirectory();
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
            var leaderKeys = useSharedLeaderIdentity
                ? new CentralKeyStore(leaderCandidate, groupIdentity)
                : new CentralKeyStore(Path.Combine(temp.Path, "wrong-leader-key.bin"));
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
            var pairingClient = new SharedQueuePairingClient(
                paths,
                clientPairing,
                TimeSpan.FromMilliseconds(20),
                TimeSpan.FromSeconds(3));
            var clientCandidate = new CandidateStateStore(Path.Combine(temp.Path, "client-candidate.bin"));
            var bootstrap = new SharedQueueGroupBootstrapService(
                paths,
                new CentralStateService(new CentralSettingsStore(Path.Combine(temp.Path, "client-central.json"))),
                new CentralKeyStore(Path.Combine(temp.Path, "client-local-key.bin")),
                new AuthorizedClientStore(Path.Combine(temp.Path, "client-authorized.bin")),
                clientPairing,
                clientCandidate);
            var coordinator = new SharedQueuePairingCoordinator(
                pairingClient,
                bootstrap,
                clientPairing,
                TimeSpan.FromMilliseconds(400),
                TimeSpan.FromMilliseconds(20));

            return new PairingFixture(
                temp,
                paths,
                codes,
                processor,
                coordinator,
                clientPairing,
                clientCandidate,
                authorized,
                groupKey,
                privateKey);
        }

        public async Task<string> WaitForPairingRequestAsync()
        {
            var deadline = DateTimeOffset.UtcNow.AddSeconds(2);
            while (DateTimeOffset.UtcNow < deadline)
            {
                var path = Directory.EnumerateFiles(Paths.PairingDirectory, "*.pair.req", SearchOption.TopDirectoryOnly).SingleOrDefault();
                if (path is not null)
                    return path;
                await Task.Delay(20);
            }

            throw new TimeoutException("Pedido de pareamento não apareceu no compartilhamento.");
        }

        public void Dispose()
        {
            CryptographicOperations.ZeroMemory(GroupKey);
            CryptographicOperations.ZeroMemory(PrivateKey);
            _temp.Dispose();
        }
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "nfe-agendamento-pairing-coordinator", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); } catch { }
        }
    }
}
