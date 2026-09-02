# Shared Folder Queue Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Substituir a comunicação HTTP LAN entre PCs por uma fila criptografada em `P:\01-Nfe agendamento`, mantendo o A1 e toda consulta SEFAZ somente no PC configurado manualmente como Central.

**Architecture:** Cada PC mantém a interface local em `127.0.0.1:17345`. Clientes cifram pedidos com AES-GCM e a chave pública RSA da Central, publicam envelopes por escrita atômica no compartilhamento e aguardam a resposta cifrada. Um serviço hospedado no PC configurado como Central mantém lock/heartbeat, processa a fila e delega ao `NfeLookupService` existente, preservando cache, deduplicação, cooldown e auditoria.

**Tech Stack:** .NET 8 Windows, ASP.NET Core/Kestrel, Windows Forms, `System.Security.Cryptography`, DPAPI (`ProtectedData`), filesystem/SMB, xUnit.

**Spec:** `docs/superpowers/specs/2026-09-02-shared-folder-queue-design.md`

## Global Constraints

- Raiz operacional fixa: `P:\01-Nfe agendamento`.
- O app nunca enumera nem modifica `P:\` ou pastas irmãs.
- Subdiretórios permitidos: `fila`, `processando`, `respostas`, `status`.
- O marcador `.nfe-agendamento` é obrigatório; clientes não criam a raiz compartilhada.
- Chave NF-e e XML nunca ficam em texto puro no compartilhamento.
- Chave privada RSA da Central nunca sai do armazenamento local protegido por DPAPI.
- Certificado A1 e chave privada fiscal permanecem somente no PC Central.
- Cada instância web escuta somente em `127.0.0.1:17345`.
- Nenhum fallback deve abrir porta, alterar firewall ou voltar para HTTP LAN.
- `ConfiguredAsCentral` começa `false` em instalações novas e **não** é inferido do antigo `Enabled`; isso evita transformar clientes antigos em Centrais após atualização.
- Depois de o usuário marcar manualmente o PC como Central, a preferência local persiste e o app tenta reassumir automaticamente em futuras inicializações.
- `Parar Central` persiste `ConfiguredAsCentral = false`.
- CI não usa `P:` real, certificado real nem SEFAZ real.

---

### Task 1: Travar o filesystem na pasta dedicada

**Files:**
- Create: `src/NfeAgendamento.App/SharedQueue/SharedQueuePaths.cs`
- Create: `tests/NfeAgendamento.App.Tests/SharedQueuePathsTests.cs`

**Interfaces:**
- Produces: `SharedQueuePaths(string? rootOverride = null)`
- Produces: `Root`, `QueueDirectory`, `ProcessingDirectory`, `ResponsesDirectory`, `StatusDirectory`, `MarkerPath`
- Produces: `InitializeAsCentral()` e `ValidateForClient()`
- Produces: `RequestPath(Guid)`, `ProcessingPath(Guid)`, `ResponsePath(Guid)`, `StatusPath(string)`

- [ ] **Step 1: Write failing path-containment tests**

```csharp
[Fact]
public void Production_root_is_the_dedicated_folder()
{
    Assert.Equal(@"P:\01-Nfe agendamento", SharedQueuePaths.DefaultRoot);
}

[Fact]
public void Request_paths_never_escape_the_configured_root()
{
    var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
    var paths = new SharedQueuePaths(root);
    var request = paths.RequestPath(Guid.Parse("11111111-1111-1111-1111-111111111111"));

    Assert.StartsWith(Path.GetFullPath(root) + Path.DirectorySeparatorChar, Path.GetFullPath(request), StringComparison.OrdinalIgnoreCase);
    Assert.EndsWith(Path.Combine("fila", "11111111111111111111111111111111.req"), request, StringComparison.OrdinalIgnoreCase);
}

[Theory]
[InlineData("..")]
[InlineData(@"C:\Windows")]
[InlineData(@"P:\outro")]
public void Arbitrary_status_names_are_rejected(string value)
{
    var paths = new SharedQueuePaths(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));
    Assert.Throws<ArgumentException>(() => paths.StatusPath(value));
}
```

- [ ] **Step 2: Run tests and verify RED**

Run: `dotnet test tests/NfeAgendamento.App.Tests/NfeAgendamento.App.Tests.csproj -c Release --filter SharedQueuePathsTests`

Expected: FAIL because `SharedQueuePaths` does not exist.

- [ ] **Step 3: Implement the constrained path builder**

Core shape:

```csharp
public sealed class SharedQueuePaths
{
    public const string DefaultRoot = @"P:\01-Nfe agendamento";
    public const string MarkerContents = "nfe-agendamento-share-v1";
    private readonly string _rootWithSeparator;

    public SharedQueuePaths(string? rootOverride = null)
    {
        Root = Path.GetFullPath(rootOverride ?? DefaultRoot).TrimEnd(Path.DirectorySeparatorChar);
        _rootWithSeparator = Root + Path.DirectorySeparatorChar;
    }

    public string Root { get; }
    public string QueueDirectory => Child("fila");
    public string ProcessingDirectory => Child("processando");
    public string ResponsesDirectory => Child("respostas");
    public string StatusDirectory => Child("status");
    public string MarkerPath => Path.Combine(Root, ".nfe-agendamento");

    public string RequestPath(Guid id) => Path.Combine(QueueDirectory, $"{id:N}.req");
    public string ProcessingPath(Guid id) => Path.Combine(ProcessingDirectory, $"{id:N}.req");
    public string ResponsePath(Guid id) => Path.Combine(ResponsesDirectory, $"{id:N}.res");

    private string Child(string name)
    {
        var full = Path.GetFullPath(Path.Combine(Root, name));
        if (!full.StartsWith(_rootWithSeparator, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Caminho fora da pasta do NFe Agendamento.");
        return full;
    }
}
```

`InitializeAsCentral()` must first require `Directory.Exists(Root)`, then create only the four known subdirectories and marker. It must never call `Directory.CreateDirectory(Root)`.

- [ ] **Step 4: Add marker validation tests and make them pass**

Test valid marker, missing marker, wrong marker and missing root. Run the same filtered test command until PASS.

- [ ] **Step 5: Commit**

Commit message: `Criar raiz segura da fila compartilhada`

---

### Task 2: Implementar criptografia dos envelopes e segredos pendentes

**Files:**
- Create: `src/NfeAgendamento.App/SharedQueue/SharedQueueModels.cs`
- Create: `src/NfeAgendamento.App/SharedQueue/SharedQueueCrypto.cs`
- Create: `src/NfeAgendamento.App/SharedQueue/PendingRequestSecretStore.cs`
- Create: `tests/NfeAgendamento.App.Tests/SharedQueueCryptoTests.cs`

**Interfaces:**
- Produces: `QueueRequestEnvelope`, `QueueResponseEnvelope`, `QueueHeartbeat`, `QueueLookupPayload`
- Produces: `SharedQueueCrypto.CreateClientRequest(...)`
- Produces: `SharedQueueCrypto.OpenRequest(...)`
- Produces: `SharedQueueCrypto.CreateResponse(...)`
- Produces: `SharedQueueCrypto.OpenResponse(...)`
- Produces: `PendingRequestSecretStore.SaveAsync(Guid, byte[])`, `LoadAsync(Guid)`, `Delete(Guid)`

- [ ] **Step 1: Write failing crypto tests**

```csharp
[Fact]
public void Request_envelope_does_not_expose_access_key()
{
    using var rsa = RSA.Create(2048);
    const string accessKey = "42260912345678000123550010000000011000000019";
    var created = SharedQueueCrypto.CreateClientRequest(Guid.NewGuid(), accessKey, rsa.ExportSubjectPublicKeyInfo());
    var json = JsonSerializer.Serialize(created.Envelope);

    Assert.DoesNotContain(accessKey, json, StringComparison.Ordinal);
    Assert.Equal(32, created.AesKey.Length);
}

[Fact]
public void Tampered_request_is_rejected()
{
    using var rsa = RSA.Create(2048);
    var created = SharedQueueCrypto.CreateClientRequest(Guid.NewGuid(), "42260912345678000123550010000000011000000019", rsa.ExportSubjectPublicKeyInfo());
    created.Envelope.Ciphertext[0] ^= 0x01;

    Assert.Throws<CryptographicException>(() => SharedQueueCrypto.OpenRequest(created.Envelope, rsa));
}
```

- [ ] **Step 2: Run filtered tests and verify RED**

Run: `dotnet test tests/NfeAgendamento.App.Tests/NfeAgendamento.App.Tests.csproj -c Release --filter SharedQueueCryptoTests`

- [ ] **Step 3: Implement AES-GCM + RSA OAEP-SHA256**

Use 32-byte AES keys, 12-byte nonces and 16-byte tags. Encrypt the AES key with:

```csharp
rsa.Encrypt(aesKey, RSAEncryptionPadding.OaepSHA256)
```

Encrypt payloads with authenticated associated data containing protocol version and request ID so an envelope cannot be retargeted to another request ID.

- [ ] **Step 4: Protect pending AES keys locally with DPAPI**

`PendingRequestSecretStore` stores only under `AppPaths.StateRoot/shared-queue/pending/{requestId:N}.key`, using:

```csharp
ProtectedData.Protect(key, Entropy, DataProtectionScope.CurrentUser)
```

and the matching `Unprotect`. Atomic `.tmp` + `File.Move(..., overwrite: true)` is required.

- [ ] **Step 5: Add round-trip response tests**

Verify `NfeLookupResult` with XML survives request/response encryption, the serialized shared files do not contain XML, wrong AES key fails and altered tag fails.

- [ ] **Step 6: Run tests and commit**

Commit message: `Criptografar transporte da fila compartilhada`

---

### Task 3: Persistir o papel do PC e garantir Central única

**Files:**
- Modify: `src/NfeAgendamento.App/CentralSettings.cs`
- Create: `src/NfeAgendamento.App/SharedQueue/SharedQueueCentralLease.cs`
- Create: `tests/NfeAgendamento.App.Tests/SharedQueueCentralLeaseTests.cs`
- Modify: `tests/NfeAgendamento.App.Tests/CentralModeTests.cs`

**Interfaces:**
- `CentralSettings(bool ConfiguredAsCentral = false)`
- `CentralStateService.IsConfiguredAsCentral`
- `CentralStateService.SetConfiguredAsCentral(bool)`
- `SharedQueueCentralLease.TryAcquire(SharedQueuePaths)` returning an owned lease or `null`

- [ ] **Step 1: Replace the dangerous legacy default test**

```csharp
[Fact]
public void Central_settings_default_to_client_when_file_does_not_exist()
{
    var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "central.json");
    Assert.False(new CentralSettingsStore(path).Load().ConfiguredAsCentral);
}

[Fact]
public void Legacy_enabled_flag_is_not_migrated_to_configured_central()
{
    var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(root);
    var path = Path.Combine(root, "central.json");
    File.WriteAllText(path, "{\"Enabled\":true}");

    Assert.False(new CentralSettingsStore(path).Load().ConfiguredAsCentral);
}
```

This migration behavior is mandatory because old versions defaulted `Enabled = true` and must not turn every upgraded client into a Central.

- [ ] **Step 2: Implement the new persisted setting**

`CentralSettingsStore.Load()` deserializes the new property; missing `ConfiguredAsCentral` resolves to `false`. Rename state methods/properties and update event behavior.

- [ ] **Step 3: Write competing lease test**

```csharp
using var first = SharedQueueCentralLease.TryAcquire(paths);
using var second = SharedQueueCentralLease.TryAcquire(paths);
Assert.NotNull(first);
Assert.Null(second);
```

- [ ] **Step 4: Implement lease using an open `FileStream` with `FileShare.None`**

Keep the handle alive for the full lifetime of the lease. Disposing releases the lock. Never delete files outside `status`.

- [ ] **Step 5: Run `CentralModeTests|SharedQueueCentralLeaseTests` and commit**

Commit message: `Definir papel persistente e lock da Central`

---

### Task 4: Publicar heartbeat e reassumir automaticamente a Central

**Files:**
- Create: `src/NfeAgendamento.App/SharedQueue/SharedQueueCentralService.cs`
- Create: `src/NfeAgendamento.App/SharedQueue/CentralKeyStore.cs`
- Create: `tests/NfeAgendamento.App.Tests/SharedQueueCentralServiceTests.cs`

**Interfaces:**
- `SharedQueueCentralService : BackgroundService`
- `bool IsActive`
- `string? LastError`
- `DateTimeOffset? LastHeartbeatUtc`
- `byte[] CentralKeyStore.GetOrCreatePublicKey()`
- `RSA CentralKeyStore.OpenPrivateKey()`

- [ ] **Step 1: Write tests for activation/retry/stop**

Use a temporary root. Assert:
- configured client does not acquire lease;
- `SetConfiguredAsCentral(true)` causes service to initialize the known directories, acquire lease and publish heartbeat;
- if the root is unavailable at startup, service records an operational error and retries later instead of modifying another path;
- `SetConfiguredAsCentral(false)` releases the lease and stops heartbeat.

- [ ] **Step 2: Implement DPAPI-protected central RSA keypair**

Persist PKCS#8 private key only under `AppPaths.StateRoot/shared-queue/central-private-key.bin`, protected with DPAPI CurrentUser. Publish only `ExportSubjectPublicKeyInfo()` into heartbeat.

- [ ] **Step 3: Implement heartbeat loop**

Use a 2-second heartbeat and a 5-second retry while configured but unable to acquire the share. Heartbeat writes atomically to `status/heartbeat.json`.

- [ ] **Step 4: Verify two services cannot both become active**

Add an integration-style temporary-folder test with two `CentralStateService` instances both configured true. Exactly one must report `IsActive`.

- [ ] **Step 5: Run tests and commit**

Commit message: `Adicionar heartbeat e retomada automática da Central`

---

### Task 5: Implementar cliente e processador da fila

**Files:**
- Create: `src/NfeAgendamento.App/SharedQueue/SharedQueueClient.cs`
- Create: `src/NfeAgendamento.App/SharedQueue/SharedQueueProcessor.cs`
- Create: `src/NfeAgendamento.App/Fiscal/LookupDispatchService.cs`
- Create: `tests/NfeAgendamento.App.Tests/SharedQueueFlowTests.cs`

**Interfaces:**
- `Task<NfeLookupResult> SharedQueueClient.LookupAsync(string accessKey, CancellationToken)`
- `Task<bool> SharedQueueProcessor.ProcessOneAsync(CancellationToken)`
- `Task<NfeLookupResult> LookupDispatchService.LookupAsync(string accessKey, CancellationToken)`

- [ ] **Step 1: Write a full temporary-folder flow test**

The test creates a fake central RSA identity and a fake lookup delegate returning:

```csharp
new NfeLookupResult(NfeLookupStatus.Found, "<nfeProc>segredo</nfeProc>", "138", "Documento localizado.", false)
```

Start client lookup, process one queued item, then assert the client receives the exact result and no `.req`/`.res` file contains either the 44-digit access key or `<nfeProc>segredo</nfeProc>`.

- [ ] **Step 2: Implement atomic publishing and polling**

All writes use same-directory temp files and rename. Client polls `respostas/{id}.res` every 250 ms with a 90-second deadline. Heartbeat older than 10 seconds returns a controlled `NfeLookupStatus.Failed` message `Central offline ou indisponível.`.

- [ ] **Step 3: Implement claim-by-move in the processor**

Enumerate only `SharedQueuePaths.QueueDirectory` with pattern `*.req`. Parse request ID strictly from the filename. Claim with `File.Move(queuePath, processingPath)`; IOException caused by another claimant means skip, not failure.

After decrypting, validate `AccessKeyValidator.IsValid()` before invoking fiscal code. Invalid/authentication-failed envelopes are removed/quarantined only inside `processando` and never reach SEFAZ.

- [ ] **Step 4: Preserve existing fiscal authority**

`SharedQueueProcessor` receives an `IServiceScopeFactory`; inside each claimed request it creates a scope and resolves `NfeLookupService`. This keeps `FiscalRequestCoordinator`, `FiscalOperationGate`, `FiscalCooldownStore`, `EncryptedXmlCache` and `FiscalAuditLog` authoritative.

- [ ] **Step 5: Implement role dispatch without resolving certificate services on clients**

`LookupDispatchService` must not constructor-inject `NfeLookupService`, because that would resolve `INfeDistributionTransport` and certificate state on client PCs. Use the current request scope's `IServiceProvider` lazily:

```csharp
if (_centralState.IsConfiguredAsCentral)
    return await _services.GetRequiredService<NfeLookupService>().LookupAsync(accessKey, cancellationToken);

return await _sharedQueueClient.LookupAsync(accessKey, cancellationToken);
```

- [ ] **Step 6: Add timeout, tamper, duplicate-claim and cleanup tests**

Verify client timeout does not delete another request, only one processor can claim a file, consumed responses and pending DPAPI secrets are removed, and temporary files are ignored.

- [ ] **Step 7: Run tests and commit**

Commit message: `Processar consultas pela pasta compartilhada`

---

### Task 6: Integrar fila ao servidor local e remover dependência de LAN/firewall

**Files:**
- Modify: `src/NfeAgendamento.App/Program.cs`
- Modify: `src/NfeAgendamento.App/LocalHost.cs`
- Modify: `src/NfeAgendamento.App/Security/LocalRequestSecurityMiddleware.cs`
- Modify: `tests/NfeAgendamento.App.Tests/LocalRequestSecurityMiddlewareTests.cs`
- Modify: `tests/NfeAgendamento.App.Tests/CentralAppTests.cs`

**Interfaces:**
- `POST /api/nfe/lookup` continues unchanged for the browser.
- `GET /api/bootstrap` returns `configuredAsCentral`, `centralActive`, `centralOnline`, `shareAvailable` in addition to CSRF token.

- [ ] **Step 1: Write tests proving the web server is loopback-only**

Change `LocalHost` tests so `GetListenUrl(...)` always equals `http://127.0.0.1:17345` even when legacy `--lan` appears. Delete expectations for `0.0.0.0`, mDNS and LAN hosts.

- [ ] **Step 2: Simplify `LocalHost.Configure`**

Always call:

```csharp
builder.WebHost.UseUrls(LocalHost.ListenUrl);
```

Remove `LanListenUrl`, `LanBrowserUrl`, `IsLanMode` and LAN argument switching.

- [ ] **Step 3: Make middleware strictly local**

Allow only loopback remote IP plus hosts `127.0.0.1:17345` / `localhost:17345`. Remove LAN host/origin exceptions and the dependency on `CentralStateService` for remote authorization.

- [ ] **Step 4: Register queue services**

Register `SharedQueuePaths`, `CentralKeyStore`, `PendingRequestSecretStore`, `SharedQueueClient`, `SharedQueueProcessor`, `SharedQueueCentralService` as hosted/singleton services as appropriate, and `LookupDispatchService` scoped.

Change `/api/nfe/lookup` to resolve `LookupDispatchService` instead of `NfeLookupService` directly.

- [ ] **Step 5: Gate certificate administration by local role**

On `/api/certificates`, `/api/certificate/current` and `/api/certificate/select`, reject with 403/409 when `!state.IsConfiguredAsCentral`. This ensures clients do not administer or depend on local certificates.

- [ ] **Step 6: Stop starting mDNS/network-name infrastructure**

Remove `NetworkNameService.Start()` from `Program.Main`. Do not touch Windows Firewall.

- [ ] **Step 7: Run API/security tests and commit**

Commit message: `Trocar LAN pelo transporte compartilhado local`

---

### Task 7: Atualizar painel da Central e interface web

**Files:**
- Modify: `src/NfeAgendamento.App/CentralForm.cs`
- Modify: `src/NfeAgendamento.App/TrayApplicationContext.cs`
- Modify: `src/NfeAgendamento.App/wwwroot/app.js`
- Modify: `tests/NfeAgendamento.App.Tests/CentralModeTests.cs`
- Modify: `tests/NfeAgendamento.App.Tests/CentralNetworkDiagnosticsTests.cs`

**Interfaces:**
- Primary actions remain `Iniciar Central`, `Parar Central`, `Abrir sistema`.
- Panel diagnostics become role/share/lock/heartbeat/processor instead of IP/port/firewall.

- [ ] **Step 1: Change UI expectation tests first**

Assert panel copy references `P:\01-Nfe agendamento`, `Central ativa`, `Central offline`, and contains no operational `Configurar firewall` action.

- [ ] **Step 2: Rewire start/stop buttons**

`Iniciar Central` calls `SetConfiguredAsCentral(true)`. `Parar Central` calls `SetConfiguredAsCentral(false)`. Runtime status shown as:
- `Central ativa` when lease is active;
- `Central aguardando pasta` when configured but share unavailable;
- `Conflito: outra Central ativa` when configured but lease is held elsewhere;
- `Este PC é cliente` when not configured.

- [ ] **Step 3: Replace network diagnostics**

Panel rows should report:
- Papel deste PC
- Pasta compartilhada
- Lock da Central
- Heartbeat
- Processador

Remove firewall configuration button and any UAC prompt path from the normal UI.

- [ ] **Step 4: Simplify tray menu**

Remove `Copiar endereço da Central` and network-address display. Keep `Abrir Central`, `Abrir sistema`, certificate config (enabled only when configured as Central), update, startup and exit.

- [ ] **Step 5: Make browser certificate UI role-aware**

Read `configuredAsCentral` from bootstrap. Clients hide/disable certificate selection and use the same lookup button; status text reflects Central offline/share errors returned by local backend.

- [ ] **Step 6: Run UI/static tests and commit**

Commit message: `Adaptar painel ao modo de fila compartilhada`

---

### Task 8: Remover legado LAN/firewall e atualizar documentação

**Files:**
- Delete when no longer referenced: `src/NfeAgendamento.App/WindowsFirewallService.cs`
- Delete when no longer referenced: `src/NfeAgendamento.App/NetworkNameService.cs`
- Delete or replace when no longer referenced: `src/NfeAgendamento.App/CentralNetworkInfo.cs`
- Delete or replace: `src/NfeAgendamento.App/CentralNetworkDiagnostics.cs`
- Delete/update matching legacy tests
- Modify: `README.md`
- Modify: `docs/CENTRAL-LAN.md` (rename content conceptually to shared-folder operation; keep path if avoiding broken links)
- Modify: `docs/superpowers/specs/2026-09-01-central-lan-architecture-design.md` with a superseded notice

**Interfaces:**
- No production path may invoke `New-NetFirewallRule`, mDNS or bind `0.0.0.0`.

- [ ] **Step 1: Search for all legacy references**

Search repository for:
- `WindowsFirewallService`
- `NetworkNameService`
- `0.0.0.0:17345`
- `nfeagendamento.local`
- `BuildAccessUrl`
- `Configurar firewall`

Only historical/spec references explicitly marked superseded may remain.

- [ ] **Step 2: Delete unreachable production code and update tests**

Remove legacy files only after Tasks 1–7 are green. Keep no dormant fallback that could silently expose port 17345 again.

- [ ] **Step 3: Update operator documentation**

README must explain:
1. copy the app folder normally to each PC;
2. ensure all PCs can use `P:\01-Nfe agendamento`;
3. on the PC with A1 click `Iniciar Central` once;
4. that preference persists locally and reassumes after restart;
5. other PCs remain clients automatically;
6. no firewall rule is required for multi-PC use.

- [ ] **Step 4: Commit**

Commit message: `Remover arquitetura LAN substituída pela fila`

---

### Task 9: Verificação completa e regressão de release

**Files:**
- Modify only if required by failing regression: `.github/workflows/ci.yml`
- Modify only if required by release assertions: release-readiness tests/scripts already present in repository

**Interfaces:**
- Existing release pipeline remains the authority for producing the Windows package.

- [ ] **Step 1: Run restore**

Run: `dotnet restore Nfe-Agendamento.sln`

Expected: success.

- [ ] **Step 2: Run all tests**

Run: `dotnet test Nfe-Agendamento.sln -c Release --no-restore`

Expected: all tests PASS.

- [ ] **Step 3: Run build**

Run: `dotnet build Nfe-Agendamento.sln -c Release --no-restore`

Expected: build PASS with no compile errors.

- [ ] **Step 4: Run existing repository regression scripts exactly as CI does**

Use `.github/workflows/ci.yml` as the source of truth. Fernando Klein, fiscal feedback and release-readiness regressions must all pass.

- [ ] **Step 5: Verify source invariants**

Repository search must confirm production code has:
- no `UseUrls("http://0.0.0.0:17345")`;
- no firewall configuration call;
- no mDNS startup;
- exactly one production default share root `P:\01-Nfe agendamento`;
- no plaintext XML/access-key serialization into shared-queue files.

- [ ] **Step 6: Verify GitHub Actions on `main`**

Push final commits to `main`, wait for CI completion and inspect failed job logs if any. Do not claim completion until the current `main` run concludes `success`.

- [ ] **Step 7: Physical acceptance after release**

On company PCs:
1. Central PC opens app locally and activates Central;
2. client PC without A1 sees recent heartbeat;
3. client submits a known NF-e and receives XML/DANFE;
4. inspect `P:\01-Nfe agendamento` and confirm files are opaque/ciphertext;
5. restart Central PC and confirm automatic reassumption;
6. stop Central manually and confirm it does not reassume after another restart;
7. confirm files outside the dedicated folder were untouched.
