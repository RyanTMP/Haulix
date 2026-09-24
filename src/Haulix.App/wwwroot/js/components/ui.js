import { html, raw, cx, esc } from "../core/html.js";
import { icon } from "../core/icons.js";

/* ---------------- Building blocks (return html) ---------------- */

export function card({ title, meta = "", body = "", foot = "", cls = "", bodyCls = "", id = "", muted = false, bare = false, attrs = "" }) {
  return html`<section class="${cx("card", cls)}" ${raw(id ? `id="${esc(id)}"` : "")} ${raw(attrs)}>
    ${title ? html`<header class="${cx("card__head", bare && "card__head--bare")}"><h2 class="${cx("label", muted && "label--muted")}">${title}</h2><div class="card__meta">${meta}</div></header>` : ""}
    <div class="${cx("card__body", bodyCls)}">${body}</div>
    ${foot ? html`<footer class="card__foot">${foot}</footer>` : ""}
  </section>`;
}

export function statTile({ iconName, value, label, bind = "", sub = "", tip = "" }) {
  return html`<div class="stat-tile" ${raw(tip ? `data-tip="${esc(tip)}"` : "")}>
    ${iconName ? html`<div class="stat-tile__icon">${icon(iconName)}</div>` : ""}
    <div class="stat">
      <div class="stat__value" ${raw(bind ? `data-bind="${esc(bind)}"` : "")}>${value}</div>
      <div class="stat__label">${label}${sub ? html`<span class="faint">· ${sub}</span>` : ""}</div>
    </div>
  </div>`;
}

export function empty({ iconName = null, title, text = "", action = "", brand = false, compact = false, error = false }) {
  return html`<div class="${cx("empty", compact && "empty--compact", error && "empty--error")}">
    ${brand ? html`<img class="empty__mark brand-img" src="assets/brand/h-logo.png" alt="">` : html`<div class="empty__icon">${icon(error ? "triangle-alert" : iconName || "circle-dot")}</div>`}
    <h3>${title}</h3>
    ${text ? html`<p>${text}</p>` : ""}
    ${action}
  </div>`;
}

export function skeleton(lines = 3, { block = false } = {}) {
  return html`<div aria-busy="true">
    <div class="skeleton skeleton--title"></div>
    ${Array.from({ length: lines }, (_, i) => html`<div class="skeleton skeleton--text" style="width:${90 - i * 12}%"></div>`)}
    ${block ? html`<div class="skeleton skeleton--block" style="margin-top:14px"></div>` : ""}
  </div>`;
}

export function progress(value, { tone = "", thin = false, thick = false, tip = "" } = {}) {
  const v = Math.max(0, Math.min(1, value || 0));
  return html`<div class="${cx("progress", tone && `progress--${tone}`, thin && "progress--thin", thick && "progress--thick")}" ${raw(tip ? `data-tip="${esc(tip)}"` : "")}>
    <div class="progress__fill" style="width:${(v * 100).toFixed(1)}%"></div></div>`;
}

export function damageTone(v) {
  if (v >= 0.25) return "crit";
  if (v >= 0.08) return "warn";
  return "ok";
}

export function slots(total, used, { muted = false } = {}) {
  return html`<div class="slots">${Array.from({ length: Math.max(total, 1) }, (_, i) => html`<i class="${i < used ? (muted ? "on-muted" : "on") : ""}"></i>`)}</div>`;
}

const DRIVER_STATUS = {
  driving: ["ok", "Driving"],
  on_job: ["warn", "On job"],
  resting: ["info", "Resting"],
  available: ["", "Available"],
  unknown: ["crit", "Unknown"],
  player: ["accent", "You"],
};
export function driverStatus(s) {
  const [tone, label] = DRIVER_STATUS[s] || DRIVER_STATUS.unknown;
  return html`<span class="${cx("badge", tone && `badge--${tone}`)}"><span class="${cx("dot", tone && `dot--${tone}`)}"></span>${label}</span>`;
}

export function kv(rows, { compact = false } = {}) {
  return html`<dl class="${cx("kv", compact && "kv--compact")}">${rows.filter(Boolean).map(
    ([k, v, opts = {}]) => html`<div><dt ${raw(opts.tip ? `data-tip="${esc(opts.tip)}"` : "")}>${opts.icon ? icon(opts.icon) : ""}${k}</dt><dd class="${cx(opts.text && "text", opts.cls)}" ${raw(opts.bind ? `data-bind="${esc(opts.bind)}"` : "")}>${v}</dd></div>`
  )}</dl>`;
}

export function segmented(name, options, active) {
  return html`<div class="segmented" role="tablist" data-seg="${name}">${options.map(
    ([value, label]) => html`<button type="button" data-action="seg" data-name="${name}" data-value="${value}" class="${value === active ? "is-active" : ""}">${label}</button>`
  )}</div>`;
}

export function toggle(name, checked, { disabled = false } = {}) {
  return html`<label class="toggle"><input type="checkbox" name="${name}" ${raw(checked ? "checked" : "")} ${raw(disabled ? "disabled" : "")}><span></span></label>`;
}

export function select(name, options, value, attrs = "") {
  return html`<select class="select" name="${name}" ${raw(attrs)}>${options.map(
    (o) => {
      const [v, l] = Array.isArray(o) ? o : [o, o];
      return html`<option value="${v}" ${raw(String(v) === String(value ?? "") ? "selected" : "")}>${l}</option>`;
    }
  )}</select>`;
}

/* ---------------- Tooltips (global, via data-tip) ---------------- */

let tipEl;
export function initTooltips() {
  tipEl = document.createElement("div");
  tipEl.className = "tooltip";
  document.body.append(tipEl);
  let current = null;
  document.addEventListener("mouseover", (e) => {
    const t = e.target.closest("[data-tip]");
    if (t === current) return;
    current = t;
    if (!t || !t.dataset.tip) { tipEl.classList.remove("is-visible"); return; }
    tipEl.textContent = t.dataset.tip;
    const r = t.getBoundingClientRect();
    tipEl.classList.add("is-visible");
    const tw = tipEl.offsetWidth, th = tipEl.offsetHeight;
    let x = r.left + r.width / 2 - tw / 2;
    let y = r.top - th - 8;
    if (y < 6) y = r.bottom + 8;
    x = Math.max(6, Math.min(window.innerWidth - tw - 6, x));
    tipEl.style.left = `${x}px`;
    tipEl.style.top = `${y}px`;
  });
  document.addEventListener("mousedown", () => tipEl.classList.remove("is-visible"));
}

/* ---------------- Toasts ---------------- */

let toastHost;
export function toast({ kind = "info", title, message = "", timeout = 5000 }) {
  if (!toastHost) {
    toastHost = document.createElement("div");
    toastHost.className = "toasts";
    document.body.append(toastHost);
  }
  const iconName = { success: "circle-check", warning: "triangle-alert", error: "circle-alert", info: "info" }[kind] || "info";
  const el = document.createElement("div");
  el.className = `toast toast--${kind}`;
  el.setAttribute("role", kind === "error" ? "alert" : "status");
  el.innerHTML = html`<div class="toast__icon">${icon(iconName)}</div><div class="grow"><div class="toast__title">${title}</div>${message ? html`<div class="toast__msg">${message}</div>` : ""}</div><button class="toast__close" aria-label="Dismiss">${icon("x")}</button>`.toString();
  const close = () => { el.classList.add("is-leaving"); setTimeout(() => el.remove(), 200); };
  el.querySelector(".toast__close").onclick = close;
  toastHost.append(el);
  while (toastHost.children.length > 4) toastHost.firstChild.remove();
  if (timeout) setTimeout(close, timeout);
}

/* ---------------- Modal & confirm ---------------- */

export function modal({ title, body, actions = [], wide = false, onClose }) {
  const overlay = document.createElement("div");
  overlay.className = "overlay";
  overlay.innerHTML = html`<div class="${cx("modal", wide && "modal--wide")}" role="dialog" aria-modal="true">
    <div class="modal__head"><h2>${title}</h2><button class="btn btn--ghost btn--icon btn--sm" style="margin-left:auto" data-close aria-label="Close">${icon("x")}</button></div>
    <div class="modal__body">${body}</div>
    ${actions.length ? html`<div class="modal__foot">${actions.map((a, i) => html`<button class="btn ${a.kind ? `btn--${a.kind}` : ""}" data-idx="${i}">${a.label}</button>`)}</div>` : ""}
  </div>`.toString();
  const close = (v) => {
    overlay.remove();
    document.removeEventListener("keydown", onKey);
    onClose?.(v);
  };
  const onKey = (e) => { if (e.key === "Escape") close(null); };
  document.addEventListener("keydown", onKey);
  overlay.addEventListener("mousedown", (e) => { if (e.target === overlay) close(null); });
  overlay.querySelector("[data-close]").onclick = () => close(null);
  overlay.querySelectorAll("[data-idx]").forEach((b) => {
    b.onclick = async () => {
      const a = actions[+b.dataset.idx];
      if (a.onClick) {
        b.classList.add("is-loading");
        try {
          const keep = await a.onClick(overlay);
          if (keep === false) { b.classList.remove("is-loading"); return; }
        } catch (err) {
          b.classList.remove("is-loading");
          toast({ kind: "error", title: "Action failed", message: err.message });
          return;
        }
      }
      close(a.value ?? true);
    };
  });
  document.body.append(overlay);
  overlay.querySelector(".btn--primary, .btn--danger")?.focus();
  return { el: overlay, close };
}

export function confirm({ title, text, confirmLabel = "Confirm", danger = false }) {
  return new Promise((resolve) => {
    modal({
      title,
      body: html`<p class="muted" style="line-height:1.6">${text}</p>`,
      actions: [
        { label: "Cancel", kind: "ghost", value: false },
        { label: confirmLabel, kind: danger ? "danger" : "primary", value: true },
      ],
      onClose: (v) => resolve(!!v),
    });
  });
}

/* ---------------- Drawer (detail panel) ---------------- */

let openDrawer = null;
export function drawer({ title, sub = "", head = "", body, wide = false, onMount }) {
  closeDrawer();
  const overlay = document.createElement("div");
  overlay.className = "drawer-overlay";
  const el = document.createElement("aside");
  el.className = cx("drawer", wide && "drawer--wide");
  el.setAttribute("role", "dialog");
  el.innerHTML = html`<header class="drawer__head">
      ${head}
      <div class="grow"><div class="drawer__title">${title}</div>${sub ? html`<div class="drawer__sub">${sub}</div>` : ""}</div>
      <button class="btn btn--ghost btn--icon btn--sm" data-close aria-label="Close">${icon("x")}</button>
    </header>
    <div class="drawer__body">${body}</div>`.toString();
  const cleanup = [];
  const close = () => {
    overlay.remove();
    el.remove();
    document.removeEventListener("keydown", onKey);
    cleanup.forEach((f) => f?.());
    openDrawer = null;
  };
  const onKey = (e) => { if (e.key === "Escape") close(); };
  document.addEventListener("keydown", onKey);
  overlay.onclick = close;
  el.querySelector("[data-close]").onclick = close;
  document.body.append(overlay, el);
  openDrawer = close;
  const c = onMount?.(el.querySelector(".drawer__body"), el);
  if (c) cleanup.push(c);
  return { el, close };
}
export function closeDrawer() { openDrawer?.(); }

/* ---------------- Context menu ---------------- */

export function contextMenu(ev, items) {
  ev.preventDefault();
  document.querySelectorAll(".menu").forEach((m) => m.remove());
  const m = document.createElement("div");
  m.className = "menu";
  m.innerHTML = items.map((it, i) =>
    it === "-" ? "<hr>" : html`<button data-i="${i}" class="${it.danger ? "danger" : ""}">${it.icon ? icon(it.icon) : ""}${it.label}</button>`.toString()
  ).join("");
  document.body.append(m);
  const w = m.offsetWidth, h = m.offsetHeight;
  m.style.left = `${Math.min(ev.clientX, window.innerWidth - w - 8)}px`;
  m.style.top = `${Math.min(ev.clientY, window.innerHeight - h - 8)}px`;
  const close = () => { m.remove(); document.removeEventListener("mousedown", outside, true); };
  const outside = (e) => { if (!m.contains(e.target)) close(); };
  setTimeout(() => document.addEventListener("mousedown", outside, true));
  m.querySelectorAll("[data-i]").forEach((b) => (b.onclick = () => { close(); items[+b.dataset.i].onClick?.(); }));
}
