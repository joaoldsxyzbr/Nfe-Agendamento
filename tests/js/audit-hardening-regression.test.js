const assert = require('assert');
const fs = require('fs');
const path = require('path');

const root = path.resolve(__dirname, '../..');
const read = (relativePath) => fs.readFileSync(path.join(root, relativePath), 'utf8');

const bridge = read('.github/workflows/release-bridge.yml');
const project = read('src/NfeAgendamento.App/NfeAgendamento.App.csproj');
const index = read('src/NfeAgendamento.App/wwwroot/index.html');
const bootstrap = read('src/NfeAgendamento.App/SharedQueue/SharedQueueGroupBootstrapService.cs');
const cosignInstallerSha = '6f9f17788090df1f26f669e9d70d6ae9567deba6';
const workflowDirectory = path.join(root, '.github/workflows');
const workflowFiles = fs.readdirSync(workflowDirectory)
  .filter((name) => /\.ya?ml$/i.test(name));

for (const workflowFile of workflowFiles) {
  const workflow = read(path.join('.github/workflows', workflowFile));
  const mutableUses = workflow.split(/\r?\n/).filter((line) => {
    const match = line.match(/^\s*-?\s*uses:\s*([^\s#]+)/i);
    if (!match || match[1].startsWith('./')) return false;
    return !/@[0-9a-f]{40}$/i.test(match[1]);
  });
  assert.deepStrictEqual(
    mutableUses,
    [],
    `${workflowFile} deve fixar toda action em um commit SHA hexadecimal de 40 caracteres.`
  );
}

assert.ok(
  bridge.includes(`sigstore/cosign-installer@${cosignInstallerSha}`),
  'Release Bridge deve fixar cosign-installer no commit exato da v4.1.2.'
);
assert.ok(
  !project.includes('PackageReference Include="System.Security.Cryptography.ProtectedData"'),
  'ProtectedData não deve permanecer como PackageReference redundante no .NET 10.'
);
assert.ok(
  !/<script(?![^>]*\bsrc=)[^>]*>[\s\S]*?<\/script>/i.test(index),
  'index.html não deve conter script inline para permitir CSP estrita.'
);
assert.ok(
  !bootstrap.includes('System.Reflection') && !bootstrap.includes('FieldInfo') && !bootstrap.includes('GetField('),
  'Migração do grupo não deve depender de reflection sobre campos privados do store legado.'
);

console.log('OK: hardening de supply chain, dependências, CSP e migração sem reflection permanece aplicado.');
