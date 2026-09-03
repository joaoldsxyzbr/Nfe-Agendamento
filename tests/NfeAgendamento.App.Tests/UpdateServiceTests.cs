using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using NfeAgendamento.App.Updates;
using Xunit;

namespace NfeAgendamento.App.Tests;

public sealed class UpdateServiceTests
{
    [Fact]
    public async Task CheckAsync_detects_newer_release_and_official_windows_package()
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
                        browser_download_url = "https://github.com/joaoldsxyzbr/Nfe-Agendamento/releases/download/v1.2.0/Nfe-Agendamento-win-x64.zip"
                    },
                    new
                    {
                        name = "Nfe-Agendamento-win-x64.zip.sig",
                        size = 512,
                        digest = (string?)null,
                        browser_download_url = "https://github.com/joaoldsxyzbr/Nfe-Agendamento/releases/download/v1.2.0/Nfe-Agendamento-win-x64.zip.sig"
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
        Assert.Equal("github.com", result.Package.DownloadUrl.Host);
    }

    [Fact]
    public async Task CheckAsync_rejects_release_without_detached_signature()
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
                        browser_download_url = "https://github.com/joaoldsxyzbr/Nfe-Agendamento/releases/download/v1.2.0/Nfe-Agendamento-win-x64.zip"
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
    public async Task PrepareUpdateAsync_validates_digest_and_creates_post_exit_installer()
    {
        using var temp = new TemporaryDirectory();
        var zip = BuildPackage();
        var digest = Convert.ToHexString(SHA256.HashData(zip)).ToLowerInvariant();
        using var client = new HttpClient(new Handler(request =>
        {
            Assert.Equal("github.com", request.RequestUri!.Host);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(zip) };
        }));
        using var service = new UpdateService(client, new Version(1, 0, 0));
        var update = new UpdateCheckResult(
            new Version(1, 0, 0),
            new Version(1, 2, 0),
            new UpdatePackage(
                new Uri("https://github.com/joaoldsxyzbr/Nfe-Agendamento/releases/download/v1.2.0/Nfe-Agendamento-win-x64.zip"),
                zip.Length,
                digest));

        var prepared = await service.PrepareUpdateAsync(update, temp.Path, processId: 4321);

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
    public async Task PrepareUpdateAsync_rejects_package_with_wrong_digest()
    {
        using var temp = new TemporaryDirectory();
        var zip = BuildPackage();
        using var client = new HttpClient(new Handler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(zip) }));
        using var service = new UpdateService(client, new Version(1, 0, 0));
        var update = new UpdateCheckResult(
            new Version(1, 0, 0),
            new Version(1, 2, 0),
            new UpdatePackage(
                new Uri("https://github.com/joaoldsxyzbr/Nfe-Agendamento/releases/download/v1.2.0/Nfe-Agendamento-win-x64.zip"),
                zip.Length,
                new string('0', 64)));

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
            service.PrepareUpdateAsync(update, temp.Path, processId: 4321));

        Assert.Contains("integridade", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

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

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(responder(request));
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "nfe-agendamento-update-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); } catch { }
        }
    }
}
