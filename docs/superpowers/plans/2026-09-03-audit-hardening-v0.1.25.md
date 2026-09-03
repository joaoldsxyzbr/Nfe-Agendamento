# Audit Hardening v0.1.25 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fechar os riscos de maior impacto encontrados na auditoria, migrar a solução para .NET 10 LTS, endurecer CI/release e publicar a v0.1.25 sem alterar a arquitetura operacional do produto.

**Architecture:** A autoridade fiscal passa a ser revalidada no boundary imediatamente anterior ao POST para a SEFAZ. O atualizador troca diretórios por rename no mesmo volume, valida a nova instância por `/api/bootstrap` e restaura a versão anterior em falha. CI e Release Bridge passam a usar .NET 10 e auditoria de dependências; Dependabot, CodeQL e bloqueios de segredos completam o hardening.

**Tech Stack:** C#/.NET 10, ASP.NET Core local loopback, WinForms, WebView2, xUnit, Node.js regressions, GitHub Actions, PowerShell.

**Spec:** `docs/superpowers/specs/2026-09-03-audit-hardening-design.md`

## Global Constraints

- Alterações diretamente na `main`.
- HTTP continua somente em `http://127.0.0.1:17345`.
- Compartilhamento continua em `P:\01-Nfe agendamento`.
- Nenhuma repetição fiscal automática após falha ambígua.
- Certificado A1 e chave privada nunca entram no repositório ou na pasta compartilhada.
- Pacote Windows continua `win-x64`, autocontido e single-file.
- A assinatura independente de update exige o Secret externo `NFE_UPDATE_SIGNING_KEY_PKCS8_B64`; como a integração disponível não permite criar GitHub Secrets, esse gate permanece pendente e não será simulado com chave privada versionada. A v0.1.25 mantém a validação atual por digest SHA-256 do asset e recebe as demais proteções deste plano.

---

### Task 1: Fencing fiscal no último boundary

**Files:**
- Create: `src/NfeAgendamento.App/Fiscal/FiscalLeadershipGuard.cs`
- Modify: `src/NfeAgendamento.App/Fiscal/NfeDistributionTransport.cs`
- Modify: `src/NfeAgendamento.App/Fiscal/NfeLookupService.cs`
- Modify: `src/NfeAgendamento.App/Program.cs`
- Test: `tests/NfeAgendamento.App.Tests/FiscalRetrySafetyTests.cs`

**Interfaces:**
- Produces: `IFiscalLeadershipGuard.EnsureCanStartFiscalOperation()`.
- Produces: `FiscalLeadershipLostException`.
- Production guard consumes `SharedQueueCentralService.CanProcessWork()`.

- [ ] **Step 1: escrever regressão que falha**

Adicionar um transporte fake que cria `FiscalLeadershipLostException` por reflexão e confirmar que a consulta termina em `NfeLookupStatus.Failed`, contém mensagem de liderança e ocorre uma única tentativa.

```csharp
[Fact]
public async Task Leadership_loss_is_fail_closed_and_not_retried()
{
    using var temp = new TemporaryDirectory();
    var transport = new LeadershipLostTransport();
    var service = CreateService(temp.Path, transport);

    var result = await service.LookupAsync(ValidKey);

    Assert.Equal(NfeLookupStatus.Failed, result.Status);
    Assert.Equal(1, transport.CallCount);
    Assert.Contains("liderança", result.Message ?? string.Empty, StringComparison.OrdinalIgnoreCase);
}
```

- [ ] **Step 2: confirmar RED no GitHub Actions**

Esperado: teste falha porque o tipo `FiscalLeadershipLostException` ainda não existe.

- [ ] **Step 3: implementar guard e wiring**

```csharp
public interface IFiscalLeadershipGuard
{
    void EnsureCanStartFiscalOperation();
}

public sealed class FiscalLeadershipLostException : InvalidOperationException
{
    public FiscalLeadershipLostException(string message) : base(message) { }
}
```

`NfeDistributionTransport.QueryByAccessKeyAsync()` chama `_leadershipGuard.EnsureCanStartFiscalOperation()` imediatamente antes de `_httpClient.PostAsync(...)`.

`NfeLookupService` trata a exceção antes dos catches genéricos e retorna falha segura sem retry.

- [ ] **Step 4: confirmar GREEN**

CI deve executar todos os testes, regressões JS, build e publish sem falhas.

- [ ] **Step 5: commit**

`fix: adicionar fencing fiscal antes da sefaz`

---

### Task 2: Atualização com swap e rollback

**Files:**
- Modify: `src/NfeAgendamento.App/Updates/UpdateService.cs`
- Test: `tests/NfeAgendamento.App.Tests/UpdateServiceTests.cs`

**Interfaces:**
- Mantém `UpdateCheckResult`, `UpdatePackage` e `PreparedUpdate` compatíveis.
- `PreparedUpdate.StagingDirectory` passa a apontar para o diretório sibling preparado no mesmo volume da instalação.

- [ ] **Step 1: escrever regressão RED**

O teste deve exigir no script gerado:

```csharp
Assert.Contains("AddSeconds(20)", script, StringComparison.Ordinal);
Assert.Contains("http://127.0.0.1:17345/api/bootstrap", script, StringComparison.Ordinal);
Assert.Contains("Move-Item -LiteralPath $install -Destination $backup", script, StringComparison.Ordinal);
Assert.Contains("Move-Item -LiteralPath $backup -Destination $install", script, StringComparison.Ordinal);
Assert.DoesNotContain("Copy-Item -LiteralPath $_.FullName", script, StringComparison.Ordinal);
```

- [ ] **Step 2: confirmar RED**

Esperado: `UpdateServiceTests` falha porque o instalador atual usa cópia arquivo a arquivo e não possui health check/rollback.

- [ ] **Step 3: implementar swap seguro**

Preparar a versão nova em diretório sibling do diretório instalado. Após o PID atual encerrar, o script:

1. move instalação atual para backup;
2. move staging para o caminho original;
3. inicia o novo executável;
4. consulta `/api/bootstrap` a cada 500 ms por até 20 s;
5. remove backup em sucesso;
6. encerra processo novo e restaura backup em falha;
7. reinicia a versão anterior uma única vez.

Se o primeiro rename da instalação falhar, não tocar no conteúdo atual.

- [ ] **Step 4: confirmar GREEN**

Executar CI completo e confirmar `UpdateServiceTests` + publish.

- [ ] **Step 5: commit**

`fix: adicionar rollback ao atualizador`

---

### Task 3: .NET 10, versão e gates de dependência

**Files:**
- Modify: `src/NfeAgendamento.App/NfeAgendamento.App.csproj`
- Modify: `tests/NfeAgendamento.App.Tests/NfeAgendamento.App.Tests.csproj`
- Modify: `.github/workflows/ci.yml`
- Modify: `.github/workflows/release-bridge.yml`
- Modify: `tests/js/release-readiness-regression.test.js`
- Modify: `README.md`

- [ ] **Step 1: escrever regressão de configuração RED**

Exigir `net10.0-windows`, `dotnet-version: 10.0.x`, auditoria `--vulnerable --include-transitive --format json` no CI e Release Bridge, além de versão candidata `v0.1.25`.

- [ ] **Step 2: confirmar RED**

Esperado: `release-readiness-regression.test.js` falha no runtime/configuração ainda em .NET 8.

- [ ] **Step 3: migrar runtime e versão**

```xml
<TargetFramework>net10.0-windows</TargetFramework>
<Version>0.1.25</Version>
```

Atualizar `System.Security.Cryptography.ProtectedData` para `10.0.11`; manter WebView2 na versão estável já usada quando não houver necessidade de mudança.

- [ ] **Step 4: adicionar auditoria de vulnerabilidades**

Gerar JSON com:

```powershell
dotnet list Nfe-Agendamento.sln package --vulnerable --include-transitive --format json
```

PowerShell deve percorrer top-level/transitive packages e lançar erro quando existir `vulnerabilities` não vazio.

- [ ] **Step 5: confirmar GREEN no SDK 10**

GitHub Actions deve restaurar, testar, executar todas as regressões JS, auditar dependências, compilar e publicar `win-x64` autocontido.

- [ ] **Step 6: commit**

`chore: migrar v0.1.25 para dotnet 10`

---

### Task 4: Hardening do repositório e análise estática

**Files:**
- Modify: `.gitignore`
- Create: `.github/dependabot.yml`
- Create: `.github/workflows/codeql.yml`
- Modify: `tests/js/release-readiness-regression.test.js`

- [ ] **Step 1: ampliar regressão de prontidão**

Exigir que `.gitignore` contenha `*.pfx`, `*.p12`, `*.pem`, `*.key`, `.env`, `.env.*`, `secrets.json` e `*.snk`; exigir existência de Dependabot e CodeQL.

- [ ] **Step 2: adicionar proteções**

Dependabot: NuGet + GitHub Actions semanal na segunda, máximo 3 PRs por ecossistema.

CodeQL: C#, push main, PR main, quarta 06:00 UTC.

- [ ] **Step 3: confirmar GREEN**

CI principal e CodeQL devem ser criados sem erro de sintaxe/configuração.

- [ ] **Step 4: commit**

`chore: endurecer seguranca do repositorio`

---

### Task 5: Documentação operacional

**Files:**
- Modify: `README.md`
- Modify: `docs/ATUALIZACAO-E-INICIALIZACAO.md`
- Modify: `docs/superpowers/specs/2026-09-03-audit-hardening-design.md` apenas para registrar o status de execução, sem reescrever a arquitetura-alvo.

- [ ] **Step 1: documentar fencing e rollback**

README deve explicar revalidação imediatamente antes do POST fiscal e atualização por swap/rollback com health check de 20 s.

- [ ] **Step 2: documentar limitação de assinatura**

Registrar claramente que a raiz de confiança independente permanece pendente até provisionamento do GitHub Secret externo; não descrever a v0.1.25 como assinada quando ela não for.

- [ ] **Step 3: atualizar checklist físico**

Manter teste com dois PCs, takeover, cache, perda do compartilhamento, A1, Portal e rollback.

- [ ] **Step 4: commit**

`docs: preparar operacao da v0.1.25`

---

### Task 6: Verificação e release v0.1.25

**Files:** nenhum código novo; usa workflows.

- [ ] **Step 1: verificar SHA final da main**

Confirmar que o CI desse SHA está `completed/success` e que build/publish ocorreram no mesmo run.

- [ ] **Step 2: executar Release Bridge**

Input: `v0.1.25` a partir de `main`.

- [ ] **Step 3: validar workflow de release**

Confirmar testes .NET, JS, auditoria, build e publish todos verdes.

- [ ] **Step 4: validar release publicada**

A release `v0.1.25` deve apontar para o SHA testado e conter `Nfe-Agendamento-win-x64.zip` com digest SHA-256 informado pelo GitHub.

- [ ] **Step 5: documentação pós-release**

Atualizar README para marcar `v0.1.25` como última release publicada e rodar CI desse commit documental.

## Deferred Security Gate

A assinatura RSA-PSS independente prevista no design não deve ser falsa nem enfraquecida. Para concluí-la será necessário provisionar fora do repositório o GitHub Environment `release-signing` e o Secret `NFE_UPDATE_SIGNING_KEY_PKCS8_B64`. Até isso ocorrer, a v0.1.25 mantém a validação existente do digest SHA-256 publicado pelo GitHub e todas as demais proteções implementadas neste plano.