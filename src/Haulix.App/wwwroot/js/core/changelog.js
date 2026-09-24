// "What's new": opens once after an update (Settings → About → What's new opens it again).
// Written in both languages instead of the i18n dictionary, because it is long prose.
import { html } from "./html.js";
import { icon } from "./icons.js";
import { language } from "./i18n.js";
import { modal } from "../components/ui.js";

// Content lives in /changelog.json (also used for CHANGELOG.md and the GitHub release notes).
let cache = null;
const loadChangelog = () => (cache ??= fetch("changelog.json").then((r) => r.json()));

/** Opens the "What's new" window. */
export async function showChangelog() {
  const de = language() === "de";
  const CHANGELOG = await loadChangelog().catch(() => []);
  modal({
    wide: true,
    title: de ? "Neu in HAULIX" : "What's new in HAULIX",
    body: html`<div class="changelog">${CHANGELOG.map((v, i) => html`<section class="changelog__version">
      <div class="changelog__head"><span class="changelog__v">v${v.version}</span>${v.tag ? html`<span class="badge ${i === 0 ? "badge--accent" : "badge--outline"}">${v.tag}</span>` : ""}${i === 0 ? html`<span class="badge badge--ok">${de ? "Aktuell" : "Current"}</span>` : ""}</div>
      <ul>${(de ? v.de : v.en).map(([ic, title, text]) => html`<li><span class="changelog__icon">${icon(ic)}</span><div><strong>${title}</strong><p>${text}</p></div></li>`)}</ul>
    </section>`)}</div>`,
    actions: [{ label: de ? "Los geht's" : "Let's drive", kind: "primary", value: true }],
  });
}
