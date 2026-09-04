from pathlib import Path

ROOT = Path.cwd()

def replace_once(path, old, new):
    p = ROOT / path
    text = p.read_text(encoding="utf-8")
    count = text.count(old)
    if count != 1:
        raise RuntimeError(f"{path}: esperado 1 match, encontrado {count}: {old[:80]!r}")
    p.write_text(text.replace(old, new, 1), encoding="utf-8")

def write(path, content):
    p = ROOT / path
    p.parent.mkdir(parents=True, exist_ok=True)
    p.write_text(content, encoding="utf-8")

replace_once(
    "src/NfeAgendamento.App/NfeAgendamento.App.csproj",
    "    <Version>0.1.25</Version>",
    "    <Version>0.1.26</Version>")
replace_once(
    "src/NfeAgendamento.App/NfeAgendamento.App.csproj",
    '    <PackageReference Include="System.Security.Cryptography.ProtectedData" Version="10.0.11" />',
    '    <PackageReference Include="System.Security.Cryptography.ProtectedData" Version="10.0.11" />\n'
    '    <PackageReference Include="Sigstore" Version="0.5.0" />')

verifier = """using Sigstore;

namespace NfeAgendamento.App.Updates;

public interface IUpdateSignatureVerifier
{
    Task VerifyAsync(
        string packagePath,
        string bundleJson,
        CancellationToken cancellationToken = default);
}

public sealed class SigstoreUpdateSignatureVerifier : IUpdateSignatureVerifier
{
    public const string GitHubOidcIssuer = "https://token.actions.githubusercontent.com";
    public const string RepositoryUri = "https://github.com/joaoldsxyzbr/Nfe-Agendamento";
    public const string ReleaseWorkflowIdentity =
        "https://github.com/joaoldsxyzbr/Nfe-Agendamento/.github/workflows/release-bridge.yml@refs/heads/main";

    public static VerificationPolicy CreatePolicy() =>
        new()
        {
            CertificateIdentity = new CertificateIdentity
            {
                SubjectAlternativeName = ReleaseWorkflowIdentity,
                Issuer = GitHubOidcIssuer,
                Extensions = new CertificateExtensionPolicy
                {
                    SourceRepositoryUri = RepositoryUri,
                    SourceRepositoryRef = "refs/heads/main",
                    RunnerEnvironment = "github-hosted",
                    SourceRepositoryVisibilityAtSigning = "public"
                }
            },
            RequireTransparencyLog = true,
            TransparencyLogThreshold = 1,
            RequireSignedCertificateTimestamps = true
        };

    public async Task VerifyAsync(
        string packagePath,
        string bundleJson,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(packagePath))
            throw new ArgumentException("Caminho do pacote de atualização inválido.", nameof(packagePath));
        if (string.IsNullOrWhiteSpace(bundleJson))
            throw new InvalidDataException("Bundle Sigstore de atualização vazio.");

        try
        {
            var bundle = SigstoreBundle.Deserialize(bundleJson);
            var verifier = new SigstoreVerifier();
            await using var artifact = File.OpenRead(packagePath);
            await verifier.VerifyStreamAsync(
                artifact,
                bundle,
                CreatePolicy(),
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new InvalidDataException(
                "A assinatura Sigstore da atualização não pôde ser validada contra o workflow oficial.",
                ex);
        }
    }
}
"""
write("src/NfeAgendamento.App/Updates/SigstoreUpdateSignatureVerifier.cs", verifier)

update_path = ROOT / "src/NfeAgendamento.App/Updates/UpdateService.cs"
text = update_path.read_text(encoding="utf-8")
repls = [
    ('private const string WindowsSignatureName = WindowsPackageName + ".sig";',
     'private const string WindowsSignatureName = WindowsPackageName + ".sigstore.json";'),
    ('private const long MaxSignatureBytes = 16L * 1024;',
     'private const long MaxSignatureBytes = 128L * 1024;'),
    ('private readonly byte[] _signingPublicKey;',
     'private readonly IUpdateSignatureVerifier _signatureVerifier;'),
    ('        Version? currentVersion = null,\n        byte[]? signingPublicKey = null)',
     '        Version? currentVersion = null,\n        IUpdateSignatureVerifier? signatureVerifier = null)'),
    ('        _signingPublicKey = (signingPublicKey ?? UpdateSigningKey.GetSubjectPublicKeyInfo()).ToArray();\n'
     '        if (_signingPublicKey.Length == 0)\n'
     '            throw new ArgumentException("Chave pública de assinatura de update inválida.", nameof(signingPublicKey));',
     '        _signatureVerifier = signatureVerifier ?? new SigstoreUpdateSignatureVerifier();'),
    ('            var signature = await DownloadSignatureAsync(update.Package.SignatureUrl, cancellationToken);\n'
     '            try\n'
     '            {\n'
     '                VerifyPackageSignature(packagePath, signature);\n'
     '            }\n'
     '            finally\n'
     '            {\n'
     '                CryptographicOperations.ZeroMemory(signature);\n'
     '            }',
     '            var verificationBundle = await DownloadSignatureBundleAsync(\n'
     '                update.Package.SignatureUrl,\n'
     '                cancellationToken);\n'
     '            await _signatureVerifier.VerifyAsync(\n'
     '                packagePath,\n'
     '                verificationBundle,\n'
     '                cancellationToken);'),
    ('        _httpClient.Dispose();\n        CryptographicOperations.ZeroMemory(_signingPublicKey);',
     '        _httpClient.Dispose();'),
]
for old, new in repls:
    count = text.count(old)
    if count != 1:
        raise RuntimeError(f"UpdateService replacement esperado 1, encontrado {count}: {old[:100]!r}")
    text = text.replace(old, new, 1)

start = text.index("    private async Task<byte[]> DownloadSignatureAsync")
end = text.index("    private static void VerifyPackageIntegrity", start)
new_download = """    private async Task<string> DownloadSignatureBundleAsync(
        Uri signatureUrl,
        CancellationToken cancellationToken)
    {
        if (!TryGetTrustedGitHubUri(signatureUrl.ToString(), out _))
            throw new InvalidDataException("Endereço do bundle Sigstore de atualização inválido.");

        using var response = await _httpClient.GetAsync(
            signatureUrl,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();

        if (response.Content.Headers.ContentLength is > MaxSignatureBytes)
            throw new InvalidDataException("Bundle Sigstore de atualização excede o limite permitido.");

        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var output = new MemoryStream();
        var buffer = new byte[4096];
        long total = 0;
        while (true)
        {
            var read = await input.ReadAsync(buffer, cancellationToken);
            if (read == 0)
                break;

            total += read;
            if (total > MaxSignatureBytes)
                throw new InvalidDataException("Bundle Sigstore de atualização excede o limite permitido.");

            output.Write(buffer, 0, read);
        }

        if (total <= 0)
            throw new InvalidDataException("Bundle Sigstore de atualização vazio.");

        return Encoding.UTF8.GetString(output.ToArray());
    }

"""
text = text[:start] + new_download + text[end:]
start = text.index("    private void VerifyPackageSignature")
end = text.index("    private static void ExtractPackageSafely", start)
text = text[:start] + text[end:]
update_path.write_text(text, encoding="utf-8")

old_key = ROOT / "src/NfeAgendamento.App/Updates/UpdateSigningKey.cs"
if old_key.exists():
    old_key.unlink()

tests = """using System.IO.Compression;
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
                        digest = $"sha256:{publishedDigest}",
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
        using var client = VerifiedPackageClient(zip, "{\"mediaType\":\"test\"}");
        using var service = new UpdateService(client, new Version(1, 0, 0), verifier);
        var update = SignedUpdate(zip.Length, digest);

        var prepared = await service.PrepareUpdateAsync(update, temp.Path, processId: 4321);

        Assert.Equal(1, verifier.Calls);
        Assert.Equal("{\"mediaType\":\"test\"}", verifier.LastBundle);
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
"""
write("tests/NfeAgendamento.App.Tests/UpdateServiceTests.cs", tests)

rr = ROOT / "tests/js/release-readiness-regression.test.js"
r = rr.read_text(encoding="utf-8")
old_block = """assert.ok(bridge.includes('NFE_UPDATE_SIGNING_KEY_PKCS8_B64'), 'Release deve exigir a chave privada de assinatura via GitHub Secret.');
assert.ok(bridge.includes('ImportPkcs8PrivateKey'), 'Release deve importar a chave privada PKCS#8 sem versioná-la.');
assert.ok(bridge.includes('RSASignaturePadding]::Pss'), 'Release deve assinar o pacote com RSA-PSS.');
assert.ok(bridge.includes('Nfe-Agendamento-win-x64.zip.sig'), 'Release deve publicar a assinatura destacada do pacote.');"""
new_block = """assert.ok(bridge.includes('id-token: write'), 'Release deve permitir OIDC somente para assinatura keyless.');
assert.ok(bridge.includes('sigstore/cosign-installer@v4.1.2'), 'Release deve instalar Cosign por action oficial.');
assert.ok(bridge.includes('cosign sign-blob'), 'Release deve assinar o pacote com Sigstore keyless.');
assert.ok(bridge.includes('cosign verify-blob'), 'Release deve verificar a assinatura antes de publicar.');
assert.ok(bridge.includes('https://token.actions.githubusercontent.com'), 'Release deve fixar o issuer OIDC do GitHub Actions.');
assert.ok(bridge.includes('release-bridge.yml@refs/heads/main'), 'Release deve fixar a identidade do workflow oficial.');
assert.ok(bridge.includes('Nfe-Agendamento-win-x64.zip.sigstore.json'), 'Release deve publicar o bundle Sigstore.');
assert.ok(!bridge.includes('NFE_UPDATE_SIGNING_KEY_PKCS8_B64'), 'Release não deve depender de chave privada persistente.');"""
if old_block not in r:
    raise RuntimeError("Bloco RSA de release-readiness não encontrado")
r = r.replace(old_block, new_block, 1)
r = r.replace("assert.strictEqual(projectVersion, '0.1.25', 'Versão esperada para este hardening é 0.1.25.');",
              "assert.strictEqual(projectVersion, '0.1.26', 'Versão esperada para este hardening é 0.1.26.');")
r = r.replace("console.log(`OK: release usa SHA imutável, assinatura RSA-PSS, .NET 10, v${projectVersion}, auditoria de dependências, hardening do repositório e nenhuma credencial fiscal real.`);",
              "console.log(`OK: release usa SHA imutável, Sigstore keyless, .NET 10, v${projectVersion}, auditoria de dependências, hardening do repositório e nenhuma credencial fiscal real.`);")
rr.write_text(r, encoding="utf-8")

release = r"""name: Release Bridge

on:
  workflow_dispatch:
    inputs:
      version:
        description: 'Versão da release (ex.: v0.1.26)'
        required: true
        type: string

permissions:
  contents: write
  id-token: write

concurrency:
  group: release-nfe-agendamento
  cancel-in-progress: false

jobs:
  release:
    runs-on: windows-latest
    steps:
      - uses: actions/checkout@v4
        with:
          ref: ${{ github.sha }}
          fetch-depth: 0

      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: 10.0.x

      - name: Validar versão
        id: version
        shell: pwsh
        env:
          INPUT_VERSION: ${{ inputs.version }}
        run: |
          if ($env:GITHUB_REF -ne 'refs/heads/main') {
            throw "A release deve ser executada a partir da branch main. Ref atual: $env:GITHUB_REF"
          }

          $raw = $env:INPUT_VERSION.Trim()
          if ($raw -notmatch '^v?[0-9]+\.[0-9]+\.[0-9]+$') {
            throw 'Versão inválida. Use o formato v1.2.3.'
          }

          $normalized = $raw.TrimStart('v','V')
          $requested = [version]$normalized
          $tag = "v$normalized"

          git fetch --tags --force
          if (git tag --list $tag) {
            throw "A versão $tag já existe."
          }

          $versions = git tag --list 'v*.*.*' | ForEach-Object {
            $candidate = $_.TrimStart('v','V')
            $parsed = $null
            if ([version]::TryParse($candidate, [ref]$parsed)) { $parsed }
          }

          if ($versions) {
            $latest = $versions | Sort-Object -Descending | Select-Object -First 1
            if ($requested -le $latest) {
              throw "A nova versão deve ser maior que v$latest. Solicitada: $tag."
            }
          }

          "version=$normalized" >> $env:GITHUB_OUTPUT
          "tag=$tag" >> $env:GITHUB_OUTPUT

      - name: Restaurar dependências
        run: dotnet restore Nfe-Agendamento.sln

      - name: Auditar dependências NuGet
        shell: pwsh
        run: |
          $reportPath = Join-Path $env:RUNNER_TEMP 'nuget-vulnerabilities.json'
          dotnet list Nfe-Agendamento.sln package --vulnerable --include-transitive --format json | Out-File -FilePath $reportPath -Encoding utf8
          if ($LASTEXITCODE -ne 0) { throw 'Falha ao executar a auditoria de dependências NuGet.' }
          $report = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json
          $findings = @()
          foreach ($project in @($report.projects)) {
            foreach ($framework in @($project.frameworks)) {
              foreach ($package in @($framework.topLevelPackages) + @($framework.transitivePackages)) {
                if ($null -ne $package -and @($package.vulnerabilities).Count -gt 0) {
                  $findings += "$($project.path): $($package.id) $($package.resolvedVersion)"
                }
              }
            }
          }
          if ($findings.Count -gt 0) {
            $findings | ForEach-Object { Write-Error $_ }
            throw 'Dependências NuGet vulneráveis encontradas.'
          }

      - name: Executar testes .NET
        run: dotnet test Nfe-Agendamento.sln -c Release --no-restore

      - name: Testar regressão de produtos Fernando Klein
        run: node tests/js/product-mapping-regression.test.js

      - name: Testar feedback de erros fiscais
        run: node tests/js/lookup-feedback-regression.test.js

      - name: Testar contingência pelo Portal NF-e
        run: node tests/js/portal-fallback-regression.test.js

      - name: Testar botão de consulta durante bootstrap
        run: node tests/js/pairing-lookup-regression.test.js

      - name: Testar consulta em lote
        run: node tests/js/batch-lookup-regression.test.js

      - name: Testar prontidão dos workflows de release
        run: node tests/js/release-readiness-regression.test.js

      - name: Compilar solução
        run: dotnet build Nfe-Agendamento.sln -c Release --no-restore

      - name: Publicar pacote Windows
        shell: pwsh
        run: |
          dotnet publish src/NfeAgendamento.App/NfeAgendamento.App.csproj -c Release -r win-x64 --self-contained true -p:Version=${{ steps.version.outputs.version }} -p:AssemblyVersion=${{ steps.version.outputs.version }}.0 -p:FileVersion=${{ steps.version.outputs.version }}.0 -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o artifacts/nfe-agendamento
          Compress-Archive -Path artifacts/nfe-agendamento/* -DestinationPath artifacts/Nfe-Agendamento-win-x64.zip

      - name: Instalar Cosign
        uses: sigstore/cosign-installer@v4.1.2

      - name: Assinar pacote Windows com Sigstore
        shell: pwsh
        run: |
          cosign sign-blob --yes --bundle artifacts/Nfe-Agendamento-win-x64.zip.sigstore.json artifacts/Nfe-Agendamento-win-x64.zip
          if ($LASTEXITCODE -ne 0) { throw 'Falha ao assinar pacote com Sigstore.' }

      - name: Verificar assinatura Sigstore antes da publicação
        shell: pwsh
        run: |
          cosign verify-blob artifacts/Nfe-Agendamento-win-x64.zip `
            --bundle artifacts/Nfe-Agendamento-win-x64.zip.sigstore.json `
            --certificate-identity "https://github.com/joaoldsxyzbr/Nfe-Agendamento/.github/workflows/release-bridge.yml@refs/heads/main" `
            --certificate-oidc-issuer "https://token.actions.githubusercontent.com"
          if ($LASTEXITCODE -ne 0) { throw 'A verificação Sigstore do pacote falhou.' }

      - name: Criar release
        env:
          GH_TOKEN: ${{ github.token }}
        shell: pwsh
        run: |
          gh release create "${{ steps.version.outputs.tag }}" artifacts/Nfe-Agendamento-win-x64.zip artifacts/Nfe-Agendamento-win-x64.zip.sigstore.json --target "${{ github.sha }}" --title "NFe Agendamento ${{ steps.version.outputs.tag }}" --notes "v${{ steps.version.outputs.version }}: fallback do Portal integrado ao site e atualização protegida por SHA-256 + Sigstore keyless vinculada ao workflow oficial."
"""
write(".github/workflows/release-bridge.yml", release)

readme = ROOT / "README.md"
r = readme.read_text(encoding="utf-8")
r = r.replace("- `main`: em desenvolvimento após a **v0.1.25**.",
              "- `main`: preparada para a **v0.1.26**.")
r = r.replace(
    "A `main` também inclui a evolução do fallback pelo Portal: o fluxo é acionado pelo site local, o hCaptcha continua manual no WebView2 e, após o XML oficial ser validado e salvo no cache, a interface carrega a NF-e automaticamente sem nova consulta fiscal.",
    "A `main` preparada para v0.1.26 inclui o fallback do Portal acionado pelo site local, carregamento automático do XML pelo cache e atualização assinada com Sigstore keyless vinculada ao workflow oficial do GitHub Actions.")
r = r.replace(
    "A última release publicada é **v0.1.25**. A `main` contém mudanças posteriores ainda não publicadas em release.",
    "A última release publicada é **v0.1.25**. A `main` está preparada para a **v0.1.26**; a publicação é feita exclusivamente pelo Release Bridge após todos os gates.")
readme.write_text(r, encoding="utf-8")

docs = ROOT / "docs/ATUALIZACAO-E-INICIALIZACAO.md"
d = docs.read_text(encoding="utf-8")
d = d.replace("- assinatura destacada `Nfe-Agendamento-win-x64.zip.sig`;",
              "- bundle de assinatura `Nfe-Agendamento-win-x64.zip.sigstore.json`;")
d = d.replace("- assinatura RSA-PSS/SHA-256 válida contra a chave pública embutida no aplicativo.",
              "- assinatura Sigstore keyless válida para o workflow oficial `release-bridge.yml` em `main`, issuer OIDC do GitHub Actions e transparency log.")
d = d.replace("3. baixa a assinatura destacada;\n4. valida a assinatura RSA-PSS/SHA-256;",
              "3. baixa o bundle Sigstore;\n4. valida a assinatura, identidade do workflow, issuer OIDC e transparency log;")
start_marker = "### Chave de assinatura das releases"
end_marker = "## Atualização manual segura"
if start_marker in d and end_marker in d:
    s = d.index(start_marker)
    e = d.index(end_marker, s)
    replacement = """### Assinatura keyless das releases

A partir da v0.1.26, a release não depende de chave privada persistente nem de GitHub Secret de assinatura. O `Release Bridge` usa Sigstore keyless com o token OIDC efêmero do GitHub Actions.

O pacote é aceito somente quando o bundle comprova:

- issuer `https://token.actions.githubusercontent.com`;
- identidade `https://github.com/joaoldsxyzbr/Nfe-Agendamento/.github/workflows/release-bridge.yml@refs/heads/main`;
- repositório oficial `https://github.com/joaoldsxyzbr/Nfe-Agendamento`;
- execução em runner hospedado pelo GitHub;
- inclusão verificável no transparency log;
- assinatura correspondente exatamente aos bytes do ZIP cujo SHA-256 também foi validado.

O workflow verifica o bundle antes de publicar a release. O updater repete essa verificação antes de extrair qualquer arquivo.

"""
    d = d[:s] + replacement + d[e:]
docs.write_text(d, encoding="utf-8")

write("docs/superpowers/specs/2026-09-04-keyless-release-signing-design.md", """# Assinatura keyless das releases — design

## Contexto
O hardening RSA introduziu uma raiz de confiança externa, mas a chave privada correspondente nunca foi provisionada no GitHub Secret exigido pelo Release Bridge. O pipeline corretamente recusou publicar v0.1.26 sem assinatura.

## Decisão
Usar Sigstore keyless no Release Bridge. O GitHub Actions obtém um token OIDC efêmero, Fulcio emite o certificado de curta duração e a assinatura do ZIP é registrada no transparency log. Nenhuma chave privada persistente é armazenada no repositório, no runner ou em Secret.

## Política do updater
O updater mantém SHA-256 e HTTPS e exige `Nfe-Agendamento-win-x64.zip.sigstore.json`. A verificação aceita somente issuer do GitHub Actions, SAN exato do Release Bridge em main, repositório oficial, ref main, runner hospedado pelo GitHub, repositório público, transparency log e SCT válidos. Qualquer falha aborta antes da extração.

## Release
O Release Bridge recebe `id-token: write`, instala Cosign por action oficial, assina o ZIP, verifica a própria assinatura com identidade/issuer fixos e só então cria a release com ZIP + bundle.

## Compatibilidade
A v0.1.25 não depende da nova verificação para baixar a v0.1.26. A v0.1.26 passa a exigir Sigstore para todas as atualizações seguintes.
""")

write("docs/superpowers/plans/2026-09-04-keyless-release-signing-v0.1.26.md", """# Keyless Release Signing v0.1.26 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Publicar v0.1.26 sem chave privada persistente, mantendo assinatura criptográfica fail-closed.
**Architecture:** Release Bridge assina o ZIP via Sigstore keyless/GitHub OIDC; o updater valida SHA-256 e o bundle Sigstore contra a identidade fixa do workflow oficial antes do staging.
**Tech Stack:** .NET 10, Sigstore 0.5.0, GitHub Actions OIDC, Cosign 3.x.
**Spec:** `docs/superpowers/specs/2026-09-04-keyless-release-signing-design.md`

## Global Constraints
- Trabalhar direto na `main`.
- Não versionar chave privada.
- Não remover SHA-256, HTTPS, rollback ou health check.
- Release deve falhar antes da publicação se assinatura ou verificação falhar.

---

### Task 1: Verificação do updater
**Files:** `UpdateService.cs`, `SigstoreUpdateSignatureVerifier.cs`, `UpdateServiceTests.cs`
- [x] Trocar asset `.sig` por `.sigstore.json`.
- [x] Injetar verificador para testes sem rede.
- [x] Fixar issuer, workflow, repositório, ref e runner.
- [x] Validar bundle antes de extrair.

### Task 2: Release Bridge
**Files:** `.github/workflows/release-bridge.yml`, `tests/js/release-readiness-regression.test.js`
- [x] Remover dependência do Secret RSA.
- [x] Conceder `id-token: write`.
- [x] Assinar com Cosign keyless.
- [x] Verificar identidade/issuer antes de `gh release create`.
- [x] Publicar ZIP + bundle.

### Task 3: Versão e documentação
**Files:** `NfeAgendamento.App.csproj`, `README.md`, `docs/ATUALIZACAO-E-INICIALIZACAO.md`
- [x] Subir base para 0.1.26.
- [x] Documentar raiz de confiança keyless.
- [x] Validar restore, auditoria, testes, regressões e build antes do commit.
""")

print("migration prepared")
