# Consulta em lote Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Adicionar consulta em lote de até 50 chaves NF-e usando exclusivamente o endpoint e a fila segura já existentes, sem paralelismo fiscal novo.

**Architecture:** A nova funcionalidade vive no frontend local em `batch.js`. O módulo normaliza/deduplica entrada, executa uma chamada por vez para `POST /api/nfe/lookup`, trata backpressure/cooldown/cancelamento e atualiza uma tabela de progresso. Não há novo endpoint, novo formato de fila ou alteração do protocolo criptográfico.

**Tech Stack:** JavaScript browser/CommonJS, HTML/CSS existente, ASP.NET Core local já existente, GitHub Actions, Node.js para regressões.

**Spec:** `docs/superpowers/specs/2026-09-02-batch-lookup-design.md`

## Global Constraints

- Todas as alterações são feitas diretamente na `main`, conforme instrução explícita do projeto.
- Não gerar imagens.
- Máximo de 50 chaves únicas por lote.
- Máximo de uma consulta de lote em andamento por instalação.
- Usar somente `POST /api/nfe/lookup`; não criar endpoint de lote.
- Não alterar o protocolo criptográfico da fila compartilhada.
- `cStat=656` interrompe o lote e nunca é contornado.
- Nenhuma chave/XML do lote deve ser persistida em `localStorage`, IndexedDB ou novo arquivo compartilhado.

---

### Task 1: Núcleo testável do lote

**Files:**
- Create: `tests/js/batch-lookup-regression.test.js`
- Create: `src/NfeAgendamento.App/wwwroot/batch.js`

**Interfaces:**
- Produces: `NfeBatch.parseBatchInput(text, maxItems = 50)`
- Produces: `NfeBatch.runSequentialBatch(items, worker, hooks, signal)`
- Produces: `NfeBatch.classifyLookupFailure(statusCode, error, retryAfter)`
- Produces: CommonJS exports das mesmas funções para os testes Node.

- [ ] **Step 1: Escrever teste RED do parser**

Cobrir:

```js
const parsed = parseBatchInput([
  '4226 0912 3456 7800 0123 5500 1000 0000 0110 0000 0015',
  '42260912345678000123550010000000011000000015',
  '123'
].join('\n'));

assert.deepStrictEqual(parsed.items, ['42260912345678000123550010000000011000000015']);
assert.strictEqual(parsed.duplicates, 1);
assert.strictEqual(parsed.invalid.length, 1);
```

Também validar que 51 chaves únicas produzem `overflow > 0` e somente 50 itens executáveis.

- [ ] **Step 2: Executar teste e confirmar RED**

Run:

```bash
node tests/js/batch-lookup-regression.test.js
```

Expected: FAIL porque `batch.js`/exports ainda não existem.

- [ ] **Step 3: Implementar parser mínimo**

`parseBatchInput` deve:

```js
function parseBatchInput(text, maxItems = 50) {
  const lines = String(text || '').split(/\r?\n/);
  const items = [];
  const invalid = [];
  const seen = new Set();
  let duplicates = 0;
  let overflow = 0;

  for (const line of lines) {
    if (!line.trim()) continue;
    const key = line.replace(/\D/g, '');
    if (key.length !== 44) {
      invalid.push(line.trim());
      continue;
    }
    if (seen.has(key)) {
      duplicates += 1;
      continue;
    }
    seen.add(key);
    if (items.length >= maxItems) {
      overflow += 1;
      continue;
    }
    items.push(key);
  }

  return { items, invalid, duplicates, overflow };
}
```

- [ ] **Step 4: Testar executor serial RED/GREEN**

Adicionar teste que mede concorrência:

```js
let active = 0;
let maxConcurrent = 0;
const order = [];
await runSequentialBatch(['1','2','3'], async (item) => {
  active += 1;
  maxConcurrent = Math.max(maxConcurrent, active);
  await new Promise(resolve => setTimeout(resolve, 5));
  order.push(item);
  active -= 1;
  return { kind: 'success' };
}, {}, new AbortController().signal);
assert.strictEqual(maxConcurrent, 1);
assert.deepStrictEqual(order, ['1','2','3']);
```

Implementar loop `for ... of` que aguarda cada worker antes de seguir.

- [ ] **Step 5: Testar cancelamento**

O executor deve verificar `signal.aborted` antes de iniciar cada item e chamar hook `onCancelled(index, item)` para os pendentes.

- [ ] **Step 6: Testar classificação de 429**

Cobrir:

```js
assert.deepStrictEqual(
  classifyLookupFailure(429, { status: 'fila_ocupada' }, '7'),
  { kind: 'busy', retryAfterSeconds: 7 }
);
assert.strictEqual(
  classifyLookupFailure(429, { status: 'consumo_indevido', cStat: '656' }, '3600').kind,
  'blocked'
);
```

- [ ] **Step 7: Rodar regressão do módulo**

Run:

```bash
node tests/js/batch-lookup-regression.test.js
```

Expected: PASS.

- [ ] **Step 8: Commit**

Commit message:

```text
Adicionar núcleo serial da consulta em lote
```

---

### Task 2: Interface e integração com o endpoint existente

**Files:**
- Modify: `src/NfeAgendamento.App/wwwroot/index.html`
- Modify: `src/NfeAgendamento.App/wwwroot/batch.js`
- Modify: `src/NfeAgendamento.App/wwwroot/ui-adjustments.css`
- Test: `tests/js/batch-lookup-regression.test.js`

**Interfaces:**
- Consumes: `csrfToken`, `currentXml`, `currentKey`, `renderDanfe()` do frontend existente.
- Consumes: `NfeLookupFeedback.buildLookupErrorMessage` para mensagens consistentes.
- Produces: controles `batchInput`, `startBatch`, `cancelBatch`, `clearBatch`, `batchSummary`, `batchResults`.

- [ ] **Step 1: Adicionar teste RED da interface**

Ler `index.html` no teste e exigir:

```js
assert.ok(index.includes('id="batchInput"'));
assert.ok(index.includes('id="startBatch"'));
assert.ok(index.includes('id="cancelBatch"'));
assert.ok(index.includes('id="batchResults"'));
assert.ok(index.includes('<script src="/batch.js" defer></script>'));
```

- [ ] **Step 2: Executar teste e confirmar RED**

Run:

```bash
node tests/js/batch-lookup-regression.test.js
```

Expected: FAIL nos controles ausentes.

- [ ] **Step 3: Adicionar painel HTML**

Depois de `workspace-grid`, adicionar um painel full-width com:

```html
<section class="panel batch-panel" aria-labelledby="batchTitle">
  <div class="panel-heading">
    <div>
      <p class="panel-kicker">CONSULTA EM LOTE</p>
      <h2 id="batchTitle">Consultar várias NF-e</h2>
      <p>Uma chave por linha. O aplicativo envia uma consulta por vez para a fila segura.</p>
    </div>
  </div>
  <div class="field">
    <label for="batchInput">Chaves de acesso</label>
    <textarea id="batchInput" rows="6" spellcheck="false" placeholder="Cole uma chave de 44 dígitos por linha"></textarea>
    <p id="batchInputSummary" class="muted"></p>
  </div>
  <div class="batch-actions">
    <button id="clearBatch" class="secondary" type="button">Limpar</button>
    <button id="cancelBatch" class="secondary" type="button" hidden>Cancelar lote</button>
    <button id="startBatch" class="primary" type="button">Iniciar lote</button>
  </div>
  <p id="batchSummary" class="status" aria-live="polite"></p>
  <div class="batch-table-wrap" hidden>
    <table class="batch-table">
      <thead><tr><th>#</th><th>Chave</th><th>Status</th><th>Ações</th></tr></thead>
      <tbody id="batchResults"></tbody>
    </table>
  </div>
</section>
```

Carregar `batch.js` depois de `app.js` e `lookup-feedback.js`.

- [ ] **Step 4: Implementar orquestração DOM**

No browser, `batch.js` deve:

- atualizar `batchInputSummary` no evento `input`;
- bloquear textarea/start/clear durante execução;
- criar uma linha por item antes do início;
- executar `runSequentialBatch`;
- atualizar status por hooks;
- manter `Map<accessKey, xml>` apenas em memória;
- usar `AbortController` para cancelamento.

- [ ] **Step 5: Implementar worker HTTP com retry de fila ocupada**

Pseudo-fluxo:

```js
for (let attempt = 0; attempt <= 3; attempt += 1) {
  const response = await fetch('/api/nfe/lookup', { method: 'POST', headers: ..., body: ..., signal });
  if (response.ok) return { kind: 'success', xml: await response.text() };
  const error = await response.json().catch(() => ({ message: 'Falha na consulta.' }));
  const classification = classifyLookupFailure(response.status, error, response.headers.get('Retry-After'));
  if (classification.kind === 'busy' && attempt < 3) {
    await delay(classification.retryAfterSeconds * 1000, signal);
    continue;
  }
  return { ...classification, statusCode: response.status, error };
}
```

- [ ] **Step 6: Implementar bloqueio 656**

Quando worker retornar `kind === 'blocked'`, `runSequentialBatch` deve encerrar via resultado `stop: true`; hooks marcam os pendentes como `Não processada — cooldown SEFAZ`.

- [ ] **Step 7: Implementar ações por linha**

Para `data-action="danfe"`:

```js
currentXml = xmlByKey.get(key) || '';
currentKey = key;
renderDanfe();
```

Para `data-action="xml"`, criar `Blob`, URL temporária, disparar download e revogar URL.

- [ ] **Step 8: Adicionar CSS focado**

Adicionar apenas classes da área de lote, seguindo azul/amarelo existente:

- `.batch-panel`
- `.batch-actions`
- `.batch-table-wrap`
- `.batch-table`
- `.batch-status-*`
- responsividade para mobile sem alterar layout principal.

- [ ] **Step 9: Rodar regressão do lote**

Run:

```bash
node tests/js/batch-lookup-regression.test.js
```

Expected: PASS.

- [ ] **Step 10: Commit**

Commit message:

```text
Integrar consulta em lote à interface local
```

---

### Task 3: CI, release e documentação operacional

**Files:**
- Modify: `.github/workflows/ci.yml`
- Modify: `.github/workflows/release-bridge.yml`
- Modify: `README.md`
- Modify: `docs/CENTRAL-LAN.md`
- Test: `tests/js/release-readiness-regression.test.js` somente se necessário para refletir a nova etapa obrigatória.

**Interfaces:**
- Produces: CI e Release Bridge executando `node tests/js/batch-lookup-regression.test.js`.

- [ ] **Step 1: Adicionar teste do lote aos workflows**

Em ambos os workflows, depois das regressões de lookup:

```yaml
- name: Testar consulta em lote
  run: node tests/js/batch-lookup-regression.test.js
```

- [ ] **Step 2: Atualizar README**

Substituir a afirmação antiga `A consulta em lote permanece removida.` por documentação do novo lote serial:

- até 50 chaves únicas;
- uma por vez por instalação;
- mesma fila segura;
- retry limitado apenas para fila ocupada;
- `656` interrompe o lote;
- sem histórico persistente.

Adicionar teste físico de lote Central + cliente ao checklist da próxima release.

- [ ] **Step 3: Atualizar guia operacional**

Adicionar seção **Consulta em lote** em `docs/CENTRAL-LAN.md` com uso e comportamento em multi-PC.

- [ ] **Step 4: Executar todos os testes JS**

Run:

```bash
node tests/js/product-mapping-regression.test.js
node tests/js/lookup-feedback-regression.test.js
node tests/js/batch-lookup-regression.test.js
node tests/js/release-readiness-regression.test.js
```

Expected: todos PASS.

- [ ] **Step 5: Commit**

Commit message:

```text
Documentar e validar consulta em lote
```

---

### Task 4: Verificação final da main

**Files:**
- No new implementation files unless verification exposes a concrete regression.

- [ ] **Step 1: Executar suíte .NET completa**

Run:

```bash
dotnet restore Nfe-Agendamento.sln
dotnet test Nfe-Agendamento.sln -c Release --no-restore
```

Expected: 0 failures.

- [ ] **Step 2: Executar regressões JS completas**

Run:

```bash
node tests/js/product-mapping-regression.test.js
node tests/js/lookup-feedback-regression.test.js
node tests/js/batch-lookup-regression.test.js
node tests/js/release-readiness-regression.test.js
```

Expected: todos PASS.

- [ ] **Step 3: Build Release**

Run:

```bash
dotnet build Nfe-Agendamento.sln -c Release --no-restore
```

Expected: exit code 0.

- [ ] **Step 4: Publish Windows**

Run:

```bash
dotnet publish src/NfeAgendamento.App/NfeAgendamento.App.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o artifacts/nfe-agendamento
```

Expected: exit code 0 e `NfeAgendamento.App.exe` produzido.

- [ ] **Step 5: Confirmar CI do HEAD final**

Aguardar o workflow `CI` da `main` e exigir `conclusion = success`, incluindo artifact Windows.

- [ ] **Step 6: Revisar diff da entrega**

Confirmar que não houve:

- novo endpoint de lote;
- mudança no protocolo criptográfico;
- `Promise.all` para consultas NF-e do lote;
- persistência das chaves/XML em storage do browser;
- alteração de firewall/LAN.

- [ ] **Step 7: Preparar pacote de teste**

Baixar o artifact Windows do HEAD verde e disponibilizar ao usuário para teste físico nos três PCs.
