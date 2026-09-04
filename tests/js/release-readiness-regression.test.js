const assert = require('assert');
const fs = require('fs');
const path = require('path');
const vm = require('vm');

const root = path.resolve(__dirname, '../..');
const read = (relativePath) => fs.readFileSync(path.join(root, relativePath), 'utf8');

const ciPath = '.github/workflows/ci.yml';
const bridgePath = '.github/workflows/release-bridge.yml';
const codeqlPath = '.github/workflows/codeql.yml';
const dependabotPath = '.github/dependabot.yml';
const requestPath = '.github/release-request.json';
const verifyPath = 'scripts/verify.ps1';
const projectPath = 'src/NfeAgendamento.App/NfeAgendamento.App.csproj';
const testProjectPath = 'tests/NfeAgendamento.App.Tests/NfeAgendamento.App.Tests.csproj';
const gitignorePath = '.gitignore';
const readmePath = 'README.md';
const tabsPath = 'src/NfeAgendamento.App/wwwroot/tabs.js';

const ci = read(ciPath);
const bridge = read(bridgePath);
const codeql = read(codeqlPath);
const project = read(projectPath);
const testProject = read(testProjectPath);
const gitignore = read(gitignorePath);
const readme = read(readmePath);

const workflowFiles = fs.readdirSync(path.join(root, '.github/workflows'))
  .filter((name) => /\.ya?ml$/i.test(name))
  .sort();
assert.deepStrictEqual(
  workflowFiles,
  ['ci.yml', 'codeql.yml', 'release-bridge.yml'],
  'O projeto deve manter exatamente CI, CodeQL e Release Bridge.'
);

assert.ok(fs.existsSync(path.join(root, verifyPath)), 'scripts/verify.ps1 deve existir.');
assert.ok(fs.existsSync(path.join(root, requestPath)), '.github/release-request.json deve existir.');

const verify = read(verifyPath);
const request = JSON.parse(read(requestPath));

assert.ok(/permissions:\r?\n\s*contents:\s*read/.test(ci), 'CI deve declarar contents: read.');
assert.ok(ci.includes('timeout-minutes: 30'), 'CI deve ter timeout explícito.');
assert.ok(ci.includes('./scripts/verify.ps1 -Restore'), 'CI deve usar verify.ps1.');
assert.ok(ci.includes('retention-days: 7'), 'Artifact de CI deve expirar em 7 dias.');
assert.ok(!ci.includes('dotnet test Nfe-Agendamento.sln'), 'CI não deve duplicar testes fora do verify.ps1.');
assert.ok(!ci.includes('node tests/js/'), 'CI não deve duplicar regressões JS fora do verify.ps1.');

assert.ok(bridge.includes('workflow_dispatch:'), 'Release Bridge deve manter workflow_dispatch.');
assert.ok(bridge.includes('push:'), 'Release Bridge deve aceitar trigger por push.');
assert.ok(bridge.includes("'.github/release-request.json'"), 'Push de release deve ser restrito ao request.');
assert.ok(bridge.includes('branches: [main]'), 'Trigger automático deve ficar restrito à main.');
assert.ok(bridge.includes('ConvertFrom-Json'), 'Release deve ler o request JSON.');
assert.ok(bridge.includes('NfeAgendamento.App.csproj'), 'Release deve conferir a versão do projeto.');
assert.ok(bridge.includes('<Version>'), 'Release deve extrair <Version> do projeto.');
assert.ok(bridge.includes('./scripts/verify.ps1 -Restore'), 'Release deve usar verify.ps1.');
assert.ok(bridge.includes('--generate-notes'), 'Release deve gerar notas a partir das mudanças reais.');
assert.ok(!bridge.includes('fallback do Portal integrado ao site'), 'Release notes não podem ficar presas à v0.1.26.');
assert.ok(bridge.includes('ref: ${{ github.sha }}'), 'Release deve fazer checkout do SHA disparador.');
assert.ok(bridge.includes('--target "${{ github.sha }}"'), 'Release deve publicar exatamente o SHA testado.');
assert.ok(bridge.includes('id-token: write'), 'Release deve manter OIDC para Sigstore.');
assert.ok(bridge.includes('sigstore/cosign-installer@v4.1.2'), 'Release deve instalar Cosign oficial.');
assert.ok(bridge.includes('cosign sign-blob'), 'Release deve assinar o ZIP.');
assert.ok(bridge.includes('cosign verify-blob'), 'Release deve verificar a assinatura.');
assert.ok(bridge.includes('release-bridge.yml@refs/heads/main'), 'Identidade Sigstore deve permanecer fixada ao workflow oficial.');
assert.ok(!bridge.includes('NFE_UPDATE_SIGNING_KEY_PKCS8_B64'), 'Release não deve depender de chave privada persistente.');

const projectVersionMatch = project.match(/<Version>(\d+\.\d+\.\d+)<\/Version>/);
assert.ok(projectVersionMatch, 'Projeto deve declarar versão semântica.');
const projectVersion = projectVersionMatch[1];
assert.strictEqual(request.version, projectVersion, 'release-request deve refletir a versão atual do projeto.');

const auditCommand = 'dotnet list Nfe-Agendamento.sln package --vulnerable --include-transitive --format json';
assert.ok(verify.includes(auditCommand), 'verify.ps1 deve auditar NuGet transitivo.');
assert.ok(verify.includes('dotnet test Nfe-Agendamento.sln -c Release --no-restore'), 'verify.ps1 deve executar testes .NET.');
assert.ok(verify.includes('dotnet build Nfe-Agendamento.sln -c Release --no-restore'), 'verify.ps1 deve compilar a solução.');
for (const jsTest of [
  'product-mapping-regression.test.js',
  'lookup-feedback-regression.test.js',
  'portal-fallback-regression.test.js',
  'pairing-lookup-regression.test.js',
  'batch-lookup-regression.test.js',
  'release-readiness-regression.test.js'
]) {
  assert.ok(verify.includes(jsTest), `verify.ps1 deve executar ${jsTest}.`);
}
assert.ok(verify.includes("$ErrorActionPreference = 'Stop'"), 'verify.ps1 deve falhar fechado em erros PowerShell.');
assert.ok(verify.includes('$LASTEXITCODE'), 'verify.ps1 deve verificar falhas de processos externos.');

assert.ok(project.includes('<TargetFramework>net10.0-windows</TargetFramework>'), 'Aplicação deve usar .NET 10.');
assert.ok(testProject.includes('<TargetFramework>net10.0-windows</TargetFramework>'), 'Testes devem usar .NET 10.');
assert.ok(ci.includes('dotnet-version: 10.0.x'), 'CI deve usar SDK .NET 10.');
assert.ok(bridge.includes('dotnet-version: 10.0.x'), 'Release deve usar SDK .NET 10.');

for (const entry of ['*.pfx', '*.p12', '*.pem', '*.key', '*.snk', '.env', '.env.*', 'secrets.json']) {
  assert.ok(gitignore.split(/\r?\n/).includes(entry), `.gitignore deve bloquear ${entry}.`);
}

assert.ok(fs.existsSync(path.join(root, dependabotPath)), 'Dependabot deve existir.');
const dependabot = read(dependabotPath);
assert.ok(dependabot.includes('package-ecosystem: "nuget"'), 'Dependabot deve monitorar NuGet.');
assert.ok(dependabot.includes('package-ecosystem: "github-actions"'), 'Dependabot deve monitorar GitHub Actions.');
assert.ok((dependabot.match(/open-pull-requests-limit:\s*3/g) || []).length >= 2, 'Dependabot deve limitar PRs automáticos.');

assert.ok(codeql.includes('github/codeql-action/init@v3'), 'CodeQL deve usar action oficial.');
assert.ok(codeql.includes('languages: csharp'), 'CodeQL deve analisar C#.');
assert.ok(codeql.includes('github/codeql-action/analyze@v3'), 'CodeQL deve executar análise.');
assert.ok(codeql.includes('cron:'), 'CodeQL deve manter análise agendada.');

assert.ok(readme.includes('última release publicada: **v0.1.26**'), 'README deve refletir v0.1.26 publicada.');
assert.ok(readme.includes('`main`: **v0.1.26**'), 'README deve refletir a versão atual da main.');
assert.ok(/Sigstore keyless/i.test(readme), 'README deve documentar Sigstore keyless.');
assert.ok(readme.includes('.github/release-request.json'), 'README deve documentar o request de release.');
assert.ok(
  !/depende de uma chave privada externa armazenada em GitHub Secret/i.test(readme),
  'README não pode anunciar a assinatura RSA antiga como necessária.'
);

assert.doesNotThrow(() => new vm.Script(read(tabsPath), { filename: tabsPath }), 'tabs.js deve permanecer sintaticamente válido.');

const workflowText = `${ci}\n${bridge}`;
for (const pattern of [/NFeDistribuicaoDFe/i, /\.pfx\b/i, /\.p12\b/i, /CERTIFICATE_PASSWORD/i, /SEFAZ_PASSWORD/i]) {
  assert.ok(!pattern.test(workflowText), `Workflow não pode conter credencial/endpoint fiscal real: ${pattern}`);
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

const testFiles = walk(path.join(root, 'tests')).filter((file) => /\.(cs|js)$/i.test(file));
const liveTransportUsage = testFiles.filter((file) => /new\s+NfeDistributionTransport\s*\(/.test(fs.readFileSync(file, 'utf8')));
assert.deepStrictEqual(liveTransportUsage, [], 'Testes não podem instanciar transporte fiscal real.');

const certificateFiles = walk(root).filter((file) => /\.(pfx|p12)$/i.test(file));
assert.deepStrictEqual(certificateFiles, [], 'Repositório não pode conter certificado A1 empacotado.');

console.log(`OK: fluxo GitHub enxuto, release v${projectVersion}, verificação única, Sigstore keyless e sem credenciais fiscais reais.`);
