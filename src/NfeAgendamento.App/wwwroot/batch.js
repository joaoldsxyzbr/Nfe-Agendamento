(function (root, factory) {
  const api = factory();
  if (typeof module === 'object' && module.exports) module.exports = api;
  if (root) root.NfeBatch = api;
})(typeof globalThis !== 'undefined' ? globalThis : this, function () {
  const DEFAULT_MAX_ITEMS = 50;

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

  return {
    DEFAULT_MAX_ITEMS,
    parseBatchInput,
    runSequentialBatch,
    classifyLookupFailure
  };
});
