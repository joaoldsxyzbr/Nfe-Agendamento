(() => {
  let pairingCsrfToken = '';
  let pairingInFlight = false;
  let codeGenerationInFlight = false;
  let clientsLoadingInFlight = false;
  let revokeInFlight = false;
  let leaderActive = false;

  const byId = (id) => document.getElementById(id);

  async function readBootstrap() {
    const response = await fetch('/api/bootstrap', { cache: 'no-store' });
    if (!response.ok) throw new Error('Não foi possível consultar o estado local do aplicativo.');
    return response.json();
  }

  function renderRole(bootstrap) {
    pairingCsrfToken = bootstrap.csrfToken || pairingCsrfToken;
    leaderActive = Boolean(bootstrap.centralActive);

    const centralPanel = byId('centralConfigPanel');
    const clientPanel = byId('clientPairingPanel');
    const generateButton = byId('generatePairingCode');
    const authorizedPanel = byId('authorizedClientsPanel');
    if (centralPanel) centralPanel.hidden = false;
    if (clientPanel) clientPanel.hidden = Boolean(bootstrap.clientPaired);
    if (generateButton) generateButton.disabled = !leaderActive || codeGenerationInFlight;
    if (authorizedPanel) authorizedPanel.hidden = !leaderActive;

    const centralStatus = byId('centralPairingStatus');
    if (centralStatus) {
      centralStatus.textContent = leaderActive
        ? 'Este PC está processando a fila. Gere um código somente quando for autorizar um novo computador.'
        : bootstrap.centralOnline
          ? `A fila está sendo processada${bootstrap.centralId ? ` por ${bootstrap.centralId}` : ' por outro PC'}. O código de pareamento deve ser gerado no líder atual.`
          : 'Aguardando um PC autorizado assumir a fila para permitir novos pareamentos.';
    }

    const form = byId('clientPairingForm');
    const status = byId('clientPairingStatus');
    const lookup = byId('lookup');
    if (form) form.hidden = Boolean(bootstrap.clientPaired);
    if (lookup) lookup.disabled = false;

    if (bootstrap.clientPaired) {
      if (status) {
        status.textContent = leaderActive
          ? 'Este PC está autorizado e atualmente processa a fila.'
          : bootstrap.centralOnline
            ? `PC autorizado. Fila processada${bootstrap.centralId ? ` por ${bootstrap.centralId}` : ' por outro PC'}.`
            : 'PC autorizado. Aguardando um líder da fila ficar disponível.';
        status.className = 'status';
      }
    } else {
      if (status) {
        status.textContent = 'Este PC ainda precisa ser autorizado uma vez por um PC que esteja processando a fila.';
        status.className = 'status error';
      }
    }
  }

  function renderAuthorizedClients(clients) {
    const list = byId('authorizedClientsList');
    if (!list) return;

    list.textContent = '';
    if (!Array.isArray(clients) || clients.length === 0) {
      const empty = document.createElement('p');
      empty.className = 'muted';
      empty.textContent = 'Nenhum PC autorizado foi encontrado.';
      list.appendChild(empty);
      return;
    }

    for (const client of clients) {
      const row = document.createElement('div');
      row.className = 'authorized-client-row';

      const identity = document.createElement('div');
      identity.className = 'authorized-client-identity';

      const name = document.createElement('strong');
      name.textContent = client.clientName || 'PC sem nome';
      identity.appendChild(name);

      const meta = document.createElement('span');
      meta.className = 'muted authorized-client-meta';
      meta.textContent = client.isCurrent
        ? `Líder atual • ${client.clientId || ''}`
        : `ID ${client.clientId || ''}`;
      identity.appendChild(meta);

      const action = document.createElement('button');
      action.type = 'button';
      action.className = client.isCurrent ? 'secondary compact-button' : 'secondary compact-button danger-button';
      action.textContent = client.isCurrent ? 'Líder atual' : 'Remover';
      action.disabled = Boolean(client.isCurrent) || revokeInFlight;
      if (!client.isCurrent) {
        action.addEventListener('click', () => revokeAuthorizedClient(client.clientId, client.clientName));
      }

      row.appendChild(identity);
      row.appendChild(action);
      list.appendChild(row);
    }
  }

  async function loadAuthorizedClients() {
    if (!leaderActive || clientsLoadingInFlight) return;
    clientsLoadingInFlight = true;

    const refresh = byId('refreshAuthorizedClients');
    const status = byId('authorizedClientsStatus');
    if (refresh) refresh.disabled = true;
    if (status) status.textContent = 'Carregando PCs autorizados...';

    try {
      const response = await fetch('/api/pairing/clients', { cache: 'no-store' });
      const payload = await response.json().catch(() => ({}));
      if (!response.ok) {
        if (status) status.textContent = payload.message || 'Não foi possível carregar os PCs autorizados.';
        return;
      }

      renderAuthorizedClients(payload.clients || []);
      if (status) {
        const count = Array.isArray(payload.clients) ? payload.clients.length : 0;
        status.textContent = `${count} PC${count === 1 ? '' : 's'} autorizado${count === 1 ? '' : 's'} no grupo.`;
      }
    } catch {
      if (status) status.textContent = 'Não foi possível carregar os PCs autorizados agora.';
    } finally {
      clientsLoadingInFlight = false;
      if (refresh) refresh.disabled = !leaderActive;
    }
  }

  async function revokeAuthorizedClient(clientId, clientName) {
    if (revokeInFlight || !leaderActive || !clientId) return;

    const confirmed = typeof window.confirm !== 'function'
      || window.confirm(`Remover ${clientName || 'este PC'}? As chaves seguras do grupo serão rotacionadas e este computador perderá o acesso.`);
    if (!confirmed) return;

    revokeInFlight = true;
    const status = byId('authorizedClientsStatus');
    const refresh = byId('refreshAuthorizedClients');
    if (refresh) refresh.disabled = true;
    if (status) status.textContent = `Removendo ${clientName || 'PC'} e rotacionando as chaves do grupo...`;

    try {
      const response = await fetch('/api/pairing/revoke', {
        method: 'POST',
        cache: 'no-store',
        headers: {
          'content-type': 'application/json',
          'X-CSRF-Token': pairingCsrfToken
        },
        body: JSON.stringify({ clientId })
      });
      const payload = await response.json().catch(() => ({}));
      if (!response.ok) {
        if (status) status.textContent = payload.message || 'Não foi possível remover este PC.';
        return;
      }

      if (status) status.textContent = payload.message || 'PC removido com sucesso.';
      await loadAuthorizedClients();
    } catch {
      if (status) status.textContent = 'Não foi possível concluir a remoção agora.';
    } finally {
      revokeInFlight = false;
      if (refresh) refresh.disabled = !leaderActive;
    }
  }

  async function generatePairingCode() {
    if (codeGenerationInFlight) return;
    codeGenerationInFlight = true;

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
      codeGenerationInFlight = false;
      try {
        const bootstrap = await readBootstrap();
        renderRole(bootstrap);
        if (bootstrap.centralActive) await loadAuthorizedClients();
      } catch {
        if (button) button.disabled = false;
      }
    }
  }

  async function pairClient() {
    if (pairingInFlight) return;

    const input = byId('pairingCode');
    const button = byId('pairClient');
    const status = byId('clientPairingStatus');
    const code = input?.value?.trim() || '';

    if (!code) {
      if (status) {
        status.textContent = 'Informe o código exibido pelo PC que está processando a fila.';
        status.className = 'status error';
      }
      input?.focus();
      return;
    }

    pairingInFlight = true;
    if (button) button.disabled = true;
    if (status) {
      status.textContent = 'Autorizando este PC na fila...';
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
          status.textContent = payload.message || 'Não foi possível autorizar este PC.';
          status.className = 'status error';
        }
        return;
      }

      if (input) input.value = '';
      const bootstrap = await readBootstrap();
      renderRole(bootstrap);
      if (bootstrap.centralActive) await loadAuthorizedClients();
      if (status) {
        status.textContent = payload.message || 'PC autorizado na fila com sucesso.';
        status.className = 'status';
      }
    } catch {
      if (status) {
        status.textContent = 'Não foi possível concluir a autorização agora.';
        status.className = 'status error';
      }
    } finally {
      pairingInFlight = false;
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
      const bootstrap = await readBootstrap();
      renderRole(bootstrap);
      if (bootstrap.centralActive) await loadAuthorizedClients();
    } catch {
      const status = byId('clientPairingStatus') || byId('centralPairingStatus');
      if (status) status.textContent = 'Não foi possível carregar o estado de autorização da fila.';
    }
  }

  window.generatePairingCode = generatePairingCode;
  window.pairClient = pairClient;
  window.loadAuthorizedClients = loadAuthorizedClients;

  byId('generatePairingCode')?.addEventListener('click', generatePairingCode);
  byId('pairClient')?.addEventListener('click', pairClient);
  byId('refreshAuthorizedClients')?.addEventListener('click', loadAuthorizedClients);
  byId('pairingCode')?.addEventListener('input', formatPairingCode);
  byId('pairingCode')?.addEventListener('keydown', (event) => {
    if (event.key === 'Enter') pairClient();
  });

  bootPairing();
})();
