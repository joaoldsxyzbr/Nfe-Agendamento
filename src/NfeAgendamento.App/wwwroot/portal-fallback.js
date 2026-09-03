(function () {
  let centralActive = false;
  let roleKnown = false;
  let opening = false;

  const panel = document.getElementById('portalFallbackPanel');
  const button = document.getElementById('portalFallback');
  const accessKeyInput = document.getElementById('accessKey');
  const status = document.getElementById('status');
  const lookupButton = document.getElementById('lookup');

  function hideFallback() {
    if (panel) panel.hidden = true;
  }

  function showFallback() {
    if (panel) panel.hidden = false;
  }

  async function loadRole() {
    try {
      const response = await fetch('/api/bootstrap', { cache: 'no-store' });
      if (!response.ok) return;
      const bootstrap = await response.json();
      centralActive = Boolean(bootstrap.centralActive);
      roleKnown = true;
    } catch {
    }
  }

  function installLookupFeedbackHook() {
    if (!globalThis.NfeLookupFeedback?.buildLookupErrorMessage) return;

    const original = globalThis.NfeLookupFeedback.buildLookupErrorMessage;
    globalThis.NfeLookupFeedback.buildLookupErrorMessage = function (args) {
      hideFallback();
      let message = original(args);
      const consumoIndevido = args?.statusCode === 429 && args?.error?.status === 'consumo_indevido';
      if (!consumoIndevido) return message;

      if (roleKnown && centralActive) {
        showFallback();
      } else if (roleKnown) {
        message += ' A consulta alternativa pelo Portal da NF-e deve ser feita no líder da fila.';
      }

      return message;
    };
  }

  async function openPortalFallback() {
    if (opening) return;

    const accessKey = String(accessKeyInput?.value || '').replace(/\D/g, '');
    if (accessKey.length !== 44) {
      if (status) {
        status.textContent = 'Informe uma chave de acesso com 44 dígitos.';
        status.className = 'status error';
      }
      return;
    }

    opening = true;
    if (button) {
      button.disabled = true;
      button.textContent = 'Abrindo Portal...';
    }

    try {
      const bootstrapResponse = await fetch('/api/bootstrap', { cache: 'no-store' });
      if (!bootstrapResponse.ok) throw new Error('Não foi possível confirmar o estado deste PC.');
      const bootstrap = await bootstrapResponse.json();
      centralActive = Boolean(bootstrap.centralActive);
      roleKnown = true;

      if (!centralActive) {
        hideFallback();
        throw new Error('A consulta alternativa pelo Portal da NF-e só pode ser aberta no líder da fila.');
      }

      const response = await fetch('/api/nfe/portal-fallback', {
        method: 'POST',
        cache: 'no-store',
        headers: {
          'content-type': 'application/json',
          'X-CSRF-Token': bootstrap.csrfToken
        },
        body: JSON.stringify({ accessKey })
      });

      const payload = await response.json().catch(() => ({ message: 'Não foi possível abrir o Portal da NF-e.' }));
      if (response.status !== 202) throw new Error(payload.message || 'Não foi possível abrir o Portal da NF-e.');

      if (status) {
        status.textContent = 'Portal da NF-e aberto neste PC líder. Resolva o hCaptcha e conclua o download do XML; depois consulte a mesma chave novamente.';
        status.className = 'status';
      }
    } catch (error) {
      if (status) {
        status.textContent = error?.message || 'Não foi possível abrir o Portal da NF-e.';
        status.className = 'status error';
      }
    } finally {
      opening = false;
      if (button) {
        button.disabled = false;
        button.textContent = 'Consultar pela Fazenda';
      }
    }
  }

  lookupButton?.addEventListener('click', hideFallback, true);
  accessKeyInput?.addEventListener('keydown', event => {
    if (event.key === 'Enter') hideFallback();
  }, true);
  button?.addEventListener('click', openPortalFallback);

  installLookupFeedbackHook();
  loadRole();
})();
