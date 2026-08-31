# Stabilize Local Foundation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Tornar a consulta única local mais confiável antes do piloto nos três PCs.

**Architecture:** Manter o app Windows local em loopback, sem login, LAN ou servidor central. A próxima fatia melhora a leitura do certificado e expõe na bandeja somente ações que já podem ser executadas com segurança.

**Tech Stack:** .NET 8 Windows Forms, ASP.NET Core, xUnit e GitHub Actions.

**Spec:** `docs/superpowers/specs/2026-08-31-local-browser-nfe-design.md`

## Global Constraints

- O host deve escutar somente em `http://127.0.0.1:17345`.
- O certificado e a chave privada permanecem no Windows Certificate Store.
- CI nunca consulta a SEFAZ real nem usa certificado/XML real da empresa.
- Não adicionar login, banco, servidor LAN, `distNSU` ou dashboard.
- Atualização deve apenas consultar metadados públicos e nunca substituir arquivos automaticamente nesta etapa.

### Task 1: Tornar a identidade do certificado configurável e testável

**Files:**
- Modify: `src/NfeAgendamento.App/Certificates/CertificateIdentity.cs`
- Modify: `src/NfeAgendamento.App/Certificates/CertificateService.cs`
- Test: `tests/NfeAgendamento.App.Tests/CertificateServiceTests.cs`

- [ ] Escrever testes para certificado sem UF no assunto e para configuração explícita da UF autora.
- [ ] Executar os testes no CI e confirmar a falha antes da implementação.
- [ ] Implementar a UF configurável sem exportar certificado ou chave privada.
- [ ] Executar todos os testes e build no Windows.

### Task 2: Completar ações seguras da bandeja

**Files:**
- Modify: `src/NfeAgendamento.App/TrayApplicationContext.cs`
- Modify: `src/NfeAgendamento.App/Program.cs`
- Test: `tests/NfeAgendamento.App.Tests/TrayApplicationContextTests.cs`

- [ ] Escrever teste para o menu conter abrir, configurar certificado, verificar atualização e sair.
- [ ] Executar o teste e confirmar a falha.
- [ ] Adicionar as ações sem criar novo painel administrativo.
- [ ] Executar testes e build no Windows.

### Task 3: Verificação de atualização sem auto-update

**Files:**
- Create: `src/NfeAgendamento.App/Updates/UpdateService.cs`
- Modify: `src/NfeAgendamento.App/Program.cs`
- Modify: `src/NfeAgendamento.App/TrayApplicationContext.cs`
- Test: `tests/NfeAgendamento.App.Tests/UpdateServiceTests.cs`

- [ ] Escrever testes para validar versão remota, URL HTTPS e SHA-256.
- [ ] Executar os testes e confirmar a falha.
- [ ] Implementar apenas consulta de manifesto público; não baixar nem substituir o executável.
- [ ] Executar todos os testes e build no Windows.

### Task 4: Documentação e verificação final

- [ ] Atualizar README com o limite atual e o comportamento da bandeja.
- [ ] Rodar `dotnet test Nfe-Agendamento.sln -c Release`.
- [ ] Rodar `dotnet build Nfe-Agendamento.sln -c Release`.
- [ ] Confirmar CI verde na branch da PR.
