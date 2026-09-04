const assert = require('assert');
const fs = require('fs');
const path = require('path');
const vm = require('vm');

const scriptPath = path.resolve(__dirname, '../../src/NfeAgendamento.App/wwwroot/pairing.js');
const programPath = path.resolve(__dirname, '../../src/NfeAgendamento.App/Program.cs');
const htmlPath = path.resolve(__dirname, '../../src/NfeAgendamento.App/wwwroot/index.html');
const source = fs.readFileSync(scriptPath, 'utf8');
const program = fs.readFileSync(programPath, 'utf8');
const html = fs.readFileSync(htmlPath, 'utf8');

function makeElement(initial = {}) {
  const listeners = new Map();
  return {
    hidden: false,
    disabled: false,
    textContent: '',
    className: '',
    value: '',
    innerHTML: '',
    ...initial,
    addEventListener(type, listener) {
      listeners.set(type, listener);
    },
    listener(type) {
      return listeners.get(type);
    },
    focus() {}
  };
}

function makeResponse(status, payload) {
  return {
    ok: status >= 200 && status < 300,
    status,
    async json() { return payload; }
  };
}

(async () => {
  const elements = {
    centralConfigPanel: makeElement(),
    clientPairingPanel: makeElement({ hidden: true }),
    generatePairingCode: makeElement(),
    centralPairingStatus: makeElement(),
    clientPairingForm: makeElement({ hidden: true }),
    clientPairingStatus: makeElement(),
    lookup: makeElement(),
    pairingCode: makeElement(),
    pairClient: makeElement(),
    centralPairingResult: makeElement({ hidden: true }),
    centralPairingCode: makeElement(),
    centralPairingExpiry: makeElement(),
    authorizedClientsPanel: makeElement({ hidden: true }),
    authorizedClientsList: makeElement(),
    authorizedClientsStatus: makeElement(),
    refreshAuthorizedClients: makeElement()
  };

  const context = {
    console,
    Promise,
    Map,
    Date,
    document: {
      getElementById(id) { return elements[id] || null; },
      createElement() { return makeElement(); }
    },
    async fetch(url) {
      if (url !== '/api/bootstrap') throw new Error(`URL inesperada: ${url}`);
      return makeResponse(200, {
        csrfToken: 'csrf-test',
        clientPaired: false,
        centralActive: false,
        centralOnline: false,
        centralId: null
      });
    }
  };
  context.window = context;
  context.globalThis = context;

  vm.runInNewContext(source, context, { filename: scriptPath });
  await new Promise(resolve => setImmediate(resolve));

  assert.strictEqual(
    elements.lookup.disabled,
    false,
    'O estado transitório de pareamento não pode transformar Consultar NF-e em um botão sem ação; o backend deve devolver a mensagem de configuração.'
  );
  assert.strictEqual(elements.clientPairingPanel.hidden, false, 'A orientação de autorização deve continuar visível quando necessária.');
  assert.ok(elements.clientPairingStatus.textContent.includes('precisa ser autorizado'), elements.clientPairingStatus.textContent);

  assert.ok(
    program.includes('clientPaired = group.IsCandidateReady,'),
    'Somente o estado real do grupo pode marcar o PC como autorizado; pareamento legado não pode esconder o formulário de recuperação.'
  );
  assert.ok(
    !program.includes('clientPaired = group.IsCandidateReady || clientStatus.IsPaired || state.IsConfiguredAsCentral'),
    'Estado legado/local não pode mascarar falha na adesão ao grupo automático.'
  );
  assert.ok(
    program.includes('builder.Services.AddSingleton<SharedQueuePairingCoordinator>();')
      && program.includes('SharedQueuePairingCoordinator pairing')
      && program.includes('await pairing.PairAsync(request.Code, cancellationToken)'),
    'A API deve delegar o pareamento ao coordenador que só confirma sucesso depois da validação segura do grupo.'
  );
  assert.ok(
    source.includes('if (pairingInFlight) return;')
      && source.includes('pairingInFlight = true;')
      && source.includes('pairingInFlight = false;'),
    'O navegador deve bloquear envios duplicados do mesmo pareamento.'
  );

  assert.ok(html.includes('id="authorizedClientsPanel"'), 'A configuração deve ter painel de PCs autorizados.');
  assert.ok(html.includes('id="authorizedClientsList"'), 'A configuração deve ter lista de PCs autorizados.');
  assert.ok(source.includes("fetch('/api/pairing/clients'"), 'O líder deve carregar os PCs autorizados pela API local.');
  assert.ok(source.includes("fetch('/api/pairing/revoke'"), 'A interface deve permitir revogar um PC pela API local.');
  assert.ok(source.includes('client.isCurrent'), 'A interface deve identificar o PC líder e impedir sua remoção acidental.');

  assert.doesNotThrow(() => new vm.Script(source, { filename: scriptPath }), 'pairing.js deve ser sintaticamente válido.');

  console.log('OK: pareamento exige estado real do grupo, coordenação atômica e gerenciamento seguro de PCs.');
})().catch(error => {
  console.error(error);
  process.exitCode = 1;
});
