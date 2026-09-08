const assert = require('assert');
const fs = require('fs');
const path = require('path');

const programPath = path.resolve(__dirname, '../../src/NfeAgendamento.App/Program.cs');
const htmlPath = path.resolve(__dirname, '../../src/NfeAgendamento.App/wwwroot/index.html');
const program = fs.readFileSync(programPath, 'utf8');
const html = fs.readFileSync(htmlPath, 'utf8');

assert.ok(!program.includes('/api/pairing/'), 'A API de produção não deve expor endpoints de pareamento.');
assert.ok(!program.includes('SharedQueueCentralService'), 'A composição de produção não deve depender de uma Central/líder.');
assert.ok(!program.includes('SharedQueueClient'), 'Cada PC deve consultar localmente; não deve encaminhar pedidos para outro PC.');
assert.ok(program.includes('mode = "shared_lock"'), 'O bootstrap deve identificar a coordenação por lock compartilhado.');
assert.ok(program.includes('pairingRequired = false'), 'O bootstrap deve declarar explicitamente que pareamento não é necessário.');

assert.ok(!html.includes('/pairing.js'), 'A interface não deve carregar o fluxo de pareamento antigo.');
assert.ok(!html.includes('id="pairingCode"'), 'A interface não deve solicitar código de pareamento.');
assert.ok(!html.includes('id="generatePairingCode"'), 'A interface não deve permitir gerar código de pareamento.');
assert.ok(!html.includes('id="authorizedClientsPanel"'), 'A interface não deve gerenciar PCs autorizados.');
assert.ok(html.includes('Pasta compartilhada'), 'A configuração deve explicar a coordenação pela pasta compartilhada.');
assert.ok(html.includes('id="sharedFolderStatus"'), 'A configuração deve mostrar o estado da pasta compartilhada.');
assert.ok(html.includes('/shared-status.js'), 'A interface deve carregar o status simples da pasta compartilhada.');

console.log('OK: produção sem Central, sem pareamento e com coordenação por pasta compartilhada.');
