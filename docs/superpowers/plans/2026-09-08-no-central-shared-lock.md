# No Central Shared Lock Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Remover central/pareamento do fluxo de produção e coordenar todas as consultas fiscais diretamente pela pasta compartilhada.

**Architecture:** Cada PC consulta a SEFAZ com seu certificado A1 local. `FiscalOperationGate` combina serialização local com um lock exclusivo SMB em `status/fiscal.lock`; `FiscalCooldownStore` compartilha apenas o bloqueio 656; XML/cache permanece local via DPAPI.

**Tech Stack:** .NET 10, WinForms, ASP.NET Core minimal APIs, JavaScript, SMB/file locking, DPAPI, GitHub Actions.

**Spec:** `docs/superpowers/specs/2026-09-08-no-central-shared-lock-design.md`

## Global Constraints

- Alterações diretamente na `main`.
- Servidor HTTP continua apenas em `127.0.0.1:17345`.
- Certificado A1 é local em cada PC.
- Nenhuma consulta fiscal pode começar sem a pasta compartilhada disponível.
- Nenhum fluxo de produção depende de pareamento, líder ou Central.
- Cache XML permanece criptografado localmente com DPAPI.

---

### Task 1: Lock fiscal compartilhado

**Files:**
- Modify: `src/NfeAgendamento.App/SharedQueue/SharedQueuePaths.cs`
- Modify: `src/NfeAgendamento.App/Fiscal/FiscalOperationGate.cs`
- Modify: `tests/NfeAgendamento.App.Tests/FiscalOperationGateTests.cs`

**Interfaces:**
- Produces: `SharedQueuePaths.FiscalLockPath`
- Produces: `FiscalOperationGate(SharedQueuePaths paths, int maxPendingOperations = DefaultMaxPendingOperations)`

- [ ] **Step 1: Write failing tests** demonstrando que dois gates com a mesma raiz compartilhada não obtêm lease simultaneamente e que o lock é liberado após `Dispose`.
- [ ] **Step 2: Run tests and confirm RED** com `dotnet test Nfe-Agendamento.sln -c Release --filter FiscalOperationGateTests`.
- [ ] **Step 3: Implement minimal shared lock** usando `FileStream(..., FileShare.None)` e espera curta/repetida até o handle ficar disponível.
- [ ] **Step 4: Run focused tests and confirm GREEN**.
- [ ] **Step 5: Commit** o gate e seus testes.

### Task 2: Cooldown compartilhado sem chave de grupo

**Files:**
- Modify: `src/NfeAgendamento.App/Fiscal/FiscalCooldownStore.cs`
- Modify: `tests/NfeAgendamento.App.Tests/FiscalCooldownStoreTests.cs`

**Interfaces:**
- Produces: `FiscalCooldownStore(SharedQueuePaths paths)`

- [ ] **Step 1: Write failing test** em que duas instâncias usando a mesma raiz enxergam o mesmo `BlockedUntilUtc` sem `CandidateStateStore`.
- [ ] **Step 2: Run focused test and confirm RED**.
- [ ] **Step 3: Implement atomic JSON shared state** com validação estrita e sem segredo de grupo.
- [ ] **Step 4: Run focused tests and confirm GREEN**.
- [ ] **Step 5: Commit**.

### Task 3: Composição sem central/pareamento

**Files:**
- Modify: `src/NfeAgendamento.App/Program.cs`
- Modify: `src/NfeAgendamento.App/Fiscal/LookupDispatchService.cs`
- Modify: `src/NfeAgendamento.App/Fiscal/NfeLookupService.cs`
- Modify: `tests/NfeAgendamento.App.Tests/ProductionCompositionRegressionTests.cs`
- Modify/Delete: regressões de pareamento que afirmam o comportamento antigo.

**Interfaces:**
- `LookupDispatchService.LookupAsync` resolve diretamente `NfeLookupService`.
- `/api/bootstrap` retorna `mode = "shared_lock"`, `shareAvailable`, `sharedFolder`, `pairingRequired = false`.

- [ ] **Step 1: Change regression tests first** para exigir ausência dos endpoints/serviços de pareamento e uso de cache local/cooldown compartilhado/gate compartilhado.
- [ ] **Step 2: Confirm RED** no CI/testes.
- [ ] **Step 3: Remove production composition** de Central, líder, queue client/processors, pairing, group bootstrap e rotation.
- [ ] **Step 4: Wire local encrypted cache, shared cooldown and shared fiscal gate**.
- [ ] **Step 5: Remove leadership guard from production lookup path**.
- [ ] **Step 6: Run tests and confirm GREEN**.
- [ ] **Step 7: Commit**.

### Task 4: Interface sem pareamento

**Files:**
- Modify: `src/NfeAgendamento.App/wwwroot/index.html`
- Delete: `src/NfeAgendamento.App/wwwroot/pairing.js`
- Modify: `src/NfeAgendamento.App/wwwroot/portal-fallback.js`
- Modify: JS regression tests relacionados.

**Interfaces:**
- Configuração exibe certificado local e estado da pasta compartilhada.
- Nenhum botão/campo/copy de pareamento ou líder permanece.

- [ ] **Step 1: Update JS regression assertions first** para exigir ausência de pareamento.
- [ ] **Step 2: Confirm RED**.
- [ ] **Step 3: Remove pairing UI/script and update copy**.
- [ ] **Step 4: Update Portal fallback wording** para certificado local.
- [ ] **Step 5: Run `node --test tests/js/*.test.js` and confirm GREEN**.
- [ ] **Step 6: Commit**.

### Task 5: Bandeja e status local

**Files:**
- Modify: `src/NfeAgendamento.App/CentralForm.cs`
- Modify: `src/NfeAgendamento.App/TrayApplicationContext.cs`
- Modify: tests de UI/composição correspondentes.

**Interfaces:**
- `CentralForm(SharedQueuePaths paths)`.
- `TrayApplicationContext(SharedQueuePaths paths)`.

- [ ] **Step 1: Update tests first** para remover dependência de Central/líder.
- [ ] **Step 2: Confirm RED**.
- [ ] **Step 3: Simplify status window** para pasta/PC/modo shared-lock.
- [ ] **Step 4: Simplify tray composition**.
- [ ] **Step 5: Run tests and confirm GREEN**.
- [ ] **Step 6: Commit**.

### Task 6: Documentação, versão e verificação

**Files:**
- Modify: `README.md`
- Modify: documentação operacional/release aplicável.
- Modify: versão do projeto somente se a mudança estiver pronta para release.

- [ ] **Step 1: Remove operational instructions for Central/pairing** e documentar que todos os PCs precisam de certificado A1 e acesso à pasta `P:\01-Nfe agendamento`.
- [ ] **Step 2: Run full verification**: `dotnet restore Nfe-Agendamento.sln`, `dotnet test Nfe-Agendamento.sln -c Release`, `dotnet build Nfe-Agendamento.sln -c Release --no-restore`, `node --test tests/js/*.test.js`.
- [ ] **Step 3: Inspect GitHub Actions** e corrigir qualquer falha real.
- [ ] **Step 4: Review final diff/commits** para confirmar que produção não referencia pareamento/central.
- [ ] **Step 5: Commit documentation/release metadata**.