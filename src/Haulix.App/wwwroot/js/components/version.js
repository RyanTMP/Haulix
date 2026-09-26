import { html, cx } from "../core/html.js";
import { store } from "../core/store.js";
import { versionParts } from "../core/format.js";

// The HAULIX version the user is running, shown the same way everywhere: "HAULIX 0.0.9" plus a coloured channel pill
// (BETA, DEVKIT …) and, once the update check has run, whether it is up to date.
const CHANNEL_TONE = { BETA: "beta", ALPHA: "alpha", DEVKIT: "dev", DEV: "dev", RC: "rc" };

export function channelPill(channel) {
  if (!channel) return html`<span class="ver-pill ver-pill--stable">STABLE</span>`;
  return html`<span class="${cx("ver-pill", `ver-pill--${CHANNEL_TONE[channel] || "beta"}`)}">${channel}</span>`;
}

/** Update state for the badge: "latest", "update" (newer version available) or "" (not checked). */
export function updateState() {
  const u = store.get("updateInfo");
  if (!u || u.error || u.enabled === false) return "";
  return u.available ? "update" : "latest";
}

/** size: "sm" (sidebar), "lg" (About). */
export function versionBadge(size = "sm") {
  const { number, channel } = versionParts(store.get("versionRaw"));
  const state = updateState();
  const u = store.get("updateInfo");
  const tip = state === "update" ? `Version ${versionParts(u.latest).number} is available – click to update`
    : state === "latest" ? "You are on the newest version" : "Your HAULIX version – click for what's new";
  return html`<button class="${cx("ver", `ver--${size}`, state && `ver--${state}`)}" data-version-badge data-tip="${tip}" type="button">
    <span class="ver__label">${size === "lg" ? "You are using" : "HAULIX"}</span>
    <span class="ver__main">${size === "lg" ? html`<strong>HAULIX</strong> ` : ""}<span class="ver__num">${number}</span>${channelPill(channel)}</span>
    ${state ? html`<span class="ver__state"><i></i>${state === "update" ? `Update ${versionParts(u.latest).number} available` : "Up to date"}</span>` : ""}
  </button>`;
}
