(() => {
  let pairingCsrfToken = '';

  const byId = (id) => document.getElementById(id);

  async function readBootstrap() {
    const response = await fetch('/api/bootstrap', { cache: 'no-store' });
    if (!response.ok) throw new Error('Não foi possível consultar o estado local do aplicativo.');
    return response.json();
  }

  function renderRole(bootstrap) {
    pairingCsrfToken = bootstrap.csrfToken || pairingCsrfToken;

    const centralPanel = byId('centralConfigPanel');
    const clientPanel = byId('clientPairingPanel');
    if (centralPanel) centralPanel.hidden = !bootstrap.configuredAsCentral;
    if (clientPanel) clientPanel.hidden = Boolean(bootstrap.configuredAsCentral);

    if (bootstrap.configuredAsCentral) {
      const centralStatus = byId('centralPairingStatus');
      if (centralStatus) {
        centralStatus.textContent = bootstrap.centralActive
          ? 'Central ativa. Gere um código somente quando for conectar um novo PC.'
          : 'Ative a Central antes de gerar um código de pareamento.';
      }
      return;
    }

    const form = byId('clientPairingForm');
    const status = byId('clientPairingStatus');
    const lookup = byId('lookup');
    if (form) form.hidden = Boolean(bootstrap.clientPaired);

    if (bootstrap.clientPaired) {
      if (status) {
        status.textContent = bootstrap.centralOnline
          ? `PC pareado com a Central${bootstrap.centralId ? ` ${bootstrap.centralId}` : ''}.`
          : 'PC pareado. A Central está offline ou indisponível neste momento.';
        status.className = 'status';
      }
      if (lookup) lookup.disabled = false;
    } else {
      if (status) {
        status.textContent = 'Este PC ainda precisa ser pareado uma vez com a Central.';
        status.className = 'status error';
      }
      if (lookup) lookup.disabled = true;
    }
  }

  async function generatePairingCode() {
    const button = byId('generatePairingCode');
    const resultBox = byId('centralPairingResult');
    const codeValue = byId('centralPairingCode');
    const expiry = byId('centralPairingExpiry');
    const status = byId('centralPairingStatus');

    if (button) button.disabled = true;
    try {
      const response = await fetch('/api/pairing/code', {
        method: 'POST',
        cache: 'no-store',
        headers: { 'X-CSRF-Token': pairingCsrfToken }
      });
      const payload = await response.json().catch(() => ({}));
      if (!response.ok) {
        if (resultBox) resultBox.hidden = true;
        if (status) status.textContent = payload.message || 'Não foi possível gerar o código de pareamento.';
        return;
      }

      if (codeValue) codeValue.textContent = payload.code || '';
      if (expiry) {
        const expiresAt = new Date(payload.expiresUtc);
        expiry.textContent = Number.isNaN(expiresAt.getTime())
          ? 'Código temporário.'
          : `Válido até ${expiresAt.toLocaleTimeString('pt-BR', { hour: '2-digit', minute: '2-digit' })}.`;
      }
      if (resultBox) resultBox.hidden = false;
      if (status) status.textContent = 'Digite este código somente no PC que você quer autorizar.';
    } catch {
      if (resultBox) resultBox.hidden = true;
      if (status) status.textContent = 'Não foi possível gerar o código agora.';
    } finally {
      if (button) button.disabled = false;
    }
  }

  async function pairClient() {
    const input = byId('pairingCode');
    const button = byId('pairClient');
    const status = byId('clientPairingStatus');
    const code = input?.value?.trim() || '';

    if (!code) {
      if (status) {
        status.textContent = 'Informe o código exibido no PC Central.';
        status.className = 'status error';
      }
      input?.focus();
      return;
    }

    if (button) button.disabled = true;
    if (status) {
      status.textContent = 'Conectando este PC à Central...';
      status.className = 'status';
    }

    try {
      const response = await fetch('/api/pairing/client', {
        method: 'POST',
        cache: 'no-store',
        headers: {
          'content-type': 'application/json',
          'X-CSRF-Token': pairingCsrfToken
        },
        body: JSON.stringify({ code })
      });
      const payload = await response.json().catch(() => ({}));
      if (!response.ok) {
        if (status) {
          status.textContent = payload.message || 'Não foi possível parear este PC.';
          status.className = 'status error';
        }
        return;
      }

      if (input) input.value = '';
      const bootstrap = await readBootstrap();
      renderRole(bootstrap);
      if (status) {
        status.textContent = payload.message || 'PC pareado com a Central com sucesso.';
        status.className = 'status';
      }
    } catch {
      if (status) {
        status.textContent = 'Não foi possível concluir o pareamento agora.';
        status.className = 'status error';
      }
    } finally {
      if (button) button.disabled = false;
    }
  }

  function formatPairingCode(event) {
    const input = event.currentTarget;
    const compact = input.value.replace(/[^0-9a-f]/gi, '').slice(0, 24).toUpperCase();
    input.value = compact.match(/.{1,4}/g)?.join('-') || compact;
  }

  async function bootPairing() {
    try {
      renderRole(await readBootstrap());
    } catch {
      const status = byId('clientPairingStatus') || byId('centralPairingStatus');
      if (status) status.textContent = 'Não foi possível carregar o estado de pareamento.';
    }
  }

  window.generatePairingCode = generatePairingCode;
  window.pairClient = pairClient;

  byId('generatePairingCode')?.addEventListener('click', generatePairingCode);
  byId('pairClient')?.addEventListener('click', pairClient);
  byId('pairingCode')?.addEventListener('input', formatPairingCode);
  byId('pairingCode')?.addEventListener('keydown', (event) => {
    if (event.key === 'Enter') pairClient();
  });

  bootPairing();
})();
