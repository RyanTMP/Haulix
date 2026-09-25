import { raw } from "./html.js";

// Icons come from the local Lucide sprite (assets/icons.svg), inlined into the document on boot.
let loaded = false;

export async function loadIcons() {
  if (loaded) return;
  const res = await fetch("assets/icons.svg");
  const text = await res.text();
  const holder = document.createElement("div");
  holder.style.display = "none";
  holder.innerHTML = text;
  document.body.prepend(holder);
  loaded = true;
}

export function icon(name, cls = "icon") {
  return raw(
    `<svg class="${cls}" fill="none" stroke="currentColor" stroke-width="1.75" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><use href="#i-${name}"/></svg>`
  );
}

