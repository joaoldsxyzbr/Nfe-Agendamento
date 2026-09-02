(() => {
  const tabs = Array.from(document.querySelectorAll('.tab-button[data-tab]'));
  const panels = new Map([
    ['lookup', document.getElementById('tabPanelLookup')],
    ['batch', document.getElementById('tabPanelBatch')],
    ['config', document.getElementById('tabPanelConfig')]
  ]);

  if (tabs.length !== 3 || Array.from(panels.values()).some(panel => !panel)) return;

  function activateTab(name, focus = false) {
    const selected = tabs.find(tab => tab.dataset.tab === name) || tabs[0];

    for (const tab of tabs) {
      const active = tab === selected;
      tab.setAttribute('aria-selected', active ? 'true' : 'false');
      tab.tabIndex = active ? 0 : -1;
      const panel = panels.get(tab.dataset.tab);
      if (panel) panel.hidden = !active;
    }

    if (focus) selected.focus();
  }

  tabs.forEach((tab, index) => {
    tab.addEventListener('click', () => activateTab(tab.dataset.tab));
    tab.addEventListener('keydown', event => {
      if (!['ArrowLeft', 'ArrowRight', 'Home', 'End'].includes(event.key)) return;
      event.preventDefault();

      let nextIndex = index;
      if (event.key === 'ArrowRight') nextIndex = (index + 1) % tabs.length;
      if (event.key === 'ArrowLeft') nextIndex = (index - 1 + tabs.length) % tabs.length;
      if (event.key === 'Home') nextIndex = 0;
      if (event.key === 'End') nextIndex = tabs.length - 1;

      activateTab(tabs[nextIndex].dataset.tab, true);
    });
  });

  activateTab('lookup');
})();
