# Automatic Queue Leader Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Remover a dependência de um PC Central fixo, permitindo que qualquer PC confiável com o mesmo A1 assuma automaticamente a liderança exclusiva da fila compartilhada.

**Architecture:** Reutilizar `central.lock` como lease exclusivo. Tornar `CentralKeyStore` transparente ao modo cluster: em produção ele preserva a chave RSA existente da fila, mas armazena uma cópia cifrada na pasta compartilhada, cuja chave AES é embrulhada pelo A1. Tornar autorização/replay e cooldown estados globais da pasta; o runtime passa a escolher entre processamento local (líder) e fila (standby) pelo lease, não por `ConfiguredAsCentral`.

**Tech Stack:** .NET 8, ASP.NET Core minimal APIs, WinForms/WebView2, X509Certificate2, RSA OAEP-SHA256, AES-256-GCM, DPAPI legado, SMB/shared folder, xUnit, GitHub Actions.

**Spec:** `docs/superpowers/specs/2026-09-03-automatic-queue-leader-design.md`

## Global Constraints

- Alterações direto na `main`, conforme regra do projeto e autorização explícita do usuário.
- Sem imagens.
- Exatamente um líder fiscal por vez; sem lock exclusivo válido, nenhuma chamada SEFAZ pela fila.
- Não repetir automaticamente chamadas fiscais ambíguas durante failover.
- Preservar chave pública da fila para não exigir novo pareamento.
- Mesmo A1 instalado nos PCs candidatos; certificado deve possuir chave privada RSA compatível com OAEP-SHA256.
- hCaptcha continua manual.
- XML/cache permanece local nesta mudança.

---

### Task 1: Bundle de identidade do cluster

**Files:**
- Modify: `src/NfeAgendamento.App/SharedQueue/SharedQueuePaths.cs`
- Modify: `src/NfeAgendamento.App/SharedQueue/SharedQueueFileIO.cs`
- Modify: `src/NfeAgendamento.App/SharedQueue/CentralKeyStore.cs`
- Create: `tests/NfeAgendamento.App.Tests/SharedQueueClusterIdentityTests.cs`

**Interfaces:**
- `CentralKeyStore(SharedQueuePaths paths, CertificateService certificates, CentralStateService legacyState)` ativa modo cluster.
- `bool ClusterIdentityExists { get; }`
- `QueueClusterBinding GetClusterBinding()` retorna thumbprint/UF.
- `RSA OpenPrivateKey()` e `byte[] GetOrCreatePublicKey()` continuam sendo a API consumida pelo restante do app.

- [ ] **Step 1: Write the failing tests**

Cobrir: bootstrap preserva a chave pública existente; bundle não contém PKCS#8 em claro; segundo store com o mesmo A1 abre a mesma identidade; certificado diferente falha; PC não legado não cria identidade nova quando bundle não existe.

- [ ] **Step 2: Run tests to verify RED**

Run: `dotnet test Nfe-Agendamento.sln -c Release --no-restore --filter SharedQueueClusterIdentityTests`
Expected: FAIL porque o modo cluster ainda não existe.

- [ ] **Step 3: Implement minimal cluster identity bundle**

Adicionar caminhos permitidos `cluster-identity.json` e temporário. O bundle usa AES-GCM para o PKCS#8 da fila e RSA OAEP-SHA256 do A1 para embrulhar a chave AES. Bootstrap só é permitido quando `legacyState.IsConfiguredAsCentral` e existe a chave local legado; gravação é atômica.

- [ ] **Step 4: Run tests and full .NET suite**

Run: `dotnet test Nfe-Agendamento.sln -c Release --no-restore`
Expected: PASS.

- [ ] **Step 5: Commit**

`git commit -m "feat: compartilhar identidade segura da fila"`

### Task 2: Estado global de autorização e replay

**Files:**
- Modify: `src/NfeAgendamento.App/SharedQueue/SharedQueuePairing.cs`
- Modify: `src/NfeAgendamento.App/Program.cs`
- Create: `tests/NfeAgendamento.App.Tests/SharedAuthorizedClientStoreTests.cs`

**Interfaces:**
- `AuthorizedClientStore(SharedQueuePaths paths, CentralKeyStore keyStore)` ativa armazenamento compartilhado.
- `Authorize`, `TryAuthenticateAndAdvance` e `Count` permanecem compatíveis.
- Estado compartilhado em `status/authorized-clients.dat`, cifrado com AES-GCM; chave AES por gravação embrulhada pela identidade RSA da fila.

- [ ] **Step 1: Write failing tests**

Cobrir: leader A autoriza/avança sequence e leader B lê o mesmo `LastSequence`; replay após failover é bloqueado; conteúdo não expõe segredo; migração do `authorized-clients.bin` legado mantém clientes e sequence.

- [ ] **Step 2: Verify RED**

Run: `dotnet test Nfe-Agendamento.sln -c Release --no-restore --filter SharedAuthorizedClientStoreTests`
Expected: FAIL.

- [ ] **Step 3: Implement shared encrypted store**

Manter construtor por path para testes/compatibilidade legado. Em produção, registrar factory com `SharedQueuePaths` + `CentralKeyStore`. Escrever arquivo atomicamente e usar limites de leitura.

- [ ] **Step 4: Verify GREEN/full suite**

Run: `dotnet test Nfe-Agendamento.sln -c Release --no-restore`
Expected: PASS.

- [ ] **Step 5: Commit**

`git commit -m "feat: compartilhar autorização e replay da fila"`

### Task 3: Eleição automática e failover

**Files:**
- Modify: `src/NfeAgendamento.App/SharedQueue/SharedQueueCentralService.cs`
- Modify: `src/NfeAgendamento.App/Fiscal/LookupDispatchService.cs`
- Modify: `src/NfeAgendamento.App/Certificates/CertificateService.cs`
- Modify: `tests/NfeAgendamento.App.Tests/SharedQueueCentralServiceTests.cs`
- Modify: `tests/NfeAgendamento.App.Tests/SharedQueueSecurityRegressionTests.cs`
- Create: `tests/NfeAgendamento.App.Tests/AutomaticLeaderFailoverTests.cs`

**Interfaces:**
- `SharedQueueCentralService` tenta liderança automaticamente quando a pasta existe e a identidade do cluster pode ser aberta.
- `CentralRuntimeStatus`: `Active`, `Standby`, `ShareUnavailable`, `CertificateUnavailable`.
- `LookupDispatchService.LookupAsync`: `runtime.IsActive` => consulta local; caso contrário => `SharedQueueClient`.

- [ ] **Step 1: Write failing tests**

Cobrir: apenas um líder; standby assume após dispose do primeiro; `ConfiguredAsCentral=false` não impede liderança quando cluster existe; nonleader nunca chama `NfeLookupService` diretamente; candidato sem A1 não processa.

- [ ] **Step 2: Verify RED**

Run: `dotnet test Nfe-Agendamento.sln -c Release --no-restore --filter "AutomaticLeaderFailoverTests|SharedQueueCentralServiceTests"`
Expected: FAIL.

- [ ] **Step 3: Implement automatic leader runtime**

Remover `ConfiguredAsCentral` como condição de execução normal. Manter flag somente para bootstrap legado. Depois de adquirir o lock, validar/abrir identidade antes de publicar heartbeat/processar. Ao perder acesso/identidade, soltar lease e falhar fechado.

- [ ] **Step 4: Verify GREEN/full suite**

Run: `dotnet test Nfe-Agendamento.sln -c Release --no-restore`
Expected: PASS.

- [ ] **Step 5: Commit**

`git commit -m "feat: eleger líder da fila automaticamente"`

### Task 4: Cooldown 656 compartilhado

**Files:**
- Modify: `src/NfeAgendamento.App/Fiscal/FiscalCooldownStore.cs`
- Modify: `src/NfeAgendamento.App/Program.cs`
- Modify: `src/NfeAgendamento.App/SharedQueue/SharedQueuePaths.cs`
- Create: `tests/NfeAgendamento.App.Tests/SharedFiscalCooldownTests.cs`

**Interfaces:**
- `FiscalCooldownStore(SharedQueuePaths paths)` usa `status/fiscal-cooldown.json` compartilhado.
- Construtor por path continua sendo DPAPI/local para testes legados.

- [ ] **Step 1: Write failing tests**

Cobrir: store A bloqueia por 656 e store B observa o mesmo horário; store B rejeita consulta antes do vencimento; vencimento limpa o estado global; falha de persistência mantém proteção volátil do processo atual.

- [ ] **Step 2: Verify RED**

Run: `dotnet test Nfe-Agendamento.sln -c Release --no-restore --filter SharedFiscalCooldownTests`
Expected: FAIL.

- [ ] **Step 3: Implement shared cooldown**

Persistir somente `BlockedUntilUtc` em escrita atômica no status. Validar tamanho/reparse point. Não enfraquecer `_volatileBlockedUntilUtc`.

- [ ] **Step 4: Verify GREEN/full suite**

Run: `dotnet test Nfe-Agendamento.sln -c Release --no-restore`
Expected: PASS.

- [ ] **Step 5: Commit**

`git commit -m "feat: compartilhar cooldown fiscal entre líderes"`

### Task 5: Certificado, Portal e experiência sem Central fixa

**Files:**
- Modify: `src/NfeAgendamento.App/Program.cs`
- Modify: `src/NfeAgendamento.App/CentralForm.cs`
- Modify: `src/NfeAgendamento.App/TrayApplicationContext.cs`
- Modify: `src/NfeAgendamento.App/Portal/PortalNfeFallbackLauncher.cs` somente se necessário para remover gating legado
- Modify: `tests/NfeAgendamento.App.Tests/ReleaseReadinessBehaviorTests.cs`
- Modify/create JS regression tests conforme o markup atual

**Interfaces:**
- Certificado pode ser administrado localmente em qualquer PC.
- Portal fallback pode abrir localmente em qualquer PC com A1 compatível.
- UI mostra líder/standby/indisponível; não exige botão operacional `Iniciar Central`.

- [ ] **Step 1: Write failing behavior/regression tests**

Cobrir cópias/estado esperado e ausência de gating por `IsConfiguredAsCentral` nos endpoints de certificado/Portal.

- [ ] **Step 2: Verify RED**

Run: `dotnet test Nfe-Agendamento.sln -c Release --no-restore`
Expected: FAIL nos novos testes.

- [ ] **Step 3: Implement minimal UX/API changes**

Não redesenhar o app. Trocar apenas textos, botões e condições necessárias para o novo modelo.

- [ ] **Step 4: Verify all tests**

Run: `dotnet test Nfe-Agendamento.sln -c Release --no-restore`
Run: todos os scripts JS já usados pelo CI.
Expected: PASS.

- [ ] **Step 5: Commit**

`git commit -m "feat: remover operação de Central fixa"`

### Task 6: Documentação e verificação de release

**Files:**
- Modify: `README.md`
- Modify: documentação operacional pertinente

**Interfaces:**
- Documentar requisitos para failover: mesma pasta, mesmo A1 instalado e acesso à chave privada.
- Documentar bootstrap/migração e comportamento fail-closed.

- [ ] **Step 1: Update README**

Explicar líder automático, lock exclusivo, identidade cifrada pelo A1, cooldown global, recuperação segura e Portal local.

- [ ] **Step 2: Run final verification**

Run: `dotnet restore Nfe-Agendamento.sln`
Run: `dotnet test Nfe-Agendamento.sln -c Release --no-restore`
Run: regressões JS do workflow
Run: `dotnet build Nfe-Agendamento.sln -c Release --no-restore`
Run: publish self-contained Windows usado pelo CI
Expected: tudo verde.

- [ ] **Step 3: Compare main**

Revisar diff completo desde `f634331c2d4f0590ae54430f6ea542b6b5c294db` para confirmar que não houve mudanças fora do escopo.

- [ ] **Step 4: Commit docs**

`git commit -m "docs: documentar liderança automática da fila"`
