const assert = require('assert');
const fs = require('fs');
const path = require('path');

const root = path.resolve(__dirname, '../..');
const read = (relativePath) => fs.readFileSync(path.join(root, relativePath), 'utf8');

const ciPath = '.github/workflows/ci.yml';
const bridgePath = '.github/workflows/release-bridge.yml';
const legacyTagPath = '.github/workflows/release-on-tag.yml';
const ci = read(ciPath);
const bridge = read(bridgePath);

assert.ok(bridge.includes('workflow_dispatch:'), 'Release Bridge deve continuar manual.');
assert.ok(bridge.includes('node tests/js/product-mapping-regression.test.js'), 'Release deve validar o mapeamento Fernando Klein.');
assert.ok(bridge.includes('node tests/js/lookup-feedback-regression.test.js'), 'Release deve validar o feedback fiscal.');
assert.ok(!fs.existsSync(path.join(root, legacyTagPath)), 'Workflow legado por tag deve ser removido para existir um único caminho de release.');

const workflowText = `${ci}\n${bridge}`;
const forbiddenWorkflowPatterns = [
  /NFeDistribuicaoDFe/i,
  /\.pfx\b/i,
  /\.p12\b/i,
  /CERTIFICATE_PASSWORD/i,
  /SEFAZ_PASSWORD/i
];
for (const pattern of forbiddenWorkflowPatterns) {
  assert.ok(!pattern.test(workflowText), `Workflow não pode depender de credencial/endpoint fiscal real: ${pattern}`);
}

function walk(directory) {
  const entries = fs.readdirSync(directory, { withFileTypes: true });
  return entries.flatMap((entry) => {
    const full = path.join(directory, entry.name);
    return entry.isDirectory() ? walk(full) : [full];
  });
}

const testFiles = walk(path.join(root, 'tests'))
  .filter((file) => /\.(cs|js)$/i.test(file));
const liveTransportUsage = testFiles
  .filter((file) => /new\s+NfeDistributionTransport\s*\(/.test(fs.readFileSync(file, 'utf8')));
assert.deepStrictEqual(liveTransportUsage, [], 'Testes não podem instanciar o transporte fiscal real.');

const certificateFiles = walk(root)
  .filter((file) => /\.(pfx|p12)$/i.test(file));
assert.deepStrictEqual(certificateFiles, [], 'Repositório não pode conter certificado A1 empacotado.');

console.log('OK: release tem um único caminho, executa regressões e não depende de credenciais fiscais reais.');
