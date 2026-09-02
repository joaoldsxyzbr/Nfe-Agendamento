(function (root, factory) {
  const api = factory(root);
  if (typeof module === 'object' && module.exports) module.exports = api;
  if (root) root.NfeBatch = api;

  if (root?.document) {
    if (root.document.readyState === 'loading') {
      root.document.addEventListener('DOMContentLoaded', api.mountBatchUi, { once: true });
    } else {
      api.mountBatchUi();
    }
  }
})(typeof globalThis !== 'undefined' ? globalThis : this, function (root) {
  const DEFAULT_MAX_ITEMS = 50;
  const MAX_BUSY_RETRIES = 3;

  let batchRunning = false;
  let batchController = null;
  const xmlByKey = new Map();

  function parseBatchInput(text, maxItems = DEFAULT_MAX_ITEMS) {
    const limit = Number.isInteger(maxItems) && maxItems > 0 ? maxItems : DEFAULT_MAX_ITEMS;
    const items = [];
    const invalid = [];
    const seen = new Set();
    let duplicates = 0;
    let overflow = 0;

    for (const rawLine of String(text || '').split(/\r?\n/)) {
      const line = rawLine.trim();
      if (!line) continue;

      const key = line.replace(/\D/g, '');
      if (key.length !== 44) {
        invalid.push(line);
        continue;
      }

      if (seen.has(key)) {
        duplicates += 1;
        continue;
      }

      seen.add(key);
      if (items.length >= limit) {
        overflow += 1;
        continue;
      }

      items.push(key);
    }

    return { items, invalid, duplicates, overflow };
  }

  function parseRetryAfterSeconds(value, fallback = null) {
    const seconds = Number.parseInt(String(value ?? ''), 10);
    return Number.isFinite(seconds) && seconds > 0 ? seconds : fallback;
  }

  function classifyLookupFailure(statusCode, error = {}, retryAfter = null) {
    if (statusCode === 429 && error?.status === 'fila_ocupada') {
      return {
        kind: 'busy',
        retryAfterSeconds: parseRetryAfterSeconds(retryAfter, 5)
      };
    }

    if (statusCode === 429 && (error?.status === 'consumo_indevido' || String(error?.cStat || '') === '656')) {
      return {
        kind: 'blocked',
        stop: true,
        retryAfterSeconds: parseRetryAfterSeconds(retryAfter, null),
        blockedUntilUtc: error?.blockedUntilUtc || null,
        message: error?.message || null
      };
    }

    if (statusCode === 404 || error?.status === 'not_found') {
      return {
        kind: 'not_found',
        message: error?.message || null
      };
    }

    if (statusCode === 409 && error?.status === 'manifestation_required') {
      return {
        kind: 'manifestation_required',
        message: error?.message || null
      };
    }

    return {
      kind: 'error',
      message: error?.message || null
    };
  }

  async function runSequentialBatch(items, worker, hooks = {}, signal) {
    if (!Array.isArray(items)) throw new TypeError('items deve ser um array.');
    if (typeof worker !== 'function') throw new TypeError('worker deve ser uma função.');

    let processed = 0;
    let cancelled = 0;
    let stopped = false;

    const cancelRemaining = (startIndex) => {
      for (let index = startIndex; index < items.length; index += 1) {
        hooks.onCancelled?.(index, items[index]);
        cancelled += 1;
      }
    };

    const skipRemaining = (startIndex, reason) => {
      for (let index = startIndex; index < items.length; index += 1) {
        hooks.onSkipped?.(index, items[index], reason);
      }
    };

    for (let index = 0; index < items.length; index += 1) {
      if (signal?.aborted) {
        cancelRemaining(index);
        break;
      }

      const item = items[index];
      hooks.onStart?.(index, item);

      let result;
      try {
        result = await worker(item, index, signal);
      } catch (error) {
        if (signal?.aborted || error?.name === 'AbortError') {
          hooks.onCancelled?.(index, item);
          cancelled += 1;
          cancelRemaining(index + 1);
          break;
        }
        result = {
          kind: 'error',
          message: error instanceof Error ? error.message : String(error)
        };
      }

      processed += 1;
      hooks.onResult?.(index, item, result);

      if (result?.stop === true) {
        stopped = true;
        skipRemaining(index + 1, result);
        break;
      }
    }

    return { processed, cancelled, stopped };
  }

  function abortError() {
    const error = new Error('Operação cancelada.');
    error.name = 'AbortError';
    return error;
  }

  function delay(milliseconds, signal) {
    return new Promise((resolve, reject) => {
      if (signal?.aborted) {
        reject(abortError());
        return;
      }

      const timeout = setTimeout(() => {
        signal?.removeEventListener('abort', onAbort);
        resolve();
      }, Math.max(0, milliseconds));

      const onAbort = () => {
        clearTimeout(timeout);
        signal?.removeEventListener('abort', onAbort);
        reject(abortError());
      };

      signal?.addEventListener('abort', onAbort, { once: true });
    });
  }

  function formatKey(key) {
    return String(key || '').replace(/(.{4})/g, '$1 ').trim();
  }

  function setBatchSummary(message, error = false) {
    const element = root?.document?.getElementById('batchSummary');
    if (!element) return;
    element.textContent = message || '';
    element.className = `status batch-summary${error ? ' error' : ''}`;
  }

  function setRowStatus(index, label, state = 'pending', detail = '') {
    const row = root?.document?.querySelector(`[data-batch-index="${index}"]`);
    if (!row) return;

    const status = row.querySelector('[data-batch-status]');
    if (status) {
      status.textContent = label;
      status.className = `batch-status batch-status-${state}`;
      if (detail) status.title = detail;
      else status.removeAttribute('title');
    }
  }

  function setRowActions(index, key, enabled) {
    const row = root?.document?.querySelector(`[data-batch-index="${index}"]`);
    const cell = row?.querySelector('[data-batch-actions]');
    if (!cell) return;

    cell.replaceChildren();
    if (!enabled) return;

    const danfe = root.document.createElement('button');
    danfe.type = 'button';
    danfe.className = 'batch-row-action';
    danfe.dataset.action = 'danfe';
    danfe.dataset.key = key;
    danfe.textContent = 'Ver DANFE';

    const xml = root.document.createElement('button');
    xml.type = 'button';
    xml.className = 'batch-row-action';
    xml.dataset.action = 'xml';
    xml.dataset.key = key;
    xml.textContent = 'Baixar XML';

    cell.append(danfe, xml);
  }

  function renderRows(items) {
    const body = root?.document?.getElementById('batchResults');
    const wrap = root?.document?.getElementById('batchTableWrap');
    if (!body || !wrap) return;

    body.replaceChildren();
    items.forEach((key, index) => {
      const row = root.document.createElement('tr');
      row.dataset.batchIndex = String(index);

      const number = root.document.createElement('td');
      number.textContent = String(index + 1);

      const keyCell = root.document.createElement('td');
      keyCell.className = 'batch-key';
      keyCell.textContent = formatKey(key);

      const status = root.document.createElement('td');
      const statusText = root.document.createElement('span');
      statusText.dataset.batchStatus = '';
      statusText.className = 'batch-status batch-status-pending';
      statusText.textContent = 'Aguardando';
      status.append(statusText);

      const actions = root.document.createElement('td');
      actions.dataset.batchActions = '';
      actions.className = 'batch-row-actions';

      row.append(number, keyCell, status, actions);
      body.append(row);
    });

    wrap.hidden = items.length === 0;
  }

  function inputSummary(parsed) {
    if (!parsed.items.length && !parsed.invalid.length && !parsed.duplicates && !parsed.overflow) {
      return 'Nenhuma chave informada.';
    }

    const parts = [`${parsed.items.length} válida${parsed.items.length === 1 ? '' : 's'}`];
    if (parsed.duplicates) parts.push(`${parsed.duplicates} duplicada${parsed.duplicates === 1 ? '' : 's'} ignorada${parsed.duplicates === 1 ? '' : 's'}`);
    if (parsed.invalid.length) parts.push(`${parsed.invalid.length} inválida${parsed.invalid.length === 1 ? '' : 's'} ignorada${parsed.invalid.length === 1 ? '' : 's'}`);
    if (parsed.overflow) parts.push(`${parsed.overflow} acima do limite de ${DEFAULT_MAX_ITEMS}`);
    return `${parts.join(' · ')}.`;
  }

  function updateInputSummary() {
    const input = root?.document?.getElementById('batchInput');
    const summary = root?.document?.getElementById('batchInputSummary');
    const start = root?.document?.getElementById('startBatch');
    if (!input || !summary || !start) return;

    const parsed = parseBatchInput(input.value);
    summary.textContent = inputSummary(parsed);
    start.disabled = batchRunning || parsed.items.length === 0;
  }

  function setControlsRunning(running) {
    batchRunning = running;

    const input = root?.document?.getElementById('batchInput');
    const start = root?.document?.getElementById('startBatch');
    const cancel = root?.document?.getElementById('cancelBatch');
    const clear = root?.document?.getElementById('clearBatch');
    if (input) input.disabled = running;
    if (start) start.disabled = running;
    if (cancel) cancel.hidden = !running;
    if (clear) clear.disabled = running;

    for (const id of ['accessKey', 'lookup', 'newLookup']) {
      const element = root?.document?.getElementById(id);
      if (element) element.disabled = running;
    }

    if (!running) updateInputSummary();
  }

  function buildFeedbackMessage(response, error) {
    if (root?.NfeLookupFeedback?.buildLookupErrorMessage) {
      return root.NfeLookupFeedback.buildLookupErrorMessage({
        statusCode: response.status,
        error,
        retryAfter: response.headers.get('Retry-After')
      });
    }
    return error?.message || 'Falha na consulta.';
  }

  async function lookupBatchItem(accessKey, index, signal) {
    for (let attempt = 0; attempt <= MAX_BUSY_RETRIES; attempt += 1) {
      const response = await root.fetch('/api/nfe/lookup', {
        method: 'POST',
        cache: 'no-store',
        headers: {
          'content-type': 'application/json',
          'X-CSRF-Token': typeof csrfToken === 'string' ? csrfToken : ''
        },
        body: JSON.stringify({ accessKey }),
        signal
      });

      if (response.ok) {
        return {
          kind: 'success',
          xml: await response.text()
        };
      }

      const error = await response.json().catch(() => ({ message: 'Falha na consulta.' }));
      const failure = classifyLookupFailure(response.status, error, response.headers.get('Retry-After'));
      const message = buildFeedbackMessage(response, error);

      if (failure.kind === 'busy' && attempt < MAX_BUSY_RETRIES) {
        const seconds = failure.retryAfterSeconds || 5;
        setRowStatus(index, `Fila ocupada — nova tentativa em ${seconds}s`, 'waiting', message);
        await delay(seconds * 1000, signal);
        continue;
      }

      if (failure.kind === 'busy') {
        return {
          kind: 'error',
          message: `A Central continuou ocupada após ${MAX_BUSY_RETRIES + 1} tentativas. ${message}`
        };
      }

      return { ...failure, message };
    }

    return { kind: 'error', message: 'Não foi possível concluir a consulta.' };
  }

  function applyResult(index, key, result, counters) {
    counters.processed += 1;

    switch (result?.kind) {
      case 'success':
        xmlByKey.set(key, result.xml);
        counters.success += 1;
        setRowStatus(index, 'Concluída', 'success');
        setRowActions(index, key, true);
        break;
      case 'not_found':
        counters.failed += 1;
        setRowStatus(index, 'Não encontrada', 'warning', result.message || 'NF-e não encontrada.');
        break;
      case 'manifestation_required':
        counters.failed += 1;
        setRowStatus(index, 'Manifestação necessária', 'warning', result.message || 'Manifestação necessária.');
        break;
      case 'blocked':
        counters.failed += 1;
        setRowStatus(index, 'Bloqueada pela SEFAZ', 'error', result.message || 'Consultas temporariamente bloqueadas.');
        setBatchSummary(result.message || 'A SEFAZ bloqueou temporariamente novas consultas. O lote foi interrompido.', true);
        break;
      default:
        counters.failed += 1;
        setRowStatus(index, 'Erro', 'error', result?.message || 'Falha na consulta.');
        break;
    }

    if (result?.kind !== 'blocked') {
      setBatchSummary(`${counters.processed} de ${counters.total} processadas · ${counters.success} localizada${counters.success === 1 ? '' : 's'}.`);
    }
  }

  async function startBatch() {
    if (batchRunning) return;

    if (typeof lookupInProgress !== 'undefined' && lookupInProgress) {
      setBatchSummary('Aguarde a consulta individual atual terminar antes de iniciar o lote.', true);
      return;
    }

    const input = root?.document?.getElementById('batchInput');
    if (!input) return;

    const parsed = parseBatchInput(input.value);
    if (!parsed.items.length) {
      setBatchSummary('Informe pelo menos uma chave de acesso válida com 44 dígitos.', true);
      return;
    }

    xmlByKey.clear();
    renderRows(parsed.items);
    batchController = new AbortController();
    setControlsRunning(true);

    const counters = {
      total: parsed.items.length,
      processed: 0,
      success: 0,
      failed: 0
    };

    setBatchSummary(`Lote iniciado: 0 de ${counters.total} processadas.`);

    try {
      const outcome = await runSequentialBatch(
        parsed.items,
        lookupBatchItem,
        {
          onStart: (index) => setRowStatus(index, 'Consultando', 'running'),
          onResult: (index, key, result) => applyResult(index, key, result, counters),
          onCancelled: (index) => setRowStatus(index, 'Cancelada', 'cancelled'),
          onSkipped: (index, _key, reason) => {
            const label = reason?.kind === 'blocked'
              ? 'Não processada — cooldown SEFAZ'
              : 'Não processada';
            setRowStatus(index, label, 'cancelled');
          }
        },
        batchController.signal
      );

      if (outcome.stopped) return;

      if (outcome.cancelled > 0 || batchController.signal.aborted) {
        setBatchSummary(`Lote cancelado. ${counters.processed} de ${counters.total} chegaram a ser processadas.`);
        return;
      }

      const failureText = counters.failed ? ` · ${counters.failed} sem sucesso` : '';
      setBatchSummary(`Lote concluído: ${counters.success} localizada${counters.success === 1 ? '' : 's'}${failureText}.`);
    } catch (error) {
      if (error?.name === 'AbortError') {
        setBatchSummary(`Lote cancelado. ${counters.processed} de ${counters.total} chegaram a ser processadas.`);
      } else {
        setBatchSummary(error instanceof Error ? error.message : 'Falha inesperada no lote.', true);
      }
    } finally {
      batchController = null;
      setControlsRunning(false);
    }
  }

  function cancelBatch() {
    batchController?.abort();
  }

  function clearBatch() {
    if (batchRunning) return;

    xmlByKey.clear();
    const input = root?.document?.getElementById('batchInput');
    const body = root?.document?.getElementById('batchResults');
    const wrap = root?.document?.getElementById('batchTableWrap');
    if (input) input.value = '';
    if (body) body.replaceChildren();
    if (wrap) wrap.hidden = true;
    setBatchSummary('');
    updateInputSummary();
    input?.focus();
  }

  function openBatchDanfe(key) {
    const xml = xmlByKey.get(key);
    if (!xml) return;

    try {
      currentXml = xml;
      currentKey = key;
      renderDanfe();
    } catch (error) {
      setBatchSummary(error instanceof Error ? error.message : 'Não foi possível abrir o DANFE.', true);
    }
  }

  function downloadBatchXml(key) {
    const xml = xmlByKey.get(key);
    if (!xml) return;

    const blob = new Blob([xml], { type: 'application/xml' });
    const url = URL.createObjectURL(blob);
    const anchor = root.document.createElement('a');
    anchor.href = url;
    anchor.download = `${key}.xml`;
    anchor.hidden = true;
    root.document.body.append(anchor);
    anchor.click();
    anchor.remove();
    setTimeout(() => URL.revokeObjectURL(url), 1000);
  }

  function handleRowAction(event) {
    const button = event.target.closest('button[data-action][data-key]');
    if (!button) return;

    const { action, key } = button.dataset;
    if (action === 'danfe') openBatchDanfe(key);
    if (action === 'xml') downloadBatchXml(key);
  }

  function mountBatchUi() {
    if (!root?.document) return;

    const input = root.document.getElementById('batchInput');
    const start = root.document.getElementById('startBatch');
    const cancel = root.document.getElementById('cancelBatch');
    const clear = root.document.getElementById('clearBatch');
    const results = root.document.getElementById('batchResults');
    if (!input || !start || !cancel || !clear || !results) return;
    if (input.dataset.batchMounted === 'true') return;
    input.dataset.batchMounted = 'true';

    input.addEventListener('input', updateInputSummary);
    start.addEventListener('click', () => startBatch().catch(error => {
      setBatchSummary(error instanceof Error ? error.message : 'Falha inesperada no lote.', true);
      setControlsRunning(false);
    }));
    cancel.addEventListener('click', cancelBatch);
    clear.addEventListener('click', clearBatch);
    results.addEventListener('click', handleRowAction);

    updateInputSummary();
  }

  return {
    DEFAULT_MAX_ITEMS,
    parseBatchInput,
    runSequentialBatch,
    classifyLookupFailure,
    mountBatchUi,
    isRunning: () => batchRunning
  };
});
