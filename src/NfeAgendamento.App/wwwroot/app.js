let csrfToken = '';
let currentXml = '';
let currentKey = '';
let lookupInProgress = false;

const $ = (id) => document.getElementById(id);

async function boot() {
  const response = await fetch('/api/bootstrap', { cache: 'no-store' });
  if (!response.ok) throw new Error('Não foi possível inicializar a sessão local.');
  const bootstrap = await response.json();
  csrfToken = bootstrap.csrfToken;
  await loadCertificates();
}

async function loadCertificates() {
  const [listResponse, currentResponse] = await Promise.all([
    fetch('/api/certificates', { cache: 'no-store' }),
    fetch('/api/certificate/current', { cache: 'no-store' })
  ]);
  if (!listResponse.ok) throw new Error('Não foi possível carregar os certificados locais.');

  const certificates = await listResponse.json();
  const select = $('certificateSelect');
  select.replaceChildren(new Option('Selecione um certificado...', ''));
  for (const cert of certificates) {
    const option = new Option(`${cert.subject} — válido até ${new Date(cert.notAfter).toLocaleDateString('pt-BR')}`, cert.thumbprint);
    select.append(option);
  }

  if (currentResponse.status === 200) {
    const current = await currentResponse.json();
    select.value = current.thumbprint;
    if (current.ufAutor) $('ufAutor').value = current.ufAutor;
    $('certificateStatus').textContent = current.ufAutor ? 'Certificado selecionado.' : 'Informe a UF autora para concluir a configuração.';
  } else {
    $('certificateStatus').textContent = certificates.length ? 'Selecione o certificado usado nas consultas.' : 'Nenhum certificado A1 válido foi encontrado.';
  }
}

async function saveCertificate() {
  const thumbprint = $('certificateSelect').value;
  if (!thumbprint) {
    setStatus('Selecione um certificado antes de salvar.', true);
    return;
  }

  const response = await fetch('/api/certificate/select', {
    method: 'POST',
    cache: 'no-store',
    headers: { 'content-type': 'application/json', 'X-CSRF-Token': csrfToken },
    body: JSON.stringify({ thumbprint, ufAutor: $('ufAutor').value })
  });
  if (!response.ok) {
    const error = await response.json().catch(() => ({ message: 'Não foi possível salvar o certificado.' }));
    setStatus(error.message, true);
    return;
  }
  $('certificateStatus').textContent = 'Certificado selecionado.';
  setStatus('Certificado salvo.');
}

async function lookup() {
  if (lookupInProgress) return;

  const accessKey = $('accessKey').value.replace(/\D/g, '');
  const lookupButton = $('lookup');
  $('accessKey').value = accessKey;
  $('actions').hidden = true;
  currentXml = '';
  currentKey = '';

  if (accessKey.length !== 44) {
    setStatus('Informe uma chave de acesso com 44 dígitos.', true);
    return;
  }

  lookupInProgress = true;
  lookupButton.disabled = true;
  lookupButton.querySelector('span').textContent = 'Consultando...';
  setStatus('Consultando a SEFAZ...');
  try {
    const response = await fetch('/api/nfe/lookup', {
      method: 'POST',
      cache: 'no-store',
      headers: { 'content-type': 'application/json', 'X-CSRF-Token': csrfToken },
      body: JSON.stringify({ accessKey })
    });

    if (response.ok) {
      currentXml = await response.text();
      currentKey = accessKey;
      setStatus('NF-e localizada com sucesso.');
      $('actions').hidden = false;
      return;
    }

    const error = await response.json().catch(() => ({ message: 'Falha na consulta.' }));
    if (response.status === 429 && error.blockedUntilUtc) {
      const until = new Date(error.blockedUntilUtc).toLocaleString('pt-BR');
      setStatus(`Consultas temporariamente bloqueadas até ${until}.`, true);
      return;
    }
    setStatus(error.message || 'Falha na consulta.', true);
  } catch {
    setStatus('Não foi possível conectar ao aplicativo local.', true);
  } finally {
    lookupInProgress = false;
    lookupButton.disabled = false;
    lookupButton.querySelector('span').textContent = 'Consultar NF-e';
  }
}

function setStatus(message, error = false) {
  $('status').textContent = message;
  $('status').className = `status${error ? ' error' : ''}`;
}

function elementsByName(root, name) {
  if (!root) return [];
  return [...root.getElementsByTagName('*')].filter(element => element.localName === name);
}

function firstElement(root, name) {
  if (!root) return null;
  if (root.localName === name) return root;
  return elementsByName(root, name)[0] || null;
}

function firstChildElement(root) {
  if (!root) return null;
  return [...root.childNodes].find(node => node.nodeType === 1) || null;
}

function value(root, name) {
  return firstElement(root, name)?.textContent?.trim() || '';
}

function attr(root, name, attribute) {
  const element = root?.localName === name ? root : firstElement(root, name);
  return element?.getAttribute(attribute) || '';
}

function escapeHtml(text) {
  return String(text ?? '').replace(/[&<>"']/g, character => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[character]);
}

function numberValue(text) {
  const number = Number.parseFloat(text);
  return Number.isFinite(number) ? number : null;
}

function decimal(text, minimumFractionDigits = 2, maximumFractionDigits = 2) {
  const number = numberValue(text);
  return number === null ? '' : number.toLocaleString('pt-BR', { minimumFractionDigits, maximumFractionDigits });
}

function money(text) {
  const number = numberValue(text);
  return number === null ? '' : number.toLocaleString('pt-BR', { style: 'currency', currency: 'BRL' });
}

function moneyFiscal(text) {
  return decimal(text, 2, 2) || '0,00';
}

function digits(text) {
  return String(text || '').replace(/\D/g, '');
}

function formatDocument(text) {
  const clean = digits(text);
  if (clean.length === 14) return clean.replace(/^(\d{2})(\d{3})(\d{3})(\d{4})(\d{2})$/, '$1.$2.$3/$4-$5');
  if (clean.length === 11) return clean.replace(/^(\d{3})(\d{3})(\d{3})(\d{2})$/, '$1.$2.$3-$4');
  return text || '';
}

function formatCep(text) {
  const clean = digits(text);
  return clean.length === 8 ? clean.replace(/^(\d{5})(\d{3})$/, '$1-$2') : text || '';
}

function formatInvoiceNumber(text) {
  const clean = digits(text).padStart(9, '0').slice(-9);
  return clean.replace(/^(\d{3})(\d{3})(\d{3})$/, '$1.$2.$3');
}

function formatSeries(text) {
  const clean = digits(text);
  return clean ? clean.padStart(3, '0') : (text || '');
}

function formatKey(text) {
  return digits(text).replace(/(.{4})/g, '$1 ').trim();
}

function dateTimeParts(text) {
  if (!text) return { date: '', time: '', full: '' };
  if (/^\d{4}-\d{2}-\d{2}$/.test(text)) {
    const [year, month, day] = text.split('-');
    const date = `${day}/${month}/${year}`;
    return { date, time: '', full: date };
  }
  const parsed = new Date(text);
  if (Number.isNaN(parsed.getTime())) return { date: text, time: '', full: text };
  const date = parsed.toLocaleDateString('pt-BR');
  const time = parsed.toLocaleTimeString('pt-BR', { hour: '2-digit', minute: '2-digit', second: '2-digit' });
  return { date, time, full: `${date} ${time}` };
}

function addressData(party, addressTag) {
  const root = firstElement(party, addressTag) || party;
  return {
    street: [value(root, 'xLgr'), value(root, 'nro')].filter(Boolean).join(', '),
    complement: value(root, 'xCpl'),
    district: value(root, 'xBairro'),
    cep: formatCep(value(root, 'CEP')),
    city: value(root, 'xMun'),
    uf: value(root, 'UF'),
    phone: value(root, 'fone')
  };
}

function paymentName(code, custom) {
  if (custom) return custom;
  const names = {
    '01': 'Dinheiro', '02': 'Cheque', '03': 'Cartão de Crédito', '04': 'Cartão de Débito',
    '05': 'Crédito Loja', '10': 'Vale Alimentação', '11': 'Vale Refeição', '12': 'Vale Presente',
    '13': 'Vale Combustível', '14': 'Duplicata Mercantil', '15': 'Boleto Bancário', '16': 'Depósito Bancário',
    '17': 'PIX', '18': 'Transferência bancária', '19': 'Programa de fidelidade', '90': 'Sem pagamento', '99': 'Outros'
  };
  return names[code] || code || 'Não informado';
}

function freightName(code) {
  const names = {
    '0': '0-Por conta do Remetente', '1': '1-Por conta do Destinatário', '2': '2-Por conta de Terceiros',
    '3': '3-Transporte Próprio por conta do Remetente', '4': '4-Transporte Próprio por conta do Destinatário',
    '9': '9-Sem Transporte'
  };
  return names[code] || code || '';
}

function taxContainer(det, name) {
  const imposto = firstElement(det, 'imposto');
  const container = firstElement(imposto, name);
  if (!container) return null;
  return [...container.childNodes].find(node => node.nodeType === 1 && node.localName.startsWith(name)) || firstChildElement(container);
}

function buildProductTaxData(det) {
  const icms = taxContainer(det, 'ICMS');
  const ipi = taxContainer(det, 'IPI');
  const pis = taxContainer(det, 'PIS');
  const cofins = taxContainer(det, 'COFINS');
  const origin = value(icms, 'orig');
  const cst = value(icms, 'CST') || value(icms, 'CSOSN');
  const pIcmsSt = value(icms, 'pICMSST');
  const vBcSt = value(icms, 'vBCST');
  const vIcmsSt = value(icms, 'vICMSST');
  const taxNote = [
    pIcmsSt ? `pIcmsSt=${decimal(pIcmsSt)}%` : '',
    vBcSt ? `BcIcmsSt=${moneyFiscal(vBcSt)}` : '',
    vIcmsSt ? `vIcmsSt=${moneyFiscal(vIcmsSt)}` : ''
  ].filter(Boolean).join(' ');

  return {
    cst: `${origin}${cst}`,
    vBC: value(icms, 'vBC'),
    vICMS: value(icms, 'vICMS'),
    pICMS: value(icms, 'pICMS'),
    vIPI: value(ipi, 'vIPI'),
    pIPI: value(ipi, 'pIPI'),
    vPIS: value(pis, 'vPIS'),
    vCOFINS: value(cofins, 'vCOFINS'),
    taxNote
  };
}

const CODE128_PATTERNS = [
  '212222','222122','222221','121223','121322','131222','122213','122312','132212','221213','221312','231212',
  '112232','122132','122231','113222','123122','123221','223211','221132','221231','213212','223112','312131',
  '311222','321122','321221','312212','322112','322211','212123','212321','232121','111323','131123','131321',
  '112313','132113','132311','211313','231113','231311','112133','112331','132131','113123','113321','133121',
  '313121','211331','231131','213113','213311','213131','311123','311321','331121','312113','312311','332111',
  '314111','221411','431111','111224','111422','121124','121421','141122','141221','112214','112412','122114',
  '122411','142112','142211','241211','221114','413111','241112','134111','111242','121142','121241','114212',
  '124112','124211','411212','421112','421211','212141','214121','412121','111143','111341','131141','114113',
  '114311','411113','411311','113141','114131','311141','411131','211412','211214','211232','2331112'
];

function barcodeSvg(text) {
  const clean = digits(text);
  if (!clean || clean.length % 2 !== 0) return '';
  const values = [];
  for (let index = 0; index < clean.length; index += 2) values.push(Number(clean.slice(index, index + 2)));
  let checksum = 105;
  values.forEach((item, index) => { checksum += item * (index + 1); });
  const encoded = [105, ...values, checksum % 103, 106];
  const modules = encoded.map(code => CODE128_PATTERNS[code]).join('');
  let x = 10;
  const bars = [];
  let black = true;
  for (const widthChar of modules) {
    const width = Number(widthChar) * 1.2;
    if (black) bars.push(`<rect x="${x.toFixed(1)}" y="0" width="${width.toFixed(1)}" height="44" fill="#000"/>`);
    x += width;
    black = !black;
  }
  x += 10;
  return `<svg viewBox="0 0 ${x.toFixed(1)} 44" role="img" aria-label="Código de barras da chave de acesso" preserveAspectRatio="none">${bars.join('')}</svg>`;
}

function fiscalCell(label, content, extraClass = '') {
  return `<div class="${extraClass}"><span class="fiscal-label">${escapeHtml(label)}</span><strong class="fiscal-value">${escapeHtml(content || '')}</strong></div>`;
}

function buildHeader({ emit, ide, document, key, page, totalPages }) {
  const emitAddress = addressData(emit, 'enderEmit');
  const issueTypeNumber = value(ide, 'tpNF') === '1' ? '1' : '0';
  return `
    <div class="danfe-block danfe-header">
      <div class="issuer-identification">
        <span class="issuer-title">Identificação do emitente</span>
        <strong>${escapeHtml(value(emit, 'xNome'))}</strong>
        <span>${escapeHtml([emitAddress.street, emitAddress.complement].filter(Boolean).join(' - '))}</span>
        <span>${escapeHtml([emitAddress.district, emitAddress.cep].filter(Boolean).join(' - '))}</span>
        <span>${escapeHtml([emitAddress.city, emitAddress.uf].filter(Boolean).join(' - '))}${emitAddress.phone ? ` · Fone/Fax: ${escapeHtml(emitAddress.phone)}` : ''}</span>
      </div>
      <div class="danfe-identity">
        <div class="danfe-word">DANFE</div>
        <div class="danfe-subtitle">Documento Auxiliar da Nota Fiscal Eletrônica</div>
        <div class="entry-exit"><span>0 - ENTRADA<br>1 - SAÍDA</span><b>${issueTypeNumber}</b></div>
        <div class="invoice-id"><strong>Nº. ${escapeHtml(formatInvoiceNumber(value(ide, 'nNF')))}</strong>Série ${escapeHtml(formatSeries(value(ide, 'serie')))}<br>Folha ${page}/${totalPages}</div>
      </div>
      <div class="barcode-area">
        <div class="barcode-wrap">${barcodeSvg(key)}</div>
        <div class="key-box"><span class="fiscal-label">Chave de acesso</span><strong class="key">${escapeHtml(formatKey(key))}</strong></div>
        <div class="auth-copy">Consulta de autenticidade no portal nacional da NF-e<br>www.nfe.fazenda.gov.br/portal ou no site da Sefaz Autorizadora</div>
      </div>
    </div>
    <div class="danfe-block operation-grid">
      ${fiscalCell('Natureza da operação', value(ide, 'natOp'))}
      ${fiscalCell('Protocolo de autorização de uso', [value(document, 'nProt'), dateTimeParts(value(document, 'dhRecbto')).full].filter(Boolean).join(' - '))}
    </div>
    <div class="danfe-block issuer-registry">
      ${fiscalCell('Inscrição estadual', value(emit, 'IE'))}
      ${fiscalCell('Inscrição municipal', value(emit, 'IM'))}
      ${fiscalCell('Inscrição estadual do subst. tribut.', value(emit, 'IEST'))}
      ${fiscalCell('CNPJ / CPF', formatDocument(value(emit, 'CNPJ') || value(emit, 'CPF')))}
    </div>`;
}

function buildReceipt({ emit, dest, ide, total }) {
  const issueDate = dateTimeParts(value(ide, 'dhEmi') || value(ide, 'dEmi')).date;
  const destinationAddress = addressData(dest, 'enderDest');
  const destination = [
    value(dest, 'xNome'),
    [destinationAddress.street, destinationAddress.district, destinationAddress.city, destinationAddress.uf].filter(Boolean).join(' ')
  ].filter(Boolean).join(' - ');
  return `
    <div class="danfe-block receipt-stub">
      <div>
        <div class="receipt-copy">Recebemos de ${escapeHtml(value(emit, 'xNome'))} os produtos e/ou serviços constantes da nota fiscal eletrônica indicada abaixo. Emissão: ${escapeHtml(issueDate)} Valor total: ${escapeHtml(money(value(total, 'vNF')))} Destinatário: ${escapeHtml(destination)}</div>
        <div class="receipt-signature"><div><span class="fiscal-label">Data de recebimento</span></div><div><span class="fiscal-label">Identificação e assinatura do recebedor</span></div></div>
      </div>
      <div class="receipt-nfe"><strong>NF-e</strong><span>Nº. ${escapeHtml(formatInvoiceNumber(value(ide, 'nNF')))}</span><span>Série ${escapeHtml(formatSeries(value(ide, 'serie')))}</span></div>
    </div>`;
}

function buildRecipient(dest, ide) {
  const address = addressData(dest, 'enderDest');
  const issue = dateTimeParts(value(ide, 'dhEmi') || value(ide, 'dEmi'));
  const departure = dateTimeParts(value(ide, 'dhSaiEnt') || value(ide, 'dSaiEnt'));
  return `
    <div class="danfe-section-title">Destinatário / Remetente</div>
    <div class="danfe-block recipient-grid">
      ${fiscalCell('Nome / Razão social', value(dest, 'xNome'))}
      ${fiscalCell('CNPJ / CPF', formatDocument(value(dest, 'CNPJ') || value(dest, 'CPF')))}
      ${fiscalCell('Data da emissão', issue.date)}
      ${fiscalCell('Endereço', [address.street, address.complement].filter(Boolean).join(' - '))}
      ${fiscalCell('Bairro / Distrito', address.district)}
      ${fiscalCell('CEP', address.cep)}
      ${fiscalCell('Município', address.city)}
      ${fiscalCell('UF / Fone / Fax', [address.uf, address.phone].filter(Boolean).join(' · '))}
      ${fiscalCell('Data/Hora da saída/entrada', [departure.date, departure.time].filter(Boolean).join(' '))}
    </div>
    <div class="danfe-block recipient-grid" style="grid-template-columns: 1fr 1fr 1fr;">
      ${fiscalCell('Inscrição estadual', value(dest, 'IE'))}
      ${fiscalCell('Indicador IE', value(dest, 'indIEDest'))}
      ${fiscalCell('E-mail', value(dest, 'email'))}
    </div>`;
}

function buildPayments(infNFe) {
  const cobr = firstElement(infNFe, 'cobr');
  const fat = firstElement(cobr, 'fat');
  const duplicates = elementsByName(cobr, 'dup');
  const payments = elementsByName(firstElement(infNFe, 'pag'), 'detPag');
  const blocks = [];

  if (fat || duplicates.length) {
    const items = [];
    if (fat) items.push(`<div class="payment-item"><span class="fiscal-label">Fatura</span><strong class="fiscal-value">Nº ${escapeHtml(value(fat, 'nFat'))} · Original ${escapeHtml(money(value(fat, 'vOrig')))} · Líquido ${escapeHtml(money(value(fat, 'vLiq')))}</strong></div>`);
    for (const duplicate of duplicates) {
      items.push(`<div class="payment-item"><span class="fiscal-label">Duplicata ${escapeHtml(value(duplicate, 'nDup'))}</span><strong class="fiscal-value">Venc. ${escapeHtml(dateTimeParts(value(duplicate, 'dVenc')).date)} · ${escapeHtml(money(value(duplicate, 'vDup')))}</strong></div>`);
    }
    blocks.push(`<div class="danfe-section-title">FATURA / DUPLICATA</div><div class="danfe-block payment-wrap">${items.join('')}</div>`);
  }

  if (payments.length) {
    const items = payments.map(payment => `<div class="payment-item"><span class="fiscal-label">Forma de pagamento</span><strong class="fiscal-value">${escapeHtml(paymentName(value(payment, 'tPag'), value(payment, 'xPag')))} · ${escapeHtml(money(value(payment, 'vPag')))}</strong></div>`).join('');
    blocks.push(`<div class="danfe-section-title">PAGAMENTO</div><div class="danfe-block payment-wrap">${items}</div>`);
  }

  return blocks.join('');
}

function buildTotals(total) {
  const fields = [
    ['Base de cálc. do ICMS', 'vBC'], ['Valor do ICMS', 'vICMS'], ['BASE DE CÁLC. ICMS S.T.', 'vBCST'], ['VALOR DO ICMS SUBST.', 'vST'],
    ['V. Imp. importação', 'vII'], ['V. ICMS UF remet.', 'vICMSUFRemet'], ['V. FCP UF dest.', 'vFCPUFDest'], ['VALOR DO PIS', 'vPIS'],
    ['V. total produtos', 'vProd'], ['Valor do frete', 'vFrete'], ['Valor do seguro', 'vSeg'], ['Desconto', 'vDesc'],
    ['Outras despesas', 'vOutro'], ['Valor total IPI', 'vIPI'], ['VALOR DA COFINS', 'vCOFINS'], ['V. total da nota', 'vNF']
  ];
  return `
    <div class="danfe-section-title">Cálculo do imposto</div>
    <div class="danfe-block total-grid">${fields.map(([label, tag], index) => fiscalCell(label, moneyFiscal(value(total, tag)), index === fields.length - 1 ? 'invoice-total' : '')).join('')}</div>`;
}

function buildTransport(infNFe) {
  const transp = firstElement(infNFe, 'transp');
  const transporta = firstElement(transp, 'transporta');
  const vehicle = firstElement(transp, 'veicTransp');
  const volume = firstElement(transp, 'vol');
  const address = [value(transporta, 'xEnder')].filter(Boolean).join('');
  return `
    <div class="danfe-section-title">Transportador / Volumes transportados</div>
    <div class="danfe-block transport-grid">
      ${fiscalCell('Nome / Razão social', value(transporta, 'xNome'))}
      ${fiscalCell('Frete', freightName(value(transp, 'modFrete')))}
      ${fiscalCell('Código ANTT', value(vehicle, 'RNTC'))}
      ${fiscalCell('Placa do veículo', value(vehicle, 'placa'))}
      ${fiscalCell('UF', value(vehicle, 'UF'))}
      ${fiscalCell('CNPJ / CPF', formatDocument(value(transporta, 'CNPJ') || value(transporta, 'CPF')))}
      ${fiscalCell('Endereço', address)}
      ${fiscalCell('Município', value(transporta, 'xMun'))}
      ${fiscalCell('UF', value(transporta, 'UF'))}
      ${fiscalCell('Inscrição estadual', value(transporta, 'IE'))}
      ${fiscalCell('Quantidade / Espécie', [value(volume, 'qVol'), value(volume, 'esp')].filter(Boolean).join(' · '))}
      ${fiscalCell('Marca / Numeração', [value(volume, 'marca'), value(volume, 'nVol')].filter(Boolean).join(' · '))}
      ${fiscalCell('Peso bruto', decimal(value(volume, 'pesoB'), 3, 3))}
      ${fiscalCell('Peso líquido', decimal(value(volume, 'pesoL'), 3, 3))}
      ${fiscalCell('Lacres', elementsByName(volume, 'lacres').map(item => value(item, 'nLacre')).filter(Boolean).join(', '))}
      ${fiscalCell('Volumes', elementsByName(transp, 'vol').map(item => value(item, 'qVol')).filter(Boolean).join(', '))}
      ${fiscalCell('Observação transporte', '')}
      ${fiscalCell('Identificação', '')}
    </div>`;
}

function buildProductsTable(products) {
  const rows = products.map(det => {
    const prod = firstElement(det, 'prod');
    const tax = buildProductTaxData(det);
    const description = `${escapeHtml(value(prod, 'xProd'))}${tax.taxNote ? `<small class="tax-detail">${escapeHtml(tax.taxNote)}</small>` : ''}`;
    return `<tr>
      <td>${escapeHtml(value(prod, 'cProd'))}</td>
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
    <table class="products-table">
      <colgroup><col class="code"><col class="description"><col class="ncm"><col class="cst"><col class="cfop"><col class="unit"><col class="qty"><col class="unit-value"><col class="total-value"><col class="discount"><col class="bc"><col class="icms"><col class="ipi"><col class="rate"><col class="rate"></colgroup>
      <thead><tr><th>Código produto</th><th>Descrição do produto / serviço</th><th>NCM/SH</th><th>O/CST</th><th>CFOP</th><th>UN</th><th>Quant.</th><th>Valor unit.</th><th>Valor total</th><th>Valor desc.</th><th>B.Cálc ICMS</th><th>Valor ICMS</th><th>Valor IPI</th><th>Alíq. ICMS</th><th>Alíq. IPI</th></tr></thead>
      <tbody>${rows || '<tr><td colspan="15">Nenhum produto informado no XML.</td></tr>'}</tbody>
    </table>`;
}

function buildAdditional(infNFe) {
  const contributor = value(infNFe, 'infCpl');
  const taxAuthority = value(infNFe, 'infAdFisco');
  return `
    <div class="danfe-section-title">Dados adicionais</div>
    <div class="danfe-block additional-grid">
      <div><span class="fiscal-label">Informações complementares</span><p>${escapeHtml(contributor ? `Inf. Contribuinte: ${contributor}` : '')}${taxAuthority ? `\nInf. fisco: ${escapeHtml(taxAuthority)}` : ''}</p></div>
      <div><span class="fiscal-label">RESERVADO AO FISCO</span></div>
    </div>`;
}

function paginateProducts(products, additionalText) {
  let firstCapacity = 14;
  if (additionalText.length > 1400) firstCapacity = 2;
  else if (additionalText.length > 900) firstCapacity = 5;
  else if (additionalText.length > 600) firstCapacity = 8;
  if (products.length <= firstCapacity) return [products];
  const pages = [products.slice(0, firstCapacity)];
  for (let index = firstCapacity; index < products.length; index += 18) pages.push(products.slice(index, index + 18));
  return pages;
}

function closeDanfe() {
  $('danfe').hidden = true;
  document.body.classList.remove('danfe-open');
}

function openDanfe() {
  $('danfe').hidden = false;
  document.body.classList.add('danfe-open');
  $('closeDanfe')?.focus();
}

function renderDanfe() {
  const document = new DOMParser().parseFromString(currentXml, 'application/xml');
  if (document.querySelector('parsererror')) throw new Error('O XML da NF-e não pôde ser lido.');

  const infNFe = firstElement(document, 'infNFe');
  if (!infNFe) throw new Error('O XML não contém uma NF-e reconhecida.');
  const emit = firstElement(infNFe, 'emit');
  const dest = firstElement(infNFe, 'dest');
  const ide = firstElement(infNFe, 'ide');
  const total = firstElement(infNFe, 'ICMSTot');
  const products = elementsByName(infNFe, 'det');
  const key = (attr(infNFe, 'infNFe', 'Id') || '').replace(/^NFe/, '') || currentKey;
  const additionalText = `${value(infNFe, 'infCpl')} ${value(infNFe, 'infAdFisco')}`;
  const productPages = paginateProducts(products, additionalText);
  const totalPages = productPages.length;

  const pages = productPages.map((pageProducts, index) => {
    const page = index + 1;
    const isFirst = index === 0;
    return `<section class="danfe-page">
      ${isFirst ? buildReceipt({ emit, dest, ide, total }) : ''}
      ${buildHeader({ emit, ide, document, key, page, totalPages })}
      ${isFirst ? buildRecipient(dest, ide) : '<div class="continuation-note">Continuação dos dados dos produtos / serviços</div>'}
      ${isFirst ? buildPayments(infNFe) : ''}
      ${isFirst ? buildTotals(total) : ''}
      ${isFirst ? buildTransport(infNFe) : ''}
      ${buildProductsTable(pageProducts)}
      ${isFirst ? buildAdditional(infNFe) : ''}
      <div class="danfe-footer"><span>Documento gerado localmente pelo NFe Agendamento</span><span>Chave: ${escapeHtml(formatKey(key))}</span></div>
    </section>`;
  }).join('');

  $('danfe').innerHTML = `
    <div class="danfe-modal" role="document">
      <div class="danfe-toolbar">
        <div class="danfe-toolbar-title">NF-e ${escapeHtml(formatInvoiceNumber(value(ide, 'nNF')))} · ${escapeHtml(value(emit, 'xNome'))}</div>
        <div class="danfe-toolbar-actions"><button type="button" id="closeDanfe">Fechar</button><button type="button" id="printDanfeTop">Imprimir / Salvar PDF</button></div>
      </div>
      <div class="danfe-scroll"><div class="danfe-pages">${pages}</div></div>
    </div>`;

  openDanfe();
  $('closeDanfe').addEventListener('click', closeDanfe);
  $('printDanfeTop').addEventListener('click', () => window.print());
}

$('danfe').addEventListener('click', (event) => {
  if (event.target === $('danfe')) closeDanfe();
});

document.addEventListener('keydown', (event) => {
  if (event.key === 'Escape' && !$('danfe').hidden) closeDanfe();
});

$('lookup').addEventListener('click', lookup);
$('saveCertificate').addEventListener('click', () => saveCertificate().catch(error => setStatus(error.message, true)));
$('accessKey').addEventListener('keydown', (event) => { if (event.key === 'Enter') lookup(); });
$('viewDanfe').addEventListener('click', () => { try { renderDanfe(); } catch (error) { setStatus(error.message, true); } });
$('printDanfe').addEventListener('click', () => { try { renderDanfe(); setTimeout(() => window.print(), 50); } catch (error) { setStatus(error.message, true); } });
$('download').addEventListener('click', () => {
  const blob = new Blob([currentXml], { type: 'application/xml' });
  const url = URL.createObjectURL(blob);
  const anchor = document.createElement('a');
  anchor.href = url;
  anchor.download = `${currentKey}.xml`;
  anchor.click();
  setTimeout(() => URL.revokeObjectURL(url), 1000);
});

boot().catch(error => setStatus(error.message, true));
