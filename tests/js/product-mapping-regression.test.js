const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

const root = path.resolve(__dirname, '../..');
const mappingPath = path.join(root, 'src/NfeAgendamento.App/wwwroot/product-mapping.js');
const mappingSource = `${fs.readFileSync(mappingPath, 'utf8')}\n;globalThis.__mapping = NfeProductMapping;`;
const context = {};
vm.runInNewContext(mappingSource, context, { filename: mappingPath });
const mapping = context.__mapping;

function tag(xml, name) {
  const match = xml.match(new RegExp(`<${name}[^>]*>([\\s\\S]*?)</${name}>`, 'i'));
  return match ? match[1].trim() : '';
}

function parseProducts(xml) {
  return [...xml.matchAll(/<det\b[^>]*>([\s\S]*?)<\/det>/gi)].map(match => ({
    cProd: tag(match[1], 'cProd'),
    xProd: tag(match[1], 'xProd')
  }));
}

function resolve(xProd, cProd = 'SRC', emitterTaxId = '067.277.939-05') {
  return mapping.resolveFernandoKleinProduct({ emitterTaxId, xProd, cProd });
}

assert.ok(Array.isArray(mapping.catalog), 'O catálogo deve ser exposto para testes de regressão.');
assert.equal(mapping.catalog.length, 17, 'O catálogo Fernando Klein deve conter 17 produtos internos.');
assert.equal(mapping.validateCatalog(mapping.catalog), true, 'O catálogo oficial deve ser válido e sem aliases conflitantes.');

const expectedCodes = [
  '73457', '104128', '104129', '30228', '104130', '104109', '104108', '104106', '104113',
  '104107', '104104', '104110', '104115', '104114', '104111', '104112', '104105'
];

const fixture = fs.readFileSync(path.join(root, 'tests/Fixtures/fernando-klein-full.xml'), 'utf8');
const emitterCpf = tag(tag(fixture, 'emit'), 'CPF');
const products = parseProducts(fixture);
assert.equal(products.length, 18, 'A NF de regressão deve conter os 17 produtos conhecidos e 1 desconhecido.');

products.slice(0, 17).forEach((product, index) => {
  const result = mapping.resolveFernandoKleinProduct({ emitterTaxId: emitterCpf, ...product });
  assert.equal(result.sourceCode, product.cProd, `Item ${index + 1}: cProd original deve ser preservado.`);
  assert.equal(result.internalCode, expectedCodes[index], `Item ${index + 1}: código interno incorreto para ${product.xProd}.`);
});

const unknown = mapping.resolveFernandoKleinProduct({ emitterTaxId: emitterCpf, ...products[17] });
assert.equal(unknown.sourceCode, 'FK999');
assert.equal(unknown.internalCode, '', 'Produto desconhecido não pode receber código interno por aproximação.');

const summary = mapping.summarizeFernandoKleinProducts({ emitterTaxId: emitterCpf, products });
assert.equal(summary.applies, true, 'A NF Fernando Klein deve ativar o resumo de mapeamento.');
assert.equal(summary.total, 18);
assert.equal(summary.mapped, 17);
assert.equal(summary.unmapped, 1);
assert.equal(summary.unknownProducts.length, 1);
assert.equal(summary.unknownProducts[0].cProd, 'FK999');
assert.equal(summary.unknownProducts[0].xProd, 'PRODUTO NOVO SEM MAPA');

const otherSummary = mapping.summarizeFernandoKleinProducts({
  emitterTaxId: '12.345.678/0001-90',
  products: [{ cProd: 'OUT001', xProd: 'ALFACE' }]
});
assert.equal(otherSummary.applies, false, 'Outro fornecedor não deve gerar resumo Fernando Klein.');
assert.equal(otherSummary.total, 1);
assert.equal(otherSummary.mapped, 0);
assert.equal(otherSummary.unmapped, 0);
assert.equal(otherSummary.unknownProducts.length, 0);

for (const variant of ['RÚCULA', 'rucula', ' VERDURAS - RÚCULA ', 'VERDURAS: RUCULA']) {
  assert.equal(resolve(variant).internalCode, '104111', `Normalização falhou para ${variant}.`);
}
assert.equal(resolve('CEBOLA').internalCode, '104106');
assert.equal(resolve('CEBOLINHA').internalCode, '104106');
assert.equal(resolve('SALSA').internalCode, '104105');
assert.equal(resolve('SALSINHA').internalCode, '104105');

const otherSupplier = resolve('ALFACE', 'OUT001', '12.345.678/0001-90');
assert.equal(otherSupplier.sourceCode, 'OUT001');
assert.equal(otherSupplier.internalCode, '', 'Outro fornecedor não pode usar o mapa Fernando Klein.');

const transporterOnly = resolve('ALFACE', 'TR001', '98.765.432/0001-10');
assert.equal(transporterOnly.internalCode, '', 'Fernando Klein como transportador não pode ativar o mapeamento.');

assert.throws(
  () => mapping.validateCatalog([
    { internalCode: '1', aliases: ['ALFACE'] },
    { internalCode: '2', aliases: [' alface '] }
  ]),
  /Alias conflitante ALFACE/,
  'Aliases equivalentes após normalização devem gerar conflito.'
);

console.log(`OK: ${products.length} itens da NF de regressão + aliases, normalização, isolamento e resumo de mapeamento.`);
