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

  const categoryLabels = { Style: 'Simplification', Performance: 'Performance', DeadCode: 'Dead code', Duplicates: 'Duplicates' };
  const categoryOrder = Object.values(categoryLabels);
  const search = /** @type {HTMLInputElement} */ (document.getElementById('rule-search'));
  const chips = /** @type {HTMLElement[]} */ (Array.from(document.querySelectorAll('[data-filter]')));
  const modifiedCount = /** @type {HTMLElement} */ (document.getElementById('modified-count'));
  const resetAll = /** @type {HTMLButtonElement} */ (document.getElementById('reset-all'));
  const expanded = new Set();
  let filter = 'all';
  let lastState;

  const label = (rule) => categoryLabels[rule.category] ?? rule.category;
  const severityOf = (rule, state) => state.values[`dotnet_diagnostic.${rule.id}.severity`] ?? rule.defaultSeverity;

  /** Settings that differ from the defaults, as {key, value} pairs that restore them. */
  function modifications(rule, state) {
    if (rule.type !== 'realtime') return [];
    const changed = [];
    if (severityOf(rule, state) !== rule.defaultSeverity) {
      changed.push({ key: `dotnet_diagnostic.${rule.id}.severity`, value: rule.defaultSeverity });
    }
    for (const option of rule.options) {
      const value = state.values[option.key];
      if (value !== undefined && value !== String(option.default)) changed.push({ key: option.key, value: String(option.default) });
    }
    return changed;
  }

  function render(state) {
    lastState = state;
    const focusedId = document.activeElement?.id;
    rulesContainer.replaceChildren();

    const query = search.value.trim().toLowerCase();
    const counts = {
      all: state.rules.length,
      realtime: state.rules.filter((r) => r.type === 'realtime').length,
      cli: state.rules.filter((r) => r.type === 'cli').length,
      modified: state.rules.filter((r) => modifications(r, state).length > 0).length,
    };
    for (const chip of chips) {
      const name = /** @type {string} */ (chip.dataset.filter);
      chip.setAttribute('aria-pressed', String(name === filter));
      /** @type {HTMLElement} */ (chip.querySelector('.count')).textContent = String(counts[name]);
    }
    modifiedCount.textContent = `${counts.modified} modified rule${counts.modified === 1 ? '' : 's'}`;
    resetAll.disabled = counts.modified === 0;

    const visible = state.rules.filter((rule) =>
      (filter === 'all' || (filter === 'modified' ? modifications(rule, state).length > 0 : rule.type === filter)) &&
      (query === '' || rule.id.toLowerCase().includes(query) || rule.title.toLowerCase().includes(query)));

    const groups = new Map();
    for (const rule of visible) {
      groups.set(label(rule), [...(groups.get(label(rule)) ?? []), rule]);
    }
    const names = [...groups.keys()].sort((x, y) => (categoryOrder.indexOf(x) + 1 || 99) - (categoryOrder.indexOf(y) + 1 || 99));
    if (names.length === 0) {
      rulesContainer.append(element('p', { class: 'hint empty' }, 'No rule matches.'));
    }
    for (const name of names) {
      const group = element('div', { role: 'rowgroup', class: 'group' });
      group.append(element('h2', { class: 'group-title' }, name));
      for (const rule of groups.get(name)) group.append(renderRule(rule, state));
      rulesContainer.append(group);
    }

    const banner = /** @type {HTMLElement} */ (document.getElementById('package-banner'));
    banner.hidden = !state.hasCSharpProject || state.packageReferenced;

    if (focusedId) {
      document.getElementById(focusedId)?.focus();
    }
  }

  function renderRule(rule, state) {
    const realtime = rule.type === 'realtime';
    const severity = severityOf(rule, state);
    const open = expanded.has(rule.id);
    const item = element('div', { class: 'rule' + (realtime && severity === 'none' ? ' disabled' : '') });
    const row = element('div', { role: 'row', class: 'rule-row', id: `row-${rule.id}`, tabindex: '0', 'aria-expanded': String(open), 'aria-controls': `details-${rule.id}` });
    const toggle = () => {
      if (expanded.has(rule.id)) expanded.delete(rule.id);
      else expanded.add(rule.id);
      render(lastState);
    };
    row.addEventListener('click', (event) => {
      if (!(/** @type {HTMLElement} */ (event.target).closest('input, select, label'))) toggle();
    });
    row.addEventListener('keydown', (event) => {
      if (event.target === row && (event.key === 'Enter' || event.key === ' ')) {
        event.preventDefault();
        toggle();
      }
    });

    const cell = (...children) => {
      const c = element('div', { role: 'gridcell' });
      c.append(...children);
      return c;
    };
    if (realtime) {
      const enabled = /** @type {HTMLInputElement} */ (element('input', { type: 'checkbox', id: `enabled-${rule.id}`, 'aria-label': `Enable ${rule.id}` }));
      enabled.checked = severity !== 'none';
      enabled.addEventListener('change', () => {
        const fallback = rule.defaultSeverity === 'none' ? 'suggestion' : rule.defaultSeverity;
        vscode.postMessage({ type: 'setSeverity', ruleId: rule.id, severity: enabled.checked ? fallback : 'none' });
      });
      row.append(cell(enabled));
    } else {
      row.append(cell());
    }
    row.append(cell(element('span', { class: 'rule-id' }, rule.id)), cell(element('span', { class: 'rule-title', title: rule.title }, rule.title)));

    if (realtime) {
      const select = /** @type {HTMLSelectElement} */ (element('select', { id: `severity-${rule.id}`, 'aria-label': `Severity of ${rule.id}` }));
      for (const value of severities) {
        const option = /** @type {HTMLOptionElement} */ (element('option', { value }, value === rule.defaultSeverity ? `${value} (default)` : value));
        option.selected = value === severity;
        select.append(option);
      }
      select.addEventListener('change', () => vscode.postMessage({ type: 'setSeverity', ruleId: rule.id, severity: select.value }));
      row.append(cell(select));
    } else {
      row.append(cell(element('span', { class: 'hint' }, 'command line')));
    }
    row.append(cell(element('span', { class: 'chevron', 'aria-hidden': 'true' })));
    item.append(row);

    if (open) item.append(renderDetails(rule, state));
    return item;
  }

  function renderDetails(rule, state) {
    const details = element('div', { role: 'row', class: 'details', id: `details-${rule.id}` });
    const body = element('div', { role: 'gridcell' });
    body.append(element('p', { class: 'meta' }, `${rule.id} · ${label(rule)} · ${rule.type === 'realtime' ? 'real time' : 'command line'}`));
    body.append(element('p', { class: 'description' }, rule.description));
    if (rule.details) {
      body.append(element('h3', {}, 'Details'), element('p', { class: 'description' }, rule.details));
    }
    if (rule.options.length > 0) {
      body.append(element('h3', {}, 'Options'));
      for (const option of rule.options) {
        body.append(renderOption(option, state.values[option.key] ?? String(option.default)));
      }
    }
    for (const example of rule.examples ?? []) {
      const pair = element('div', { class: 'examples' });
      pair.append(codeBlock(example.after === undefined ? 'Reported code' : 'Before', example.before));
      if (example.after !== undefined) pair.append(codeBlock('After', example.after));
      body.append(pair);
    }
    if (rule.fix) {
      body.append(element('p', { class: 'description' }, `Suggested fix: ${rule.fix}`));
    }
    details.append(body);
    return details;
  }

  function codeBlock(title, code) {
    const block = element('figure', { class: 'code' });
    block.append(element('figcaption', {}, title), element('pre', { tabindex: '0' }, code));
    return block;
  }

  search.addEventListener('input', () => lastState && render(lastState));
  for (const chip of chips) {
    chip.addEventListener('click', () => {
      filter = /** @type {string} */ (chip.dataset.filter);
      if (lastState) render(lastState);
    });
  }
  resetAll.addEventListener('click', () => {
    const entries = lastState.rules.flatMap((rule) => modifications(rule, lastState));
    vscode.postMessage({ type: 'reset', entries });
  });

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

    row.append(element('code', { class: 'key' }, option.key));
    row.append(element('p', { id: describedBy, class: 'hint' }, `${option.description} (default ${option.default})`));
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

  for (const tab of tabs) {
    tab.addEventListener('click', () => {
      for (const other of tabs) {
        const selected = other === tab;
        other.setAttribute('aria-selected', String(selected));
        other.tabIndex = selected ? 0 : -1;
        const panel = document.getElementById(/** @type {string} */ (other.getAttribute('aria-controls')));
        if (panel) panel.hidden = !selected;
      }
    });
  }

  const toolSources = { setting: 'the configured path', local: 'the local tool manifest', global: 'the global tool', project: 'dotnet run --project' };
  const toolStatus = /** @type {HTMLElement} */ (document.getElementById('tool-status'));
  const runButton = /** @type {HTMLButtonElement} */ (document.getElementById('run'));
  const runStatus = /** @type {HTMLElement} */ (document.getElementById('run-status'));
  const results = /** @type {HTMLElement} */ (document.getElementById('results'));
  const problems = /** @type {HTMLInputElement} */ (document.getElementById('problems'));

  document.getElementById('install')?.addEventListener('click', () => vscode.postMessage({ type: 'install' }));
  document.getElementById('recheck')?.addEventListener('click', () => vscode.postMessage({ type: 'recheck' }));
  document.getElementById('cli-settings')?.addEventListener('click', () => vscode.postMessage({ type: 'openCliSettings' }));
  runButton.addEventListener('click', () => vscode.postMessage({ type: 'run', analysis: /** @type {HTMLSelectElement} */ (document.getElementById('analysis')).value }));
  problems.addEventListener('change', () => vscode.postMessage({ type: 'setProblems', enabled: problems.checked }));

  function renderAnalysis(state) {
    toolStatus.textContent = state.tool
      ? `neolinter command line found (${toolSources[state.tool]}).`
      : 'The neolinter command line was not found. Install it, or set neolinter.cli.path / neolinter.cli.projectPath. The Neolinter.Cli package is not on nuget.org until the first release.';
    runButton.disabled = state.status === 'running';
    problems.checked = state.showInProblems;

    const count = state.groups.reduce((total, group) => total + group.findings.length, 0);
    runStatus.textContent =
      state.status === 'running' ? 'Analysis running…'
      : state.status === 'failed' ? state.message ?? 'The analysis failed.'
      : state.status === 'done' ? (count === 0 ? 'No findings.' : `${count} finding(s).`)
      : '';

    results.replaceChildren();
    for (const group of state.groups) {
      const section = element('section', { class: 'rule', 'aria-labelledby': `result-${group.ruleId}` });
      section.append(element('h2', { id: `result-${group.ruleId}` }, `${group.ruleId} — ${group.description ?? ''} (${group.findings.length})`));
      const list = element('ul', { class: 'findings' });
      for (const finding of group.findings) {
        const item = element('li');
        const link = element('button', { type: 'button', class: 'link' }, `${finding.file}:${finding.line}:${finding.column}`);
        link.addEventListener('click', () => vscode.postMessage({ type: 'openFinding', index: finding.index }));
        item.append(link, ' ', element('span', {}, finding.message));
        list.append(item);
      }
      section.append(list);
      results.append(section);
    }
  }

  window.addEventListener('message', (event) => {
    // Only the extension host (a vscode-webview origin) may drive the page.
    if (!event.origin.startsWith('vscode-webview://')) return;
    if (event.data.type === 'state') render(event.data);
    if (event.data.type === 'analysis') renderAnalysis(event.data);
  });
  vscode.postMessage({ type: 'ready' });
})();
