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
    private const string WindowsSignatureName = WindowsPackageName + ".sig";
    private const long MaxPackageBytes = 200L * 1024 * 1024;
    private const long MaxSignatureBytes = 16L * 1024;
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

        var installPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(installDirectory));
        Directory.CreateDirectory(installPath);
        EnsureDirectoryIsWritable(
            installPath,
            "A pasta atual não permite atualização automática. Mova o aplicativo para uma pasta gravável ou atualize manualmente.");

        var installParent = Directory.GetParent(installPath)?.FullName
            ?? throw new InvalidOperationException("A pasta de instalação não possui um diretório pai válido para atualização segura.");
        EnsureDirectoryIsWritable(
            installParent,
            "A pasta pai da instalação não permite preparar uma troca segura. Atualize manualmente.");

        var installName = Path.GetFileName(installPath);
        if (string.IsNullOrWhiteSpace(installName))
            throw new InvalidOperationException("A pasta de instalação não pode ser substituída com segurança.");

        var workspace = Path.Combine(
            Path.GetTempPath(),
            "NfeAgendamento",
            "updates",
            Guid.NewGuid().ToString("N"));
        var staging = Path.Combine(workspace, "staging");
        var packagePath = Path.Combine(workspace, WindowsPackageName);
        var nextInstall = Path.Combine(installParent, $".{installName}.update-{Guid.NewGuid():N}");
        var backupInstall = Path.Combine(installParent, $".{installName}.backup");
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
            var script = BuildInstallerScript(
                processId,
                staging,
                installPath,
                nextInstall,
                backupInstall,
                workspace);
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

        _ = Process.Start(startInfo)
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
        var signature = assets?.FirstOrDefault(candidate =>
            string.Equals(candidate.Name, WindowsSignatureName, StringComparison.OrdinalIgnoreCase));

        if (asset is null
            || signature is null
            || asset.Size <= 0
            || asset.Size > MaxPackageBytes
            || signature.Size <= 0
            || signature.Size > MaxSignatureBytes
            || !TryGetTrustedGitHubUri(asset.DownloadUrl, out var downloadUrl)
            || !TryGetTrustedGitHubUri(signature.DownloadUrl, out _))
            return null;

        var digest = NormalizeDigest(asset.Digest);
        return digest is null ? null : new UpdatePackage(downloadUrl, asset.Size, digest);
    }

    private static bool TryGetTrustedGitHubUri(string? value, out Uri uri)
    {
        if (Uri.TryCreate(value, UriKind.Absolute, out var parsed)
            && string.Equals(parsed.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            && string.Equals(parsed.Host, "github.com", StringComparison.OrdinalIgnoreCase))
        {
            uri = parsed;
            return true;
        }

        uri = null!;
        return false;
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
        string nextInstallDirectory,
        string backupInstallDirectory,
        string workspace)
    {
        static string Ps(string value) => "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";

        return $$"""
$ErrorActionPreference = 'Stop'
$staging = {{Ps(stagingDirectory)}}
$install = {{Ps(installDirectory)}}
$next = {{Ps(nextInstallDirectory)}}
$backup = {{Ps(backupInstallDirectory)}}
$workspace = {{Ps(workspace)}}
$newProcess = $null
$swapped = $false

try { Wait-Process -Id {{processId}} -ErrorAction SilentlyContinue } catch { }
Start-Sleep -Milliseconds 500

try {
    if (Test-Path -LiteralPath $next) {
        Remove-Item -LiteralPath $next -Recurse -Force
    }
    if (Test-Path -LiteralPath $backup) {
        throw 'Existe um backup de atualização anterior. Restaure ou remova esse backup manualmente antes de atualizar novamente.'
    }

    Move-Item -LiteralPath $staging -Destination $next
    if (-not (Test-Path -LiteralPath (Join-Path $next 'NfeAgendamento.App.exe'))) {
        throw 'A versão preparada não contém o executável esperado.'
    }

    Move-Item -LiteralPath $install -Destination $backup
    $swapped = $true
    Move-Item -LiteralPath $next -Destination $install

    $exe = Join-Path $install 'NfeAgendamento.App.exe'
    $newProcess = Start-Process -FilePath $exe -WorkingDirectory $install -PassThru
    $deadline = [DateTime]::UtcNow.AddSeconds(20)
    $healthy = $false

    while ([DateTime]::UtcNow -lt $deadline) {
        if ($newProcess.HasExited) {
            break
        }

        try {
            $response = Invoke-WebRequest -Uri 'http://127.0.0.1:17345/api/bootstrap' -UseBasicParsing -TimeoutSec 2
            if ($response.StatusCode -ge 200 -and $response.StatusCode -lt 300) {
                $healthy = $true
                break
            }
        } catch { }

        Start-Sleep -Milliseconds 500
    }

    if (-not $healthy) {
        throw 'A nova versão não respondeu ao health check local em até 20 segundos.'
    }

    Remove-Item -LiteralPath $backup -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $workspace -Recurse -Force -ErrorAction SilentlyContinue
    exit 0
}
catch {
    $failure = $_.Exception.Message

    if ($newProcess -ne $null) {
        try {
            if (-not $newProcess.HasExited) {
                Stop-Process -Id $newProcess.Id -Force -ErrorAction SilentlyContinue
                $newProcess.WaitForExit(5000) | Out-Null
            }
        } catch { }
    }

    if ($swapped) {
        try {
            if (Test-Path -LiteralPath $install) {
                Remove-Item -LiteralPath $install -Recurse -Force
            }
            if (Test-Path -LiteralPath $backup) {
                Move-Item -LiteralPath $backup -Destination $install
            }
        } catch {
            try { Set-Content -LiteralPath (Join-Path $workspace 'rollback-error.txt') -Value $_.Exception.Message -Encoding UTF8 } catch { }
            throw
        }
    }

    if (Test-Path -LiteralPath $next) {
        Remove-Item -LiteralPath $next -Recurse -Force -ErrorAction SilentlyContinue
    }

    $oldExe = Join-Path $install 'NfeAgendamento.App.exe'
    if (Test-Path -LiteralPath $oldExe) {
        try { Start-Process -FilePath $oldExe -WorkingDirectory $install } catch { }
    }

    try { Set-Content -LiteralPath (Join-Path $workspace 'update-error.txt') -Value $failure -Encoding UTF8 } catch { }
    exit 1
}
""";
    }

    private static void EnsureDirectoryIsWritable(string directory, string errorMessage)
    {
        var probe = Path.Combine(directory, $".nfe-update-{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(probe, string.Empty);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            throw new InvalidOperationException(errorMessage, ex);
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
