using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using NfeAgendamento.App.Updates;
using Sigstore;
using Xunit;

namespace NfeAgendamento.App.Tests;

public sealed class UpdateServiceTests
{
    private static readonly Uri PackageUri = new(
        "https://github.com/joaoldsxyzbr/Nfe-Agendamento/releases/download/v1.2.0/Nfe-Agendamento-win-x64.zip");
    private static readonly Uri SignatureUri = new(
        "https://github.com/joaoldsxyzbr/Nfe-Agendamento/releases/download/v1.2.0/Nfe-Agendamento-win-x64.zip.sigstore.json");

    [Fact]
    public async Task CheckAsync_detects_newer_release_and_sigstore_bundle()
    {
        var publishedDigest = new string('a', 64);
        using var client = new HttpClient(new Handler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new
            {
                tag_name = "v1.2.0",
                assets = new[]
                {
                    new
                    {
                        name = "Nfe-Agendamento-win-x64.zip",
                        size = 1234,
                        digest = (string?)$"sha256:{publishedDigest}",
                        browser_download_url = PackageUri.ToString()
                    },
                    new
                    {
                        name = "Nfe-Agendamento-win-x64.zip.sigstore.json",
                        size = 4096,
                        digest = (string?)null,
                        browser_download_url = SignatureUri.ToString()
                    }
                }
            })
        }));
        using var service = new UpdateService(client, new Version(1, 0, 0));

        var result = await service.CheckAsync();

        Assert.True(result.IsUpdateAvailable);
        Assert.True(result.CanInstall);
        Assert.Equal(new Version(1, 2, 0), result.LatestVersion);
        Assert.Equal(1234, result.Package!.Size);
        Assert.Equal(publishedDigest, result.Package.Sha256);
        Assert.Equal(PackageUri, result.Package.DownloadUrl);
        Assert.Equal(SignatureUri, result.Package.SignatureUrl);
    }

    [Fact]
    public async Task CheckAsync_rejects_release_without_sigstore_bundle()
    {
        var publishedDigest = new string('a', 64);
        using var client = new HttpClient(new Handler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new
            {
                tag_name = "v1.2.0",
                assets = new[]
                {
                    new
                    {
                        name = "Nfe-Agendamento-win-x64.zip",
                        size = 1234,
                        digest = $"sha256:{publishedDigest}",
                        browser_download_url = PackageUri.ToString()
                    }
                }
            })
        }));
        using var service = new UpdateService(client, new Version(1, 0, 0));

        var result = await service.CheckAsync();

        Assert.True(result.IsUpdateAvailable);
        Assert.False(result.CanInstall);
        Assert.Null(result.Package);
    }

    [Fact]
    public async Task CheckAsync_treats_missing_release_as_no_update()
    {
        using var client = new HttpClient(new Handler(_ => new HttpResponseMessage(HttpStatusCode.NotFound)));
        using var service = new UpdateService(client, new Version(1, 0, 0));

        var result = await service.CheckAsync();

        Assert.False(result.IsUpdateAvailable);
        Assert.False(result.CanInstall);
        Assert.Null(result.LatestVersion);
        Assert.Null(result.Package);
    }

    [Fact]
    public async Task PrepareUpdateAsync_validates_digest_and_sigstore_before_staging()
    {
        using var temp = new TemporaryDirectory();
        var zip = BuildPackage();
        var digest = Convert.ToHexString(SHA256.HashData(zip)).ToLowerInvariant();
        var verifier = new RecordingSignatureVerifier();
        using var client = VerifiedPackageClient(zip, "{}");
        using var service = new UpdateService(client, new Version(1, 0, 0), verifier);
        var update = SignedUpdate(zip.Length, digest);

        var prepared = await service.PrepareUpdateAsync(update, temp.Path, processId: 4321);

        Assert.Equal(1, verifier.Calls);
        Assert.Equal("{}", verifier.LastBundle);
        Assert.Equal(new Version(1, 2, 0), prepared.Version);
        Assert.True(File.Exists(prepared.ScriptPath));
        Assert.True(File.Exists(Path.Combine(prepared.StagingDirectory, "NfeAgendamento.App.exe")));
        var script = await File.ReadAllTextAsync(prepared.ScriptPath);
        Assert.Contains("Wait-Process -Id 4321", script, StringComparison.Ordinal);
        Assert.Contains(temp.Path, script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("NfeAgendamento.App.exe", script, StringComparison.Ordinal);
        Assert.Contains("AddSeconds(20)", script, StringComparison.Ordinal);
        Assert.Contains("http://127.0.0.1:17345/api/bootstrap", script, StringComparison.Ordinal);
        Assert.Contains("Move-Item -LiteralPath $install -Destination $backup", script, StringComparison.Ordinal);
        Assert.Contains("Move-Item -LiteralPath $backup -Destination $install", script, StringComparison.Ordinal);
        Assert.DoesNotContain("Copy-Item -LiteralPath $_.FullName", script, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PrepareUpdateAsync_rejects_package_with_wrong_digest_before_sigstore()
    {
        using var temp = new TemporaryDirectory();
        var zip = BuildPackage();
        var verifier = new RecordingSignatureVerifier();
        using var client = VerifiedPackageClient(zip, "{}");
        using var service = new UpdateService(client, new Version(1, 0, 0), verifier);
        var update = SignedUpdate(zip.Length, new string('0', 64));

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
            service.PrepareUpdateAsync(update, temp.Path, processId: 4321));

        Assert.Contains("integridade", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, verifier.Calls);
    }

    [Fact]
    public async Task PrepareUpdateAsync_rejects_invalid_sigstore_bundle()
    {
        using var temp = new TemporaryDirectory();
        var zip = BuildPackage();
        var digest = Convert.ToHexString(SHA256.HashData(zip)).ToLowerInvariant();
        var verifier = new RecordingSignatureVerifier(new InvalidDataException("assinatura Sigstore inválida"));
        using var client = VerifiedPackageClient(zip, "{}");
        using var service = new UpdateService(client, new Version(1, 0, 0), verifier);
        var update = SignedUpdate(zip.Length, digest);

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
            service.PrepareUpdateAsync(update, temp.Path, processId: 4321));

        Assert.Contains("assinatura", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(Directory.EnumerateDirectories(temp.Path, ".*.update-*", SearchOption.TopDirectoryOnly));
    }

    [Fact]
    public void Sigstore_policy_pins_official_repository_workflow_and_github_hosted_runner()
    {
        var policy = SigstoreUpdateSignatureVerifier.CreatePolicy();
        var identity = Assert.IsType<CertificateIdentity>(policy.CertificateIdentity);
        var extensions = Assert.IsType<CertificateExtensionPolicy>(identity.Extensions);

        Assert.Equal(SigstoreUpdateSignatureVerifier.GitHubOidcIssuer, identity.Issuer);
        Assert.Equal(SigstoreUpdateSignatureVerifier.ReleaseWorkflowIdentity, identity.SubjectAlternativeName);
        Assert.Equal(SigstoreUpdateSignatureVerifier.RepositoryUri, extensions.SourceRepositoryUri);
        Assert.Equal("refs/heads/main", extensions.SourceRepositoryRef);
        Assert.Equal("github-hosted", extensions.RunnerEnvironment);
        Assert.Equal("public", extensions.SourceRepositoryVisibilityAtSigning);
        Assert.True(policy.RequireTransparencyLog);
        Assert.Equal(1, policy.TransparencyLogThreshold);
        Assert.True(policy.RequireSignedCertificateTimestamps);
    }

    private static UpdateCheckResult SignedUpdate(int size, string digest) =>
        new(
            new Version(1, 0, 0),
            new Version(1, 2, 0),
            new UpdatePackage(PackageUri, size, digest, SignatureUri));

    private static HttpClient VerifiedPackageClient(byte[] zip, string bundle) =>
        new(new Handler(request =>
        {
            Assert.Equal("github.com", request.RequestUri!.Host);
            if (request.RequestUri == PackageUri)
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(zip) };
            if (request.RequestUri == SignatureUri)
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(bundle, System.Text.Encoding.UTF8, "application/json")
                };
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }));

    private static byte[] BuildPackage()
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            var executable = archive.CreateEntry("NfeAgendamento.App.exe");
            using var writer = new StreamWriter(executable.Open());
            writer.Write("executável de teste");
        }
        return buffer.ToArray();
    }

    private sealed class RecordingSignatureVerifier(Exception? exception = null) : IUpdateSignatureVerifier
    {
        public int Calls { get; private set; }
        public string? LastBundle { get; private set; }

        public Task VerifyAsync(
            string packagePath,
            string bundleJson,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            LastBundle = bundleJson;
            if (exception is not null)
                return Task.FromException(exception);
            return Task.CompletedTask;
        }
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(responder(request));
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "nfe-agendamento-update-tests",
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
