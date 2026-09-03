const assert = require('assert');
const fs = require('fs');
const path = require('path');
const vm = require('vm');

const root = path.resolve(__dirname, '../..');
const read = (relativePath) => fs.readFileSync(path.join(root, relativePath), 'utf8');

const ciPath = '.github/workflows/ci.yml';
const bridgePath = '.github/workflows/release-bridge.yml';
const legacyTagPath = '.github/workflows/release-on-tag.yml';
const projectPath = 'src/NfeAgendamento.App/NfeAgendamento.App.csproj';
const readmePath = 'README.md';
const tabsPath = 'src/NfeAgendamento.App/wwwroot/tabs.js';
const ci = read(ciPath);
const bridge = read(bridgePath);
const project = read(projectPath);
const readme = read(readmePath);

assert.ok(bridge.includes('workflow_dispatch:'), 'Release Bridge deve continuar manual.');
assert.ok(bridge.includes('node tests/js/product-mapping-regression.test.js'), 'Release deve validar o mapeamento Fernando Klein.');
assert.ok(bridge.includes('node tests/js/lookup-feedback-regression.test.js'), 'Release deve validar o feedback fiscal.');
assert.ok(ci.includes('node tests/js/portal-fallback-regression.test.js'), 'CI deve validar a contingência pelo Portal NF-e.');
assert.ok(bridge.includes('node tests/js/portal-fallback-regression.test.js'), 'Release deve validar a contingência pelo Portal NF-e.');
assert.ok(ci.includes('node tests/js/batch-lookup-regression.test.js'), 'CI deve validar a consulta em lote.');
assert.ok(bridge.includes('node tests/js/batch-lookup-regression.test.js'), 'Release deve validar a consulta em lote.');
assert.ok(bridge.includes('node tests/js/release-readiness-regression.test.js'), 'Release deve validar a própria prontidão.');
assert.ok(!fs.existsSync(path.join(root, legacyTagPath)), 'Workflow legado por tag deve ser removido para existir um único caminho de release.');
assert.ok(bridge.includes('GITHUB_REF') && bridge.includes('refs/heads/main'), 'Release manual deve recusar execução a partir de branch ou tag diferente de main.');
assert.ok(bridge.includes('ref: ${{ github.sha }}'), 'Release deve fazer checkout do SHA imutável que disparou o workflow.');
assert.ok(bridge.includes('--target "${{ github.sha }}"'), 'Tag/release deve apontar para o mesmo SHA que foi testado e empacotado.');
assert.ok(!bridge.includes('--target main'), 'Release não pode tagar main mutável depois dos testes.');
assert.ok(bridge.includes('-p:Version=${{ steps.version.outputs.version }}'), 'Release deve aplicar ao binário a versão validada informada no workflow.');

const projectVersionMatch = project.match(/<Version>(\d+\.\d+\.\d+)<\/Version>/);
assert.ok(projectVersionMatch, 'Projeto deve declarar uma versão semântica base no formato X.Y.Z.');
const projectVersion = projectVersionMatch[1];
assert.ok(readme.includes(`candidata **v${projectVersion}**`), 'README deve indicar a mesma versão candidata declarada no projeto.');
assert.doesNotThrow(() => new vm.Script(read(tabsPath), { filename: tabsPath }), 'tabs.js deve permanecer sintaticamente válido.');

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

const ignoredDirectories = new Set(['.git', 'bin', 'obj', 'artifacts']);
function walk(directory) {
  const entries = fs.readdirSync(directory, { withFileTypes: true });
  return entries.flatMap((entry) => {
    if (entry.isDirectory() && ignoredDirectories.has(entry.name)) return [];
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

console.log(`OK: release usa SHA imutável, versão candidata v${projectVersion}, regressões e nenhuma credencial fiscal real.`);
