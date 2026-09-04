# GitHub Operations Simplification Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Simplificar CI e release do NFe Agendamento, eliminar duplicação, permitir disparo de release sem clique manual e manter todos os gates de segurança existentes.

**Architecture:** CI e Release Bridge passam a delegar os gates comuns a um único `scripts/verify.ps1`. O Release Bridge mantém `workflow_dispatch` e ganha um trigger de `push` restrito a `.github/release-request.json`; o request informa a versão e é validado contra o `.csproj` e as tags existentes. A publicação continua presa ao SHA disparador e usa Sigstore keyless.

**Tech Stack:** GitHub Actions, PowerShell 7, .NET 10, Node.js, GitHub CLI, Cosign/Sigstore.

**Spec:** `docs/superpowers/specs/2026-09-04-github-operations-simplification-design.md`

## Global Constraints

- Trabalhar diretamente na `main`.
- Manter somente `ci.yml`, `codeql.yml` e `release-bridge.yml` como workflows.
- Não alterar comportamento fiscal, fila, cache, certificado A1, DANFE ou arquitetura multi-PC.
- Não criar nova release apenas para validar este hardening.
- Preservar Sigstore keyless, OIDC e vínculo ao SHA imutável.
- Não introduzir secrets persistentes.

---

### Task 1: Proteções de regressão do fluxo

**Files:**
- Modify: `tests/js/release-readiness-regression.test.js`
- Test: `tests/js/release-readiness-regression.test.js`

**Interfaces:**
- Consumes: conteúdo dos workflows, `scripts/verify.ps1`, `.github/release-request.json`, `.csproj` e README.
- Produces: invariantes automatizadas para o novo fluxo.

- [ ] **Step 1: Escrever primeiro as novas assertions**

Adicionar assertions que exijam:

```js
assert.ok(fs.existsSync(path.join(root, 'scripts/verify.ps1')));
assert.ok(fs.existsSync(path.join(root, '.github/release-request.json')));
assert.ok(ci.includes('scripts/verify.ps1'));
assert.ok(bridge.includes('scripts/verify.ps1'));
assert.ok(bridge.includes('push:'));
assert.ok(bridge.includes('.github/release-request.json'));
assert.ok(bridge.includes('workflow_dispatch:'));
assert.ok(bridge.includes('ConvertFrom-Json'));
assert.ok(bridge.includes('<Version>'));
assert.ok(!bridge.includes('fallback do Portal integrado ao site'));
```

Também exigir `permissions: contents: read`, `timeout-minutes` e retenção curta no CI.

- [ ] **Step 2: Executar o teste e confirmar RED**

Run:

```bash
node tests/js/release-readiness-regression.test.js
```

Expected: FAIL porque script/request/trigger ainda não existem.

---

### Task 2: Criar a fonte única de verificação

**Files:**
- Create: `scripts/verify.ps1`
- Modify: `.github/workflows/ci.yml`

**Interfaces:**
- Consumes: solução `Nfe-Agendamento.sln` e testes JS existentes.
- Produces: comando único `pwsh -File scripts/verify.ps1 -Restore` usado pelo CI.

- [ ] **Step 1: Criar `scripts/verify.ps1`**

O script deve:

```powershell
param([switch]$Restore)
$ErrorActionPreference = 'Stop'
if ($Restore) { dotnet restore Nfe-Agendamento.sln; if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE } }
# auditoria NuGet JSON, falhando se houver vulnerabilidades
# dotnet test --no-restore
# todos os testes JS existentes
# dotnet build --no-restore
```

Cada comando externo deve ter `$LASTEXITCODE` validado.

- [ ] **Step 2: Simplificar `ci.yml`**

Substituir restore/auditoria/testes/build duplicados por:

```yaml
permissions:
  contents: read

jobs:
  test:
    timeout-minutes: 30
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: 10.0.x
      - name: Verificar projeto
        shell: pwsh
        run: ./scripts/verify.ps1 -Restore
```

Manter publish/zip/upload. No upload artifact adicionar:

```yaml
retention-days: 7
```

- [ ] **Step 3: Executar regressão**

Run:

```bash
node tests/js/release-readiness-regression.test.js
```

Expected: ainda FAIL por faltar o request/trigger de release, mas assertions de CI/script devem passar.

---

### Task 3: Adicionar request de release sem disparar publicação

**Files:**
- Create: `.github/release-request.json`

**Interfaces:**
- Consumes: versão base atual do projeto.
- Produces: payload simples para futuras releases.

- [ ] **Step 1: Criar request com a versão já publicada**

```json
{
  "version": "0.1.26"
}
```

Este arquivo deve ser criado **antes** de adicionar o trigger de `push` ao Release Bridge, para não disparar uma release durante esta manutenção.

- [ ] **Step 2: Validar JSON**

Run:

```powershell
Get-Content .github/release-request.json -Raw | ConvertFrom-Json
```

Expected: parse sem erro.

---

### Task 4: Simplificar e automatizar o Release Bridge

**Files:**
- Modify: `.github/workflows/release-bridge.yml`

**Interfaces:**
- Consumes: `inputs.version` em dispatch manual ou `.github/release-request.json` em push.
- Produces: versão normalizada `steps.version.outputs.version` e `tag`.

- [ ] **Step 1: Adicionar trigger restrito**

```yaml
on:
  push:
    branches: [main]
    paths:
      - '.github/release-request.json'
  workflow_dispatch:
    inputs:
      version:
        description: 'Versão da release (ex.: v0.1.27)'
        required: true
        type: string
```

- [ ] **Step 2: Resolver versão conforme evento**

No step `Validar versão`, usar `workflow_dispatch` quando aplicável; em `push`, ler `.github/release-request.json` com `ConvertFrom-Json`.

- [ ] **Step 3: Validar contra `<Version>` do projeto**

Ler `src/NfeAgendamento.App/NfeAgendamento.App.csproj`, extrair `<Version>` e exigir igualdade com a versão solicitada.

- [ ] **Step 4: Reutilizar `verify.ps1`**

Substituir restore/auditoria/testes/build duplicados por:

```yaml
- name: Verificar projeto
  shell: pwsh
  run: ./scripts/verify.ps1 -Restore
```

- [ ] **Step 5: Remover release notes hard-coded**

Publicar com notas geradas pelo GitHub:

```powershell
gh release create "${{ steps.version.outputs.tag }}" artifacts/Nfe-Agendamento-win-x64.zip artifacts/Nfe-Agendamento-win-x64.zip.sigstore.json --target "${{ github.sha }}" --title "NFe Agendamento ${{ steps.version.outputs.tag }}" --generate-notes
```

- [ ] **Step 6: Executar regressão e confirmar GREEN**

Run:

```bash
node tests/js/release-readiness-regression.test.js
```

Expected: PASS.

---

### Task 5: Atualizar documentação

**Files:**
- Modify: `README.md`

**Interfaces:**
- Consumes: estado real da release v0.1.26 e novo fluxo.
- Produces: documentação operacional atualizada.

- [ ] **Step 1: Corrigir versão e assinatura**

README deve declarar:

- última release publicada: `v0.1.26`;
- `main`: `v0.1.26` até a próxima mudança funcional;
- updater assinado via Sigstore keyless;
- nenhuma chave RSA persistente/secret de assinatura.

- [ ] **Step 2: Documentar release request**

Explicar que futuras releases são solicitadas alterando `.github/release-request.json`, com `workflow_dispatch` mantido como fallback.

- [ ] **Step 3: Remover texto obsoleto**

Eliminar referências que dizem que a assinatura ainda depende de chave privada externa.

---

### Task 6: Verificação completa e integração na main

**Files:**
- All modified files above.

**Interfaces:**
- Consumes: implementação completa.
- Produces: main verificada sem publicação de release.

- [ ] **Step 1: Executar verificações locais completas**

Run:

```powershell
./scripts/verify.ps1 -Restore
```

Expected: todos os gates passam.

- [ ] **Step 2: Fazer commits em ordem segura**

Primeiro commit deve incluir `scripts/verify.ps1`, `.github/release-request.json`, testes e documentação necessária, mas **não** o trigger automático ainda. Segundo commit pode alterar `release-bridge.yml` para adicionar o trigger; como o request não muda nesse segundo commit, nenhuma release é disparada.

- [ ] **Step 3: Acompanhar CI/CodeQL da main**

Confirmar `success` nos checks aplicáveis para os commits finais.

- [ ] **Step 4: Confirmar que nenhuma nova release foi publicada**

Verificar a lista de releases e garantir que `v0.1.26` continua sendo a última release.
