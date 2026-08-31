let csrfToken = '';
let currentXml = '';
let currentKey = '';

const $ = (id) => document.getElementById(id);

async function boot() {
  const response = await fetch('/api/bootstrap', { cache: 'no-store' });
  if (!response.ok) throw new Error('Não foi possível inicializar a sessão local.');
  ({ csrfToken } = await response.json());
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
  const accessKey = $('accessKey').value.replace(/\D/g, '');
  $('accessKey').value = accessKey;
  $('actions').hidden = true;
  currentXml = '';
  currentKey = '';

  if (accessKey.length !== 44) {
    setStatus('Informe uma chave de acesso com 44 dígitos.', true);
    return;
  }

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
  }
}

function setStatus(message, error = false) {
  $('status').textContent = message;
  $('status').className = `status${error ? ' error' : ''}`;
}

function elementsByName(root, name) {
  return [...root.getElementsByTagName('*')].filter(element => element.localName === name);
}

function firstElement(root, name) {
  return elementsByName(root, name)[0] || null;
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

function money(text) {
  const number = Number.parseFloat(text);
  return Number.isFinite(number) ? number.toLocaleString('pt-BR', { style: 'currency', currency: 'BRL' }) : '';
}

function address(root) {
  const street = [value(root, 'xLgr'), value(root, 'nro')].filter(Boolean).join(', ');
  const city = [value(root, 'xMun'), value(root, 'UF')].filter(Boolean).join(' - ');
  return [street, value(root, 'xBairro'), city, value(root, 'CEP')].filter(Boolean).join(' · ');
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
  const emit = firstElement(infNFe, 'emit');
  const dest = firstElement(infNFe, 'dest');
  const ide = firstElement(infNFe, 'ide');
  const total = firstElement(infNFe, 'ICMSTot');
  const transp = firstElement(infNFe, 'transp');
  const products = elementsByName(infNFe, 'det');
  const rows = products.map(det => {
    const prod = firstElement(det, 'prod');
    return `<tr><td>${escapeHtml(attr(det, 'det', 'nItem'))}</td><td>${escapeHtml(value(prod, 'cProd'))}</td><td>${escapeHtml(value(prod, 'xProd'))}</td><td>${escapeHtml(value(prod, 'NCM'))}</td><td>${escapeHtml(value(prod, 'CFOP'))}</td><td>${escapeHtml(value(prod, 'uCom'))}</td><td class="number">${escapeHtml(value(prod, 'qCom'))}</td><td class="number">${money(value(prod, 'vUnCom'))}</td><td class="number">${money(value(prod, 'vProd'))}</td></tr>`;
  }).join('');

  const key = (attr(infNFe, 'infNFe', 'Id') || '').replace(/^NFe/, '') || currentKey;
  $('danfe').innerHTML = `
    <div class="danfe-modal" role="document">
      <div class="danfe-toolbar"><button type="button" id="closeDanfe">Fechar</button><button type="button" id="printDanfeTop">Imprimir / Salvar PDF</button></div>
      <div class="danfe-scroll">
        <div class="danfe-page">
          <div class="danfe-top">
            <div class="issuer"><strong>${escapeHtml(value(emit, 'xNome'))}</strong><span>${escapeHtml(address(emit))}</span><span>CNPJ: ${escapeHtml(value(emit, 'CNPJ'))}</span></div>
            <div class="danfe-title"><strong>DANFE</strong><span>Documento Auxiliar da Nota Fiscal Eletrônica</span><b>NF-e</b></div>
            <div class="invoice-number"><span>Nº</span><strong>${escapeHtml(value(ide, 'nNF'))}</strong><span>Série ${escapeHtml(value(ide, 'serie'))}</span><small>Folha 1/1</small></div>
          </div>
          <div class="section key-section"><span class="section-title">Chave de acesso</span><strong class="access-key">${escapeHtml(key.replace(/(.{4})/g, '$1 '))}</strong><small>Consulta de autenticidade no portal nacional da NF-e</small></div>
          <div class="grid grid-4"><div><span>Natureza da operação</span><strong>${escapeHtml(value(ide, 'natOp'))}</strong></div><div><span>Data de emissão</span><strong>${escapeHtml(value(ide, 'dhEmi') || value(ide, 'dEmi'))}</strong></div><div><span>Tipo</span><strong>${value(ide, 'tpNF') === '1' ? 'Saída' : 'Entrada'}</strong></div><div><span>Protocolo</span><strong>${escapeHtml(value(document, 'nProt'))}</strong></div></div>
          <div class="section"><span class="section-title">Emitente</span><div class="party"><strong>${escapeHtml(value(emit, 'xNome'))}</strong><span>CNPJ: ${escapeHtml(value(emit, 'CNPJ'))} · IE: ${escapeHtml(value(emit, 'IE'))}</span><span>${escapeHtml(address(emit))}</span></div></div>
          <div class="section"><span class="section-title">Destinatário / Remetente</span><div class="party"><strong>${escapeHtml(value(dest, 'xNome'))}</strong><span>CNPJ/CPF: ${escapeHtml(value(dest, 'CNPJ') || value(dest, 'CPF'))} · IE: ${escapeHtml(value(dest, 'IE'))}</span><span>${escapeHtml(address(dest))}</span></div></div>
          <div class="section products"><span class="section-title">Produtos e serviços</span><table><thead><tr><th>Item</th><th>Código</th><th>Descrição</th><th>NCM</th><th>CFOP</th><th>UN</th><th>Qtd.</th><th>Vlr. unit.</th><th>Vlr. total</th></tr></thead><tbody>${rows || '<tr><td colspan="9">Nenhum produto informado no XML.</td></tr>'}</tbody></table></div>
          <div class="section totals"><span class="section-title">Cálculo do imposto</span><div class="grid grid-5"><div><span>Base ICMS</span><strong>${money(value(total, 'vBC'))}</strong></div><div><span>ICMS</span><strong>${money(value(total, 'vICMS'))}</strong></div><div><span>IPI</span><strong>${money(value(total, 'vIPI'))}</strong></div><div><span>Frete</span><strong>${money(value(total, 'vFrete'))}</strong></div><div class="grand-total"><span>Valor total da NF-e</span><strong>${money(value(total, 'vNF'))}</strong></div></div></div>
          <div class="section"><span class="section-title">Transportador / Volumes transportados</span><div class="grid grid-3"><div><span>Transportador</span><strong>${escapeHtml(value(transp, 'xNome'))}</strong></div><div><span>CNPJ</span><strong>${escapeHtml(value(transp, 'CNPJ'))}</strong></div><div><span>Volumes</span><strong>${escapeHtml(value(transp, 'qVol'))}</strong></div></div></div>
          <div class="section additional"><span class="section-title">Dados adicionais</span><p>${escapeHtml(value(infNFe, 'infCpl'))}</p></div>
        </div>
      </div>
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
$('view').addEventListener('click', () => {
  const blob = new Blob([currentXml], { type: 'application/xml' });
  window.open(URL.createObjectURL(blob), '_blank', 'noopener');
});
$('download').addEventListener('click', () => {
  const blob = new Blob([currentXml], { type: 'application/xml' });
  const url = URL.createObjectURL(blob);
  const anchor = document.createElement('a');
  anchor.href = url;
  anchor.download = `${currentKey}.xml`;
  anchor.click();
  setTimeout(() => URL.revokeObjectURL(url), 1000);
});

$('batchLookup').addEventListener('click', async () => {
  const accessKeys = $('batchKeys').value.split(/\s+/).map(key => key.replace(/\D/g, '')).filter(Boolean);
  $('batchStatus').textContent = 'Consultando lote...';
  try {
    const response = await fetch('/api/nfe/batch', {
      method: 'POST',
      cache: 'no-store',
      headers: { 'content-type': 'application/json', 'X-CSRF-Token': csrfToken },
      body: JSON.stringify({ accessKeys })
    });
    if (!response.ok) {
      const error = await response.json().catch(() => ({ message: 'Falha no lote.' }));
      $('batchStatus').textContent = error.message || 'Falha no lote.';
      return;
    }
    const blob = await response.blob();
    const url = URL.createObjectURL(blob);
    const anchor = document.createElement('a');
    anchor.href = url;
    anchor.download = 'nfe-agendamento.zip';
    anchor.click();
    setTimeout(() => URL.revokeObjectURL(url), 1000);
    $('batchStatus').textContent = 'Lote concluído. O ZIP foi baixado.';
  } catch {
    $('batchStatus').textContent = 'Não foi possível conectar ao aplicativo local.';
  }
});

boot().catch(error => setStatus(error.message, true));
