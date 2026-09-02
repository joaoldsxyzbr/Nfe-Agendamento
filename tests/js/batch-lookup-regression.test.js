const assert = require('assert');
const fs = require('fs');
const path = require('path');

const batchPath = path.resolve(__dirname, '../../src/NfeAgendamento.App/wwwroot/batch.js');
const indexPath = path.resolve(__dirname, '../../src/NfeAgendamento.App/wwwroot/index.html');
const {
  parseBatchInput,
  runSequentialBatch,
  classifyLookupFailure
} = require(batchPath);

const SAMPLE_KEY = '42260912345678000123550010000000011000000015';

function makeKey(index) {
  const prefix = String(index).padStart(43, '0');
  return `${prefix}1`;
}

async function main() {
  const parsed = parseBatchInput([
    '4226 0912 3456 7800 0123 5500 1000 0000 0110 0000 0015',
    SAMPLE_KEY,
    '123'
  ].join('\n'));

  assert.deepStrictEqual(parsed.items, [SAMPLE_KEY]);
  assert.strictEqual(parsed.duplicates, 1);
  assert.strictEqual(parsed.invalid.length, 1);
  assert.strictEqual(parsed.overflow, 0);

  const many = parseBatchInput(Array.from({ length: 51 }, (_, index) => makeKey(index + 1)).join('\n'));
  assert.strictEqual(many.items.length, 50);
  assert.strictEqual(many.overflow, 1);

  let active = 0;
  let maxConcurrent = 0;
  const order = [];
  const serial = await runSequentialBatch(
    ['1', '2', '3'],
    async (item) => {
      active += 1;
      maxConcurrent = Math.max(maxConcurrent, active);
      await new Promise(resolve => setTimeout(resolve, 5));
      order.push(item);
      active -= 1;
      return { kind: 'success' };
    },
    {},
    new AbortController().signal
  );

  assert.strictEqual(maxConcurrent, 1);
  assert.deepStrictEqual(order, ['1', '2', '3']);
  assert.strictEqual(serial.processed, 3);
  assert.strictEqual(serial.cancelled, 0);
  assert.strictEqual(serial.stopped, false);

  const controller = new AbortController();
  const cancelledItems = [];
  const cancellation = await runSequentialBatch(
    ['1', '2', '3'],
    async (item) => {
      if (item === '1') controller.abort();
      return { kind: 'success' };
    },
    {
      onCancelled: (_index, item) => cancelledItems.push(item)
    },
    controller.signal
  );

  assert.deepStrictEqual(cancelledItems, ['2', '3']);
  assert.strictEqual(cancellation.processed, 1);
  assert.strictEqual(cancellation.cancelled, 2);

  const stoppedItems = [];
  const stopped = await runSequentialBatch(
    ['1', '2', '3'],
    async (item) => item === '2'
      ? { kind: 'blocked', stop: true }
      : { kind: 'success' },
    {
      onSkipped: (_index, item, reason) => stoppedItems.push([item, reason.kind])
    },
    new AbortController().signal
  );

  assert.deepStrictEqual(stoppedItems, [['3', 'blocked']]);
  assert.strictEqual(stopped.processed, 2);
  assert.strictEqual(stopped.stopped, true);

  assert.deepStrictEqual(
    classifyLookupFailure(429, { status: 'fila_ocupada' }, '7'),
    { kind: 'busy', retryAfterSeconds: 7 }
  );

  const blocked = classifyLookupFailure(
    429,
    {
      status: 'consumo_indevido',
      cStat: '656',
      message: 'Consumo indevido.',
      blockedUntilUtc: '2026-09-02T15:00:00Z'
    },
    '3600'
  );
  assert.strictEqual(blocked.kind, 'blocked');
  assert.strictEqual(blocked.retryAfterSeconds, 3600);
  assert.strictEqual(blocked.blockedUntilUtc, '2026-09-02T15:00:00Z');

  const notFound = classifyLookupFailure(404, { status: 'not_found', message: 'Não encontrada.' }, null);
  assert.strictEqual(notFound.kind, 'not_found');

  const manifestation = classifyLookupFailure(409, { status: 'manifestation_required' }, null);
  assert.strictEqual(manifestation.kind, 'manifestation_required');

  const index = fs.readFileSync(indexPath, 'utf8');
  assert.ok(index.includes('id="batchInput"'), 'textarea do lote ausente');
  assert.ok(index.includes('id="startBatch"'), 'botão iniciar lote ausente');
  assert.ok(index.includes('id="cancelBatch"'), 'botão cancelar lote ausente');
  assert.ok(index.includes('id="clearBatch"'), 'botão limpar lote ausente');
  assert.ok(index.includes('id="batchResults"'), 'tabela de resultados do lote ausente');
  assert.ok(index.includes('<script src="/batch.js" defer></script>'), 'batch.js não está carregado na interface');

  const batchSource = fs.readFileSync(batchPath, 'utf8');
  assert.ok(!batchSource.includes('localStorage'), 'lote não pode persistir chaves/XML em localStorage');
  assert.ok(!batchSource.includes('indexedDB'), 'lote não pode persistir chaves/XML em IndexedDB');

  console.log('OK: lote normaliza entrada, limita 50 itens, executa em série, cancela, respeita bloqueios e está integrado à interface.');
}

main().catch(error => {
  console.error(error);
  process.exitCode = 1;
});
