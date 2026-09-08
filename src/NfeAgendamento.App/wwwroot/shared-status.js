(() => {
  const status = document.getElementById('sharedFolderStatus');
  if (!status) return;

  async function refreshSharedFolderStatus() {
    try {
      const response = await fetch('/api/bootstrap', { cache: 'no-store' });
      if (!response.ok) throw new Error();
      const bootstrap = await response.json();
      const folder = bootstrap.sharedFolder || 'pasta compartilhada';

      if (bootstrap.shareAvailable) {
        status.textContent = `Disponível — ${folder}. As consultas deste PC serão coordenadas com os demais computadores.`;
        status.className = 'status';
      } else {
        status.textContent = `Indisponível — ${folder}. Nenhuma nova consulta será enviada à SEFAZ até a pasta voltar.`;
        status.className = 'status error';
      }
    } catch {
      status.textContent = 'Não foi possível verificar a pasta compartilhada agora.';
      status.className = 'status error';
    }
  }

  void refreshSharedFolderStatus();
  window.setInterval(refreshSharedFolderStatus, 5000);
})();
