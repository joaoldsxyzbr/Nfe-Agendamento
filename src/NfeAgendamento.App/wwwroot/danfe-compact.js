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

const FIRST_PAGE_PRODUCT_SPACE_MM = 108;
const CONTINUATION_PRODUCT_SPACE_MM = 224;

function estimateProductHeight(det) {
  const prod = firstElement(det, 'prod');
  const tax = buildProductTaxData(det);
  const description = value(prod, 'xProd');
  const descriptionLines = Math.max(1, Math.ceil(description.length / 52));
  const taxLines = tax.taxNote ? Math.max(1, Math.ceil(tax.taxNote.length / 58)) : 0;
  return 3.4 + ((descriptionLines - 1) * 1.8) + (taxLines * 1.7);
}

function estimateAdditionalPenaltyMm(additionalText) {
  const text = String(additionalText || '').trim();
  if (!text) return 0;
  const estimatedLines = Math.ceil(text.length / 145);
  return Math.max(0, estimatedLines - 4) * 1.35;
}

function paginateProductsByAvailableSpace(products, additionalText) {
  if (!products.length) return [[]];

  const pages = [];
  let currentPage = [];
  let available = Math.max(58, FIRST_PAGE_PRODUCT_SPACE_MM - estimateAdditionalPenaltyMm(additionalText));
  let used = 0;

  for (const product of products) {
    const rowHeight = estimateProductHeight(product);
    if (currentPage.length && used + rowHeight > available) {
      pages.push(currentPage);
      currentPage = [];
      available = CONTINUATION_PRODUCT_SPACE_MM;
      used = 0;
    }
    currentPage.push(product);
    used += rowHeight;
  }

  if (currentPage.length) pages.push(currentPage);
  return pages;
}

paginateProducts = paginateProductsByAvailableSpace;
