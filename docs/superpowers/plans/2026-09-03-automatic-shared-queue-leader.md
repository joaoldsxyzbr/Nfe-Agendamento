# Automatic Shared Queue Leader Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Permitir que qualquer PC confiável e já pareado assuma automaticamente a liderança da fila compartilhada, sem trocar a identidade pública atual e sem repetir consultas fiscais ambíguas.

**Architecture:** A Central atual migra sua identidade RSA para um pacote compartilhado cifrado por uma `GroupStateKey`. Cada cliente autorizado recebe essa chave em um pacote individual cifrado com seu `ClientSecret`, guarda-a localmente via DPAPI e passa a disputar o `central.lock`. O estado de clientes autorizados e replay também passa a ser compartilhado e cifrado com a mesma chave de grupo.

**Tech Stack:** .NET 8, C#, Windows DPAPI, RSA OAEP/PSS, AES-256-GCM, filesystem/SMB locking, xUnit, GitHub Actions.

**Spec:** `docs/superpowers/specs/2026-09-03-automatic-shared-queue-leader-design.md`

## Global Constraints

- Alterar somente `main`.
- Não abrir portas TCP/HTTP adicionais.
- O certificado A1 nunca é exportado nem usado como segredo de transporte da fila.
- `central.lock` é a única autoridade de liderança.
- Sem lock exclusivo válido, nenhuma consulta SEFAZ pode começar.
- A chave pública já pareada deve permanecer a mesma durante a migração.
- Nenhum segredo privado pode ficar em claro na pasta compartilhada.
- Failover nunca repete automaticamente consulta fiscal ambígua.
- TDD obrigatório: teste vermelho antes de código de produção.

---

### Task 1: Primitivas de estado do grupo e pacotes de candidatura

**Files:**
- Create: `src/NfeAgendamento.App/SharedQueue/SharedQueueGroupState.cs`
- Modify: `src/NfeAgendamento.App/SharedQueue/SharedQueuePaths.cs`
- Modify: `src/NfeAgendamento.App/SharedQueue/CentralKeyStore.cs`
- Test: `tests/NfeAgendamento.App.Tests/SharedQueueGroupStateTests.cs`

**Interfaces:**
- Produces: `CandidateStateStore`, `SharedGroupIdentityStore`, `CandidateBundleStore`.
- Produces: `CentralKeyStore.ExportPrivateKeyPkcs8()` for one-time migration only.
- Produces paths for `group-identity.bin`, `authorized-clients.bin` and candidate bundles.

- [ ] **Step 1: Write failing tests** proving DPAPI local persistence, authenticated candidate bundle round-trip, tamper rejection, wrong-client-secret rejection, and group identity round-trip preserving the same public key.
- [ ] **Step 2: Commit RED tests** and verify CI fails only because the new APIs do not exist.
- [ ] **Step 3: Implement minimal cryptographic stores** with AES-GCM, explicit format version, AAD per file type, maximum size checks and zeroing of temporary key material.
- [ ] **Step 4: Run full tests in CI** and keep existing queue crypto tests green.
- [ ] **Step 5: Commit GREEN implementation.**

### Task 2: Estado compartilhado de clientes autorizados e replay

**Files:**
- Create: `src/NfeAgendamento.App/SharedQueue/SharedAuthorizedClientStore.cs`
- Modify: `src/NfeAgendamento.App/SharedQueue/SharedQueuePairing.cs`
- Test: `tests/NfeAgendamento.App.Tests/SharedAuthorizedClientStoreTests.cs`

**Interfaces:**
- Consumes: `CandidateStateStore.GroupStateKey`.
- Produces: `Authorize`, `TryAuthenticateAndAdvance`, `Snapshot`, `ReplaceFromLegacy`.

- [ ] **Step 1: Write failing tests** for encrypted persistence, replay block across two store instances, tamper rejection, migration from legacy authorized clients, and monotonic sequence persistence.
- [ ] **Step 2: Commit RED tests** and confirm expected CI failures.
- [ ] **Step 3: Implement shared encrypted state** using atomic temp+rename writes; only leader callers may mutate it.
- [ ] **Step 4: Expose a safe snapshot from legacy `AuthorizedClientStore`** solely for one-time bootstrap; clone and zero secrets where applicable.
- [ ] **Step 5: Run full CI and commit GREEN.**

### Task 3: Bootstrap/migração e adesão automática

**Files:**
- Create: `src/NfeAgendamento.App/SharedQueue/SharedQueueGroupBootstrapService.cs`
- Modify: `src/NfeAgendamento.App/SharedQueue/SharedQueueCentralService.cs`
- Modify: `src/NfeAgendamento.App/SharedQueue/SharedQueuePairing.cs`
- Modify: `src/NfeAgendamento.App/Program.cs`
- Test: `tests/NfeAgendamento.App.Tests/SharedQueueGroupBootstrapTests.cs`

**Interfaces:**
- Produces: `EnsureBootstrapAsync()`, `TryImportCandidateBundle()`, `IsCandidateReady`.
- Central bootstrap retains current public key.
- Pairing publishes candidate bundle before completing new-client pairing.

- [ ] **Step 1: Write failing tests** for idempotent bootstrap, same-public-key migration, existing client import without re-pair, bundle/public-key mismatch rejection and new-pair candidate publication.
- [ ] **Step 2: Commit RED tests** and confirm failures represent missing migration behavior.
- [ ] **Step 3: Implement bootstrap service** using the legacy central flag only to initialize the group when no group identity exists.
- [ ] **Step 4: Implement candidate import background path** so already-paired clients become eligible automatically.
- [ ] **Step 5: Update pairing** to persist authorization and candidate bundle before response publication.
- [ ] **Step 6: Run full CI and commit GREEN.**

### Task 4: Liderança automática e despacho local/remoto

**Files:**
- Modify: `src/NfeAgendamento.App/SharedQueue/SharedQueueCentralService.cs`
- Modify: `src/NfeAgendamento.App/SharedQueue/SharedQueueProcessor.cs`
- Modify: `src/NfeAgendamento.App/Fiscal/LookupDispatchService.cs`
- Test: `tests/NfeAgendamento.App.Tests/SharedQueueAutomaticLeaderTests.cs`
- Test: `tests/NfeAgendamento.App.Tests/SafetyRegressionTests.cs`

**Interfaces:**
- `SharedQueueCentralService.IsActive` means this PC currently owns `central.lock`.
- Standby PCs always dispatch to `SharedQueueClient`.
- Active leader dispatches directly through `NfeLookupService`.

- [ ] **Step 1: Write failing tests** showing two eligible PCs elect exactly one leader, release causes takeover, public key remains identical after takeover, and dispatch chooses direct only while active.
- [ ] **Step 2: Extend safety regression** to prove recovered requests still never trigger a second fiscal call after leader takeover.
- [ ] **Step 3: Commit RED tests** and verify expected failures.
- [ ] **Step 4: Remove `IsConfiguredAsCentral` from runtime election/dispatch decisions** while preserving it as bootstrap compatibility state.
- [ ] **Step 5: Switch processor authentication to shared authorized state.**
- [ ] **Step 6: Run full CI and commit GREEN.**

### Task 5: Certificado, Portal e UX de líder automático

**Files:**
- Modify: `src/NfeAgendamento.App/Program.cs`
- Modify: `src/NfeAgendamento.App/CentralForm.cs`
- Modify: `src/NfeAgendamento.App/TrayApplicationContext.cs`
- Test: `tests/NfeAgendamento.App.Tests/CentralUiContractTests.cs` or existing equivalent.
- Test: JS regression files covering bootstrap/Portal behavior.

**Interfaces:**
- Certificate selection is local and permitted on every trusted PC.
- Portal fallback is local and permitted on every PC with a configured A1.
- UI reports `leader`, `standby`, `share unavailable` instead of manual Central start/stop.

- [ ] **Step 1: Write/adjust failing contract tests** for automatic status labels and removal of manual-start semantics.
- [ ] **Step 2: Commit RED tests.**
- [ ] **Step 3: Update API guards** so certificate and Portal are no longer tied to `ConfiguredAsCentral`.
- [ ] **Step 4: Simplify Central/Tray UI** to diagnostics and automatic leader state; retain no manual action that could disable failover accidentally.
- [ ] **Step 5: Run .NET and JS regression suites and commit GREEN.**

### Task 6: Documentação, migração e verificação final

**Files:**
- Modify: `README.md`
- Modify: `docs/CENTRAL-LAN.md`
- Modify: `docs/ATUALIZACAO-E-INICIALIZACAO.md`
- Modify: `docs/superpowers/plans/2026-09-03-automatic-shared-queue-leader.md`

**Interfaces:**
- Documentation must state that all trusted PCs need the A1 installed/configured and shared-folder access.
- Document one-time requirement: run the former Central once after upgrading so it can bootstrap the group.

- [ ] **Step 1: Update docs** with architecture, migration, failure behavior and operational checklist.
- [ ] **Step 2: Run final GitHub Actions**: restore, all .NET tests, all JS regressions, Release build, Windows self-contained publish, ZIP and artifact upload.
- [ ] **Step 3: Review commit diff** for accidental unrelated changes, plaintext secrets, weakened path validation or retry regressions.
- [ ] **Step 4: Mark plan checkboxes complete** only for actually verified steps.
- [ ] **Step 5: Do not publish a release unless explicitly requested.**
