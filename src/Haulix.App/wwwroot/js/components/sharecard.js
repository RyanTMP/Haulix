// Share cards: a delivery (or a stats summary) rendered as a 1200×630 PNG in the HAULIX style,
// ready for Discord or social media. Drawn on a canvas; saved or copied through the desktop host.
import * as f from "../core/format.js";
import { t } from "../core/i18n.js";

const W = 1200, H = 630;

function loadImage(src) {
  return new Promise((resolve) => { const i = new Image(); i.onload = () => resolve(i); i.onerror = () => resolve(null); i.src = src; });
}

async function fonts() {
  try {
    await Promise.all(["600 40px 'Barlow Condensed'", "700 italic 40px 'Barlow Condensed'", "500 20px Inter", "500 20px 'JetBrains Mono'"].map((x) => document.fonts.load(x)));
  } catch { /* fall back to system fonts */ }
}

function base(ctx, accent) {
  ctx.fillStyle = "#0b0c0e"; ctx.fillRect(0, 0, W, H);
  // subtle diagonal texture
  ctx.strokeStyle = "rgba(255,255,255,0.025)"; ctx.lineWidth = 2;
  for (let x = -H; x < W; x += 28) { ctx.beginPath(); ctx.moveTo(x, H); ctx.lineTo(x + H * 0.27, 0); ctx.stroke(); }
  // slanted accent bar (HAULIX signature)
  ctx.fillStyle = accent;
  ctx.beginPath(); ctx.moveTo(64, 70); ctx.lineTo(78, 70); ctx.lineTo(64 + 14 - 34, H - 70); ctx.lineTo(64 - 34, H - 70); ctx.closePath(); ctx.fill();
}

function label(ctx, text, x, y) {
  ctx.fillStyle = "#6c737d"; ctx.font = "600 20px 'Barlow Condensed', sans-serif";
  ctx.letterSpacing = "3px"; ctx.fillText(t(text).toUpperCase(), x, y); ctx.letterSpacing = "0px";
}

function value(ctx, text, x, y, color = "#ecedee", size = 40) {
  ctx.fillStyle = color; ctx.font = `500 ${size}px 'JetBrains Mono', monospace`; ctx.fillText(text, x, y);
}

function fit(ctx, text, maxWidth, size, weight = "600", family = "'Barlow Condensed', sans-serif") {
  let s = size;
  do { ctx.font = `${weight} ${s}px ${family}`; s -= 2; } while (ctx.measureText(text).width > maxWidth && s > 24);
}

/** PNG data URL of a delivery card. */
export async function deliveryCard(d) {
  await fonts();
  const accent = getComputedStyle(document.documentElement).getPropertyValue("--accent").trim() || "#ffb020";
  const c = document.createElement("canvas"); c.width = W; c.height = H;
  const ctx = c.getContext("2d");
  base(ctx, accent);
  const logo = await loadImage("assets/brand/banner.png");
  if (logo) { const h = 54; ctx.drawImage(logo, W - 60 - (logo.width * h) / logo.height, 52, (logo.width * h) / logo.height, h); }

  label(ctx, d.status === "delivered" ? "Delivered" : "Cancelled", 120, 96);
  ctx.fillStyle = "#ecedee";
  const route = `${d.originCity} → ${d.destCity}`.toUpperCase();
  fit(ctx, route, W - 240, 76);
  ctx.fillText(route, 120, 178);
  ctx.fillStyle = "#9ba1aa"; ctx.font = "500 26px Inter, sans-serif";
  ctx.fillText(`${d.cargo}${d.cargoMassKg ? ` · ${f.mass(d.cargoMassKg)}` : ""}${d.truck ? ` · ${d.truck}` : ""}`, 120, 222);

  const stats = [
    ["Distance", f.dist(d.distanceKm)],
    [d.status === "delivered" ? "Income" : "Penalty", d.status === "delivered" ? f.money(d.income) : `−${f.money(d.penalty)}`],
    ["XP", f.num(d.xp)],
    ["Driving score", d.score != null ? `${d.score}/100` : "—"],
  ];
  const colW = (W - 240) / stats.length;
  stats.forEach(([l, v], i) => {
    const x = 120 + i * colW;
    label(ctx, l, x, 360);
    value(ctx, v, x, 412, i === 3 && d.score != null ? scoreColor(d.score) : i === 1 ? (d.status === "delivered" ? "#3dd68c" : "#f0474f") : "#ecedee");
  });
  ctx.strokeStyle = "#23272e"; ctx.lineWidth = 2; ctx.beginPath(); ctx.moveTo(120, 300); ctx.lineTo(W - 120, 300); ctx.stroke();

  ctx.fillStyle = "#5e656f"; ctx.font = "500 20px Inter, sans-serif";
  ctx.fillText(d.finishedUtc ? f.dateTime(d.finishedUtc) : f.gameDay(d.gameEndMin), 120, H - 72);
  ctx.textAlign = "right"; ctx.fillText(t("Logged with HAULIX · ETS2 Logger"), W - 60, H - 72); ctx.textAlign = "left";
  return c.toDataURL("image/png");
}

/** PNG data URL of a period summary card: { title, subtitle, stats: [[label, value], …] } */
export async function summaryCard({ title, subtitle, stats }) {
  await fonts();
  const accent = getComputedStyle(document.documentElement).getPropertyValue("--accent").trim() || "#ffb020";
  const c = document.createElement("canvas"); c.width = W; c.height = H;
  const ctx = c.getContext("2d");
  base(ctx, accent);
  const logo = await loadImage("assets/brand/banner.png");
  if (logo) { const h = 54; ctx.drawImage(logo, W - 60 - (logo.width * h) / logo.height, 52, (logo.width * h) / logo.height, h); }
  label(ctx, subtitle, 120, 96);
  ctx.fillStyle = "#ecedee"; fit(ctx, title.toUpperCase(), W - 480, 76); ctx.fillText(title.toUpperCase(), 120, 178);
  const cols = 3, colW = (W - 240) / cols;
  stats.slice(0, 6).forEach(([l, v], i) => {
    const x = 120 + (i % cols) * colW, y = 300 + Math.floor(i / cols) * 140;
    label(ctx, l, x, y);
    value(ctx, v, x, y + 52, i === 0 ? accent : "#ecedee");
  });
  ctx.fillStyle = "#5e656f"; ctx.font = "500 20px Inter, sans-serif";
  ctx.textAlign = "right"; ctx.fillText(t("Logged with HAULIX · ETS2 Logger"), W - 60, H - 72); ctx.textAlign = "left";
  return c.toDataURL("image/png");
}

export function scoreColor(score) {
  return score >= 90 ? "#3dd68c" : score >= 70 ? "#ffb020" : score >= 50 ? "#ff8a3d" : "#f0474f";
}
