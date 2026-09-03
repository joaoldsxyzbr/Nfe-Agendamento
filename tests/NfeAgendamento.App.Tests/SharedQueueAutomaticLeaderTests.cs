using System.Security.Cryptography;
using System.Text.Json;
using NfeAgendamento.App.Fiscal;
using NfeAgendamento.App.SharedQueue;
using Xunit;

namespace NfeAgendamento.App.Tests;

public sealed class SharedQueueAutomaticLeaderTests
{
    private const string AccessKey = "42260912345678000123550010000000011000000015";

    [Fact]
    public void Lease_health_fails_closed_after_release()
    {
        using var temp = new TempDirectory();
        var share = Path.Combine(temp.Path, "share");
        Directory.CreateDirectory(share);
        var paths = new SharedQueuePaths(share);
        paths.InitializeAsCentral();

        var lease = SharedQueueCentralLease.TryAcquire(paths);
        Assert.NotNull(lease);
        Assert.True(lease!.IsHealthy);

        lease.Dispose();
        Assert.False(lease.IsHealthy);
    }

    [Fact]
    public async Task Exactly_one_candidate_leads_and_second_takes_over_with_same_public_key()
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

        var firstCandidate = new CandidateStateStore(Path.Combine(temp.Path, "first-candidate.bin"));
        var secondCandidate = new CandidateStateStore(Path.Combine(temp.Path, "second-candidate.bin"));
        firstCandidate.Save(groupKey);
        secondCandidate.Save(groupKey);

        var firstKeys = new CentralKeyStore(firstCandidate, groupIdentity);
        var secondKeys = new CentralKeyStore(secondCandidate, groupIdentity);
        var expectedPublic = firstKeys.GetOrCreatePublicKey();

        using var first = CreateRuntime(temp.Path, "first", paths, firstCandidate, firstKeys);
        using var second = CreateRuntime(temp.Path, "second", paths, secondCandidate, secondKeys);

        await first.TryActivateOnceAsync();
        await second.TryActivateOnceAsync();

        Assert.True(first.CanProcessWork());
        Assert.False(second.IsActive);
        Assert.Equal(CentralRuntimeStatus.Standby, second.Status);

        first.Dispose();
        await second.TryActivateOnceAsync();

        Assert.True(second.CanProcessWork());
        Assert.Equal(expectedPublic, secondKeys.GetOrCreatePublicKey());

        CryptographicOperations.ZeroMemory(groupKey);
        CryptographicOperations.ZeroMemory(privateKey);
        CryptographicOperations.ZeroMemory(expectedPublic);
    }

    [Fact]
    public async Task Takeover_never_repeats_request_that_may_have_reached_sefaz()
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
        var candidate = new CandidateStateStore(Path.Combine(temp.Path, "candidate.bin"));
        candidate.Save(groupKey);
        var keyStore = new CentralKeyStore(candidate, groupIdentity);
        var authorized = new SharedAuthorizedClientStore(paths, candidate);

        var clientId = Guid.NewGuid();
        var secret = RandomNumberGenerator.GetBytes(32);
        authorized.Authorize(clientId, "PC-CLIENTE", secret);
        var material = SharedQueueCrypto.CreateClientRequest(
            Guid.NewGuid(), AccessKey, keyStore.GetOrCreatePublicKey(), clientId, 1, secret);
        await File.WriteAllBytesAsync(
            paths.RequestPath(material.Envelope.RequestId),
            JsonSerializer.SerializeToUtf8Bytes(material.Envelope));

        var fiscalCalls = 0;
        var interrupted = new SharedQueueGroupProcessor(paths, keyStore, authorized, (_, _) =>
        {
            Interlocked.Increment(ref fiscalCalls);
            throw new IOException("queda simulada após autenticação");
        });

        Assert.False(await interrupted.ProcessOneAsync());
        Assert.Equal(1, fiscalCalls);

        var processing = paths.ProcessingPath(material.Envelope.RequestId);
        Assert.True(File.Exists(processing));
        File.SetLastWriteTimeUtc(processing, DateTime.UtcNow.AddMinutes(-3));
        await interrupted.MaintainAsync();

        var takeover = new SharedQueueGroupProcessor(paths, keyStore, authorized, (_, _) =>
        {
            Interlocked.Increment(ref fiscalCalls);
            return Task.FromResult(new NfeLookupResult(NfeLookupStatus.Found, "<xml/>", "138", "não deveria repetir", false));
        });

        Assert.True(await takeover.ProcessOneAsync());
        Assert.Equal(1, fiscalCalls);

        var responseBytes = await File.ReadAllBytesAsync(paths.ResponsePath(material.Envelope.RequestId));
        var responseEnvelope = JsonSerializer.Deserialize<QueueResponseEnvelope>(responseBytes)
            ?? throw new InvalidDataException("Resposta de recuperação inválida.");
        var recovered = SharedQueueCrypto.OpenResponse(responseEnvelope, material.AesKey);
        Assert.Equal(NfeLookupStatus.Failed, recovered.Status);
        Assert.Contains("não foi repetida", recovered.Message ?? string.Empty, StringComparison.OrdinalIgnoreCase);

        CryptographicOperations.ZeroMemory(groupKey);
        CryptographicOperations.ZeroMemory(privateKey);
        CryptographicOperations.ZeroMemory(secret);
        CryptographicOperations.ZeroMemory(material.AesKey);
    }

    private static SharedQueueCentralService CreateRuntime(
        string root,
        string suffix,
        SharedQueuePaths paths,
        CandidateStateStore candidate,
        CentralKeyStore keyStore)
    {
        var state = new CentralStateService(new CentralSettingsStore(Path.Combine(root, $"{suffix}-central.json")));
        var bootstrap = new SharedQueueGroupBootstrapService(
            paths,
            state,
            keyStore,
            new AuthorizedClientStore(Path.Combine(root, $"{suffix}-authorized.bin")),
            new ClientPairingStore(Path.Combine(root, $"{suffix}-client.bin")),
            candidate);
        return new SharedQueueCentralService(state, paths, keyStore, bootstrap);
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "nfe-agendamento-auto-leader", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); } catch { }
        }
    }
}
