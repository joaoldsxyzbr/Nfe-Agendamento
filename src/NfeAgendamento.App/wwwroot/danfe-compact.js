// Ajustes de impressão para aproximar a ocupação da A4 ao DANFE de referência.
const originalBuildTransport = buildTransport;

function resolveProductCodes(det, prod) {
  const document = det?.ownerDocument;
  const infNFe = firstElement(document, 'infNFe');
  const emit = firstElement(infNFe, 'emit');
  const emitterTaxId = value(emit, 'CNPJ') || value(emit, 'CPF');

  return NfeProductMapping.resolveFernandoKleinProduct({
    emitterTaxId,
    xProd: value(prod, 'xProd'),
    cProd: value(prod, 'cProd')
  });
}

function logFernandoKleinMappingSummary(products) {
  const firstDet = products?.[0];
  if (!firstDet) return;

  const document = firstDet.ownerDocument;
  const infNFe = firstElement(document, 'infNFe');
  const emit = firstElement(infNFe, 'emit');
  const emitterTaxId = value(emit, 'CNPJ') || value(emit, 'CPF');
  const productData = products.map(det => {
    const prod = firstElement(det, 'prod');
    return { cProd: value(prod, 'cProd'), xProd: value(prod, 'xProd') };
  });
  const summary = NfeProductMapping.summarizeFernandoKleinProducts({ emitterTaxId, products: productData });
  if (!summary.applies) return;

  console.info(`[NFe Agendamento] Fernando Klein: ${summary.total} itens, ${summary.mapped} mapeados, ${summary.unmapped} não mapeados.`);
  for (const product of summary.unknownProducts) {
    console.warn(`[NFe Agendamento] Produto Fernando Klein não mapeado: ${product.xProd} (cProd: ${product.cProd}).`);
  }
}

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
  const rows = products.map(det => {
    const prod = firstElement(det, 'prod');
    const tax = buildProductTaxData(det);
    const description = `${escapeHtml(value(prod, 'xProd'))}${tax.taxNote ? `<small class="tax-detail">${escapeHtml(tax.taxNote)}</small>` : ''}`;
    const itemNumber = attr(det, 'det', 'nItem');
    const productCodes = resolveProductCodes(det, prod);
    const codeDisplay = `<span class="source-product-code">${escapeHtml(productCodes.sourceCode)}</span>${productCodes.internalCode ? `<small class="internal-product-code">Int.: ${escapeHtml(productCodes.internalCode)}</small>` : ''}`;
    return `<tr>
      <td class="center item-col">${escapeHtml(itemNumber)}</td>
      <td class="code-col">${codeDisplay}</td>
      <td class="description">${description}</td>
      <td class="center">${escapeHtml(value(prod, 'NCM'))}</td>
      <td class="center">${escapeHtml(tax.cst)}</td>
      <td class="center">${escapeHtml(value(prod, 'CFOP'))}</td>
      <td class="center">${escapeHtml(value(prod, 'uCom'))}</td>
      <td class="numeric">${escapeHtml(decimal(value(prod, 'qCom'), 4, 4))}</td>
      <td class="numeric">${escapeHtml(decimal(value(prod, 'vUnCom'), 4, 4))}</td>
      <td class="numeric">${escapeHtml(moneyFiscal(value(prod, 'vProd')))}</td>
      <td class="numeric">${escapeHtml(moneyFiscal(value(prod, 'vDesc')))}</td>
      <td class="numeric">${escapeHtml(moneyFiscal(tax.vBC))}</td>
      <td class="numeric">${escapeHtml(moneyFiscal(tax.vICMS))}</td>
      <td class="numeric">${escapeHtml(moneyFiscal(tax.vIPI))}</td>
      <td class="numeric">${escapeHtml(decimal(tax.pICMS))}</td>
      <td class="numeric">${escapeHtml(decimal(tax.pIPI))}</td>
    </tr>`;
  }).join('');

  return `
    <div class="danfe-section-title">Dados dos produtos / serviços</div>
    <table class="products-table danfe-products-fill">
      <colgroup><col class="item"><col class="code"><col class="description"><col class="ncm"><col class="cst"><col class="cfop"><col class="unit"><col class="qty"><col class="unit-value"><col class="total-value"><col class="discount"><col class="bc"><col class="icms"><col class="ipi"><col class="rate"><col class="rate"></colgroup>
      <thead><tr><th>Item</th><th>Código produto</th><th>Descrição do produto / serviço</th><th>NCM/SH</th><th>O/CST</th><th>CFOP</th><th>UN</th><th>Quant.</th><th>Valor unit.</th><th>Valor total</th><th>Valor desc.</th><th>B.Cálc ICMS</th><th>Valor ICMS</th><th>Valor IPI</th><th>Alíq. ICMS</th><th>Alíq. IPI</th></tr></thead>
      <tbody>${rows || '<tr><td colspan="16">Nenhum produto informado no XML.</td></tr>'}</tbody>
    </table>`;
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

  logFernandoKleinMappingSummary(products);

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

const DANFE_ZOOM_MIN = 0.6;
const DANFE_ZOOM_MAX = 2;
const DANFE_ZOOM_STEP = 0.1;
let danfeZoom = 1;

function applyDanfeZoom(zoom = danfeZoom) {
  danfeZoom = Math.min(DANFE_ZOOM_MAX, Math.max(DANFE_ZOOM_MIN, zoom));
  document.querySelectorAll('#danfe .danfe-page').forEach(page => {
    page.style.zoom = String(danfeZoom);
  });
}

function ensureDanfeZoomHint() {
  const toolbar = document.querySelector('#danfe .danfe-toolbar');
  if (!toolbar || toolbar.querySelector('.danfe-zoom-hint')) return;

  const hint = document.createElement('span');
  hint.className = 'danfe-zoom-hint';
  hint.textContent = 'Ctrl + scroll para zoom';
  toolbar.querySelector('.danfe-toolbar-actions')?.before(hint);
}

function resetDanfeZoom() {
  danfeZoom = 1;
  applyDanfeZoom(1);
  const scroll = document.querySelector('#danfe .danfe-scroll');
  if (scroll) {
    scroll.scrollLeft = 0;
    scroll.scrollTop = 0;
  }
}

const originalOpenDanfe = openDanfe;
openDanfe = function openDanfeWithZoomReset() {
  originalOpenDanfe();
  resetDanfeZoom();
  ensureDanfeZoomHint();
};

const danfeViewer = document.getElementById('danfe');
if (danfeViewer) {
  danfeViewer.addEventListener('wheel', event => {
    if (!event.ctrlKey || danfeViewer.hidden) return;

    const scroll = event.target.closest?.('.danfe-scroll');
    if (!scroll) return;

    event.preventDefault();
    const oldZoom = danfeZoom;
    const direction = event.deltaY < 0 ? 1 : -1;
    const nextZoom = Math.min(DANFE_ZOOM_MAX, Math.max(DANFE_ZOOM_MIN, Number((oldZoom + (direction * DANFE_ZOOM_STEP)).toFixed(2))));
    if (nextZoom === oldZoom) return;

    const viewportRect = scroll.getBoundingClientRect();
    const pointerX = event.clientX - viewportRect.left;
    const pointerY = event.clientY - viewportRect.top;
    const contentX = scroll.scrollLeft + pointerX;
    const contentY = scroll.scrollTop + pointerY;
    const ratio = nextZoom / oldZoom;

    applyDanfeZoom(nextZoom);
    scroll.scrollLeft = (contentX * ratio) - pointerX;
    scroll.scrollTop = (contentY * ratio) - pointerY;
  }, { passive: false });
}

window.addEventListener('beforeprint', () => {
  document.querySelectorAll('#danfe .danfe-page').forEach(page => {
    page.dataset.screenZoom = page.style.zoom || String(danfeZoom);
    page.style.zoom = '1';
  });
});

window.addEventListener('afterprint', () => {
  document.querySelectorAll('#danfe .danfe-page').forEach(page => {
    page.style.zoom = page.dataset.screenZoom || String(danfeZoom);
    delete page.dataset.screenZoom;
  });
});
