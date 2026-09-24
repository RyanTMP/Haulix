import { html, $ } from "../core/html.js";
import { icon } from "../core/icons.js";
import { store } from "../core/store.js";
import { progress, toast, skeleton } from "../components/ui.js";
import { summaryCard } from "../components/sharecard.js";
import * as f from "../core/format.js";

// Local achievements computed from the logbook and the save (see Haulix.Core/Services/Achievements.cs).
export default {
  title: "Achievements",
  crumb: () => "Milestones of your trucking career · stored locally",

  render() {
    return html`<div class="page-max stack">
      <section class="card"><div class="card__body ach-summary" id="achSummary">${skeleton(1)}</div></section>
      <div class="ach-grid" id="achGrid"></div>
    </div>`;
  },

  async mount(root, { call }) {
    const draw = (list) => {
      const done = list.filter((a) => a.unlocked);
      $("#achSummary", root).innerHTML = html`
        <div class="stat"><div class="stat__value stat__value--lg">${done.length}<small>/ ${list.length}</small></div><div class="stat__label">Unlocked</div></div>
        <div class="grow" style="min-width:200px">${progress(list.length ? done.length / list.length : 0, { thick: true })}</div>
        <button class="btn btn--sm" id="achShare">${icon("download")}Save share card</button>`.toString();
      const sorted = [...list].sort((a, b) => (b.unlocked - a.unlocked) || (b.progress / b.target - a.progress / a.target));
      $("#achGrid", root).innerHTML = sorted.map((a) => html`<div class="ach tier-${a.tier} ${a.unlocked ? "" : "is-locked"}">
        <div class="ach__icon">${icon(a.icon)}</div>
        <div class="grow" style="min-width:0">
          <div class="ach__title">${a.title}</div>
          <div class="ach__desc">${a.description}</div>
          ${progress(a.target ? a.progress / a.target : 0, { thin: true, tone: a.unlocked ? "ok" : undefined })}
          <div class="ach__meta"><span>${a.unlocked ? `Unlocked ${f.date(a.unlockedUtc)}` : `${f.num(a.progress)} / ${f.num(a.target)}`}</span><span>${a.tier.toUpperCase()}</span></div>
        </div></div>`.toString()).join("");
      $("#achShare", root).onclick = async () => {
        try {
          const p = store.get("profile");
          const dataUrl = await summaryCard({
            title: p?.companyName || p?.profileName || "HAULIX",
            subtitle: "Achievements",
            stats: [["Unlocked", `${done.length}/${list.length}`], ...done.slice(-5).reverse().map((a) => [a.tier, a.title])],
          });
          const path = await call("image.save", { dataUrl, name: "haulix-achievements.png" });
          if (path) toast({ kind: "success", title: "Share card saved", message: path });
        } catch (e) { toast({ kind: "error", title: "Could not save image", message: e.message }); }
      };
    };
    try { draw(await call("achievements.get")); }
    catch (e) { toast({ kind: "error", title: "Achievements unavailable", message: e.message }); }
    return store.on("achievements", draw);
  },
};
