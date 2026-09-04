function resetLookup() {
  const accessKey = document.getElementById('accessKey');
  const status = document.getElementById('status');
  const actions = document.getElementById('actions');
  const portalFallbackPanel = document.getElementById('portalFallbackPanel');

  if (!accessKey || !status || !actions || !portalFallbackPanel) return;

  accessKey.value = '';
  status.textContent = '';
  status.className = 'status';
  actions.hidden = true;
  portalFallbackPanel.hidden = true;
  accessKey.focus();
}

document.getElementById('newLookup')?.addEventListener('click', resetLookup);
