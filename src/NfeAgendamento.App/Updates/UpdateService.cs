using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http.Json;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;

namespace NfeAgendamento.App.Updates;

public sealed record UpdatePackage(Uri DownloadUrl, long Size, string Sha256);

public sealed record UpdateCheckResult(
    Version CurrentVersion,
    Version? LatestVersion,
    UpdatePackage? Package = null)
{
    public bool IsUpdateAvailable => LatestVersion is not null && LatestVersion > CurrentVersion;
    public bool CanInstall => IsUpdateAvailable && Package is not null;
}

public sealed record PreparedUpdate(
    Version Version,
    string ScriptPath,
    string StagingDirectory);

public sealed class UpdateService : IDisposable
{
    private const string LatestReleaseUrl =
        "https://api.github.com/repos/joaoldsxyzbr/Nfe-Agendamento/releases/latest";
    private const string WindowsPackageName = "Nfe-Agendamento-win-x64.zip";
    private const long MaxPackageBytes = 200L * 1024 * 1024;
    private readonly HttpClient _httpClient;
    private readonly Version _currentVersion;

    public UpdateService(HttpClient? httpClient = null, Version? currentVersion = null)
    {
        _httpClient = httpClient ?? new HttpClient();
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("NfeAgendamento-Updater/1.0");
        _currentVersion = currentVersion ?? Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(0, 0, 0);
    }

    public async Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync(LatestReleaseUrl, cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return new UpdateCheckResult(_currentVersion, null);

        response.EnsureSuccessStatusCode();
        var release = await response.Content.ReadFromJsonAsync<GitHubRelease>(cancellationToken)
            ?? throw new InvalidDataException("Resposta de atualização vazia.");
        var tag = release.TagName?.Trim().TrimStart('v', 'V');
        if (!Version.TryParse(tag, out var latestVersion))
            throw new InvalidDataException("Versão publicada inválida.");

        var package = BuildPackage(release.Assets);
        return new UpdateCheckResult(_currentVersion, latestVersion, package);
    }

    public async Task<PreparedUpdate> PrepareUpdateAsync(
        UpdateCheckResult update,
        string installDirectory,
        int processId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(update);
        if (!update.CanInstall || update.LatestVersion is null || update.Package is null)
            throw new InvalidOperationException("A release encontrada não possui um pacote Windows instalável.");
        if (string.IsNullOrWhiteSpace(installDirectory))
            throw new ArgumentException("Pasta de instalação inválida.", nameof(installDirectory));
        if (processId <= 0)
            throw new ArgumentOutOfRangeException(nameof(processId));

        var installPath = Path.GetFullPath(installDirectory);
        Directory.CreateDirectory(installPath);
        EnsureInstallDirectoryIsWritable(installPath);

        var workspace = Path.Combine(
            Path.GetTempPath(),
            "NfeAgendamento",
            "updates",
            Guid.NewGuid().ToString("N"));
        var staging = Path.Combine(workspace, "staging");
        var packagePath = Path.Combine(workspace, WindowsPackageName);
        Directory.CreateDirectory(staging);

        try
        {
            await DownloadPackageAsync(update.Package, packagePath, cancellationToken);
            VerifyPackageIntegrity(packagePath, update.Package);
            ExtractPackageSafely(packagePath, staging);

            var executable = Path.Combine(staging, "NfeAgendamento.App.exe");
            if (!File.Exists(executable))
                throw new InvalidDataException("O pacote de atualização não contém o executável esperado.");

            var scriptPath = Path.Combine(workspace, "apply-update.ps1");
            var script = BuildInstallerScript(processId, staging, installPath, workspace);
            await File.WriteAllTextAsync(scriptPath, script, new UTF8Encoding(false), cancellationToken);

            return new PreparedUpdate(update.LatestVersion, scriptPath, staging);
        }
        catch
        {
            TryDeleteDirectory(workspace);
            throw;
        }
    }

    public static void LaunchPreparedUpdate(PreparedUpdate prepared)
    {
        ArgumentNullException.ThrowIfNull(prepared);
        if (!File.Exists(prepared.ScriptPath))
            throw new FileNotFoundException("Script de atualização não encontrado.", prepared.ScriptPath);

        var startInfo = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(prepared.ScriptPath) ?? Path.GetTempPath()
        };
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-NonInteractive");
        startInfo.ArgumentList.Add("-ExecutionPolicy");
        startInfo.ArgumentList.Add("Bypass");
        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(prepared.ScriptPath);

        Process.Start(startInfo)
            ?? throw new InvalidOperationException("Não foi possível iniciar o instalador da atualização.");
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }

    private static UpdatePackage? BuildPackage(IReadOnlyList<GitHubReleaseAsset>? assets)
    {
        var asset = assets?.FirstOrDefault(candidate =>
            string.Equals(candidate.Name, WindowsPackageName, StringComparison.OrdinalIgnoreCase));
        if (asset is null
            || asset.Size <= 0
            || asset.Size > MaxPackageBytes
            || !Uri.TryCreate(asset.DownloadUrl, UriKind.Absolute, out var downloadUrl)
            || !string.Equals(downloadUrl.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(downloadUrl.Host, "github.com", StringComparison.OrdinalIgnoreCase))
            return null;

        var digest = NormalizeDigest(asset.Digest);
        return digest is null ? null : new UpdatePackage(downloadUrl, asset.Size, digest);
    }

    private async Task DownloadPackageAsync(
        UpdatePackage package,
        string destination,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(package.DownloadUrl.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(package.DownloadUrl.Host, "github.com", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Endereço do pacote de atualização inválido.");
        if (package.Size <= 0 || package.Size > MaxPackageBytes)
            throw new InvalidDataException("Tamanho do pacote de atualização inválido.");

        using var response = await _httpClient.GetAsync(
            package.DownloadUrl,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();

        if (response.Content.Headers.ContentLength is > MaxPackageBytes)
            throw new InvalidDataException("Pacote de atualização excede o limite permitido.");

        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true);
        var buffer = new byte[81920];
        long total = 0;
        while (true)
        {
            var read = await input.ReadAsync(buffer, cancellationToken);
            if (read == 0)
                break;

            total += read;
            if (total > MaxPackageBytes)
                throw new InvalidDataException("Pacote de atualização excede o limite permitido.");

            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }

        if (total != package.Size)
            throw new InvalidDataException("O tamanho do pacote baixado não corresponde ao publicado pelo GitHub.");
    }

    private static void VerifyPackageIntegrity(string packagePath, UpdatePackage package)
    {
        using var stream = File.OpenRead(packagePath);
        var actualHash = SHA256.HashData(stream);
        byte[] expectedHash;
        try
        {
            expectedHash = Convert.FromHexString(package.Sha256);
        }
        catch (FormatException ex)
        {
            throw new InvalidDataException("Digest SHA-256 publicado é inválido.", ex);
        }

        if (expectedHash.Length != actualHash.Length
            || !CryptographicOperations.FixedTimeEquals(actualHash, expectedHash))
            throw new InvalidDataException("A verificação de integridade da atualização falhou.");
    }

    private static void ExtractPackageSafely(string packagePath, string stagingDirectory)
    {
        var root = Path.GetFullPath(stagingDirectory);
        var rootPrefix = root.EndsWith(Path.DirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;

        using var archive = ZipFile.OpenRead(packagePath);
        foreach (var entry in archive.Entries)
        {
            var destination = Path.GetFullPath(Path.Combine(root, entry.FullName));
            if (!destination.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("O pacote de atualização contém um caminho inválido.");

            if (string.IsNullOrEmpty(entry.Name))
            {
                Directory.CreateDirectory(destination);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            entry.ExtractToFile(destination, overwrite: false);
        }
    }

    private static string BuildInstallerScript(
        int processId,
        string stagingDirectory,
        string installDirectory,
        string workspace)
    {
        static string Ps(string value) => "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";

        return $$"""
$ErrorActionPreference = 'Stop'
try { Wait-Process -Id {{processId}} -ErrorAction SilentlyContinue } catch { }
Start-Sleep -Milliseconds 500
$staging = {{Ps(stagingDirectory)}}
$install = {{Ps(installDirectory)}}
Get-ChildItem -LiteralPath $staging -Force | ForEach-Object {
    Copy-Item -LiteralPath $_.FullName -Destination $install -Recurse -Force
}
$exe = Join-Path $install 'NfeAgendamento.App.exe'
Start-Process -FilePath $exe -WorkingDirectory $install
Start-Sleep -Milliseconds 500
Remove-Item -LiteralPath {{Ps(workspace)}} -Recurse -Force -ErrorAction SilentlyContinue
""";
    }

    private static void EnsureInstallDirectoryIsWritable(string installDirectory)
    {
        var probe = Path.Combine(installDirectory, $".nfe-update-{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(probe, string.Empty);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            throw new InvalidOperationException(
                "A pasta atual não permite atualização automática. Mova o aplicativo para uma pasta gravável ou atualize manualmente.",
                ex);
        }
        finally
        {
            try { File.Delete(probe); } catch { }
        }
    }

    private static string? NormalizeDigest(string? digest)
    {
        if (string.IsNullOrWhiteSpace(digest))
            return null;

        const string prefix = "sha256:";
        var normalized = digest.Trim();
        if (normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            normalized = normalized[prefix.Length..];

        if (normalized.Length != 64 || normalized.Any(character => !Uri.IsHexDigit(character)))
            return null;

        return normalized.ToLowerInvariant();
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch
        {
        }
    }

    private sealed record GitHubRelease(
        [property: JsonPropertyName("tag_name")] string? TagName,
        [property: JsonPropertyName("assets")] GitHubReleaseAsset[]? Assets);

    private sealed record GitHubReleaseAsset(
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("size")] long Size,
        [property: JsonPropertyName("digest")] string? Digest,
        [property: JsonPropertyName("browser_download_url")] string? DownloadUrl);
}
