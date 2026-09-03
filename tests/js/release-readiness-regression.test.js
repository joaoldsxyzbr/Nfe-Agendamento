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
const testProjectPath = 'tests/NfeAgendamento.App.Tests/NfeAgendamento.App.Tests.csproj';
const readmePath = 'README.md';
const tabsPath = 'src/NfeAgendamento.App/wwwroot/tabs.js';
const ci = read(ciPath);
const bridge = read(bridgePath);
const project = read(projectPath);
const testProject = read(testProjectPath);
const readme = read(readmePath);

assert.ok(bridge.includes('workflow_dispatch:'), 'Release Bridge deve continuar manual.');
assert.ok(bridge.includes('node tests/js/product-mapping-regression.test.js'), 'Release deve validar o mapeamento Fernando Klein.');
assert.ok(bridge.includes('node tests/js/lookup-feedback-regression.test.js'), 'Release deve validar o feedback fiscal.');
assert.ok(ci.includes('node tests/js/portal-fallback-regression.test.js'), 'CI deve validar a contingência pelo Portal NF-e.');
assert.ok(bridge.includes('node tests/js/portal-fallback-regression.test.js'), 'Release deve validar a contingência pelo Portal NF-e.');
assert.ok(ci.includes('node tests/js/pairing-lookup-regression.test.js'), 'CI deve validar que Consultar NF-e não fique bloqueado pelo bootstrap.');
assert.ok(bridge.includes('node tests/js/pairing-lookup-regression.test.js'), 'Release deve validar que Consultar NF-e não fique bloqueado pelo bootstrap.');
assert.ok(ci.includes('node tests/js/batch-lookup-regression.test.js'), 'CI deve validar a consulta em lote.');
assert.ok(bridge.includes('node tests/js/batch-lookup-regression.test.js'), 'Release deve validar a consulta em lote.');
assert.ok(bridge.includes('node tests/js/release-readiness-regression.test.js'), 'Release deve validar a própria prontidão.');
assert.ok(!fs.existsSync(path.join(root, legacyTagPath)), 'Workflow legado por tag deve ser removido para existir um único caminho de release.');
assert.ok(bridge.includes('GITHUB_REF') && bridge.includes('refs/heads/main'), 'Release manual deve recusar execução a partir de branch ou tag diferente de main.');
assert.ok(bridge.includes('ref: ${{ github.sha }}'), 'Release deve fazer checkout do SHA imutável que disparou o workflow.');
assert.ok(bridge.includes('--target "${{ github.sha }}"'), 'Tag/release deve apontar para o mesmo SHA que foi testado e empacotado.');
assert.ok(!bridge.includes('--target main'), 'Release não pode tagar main mutável depois dos testes.');
assert.ok(bridge.includes('-p:Version=${{ steps.version.outputs.version }}'), 'Release deve aplicar ao binário a versão validada informada no workflow.');

assert.ok(project.includes('<TargetFramework>net10.0-windows</TargetFramework>'), 'Aplicação deve usar .NET 10 LTS.');
assert.ok(testProject.includes('<TargetFramework>net10.0-windows</TargetFramework>'), 'Testes devem usar .NET 10 LTS.');
assert.ok(ci.includes('dotnet-version: 10.0.x'), 'CI deve usar SDK .NET 10.');
assert.ok(bridge.includes('dotnet-version: 10.0.x'), 'Release deve usar SDK .NET 10.');
const auditCommand = 'dotnet list Nfe-Agendamento.sln package --vulnerable --include-transitive --format json';
assert.ok(ci.includes(auditCommand), 'CI deve auditar dependências NuGet vulneráveis, incluindo transitivas.');
assert.ok(bridge.includes(auditCommand), 'Release deve auditar dependências NuGet vulneráveis, incluindo transitivas.');

const projectVersionMatch = project.match(/<Version>(\d+\.\d+\.\d+)<\/Version>/);
assert.ok(projectVersionMatch, 'Projeto deve declarar uma versão semântica base no formato X.Y.Z.');
const projectVersion = projectVersionMatch[1];
assert.strictEqual(projectVersion, '0.1.25', 'Versão esperada para este hardening é 0.1.25.');
const mainVersionPattern = new RegExp('`main`:[^\\n]*\\*\\*v' + projectVersion.replace(/\./g, '\\.') + '\\*\\*');
assert.ok(mainVersionPattern.test(readme), 'README deve indicar na linha da main a mesma versão declarada no projeto.');
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

console.log(`OK: release usa SHA imutável, .NET 10, versão v${projectVersion}, auditoria de dependências e nenhuma credencial fiscal real.`);
