// Blazor Workbench shell module: bridges the preview iframe (postMessage) and the shell component (.NET).
const SHELL = 'blazor-workbench-shell';
const PREVIEW = 'blazor-workbench-preview';

const MIN_SIZE = 120;
const MAX_SIZE = 3840;

let dotnet = null;
let listener = null;
let keyListener = null;
let pointerListener = null;

export function init(dotnetRef) {
  dispose();
  dotnet = dotnetRef;
  listener = (e) => {
    if (e.origin !== location.origin || !e.data || e.data.source !== PREVIEW) return;
    const frame = document.getElementById('wb-preview-frame');
    const fromCanvas = frame && e.source === frame.contentWindow;
    // Compare frames load the same stylesheets: their breakpoints fill the Breakpoints axis. Nothing else is taken from them.
    const fromCompare = !fromCanvas && e.data.type === 'breakpoints' &&
      Array.from(document.querySelectorAll('.wb-compare iframe')).some((f) => f.contentWindow === e.source);
    if (!fromCanvas && !fromCompare) return;
    dotnet.invokeMethodAsync('OnPreviewMessage', JSON.stringify(e.data)).catch(() => { /* circuit closing */ });
  };
  window.addEventListener('message', listener);

  pointerListener = (e) => startResize(e);
  document.addEventListener('pointerdown', pointerListener);

  keyListener = (e) => {
    const tag = (e.target && e.target.tagName) || '';
    const editing = ['INPUT', 'TEXTAREA', 'SELECT'].includes(tag) || (e.target && e.target.isContentEditable);
    if (!e.altKey && !e.ctrlKey && !e.metaKey && e.target && e.target.classList && e.target.classList.contains('wb-resize') && resizeByKey(e)) {
      return;
    }
    if ((e.key === '/' && !editing) || ((e.ctrlKey || e.metaKey) && !e.altKey && (e.key === 'k' || e.key === 'K'))) {
      e.preventDefault();
      const search = document.getElementById('wb-search');
      if (search) { search.focus(); search.select(); }
    } else if (e.key === 'Escape' && tag === 'INPUT' && e.target.id === 'wb-search') {
      e.target.blur();
    } else if (!editing && !e.ctrlKey && !e.metaKey && !e.altKey && (e.key === 'r' || e.key === 'R')) {
      dotnet.invokeMethodAsync('OnShortcut', e.shiftKey ? 'reset' : 'reload');
    } else if (!editing && !e.ctrlKey && !e.metaKey) {
      const action = shortcutFor(e);
      if (action) {
        e.preventDefault();
        dotnet.invokeMethodAsync('OnShortcut', action);
      }
    }
  };
  document.addEventListener('keydown', keyListener);
  document.documentElement.setAttribute('data-workbench-shell', 'ready');
}

// Storybook-like keys: Alt+arrows move through the catalog, Alt+F/D change the layout, single letters toggle tools.
function shortcutFor(e) {
  if (e.altKey) {
    switch (e.code) {
      case 'ArrowLeft': return 'previous-scenario';
      case 'ArrowRight': return 'next-scenario';
      case 'ArrowUp': return 'previous-component';
      case 'ArrowDown': return 'next-component';
      case 'KeyF': return 'focus';
      case 'KeyD': return 'panel';
      case 'Equal': case 'NumpadAdd': return 'zoom-in';
      case 'Minus': case 'NumpadSubtract': return 'zoom-out';
      case 'Digit0': case 'Numpad0': return 'zoom-reset';
      case 'Digit9': case 'Numpad9': return 'zoom-fit';
      default: return null;
    }
  }
  if (e.shiftKey && e.key !== '?') return null;
  switch (e.key) {
    case '?': return 'shortcuts';
    case 'Escape': return 'escape';
    case 'g': case 'G': return 'grid';
    case 'o': case 'O': return 'outline';
    case 'm': case 'M': return 'measure';
    case 'b': case 'B': return 'pin';
    default: return null;
  }
}

// ---------- device mode: resizing the preview ----------
// Dragging runs here, not in .NET: the frame follows the pointer at full frame rate and .NET hears about the final size
// once (OnViewportResized), which also puts it in the URL. The preview is centered, so its right edge moves half as fast
// as its width; the width is corrected until the edge sits under the pointer, whether centered or not.
const clamp = (v) => Math.min(MAX_SIZE, Math.max(MIN_SIZE, Math.round(v)));

function startResize(e) {
  const handle = e.target && e.target.closest && e.target.closest('.wb-resize');
  if (!handle || e.button !== 0 || !dotnet) return;
  const device = handle.closest('.wb-device');
  const box = device && device.querySelector('.wb-zoom');
  const frame = box && box.querySelector('iframe');
  if (!box || !frame) return;
  e.preventDefault();

  const axis = handle.dataset.resize || 'se';
  const zoom = parseFloat(device.dataset.zoom) || 1;
  const rect = box.getBoundingClientRect();
  const start = { y: e.clientY, w: rect.width / zoom, h: rect.height / zoom };
  const offset = { x: e.clientX - rect.right };
  const saved = { box: box.getAttribute('style'), frame: frame.getAttribute('style') };
  const size = { w: clamp(start.w), h: clamp(start.h) };
  let moved = false;

  const label = document.createElement('div');
  label.className = 'wb-size-label';
  label.setAttribute('aria-hidden', 'true');
  document.body.appendChild(label);

  const apply = (w, h) => {
    size.w = clamp(w);
    size.h = clamp(h);
    box.style.width = `${size.w * zoom}px`;
    box.style.height = `${size.h * zoom}px`;
    frame.style.width = `${size.w}px`;
    frame.style.height = `${size.h}px`;
    label.textContent = `${size.w} × ${size.h}`;
  };
  const centered = () => device.getBoundingClientRect().left - device.parentElement.getBoundingClientRect().left > 0.5;

  document.documentElement.classList.add('wb-resizing');
  device.classList.add('wb-device-resizing');
  apply(size.w, size.h);
  try { handle.setPointerCapture(e.pointerId); } catch { /* pointer already released */ }

  const move = (ev) => {
    moved = true;
    const h = axis === 'e' ? size.h : start.h + (ev.clientY - start.y) / zoom;
    let w = size.w;
    if (axis !== 's') {
      const target = ev.clientX - offset.x;
      for (let i = 0; i < 4; i++) {
        apply(w, h);
        const error = target - box.getBoundingClientRect().right;
        if (Math.abs(error) < 0.5) break;
        w = size.w + ((centered() ? 2 : 1) * error) / zoom;
      }
    }
    apply(w, h);
    label.style.left = `${ev.clientX + 14}px`;
    label.style.top = `${ev.clientY + 14}px`;
  };
  const end = () => {
    handle.removeEventListener('pointermove', move);
    handle.removeEventListener('pointerup', end);
    handle.removeEventListener('pointercancel', end);
    document.documentElement.classList.remove('wb-resizing');
    device.classList.remove('wb-device-resizing');
    label.remove();
    if (!moved) {
      restore(box, saved.box);
      restore(frame, saved.frame);
      return;
    }
    dotnet.invokeMethodAsync('OnViewportResized', size.w, size.h).catch(() => { /* circuit closing */ });
  };
  handle.addEventListener('pointermove', move);
  handle.addEventListener('pointerup', end);
  handle.addEventListener('pointercancel', end);
}

function restore(el, style) {
  if (style === null) el.removeAttribute('style'); else el.setAttribute('style', style);
}

// Arrow keys on a resize handle: 1 px, or 10 px with Shift. Sizes sent but not yet rendered are remembered briefly, so
// a held key keeps counting from the last size sent.
function resizeByKey(e) {
  const handle = e.target;
  const device = handle.closest('.wb-device');
  if (!device || !dotnet) return false;
  const axis = handle.dataset.resize;
  const step = e.shiftKey ? 10 : 1;
  const pending = device.wbPending && performance.now() - device.wbPending.at < 800 ? device.wbPending : null;
  let w = pending ? pending.w : Number(device.dataset.width);
  let h = pending ? pending.h : Number(device.dataset.height);
  if (!w || !h) return false;
  switch (e.key) {
    case 'ArrowLeft': if (axis === 's') return false; w -= step; break;
    case 'ArrowRight': if (axis === 's') return false; w += step; break;
    case 'ArrowUp': if (axis === 'e') return false; h -= step; break;
    case 'ArrowDown': if (axis === 'e') return false; h += step; break;
    default: return false;
  }
  e.preventDefault();
  device.wbPending = { w: clamp(w), h: clamp(h), at: performance.now() };
  dotnet.invokeMethodAsync('OnViewportResized', device.wbPending.w, device.wbPending.h).catch(() => { /* circuit closing */ });
  return true;
}

// The zoom at which a viewport of the given size fits the canvas (never above 100%).
export function fitZoom(width, height) {
  const area = document.querySelector('.wb-frame-area');
  const device = area && area.querySelector('.wb-device');
  if (!area || !device || !width || !height) return 1;
  const a = getComputedStyle(area);
  const d = getComputedStyle(device);
  const ruler = area.querySelector('.wb-ruler');
  const availableWidth = area.clientWidth - parseFloat(a.paddingLeft) - parseFloat(a.paddingRight) - parseFloat(d.paddingLeft) - parseFloat(d.paddingRight);
  const availableHeight = area.clientHeight - parseFloat(a.paddingTop) - parseFloat(a.paddingBottom) - parseFloat(d.paddingBottom) - (ruler ? ruler.offsetHeight + 4 : 0);
  const zoom = Math.min(availableWidth / width, availableHeight / height, 1);
  return Math.max(0.1, Math.floor(zoom * 100) / 100);
}

export function post(type, payload) {
  const frame = document.getElementById('wb-preview-frame');
  if (!frame || !frame.contentWindow) return false;
  frame.contentWindow.postMessage({ source: SHELL, type, payload }, location.origin);
  return true;
}

export function reloadFrame(src) {
  const frame = document.getElementById('wb-preview-frame');
  if (!frame) return;
  if (src) frame.src = src;
  else frame.contentWindow.location.reload();
}

export async function copy(text) {
  try {
    await navigator.clipboard.writeText(text);
    return true;
  } catch {
    const area = document.createElement('textarea');
    area.value = text;
    document.body.appendChild(area);
    area.select();
    const ok = document.execCommand('copy');
    area.remove();
    return ok;
  }
}

export function focusElement(id) {
  const el = document.getElementById(id);
  if (el) el.focus();
}

export function getPreference(key) {
  try { return localStorage.getItem('blazor-workbench:' + key); } catch { return null; }
}

// The Workbench theme when the user has not chosen one: the operating system's color scheme.
export function getSystemTheme() {
  try { return window.matchMedia && window.matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light'; } catch { return 'light'; }
}

export function setPreference(key, value) {
  try { localStorage.setItem('blazor-workbench:' + key, value); } catch { /* storage unavailable */ }
}

// ---------- test runner (Run tests in the sidebar) ----------
// Runs scenario checks in frames that are laid out but invisible: each job loads a fresh preview session, waits until it
// is ready and then checks the render, runs axe-core or runs one interaction test, like `workbench test` does with
// Playwright (which stays authoritative). Results go to .NET one by one.
const IMPACT = { minor: 1, moderate: 2, serious: 3, critical: 4 };
let testRun = null;
let runCounter = 0;

export function cancelTests() {
  if (testRun) testRun.cancelled = true;
  testRun = null;
  const host = document.getElementById('wb-test-runner');
  if (host) host.textContent = '';
}

// Starts a run without holding a JavaScript interop call open for its whole duration; .NET hears OnTestsFinished.
export function startTests(jobs, dotnetRef, options) {
  runTests(jobs, dotnetRef, options)
    .then((completed) => dotnetRef.invokeMethodAsync('OnTestsFinished', completed))
    .catch(() => { /* circuit closing */ });
}

export async function runTests(jobs, dotnetRef, options) {
  cancelTests();
  const run = { cancelled: false };
  testRun = run;
  let host = document.getElementById('wb-test-runner');
  if (!host) {
    host = document.createElement('div');
    host.id = 'wb-test-runner';
    host.setAttribute('aria-hidden', 'true');
    // Laid out inside the viewport (so intersection observers and focus behave) but invisible and inert.
    host.style.cssText = 'position:fixed;left:0;top:0;width:0;height:0;overflow:visible;opacity:0;pointer-events:none;z-index:-1;';
    document.body.appendChild(host);
  }
  const concurrency = Math.max(1, Math.min((options && options.concurrency) || 2, 4));
  let next = 0;
  const worker = async () => {
    const frame = document.createElement('iframe');
    frame.setAttribute('tabindex', '-1');
    frame.title = 'Workbench test runner';
    frame.style.cssText = 'position:absolute;left:0;top:0;width:1280px;height:800px;border:0;';
    host.appendChild(frame);
    try {
      while (!run.cancelled && next < jobs.length) {
        const job = jobs[next++];
        const result = await runJob(frame, job, run);
        if (run.cancelled) break;
        try { await dotnetRef.invokeMethodAsync('OnTestResult', JSON.stringify(result)); } catch { /* circuit closing */ }
      }
    } finally {
      frame.remove();
    }
  };
  await Promise.all(Array.from({ length: Math.min(concurrency, Math.max(1, jobs.length)) }, worker));
  if (testRun === run) testRun = null;
  return !run.cancelled;
}

function waitForLoad(frame, url) {
  return new Promise((resolve) => {
    frame.onload = () => resolve();
    frame.src = url + (url.includes('?') ? '&' : '?') + 'wbrun=' + (++runCounter);
  });
}

async function loadPreview(frame, url, timeout, run) {
  await waitForLoad(frame, url);
  const start = performance.now();
  for (;;) {
    if (run.cancelled) throw new Error('Canceled.');
    const win = frame.contentWindow;
    const wb = win && win.__workbench;
    if (wb && (wb.state === 'ready' || wb.state === 'error')) return wb;
    if (performance.now() - start > timeout) throw new Error(`The preview was not ready after ${timeout} ms (state: ${wb ? wb.state : 'no preview script'}).`);
    await new Promise((r) => setTimeout(r, 50));
  }
}

async function runJob(frame, job, run) {
  const started = performance.now();
  const result = { scenarioId: job.scenarioId, kind: job.kind, name: job.name || null, status: 'passed', message: null, durationMs: 0, details: null };
  try {
    frame.style.width = `${job.width || 1280}px`;
    frame.style.height = `${job.height || 800}px`;
    const wb = await loadPreview(frame, job.url, job.timeout || 15000, run);
    const errors = () => wb.diagnostics.filter((d) => d.severity === 'Error');
    if (wb.state === 'error') {
      const first = errors()[0];
      throw new Error('The scenario failed to render' + (first ? `: ${first.message}` : '.'));
    }
    if (job.kind === 'interaction') {
      const outcome = await wb.runInteraction(job.test);
      result.details = outcome;
      if (!outcome.passed) {
        const failed = outcome.results.find((r) => r.status === 'failed');
        result.status = 'failed';
        result.message = failed ? `Step ${failed.index + 1}, ${failed.description}: ${failed.error}` : 'The interaction test failed.';
      }
    } else {
      const found = errors();
      if (found.length) {
        result.status = 'failed';
        result.message = `${found.length} error diagnostic(s): ${found[0].message}`;
      }
      if (job.a11y) {
        const outcome = await wb.runAxe({ disabledRules: job.a11y.disabledRules || [] });
        result.details = outcome;
        const failOn = IMPACT[job.a11y.failOn] || IMPACT.serious;
        const blocking = outcome.violations.filter((v) => (IMPACT[v.impact] || 0) >= failOn);
        if (blocking.length && !job.a11y.todo && result.status === 'passed') {
          result.status = 'failed';
          result.message = `${blocking.length} accessibility violation(s): ${blocking[0].id} (${blocking[0].impact}) — ${blocking[0].help}`;
        } else if (outcome.violations.length && result.status === 'passed') {
          result.status = 'warning';
          result.message = job.a11y.todo
            ? `${outcome.violations.length} accessibility violation(s), reported only (todo: ${job.a11y.todo}).`
            : `${outcome.violations.length} minor accessibility violation(s).`;
        }
      }
    }
  } catch (e) {
    result.status = run.cancelled ? 'canceled' : 'error';
    result.message = String((e && e.message) || e);
  }
  result.durationMs = Math.round(performance.now() - started);
  return result;
}

export function dispose() {
  cancelTests();
  if (listener) window.removeEventListener('message', listener);
  if (keyListener) document.removeEventListener('keydown', keyListener);
  if (pointerListener) document.removeEventListener('pointerdown', pointerListener);
  listener = null;
  keyListener = null;
  pointerListener = null;
  dotnet = null;
}
