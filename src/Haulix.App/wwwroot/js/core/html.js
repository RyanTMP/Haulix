// Tiny, safe HTML templating: html`...` escapes interpolations unless wrapped in raw().

class Raw {
  constructor(s) { this.s = s; }
  toString() { return this.s; }
}

const ESC = { "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" };

export const esc = (v) => String(v ?? "").replace(/[&<>"']/g, (c) => ESC[c]);

export const raw = (s) => new Raw(String(s ?? ""));

function part(v) {
  if (v instanceof Raw) return v.s;
  if (Array.isArray(v)) return v.map(part).join("");
  if (v === null || v === undefined || v === false) return "";
  return esc(v);
}

export function html(strings, ...values) {
  let out = "";
  for (let i = 0; i < strings.length; i++) {
    out += strings[i];
    if (i < values.length) out += part(values[i]);
  }
  return new Raw(out);
}

/** Conditional class list: cx("a", cond && "b", { c: true }) */
export function cx(...args) {
  const out = [];
  for (const a of args) {
    if (!a) continue;
    if (typeof a === "string") out.push(a);
    else if (typeof a === "object") for (const [k, v] of Object.entries(a)) if (v) out.push(k);
  }
  return out.join(" ");
}

/** Render into an element. */
export function render(el, content) {
  el.innerHTML = part(content);
  return el;
}

export const $ = (sel, root = document) => root.querySelector(sel);
export const $$ = (sel, root = document) => [...root.querySelectorAll(sel)];

/**
 * Delegated event handling on data-action attributes.
 * on(root, "click", { open: (el, ev) => ... })
 */
export function delegate(root, type, handlers) {
  const fn = (ev) => {
    const el = ev.target.closest("[data-action]");
    if (!el || !root.contains(el)) return;
    const h = handlers[el.dataset.action];
    if (h) h(el, ev);
  };
  root.addEventListener(type, fn);
  return () => root.removeEventListener(type, fn);
}

/** Update text of all [data-bind=key] under root. Only touches the DOM when the value changed. */
export function bindText(root, values) {
  for (const el of root.querySelectorAll("[data-bind]")) {
    const key = el.dataset.bind;
    if (!(key in values)) continue;
    const v = String(values[key] ?? "—");
    if (el.textContent !== v) el.textContent = v;
  }
  // data-bind-tip="key": the value becomes the element's tooltip.
  for (const el of root.querySelectorAll("[data-bind-tip]")) {
    const key = el.dataset.bindTip;
    if (key in values && el.dataset.tip !== String(values[key] ?? "")) el.dataset.tip = String(values[key] ?? "");
  }
}

export function debounce(fn, ms) {
  let t;
  return (...a) => { clearTimeout(t); t = setTimeout(() => fn(...a), ms); };
}
