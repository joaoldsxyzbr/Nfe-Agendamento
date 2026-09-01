const assert = require('assert');
const path = require('path');

const feedbackPath = path.resolve(__dirname, '../../src/NfeAgendamento.App/wwwroot/lookup-feedback.js');
const { buildLookupErrorMessage, parseRetryAfterSeconds } = require(feedbackPath);

assert.strictEqual(parseRetryAfterSeconds('5'), 5);
assert.strictEqual(parseRetryAfterSeconds('0'), null);
assert.strictEqual(parseRetryAfterSeconds('abc'), null);

const busy = buildLookupErrorMessage({
  statusCode: 429,
  error: { status: 'fila_ocupada', message: 'Fila cheia.' },
  retryAfter: '7'
});
assert.ok(busy.includes('Central está ocupada'), busy);
assert.ok(busy.includes('7 segundos'), busy);
assert.ok(!busy.includes('SEFAZ bloqueou'), busy);

const busyDefault = buildLookupErrorMessage({
  statusCode: 429,
  error: { status: 'fila_ocupada' },
  retryAfter: null
});
assert.ok(busyDefault.includes('5 segundos'), busyDefault);

const blocked = buildLookupErrorMessage({
  statusCode: 429,
  error: {
    status: 'consumo_indevido',
    cStat: '656',
    blockedUntilUtc: '2026-09-01T20:30:00Z'
  },
  retryAfter: '3600'
});
assert.ok(blocked.includes('SEFAZ'), blocked);
assert.ok(blocked.includes('bloqueadas'), blocked);
assert.ok(blocked.includes('Não repita a consulta'), blocked);
assert.ok(!blocked.includes('Central está ocupada'), blocked);

const generic = buildLookupErrorMessage({
  statusCode: 502,
  error: { status: 'network_error', message: 'Falha controlada da rede.' },
  retryAfter: null
});
assert.strictEqual(generic, 'Falha controlada da rede.');

console.log('OK: feedback da consulta diferencia fila ocupada, cStat=656 e falhas genéricas.');
