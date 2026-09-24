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

/** The HAULIX truck marker: slanted chevron echoing the H logo, rotated to heading. */
export function truckMarkerSvg(heading = 0, size = 34) {
  return `<svg width="${size}" height="${size}" viewBox="-20 -20 40 40" style="transform:rotate(${heading}deg);overflow:visible">
    <circle r="17" fill="rgb(255 176 32 / 0.14)" stroke="rgb(255 176 32 / 0.35)" stroke-width="1"/>
    <path d="M0 -12 L9 9 L0 4 L-9 9 Z" fill="var(--accent)" stroke="#0b0c0e" stroke-width="2" stroke-linejoin="round"/>
  </svg>`;
}
