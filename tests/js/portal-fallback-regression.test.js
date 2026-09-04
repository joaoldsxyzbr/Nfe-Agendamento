const assert = require('assert');
const fs = require('fs');
const path = require('path');
const vm = require('vm');

const scriptPath = path.resolve(__dirname, '../../src/NfeAgendamento.App/wwwroot/portal-fallback.js');
const source = fs.readFileSync(scriptPath, 'utf8');
const accessKey = '35260812345678000195550010000000011000000018';

function makeElement(initial = {}) {
  const listeners = new Map();
  return {
    hidden: false,
    value: '',
    textContent: '',
    className: '',
    disabled: false,
    ...initial,
    addEventListener(type, listener) {
      listeners.set(type, listener);
    },
    listener(type) {
      return listeners.get(type);
    }
  };
}

function makeResponse(status, payload = null, text = '') {
  return {
    ok: status >= 200 && status < 300,
    status,
    async json() { return payload; },
    async text() { return text; }
  };
}

async function createHarness({ centralActive = false, portalFallbackAvailable, cacheResponses = [] }) {
  const elements = {
    portalFallbackPanel: makeElement({ hidden: true }),
    portalFallback: makeElement({ textContent: 'Baixar pelo Portal' }),
    accessKey: makeElement({ value: accessKey }),
    status: makeElement(),
    lookup: makeElement()
  };
  const requests = [];
  let lookupCalls = 0;

  const context = {
    console,
    Promise,
    Map,
    String,
    Boolean,
    encodeURIComponent,
    async lookup() {
      lookupCalls += 1;
    },
    setTimeout(callback) {
      Promise.resolve().then(callback);
      return 1;
    },
    document: {
      getElementById(id) { return elements[id] || null; }
    },
    NfeLookupFeedback: {
      buildLookupErrorMessage() { return 'Bloqueado pela SEFAZ.'; }
    },
    async fetch(url, options = {}) {
      requests.push({ url, options });
      if (url === '/api/bootstrap') {
        return makeResponse(200, {
          centralActive,
          portalFallbackAvailable,
          csrfToken: 'csrf-test'
        });
      }
      if (url === '/api/nfe/portal-fallback') {
        return makeResponse(202, {
          status: 'started',
          message: 'Portal aberto.'
        });
      }
      if (url === `/api/nfe/cache/${accessKey}`) {
        return cacheResponses.shift() || makeResponse(404, { status: 'cache_miss' });
      }
      if (url === '/api/nfe/lookup') {
        throw new Error('A contingência não pode fazer polling fiscal.');
      }
      throw new Error(`URL inesperada: ${url}`);
    }
  };
  context.globalThis = context;

  vm.runInNewContext(source, context, { filename: scriptPath });
  await new Promise(resolve => setImmediate(resolve));

  return { context, elements, requests, lookupCalls: () => lookupCalls };
}

(async () => {
  const authorizedStandby = await createHarness({
    centralActive: false,
    portalFallbackAvailable: true,
    cacheResponses: [
      makeResponse(404, { status: 'cache_miss' }),
      makeResponse(200, { status: 'cache_hit' })
    ]
  });
  const allowedMessage = authorizedStandby.context.NfeLookupFeedback.buildLookupErrorMessage({
    statusCode: 429,
    error: { status: 'consumo_indevido', cStat: '656' }
  });

  assert.strictEqual(allowedMessage, 'Bloqueado pela SEFAZ.');
  assert.strictEqual(
    authorizedStandby.elements.portalFallbackPanel.hidden,
    false,
    'PC autorizado deve oferecer o fallback após 656 mesmo quando não é o líder.'
  );

  const click = authorizedStandby.elements.portalFallback.listener('click');
  assert.ok(click, 'Botão da contingência deve registrar ação de clique.');
  await click();
  await new Promise(resolve => setImmediate(resolve));
  await new Promise(resolve => setImmediate(resolve));

  assert.ok(
    authorizedStandby.requests.some(request => request.url === '/api/nfe/portal-fallback'),
    'PC autorizado deve abrir o endpoint local da contingência.'
  );
  assert.ok(
    authorizedStandby.requests.some(request => request.url === `/api/nfe/cache/${accessKey}`),
    'O site deve acompanhar somente o cache enquanto o Portal está aberto.'
  );
  assert.ok(
    !authorizedStandby.requests.some(request => request.url === '/api/nfe/lookup'),
    'O acompanhamento do Portal não deve repetir consChNFe pelo endpoint de lookup.'
  );
  assert.strictEqual(
    authorizedStandby.lookupCalls(),
    1,
    'Ao XML aparecer no cache, o site deve recarregar a NF-e automaticamente uma única vez.'
  );
  assert.strictEqual(authorizedStandby.elements.portalFallbackPanel.hidden, true, 'Fallback deve sumir após o XML chegar ao cache.');

  const unauthorizedPc = await createHarness({ centralActive: false, portalFallbackAvailable: false });
  const unauthorizedMessage = unauthorizedPc.context.NfeLookupFeedback.buildLookupErrorMessage({
    statusCode: 429,
    error: { status: 'consumo_indevido', cStat: '656' }
  });

  assert.strictEqual(unauthorizedPc.elements.portalFallbackPanel.hidden, true, 'PC não autorizado não deve exibir o fallback.');
  assert.ok(unauthorizedMessage.toLowerCase().includes('autoriz'), unauthorizedMessage);

  assert.doesNotThrow(() => new vm.Script(source, { filename: scriptPath }), 'portal-fallback.js deve ser sintaticamente válido.');

  console.log('OK: contingência 656 pode ser aberta em qualquer PC autorizado e retorna o XML ao site sem polling fiscal.');
})().catch(error => {
  console.error(error);
  process.exitCode = 1;
});
