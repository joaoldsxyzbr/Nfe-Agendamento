using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using NfeAgendamento.App.Certificates;
using NfeAgendamento.App.SharedQueue;
using Xunit;

namespace NfeAgendamento.App.Tests;

public sealed class SharedQueueClusterIdentityTests
{
    [Fact]
    public async Task Bootstrap_preserves_existing_central_public_key_and_hides_private_key()
    {
        using var temp = new TempDirectory();
        var share = Path.Combine(temp.Path, "share");
        Directory.CreateDirectory(share);
        var paths = new SharedQueuePaths(share);
        paths.InitializeAsCentral();

        using var certificate = CreateAndInstallCertificate();
        try
        {
            var certificateService = new CertificateService(Path.Combine(temp.Path, "certificate.txt"));
            await certificateService.SelectAsync(certificate.Thumbprint, "42");

            var legacyPath = Path.Combine(temp.Path, "central-private-key.bin");
            var legacy = new CentralKeyStore(legacyPath);
            var expectedPublicKey = legacy.GetOrCreatePublicKey();
            byte[] privateKey;
            using (var rsa = legacy.OpenPrivateKey())
                privateKey = rsa.ExportPkcs8PrivateKey();

            var legacyState = CreateLegacyState(temp.Path, configuredAsCentral: true);
            var clustered = new CentralKeyStore(paths, certificateService, legacyState, legacyPath);

            var actualPublicKey = clustered.GetOrCreatePublicKey();

            Assert.Equal(expectedPublicKey, actualPublicKey);
            Assert.True(clustered.ClusterIdentityExists);
            var bundle = File.ReadAllBytes(paths.StatusPath("cluster-identity.json"));
            Assert.False(ContainsSequence(bundle, privateKey));
            CryptographicOperations.ZeroMemory(privateKey);
        }
        finally
        {
            RemoveCertificate(certificate);
        }
    }

    [Fact]
    public async Task Another_pc_with_same_A1_opens_same_cluster_identity()
    {
        using var temp = new TempDirectory();
        var share = Path.Combine(temp.Path, "share");
        Directory.CreateDirectory(share);
        var paths = new SharedQueuePaths(share);
        paths.InitializeAsCentral();

        using var certificate = CreateAndInstallCertificate();
        try
        {
            var firstCertificates = new CertificateService(Path.Combine(temp.Path, "first-cert.txt"));
            await firstCertificates.SelectAsync(certificate.Thumbprint, "42");
            var firstLegacyPath = Path.Combine(temp.Path, "first-central.bin");
            var firstLegacy = new CentralKeyStore(firstLegacyPath);
            var expectedPublicKey = firstLegacy.GetOrCreatePublicKey();
            var firstState = CreateLegacyState(Path.Combine(temp.Path, "first"), configuredAsCentral: true);
            var first = new CentralKeyStore(paths, firstCertificates, firstState, firstLegacyPath);
            Assert.Equal(expectedPublicKey, first.GetOrCreatePublicKey());

            var secondCertificates = new CertificateService(Path.Combine(temp.Path, "second-cert.txt"));
            var secondState = CreateLegacyState(Path.Combine(temp.Path, "second"), configuredAsCentral: false);
            var second = new CentralKeyStore(paths, secondCertificates, secondState, Path.Combine(temp.Path, "second-central.bin"));

            Assert.Equal(expectedPublicKey, second.GetOrCreatePublicKey());
            var binding = second.GetClusterBinding();
            Assert.Equal(certificate.Thumbprint, binding.CertificateThumbprint, ignoreCase: true);
            Assert.Equal("42", binding.AuthorityState);
        }
        finally
        {
            RemoveCertificate(certificate);
        }
    }

    [Fact]
    public void Non_legacy_pc_does_not_create_new_identity_when_cluster_is_missing()
    {
        using var temp = new TempDirectory();
        var share = Path.Combine(temp.Path, "share");
        Directory.CreateDirectory(share);
        var paths = new SharedQueuePaths(share);
        paths.InitializeAsCentral();

        var certificates = new CertificateService(Path.Combine(temp.Path, "certificate.txt"));
        var state = CreateLegacyState(temp.Path, configuredAsCentral: false);
        var store = new CentralKeyStore(paths, certificates, state, Path.Combine(temp.Path, "missing-legacy.bin"));

        var ex = Assert.Throws<InvalidOperationException>(() => store.GetOrCreatePublicKey());
        Assert.Contains("inicializada", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(store.ClusterIdentityExists);
    }

    [Fact]
    public async Task Different_A1_cannot_open_existing_cluster_identity()
    {
        using var temp = new TempDirectory();
        var share = Path.Combine(temp.Path, "share");
        Directory.CreateDirectory(share);
        var paths = new SharedQueuePaths(share);
        paths.InitializeAsCentral();

        using var firstCertificate = CreateAndInstallCertificate();
        using var secondCertificate = CreateAndInstallCertificate();
        try
        {
            var firstCertificates = new CertificateService(Path.Combine(temp.Path, "first-cert.txt"));
            await firstCertificates.SelectAsync(firstCertificate.Thumbprint, "42");
            var legacyPath = Path.Combine(temp.Path, "central.bin");
            var legacy = new CentralKeyStore(legacyPath);
            _ = legacy.GetOrCreatePublicKey();
            var first = new CentralKeyStore(paths, firstCertificates, CreateLegacyState(Path.Combine(temp.Path, "first"), true), legacyPath);
            _ = first.GetOrCreatePublicKey();

            RemoveCertificate(firstCertificate);
            var secondCertificates = new CertificateService(Path.Combine(temp.Path, "second-cert.txt"));
            await secondCertificates.SelectAsync(secondCertificate.Thumbprint, "42");
            var second = new CentralKeyStore(paths, secondCertificates, CreateLegacyState(Path.Combine(temp.Path, "second"), false), Path.Combine(temp.Path, "other.bin"));

            Assert.Throws<InvalidOperationException>(() => second.OpenPrivateKey());
        }
        finally
        {
            RemoveCertificate(firstCertificate);
            RemoveCertificate(secondCertificate);
        }
    }

    private static CentralStateService CreateLegacyState(string root, bool configuredAsCentral)
    {
        Directory.CreateDirectory(root);
        var state = new CentralStateService(new CentralSettingsStore(Path.Combine(root, "central.json")));
        state.SetConfiguredAsCentral(configuredAsCentral);
        return state;
    }

    private static X509Certificate2 CreateAndInstallCertificate()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=NFe Agendamento Cluster Test", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var generated = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(7));
        var certificate = new X509Certificate2(generated.Export(X509ContentType.Pfx), (string?)null, X509KeyStorageFlags.Exportable | X509KeyStorageFlags.PersistKeySet);
        using var store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
        store.Open(OpenFlags.ReadWrite);
        store.Add(certificate);
        return certificate;
    }

    private static void RemoveCertificate(X509Certificate2 certificate)
    {
        try
        {
            using var store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
            store.Open(OpenFlags.ReadWrite);
            var matches = store.Certificates.Find(X509FindType.FindByThumbprint, certificate.Thumbprint, validOnly: false);
            foreach (var match in matches)
            {
                store.Remove(match);
                match.Dispose();
            }
        }
        catch (CryptographicException)
        {
        }
    }

    private static bool ContainsSequence(byte[] haystack, byte[] needle)
    {
        if (needle.Length == 0 || needle.Length > haystack.Length)
            return false;
        return haystack.AsSpan().IndexOf(needle) >= 0;
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "nfe-agendamento-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); } catch { }
        }
    }
}
