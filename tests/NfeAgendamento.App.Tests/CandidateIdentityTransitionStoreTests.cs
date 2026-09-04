using System.Security.Cryptography;
using NfeAgendamento.App.SharedQueue;
using Xunit;

namespace NfeAgendamento.App.Tests;

public sealed class CandidateIdentityTransitionStoreTests
{
    [Fact]
    public async Task Stored_chain_allows_clients_pinned_at_any_valid_intermediate_identity_to_reach_latest_identity()
    {
        using var temp = new TempDirectory();
        var share = Path.Combine(temp.Path, "share");
        Directory.CreateDirectory(share);
        var paths = new SharedQueuePaths(share);
        paths.InitializeAsCentral();

        var clientId = Guid.NewGuid();
        var clientSecret = RandomNumberGenerator.GetBytes(32);
        using var rsaA = RSA.Create(2048);
        using var rsaB = RSA.Create(2048);
        using var rsaC = RSA.Create(2048);
        var publicA = rsaA.ExportSubjectPublicKeyInfo();
        var publicB = rsaB.ExportSubjectPublicKeyInfo();
        var publicC = rsaC.ExportSubjectPublicKeyInfo();
        var first = GroupRotationProof.Create(rsaA, publicA, publicB);
        var second = GroupRotationProof.Create(rsaB, publicB, publicC);
        var store = new CandidateIdentityTransitionStore(paths);

        await store.WriteAsync(clientId, clientSecret, [first, second]);
        var loaded = store.Read(clientId, clientSecret);

        Assert.True(GroupRotationProof.VerifyChain(publicA, publicC, loaded));
        Assert.True(GroupRotationProof.VerifyChain(publicB, publicC, loaded));
        Assert.Throws<CryptographicException>(() =>
            store.Read(clientId, RandomNumberGenerator.GetBytes(32)));

        CandidateIdentityTransitionStore.Zero(loaded);
        CandidateIdentityTransitionStore.Zero([first, second]);
        CryptographicOperations.ZeroMemory(clientSecret);
        CryptographicOperations.ZeroMemory(publicA);
        CryptographicOperations.ZeroMemory(publicB);
        CryptographicOperations.ZeroMemory(publicC);
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "nfe-agendamento-transition-store",
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
