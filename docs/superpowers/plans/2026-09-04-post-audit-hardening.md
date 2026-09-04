# Hardening pós-auditoria v0.1.30 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Eliminar os riscos pós-auditoria da v0.1.30 com bootstrap recuperável, revogação criptográfica, pareamento one-shot, supply chain fixada por SHA e atualização validada pela versão realmente iniciada.

**Architecture:** A confiança do grupo continua baseada em uma chave de grupo protegida por DPAPI local e estado compartilhado cifrado. Bootstrap e rotação passam a usar preparação recuperável: o bootstrap persiste primeiro a chave local; revogação prepara bundles/arquivos novos, publica marcador e só então promove o novo estado. A API permanece loopback-only e o líder ativo é o único capaz de administrar dispositivos.

**Tech Stack:** .NET 10 Windows, ASP.NET Core minimal APIs, WinForms/WebView2, System.Security.Cryptography, DPAPI, SMB/pasta compartilhada, xUnit, Node.js regression tests, GitHub Actions, Sigstore.

**Spec:** `docs/superpowers/specs/2026-09-04-post-audit-hardening-design.md`

## Status atual

As **Tasks 1–8 estão concluídas na `main`**. O hardening inclui bootstrap recuperável, pareamento one-shot, rotação/revogação recuperável, gerenciamento de PCs autorizados, health check vinculado à versão, GitHub Actions fixadas por SHA, documentação operacional e tratamento estreito de falhas de ciclo de vida do WebView2.

O ciclo TDD final do Portal foi comprovado em GitHub Actions: o commit `ef1f6d4` falhou com 1 teste novo e 196 passando; o commit `16cae2e` implementou a proteção e o `scripts/verify.ps1 -Restore` voltou a GREEN.

A **Task 9** é o único bloco de engenharia ainda aberto: executar os gates no SHA final, alinhar a versão para **v0.1.31**, alterar `release-request.json` por último e confirmar Release Bridge/Sigstore.

A validação física multi-PC continua propositalmente fora do status de implementação: os checkboxes de `docs/TESTE-MULTI-PC.md` exigem máquinas Windows reais, SMB, A1 e WebView2 reais e não podem ser marcados apenas com CI.

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

- [x] **Step 1: Write the failing tests**
- [x] **Step 2: Run tests to verify RED**
- [x] **Step 3: Implement minimal recovery**
- [x] **Step 4: Run tests to verify GREEN**
- [x] **Step 5: Commit** — bootstrap recuperável e remoção do reflection legado implementados na `main`.

### Task 2: Pareamento realmente one-shot

**Files:**
- Modify: `src/NfeAgendamento.App/SharedQueue/SharedQueuePairing.cs`
- Modify: `src/NfeAgendamento.App/SharedQueue/SharedQueueGroupPairingProcessor.cs`
- Modify: `tests/NfeAgendamento.App.Tests/PairingBindingSecurityTests.cs`
- Modify: `tests/NfeAgendamento.App.Tests/PairingRobustnessTests.cs`

**Interfaces:**
- Produces: `PairingCodeService.TryConsume(byte[] expectedKey)`; consumo atômico só do código ainda ativo correspondente.

- [x] **Step 1: Write failing tests** para código funcionar uma vez e continuar válido após falha antes da resposta.
- [x] **Step 2: Run targeted tests and verify RED.**
- [x] **Step 3: Implement consume-after-success.**
- [x] **Step 4: Run targeted tests and verify GREEN.**
- [x] **Step 5: Commit** — código consumido somente após autorização concluída.

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

- [x] **Step 1: Write failing storage tests** cobrindo preparação sem sobrescrever ativo e promoção atômica de cada arquivo.
- [x] **Step 2: Verify RED.**
- [x] **Step 3: Implement staging APIs** usando `WriteAtomicAsync`, limites atuais e rejeição de reparse points.
- [x] **Step 4: Verify GREEN.**
- [x] **Step 5: Commit** — staging recuperável e purge do cache implementados na `main`.

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

- [x] **Step 1: Write failing tests** para revogação, nova chave/RSA, preservação dos restantes, ausência de bundle do revogado, cooldown preservado e recuperação com marcador.
- [x] **Step 2: Verify RED.**
- [x] **Step 3: Implement minimal rotation service** com preparação → marcador → promoção → atualização local → limpeza.
- [x] **Step 4: Integrate recovery before fiscal leadership**; nenhum trabalho começa com rotação pendente incompleta.
- [x] **Step 5: Verify GREEN.**
- [x] **Step 6: Commit** — rotação/revogação recuperável e cadeia RSA assinada implementadas.

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

- [x] **Step 1: Write failing API/static regression tests.**
- [x] **Step 2: Verify RED.**
- [x] **Step 3: Implement endpoints leader-only e UI sem exposição de segredo.**
- [x] **Step 4: Verify GREEN.**
- [x] **Step 5: Commit** — gerenciamento de PCs implementado no líder; autorrevogação bloqueada.

### Task 6: Health check vinculado à versão

**Files:**
- Modify: `src/NfeAgendamento.App/Program.cs`
- Modify: `src/NfeAgendamento.App/Updates/UpdateService.cs`
- Modify: `tests/NfeAgendamento.App.Tests/UpdateServiceTests.cs`
- Modify: `tests/js/release-readiness-regression.test.js`

**Interfaces:**
- Produces: `appVersion` em `/api/bootstrap`.
- Installer script compara `appVersion` com `PreparedUpdate.Version`.

- [x] **Step 1: Write failing tests** procurando validação explícita de versão no bootstrap/script.
- [x] **Step 2: Verify RED.**
- [x] **Step 3: Implement version-bound health check.**
- [x] **Step 4: Verify GREEN.**
- [x] **Step 5: Commit** — concluído na `main`; o instalador exige JSON válido e `appVersion` escalar exatamente igual à versão preparada, fazendo rollback em divergência.

### Task 7: Fixar todas as GitHub Actions por SHA

**Files:**
- Modify: `.github/workflows/ci.yml`
- Modify: `.github/workflows/codeql.yml`
- Modify: `.github/workflows/release-bridge.yml`
- Modify: `tests/js/audit-hardening-regression.test.js`

**Interfaces:**
- No runtime interface change.

- [x] **Step 1: Extend regression test** para rejeitar `uses: ...@vN`.
- [x] **Step 2: Run Node test and verify RED.**
- [x] **Step 3: Replace moving tags by exact SHAs** e manter comentários com versões legíveis.
- [x] **Step 4: Run Node test and verify GREEN.**
- [x] **Step 5: Commit** — CI, CodeQL, Release Bridge, upload-artifact e cosign-installer fixados por SHA.

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

- [x] **Step 1: Write failing robustness/documentation regression** para lifecycle do WebView2; requisitos de loopback, SMB sem Offline Files e cancelamento fiscal já estavam documentados na `main`.
- [x] **Step 2: Verify RED.** — CI do commit `ef1f6d4`: 1 falha esperada, 196 testes passando.
- [x] **Step 3: Implement catches estreitos e docs.** — somente `ObjectDisposedException`, `InvalidOperationException` e HRESULTs COM de encerramento conhecidos são classificados como lifecycle; falhas COM genéricas e I/O não são ocultadas.
- [x] **Step 4: Verify GREEN.** — `scripts/verify.ps1 -Restore` e CI verdes no commit `16cae2e`.
- [x] **Step 5: Commit** — documentação operacional separa cobertura automática de validação física.

### Task 9: Verificação integral e release seguinte

**Files:**
- Modify: `src/NfeAgendamento.App/NfeAgendamento.App.csproj`
- Modify: `README.md`
- Modify: `.github/release-request.json` por último.

- [ ] **Step 1: Run full gate** `./scripts/verify.ps1 -Restore` no SHA candidato final e exigir zero falhas.
- [ ] **Step 2: Confirm CI and CodeQL green on main.**
- [ ] **Step 3: Bump semantic version to 0.1.31 and align README.**
- [ ] **Step 4: Change release request last.**
- [ ] **Step 5: Confirm Release Bridge green, Sigstore verified and release points to tested SHA.**
