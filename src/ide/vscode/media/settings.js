// @ts-check
// Webview script: renders the rules and sends edits back to the extension host.
(function () {
  // @ts-ignore acquireVsCodeApi is injected by VS Code.
  const vscode = acquireVsCodeApi();
  const severities = ['none', 'silent', 'suggestion', 'warning', 'error'];
  const rulesContainer = /** @type {HTMLElement} */ (document.getElementById('rules'));

  /** @param {string} tag @param {Record<string, string>} [attributes] @param {string} [text] */
  function element(tag, attributes = {}, text) {
    const node = document.createElement(tag);
    for (const [name, value] of Object.entries(attributes)) {
      node.setAttribute(name, value);
    }
    if (text !== undefined) {
      node.textContent = text;
    }
    return node;
  }

  function render(state) {
    const focusedId = document.activeElement?.id;
    rulesContainer.replaceChildren();

    for (const rule of state.rules.filter((r) => r.type === 'realtime')) {
      const severity = state.values[`dotnet_diagnostic.${rule.id}.severity`] ?? rule.defaultSeverity;
      const card = element('article', { class: 'rule', 'aria-labelledby': `title-${rule.id}` });
      card.append(element('h2', { id: `title-${rule.id}` }, `${rule.id} — ${rule.title}`));
      card.append(element('p', { class: 'description' }, rule.description));

      const enabled = /** @type {HTMLInputElement} */ (element('input', { type: 'checkbox', id: `enabled-${rule.id}` }));
      enabled.checked = severity !== 'none';
      enabled.addEventListener('change', () => {
        const fallback = rule.defaultSeverity === 'none' ? 'suggestion' : rule.defaultSeverity;
        vscode.postMessage({ type: 'setSeverity', ruleId: rule.id, severity: enabled.checked ? fallback : 'none' });
      });
      const enabledRow = element('div', { class: 'row' });
      enabledRow.append(enabled, element('label', { for: enabled.id }, 'Enabled'));
      card.append(enabledRow);

      const select = /** @type {HTMLSelectElement} */ (element('select', { id: `severity-${rule.id}` }));
      for (const value of severities) {
        const option = /** @type {HTMLOptionElement} */ (element('option', { value }, value === rule.defaultSeverity ? `${value} (default)` : value));
        option.selected = value === severity;
        select.append(option);
      }
      select.addEventListener('change', () => vscode.postMessage({ type: 'setSeverity', ruleId: rule.id, severity: select.value }));
      const severityRow = element('div', { class: 'row' });
      severityRow.append(element('label', { for: select.id }, 'Severity'), select);
      card.append(severityRow);

      for (const option of rule.options) {
        card.append(renderOption(option, state.values[option.key] ?? String(option.default)));
      }
      rulesContainer.append(card);
    }

    const banner = /** @type {HTMLElement} */ (document.getElementById('package-banner'));
    banner.hidden = !state.hasCSharpProject || state.packageReferenced;

    if (focusedId) {
      document.getElementById(focusedId)?.focus();
    }
  }

  function renderOption(option, value) {
    const id = `option-${option.key}`;
    const row = element('div', { class: 'row option' });
    const describedBy = `${id}-description`;
    let input;

    if (option.type === 'boolean') {
      input = /** @type {HTMLInputElement} */ (element('input', { type: 'checkbox', id, 'aria-describedby': describedBy }));
      input.checked = value === 'true';
      input.addEventListener('change', () => send(String(input.checked)));
      row.append(input, element('label', { for: id }, option.title));
    } else if (option.type === 'enum') {
      input = /** @type {HTMLSelectElement} */ (element('select', { id, 'aria-describedby': describedBy }));
      for (const choice of option.values ?? []) {
        const item = /** @type {HTMLOptionElement} */ (element('option', { value: choice }, choice));
        item.selected = choice === value;
        input.append(item);
      }
      input.addEventListener('change', () => send(input.value));
      row.append(element('label', { for: id }, option.title), input);
    } else {
      const attributes = { id, 'aria-describedby': describedBy, type: option.type === 'integer' ? 'number' : 'text' };
      if (option.type === 'integer') {
        attributes.step = '1';
        if (option.minimum !== undefined) attributes.min = String(option.minimum);
      }
      input = /** @type {HTMLInputElement} */ (element('input', attributes));
      input.value = value;
      input.addEventListener('change', () => {
        if (input.checkValidity() && input.value.trim() !== '') send(input.value.trim());
        else input.reportValidity();
      });
      row.append(element('label', { for: id }, option.title), input);
    }

    row.append(element('p', { id: describedBy, class: 'hint' }, `${option.description} (${option.key}, default ${option.default})`));
    return row;

    /** @param {string} newValue */
    function send(newValue) {
      vscode.postMessage({ type: 'setOption', key: option.key, value: newValue });
    }
  }

  document.getElementById('add-package')?.addEventListener('click', () => vscode.postMessage({ type: 'addPackage' }));

  // Arrow keys move between enabled tabs (WAI-ARIA tabs pattern).
  const tabs = /** @type {HTMLElement[]} */ (Array.from(document.querySelectorAll('[role="tab"]')));
  for (const tab of tabs) {
    tab.addEventListener('keydown', (event) => {
      if (event.key !== 'ArrowRight' && event.key !== 'ArrowLeft') return;
      const enabledTabs = tabs.filter((t) => t.getAttribute('aria-disabled') !== 'true');
      const next = enabledTabs[(enabledTabs.indexOf(tab) + (event.key === 'ArrowRight' ? 1 : enabledTabs.length - 1)) % enabledTabs.length];
      next.focus();
    });
  }

  window.addEventListener('message', (event) => {
    // Only the extension host (a vscode-webview origin) may drive the page.
    if (!event.origin.startsWith('vscode-webview://')) return;
    if (event.data.type === 'state') render(event.data);
  });
  vscode.postMessage({ type: 'ready' });
})();
