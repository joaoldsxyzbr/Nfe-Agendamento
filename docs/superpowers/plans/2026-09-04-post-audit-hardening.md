# Hardening pós-auditoria v0.1.30 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Eliminar os riscos pós-auditoria da v0.1.30 com bootstrap recuperável, revogação criptográfica, pareamento one-shot, supply chain fixada por SHA e atualização validada pela versão realmente iniciada.

**Architecture:** A confiança do grupo continua baseada em uma chave de grupo protegida por DPAPI local e estado compartilhado cifrado. Bootstrap e rotação passam a usar preparação recuperável: o bootstrap persiste primeiro a chave local; revogação prepara bundles/arquivos novos, publica marcador e só então promove o novo estado. A API permanece loopback-only e o líder ativo é o único capaz de administrar dispositivos.

**Tech Stack:** .NET 10 Windows, ASP.NET Core minimal APIs, WinForms/WebView2, System.Security.Cryptography, DPAPI, SMB/pasta compartilhada, xUnit, Node.js regression tests, GitHub Actions, Sigstore.

**Spec:** `docs/superpowers/specs/2026-09-04-post-audit-hardening-design.md`

## Global Constraints

- Alterações diretamente na `main`, conforme regra do projeto.
- Nenhuma imagem será criada.
- Nenhum certificado A1, segredo fiscal ou chave privada de release entra no GitHub.
- Uma operação fiscal ambígua nunca recebe retry automático.
- `scripts/verify.ps1 -Restore` continua sendo o gate comum.
- TDD obrigatório: teste falha antes da implementação e fica verde depois.

---

### Task 1: Bootstrap recuperável e remoção do reflection legado

**Files:**
- Modify: `src/NfeAgendamento.App/SharedQueue/SharedQueuePairing.cs`
- Modify: `src/NfeAgendamento.App/SharedQueue/SharedQueueGroupBootstrapService.cs`
- Modify: `tests/NfeAgendamento.App.Tests/SharedQueueGroupBootstrapTests.cs`

**Interfaces:**
- Produces: `AuthorizedClientStore.SnapshotForMigration()` retornando cópias validadas.
- Produces: bootstrap que reutiliza `CandidateStateStore.Load()` quando identidade ainda não existe.

- [ ] **Step 1: Write the failing tests**

Adicionar testes que comprovem que uma chave local pré-gravada é reutilizada para criar a identidade e que `SharedQueueGroupBootstrapService` não contém `System.Reflection`/`FieldInfo`.

- [ ] **Step 2: Run tests to verify RED**

Run: `dotnet test Nfe-Agendamento.sln -c Release --filter SharedQueueGroupBootstrapTests`
Expected: FAIL porque o bootstrap atual gera outra chave e ainda usa reflection.

- [ ] **Step 3: Implement minimal recovery**

Expor snapshot interno no store legado; no bootstrap carregar chave existente ou gerar/salvar antes de `SharedGroupIdentityStore.Initialize`; remover reflection.

- [ ] **Step 4: Run tests to verify GREEN**

Run: `dotnet test Nfe-Agendamento.sln -c Release --filter SharedQueueGroupBootstrapTests`
Expected: PASS.

- [ ] **Step 5: Commit**

Commit: `fix: tornar bootstrap do grupo recuperavel`

### Task 2: Pareamento realmente one-shot

**Files:**
- Modify: `src/NfeAgendamento.App/SharedQueue/SharedQueuePairing.cs`
- Modify: `src/NfeAgendamento.App/SharedQueue/SharedQueueGroupPairingProcessor.cs`
- Modify: `tests/NfeAgendamento.App.Tests/PairingBindingSecurityTests.cs`
- Modify: `tests/NfeAgendamento.App.Tests/PairingRobustnessTests.cs`

**Interfaces:**
- Produces: `PairingCodeService.TryConsume(byte[] expectedKey)`; consumo atômico só do código ainda ativo correspondente.

- [ ] **Step 1: Write failing tests** para código funcionar uma vez e continuar válido após falha antes da resposta.
- [ ] **Step 2: Run targeted tests and verify RED.**
- [ ] **Step 3: Implement consume-after-success.**
- [ ] **Step 4: Run targeted tests and verify GREEN.**
- [ ] **Step 5: Commit** `fix: consumir codigo de pareamento apos sucesso`.

### Task 3: Estado preparado para rotação e validação real de candidatura

**Files:**
- Modify: `src/NfeAgendamento.App/SharedQueue/SharedQueuePaths.cs`
- Modify: `src/NfeAgendamento.App/SharedQueue/SharedQueueGroupState.cs`
- Modify: `src/NfeAgendamento.App/SharedQueue/SharedAuthorizedClientStore.cs`
- Modify: `src/NfeAgendamento.App/Fiscal/FiscalCooldownStore.cs`
- Modify: `src/NfeAgendamento.App/Storage/EncryptedXmlCache.cs`
- Modify: `tests/NfeAgendamento.App.Tests/SharedQueueGroupStateTests.cs`
- Modify: `tests/NfeAgendamento.App.Tests/SharedAuthorizedClientStoreTests.cs`
- Modify: `tests/NfeAgendamento.App.Tests/SharedFiscalCooldownTests.cs`

**Interfaces:**
- Produces: caminhos de marcador/arquivos preparados de rotação.
- Produces: preparação e promoção de identidade/lista/cooldown com chave fornecida.
- Produces: purge explícito do cache compartilhado.

- [ ] **Step 1: Write failing storage tests** cobrindo preparação sem sobrescrever ativo e promoção atômica de cada arquivo.
- [ ] **Step 2: Verify RED.**
- [ ] **Step 3: Implement staging APIs** usando `WriteAtomicAsync`, limites atuais e rejeição de reparse points.
- [ ] **Step 4: Verify GREEN.**
- [ ] **Step 5: Commit** `feat: preparar rotacao recuperavel do grupo`.

### Task 4: Serviço de revogação + rotação recuperável

**Files:**
- Create: `src/NfeAgendamento.App/SharedQueue/SharedQueueGroupRotationService.cs`
- Modify: `src/NfeAgendamento.App/SharedQueue/SharedQueueGroupBootstrapService.cs`
- Modify: `src/NfeAgendamento.App/SharedQueue/SharedQueueCentralService.cs`
- Modify: `src/NfeAgendamento.App/SharedQueue/SharedQueuePairing.cs`
- Modify: `tests/NfeAgendamento.App.Tests/SharedQueueGroupRotationTests.cs`
- Modify: `tests/NfeAgendamento.App.Tests/SharedQueueAutomaticLeaderTests.cs`

**Interfaces:**
- Produces: `Task<GroupRotationResult> RevokeAsync(Guid clientId, CancellationToken)`.
- Produces: `Task<bool> CompletePendingAsync(CancellationToken)`.
- Produces: candidate bundle com nova chave e nova identidade pública; import atualiza pin mantendo ClientId/secret/sequence.

- [ ] **Step 1: Write failing tests** para revogação, nova chave/RSA, preservação dos restantes, ausência de bundle do revogado, cooldown preservado e recuperação com marcador.
- [ ] **Step 2: Verify RED.**
- [ ] **Step 3: Implement minimal rotation service** com preparação → marcador → promoção → atualização local → limpeza.
- [ ] **Step 4: Integrate recovery before fiscal leadership**; nenhum trabalho começa com rotação pendente incompleta.
- [ ] **Step 5: Verify GREEN.**
- [ ] **Step 6: Commit** `feat: adicionar revogacao criptografica de PCs`.

### Task 5: API e interface de dispositivos autorizados

**Files:**
- Modify: `src/NfeAgendamento.App/Program.cs`
- Modify: `src/NfeAgendamento.App/wwwroot/index.html`
- Modify: `src/NfeAgendamento.App/wwwroot/pairing.js`
- Modify: `src/NfeAgendamento.App/wwwroot/styles.css`
- Modify: `tests/js/pairing-lookup-regression.test.js`
- Modify: `tests/NfeAgendamento.App.Tests/ProductionCompositionRegressionTests.cs`

**Interfaces:**
- Produces: `GET /api/pairing/clients` e `POST /api/pairing/revoke`.

- [ ] **Step 1: Write failing API/static regression tests.**
- [ ] **Step 2: Verify RED.**
- [ ] **Step 3: Implement endpoints leader-only e UI sem exposição de segredo.**
- [ ] **Step 4: Verify GREEN.**
- [ ] **Step 5: Commit** `feat: gerenciar PCs autorizados no lider`.

### Task 6: Health check vinculado à versão

**Files:**
- Modify: `src/NfeAgendamento.App/Program.cs`
- Modify: `src/NfeAgendamento.App/Updates/UpdateService.cs`
- Modify: `tests/NfeAgendamento.App.Tests/UpdateServiceTests.cs`
- Modify: `tests/js/release-readiness-regression.test.js`

**Interfaces:**
- Produces: `appVersion` em `/api/bootstrap`.
- Installer script compara `appVersion` com `PreparedUpdate.Version`.

- [ ] **Step 1: Write failing tests** procurando validação explícita de versão no bootstrap/script.
- [ ] **Step 2: Verify RED.**
- [ ] **Step 3: Implement version-bound health check.**
- [ ] **Step 4: Verify GREEN.**
- [ ] **Step 5: Commit** `fix: validar versao no health check de atualizacao`.

### Task 7: Fixar todas as GitHub Actions por SHA

**Files:**
- Modify: `.github/workflows/ci.yml`
- Modify: `.github/workflows/codeql.yml`
- Modify: `.github/workflows/release-bridge.yml`
- Modify: `tests/js/audit-hardening-regression.test.js`

**Interfaces:**
- No runtime interface change.

- [ ] **Step 1: Extend regression test** para rejeitar `uses: ...@vN`.
- [ ] **Step 2: Run Node test and verify RED.**
- [ ] **Step 3: Replace moving tags by exact SHAs** conhecidos dos runs atuais; manter comentários com versões legíveis.
- [ ] **Step 4: Run Node test and verify GREEN.**
- [ ] **Step 5: Commit** `chore: fixar actions por commit sha`.

### Task 8: Robustez do Portal e documentação do threat model

**Files:**
- Modify: `src/NfeAgendamento.App/Portal/PortalNfeFallbackForm.cs`
- Modify: `tests/NfeAgendamento.App.Tests/PortalFallbackTests.cs`
- Modify: `README.md`
- Modify: `docs/CENTRAL-LAN.md`
- Modify: `docs/ATUALIZACAO-E-INICIALIZACAO.md`
- Modify: `docs/TESTE-MULTI-PC.md`

**Interfaces:**
- No protocol change.

- [ ] **Step 1: Write failing robustness/documentation regressions** para lifecycle WebView2, loopback threat model, SMB sem Offline Files e cancelamento fiscal deliberado.
- [ ] **Step 2: Verify RED.**
- [ ] **Step 3: Implement catches estreitos e docs.**
- [ ] **Step 4: Verify GREEN.**
- [ ] **Step 5: Commit** `docs: fechar requisitos operacionais pos-auditoria`.

### Task 9: Verificação integral e release seguinte

**Files:**
- Modify: `src/NfeAgendamento.App/NfeAgendamento.App.csproj`
- Modify: `README.md`
- Modify: `.github/release-request.json` por último.

- [ ] **Step 1: Run full gate** `./scripts/verify.ps1 -Restore` e exigir zero falhas.
- [ ] **Step 2: Confirm CI and CodeQL green on main.**
- [ ] **Step 3: Bump semantic version and align README.**
- [ ] **Step 4: Change release request last.**
- [ ] **Step 5: Confirm Release Bridge green, Sigstore verified and release points to tested SHA.**
