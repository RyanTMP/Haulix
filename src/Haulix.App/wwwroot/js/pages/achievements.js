import { html, $, cx } from "../core/html.js";
import { icon } from "../core/icons.js";
import { store } from "../core/store.js";
import { progress, toast, skeleton } from "../components/ui.js";
import { summaryCard } from "../components/sharecard.js";
import { t } from "../core/i18n.js";
import * as f from "../core/format.js";

// Local achievements computed from the logbook and the save (see Haulix.Core/Services/Achievements.cs).
// Levels of one family (bronze → silver → gold → platinum) are shown as one card.
const CATEGORIES = [["all", "All", "layout-grid"], ["career", "Career", "briefcase"], ["distance", "Distance", "route"], ["driving", "Driving", "gauge"],
  ["cargo", "Cargo", "package"], ["explorer", "Explorer", "compass"], ["business", "Business", "badge-euro"]];
const TIER_LABEL = { bronze: "Bronze", silver: "Silver", gold: "Gold", platinum: "Platinum" };
const RANKS = [[0, "Rookie"], [150, "Driver"], [400, "Professional"], [800, "Expert"], [1400, "Veteran"], [2200, "Master"], [3200, "Legend"]];

export const rankOf = (points) => {
  let i = 0;
  while (i + 1 < RANKS.length && points >= RANKS[i + 1][0]) i++;
  const next = RANKS[i + 1];
  return { name: RANKS[i][1], level: i + 1, from: RANKS[i][0], to: next?.[0] ?? null, next: next?.[1] ?? null };
};

// Groups the flat level list into families (older backends without family info become one-level families).
export const families = (list) => {
  const map = new Map();
  for (const a of list) {
    const key = a.family || a.id;
    if (!map.has(key)) map.set(key, { id: key, category: a.category || "career", icon: a.icon, levels: [] });
    map.get(key).levels.push(a);
  }
  return [...map.values()].map((fam) => {
    fam.levels.sort((x, y) => (x.level ?? 1) - (y.level ?? 1));
    fam.done = fam.levels.filter((l) => l.unlocked);
    fam.best = fam.done[fam.done.length - 1] || null;
    fam.next = fam.levels.find((l) => !l.unlocked) || null;
    fam.share = fam.next ? fam.next.progress / fam.next.target : 1;
    return fam;
  });
};

const value = (a, n) => (a.target >= 1000 ? f.num(Math.floor(n)) : f.num(n, n % 1 ? 1 : 0));

export default {
  title: "Achievements",
  crumb: () => "Milestones of your trucking career · stored locally",

  render() {
    return html`<div class="page-max stack">
      <section class="card ach-hero" id="achSummary"><div class="card__body">${skeleton(2)}</div></section>
      <div id="achNext"></div>
      <div class="ach-tabs" id="achTabs">${CATEGORIES.map(([id, l, i]) => html`<button class="${cx("ach-tab", id === "all" && "is-active")}" data-cat="${id}">${icon(i)}${l}<span data-count="${id}"></span></button>`)}</div>
      <div class="ach-grid" id="achGrid"></div>
      <section class="card" id="achRecent"></section>
    </div>`;
  },

  async mount(root, { call }) {
    let cat = "all";
    let all = [];

    const card = (fam) => {
      const cur = fam.next || fam.best;
      const tier = fam.best?.tier || "locked";
      return html`<div class="${cx("ach", `tier-${fam.best?.tier || fam.levels[0].tier}`, !fam.best && "is-locked", !fam.next && "is-complete")}">
        <div class="ach__icon">${icon(fam.icon)}</div>
        <div class="grow" style="min-width:0">
          <div class="ach__top"><div class="ach__title">${fam.best?.title || fam.levels[0].title}</div>
            <div class="ach__pips">${fam.levels.map((l) => html`<i class="${cx(`tier-${l.tier}`, l.unlocked && "is-on")}" data-tip="${t(TIER_LABEL[l.tier])} · ${l.title}"></i>`)}</div></div>
          <div class="ach__desc">${fam.next ? html`<span class="faint">${t("Next")}:</span> ${fam.next.title} – ${fam.next.description}` : html`${icon("check", "icon icon-sm")} ${t("All levels reached")}`}</div>
          ${progress(fam.next ? fam.share : 1, { thin: true, tone: fam.next ? undefined : "ok" })}
          <div class="ach__meta"><span>${fam.next ? `${value(cur, cur.progress)} / ${value(cur, cur.target)}` : `${t("Unlocked")} ${f.date(fam.best.unlockedUtc)}`}</span>
            <span>${fam.best ? `${t(TIER_LABEL[tier])} · ` : ""}${t("Level")} ${fam.done.length}/${fam.levels.length}</span></div>
        </div></div>`;
    };

    const draw = (list) => {
      all = list;
      const fams = families(list);
      const done = list.filter((a) => a.unlocked);
      const points = done.reduce((s, a) => s + (a.points || 0), 0);
      const maxPoints = list.reduce((s, a) => s + (a.points || 0), 0);
      const rank = rankOf(points);
      const tiers = ["bronze", "silver", "gold", "platinum"].map((tr) => [tr, done.filter((a) => a.tier === tr).length, list.filter((a) => a.tier === tr).length]);

      $("#achSummary", root).innerHTML = html`<div class="card__body ach-hero__body">
        <div class="ach-rank"><div class="ach-rank__badge">${icon("trophy")}<b>${rank.level}</b></div>
          <div><div class="label label--muted">${t("Driver rank")}</div><div class="ach-rank__name">${t(rank.name)}</div>
            <div class="faint" style="font-size:12px">${rank.next ? `${f.num(rank.to - points)} ${t("points to")} ${t(rank.next)}` : t("Highest rank reached")}</div></div></div>
        <div class="ach-hero__stats">
          <div class="stat"><div class="stat__label">${t("Points")}</div><div class="stat__value">${f.num(points)}<small> / ${f.num(maxPoints)}</small></div></div>
          <div class="stat"><div class="stat__label">${t("Unlocked")}</div><div class="stat__value">${done.length}<small> / ${list.length}</small></div></div>
          ${tiers.map(([tr, n, of]) => html`<div class="stat tier-${tr}"><div class="stat__label"><i class="ach-dot"></i>${t(TIER_LABEL[tr])}</div><div class="stat__value">${n}<small> / ${of}</small></div></div>`)}
        </div>
        <div class="ach-hero__bar">${progress(rank.to ? (points - rank.from) / (rank.to - rank.from) : 1, { thick: true })}</div>
        <button class="btn btn--sm" id="achShare">${icon("download")}Save share card</button>
      </div>`.toString();

      // Almost there: the three locked levels closest to being reached.
      const near = fams.filter((x) => x.next && x.share > 0).sort((a, b) => b.share - a.share).slice(0, 3);
      $("#achNext", root).innerHTML = near.length ? html`<div class="label label--muted" style="margin-bottom:10px">${t("Almost there")}</div>
        <div class="ach-near">${near.map((x) => html`<div class="ach-near__item tier-${x.next.tier}">${icon(x.icon)}<div class="grow" style="min-width:0">
          <div class="ach-near__title">${x.next.title}</div>${progress(x.share, { thin: true })}</div><b class="num">${Math.floor(x.share * 100)} %</b></div>`)}</div>`.toString() : "";

      for (const [id] of CATEGORIES) {
        const el = root.querySelector(`[data-count="${id}"]`);
        const inCat = fams.filter((x) => id === "all" || x.category === id);
        if (el) el.textContent = `${inCat.reduce((s, x) => s + x.done.length, 0)}/${inCat.reduce((s, x) => s + x.levels.length, 0)}`;
      }
      const shown = fams.filter((x) => cat === "all" || x.category === cat)
        .sort((a, b) => (b.done.length > 0) - (a.done.length > 0) || b.share - a.share);
      $("#achGrid", root).innerHTML = shown.map((x) => card(x).toString()).join("");

      const recent = [...done].sort((a, b) => String(b.unlockedUtc).localeCompare(String(a.unlockedUtc))).slice(0, 6);
      $("#achRecent", root).innerHTML = html`<header class="card__head"><h2 class="label">${t("Recently unlocked")}</h2></header>
        <div class="card__body card__body--flush">${recent.length ? html`<div class="ach-recent">${recent.map((a) => html`<div class="ach-recent__row tier-${a.tier}">
          <span class="ach-recent__icon">${icon(a.icon)}</span><div class="grow"><strong>${a.title}</strong><div class="faint" style="font-size:12px">${a.description}</div></div>
          <span class="badge">${t(TIER_LABEL[a.tier])} · +${a.points || 0}</span><span class="faint num" style="font-size:12px;min-width:90px;text-align:right">${f.date(a.unlockedUtc)}</span></div>`)}</div>`
          : html`<div class="muted" style="padding:18px 20px">${t("Deliver your first job to unlock achievements.")}</div>`}</div>`.toString();

      $("#achShare", root).onclick = async () => {
        try {
          const p = store.get("profile");
          const dataUrl = await summaryCard({
            title: p?.companyName || p?.profileName || "HAULIX",
            subtitle: `${t("Achievements")} · ${t(rank.name)}`,
            stats: [[t("Points"), f.num(points)], [t("Unlocked"), `${done.length}/${list.length}`], ...recent.slice(0, 4).map((a) => [t(TIER_LABEL[a.tier]), a.title])],
          });
          const path = await call("image.save", { dataUrl, name: "haulix-achievements.png" });
          if (path) toast({ kind: "success", title: "Share card saved", message: path });
        } catch (e) { toast({ kind: "error", title: "Could not save image", message: e.message }); }
      };
    };

    $("#achTabs", root).addEventListener("click", (e) => {
      const b = e.target.closest("[data-cat]");
      if (!b) return;
      cat = b.dataset.cat;
      root.querySelectorAll("[data-cat]").forEach((x) => x.classList.toggle("is-active", x === b));
      draw(all);
    });

    try { draw(await call("achievements.get")); }
    catch (e) { toast({ kind: "error", title: "Achievements unavailable", message: e.message }); }
    return store.on("achievements", draw);
  },
};
