// Blazor Workbench preview bridge.
// Loaded as a classic script in the preview document <head> so console, error and network capture starts before Blazor.
// Exposes window.__workbench for automation (Playwright) and talks to the Workbench shell through postMessage.
(function () {
  'use strict';
  if (window.BlazorWorkbenchPreview) return;

  const SHELL = 'blazor-workbench-shell';
  const PREVIEW = 'blazor-workbench-preview';
  const ROOT_ID = 'workbench-preview-root';
  const QUIET_MS = 150;
  const MAX_DIAGNOSTICS = 500;
  const inFrame = window.parent && window.parent !== window;

  const state = {
    state: 'loading',
    sessionId: null,
    scenarioId: (location.pathname.match(/\/_workbench\/preview\/([^/?#]+)/) || [])[1] || '',
    attached: false,
    pending: 0,
    readySelector: null,
    error: null,
    diagnostics: [],
    callbacks: [],
    callbackCounts: {},
    gates: [],
    domUpdates: 0,
    lastMutation: performance.now(),
    readyAt: null,
    dotnet: null,
  };

  // ---------- messaging ----------
  function post(type, payload) {
    if (!inFrame) return;
    try { window.parent.postMessage({ source: PREVIEW, type, payload, scenarioId: state.scenarioId }, location.origin); } catch { /* ignore */ }
  }

  // ---------- diagnostics ----------
  const redactPatterns = [
    [/\b(Bearer|Basic|Digest)\s+[A-Za-z0-9\-._~+/]+=*/gi, '$1 [REDACTED]'],
    [/\beyJ[A-Za-z0-9_-]{5,}\.[A-Za-z0-9_-]{5,}\.[A-Za-z0-9_-]*/g, '[REDACTED]'],
    [/([?&](?:access_token|id_token|refresh_token|token|code|key|api_key|apikey|secret|client_secret|password|pwd|sig|signature|session|sessionid|auth)=)[^&#\s]*/gi, '$1[REDACTED]'],
    // Quoted values are redacted up to the closing quote (honoring \" escapes), so values with spaces or commas never leak.
    [/((?:password|passwd|pwd|secret|api[_-]?key|access[_-]?token|refresh[_-]?token)"?\s*[:=]\s*)"(?:[^"\\]|\\.)*("|$)/gi, '$1"[REDACTED]$2'],
    [/((?:password|passwd|pwd|secret|api[_-]?key|access[_-]?token|refresh[_-]?token)"?\s*[:=]\s*)(?!")[^\s"&;,}]+/gi, '$1[REDACTED]'],
  ];
  function redact(text) {
    if (typeof text !== 'string') return text;
    for (const [re, rep] of redactPatterns) text = text.replace(re, rep);
    return text.length > 2000 ? text.slice(0, 2000) + '…' : text;
  }

  function stringify(value) {
    if (value instanceof Error) return value.stack || `${value.name}: ${value.message}`;
    if (typeof value === 'string') return value;
    try { return JSON.stringify(value); } catch { return String(value); }
  }

  function addDiagnostic(d) {
    const entry = {
      timestamp: d.timestamp || new Date().toISOString(),
      source: d.source || 'Console',
      severity: d.severity || 'Info',
      scenarioId: d.scenarioId || state.scenarioId,
      message: redact(d.message || ''),
      details: d.details ? redact(d.details) : undefined,
      sessionId: d.sessionId || state.sessionId || undefined,
    };
    state.diagnostics.push(entry);
    if (state.diagnostics.length > MAX_DIAGNOSTICS) state.diagnostics.shift();
    post('diagnostic', entry);
    return entry;
  }

  const originalConsole = { error: console.error.bind(console), warn: console.warn.bind(console) };
  console.error = function (...args) {
    addDiagnostic({ source: 'Console', severity: 'Error', message: args.map(stringify).join(' ') });
    originalConsole.error(...args);
  };
  console.warn = function (...args) {
    addDiagnostic({ source: 'Console', severity: 'Warning', message: args.map(stringify).join(' ') });
    originalConsole.warn(...args);
  };

  window.addEventListener('error', (e) => {
    const t = e.target;
    if (t && t !== window && t.tagName) {
      const url = t.src || t.href || t.currentSrc || '';
      addDiagnostic({ source: 'Resource', severity: 'Error', message: `Failed to load <${t.tagName.toLowerCase()}> ${url}` });
      return;
    }
    addDiagnostic({ source: 'Console', severity: 'Error', message: e.message || 'Script error', details: e.error && e.error.stack });
  }, true);

  window.addEventListener('unhandledrejection', (e) => {
    addDiagnostic({ source: 'Console', severity: 'Error', message: 'Unhandled promise rejection: ' + stringify(e.reason) });
  });

  const ignoredRequest = (url) => /\/_blazor|\/_framework\/|\/_workbench\/api\/|browser-refresh|aspnetcore-browser-refresh/.test(url);

  if (window.fetch) {
    const originalFetch = window.fetch.bind(window);
    window.fetch = async function (input, init) {
      const url = typeof input === 'string' ? input : (input && input.url) || '';
      const method = (init && init.method) || (input && input.method) || 'GET';
      try {
        const response = await originalFetch(input, init);
        if (!response.ok && !ignoredRequest(url)) {
          addDiagnostic({ source: 'Network', severity: response.status >= 500 ? 'Error' : 'Warning', message: `${method} ${url} → ${response.status}` });
        }
        return response;
      } catch (err) {
        if (!ignoredRequest(url)) addDiagnostic({ source: 'Network', severity: 'Error', message: `${method} ${url} failed: ${stringify(err)}` });
        throw err;
      }
    };
  }

  if (window.XMLHttpRequest) {
    const open = XMLHttpRequest.prototype.open;
    XMLHttpRequest.prototype.open = function (method, url) {
      this.__wb = { method, url: String(url) };
      this.addEventListener('loadend', () => {
        const info = this.__wb;
        if (!info || ignoredRequest(info.url)) return;
        if (this.status === 0) addDiagnostic({ source: 'Network', severity: 'Error', message: `${info.method} ${info.url} failed (network error)` });
        else if (this.status >= 400) addDiagnostic({ source: 'Network', severity: this.status >= 500 ? 'Error' : 'Warning', message: `${info.method} ${info.url} → ${this.status}` });
      });
      return open.apply(this, arguments);
    };
  }

  // ---------- focus tracing ----------
  document.addEventListener('focusin', (e) => {
    const el = e.target;
    if (!el || !el.tagName) return;
    addDiagnostic({ source: 'Focus', severity: 'Trace', message: `Focus → ${describe(el)}` });
  }, true);

  function describe(el) {
    if (!el || !el.tagName) return String(el);
    const id = el.id ? `#${el.id}` : '';
    const name = accessibleName(el);
    const role = roleOf(el);
    return `${el.tagName.toLowerCase()}${id}${role ? ` [${role}]` : ''}${name ? ` "${name.slice(0, 60)}"` : ''}`;
  }

  // ---------- readiness ----------
  function setState(value, detail) {
    if (state.state === value) return;
    if (state.state === 'error' && value !== 'error') return;
    state.state = value;
    document.documentElement.setAttribute('data-workbench-state', value);
    if (value === 'ready') state.readyAt = performance.now();
    post('state', { state: value, detail, metrics: metrics() });
  }

  function metrics() {
    return { readyMs: state.readyAt ? Math.round(state.readyAt) : null, domUpdates: state.domUpdates, pending: state.pending };
  }

  function root() { return document.getElementById(ROOT_ID); }

  let checkTimer = 0;
  function scheduleCheck(delay) {
    clearTimeout(checkTimer);
    checkTimer = setTimeout(checkReady, delay == null ? QUIET_MS : delay);
  }

  function imagesLoaded(scope) {
    return Array.from((scope || document).querySelectorAll('img')).every((img) => img.complete);
  }

  async function checkReady() {
    if (state.state !== 'loading' || !state.attached) return;
    if (document.querySelector('[data-workbench-error]')) { setState('error', 'error boundary'); return; }
    if (state.pending > 0) return;
    const quietFor = performance.now() - state.lastMutation;
    if (quietFor < QUIET_MS) { scheduleCheck(QUIET_MS - quietFor + 5); return; }
    const r = root();
    if (state.readySelector && !document.querySelector(state.readySelector)) { scheduleCheck(50); return; }
    if (document.fonts && document.fonts.status !== 'loaded') { document.fonts.ready.then(() => scheduleCheck(0)); return; }
    if (!imagesLoaded(r)) { scheduleCheck(50); return; }
    setState('ready');
  }

  const observer = new MutationObserver((records) => {
    // Overlays drawn by the preview tools are not part of the scenario.
    if (records.every((r) => isToolNode(r.target))) return;
    state.lastMutation = performance.now();
    if (state.state === 'loading') {
      state.domUpdates++;
      // Pages that fail before the scenario host attaches (unknown scenario, bad arguments) report errors immediately.
      if (!state.attached && document.querySelector('[data-workbench-error]')) setState('error', 'preview error');
      else scheduleCheck();
    }
  });
  document.documentElement.setAttribute('data-workbench-state', 'loading');
  function observe() { observer.observe(document.body, { subtree: true, childList: true, attributes: true, characterData: true }); }
  if (document.body) observe(); else document.addEventListener('DOMContentLoaded', observe);

  function waitForIdle(timeout, quiet) {
    timeout = timeout || 5000;
    quiet = quiet || 60;
    const start = performance.now();
    return new Promise((resolve) => {
      (function poll() {
        const idle = state.pending === 0 && performance.now() - state.lastMutation >= quiet;
        if (idle || performance.now() - start > timeout) resolve(idle);
        else setTimeout(poll, 20);
      })();
    });
  }

  // ---------- environment ----------
  function applyTheme(t) {
    if (!t) return;
    const html = document.documentElement;
    if (t.theme) html.setAttribute('data-theme', t.theme); else html.removeAttribute('data-theme');
    html.style.colorScheme = t.colorScheme || '';
    if (html.dataset.workbenchThemeClass) html.classList.remove(html.dataset.workbenchThemeClass);
    if (t.themeClass) { html.classList.add(t.themeClass); html.dataset.workbenchThemeClass = t.themeClass; }
    document.querySelectorAll('link[data-workbench-theme]').forEach((l) => l.remove());
    (t.themeStylesheets || []).forEach((href) => {
      const link = document.createElement('link');
      link.rel = 'stylesheet'; link.href = href; link.setAttribute('data-workbench-theme', t.theme || '');
      document.head.appendChild(link);
    });
  }

  function applyDir(dir) {
    if (dir) document.documentElement.setAttribute('dir', dir);
  }

  // ---------- locators ----------
  function roleOf(el) {
    const explicit = el.getAttribute && el.getAttribute('role');
    if (explicit) return explicit.trim().split(/\s+/)[0];
    const tag = el.tagName.toLowerCase();
    const type = (el.getAttribute('type') || '').toLowerCase();
    switch (tag) {
      case 'button': return 'button';
      case 'a': case 'area': return el.hasAttribute('href') ? 'link' : null;
      case 'input':
        if (['button', 'submit', 'reset', 'image'].includes(type)) return 'button';
        if (type === 'checkbox') return 'checkbox';
        if (type === 'radio') return 'radio';
        if (type === 'range') return 'slider';
        if (type === 'number') return 'spinbutton';
        if (type === 'search') return 'searchbox';
        if (type === 'hidden') return null;
        return el.hasAttribute('list') ? 'combobox' : 'textbox';
      case 'textarea': return 'textbox';
      case 'select': return el.multiple || el.size > 1 ? 'listbox' : 'combobox';
      case 'option': return 'option';
      case 'h1': case 'h2': case 'h3': case 'h4': case 'h5': case 'h6': return 'heading';
      case 'ul': case 'ol': case 'menu': return 'list';
      case 'li': return 'listitem';
      case 'nav': return 'navigation';
      case 'main': return 'main';
      case 'aside': return 'complementary';
      case 'form': return 'form';
      case 'table': return 'table';
      case 'tr': return 'row';
      case 'td': return 'cell';
      case 'th': return 'columnheader';
      case 'img': return el.getAttribute('alt') === '' ? 'presentation' : 'img';
      case 'dialog': return 'dialog';
      case 'progress': return 'progressbar';
      case 'meter': return 'meter';
      case 'article': return 'article';
      case 'output': return 'status';
      case 'fieldset': case 'details': return 'group';
      case 'summary': return 'button';
      case 'section': return el.hasAttribute('aria-label') || el.hasAttribute('aria-labelledby') ? 'region' : null;
      case 'p': return 'paragraph';
      default: return null;
    }
  }

  function normalize(s) { return (s || '').replace(/\s+/g, ' ').trim(); }

  function accessibleName(el) {
    const labelledBy = el.getAttribute('aria-labelledby');
    if (labelledBy) return normalize(labelledBy.split(/\s+/).map((id) => (document.getElementById(id) || {}).textContent || '').join(' '));
    const label = el.getAttribute('aria-label');
    if (label) return normalize(label);
    if (el.labels && el.labels.length) return normalize(Array.from(el.labels).map((l) => l.textContent).join(' '));
    if (el.tagName === 'IMG') return normalize(el.getAttribute('alt'));
    if (el.tagName === 'INPUT' && ['button', 'submit', 'reset'].includes(el.type)) return normalize(el.value);
    const role = roleOf(el);
    if (['button', 'link', 'heading', 'checkbox', 'radio', 'tab', 'menuitem', 'option', 'cell', 'columnheader', 'listitem', 'switch', 'treeitem'].includes(role)) {
      return normalize(el.textContent);
    }
    return normalize(el.getAttribute('title') || el.getAttribute('placeholder') || '');
  }

  function textMatches(actual, expected, exact) {
    actual = normalize(actual);
    expected = normalize(expected);
    return exact ? actual === expected : actual.toLowerCase().includes(expected.toLowerCase());
  }

  function isVisible(el) {
    if (!el.isConnected) return false;
    const style = getComputedStyle(el);
    if (style.visibility === 'hidden' || style.display === 'none') return false;
    return el.getClientRects().length > 0;
  }

  function cssEscape(v) { return window.CSS && CSS.escape ? CSS.escape(v) : v.replace(/"/g, '\\"'); }

  function all(selector) { return Array.from(document.body.querySelectorAll(selector)).filter((e) => !isToolNode(e)); }

  function resolve(locator) {
    const value = locator.value;
    switch (locator.kind) {
      case 'Css': return all(value);
      case 'TestId': return all(`[data-testid="${cssEscape(value)}"]`);
      case 'Placeholder': return all('[placeholder]').filter((e) => textMatches(e.getAttribute('placeholder'), value, locator.exact));
      case 'Label': {
        const byLabel = all('label').filter((l) => textMatches(l.textContent, value, locator.exact)).map((l) => l.control).filter(Boolean);
        const byAria = all('[aria-label],[aria-labelledby]').filter((e) => textMatches(accessibleName(e), value, locator.exact));
        return Array.from(new Set([...byLabel, ...byAria]));
      }
      case 'Text': {
        const candidates = all('body *').filter((e) => !['SCRIPT', 'STYLE', 'TEMPLATE'].includes(e.tagName) && textMatches(e.textContent, value, locator.exact));
        // Keep the innermost elements, like Playwright.
        return candidates.filter((e) => !candidates.some((other) => other !== e && e.contains(other)));
      }
      case 'Role': {
        return all('*').filter((e) => roleOf(e) === value && isVisible(e) &&
          (locator.name == null || textMatches(accessibleName(e), locator.name, locator.exact)));
      }
      default: throw new Error(`Unsupported locator kind ${locator.kind}`);
    }
  }

  function describeLocator(l) {
    if (!l) return '';
    if (l.kind === 'Role') return `role=${l.value}${l.name != null ? `[name="${l.name}"]` : ''}`;
    return `${l.kind.toLowerCase()}=${l.value}`;
  }

  function single(locator) {
    const matches = resolve(locator);
    if (matches.length === 0) throw new Error(`No element matches ${describeLocator(locator)}`);
    if (matches.length > 1) throw new Error(`Strict mode violation: ${describeLocator(locator)} matches ${matches.length} elements`);
    return matches[0];
  }

  async function retry(fn, timeout) {
    const start = performance.now();
    let lastError;
    for (;;) {
      try { return await fn(); } catch (e) { lastError = e; }
      if (performance.now() - start > (timeout || 5000)) throw lastError;
      await new Promise((r) => setTimeout(r, 50));
    }
  }

  // ---------- interaction runner (best effort; Playwright is authoritative) ----------
  const valueSetter = (el) => {
    const proto = el.tagName === 'TEXTAREA' ? HTMLTextAreaElement.prototype : el.tagName === 'SELECT' ? HTMLSelectElement.prototype : HTMLInputElement.prototype;
    return Object.getOwnPropertyDescriptor(proto, 'value').set;
  };

  function setValue(el, value) {
    valueSetter(el).call(el, value);
    el.dispatchEvent(new Event('input', { bubbles: true }));
  }

  function key(el, k, type) {
    return el.dispatchEvent(new KeyboardEvent(type, { key: k, code: k, bubbles: true, cancelable: true }));
  }

  function tabbables() {
    return all('a[href],area[href],button,input,select,textarea,[tabindex],[contenteditable="true"],summary')
      .filter((e) => !e.disabled && e.tabIndex >= 0 && isVisible(e) && !(e.tagName === 'INPUT' && e.type === 'hidden'))
      .sort((a, b) => (a.tabIndex || 0) === (b.tabIndex || 0) ? 0 : (a.tabIndex || Infinity) - (b.tabIndex || Infinity));
  }

  async function press(target, combo) {
    const el = target || document.activeElement || document.body;
    const parts = combo.split('+');
    const k = parts[parts.length - 1];
    const shift = parts.includes('Shift');
    if (k === 'Tab') {
      const list = tabbables();
      const index = list.indexOf(document.activeElement);
      const next = list[(index + (shift ? -1 : 1) + list.length) % list.length];
      key(el, 'Tab', 'keydown');
      if (next) next.focus();
      key(next || el, 'Tab', 'keyup');
      return;
    }
    const notCanceled = key(el, k, 'keydown');
    if (notCanceled) {
      const role = roleOf(el);
      if (k === 'Enter' && (role === 'button' || role === 'link')) el.click();
      else if ((k === ' ' || k === 'Space') && (role === 'button' || role === 'checkbox' || role === 'switch' || role === 'radio')) el.click();
      else if (k === 'Enter' && el.form && el.tagName === 'INPUT') el.form.requestSubmit ? el.form.requestSubmit() : el.form.submit();
    }
    key(el, k, 'keyup');
  }

  async function runStep(step) {
    const t = step.target;
    switch (step.action) {
      case 'Click': return retry(() => { const el = single(t); if (el.disabled) throw new Error('Element is disabled'); el.scrollIntoView({ block: 'nearest' }); el.click(); });
      case 'DoubleClick': return retry(() => { const el = single(t); el.click(); el.click(); el.dispatchEvent(new MouseEvent('dblclick', { bubbles: true })); });
      case 'Hover': return retry(() => { const el = single(t); el.dispatchEvent(new MouseEvent('mouseover', { bubbles: true })); el.dispatchEvent(new MouseEvent('mouseenter')); });
      case 'Focus': return retry(() => single(t).focus());
      case 'Blur': return retry(() => single(t).blur());
      case 'Fill': return retry(() => { const el = single(t); el.focus(); setValue(el, step.value); el.dispatchEvent(new Event('change', { bubbles: true })); });
      case 'Type': return retry(async () => {
        const el = single(t); el.focus();
        for (const ch of step.value) { key(el, ch, 'keydown'); setValue(el, (el.value || '') + ch); key(el, ch, 'keyup'); }
        el.dispatchEvent(new Event('change', { bubbles: true }));
      });
      case 'Press': return press(t ? single(t) : null, step.value);
      case 'Check': case 'Uncheck': return retry(() => { const el = single(t); if (el.checked !== (step.action === 'Check')) el.click(); });
      case 'Select': return retry(() => {
        const el = single(t);
        const option = Array.from(el.options || []).find((o) => o.value === step.value || normalize(o.textContent) === normalize(step.value));
        if (!option) throw new Error(`Option "${step.value}" not found`);
        valueSetter(el).call(el, option.value);
        el.dispatchEvent(new Event('input', { bubbles: true }));
        el.dispatchEvent(new Event('change', { bubbles: true }));
      });
      case 'ReleaseGate': return state.dotnet.invokeMethodAsync('ReleaseGate', step.value);
      case 'FailGate': return state.dotnet.invokeMethodAsync('FailGate', step.value, null);
      case 'WaitFor': case 'ExpectVisible': return retry(() => { if (!isVisible(single(t))) throw new Error(`${describeLocator(t)} is not visible`); });
      case 'ExpectHidden': return retry(() => { if (resolve(t).some(isVisible)) throw new Error(`${describeLocator(t)} is visible`); });
      case 'ExpectText': return retry(() => { const el = single(t); if (!textMatches(el.textContent, step.expected, false)) throw new Error(`Expected text "${step.expected}" but found "${normalize(el.textContent).slice(0, 120)}"`); });
      case 'ExpectValue': return retry(() => { const el = single(t); if ((el.value || '') !== step.expected) throw new Error(`Expected value "${step.expected}" but found "${el.value}"`); });
      case 'ExpectAttribute': return retry(() => { const el = single(t); const v = el.getAttribute(step.value); if (v !== step.expected) throw new Error(`Expected ${step.value}="${step.expected}" but found ${v === null ? 'no attribute' : `"${v}"`}`); });
      case 'ExpectFocused': return retry(() => { const el = single(t); if (document.activeElement !== el) throw new Error(`Focus is on ${describe(document.activeElement)}`); });
      case 'ExpectCount': return retry(() => { const n = resolve(t).length; if (n !== step.count) throw new Error(`Expected ${step.count} elements but found ${n}`); });
      case 'ExpectCallback': return retry(() => {
        const n = state.callbackCounts[step.value] || 0;
        if (step.count == null ? n === 0 : n !== step.count) throw new Error(`Expected ${step.value} to be invoked ${step.count == null ? 'at least once' : step.count + ' time(s)'} but it was invoked ${n} time(s)`);
      });
      case 'ExpectNoErrors': {
        const errors = state.diagnostics.filter((d) => d.severity === 'Error');
        if (errors.length) throw new Error(`${errors.length} error diagnostic(s): ${errors[0].message}`);
        return;
      }
      case 'Checkpoint': return;
      case 'DragTo': return retry(() => {
        const source = single(t);
        const target = single(step.destination);
        const data = new DataTransfer();
        const fire = (el, type) => el.dispatchEvent(new DragEvent(type, { bubbles: true, cancelable: true, dataTransfer: data }));
        fire(source, 'dragstart');
        fire(target, 'dragenter');
        fire(target, 'dragover');
        fire(target, 'drop');
        fire(source, 'dragend');
      });
      case 'Upload': return retry(() => {
        const input = single(t);
        const bytes = Uint8Array.from(atob(step.fileContent || ''), (c) => c.charCodeAt(0));
        const data = new DataTransfer();
        data.items.add(new File([bytes], step.value, { type: step.contentType || 'application/octet-stream' }));
        input.files = data.files;
        input.dispatchEvent(new Event('input', { bubbles: true }));
        input.dispatchEvent(new Event('change', { bubbles: true }));
      });
      case 'ScrollIntoView': return retry(() => single(t).scrollIntoView({ block: 'center' }));
      case 'ExpectEnabled': case 'ExpectDisabled': return retry(() => {
        const el = single(t);
        const disabled = el.disabled === true || el.getAttribute('aria-disabled') === 'true';
        if (disabled !== (step.action === 'ExpectDisabled')) throw new Error(`${describeLocator(t)} is ${disabled ? 'disabled' : 'enabled'}`);
      });
      case 'ExpectChecked': case 'ExpectUnchecked': return retry(() => {
        const el = single(t);
        const checked = el.checked === true || el.getAttribute('aria-checked') === 'true';
        if (checked !== (step.action === 'ExpectChecked')) throw new Error(`${describeLocator(t)} is ${checked ? 'checked' : 'not checked'}`);
      });
      default: throw new Error(`Unsupported step ${step.action}`);
    }
  }

  function describeStep(step) {
    const parts = [step.action];
    if (step.target) parts.push(describeLocator(step.target));
    if (step.destination) parts.push('→ ' + describeLocator(step.destination));
    if (step.value != null) parts.push(JSON.stringify(step.value));
    if (step.expected != null) parts.push('=> ' + JSON.stringify(step.expected));
    if (step.count != null) parts.push('x' + step.count);
    return parts.join(' ');
  }

  // Step-through debugging: the runner pauses before each step until the shell sends 'interaction-continue'.
  let debug = null;
  function waitForContinue(test, index, step) {
    let elements = [];
    try { if (step.target) elements = resolve(step.target); } catch { elements = []; }
    highlight(elements, `${index + 1}. ${describeStep(step)}`, true);
    post('interaction-paused', { name: test.name, index });
    return new Promise((resolveContinue) => { debug.resume = resolveContinue; });
  }

  async function runInteraction(test, options) {
    const results = [];
    let failed = false;
    debug = options && options.step ? { mode: 'step', resume: null } : null;
    for (let i = 0; i < test.steps.length; i++) {
      const step = test.steps[i];
      if (!failed && debug && debug.mode === 'step') {
        const command = await waitForContinue(test, i, step);
        if (command === 'stop') failed = true;
        else if (command === 'run') debug.mode = 'run';
      }
      const started = performance.now();
      if (failed) { results.push({ index: i, description: describeStep(step), status: 'skipped' }); continue; }
      try {
        await runStep(step);
        await waitForIdle(5000);
        results.push({ index: i, description: describeStep(step), status: 'passed', durationMs: Math.round(performance.now() - started) });
      } catch (e) {
        failed = true;
        results.push({ index: i, description: describeStep(step), status: 'failed', error: String(e && e.message || e), durationMs: Math.round(performance.now() - started) });
      }
      post('interaction-progress', { name: test.name, results });
    }
    const outcome = { name: test.name, passed: !results.some((r) => r.status === 'failed') && results.every((r) => r.status === 'passed'), results };
    if (debug) {
      const failedStep = results.find((r) => r.status === 'failed');
      if (failedStep && test.steps[failedStep.index].target) {
        try { highlight(resolve(test.steps[failedStep.index].target), `Failed: ${failedStep.description}`, true); } catch { highlight(null); }
      } else highlight(null);
    }
    debug = null;
    post('interaction', outcome);
    return outcome;
  }

  // ---------- accessibility ----------
  let axeLoading = null;
  function loadAxe() {
    if (window.axe) return Promise.resolve();
    if (!axeLoading) {
      axeLoading = new Promise((resolve, reject) => {
        const s = document.createElement('script');
        s.src = '/_workbench/axe.min.js';
        s.onload = () => resolve();
        s.onerror = () => reject(new Error('axe-core could not be loaded from /_workbench/axe.min.js'));
        document.head.appendChild(s);
      });
    }
    return axeLoading;
  }

  async function runAxe(options) {
    await loadAxe();
    const rules = {};
    (options && options.disabledRules || []).forEach((r) => { rules[r] = { enabled: false }; });
    const runOptions = { resultTypes: ['violations', 'incomplete'], rules };
    if (options && options.tags && options.tags.length) runOptions.runOnly = { type: 'tag', values: options.tags };
    const result = await window.axe.run(root() || document, runOptions);
    const map = (items) => items.map((v) => ({
      id: v.id, impact: v.impact, help: v.help, description: v.description, helpUrl: v.helpUrl, tags: v.tags,
      nodes: v.nodes.slice(0, 20).map((n) => ({ target: n.target.join(' '), html: redact(n.html).slice(0, 500), failureSummary: n.failureSummary })),
    }));
    const outcome = {
      engine: `axe-core ${window.axe.version}`,
      violations: map(result.violations),
      incomplete: map(result.incomplete),
      passes: result.passes ? result.passes.length : 0,
      url: location.pathname,
    };
    post('a11y', outcome);
    return outcome;
  }

  // ---------- preview tools (canvas toolbar, accessibility tools, inspection) ----------
  // Everything drawn by the tools lives in one layer outside the preview root, marked data-wb-tool, so readiness, locators,
  // axe-core scans and screenshots ignore it. Tool styles are <style data-wb-tool> elements in <head>.
  const TOOL_ATTR = 'data-wb-tool';
  const tools = { current: {}, declaredPseudo: [], gridSize: 8 };
  let layer = null;

  function isToolNode(node) {
    const el = node && (node.nodeType === 1 ? node : node.parentElement);
    return !!(el && el.closest && el.closest('[' + TOOL_ATTR + ']'));
  }

  function toolLayer() {
    if (layer && layer.isConnected) return layer;
    layer = document.createElement('div');
    layer.setAttribute(TOOL_ATTR, 'layer');
    layer.setAttribute('aria-hidden', 'true');
    layer.style.cssText = 'position:fixed;inset:0;pointer-events:none;z-index:2147483646;overflow:hidden;';
    document.body.appendChild(layer);
    return layer;
  }

  function toolPart(name, create) {
    const host = toolLayer();
    let el = host.querySelector(`[data-wb-part="${name}"]`);
    if (!el && create) {
      el = document.createElement('div');
      el.setAttribute('data-wb-part', name);
      el.style.cssText = 'position:absolute;inset:0;';
      host.appendChild(el);
    }
    return el;
  }

  function removePart(name) { const el = toolPart(name, false); if (el) el.remove(); }

  function toolStyle(name, css) {
    let el = document.head.querySelector(`style[${TOOL_ATTR}="${name}"]`);
    if (!css) { if (el) el.remove(); return; }
    if (!el) { el = document.createElement('style'); el.setAttribute(TOOL_ATTR, name); document.head.appendChild(el); }
    if (el.textContent !== css) el.textContent = css;
  }

  function box(parent, rect, style, label) {
    const b = document.createElement('div');
    b.style.cssText = `position:absolute;left:${rect.left}px;top:${rect.top}px;width:${Math.max(0, rect.width)}px;height:${Math.max(0, rect.height)}px;box-sizing:border-box;${style}`;
    if (label) {
      const l = document.createElement('span');
      l.textContent = label;
      l.style.cssText = 'position:absolute;left:0;top:-18px;font:11px/16px system-ui,sans-serif;padding:0 4px;border-radius:3px;background:#1d1d22;color:#fff;white-space:nowrap;';
      if (rect.top < 20) l.style.top = '100%';
      b.appendChild(l);
    }
    parent.appendChild(b);
    return b;
  }

  // Tool values can come from a link (?bg=, ?overlay=), so they are checked before they reach CSS or an <img>.
  const safeCssValue = (v) => typeof v === 'string' && v.length <= 300 && !/[{};<>@\\]|\/\*/.test(v);
  const safeUrl = (u) => typeof u === 'string' && u.length <= 2000 && (/^https?:\/\//i.test(u) || !/^[a-z][a-z0-9+.-]*:/i.test(u)) && !/^\/\//.test(u);

  // Background
  function applyBackground(value) {
    toolStyle('background', value && safeCssValue(value) ? `html, body { background: ${value} !important; }` : '');
  }

  // Grid
  function applyGrid(on) {
    if (!on) { removePart('grid'); return; }
    const size = tools.gridSize || 8;
    const part = toolPart('grid', true);
    const minor = 'rgba(255, 71, 133, 0.12)';
    const major = 'rgba(255, 71, 133, 0.28)';
    part.style.backgroundImage = `linear-gradient(to right, ${major} 1px, transparent 1px), linear-gradient(to bottom, ${major} 1px, transparent 1px),` +
      `linear-gradient(to right, ${minor} 1px, transparent 1px), linear-gradient(to bottom, ${minor} 1px, transparent 1px)`;
    part.style.backgroundSize = `${size * 8}px ${size * 8}px, ${size * 8}px ${size * 8}px, ${size}px ${size}px, ${size}px ${size}px`;
  }

  // Outline
  function applyOutline(on) {
    toolStyle('outline', on ? `#${ROOT_ID} * { outline: 1px solid rgba(255, 71, 133, 0.55) !important; outline-offset: -1px !important; }
      #${ROOT_ID} * * { outline-color: rgba(30, 167, 253, 0.55) !important; }
      #${ROOT_ID} * * * { outline-color: rgba(102, 191, 60, 0.6) !important; }
      #${ROOT_ID} * * * * { outline-color: rgba(255, 174, 0, 0.6) !important; }` : '');
  }

  // Measure: hover an element to see its size, padding and margin.
  let measureHandler = null;
  function applyMeasure(on) {
    if (!on) {
      if (measureHandler) { document.removeEventListener('mousemove', measureHandler, true); measureHandler = null; }
      removePart('measure');
      return;
    }
    if (measureHandler) return;
    measureHandler = (e) => {
      const el = document.elementFromPoint(e.clientX, e.clientY);
      const part = toolPart('measure', true);
      part.textContent = '';
      if (!el || isToolNode(el) || el === document.documentElement || el === document.body) return;
      const r = el.getBoundingClientRect();
      const cs = getComputedStyle(el);
      const n = (v) => parseFloat(v) || 0;
      const m = { t: n(cs.marginTop), r: n(cs.marginRight), b: n(cs.marginBottom), l: n(cs.marginLeft) };
      const p = { t: n(cs.paddingTop), r: n(cs.paddingRight), b: n(cs.paddingBottom), l: n(cs.paddingLeft) };
      const bw = { t: n(cs.borderTopWidth), r: n(cs.borderRightWidth), b: n(cs.borderBottomWidth), l: n(cs.borderLeftWidth) };
      box(part, { left: r.left - m.l, top: r.top - m.t, width: r.width + m.l + m.r, height: r.height + m.t + m.b }, 'background:rgba(246,178,107,.55);');
      box(part, r, 'background:rgba(255,229,153,.7);');
      box(part, { left: r.left + bw.l, top: r.top + bw.t, width: r.width - bw.l - bw.r, height: r.height - bw.t - bw.b }, 'background:rgba(147,196,125,.6);');
      const content = { left: r.left + bw.l + p.l, top: r.top + bw.t + p.t, width: r.width - bw.l - bw.r - p.l - p.r, height: r.height - bw.t - bw.b - p.t - p.b };
      const fmt = (v) => (Math.round(v * 100) / 100).toString();
      box(part, content, 'background:rgba(111,168,220,.7);',
        `${el.tagName.toLowerCase()} ${fmt(r.width)} × ${fmt(r.height)}` +
        (p.t || p.r || p.b || p.l ? ` · padding ${fmt(p.t)} ${fmt(p.r)} ${fmt(p.b)} ${fmt(p.l)}` : '') +
        (m.t || m.r || m.b || m.l ? ` · margin ${fmt(m.t)} ${fmt(m.r)} ${fmt(m.b)} ${fmt(m.l)}` : ''));
    };
    document.addEventListener('mousemove', measureHandler, true);
  }

  // Pseudo-states: stylesheet rules with :hover, :focus, ... are copied with the pseudo-class replaced, so the state can be
  // forced on every element (attribute on <html>) or on chosen elements (attribute on the element).
  const PSEUDO = { hover: ':hover', focus: ':focus', 'focus-visible': ':focus-visible', 'focus-within': ':focus-within', active: ':active' };
  const PSEUDO_RE = /:(hover|focus-visible|focus-within|focus|active)(?![\w-])/g;
  const pseudoKey = (cls) => Object.keys(PSEUDO).find((k) => PSEUDO[k] === cls) || cls.replace(/^:/, '');

  function splitSelectors(text) {
    const parts = [];
    let depth = 0, start = 0;
    for (let i = 0; i < text.length; i++) {
      const c = text[i];
      if (c === '(' || c === '[') depth++;
      else if (c === ')' || c === ']') depth--;
      else if (c === ',' && depth === 0) { parts.push(text.slice(start, i)); start = i + 1; }
    }
    parts.push(text.slice(start));
    return parts.map((p) => p.trim()).filter(Boolean);
  }

  function rewriteRules(rules, out) {
    for (const rule of Array.from(rules)) {
      if (rule.selectorText !== undefined && rule.style) {
        const forced = [];
        for (const sel of splitSelectors(rule.selectorText)) {
          const states = [...sel.matchAll(PSEUDO_RE)].map((m) => m[1]);
          if (!states.length) continue;
          const all = states.map((s) => `[data-wb-pseudo-all~="${s}"]`).join('');
          const stripped = sel.replace(PSEUDO_RE, '').trim() || '*';
          forced.push(/^(html|:root)\b/.test(stripped) ? stripped.replace(/^(html|:root)/, `html${all}`) : `html${all} ${stripped}`);
          forced.push(sel.replace(PSEUDO_RE, (_, s) => `[data-wb-pseudo~="${s}"]`));
        }
        if (forced.length) out.push(`${forced.join(', ')} { ${rule.style.cssText} }`);
      } else if (rule.cssRules && rule.conditionText !== undefined) {
        const inner = [];
        rewriteRules(rule.cssRules, inner);
        const at = rule.constructor && rule.constructor.name === 'CSSSupportsRule' ? '@supports' : rule.constructor && rule.constructor.name === 'CSSContainerRule' ? '@container' : '@media';
        if (inner.length) out.push(`${at} ${rule.conditionText} { ${inner.join('\n')} }`);
      } else if (rule.cssRules && rule.name !== undefined && rule.constructor && rule.constructor.name === 'CSSLayerBlockRule') {
        const inner = [];
        rewriteRules(rule.cssRules, inner);
        if (inner.length) out.push(`@layer ${rule.name} { ${inner.join('\n')} }`);
      }
    }
  }

  function buildPseudoStyles() {
    const out = [];
    for (const sheet of Array.from(document.styleSheets)) {
      if (sheet.ownerNode && sheet.ownerNode.hasAttribute && sheet.ownerNode.hasAttribute(TOOL_ATTR)) continue;
      try { rewriteRules(sheet.cssRules, out); } catch { /* cross-origin stylesheet */ }
    }
    toolStyle('pseudo', out.join('\n'));
  }

  let pseudoObserver = null;
  function applyPseudo() {
    const all = (tools.current.pseudo || []).filter((s) => PSEUDO[s]);
    const targeted = tools.declaredPseudo.filter((p) => p.selector);
    all.push(...tools.declaredPseudo.filter((p) => !p.selector).map((p) => pseudoKey(p.pseudoClass)));
    const html = document.documentElement;
    if (all.length) html.setAttribute('data-wb-pseudo-all', Array.from(new Set(all)).join(' ')); else html.removeAttribute('data-wb-pseudo-all');
    if (!all.length && !targeted.length) { toolStyle('pseudo', ''); if (pseudoObserver) { pseudoObserver.disconnect(); pseudoObserver = null; } return; }
    buildPseudoStyles();
    const mark = () => {
      for (const p of targeted) {
        const key = pseudoKey(p.pseudoClass);
        let matches = [];
        try { matches = Array.from(document.querySelectorAll(p.selector)); } catch { continue; }
        for (const el of matches) {
          const current = (el.getAttribute('data-wb-pseudo') || '').split(' ').filter(Boolean);
          if (!current.includes(key)) el.setAttribute('data-wb-pseudo', [...current, key].join(' '));
        }
      }
    };
    mark();
    if (targeted.length && !pseudoObserver && document.body) {
      // Elements re-created by Blazor lose the attribute; mark new ones as they appear.
      pseudoObserver = new MutationObserver(mark);
      pseudoObserver.observe(document.body, { childList: true, subtree: true });
    }
  }

  // Text scale (WCAG 1.4.4) and text spacing (WCAG 1.4.12)
  function applyTextScale(percent) {
    toolStyle('text-scale', '');
    const scale = Number(percent) || 100;
    if (scale === 100) return;
    const base = parseFloat(getComputedStyle(document.documentElement).fontSize) || 16;
    toolStyle('text-scale', `html { font-size: ${(base * scale) / 100}px !important; }`);
  }

  function applyTextSpacing(on) {
    toolStyle('text-spacing', on ? `#${ROOT_ID} * { line-height: 1.5 !important; letter-spacing: 0.12em !important; word-spacing: 0.16em !important; }
      #${ROOT_ID} p { margin-bottom: 2em !important; }` : '');
  }

  // Tab order: a numbered badge on every element reachable with Tab, in order.
  let tabTimer = 0;
  function drawTabOrder() {
    const part = toolPart('tab-order', true);
    part.textContent = '';
    const list = tabbables();
    const svg = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
    svg.setAttribute('width', '100%');
    svg.setAttribute('height', '100%');
    svg.style.cssText = 'position:absolute;inset:0;';
    const points = [];
    list.forEach((el, i) => {
      const r = el.getBoundingClientRect();
      if (!r.width && !r.height) return;
      box(part, r, 'border:2px solid #7c3aed;border-radius:3px;');
      const badge = document.createElement('span');
      badge.textContent = String(i + 1);
      badge.style.cssText = `position:absolute;left:${Math.max(0, r.left - 10)}px;top:${Math.max(0, r.top - 10)}px;min-width:20px;height:20px;padding:0 4px;box-sizing:border-box;border-radius:10px;background:#7c3aed;color:#fff;font:600 11px/20px system-ui,sans-serif;text-align:center;`;
      part.appendChild(badge);
      points.push(`${r.left + r.width / 2},${r.top + r.height / 2}`);
    });
    if (points.length > 1) {
      const line = document.createElementNS('http://www.w3.org/2000/svg', 'polyline');
      line.setAttribute('points', points.join(' '));
      line.setAttribute('fill', 'none');
      line.setAttribute('stroke', 'rgba(124,58,237,.55)');
      line.setAttribute('stroke-width', '2');
      line.setAttribute('stroke-dasharray', '4 3');
      svg.appendChild(line);
      part.insertBefore(svg, part.firstChild);
    }
  }
  function scheduleTabOrder() { cancelAnimationFrame(tabTimer); tabTimer = requestAnimationFrame(drawTabOrder); }
  function applyTabOrder(on) {
    window.removeEventListener('scroll', scheduleTabOrder, true);
    window.removeEventListener('resize', scheduleTabOrder);
    if (!on) { removePart('tab-order'); return; }
    window.addEventListener('scroll', scheduleTabOrder, true);
    window.addEventListener('resize', scheduleTabOrder);
    scheduleTabOrder();
  }

  // Highlight DOM updates: changed elements flash, which exposes unexpected re-rendering.
  let updatesObserver = null;
  function applyUpdates(on) {
    if (!on) { if (updatesObserver) { updatesObserver.disconnect(); updatesObserver = null; } removePart('updates'); return; }
    if (updatesObserver || !root()) return;
    toolStyle('updates-keyframes', '@keyframes wb-flash { from { opacity: 1; } to { opacity: 0; } }');
    updatesObserver = new MutationObserver((records) => {
      const part = toolPart('updates', true);
      const seen = new Set();
      for (const record of records) {
        const el = record.target.nodeType === 1 ? record.target : record.target.parentElement;
        if (!el || seen.has(el) || isToolNode(el) || seen.size > 40) continue;
        seen.add(el);
        const flash = box(part, el.getBoundingClientRect(), 'border:2px solid #f97316;background:rgba(249,115,22,.12);animation:wb-flash .9s ease-out forwards;');
        setTimeout(() => flash.remove(), 950);
      }
    });
    updatesObserver.observe(root(), { subtree: true, childList: true, attributes: true, characterData: true });
  }

  // Design overlay: a mockup over the component, for pixel comparison.
  function applyOverlay(o) {
    if (!o || !o.url || !safeUrl(o.url)) { removePart('overlay'); return; }
    const part = toolPart('overlay', true);
    let img = part.querySelector('img');
    if (!img) { img = document.createElement('img'); img.alt = ''; part.appendChild(img); }
    if (img.getAttribute('src') !== o.url) img.setAttribute('src', o.url);
    const target = (root() && root().firstElementChild) || root() || document.body;
    const r = target.getBoundingClientRect();
    img.style.cssText = `position:absolute;left:${r.left}px;top:${r.top}px;max-width:none;opacity:${(o.opacity == null ? 50 : o.opacity) / 100};mix-blend-mode:${o.blend === 'difference' ? 'difference' : 'normal'};`;
  }

  // Highlight elements (accessibility findings, interaction targets).
  let highlightTimer = 0;
  function highlight(selectorOrElements, label, keep) {
    clearTimeout(highlightTimer);
    const part = toolPart('highlight', true);
    part.textContent = '';
    if (!selectorOrElements) return 0;
    let elements = [];
    if (typeof selectorOrElements === 'string') { try { elements = Array.from(document.querySelectorAll(selectorOrElements)); } catch { elements = []; } }
    else elements = selectorOrElements;
    elements = elements.filter((el) => el && !isToolNode(el));
    if (elements[0]) elements[0].scrollIntoView({ block: 'nearest', inline: 'nearest' });
    requestAnimationFrame(() => {
      elements.forEach((el, i) => box(part, el.getBoundingClientRect(), 'border:2px dashed #e11d48;background:rgba(225,29,72,.10);', i === 0 ? label : null));
    });
    if (!keep) highlightTimer = setTimeout(() => { part.textContent = ''; }, 3000);
    return elements.length;
  }

  function applyTools(t) {
    tools.current = Object.assign({}, t || {});
    const c = tools.current;
    applyBackground(c.bg);
    applyGrid(!!c.grid);
    applyOutline(!!c.outline);
    applyMeasure(!!c.measure);
    applyPseudo();
    applyTextScale(c.text);
    applyTextSpacing(!!c.spacing);
    applyTabOrder(!!c.tabs);
    applyUpdates(!!c.updates);
    applyOverlay(c.overlay);
  }

  function toolsFromQuery() {
    const q = new URLSearchParams(location.search);
    const t = {};
    if (q.get('bg')) t.bg = q.get('bg');
    for (const flag of ['grid', 'outline', 'measure', 'spacing', 'tabs', 'updates']) if (q.get(flag) === '1') t[flag] = true;
    if (q.get('pseudo')) t.pseudo = q.get('pseudo').split(',').filter(Boolean);
    if (q.get('text')) t.text = Number(q.get('text'));
    if (q.get('overlay')) t.overlay = { url: q.get('overlay'), opacity: Number(q.get('overlayOpacity') || 50), blend: q.get('overlayBlend') || 'normal' };
    return t;
  }

  // Redraw position-dependent tools when the layout changes.
  window.addEventListener('resize', () => { if (tools.current.overlay) applyOverlay(tools.current.overlay); });

  // ---------- browser storage ----------
  const SHELL_KEY_PREFIX = 'blazor-workbench:';
  const seeded = new Set();
  function storageArea(name) { try { return name === 'session' ? window.sessionStorage : window.localStorage; } catch { return null; } }

  function isolateAndSeed(config) {
    if (!config) return;
    if (config.isolateStorage) {
      for (const name of ['local', 'session']) {
        const area = storageArea(name);
        if (!area) continue;
        try {
          for (let i = area.length - 1; i >= 0; i--) {
            const k = area.key(i);
            if (k && !k.startsWith(SHELL_KEY_PREFIX)) area.removeItem(k);
          }
        } catch { /* storage unavailable */ }
      }
    }
    seedStorage(config.storage);
  }

  function seedStorage(entries) {
    for (const e of entries || []) {
      const id = e.area + ':' + e.key;
      if (seeded.has(id)) continue;
      seeded.add(id);
      try { storageArea(e.area).setItem(e.key, e.value); } catch { /* storage unavailable */ }
    }
  }

  function storageSnapshot() {
    const read = (name) => {
      const area = storageArea(name);
      const items = [];
      try {
        for (let i = 0; i < area.length; i++) {
          const k = area.key(i);
          if (k && !k.startsWith(SHELL_KEY_PREFIX)) items.push({ key: k, value: redact(area.getItem(k) || '') });
        }
      } catch { /* storage unavailable */ }
      return items.sort((a, b) => a.key.localeCompare(b.key));
    };
    return { local: read('local'), session: read('session'), cookies: redact(document.cookie || '') };
  }

  try {
    const configElement = document.getElementById('workbench-preview-config');
    if (configElement) isolateAndSeed(JSON.parse(configElement.textContent || '{}'));
  } catch { /* malformed config: previews still render */ }

  // ---------- rendered HTML ----------
  const VOID = new Set(['area', 'base', 'br', 'col', 'embed', 'hr', 'img', 'input', 'link', 'meta', 'source', 'track', 'wbr']);
  function formatHtml(rootElement) {
    const lines = [];
    const escape = (s) => s.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
    const attrs = (el) => Array.from(el.attributes)
      .filter((a) => !/^_bl_/.test(a.name) && !/^b-[a-z0-9]{10}$/.test(a.name) && !/^data-wb-/.test(a.name))
      .map((a) => a.value === '' ? ` ${a.name}` : ` ${a.name}="${escape(a.value).replace(/"/g, '&quot;')}"`).join('');
    const walk = (node, depth) => {
      const pad = '  '.repeat(depth);
      if (node.nodeType === 3) { const text = node.textContent.replace(/\s+/g, ' ').trim(); if (text) lines.push(pad + escape(text)); return; }
      if (node.nodeType !== 1 || isToolNode(node)) return;
      const tag = node.tagName.toLowerCase();
      if (tag === 'script' || tag === 'style') return;
      const open = `<${tag}${attrs(node)}>`;
      if (VOID.has(tag)) { lines.push(pad + open); return; }
      const children = Array.from(node.childNodes).filter((c) => c.nodeType === 1 || (c.nodeType === 3 && c.textContent.trim()));
      if (children.length === 0) { lines.push(`${pad}${open}</${tag}>`); return; }
      if (children.length === 1 && children[0].nodeType === 3 && children[0].textContent.trim().length <= 80) {
        lines.push(`${pad}${open}${escape(children[0].textContent.replace(/\s+/g, ' ').trim())}</${tag}>`);
        return;
      }
      lines.push(pad + open);
      children.forEach((c) => walk(c, depth + 1));
      lines.push(`${pad}</${tag}>`);
    };
    Array.from(rootElement.childNodes).forEach((c) => walk(c, 0));
    return redact(lines.join('\n'));
  }

  // ---------- interaction recorder ----------
  let recorder = null;
  const RECORD_ROLES = ['button', 'link', 'checkbox', 'radio', 'tab', 'menuitem', 'option', 'switch', 'combobox', 'textbox', 'searchbox', 'slider', 'spinbutton', 'listbox', 'treeitem'];

  function cssPath(el) {
    const parts = [];
    for (let e = el; e && e.nodeType === 1 && e.id !== ROOT_ID && parts.length < 5; e = e.parentElement) {
      if (e.id) { parts.unshift('#' + cssEscape(e.id)); break; }
      let part = e.tagName.toLowerCase();
      const cls = Array.from(e.classList).filter((c) => !/^b-|^wb-/.test(c)).slice(0, 2);
      if (cls.length) part += '.' + cls.map(cssEscape).join('.');
      const siblings = e.parentElement ? Array.from(e.parentElement.children).filter((s) => s.tagName === e.tagName) : [];
      if (siblings.length > 1) part += `:nth-of-type(${siblings.indexOf(e) + 1})`;
      parts.unshift(part);
    }
    return parts.join(' > ');
  }

  function unique(locator, el) { try { const m = resolve(locator); return m.length === 1 && m[0] === el; } catch { return false; } }

  function locatorFor(el) {
    const testId = el.getAttribute('data-testid');
    if (testId) return { kind: 'TestId', value: testId, exact: true };
    const role = roleOf(el);
    const name = accessibleName(el);
    if (role && name && RECORD_ROLES.includes(role)) {
      for (const exact of [false, true]) { const l = { kind: 'Role', value: role, name, exact }; if (unique(l, el)) return l; }
    }
    if (['INPUT', 'TEXTAREA', 'SELECT'].includes(el.tagName)) {
      const label = el.labels && el.labels.length ? normalize(el.labels[0].textContent) : el.getAttribute('aria-label');
      if (label) { const l = { kind: 'Label', value: normalize(label), exact: false }; if (unique(l, el)) return l; }
      const ph = el.getAttribute('placeholder');
      if (ph) { const l = { kind: 'Placeholder', value: ph, exact: false }; if (unique(l, el)) return l; }
    }
    const text = normalize(el.textContent);
    if (text && text.length <= 60) { const l = { kind: 'Text', value: text, exact: false }; if (unique(l, el)) return l; }
    return { kind: 'Css', value: cssPath(el), exact: true };
  }

  const isTextField = (el) => (el.tagName === 'INPUT' && !['checkbox', 'radio', 'button', 'submit', 'reset', 'file', 'range', 'color'].includes((el.type || '').toLowerCase())) || el.tagName === 'TEXTAREA' || el.isContentEditable;

  function emitRecorded(step) { post('recorded-step', step); }

  function startRecording() {
    if (recorder) return;
    const onClick = (e) => {
      if (!e.isTrusted || isToolNode(e.target) || !root() || !root().contains(e.target)) return;
      const target = e.target.closest('button,a[href],summary,[role="button"],[role="link"],[role="tab"],[role="menuitem"],[role="option"],[role="switch"],[role="treeitem"],label') || e.target;
      if (target.tagName === 'SELECT' || isTextField(target) || (target.tagName === 'INPUT' && ['checkbox', 'radio'].includes(target.type))) return;
      if (target.tagName === 'LABEL' && target.control) return; // the control's change event is recorded instead
      emitRecorded({ action: 'Click', target: locatorFor(target) });
    };
    const onChange = (e) => {
      const el = e.target;
      if (!e.isTrusted || isToolNode(el) || !root() || !root().contains(el)) return;
      if (el.tagName === 'SELECT') {
        const option = el.options[el.selectedIndex];
        emitRecorded({ action: 'Select', target: locatorFor(el), value: option ? normalize(option.textContent) : el.value });
      } else if (el.tagName === 'INPUT' && (el.type === 'checkbox' || el.type === 'radio')) {
        emitRecorded({ action: el.checked ? 'Check' : 'Uncheck', target: locatorFor(el) });
      } else if (isTextField(el)) {
        emitRecorded({ action: 'Fill', target: locatorFor(el), value: el.value });
      }
    };
    const onKey = (e) => {
      if (!e.isTrusted || isToolNode(e.target)) return;
      const keys = ['Enter', 'Escape', 'Tab', 'ArrowUp', 'ArrowDown', 'ArrowLeft', 'ArrowRight', 'Home', 'End', 'PageUp', 'PageDown', ' '];
      if (!keys.includes(e.key) || (e.key === ' ' && isTextField(e.target))) return;
      if (e.key === 'Enter' && isTextField(e.target) && e.target.tagName === 'TEXTAREA') return;
      if (e.key === 'Tab') { emitRecorded({ action: 'Press', value: e.shiftKey ? 'Shift+Tab' : 'Tab' }); return; }
      if (e.key === 'Enter' && isTextField(e.target)) emitRecorded({ action: 'Fill', target: locatorFor(e.target), value: e.target.value });
      const mods = [e.ctrlKey && 'Control', e.altKey && 'Alt', e.shiftKey && 'Shift', e.metaKey && 'Meta'].filter(Boolean);
      emitRecorded({ action: 'Press', target: e.target && e.target !== document.body ? locatorFor(e.target) : undefined, value: [...mods, e.key === ' ' ? 'Space' : e.key].join('+') });
    };
    // Assertions: with the assert mode on, a click adds an expectation instead of clicking.
    const onAssert = (e) => {
      if (!recorder || !recorder.assert || isToolNode(e.target) || !root() || !root().contains(e.target)) return;
      e.preventDefault();
      e.stopPropagation();
      const el = e.target;
      const text = normalize(el.textContent);
      if (['INPUT', 'TEXTAREA', 'SELECT'].includes(el.tagName)) emitRecorded({ action: 'ExpectValue', target: locatorFor(el), expected: el.value });
      else if (text && text.length <= 120) emitRecorded({ action: 'ExpectText', target: locatorFor(el), expected: text });
      else emitRecorded({ action: 'ExpectVisible', target: locatorFor(el) });
      highlight([el], 'Assertion added');
    };
    recorder = { onClick, onChange, onKey, onAssert, assert: false };
    document.addEventListener('click', onAssert, true);
    document.addEventListener('click', onClick, false);
    document.addEventListener('change', onChange, true);
    document.addEventListener('keydown', onKey, true);
  }

  function stopRecording() {
    if (!recorder) return;
    document.removeEventListener('click', recorder.onAssert, true);
    document.removeEventListener('click', recorder.onClick, false);
    document.removeEventListener('change', recorder.onChange, true);
    document.removeEventListener('keydown', recorder.onKey, true);
    recorder = null;
  }

  // ---------- design tokens ----------
  // Custom properties declared in the preview stylesheets that resolve on the root element, with their computed values.
  function collectTokens() {
    const names = new Set();
    const visit = (rules) => {
      for (const rule of Array.from(rules)) {
        if (rule.style) for (let i = 0; i < rule.style.length; i++) { const p = rule.style[i]; if (p.startsWith('--')) names.add(p); }
        if (rule.cssRules) visit(rule.cssRules);
      }
    };
    for (const sheet of Array.from(document.styleSheets)) {
      if (sheet.ownerNode && sheet.ownerNode.hasAttribute && sheet.ownerNode.hasAttribute(TOOL_ATTR)) continue;
      try { visit(sheet.cssRules); } catch { /* cross-origin stylesheet */ }
    }
    const cs = getComputedStyle(document.documentElement);
    return Array.from(names).map((name) => ({ name, value: cs.getPropertyValue(name).trim() }))
      .filter((t) => t.value.length > 0 && !t.name.startsWith('--wb-'))
      .sort((a, b) => a.name.localeCompare(b.name));
  }

  // ---------- shell messages ----------
  window.addEventListener('message', async (e) => {
    if (e.origin !== location.origin || !e.data || e.data.source !== SHELL) return;
    const { type, payload } = e.data;
    try {
      switch (type) {
        case 'args': {
          if (!state.dotnet) return;
          const errors = JSON.parse(await state.dotnet.invokeMethodAsync('ApplyArgs', JSON.stringify(payload || {})));
          post('arg-errors', errors);
          break;
        }
        case 'theme': applyTheme(payload); break;
        case 'dir': applyDir(payload); break;
        case 'gate':
          if (state.dotnet) await state.dotnet.invokeMethodAsync(payload.action === 'fail' ? 'FailGate' : 'ReleaseGate', payload.name, ...(payload.action === 'fail' ? [payload.message || null] : []));
          break;
        case 'clock':
          if (state.dotnet) await state.dotnet.invokeMethodAsync('AdvanceClock', payload);
          break;
        case 'a11y': await runAxe(payload); break;
        case 'interaction': await runInteraction(payload.test || payload, payload.test ? payload : null); break;
        case 'interaction-continue': if (debug && debug.resume) { const r = debug.resume; debug.resume = null; r(payload || 'step'); } break;
        case 'tools': applyTools(payload); break;
        case 'highlight': highlight(payload && payload.selector, payload && payload.label); break;
        case 'html': { const r = root(); post('html', { html: r ? formatHtml(r) : '' }); break; }
        case 'storage': {
          const area = payload && payload.area ? storageArea(payload.area) : null;
          try {
            if (payload && payload.op === 'set' && area) area.setItem(payload.key, payload.value);
            else if (payload && payload.op === 'remove' && area) area.removeItem(payload.key);
            else if (payload && payload.op === 'clear') isolateAndSeed({ isolateStorage: true });
          } catch (err) { addDiagnostic({ source: 'JsInterop', severity: 'Warning', message: 'Storage change failed: ' + stringify(err) }); }
          post('storage', storageSnapshot());
          break;
        }
        case 'record':
          if (payload && payload.on) { startRecording(); if (recorder) recorder.assert = !!payload.assert; } else stopRecording();
          break;
        case 'tokens': post('tokens', collectTokens()); break;
        case 'session':
          if (state.dotnet) post('session', JSON.parse(await state.dotnet.invokeMethodAsync('GetSessionInfo')));
          break;
        case 'hello':
          post('hello', { state: state.state, diagnostics: state.diagnostics, callbacks: state.callbacks, gates: state.gates, metrics: metrics() });
          break;
      }
    } catch (err) {
      addDiagnostic({ source: 'JsInterop', severity: 'Error', message: `Preview bridge failed to handle '${type}': ${stringify(err)}` });
      if (type === 'a11y') post('a11y', { error: String(err && err.message || err) });
    }
  });

  // ---------- API used by ScenarioHost (.NET) ----------
  const api = {
    attach(dotnet, info) {
      state.dotnet = dotnet;
      state.scenarioId = info.scenarioId;
      state.sessionId = info.sessionId || null;
      state.diagnostics.forEach((d) => { if (!d.sessionId) d.sessionId = state.sessionId; });
      state.readySelector = info.readySelector || null;
      state.pending = info.pending || 0;
      applyTheme(info);
      applyDir(info.dir);
      if (info.lang) document.documentElement.setAttribute('lang', info.lang);
      tools.gridSize = info.gridSize || 8;
      tools.declaredPseudo = info.pseudoStates || [];
      if (info.storage && info.storage.length && !document.getElementById('workbench-preview-config')) isolateAndSeed({ isolateStorage: info.isolateStorage, storage: info.storage });
      applyTools(Object.assign(toolsFromQuery(), tools.current));
      (info.diagnostics || []).forEach((d) => { if (!state.diagnostics.some((x) => x.timestamp === d.timestamp && x.message === d.message)) addDiagnostic(d); });
      if (info.argErrors && Object.keys(info.argErrors).length) post('arg-errors', info.argErrors);
      state.attached = true;
      post('attached', { scenarioId: info.scenarioId });
      if (info.error) api.setError(info.error); else scheduleCheck(0);
    },
    setPending(n) {
      state.pending = n;
      // Completing work counts as activity: require a fresh quiet window before declaring readiness.
      state.lastMutation = performance.now();
      if (n === 0) scheduleCheck();
      post('pending', n);
    },
    setError(message) {
      state.error = message;
      setState('error', message);
    },
    diagnostic(d) { addDiagnostic(d); },
    callback(entry) {
      state.callbacks.push(entry);
      if (state.callbacks.length > MAX_DIAGNOSTICS) state.callbacks.shift();
      state.callbackCounts[entry.name] = entry.invocation;
      post('callback', entry);
    },
    gates(list) { state.gates = list; post('gates', list); },
    argsChanged(json) { post('args-changed', JSON.parse(json)); },
    theme(t) { applyTheme(t); },
    tokens: collectTokens,
  };
  window.BlazorWorkbenchPreview = api;

  // Automation surface (stable; documented in docs/testing.md).
  window.__workbench = {
    get state() { return state.state; },
    get ready() { return state.state === 'ready'; },
    get scenarioId() { return state.scenarioId; },
    get diagnostics() { return state.diagnostics.slice(); },
    get callbacks() { return state.callbacks.slice(); },
    get gates() { return state.gates.slice(); },
    get sessionId() { return state.sessionId; },
    metrics,
    serverMetrics: () => state.dotnet ? state.dotnet.invokeMethodAsync('GetMetrics').then(JSON.parse) : Promise.resolve(null),
    waitForIdle,
    callbackCount: (name) => state.callbackCounts[name] || 0,
    releaseGate: (name) => state.dotnet ? state.dotnet.invokeMethodAsync('ReleaseGate', name) : Promise.resolve(false),
    failGate: (name, message) => state.dotnet ? state.dotnet.invokeMethodAsync('FailGate', name, message || null) : Promise.resolve(false),
    advanceClock: (ms) => state.dotnet ? state.dotnet.invokeMethodAsync('AdvanceClock', ms) : Promise.resolve(false),
    runInteraction,
    runAxe,
    applyTools,
    highlight,
    html: () => (root() ? formatHtml(root()) : ''),
    storage: storageSnapshot,
    tokens: collectTokens,
  };
})();
