// Blazor Workbench shell module: bridges the preview iframe (postMessage) and the shell component (.NET).
const SHELL = 'blazor-workbench-shell';
const PREVIEW = 'blazor-workbench-preview';

let dotnet = null;
let listener = null;
let keyListener = null;

export function init(dotnetRef) {
  dispose();
  dotnet = dotnetRef;
  listener = (e) => {
    if (e.origin !== location.origin || !e.data || e.data.source !== PREVIEW) return;
    const frame = document.getElementById('wb-preview-frame');
    if (!frame || e.source !== frame.contentWindow) return;
    dotnet.invokeMethodAsync('OnPreviewMessage', JSON.stringify(e.data)).catch(() => { /* circuit closing */ });
  };
  window.addEventListener('message', listener);

  keyListener = (e) => {
    const tag = (e.target && e.target.tagName) || '';
    const editing = ['INPUT', 'TEXTAREA', 'SELECT'].includes(tag) || (e.target && e.target.isContentEditable);
    if (e.key === '/' && !editing) {
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
    default: return null;
  }
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

export function dispose() {
  if (listener) window.removeEventListener('message', listener);
  if (keyListener) document.removeEventListener('keydown', keyListener);
  listener = null;
  keyListener = null;
  dotnet = null;
}
