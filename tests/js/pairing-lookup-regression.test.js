const assert = require('assert');
const fs = require('fs');
const path = require('path');
const vm = require('vm');

const scriptPath = path.resolve(__dirname, '../../src/NfeAgendamento.App/wwwroot/pairing.js');
const programPath = path.resolve(__dirname, '../../src/NfeAgendamento.App/Program.cs');
const source = fs.readFileSync(scriptPath, 'utf8');
const program = fs.readFileSync(programPath, 'utf8');

function makeElement(initial = {}) {
  const listeners = new Map();
  return {
    hidden: false,
    disabled: false,
    textContent: '',
    className: '',
    value: '',
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
    centralPairingExpiry: makeElement()
  };

  const context = {
    console,
    Promise,
    Map,
    Date,
    document: {
      getElementById(id) { return elements[id] || null; }
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
    program.includes('result.Success && !group.TryImportCandidateBundle()'),
    'A API de pareamento não pode responder sucesso quando o pacote do grupo não foi importado.'
  );

  assert.doesNotThrow(() => new vm.Script(source, { filename: scriptPath }), 'pairing.js deve ser sintaticamente válido.');

  console.log('OK: estado de pareamento nunca deixa Consultar NF-e sem ação e não aceita autorização incompleta.');
})().catch(error => {
  console.error(error);
  process.exitCode = 1;
});
