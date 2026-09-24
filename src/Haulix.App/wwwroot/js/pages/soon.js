import { html } from "../core/html.js";
import { icon } from "../core/icons.js";

// Areas that need an online HAULIX service (accounts, VTCs, live positions). This offline version shows
// what is coming, and says plainly that the feature is not available yet.
const PAGES = {
  vtc: {
    title: "Find a VTC", crumb: "Virtual trucking companies", icon: "handshake",
    text: "Later you will be able to browse virtual trucking companies here, compare them and apply to join. HAULIX works fully offline today, so your logbook, map and statistics keep running as usual.",
    points: [["search", "Search VTCs by language, region and play style"], ["handshake", "Send join requests from HAULIX"], ["chart-column", "Share your deliveries with your company"]],
  },
  "vtc-my": {
    title: "My VTC", crumb: "Your virtual trucking company", icon: "building-2",
    text: "Your company's home in HAULIX: members and roles, company statistics built from everyone's logbooks, and a shared company bank.",
    points: [["users", "Members, roles and invitations"], ["chart-line", "Company kilometres, income and deliveries"], ["wallet", "Company bank and payouts"]],
  },
  "vtc-events": {
    title: "Events & convoys", crumb: "Drive together", icon: "calendar",
    text: "Plan and join convoys: date and time, meeting point, route on the HAULIX map and the server to meet on – with reminders shortly before the start.",
    points: [["calendar", "Event calendar for your VTC and public convoys"], ["route", "Meeting point and route on the map"], ["bell", "Reminders over the game before the start"]],
  },
  "vtc-jobs": {
    title: "Job board", crumb: "Jobs posted by your VTC", icon: "briefcase",
    text: "Your VTC posts jobs – cargo, route and reward – and members take them. HAULIX checks the delivery automatically from telemetry.",
    points: [["briefcase", "Company jobs with cargo, route and reward"], ["circle-check", "Automatic proof of delivery"], ["coins", "Rewards credited to the company bank"]],
  },
  "vtc-leaderboards": {
    title: "Leaderboards", crumb: "Rankings", icon: "award",
    text: "Rankings within your VTC and across all HAULIX drivers: kilometres, deliveries, income and driving score – weekly, monthly and all time.",
    points: [["award", "Weekly, monthly and all-time rankings"], ["gauge", "Driving score as a fair ranking"], ["users", "Your VTC against other companies"]],
  },
  "live-map": {
    title: "Live map", crumb: "Friends and colleagues on the map", icon: "globe",
    text: "See your friends and VTC colleagues live on the HAULIX map – where they drive, what they haul and when they arrive.",
    points: [["globe", "Live positions of friends and your VTC"], ["package", "Their current job and arrival time"], ["navigation", "Navigate to a friend with one click"]],
  },
  "cloud-sync": {
    title: "Cloud sync", crumb: "Your logbook on every PC", icon: "cloud-off",
    text: "Optionally back up your logbook, statistics and achievements to your HAULIX account and use them on several PCs. Offline stays the default.",
    points: [["history", "Automatic backup of your logbook"], ["monitor", "Same data on every PC"], ["shield-check", "Opt-in – nothing leaves your PC without your consent"]],
  },
};

const page = (id) => PAGES[id] || PAGES.vtc;

export default {
  title: () => page(location.hash.replace(/^#\/?/, "").split("/")[0]).title,
  crumb: () => page(location.hash.replace(/^#\/?/, "").split("/")[0]).crumb,

  render({ pageId }) {
    const p = page(pageId);
    return html`<div class="page-max stack">
      <section class="card vtc-soon">
        <div class="card__body vtc-soon__body">
          <div class="vtc-soon__icon">${icon(p.icon)}</div>
          <span class="badge badge--outline">${icon("clock", "icon icon-sm")}Coming later</span>
          <h2 class="vtc-soon__title">${p.title}</h2>
          <p class="vtc-soon__lead">This feature is not available in the current HAULIX version.</p>
          <p class="muted vtc-soon__text">${p.text}</p>
          <div class="vtc-soon__points">${p.points.map(([i, t]) => html`<div>${icon(i)}<span>${t}</span></div>`)}</div>
        </div>
      </section>
    </div>`;
  },
};
