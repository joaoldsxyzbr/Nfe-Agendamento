const assert = require('assert');
const fs = require('fs');
const path = require('path');
const vm = require('vm');

const scriptPath = path.resolve(__dirname, '../../src/NfeAgendamento.App/wwwroot/portal-fallback.js');
const source = fs.readFileSync(scriptPath, 'utf8');

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

function makeResponse(status, payload) {
  return {
    ok: status >= 200 && status < 300,
    status,
    async json() { return payload; }
  };
}

async function createHarness({ centralActive, configuredAsCentral = false }) {
  const elements = {
    portalFallbackPanel: makeElement({ hidden: true }),
    portalFallback: makeElement({ textContent: 'Consultar pela Fazenda' }),
    accessKey: makeElement({ value: '35260812345678000195550010000000011000000018' }),
    status: makeElement(),
    lookup: makeElement()
  };
  const requests = [];

  const context = {
    console,
    Promise,
    Map,
    String,
    Boolean,
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
          configuredAsCentral,
          csrfToken: 'csrf-test'
        });
      }
      if (url === '/api/nfe/portal-fallback') {
        return makeResponse(202, {
          status: 'started',
          message: 'Portal aberto.'
        });
      }
      if (url === '/api/nfe/lookup') {
        throw new Error('A contingência não pode repetir o lookup fiscal.');
      }
      throw new Error(`URL inesperada: ${url}`);
    }
  };
  context.globalThis = context;

  vm.runInNewContext(source, context, { filename: scriptPath });
  await new Promise(resolve => setImmediate(resolve));

  return { context, elements, requests };
}

(async () => {
  const leader = await createHarness({ centralActive: true, configuredAsCentral: false });
  const leaderMessage = leader.context.NfeLookupFeedback.buildLookupErrorMessage({
    statusCode: 429,
    error: { status: 'consumo_indevido', cStat: '656' }
  });

  assert.strictEqual(leaderMessage, 'Bloqueado pela SEFAZ.');
  assert.strictEqual(leader.elements.portalFallbackPanel.hidden, false, 'Líder ativo deve oferecer o fallback após 656.');

  const click = leader.elements.portalFallback.listener('click');
  assert.ok(click, 'Botão da contingência deve registrar ação de clique.');
  await click();

  assert.ok(
    leader.requests.some(request => request.url === '/api/nfe/portal-fallback'),
    'Líder deve abrir o endpoint local da contingência.'
  );
  assert.ok(
    !leader.requests.some(request => request.url === '/api/nfe/lookup'),
    'A contingência não deve repetir consChNFe pelo endpoint de lookup.'
  );
  assert.ok(leader.elements.status.textContent.includes('Portal da NF-e aberto'), leader.elements.status.textContent);

  const legacyCentralInStandby = await createHarness({ centralActive: false, configuredAsCentral: true });
  const standbyMessage = legacyCentralInStandby.context.NfeLookupFeedback.buildLookupErrorMessage({
    statusCode: 429,
    error: { status: 'consumo_indevido', cStat: '656' }
  });

  assert.strictEqual(legacyCentralInStandby.elements.portalFallbackPanel.hidden, true, 'Standby não deve exibir o fallback mesmo se era a Central antiga.');
  assert.ok(standbyMessage.includes('líder da fila'), standbyMessage);

  assert.doesNotThrow(() => new vm.Script(source, { filename: scriptPath }), 'portal-fallback.js deve ser sintaticamente válido.');

  console.log('OK: contingência 656 depende do líder ativo, não do papel legado de Central.');
})().catch(error => {
  console.error(error);
  process.exitCode = 1;
});
