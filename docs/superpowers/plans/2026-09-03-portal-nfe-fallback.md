# Portal NF-e Fallback Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Adicionar uma contingência manual pelo Portal Nacional para obter o XML no PC Central quando `consChNFe` estiver bloqueado, preservando o fluxo fiscal principal.

**Architecture:** O lookup atual continua intocado. Um launcher singleton abre uma janela WebView2 em thread STA apenas no Central; a janela navega no Portal oficial, pré-preenche a chave, deixa o hCaptcha manual, seleciona o A1 configurado por thumbprint e captura o download oficial em arquivo temporário. Um validador independente confirma a chave e grava o XML no `EncryptedXmlCache` existente.

**Tech Stack:** .NET 8 Windows, WinForms, ASP.NET Core local, Microsoft.Web.WebView2 1.0.4191.47, xUnit, JavaScript estático existente.

**Spec:** `docs/superpowers/specs/2026-09-03-portal-nfe-fallback-design.md`

## Global Constraints

- alterações direto na `main`;
- certificado A1 permanece apenas no PC Central;
- não automatizar hCaptcha;
- não alterar fila compartilhada nem `consChNFe` principal;
- reutilizar cache criptografado de 24h;
- somente domínios oficiais da NF-e no fluxo WebView2;
- validar XML antes de persistir;
- manter CI/release existente compatível.

---

### Task 1: Contratos e testes da contingência

**Files:**
- Create: `tests/NfeAgendamento.App.Tests/PortalFallbackTests.cs`
- Modify: `tests/NfeAgendamento.App.Tests/DanfeStaticAssetsTests.cs`

**Interfaces:**
- Produces expectation for: `NfeAgendamento.App.Portal.NfePortalXmlValidator.ValidateAndNormalize(string xml, string accessKey)`.
- Produces UI expectation for: `#portalFallback`, `/api/nfe/portal-fallback`, central-only behavior.

- [ ] Criar teste para XML válido da mesma chave.
- [ ] Criar teste para rejeitar XML de chave diferente.
- [ ] Criar teste para rejeitar DTD.
- [ ] Criar teste estático para botão/endpoint/condição de 656.
- [ ] Rodar CI e confirmar RED pela ausência da feature.

### Task 2: Validação segura e dependência WebView2

**Files:**
- Create: `src/NfeAgendamento.App/Portal/NfePortalXmlValidator.cs`
- Modify: `src/NfeAgendamento.App/NfeAgendamento.App.csproj`

**Interfaces:**
- Produces: `public static string ValidateAndNormalize(string xml, string accessKey)`.

- [ ] Adicionar `Microsoft.Web.WebView2` 1.0.4191.47.
- [ ] Implementar parser XML com DTD proibido, resolver nulo e limite de 10 MiB.
- [ ] Exigir `infNFe/@Id == "NFe" + accessKey`.
- [ ] Retornar o XML validado sem reconstruir assinatura/conteúdo.

### Task 3: Janela e launcher no PC Central

**Files:**
- Create: `src/NfeAgendamento.App/Portal/PortalNfeFallbackForm.cs`
- Create: `src/NfeAgendamento.App/Portal/PortalNfeFallbackLauncher.cs`

**Interfaces:**
- Produces: `PortalFallbackLaunchResult TryLaunch(string accessKey)`.
- Consumes: `CertificateService`, `EncryptedXmlCache`, `NfePortalXmlValidator`.

- [ ] Verificar WebView2 Runtime antes de abrir.
- [ ] Permitir uma janela por vez.
- [ ] Abrir em thread STA dedicada.
- [ ] Navegar somente no Portal NF-e oficial.
- [ ] Pré-preencher `ctl00_ContentPlaceHolder1_txtChaveAcessoResumo` após navegação.
- [ ] Não clicar em botão nem interagir com hCaptcha.
- [ ] Em `ClientCertificateRequested`, aceitar somente host oficial e thumbprint configurado.
- [ ] Em `DownloadStarting`, interceptar somente `downloadNFe.aspx`, salvar em arquivo temporário e esconder UI de download.
- [ ] No estado Completed, validar XML, gravar no cache e apagar temporário.
- [ ] Mostrar sucesso/falha de forma local e não derrubar o processo principal.

### Task 4: API local e interface

**Files:**
- Modify: `src/NfeAgendamento.App/Program.cs`
- Modify: `src/NfeAgendamento.App/wwwroot/index.html`
- Modify: `src/NfeAgendamento.App/wwwroot/app.js`

**Interfaces:**
- Produces: `POST /api/nfe/portal-fallback` com `{ accessKey }`.

- [ ] Registrar `PortalNfeFallbackLauncher` singleton.
- [ ] Criar endpoint com validação de chave e `CentralStateService.IsConfiguredAsCentral`.
- [ ] Retornar 202 para janela iniciada, 409 para busy/pré-requisito e 403 em cliente.
- [ ] Guardar `configuredAsCentral` do bootstrap no JS.
- [ ] Exibir botão alternativo somente para `status === "consumo_indevido"` no Central.
- [ ] Em cliente, complementar a mensagem indicando uso do PC Central.
- [ ] Ao abrir contingência, não repetir `/api/nfe/lookup`.

### Task 5: Documentação, versão e verificação

**Files:**
- Modify: `README.md`
- Modify: documentação operacional relevante se necessário.

- [ ] Documentar caminho normal e contingência.
- [ ] Documentar requisito do WebView2 Runtime.
- [ ] Rodar `dotnet restore Nfe-Agendamento.sln`.
- [ ] Rodar `dotnet test Nfe-Agendamento.sln -c Release --no-restore`.
- [ ] Rodar regressões Node do CI.
- [ ] Rodar `dotnet build Nfe-Agendamento.sln -c Release --no-restore`.
- [ ] Rodar publish win-x64 self-contained usado pelo CI.
- [ ] Confirmar workflow CI verde na `main`.
- [ ] Só então considerar gerar nova release.