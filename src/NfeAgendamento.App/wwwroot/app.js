let csrfToken = '';
let currentXml = '';
let currentKey = '';

const $ = (id) => document.getElementById(id);

async function boot() {
  const response = await fetch('/api/bootstrap', { cache: 'no-store' });
  if (!response.ok) throw new Error('Não foi possível inicializar a sessão local.');
  ({ csrfToken } = await response.json());
  await refreshCertificate();
}

async function refreshCertificate() {
  const response = await fetch('/api/certificate/current', { cache: 'no-store' });
  if (response.status === 204) {
    $('certificate').textContent = 'Nenhum certificado selecionado.';
    return;
  }
  if (!response.ok) throw new Error('Não foi possível carregar o certificado.');
  const cert = await response.json();
  $('certificate').textContent = `${cert.subject} — válido até ${new Date(cert.notAfter).toLocaleDateString('pt-BR')}`;
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
$('accessKey').addEventListener('keydown', (event) => { if (event.key === 'Enter') lookup(); });
$('refreshCertificate').addEventListener('click', () => refreshCertificate().catch(e => setStatus(e.message, true)));
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
  URL.revokeObjectURL(url);
});

boot().catch(error => setStatus(error.message, true));
