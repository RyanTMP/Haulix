// Message bridge to the local C# backend (WebView2 postMessage). No HTTP, no sockets.
// When opened in a plain browser (design preview / development) a mock backend with fixture data is used.

const listeners = new Map();
const pending = new Map();
let seq = 0;
let transport = null;

export const isNative = !!(window.chrome && window.chrome.webview);

function dispatch(msg) {
  if (msg.id && pending.has(msg.id)) {
    const { resolve, reject } = pending.get(msg.id);
    pending.delete(msg.id);
    msg.ok ? resolve(msg.result) : reject(new Error(msg.error || "Request failed"));
    return;
  }
  if (msg.event) {
    for (const fn of listeners.get(msg.event) || []) {
      try { fn(msg.data); } catch (e) { console.error(`[bridge] ${msg.event} handler failed`, e); }
    }
  }
}

export async function connect() {
  if (transport) return;
  if (isNative) {
    window.chrome.webview.addEventListener("message", (e) => dispatch(e.data));
    transport = (m) => window.chrome.webview.postMessage(m);
  } else {
    const mock = await import("../mock/mock-backend.js");
    transport = mock.createMockTransport(dispatch);
  }
}

export function call(method, params = {}, { timeout = 30000 } = {}) {
  const id = ++seq;
  return new Promise((resolve, reject) => {
    pending.set(id, { resolve, reject });
    // Native dialogs block until the user closes them, so they get no timeout.
    if (timeout && !method.startsWith("dialog.") && !method.startsWith("data.")) {
      setTimeout(() => {
        if (pending.has(id)) {
          pending.delete(id);
          reject(new Error(`${method} timed out`));
        }
      }, timeout);
    }
    transport({ id, method, params });
  });
}

export function on(event, fn) {
  if (!listeners.has(event)) listeners.set(event, new Set());
  listeners.get(event).add(fn);
  return () => listeners.get(event).delete(fn);
}
