(function () {
  let portalFallbackAvailable = false;
  let availabilityKnown = false;
  let opening = false;
  let watchingAccessKey = '';

  const panel = document.getElementById('portalFallbackPanel');
  const button = document.getElementById('portalFallback');
  const accessKeyInput = document.getElementById('accessKey');
  const status = document.getElementById('status');
  const lookupButton = document.getElementById('lookup');
  const portalPollDelayMs = 2000;
  const portalPollAttempts = 300;

  function hideFallback() {
    if (panel) panel.hidden = true;
  }

  function showFallback() {
    if (panel) panel.hidden = false;
  }

  function sleep(milliseconds) {
    return new Promise(resolve => setTimeout(resolve, milliseconds));
  }

  async function loadAvailability() {
    try {
      const response = await fetch('/api/bootstrap', { cache: 'no-store' });
      if (!response.ok) return;
      const bootstrap = await response.json();
      portalFallbackAvailable = Boolean(bootstrap.portalFallbackAvailable);
      availabilityKnown = true;
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

      if (availabilityKnown && portalFallbackAvailable) {
        showFallback();
      } else if (availabilityKnown) {
        message += ' Autorize este PC no NFe Agendamento e configure o certificado A1 localmente para usar o Portal da NF-e.';
      }

      return message;
    };
  }

  async function watchPortalImport(accessKey) {
    if (watchingAccessKey === accessKey) return;
    watchingAccessKey = accessKey;

    try {
      for (let attempt = 0; attempt < portalPollAttempts; attempt++) {
        await sleep(portalPollDelayMs);
        if (watchingAccessKey !== accessKey) return;

        const currentKey = String(accessKeyInput?.value || '').replace(/\D/g, '');
        if (currentKey !== accessKey) return;

        let response;
        try {
          response = await fetch(`/api/nfe/cache/${encodeURIComponent(accessKey)}`, { cache: 'no-store' });
        } catch {
          continue;
        }

        if (response.status === 404 || response.status === 204) continue;
        if (!response.ok) return;

        hideFallback();
        if (status) {
          status.textContent = 'XML recebido do Portal. Carregando a NF-e...';
          status.className = 'status';
        }

        if (typeof globalThis.lookup === 'function') {
          await globalThis.lookup();
        } else if (status) {
          status.textContent = 'XML recebido do Portal. Clique em Consultar NF-e para abrir o documento.';
        }
        return;
      }
    } finally {
      if (watchingAccessKey === accessKey) watchingAccessKey = '';
    }
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
      portalFallbackAvailable = Boolean(bootstrap.portalFallbackAvailable);
      availabilityKnown = true;

      if (!portalFallbackAvailable) {
        hideFallback();
        throw new Error('Este PC ainda não está autorizado para usar o Portal da NF-e. Autorize-o no grupo e configure o certificado A1 localmente.');
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
        status.textContent = 'Portal da NF-e aberto neste PC. Resolva o hCaptcha e baixe o XML; esta tela será atualizada automaticamente.';
        status.className = 'status';
      }
      void watchPortalImport(accessKey);
    } catch (error) {
      if (status) {
        status.textContent = error?.message || 'Não foi possível abrir o Portal da NF-e.';
        status.className = 'status error';
      }
    } finally {
      opening = false;
      if (button) {
        button.disabled = false;
        button.textContent = 'Baixar pelo Portal';
      }
    }
  }

  lookupButton?.addEventListener('click', hideFallback, true);
  accessKeyInput?.addEventListener('keydown', event => {
    if (event.key === 'Enter') hideFallback();
  }, true);
  button?.addEventListener('click', openPortalFallback);

  installLookupFeedbackHook();
  loadAvailability();
})();
