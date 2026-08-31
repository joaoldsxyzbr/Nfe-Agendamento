// Ajustes de impressão para aproximar a ocupação da A4 ao DANFE de referência.
const originalBuildTransport = buildTransport;
const originalBuildProductsTable = buildProductsTable;

function hasTransportData(infNFe) {
  const transp = firstElement(infNFe, 'transp');
  if (!transp) return false;

  const transporta = firstElement(transp, 'transporta');
  const vehicle = firstElement(transp, 'veicTransp');
  const volumes = elementsByName(transp, 'vol');
  const usefulValues = [
    value(transporta, 'xNome'), value(transporta, 'CNPJ'), value(transporta, 'CPF'),
    value(transporta, 'xEnder'), value(transporta, 'xMun'), value(transporta, 'IE'),
    value(vehicle, 'RNTC'), value(vehicle, 'placa'),
    ...volumes.flatMap(volume => [value(volume, 'qVol'), value(volume, 'esp'), value(volume, 'marca'), value(volume, 'nVol'), value(volume, 'pesoB'), value(volume, 'pesoL')])
  ];

  return usefulValues.some(item => String(item || '').trim() !== '');
}

function transportSection(infNFe) {
  return hasTransportData(infNFe) ? originalBuildTransport(infNFe) : '';
}

buildTransport = transportSection;

buildProductsTable = function compactProductsTable(products) {
  return originalBuildProductsTable(products).replace('class="products-table"', 'class="products-table danfe-products-fill"');
};
