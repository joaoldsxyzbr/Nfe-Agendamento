# Automatic Shared Queue Leader Implementation Plan

> **Status:** implementação concluída na `main`; validação automatizada final executada no CI #491 (`f63cb4d0a63ab1d7d1e85f6ccef9d4bc0d4605fa`) com testes .NET, regressões JS, build Release, publish Windows, ZIP e artifact em sucesso. A validação física multi-PC/A1/Portal permanece operacional antes de promover uma release.

**Goal:** Permitir que qualquer PC confiável e já pareado assuma automaticamente a liderança da fila compartilhada, sem trocar a identidade pública atual e sem repetir consultas fiscais ambíguas.

**Architecture:** A Central antiga migra sua identidade RSA para um pacote compartilhado cifrado por uma `GroupStateKey`. Cada cliente autorizado recebe essa chave em um pacote individual cifrado com seu `ClientSecret`, guarda-a localmente via DPAPI e passa a disputar o `central.lock`. O estado de clientes autorizados, replay e cooldown fiscal também é compartilhado e cifrado. A criação/migração do grupo acontece somente sob o lock exclusivo; a importação local de candidatura pode ocorrer em standby.

**Tech Stack:** .NET 8, C#, Windows DPAPI, RSA OAEP/PSS, AES-256-GCM, filesystem/SMB locking, xUnit, GitHub Actions.

**Spec:** `docs/superpowers/specs/2026-09-03-automatic-shared-queue-leader-design.md`

## Global Constraints

- Alterar somente `main`.
- Não abrir portas TCP/HTTP adicionais.
- O certificado A1 nunca é exportado nem usado como segredo de transporte da fila.
- `central.lock` é a autoridade de liderança.
- Sem lock exclusivo revalidado, nenhuma nova consulta SEFAZ pode começar.
- A chave pública já pareada permanece a mesma durante a migração.
- Nenhum segredo privado fica em claro na pasta compartilhada.
- Failover nunca repete automaticamente consulta fiscal ambígua.
- TDD aplicado nos blocos novos e regressões.

---

### Task 1: Primitivas de estado do grupo e pacotes de candidatura

**Files:**
- `src/NfeAgendamento.App/SharedQueue/SharedQueueGroupState.cs`
- `src/NfeAgendamento.App/SharedQueue/SharedQueuePaths.cs`
- `src/NfeAgendamento.App/SharedQueue/CentralKeyStore.cs`
- `tests/NfeAgendamento.App.Tests/SharedQueueGroupStateTests.cs`

- [x] **Step 1:** testes de DPAPI local, pacote autenticado, adulteração/segredo incorreto e identidade preservada.
- [x] **Step 2:** ciclo RED registrado antes das APIs de grupo.
- [x] **Step 3:** stores mínimos com AES-GCM, versão/AAD, limites e limpeza de material temporário.
- [x] **Step 4:** suíte completa preservada.
- [x] **Step 5:** implementação GREEN integrada à `main`.

### Task 2: Estado compartilhado de clientes autorizados e replay

**Files:**
- `src/NfeAgendamento.App/SharedQueue/SharedAuthorizedClientStore.cs`
- `src/NfeAgendamento.App/SharedQueue/SharedQueuePairing.cs`
- `tests/NfeAgendamento.App.Tests/SharedAuthorizedClientStoreTests.cs`

- [x] **Step 1:** testes de persistência cifrada, replay entre líderes, adulteração e migração.
- [x] **Step 2:** ciclo RED confirmado.
- [x] **Step 3:** estado compartilhado com AES-GCM e escrita atômica.
- [x] **Step 4:** leitura do estado legado limitada ao bootstrap de compatibilidade, preservando `LastSequence` e limpando segredos temporários.
- [x] **Step 5:** GREEN confirmado no CI.

### Task 3: Bootstrap/migração e adesão automática

**Files:**
- `src/NfeAgendamento.App/SharedQueue/SharedQueueGroupBootstrapService.cs`
- `src/NfeAgendamento.App/SharedQueue/SharedQueueCentralService.cs`
- `src/NfeAgendamento.App/SharedQueue/SharedQueueGroupPairingProcessor.cs`
- `src/NfeAgendamento.App/Program.cs`
- `tests/NfeAgendamento.App.Tests/SharedQueueGroupBootstrapTests.cs`

- [x] **Step 1:** testes para bootstrap idempotente, mesma chave pública, import sem reapareamento e mismatch de identidade.
- [x] **Step 2:** RED confirmado antes do bootstrap.
- [x] **Step 3:** bootstrap usa a flag legada apenas quando ainda não existe grupo e escreve a migração somente sob `central.lock`.
- [x] **Step 4:** clientes pareados importam a candidatura automaticamente sem interromper o líder atual.
- [x] **Step 5:** novo pareamento grava autorização e pacote de candidatura antes da resposta.
- [x] **Step 6:** GREEN confirmado no CI.

### Task 4: Liderança automática e despacho local/remoto

**Files:**
- `src/NfeAgendamento.App/SharedQueue/SharedQueueCentralLease.cs`
- `src/NfeAgendamento.App/SharedQueue/SharedQueueCentralService.cs`
- `src/NfeAgendamento.App/SharedQueue/SharedQueueGroupProcessor.cs`
- `src/NfeAgendamento.App/Fiscal/LookupDispatchService.cs`
- `tests/NfeAgendamento.App.Tests/SharedQueueAutomaticLeaderTests.cs`
- regressões de segurança existentes.

- [x] **Step 1:** testes cobrem exclusão de líder, takeover e preservação da chave pública.
- [x] **Step 2:** recuperação pós-takeover prova que uma chamada fiscal potencialmente emitida não é repetida.
- [x] **Step 3:** testes de failover adicionados antes do fechamento do runtime.
- [x] **Step 4:** `IsConfiguredAsCentral` removido de eleição/dispatch normal; mantido apenas no bootstrap legado.
- [x] **Step 5:** processador usa autorização/replay compartilhados.
- [x] **Step 6:** lock é revalidado antes de novo trabalho e CI final está verde.

### Task 5: Certificado, Portal e UX de líder automático

**Files:**
- `src/NfeAgendamento.App/Program.cs`
- `src/NfeAgendamento.App/CentralForm.cs`
- `src/NfeAgendamento.App/TrayApplicationContext.cs`
- `src/NfeAgendamento.App/wwwroot/index.html`
- `src/NfeAgendamento.App/wwwroot/pairing.js`
- testes de UI/regressão.

- [x] **Step 1:** contratos atualizados para status automático e remoção de start/stop manual.
- [x] **Step 2:** contratos antigos foram detectados pelo CI e corrigidos para a UX aprovada.
- [x] **Step 3:** certificado e Portal não dependem mais de `ConfiguredAsCentral`.
- [x] **Step 4:** janela/bandeja mostram líder, standby, pasta indisponível e autorização.
- [x] **Step 5:** .NET e regressões JS verdes.

### Task 6: Documentação, migração e verificação final

**Files:**
- `README.md`
- `docs/CENTRAL-LAN.md`
- `docs/ATUALIZACAO-E-INICIALIZACAO.md`
- este plano.

- [x] **Step 1:** documentação atualizada com arquitetura, migração, failure behavior e checklist físico.
- [x] **Step 2:** CI #491 executou restore, todos os testes .NET, todas as regressões JS, build Release, publish Windows autocontido, ZIP e upload de artifact com sucesso.
- [x] **Step 3:** diff revisado desde `f634331c2d4f0590ae54430f6ea542b6b5c294db`; alterações estão limitadas à liderança automática, segurança fiscal, testes e documentação relacionados.
- [x] **Step 4:** plano atualizado apenas após evidência do CI.
- [x] **Step 5:** nenhuma release foi publicada nesta execução.

## Aceitação física ainda necessária

Antes de promover a candidata para release, validar em ambiente real:

- [ ] dois ou mais PCs reais disputam e somente um fica líder;
- [ ] ao encerrar o líder, outro PC assume automaticamente;
- [ ] antigo líder volta como standby se já houver líder;
- [ ] consulta funciona no líder e em standby antes/depois do failover;
- [ ] A1 está instalado/configurado nos candidatos reais;
- [ ] cooldown/replay permanecem consistentes no compartilhamento real SMB;
- [ ] WebView2 abre o Portal Nacional atual;
- [ ] preenchimento de chave e hCaptcha manual continuam funcionais;
- [ ] certificado A1 é oferecido/selecionado no Portal real;
- [ ] XML oficial entra no cache e uma nova consulta retorna do cache.

Não provocar `cStat=656` real apenas para teste.
