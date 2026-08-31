# NFe Agendamento Foundation + Single Lookup Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Criar a primeira fatia vertical utilizável do NFe Agendamento: app Windows local, site em `127.0.0.1:17345`, seleção de certificado A1, consulta única por chave, cache criptografado e download do XML.

**Architecture:** Um único processo Windows hospeda ASP.NET Core/Kestrel exclusivamente em loopback e mantém um ícone simples na bandeja. A interface web estática conversa apenas com a API local. O motor fiscal, armazenamento e certificado ficam em serviços separados e testáveis; nenhuma parte aceita acesso pela LAN.

**Tech Stack:** .NET 8 (`net8.0-windows`), ASP.NET Core/Kestrel, WinForms tray, Windows Certificate Store, DPAPI (`ProtectedData`), xUnit, HTML/CSS/JavaScript sem framework e GitHub Actions.

**Spec:** `docs/superpowers/specs/2026-08-31-local-browser-nfe-design.md`

## Global Constraints

- O host deve escutar somente em `http://127.0.0.1:17345`.
- Não criar login, usuários, servidor LAN, `distNSU`, banco de dados, fila global, dashboard ou sincronização entre PCs.
- O certificado A1 e a chave privada nunca saem do Windows Certificate Store.
- O navegador nunca recebe PFX, chave privada ou material criptográfico do certificado.
- Ações fiscais mutáveis exigem validação de `Host`, `Origin` e token anti-CSRF local.
- XMLs em repouso ficam criptografados com chave protegida por DPAPI e retenção de 24 horas.
- `cStat=656` cria cooldown local persistente de uma hora; `137` de consulta direta não cria cooldown global.
- CI nunca consulta a SEFAZ real e nunca usa certificado/XML real da empresa.
- A interface deve continuar restrita ao fluxo `chave -> consultar -> visualizar/baixar`.

---

## File Structure

```text
Nfe-Agendamento.sln
src/
  NfeAgendamento.App/
    NfeAgendamento.App.csproj
    Program.cs
    AppPaths.cs
    LocalHost.cs
    TrayApplicationContext.cs
    Security/
      LocalRequestSecurityMiddleware.cs
      CsrfTokenService.cs
    Certificates/
      CertificateService.cs
      CertificateSelection.cs
    Fiscal/
      AccessKeyValidator.cs
      FiscalCooldownStore.cs
      INfeTransport.cs
      NfeDistributionTransport.cs
      NfeLookupService.cs
      NfeLookupResult.cs
    Storage/
      EncryptedXmlCache.cs
      CacheEntry.cs
    wwwroot/
      index.html
      app.js
      styles.css

tests/
  NfeAgendamento.App.Tests/
    NfeAgendamento.App.Tests.csproj
    LocalHostTests.cs
    LocalRequestSecurityMiddlewareTests.cs
    AccessKeyValidatorTests.cs
    FiscalCooldownStoreTests.cs
    EncryptedXmlCacheTests.cs
    NfeLookupServiceTests.cs

.github/workflows/ci.yml
.gitignore
README.md
```

The first implementation cycle intentionally stops before batch, DANFE and updater. Those are separate follow-up plans so this foundation can be reviewed and piloted independently.

---

### Task 1: Bootstrap the Windows app and CI

**Files:**
- Create: `Nfe-Agendamento.sln`
- Create: `src/NfeAgendamento.App/NfeAgendamento.App.csproj`
- Create: `src/NfeAgendamento.App/Program.cs`
- Create: `src/NfeAgendamento.App/AppPaths.cs`
- Create: `tests/NfeAgendamento.App.Tests/NfeAgendamento.App.Tests.csproj`
- Create: `.github/workflows/ci.yml`
- Create: `.gitignore`
- Create: `README.md`

**Interfaces:**
- Produces: executable `NfeAgendamento.App.exe`, test project, deterministic local data root `AppPaths.LocalDataRoot`.

- [ ] **Step 1: Create solution and projects**

Run locally:

```bash
dotnet new sln -n Nfe-Agendamento
dotnet new web -n NfeAgendamento.App -o src/NfeAgendamento.App --framework net8.0
dotnet new xunit -n NfeAgendamento.App.Tests -o tests/NfeAgendamento.App.Tests --framework net8.0
dotnet sln Nfe-Agendamento.sln add src/NfeAgendamento.App/NfeAgendamento.App.csproj
dotnet sln Nfe-Agendamento.sln add tests/NfeAgendamento.App.Tests/NfeAgendamento.App.Tests.csproj
dotnet add tests/NfeAgendamento.App.Tests/NfeAgendamento.App.Tests.csproj reference src/NfeAgendamento.App/NfeAgendamento.App.csproj
```

- [ ] **Step 2: Convert the app project to Windows + tray capable**

Use this project file:

```xml
<Project Sdk="Microsoft.NET.Sdk.Web">
  <PropertyGroup>
    <TargetFramework>net8.0-windows</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <UseWindowsForms>true</UseWindowsForms>
    <OutputType>WinExe</OutputType>
    <InvariantGlobalization>false</InvariantGlobalization>
  </PropertyGroup>
</Project>
```

Test project target must also be `net8.0-windows` and include `Microsoft.NET.Test.Sdk`, `xunit`, `xunit.runner.visualstudio`, and `coverlet.collector` from the generated template.

- [ ] **Step 3: Add deterministic local paths**

Create `AppPaths.cs`:

```csharp
namespace NfeAgendamento.App;

public static class AppPaths
{
    public static string LocalDataRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "NfeAgendamento");

    public static string CacheRoot => Path.Combine(LocalDataRoot, "cache");
    public static string StateRoot => Path.Combine(LocalDataRoot, "state");
}
```

- [ ] **Step 4: Add CI**

`.github/workflows/ci.yml`:

```yaml
name: CI

on:
  push:
    branches: [main]
  pull_request:

jobs:
  test:
    runs-on: windows-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: 8.0.x
      - run: dotnet restore Nfe-Agendamento.sln
      - run: dotnet test Nfe-Agendamento.sln -c Release --no-restore
      - run: dotnet build Nfe-Agendamento.sln -c Release --no-restore
```

- [ ] **Step 5: Verify baseline**

Run:

```bash
dotnet test Nfe-Agendamento.sln -c Release
dotnet build Nfe-Agendamento.sln -c Release
```

Expected: both commands exit `0`.

- [ ] **Step 6: Commit**

```bash
git add .
git commit -m "chore: bootstrap local NFe app"
```

---

### Task 2: Enforce loopback-only hosting and local request security

**Files:**
- Create: `src/NfeAgendamento.App/LocalHost.cs`
- Create: `src/NfeAgendamento.App/Security/CsrfTokenService.cs`
- Create: `src/NfeAgendamento.App/Security/LocalRequestSecurityMiddleware.cs`
- Modify: `src/NfeAgendamento.App/Program.cs`
- Test: `tests/NfeAgendamento.App.Tests/LocalHostTests.cs`
- Test: `tests/NfeAgendamento.App.Tests/LocalRequestSecurityMiddlewareTests.cs`

**Interfaces:**
- Produces: `LocalHost.ListenUrl`, `LocalHost.Configure(WebApplicationBuilder)`, `CsrfTokenService.CurrentToken`, and middleware that allows only local `Host`/`Origin`.

- [ ] **Step 1: Write failing loopback tests**

```csharp
[Fact]
public void ListenUrl_is_fixed_to_loopback()
{
    Assert.Equal("http://127.0.0.1:17345", LocalHost.ListenUrl);
    Assert.DoesNotContain("0.0.0.0", LocalHost.ListenUrl);
}
```

- [ ] **Step 2: Run the test and verify RED**

```bash
dotnet test tests/NfeAgendamento.App.Tests/NfeAgendamento.App.Tests.csproj --filter LocalHostTests
```

Expected: compile failure because `LocalHost` does not exist.

- [ ] **Step 3: Implement fixed Kestrel binding**

```csharp
namespace NfeAgendamento.App;

public static class LocalHost
{
    public const string ListenUrl = "http://127.0.0.1:17345";

    public static void Configure(WebApplicationBuilder builder)
    {
        builder.WebHost.UseUrls(ListenUrl);
        builder.WebHost.ConfigureKestrel(options =>
        {
            options.AddServerHeader = false;
            options.Limits.MaxRequestBodySize = 256 * 1024;
        });
    }
}
```

Do not read a configurable listen address from settings in V1.

- [ ] **Step 4: Write middleware tests for hostile Host/Origin**

Cover these cases explicitly:

```csharp
[Theory]
[InlineData("evil.example", "http://127.0.0.1:17345", 403)]
[InlineData("127.0.0.1:17345", "https://evil.example", 403)]
[InlineData("127.0.0.1:17345", "http://127.0.0.1:17345", 200)]
public async Task Security_policy_rejects_unexpected_host_or_origin(
    string host, string origin, int expectedStatus)
{
    // construct DefaultHttpContext, set Host/Origin, run middleware with a next delegate that returns 200
}
```

Also test that a POST without `X-CSRF-Token` returns `403` and a valid token proceeds.

- [ ] **Step 5: Implement CSRF service and middleware**

`CsrfTokenService` must create one random 32-byte token per app process:

```csharp
using System.Security.Cryptography;

public sealed class CsrfTokenService
{
    public string CurrentToken { get; } = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

    public bool Validate(string? token) =>
        token is not null && CryptographicOperations.FixedTimeEquals(
            Convert.FromHexString(CurrentToken),
            TryDecode(token));

    private static byte[] TryDecode(string value)
    {
        try { return Convert.FromHexString(value); }
        catch (FormatException) { return Array.Empty<byte>(); }
    }
}
```

`LocalRequestSecurityMiddleware` rules:

```text
remote IP must be loopback
Host must be 127.0.0.1:17345 or localhost:17345
Origin, when present, must be http://127.0.0.1:17345 or http://localhost:17345
POST/PUT/PATCH/DELETE require exact X-CSRF-Token
JSON body over 256 KiB returns 413
```

- [ ] **Step 6: Expose bootstrap token only to the same local page**

Add:

```csharp
app.MapGet("/api/bootstrap", (CsrfTokenService csrf) =>
    Results.Ok(new { csrfToken = csrf.CurrentToken }));
```

The middleware must still require loopback + valid Host for `/api/bootstrap`.

- [ ] **Step 7: Run security tests**

```bash
dotnet test tests/NfeAgendamento.App.Tests/NfeAgendamento.App.Tests.csproj --filter "LocalHostTests|LocalRequestSecurityMiddlewareTests"
```

Expected: PASS.

- [ ] **Step 8: Commit**

```bash
git add src/NfeAgendamento.App tests/NfeAgendamento.App.Tests
git commit -m "feat: enforce loopback-only local host"
```

---

### Task 3: Add certificate selection from Windows Certificate Store

**Files:**
- Create: `src/NfeAgendamento.App/Certificates/CertificateSelection.cs`
- Create: `src/NfeAgendamento.App/Certificates/CertificateService.cs`
- Test: `tests/NfeAgendamento.App.Tests/CertificateServiceTests.cs`

**Interfaces:**
- Produces: `IReadOnlyList<CertificateSelection> ListValidCertificates()` and `X509Certificate2 GetByThumbprint(string thumbprint)`.

- [ ] **Step 1: Write certificate filtering tests**

Use generated test certificates in memory. Verify certificates without private key or outside validity are excluded from `FilterUsable`.

```csharp
[Fact]
public void FilterUsable_excludes_expired_and_no_private_key_certificates()
{
    var now = new DateTimeOffset(2026, 8, 31, 12, 0, 0, TimeSpan.Zero);
    var result = CertificateService.FilterUsable(certificates, now);
    Assert.All(result, cert => Assert.True(cert.HasPrivateKey));
}
```

- [ ] **Step 2: Verify RED**

```bash
dotnet test tests/NfeAgendamento.App.Tests/NfeAgendamento.App.Tests.csproj --filter CertificateServiceTests
```

- [ ] **Step 3: Implement certificate service**

`CertificateSelection` exposes only safe metadata:

```csharp
public sealed record CertificateSelection(
    string Thumbprint,
    string Subject,
    DateTime NotAfter);
```

`CertificateService` opens `StoreName.My`, `StoreLocation.CurrentUser`, filters `HasPrivateKey`, `NotBefore <= now < NotAfter`, and resolves by thumbprint. Do not export the certificate or private key.

- [ ] **Step 4: Add local certificate endpoints**

```text
GET  /api/certificates          -> safe metadata only
POST /api/certificate/select    -> { thumbprint }
GET  /api/certificate/current   -> safe metadata only
```

Persist only the chosen thumbprint under `AppPaths.StateRoot`. The POST is protected by CSRF middleware.

- [ ] **Step 5: Run tests**

```bash
dotnet test Nfe-Agendamento.sln -c Release
```

Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/NfeAgendamento.App tests/NfeAgendamento.App.Tests
git commit -m "feat: select local Windows certificate"
```

---

### Task 4: Implement access-key validation, persistent 656 cooldown, and encrypted XML cache

**Files:**
- Create: `src/NfeAgendamento.App/Fiscal/AccessKeyValidator.cs`
- Create: `src/NfeAgendamento.App/Fiscal/FiscalCooldownStore.cs`
- Create: `src/NfeAgendamento.App/Storage/CacheEntry.cs`
- Create: `src/NfeAgendamento.App/Storage/EncryptedXmlCache.cs`
- Test: `tests/NfeAgendamento.App.Tests/AccessKeyValidatorTests.cs`
- Test: `tests/NfeAgendamento.App.Tests/FiscalCooldownStoreTests.cs`
- Test: `tests/NfeAgendamento.App.Tests/EncryptedXmlCacheTests.cs`

**Interfaces:**
- Produces: `AccessKeyValidator.IsValid(string)`, `FiscalCooldownStore.EnsureAllowedAsync()`, `FiscalCooldownStore.BlockFor656Async()`, `EncryptedXmlCache.TryGetAsync(string)`, `EncryptedXmlCache.PutAsync(string,string)`.

- [ ] **Step 1: Write access-key validator tests**

Test 44 digits, invalid length, non-digits and modulo-11 check digit. The validator must reject before any network request.

- [ ] **Step 2: Implement modulo-11 validator**

Use NF-e weights `2..9` from right to left over the first 43 digits. Compute remainder and DV according to NF-e access-key rules.

- [ ] **Step 3: Write cooldown persistence tests**

```csharp
[Fact]
public async Task BlockFor656_persists_one_hour_across_instances()
{
    var now = DateTimeOffset.Parse("2026-08-31T12:00:00Z");
    await first.BlockFor656Async(now);
    var second = new FiscalCooldownStore(path);
    var state = await second.ReadAsync();
    Assert.Equal(now.AddHours(1), state.BlockedUntilUtc);
}
```

Also prove `137` never calls `BlockFor656Async` in lookup-service tests later.

- [ ] **Step 4: Implement cooldown store**

Persist a small JSON document under `AppPaths.StateRoot`, protected with DPAPI `DataProtectionScope.CurrentUser`. Write atomically via `file.tmp` then `File.Move(..., overwrite: true)`.

- [ ] **Step 5: Write encrypted cache tests**

Prove:

```text
Put -> TryGet returns identical XML
raw file does not contain <nfeProc or the plaintext XML
entry older than 24h returns null and is deleted
invalid/corrupt ciphertext fails closed and does not return partial XML
```

- [ ] **Step 6: Implement encrypted cache**

For each access key, use a SHA-256 filename of the key. Protect each serialized `CacheEntry` with DPAPI CurrentUser. `CacheEntry` contains `AccessKey`, `StoredAtUtc`, and `Xml`. Retention is exactly 24 hours.

- [ ] **Step 7: Run tests**

```bash
dotnet test tests/NfeAgendamento.App.Tests/NfeAgendamento.App.Tests.csproj --filter "AccessKeyValidatorTests|FiscalCooldownStoreTests|EncryptedXmlCacheTests"
```

Expected: PASS.

- [ ] **Step 8: Commit**

```bash
git add src/NfeAgendamento.App tests/NfeAgendamento.App.Tests
git commit -m "feat: protect fiscal state and XML cache"
```

---

### Task 5: Implement testable `consChNFe` lookup service

**Files:**
- Create: `src/NfeAgendamento.App/Fiscal/INfeTransport.cs`
- Create: `src/NfeAgendamento.App/Fiscal/NfeDistributionTransport.cs`
- Create: `src/NfeAgendamento.App/Fiscal/NfeLookupResult.cs`
- Create: `src/NfeAgendamento.App/Fiscal/NfeLookupService.cs`
- Test: `tests/NfeAgendamento.App.Tests/NfeLookupServiceTests.cs`

**Interfaces:**
- `INfeTransport.SendConsChNFeAsync(X509Certificate2 certificate, string accessKey, CancellationToken cancellationToken)` returns raw SOAP XML.
- `NfeLookupService.LookupAsync(string accessKey, CancellationToken cancellationToken)` returns `NfeLookupResult`.

- [ ] **Step 1: Define result model**

```csharp
public sealed record NfeLookupResult(
    bool Ok,
    string Status,
    string Message,
    string? CStat = null,
    string? Xml = null,
    bool ManifestationRequired = false);
```

- [ ] **Step 2: Write service tests with a fake transport**

Cover all of these independently:

```text
invalid key -> invalid_key and transport call count 0
cache hit -> found and transport call count 0
137 -> not_found and no cooldown
656 -> consumo_indevido and persisted one-hour cooldown
138 + procNFe matching requested key -> found + cache write
138 + resNFe -> manifestation_required
returned nfeProc for a different key -> invalid_response
HttpRequestException -> network_error
```

- [ ] **Step 3: Verify RED**

```bash
dotnet test tests/NfeAgendamento.App.Tests/NfeAgendamento.App.Tests.csproj --filter NfeLookupServiceTests
```

- [ ] **Step 4: Implement SOAP transport**

Use endpoint:

```text
https://www1.nfe.fazenda.gov.br/NFeDistribuicaoDFe/NFeDistribuicaoDFe.asmx
```

Generate only `<consChNFe><chNFe>...</chNFe></consChNFe>`; do not implement `distNSU` in this repository.

`HttpClientHandler.ClientCertificates` receives the selected `X509Certificate2`. Set timeout to 45 seconds and cap response processing to 10 MiB. Never log request SOAP containing fiscal identity or response XML.

- [ ] **Step 5: Implement response parsing defensively**

Parse XML with DTD disabled. Decompress `docZip` with a 10 MiB decompressed cap. Accept only matching `nfeProc` as complete XML; treat `resNFe` as `manifestation_required`.

When `cStat == "656"`, call `FiscalCooldownStore.BlockFor656Async(now)` before returning. When `cStat == "137"`, return `not_found` without cooldown.

- [ ] **Step 6: Add bounded retry only for transient network errors**

The lookup service may retry a network failure at most twice, with delays `2s` then `5s`. Do not retry `137`, `656`, certificate/configuration failures or parse failures.

Expose delay through an injectable `IDelay` so tests do not sleep.

- [ ] **Step 7: Run lookup tests**

```bash
dotnet test tests/NfeAgendamento.App.Tests/NfeAgendamento.App.Tests.csproj --filter NfeLookupServiceTests
```

Expected: PASS with no real SEFAZ calls.

- [ ] **Step 8: Commit**

```bash
git add src/NfeAgendamento.App tests/NfeAgendamento.App.Tests
git commit -m "feat: add safe NF-e lookup by access key"
```

---

### Task 6: Build the minimal browser UI and single-lookup API

**Files:**
- Create: `src/NfeAgendamento.App/wwwroot/index.html`
- Create: `src/NfeAgendamento.App/wwwroot/app.js`
- Create: `src/NfeAgendamento.App/wwwroot/styles.css`
- Modify: `src/NfeAgendamento.App/Program.cs`
- Create: `src/NfeAgendamento.App/TrayApplicationContext.cs`
- Test: `tests/NfeAgendamento.App.Tests/ApiContractTests.cs`

**Interfaces:**
- `POST /api/nfe/lookup` body `{ "accessKey": "..." }`.
- Successful response uses `application/xml` and exact XML body.
- Failure response uses JSON `{ status, message, cStat?, manifestationRequired? }`.

- [ ] **Step 1: Write API contract tests**

Using an in-memory host with fake `NfeLookupService` dependency, verify:

```text
invalid key -> 400
not_found -> 404
manifestation_required -> 409
cooldown/consumo_indevido -> 429
network_error -> 502
found -> 200 application/xml
POST without CSRF -> 403
```

- [ ] **Step 2: Verify RED**

```bash
dotnet test tests/NfeAgendamento.App.Tests/NfeAgendamento.App.Tests.csproj --filter ApiContractTests
```

- [ ] **Step 3: Wire Program.cs**

Startup order:

```csharp
ApplicationConfiguration.Initialize();
var builder = WebApplication.CreateBuilder(args);
LocalHost.Configure(builder);
// register security, certificate, cache, cooldown, transport and lookup services
var app = builder.Build();
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseMiddleware<LocalRequestSecurityMiddleware>();
// map /api/bootstrap, certificate endpoints and /api/nfe/lookup
await app.StartAsync();
Application.Run(new TrayApplicationContext(LocalHost.ListenUrl));
await app.StopAsync();
```

If port `17345` is occupied, catch the startup bind exception and show one clear Windows message: `A porta local 17345 já está em uso. Feche a outra instância do NFe Agendamento ou o programa que está usando essa porta.` Then exit; do not silently choose another port.

- [ ] **Step 4: Build a minimal UI**

`index.html` must contain only:

```text
NFe Agendamento
[ certificado atual / Configurar ]
[ chave de 44 dígitos                         ] [ Consultar ]
status curto
[ Visualizar XML ] [ Baixar XML ] when found
```

No login, dashboard, metrics, NSU or technical queue.

`app.js` boot sequence:

```javascript
let csrfToken = '';

async function boot() {
  const response = await fetch('/api/bootstrap', { cache: 'no-store' });
  ({ csrfToken } = await response.json());
  await refreshCertificate();
}

async function postJson(path, body) {
  return fetch(path, {
    method: 'POST',
    cache: 'no-store',
    headers: {
      'content-type': 'application/json',
      'X-CSRF-Token': csrfToken
    },
    body: JSON.stringify(body)
  });
}
```

Do not store the CSRF token in localStorage/sessionStorage.

- [ ] **Step 5: Add tray icon**

Tray menu only:

```text
Abrir sistema
Configurar certificado
Sair
```

`Abrir sistema` uses `Process.Start(new ProcessStartInfo(LocalHost.ListenUrl) { UseShellExecute = true })`.

- [ ] **Step 6: Run full verification**

```bash
dotnet test Nfe-Agendamento.sln -c Release
dotnet build Nfe-Agendamento.sln -c Release
```

Expected: PASS / exit `0`.

- [ ] **Step 7: Manual smoke test with simulated fiscal transport**

Run the app with a development-only fake transport selected by environment variable `NFE_AGENDAMENTO_FAKE_SEFAZ=1`. Verify browser opens locally, certificate UI loads, a fixture access key returns sanitized XML, and download works. Production startup must ignore this variable unless build configuration is `Debug`.

- [ ] **Step 8: Update README**

README must state:

```text
- app installed independently on each PC
- browser address: http://127.0.0.1:17345
- certificate stays in Windows Certificate Store
- no LAN/server account
- current milestone: single-key lookup
- batch, DANFE and updater are subsequent milestones
```

- [ ] **Step 9: Commit**

```bash
git add .
git commit -m "feat: deliver local single NF-e lookup slice"
```

---

## Self-Review Checklist

- Spec coverage for this milestone: local-only host, browser UI, A1 certificate, single-key `consChNFe`, encrypted 24h cache, `137`/`656`, controlled network retry, XML download and CI are covered.
- Explicitly deferred to separate plans: batch/ZIP, DANFE/print, installer/update signing and three-PC pilot.
- No `distNSU`, LAN binding, login, database or persistent queue is introduced.
- Security boundary is explicit: loopback + Host + Origin + CSRF, with certificate material never exposed.
- All real SEFAZ traffic is excluded from automated tests.
