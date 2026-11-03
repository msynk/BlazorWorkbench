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
    [/((?:password|passwd|pwd|secret|api[_-]?key|access[_-]?token|refresh[_-]?token)"?\s*[:=]\s*"?)[^\s"&;,}]+/gi, '$1[REDACTED]'],
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

  const observer = new MutationObserver(() => {
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

  function all(selector) { return Array.from(document.body.querySelectorAll(selector)); }

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

  async function runInteraction(test) {
    const results = [];
    let failed = false;
    for (let i = 0; i < test.steps.length; i++) {
      const step = test.steps[i];
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
    const outcome = { name: test.name, passed: !failed, results };
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
        case 'interaction': await runInteraction(payload); break;
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
  };
})();
