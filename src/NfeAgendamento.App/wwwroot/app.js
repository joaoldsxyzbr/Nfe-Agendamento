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
    $('certificateStatus').textContent = 'Certificado selecionado.';
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
    body: JSON.stringify({ thumbprint })
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

$('lookup').addEventListener('click', lookup);
$('saveCertificate').addEventListener('click', () => saveCertificate().catch(error => setStatus(error.message, true)));
$('accessKey').addEventListener('keydown', (event) => { if (event.key === 'Enter') lookup(); });
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

boot().catch(error => setStatus(error.message, true));
