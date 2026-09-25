import { html, raw } from "./html.js";

// Logos for truck brands, trailer brands, job companies and cargo categories (assets/logos, synced from branding/
// with tools/brand/sync-logos.ps1). Brand and company names are matched loosely (case, accents, punctuation).
const BASE = "assets/logos";
const norm = (s) => String(s || "").normalize("NFD").replace(/[̀-ͯ]/g, "").toLowerCase().replace(/ß/g, "ss").replace(/[^a-z0-9]/g, "");

const TRUCKS = {
  daf: "daf.svg", iveco: "iveco.svg", man: "man.svg", mercedes: "mercedes-benz.svg", mercedesbenz: "mercedes-benz.svg",
  renault: "renault.svg", scania: "scania.svg", volvo: "volvo.svg",
};
const TRAILERS = {
  krone: "krone.svg", kogel: "koegel.svg", koegel: "koegel.svg", wielton: "wielton.svg", feldbinder: "feldbinder.svg",
  schmitz: "schmitz-cargobull.svg", schmitzcargobull: "schmitz-cargobull.svg", cargobull: "schmitz-cargobull.svg",
  tirsan: "tirsan.png", schwarzmuller: "schwarzmueller.png", schwarzmueller: "schwarzmueller.png",
};

let companies = null; // norm(name or slug) -> file
export async function loadLogos() {
  if (companies) return companies;
  companies = new Map();
  try {
    const idx = await fetch(`${BASE}/index.json`).then((r) => r.json());
    for (const [slug, name] of Object.entries(idx.companies || {})) {
      companies.set(norm(slug), `${slug}.png`);
      companies.set(norm(name), `${slug}.png`);
    }
  } catch { /* no logo index: text only */ }
  return companies;
}

const firstWordMatch = (map, value) => {
  const n = norm(value);
  if (!n) return null;
  if (map[n]) return map[n];
  for (const [k, v] of Object.entries(map)) if (n.startsWith(k)) return v;
  return null;
};

export const truckLogo = (brandIdOrName) => {
  const f = firstWordMatch(TRUCKS, brandIdOrName);
  return f ? `${BASE}/trucks/${f}` : null;
};
export const trailerLogo = (brandName) => {
  const f = firstWordMatch(TRAILERS, brandName);
  return f ? `${BASE}/trailers/${f}` : null;
};
/** Company logo by game id (e.g. "posped") or display name; needs loadLogos() first. */
export const companyLogo = (...keys) => {
  if (!companies) return null;
  for (const k of keys) {
    const f = companies.get(norm(k));
    if (f) return `${BASE}/companies/${f}`;
  }
  return null;
};

// Cargo categories: first matching rule wins. Keys are matched against the cargo id and name.
const CARGO_RULES = [
  ["frozen", /frozen|ice ?cream|tiefk|gefror/],
  ["hazardous", /explosiv|radioact|hazard|toxic|cyanide|nitrocell|ammunition|fireworks|pyro/],
  ["fuel", /fuel|diesel|gasoline|petrol|kerosene|lpg|lng|propane|butane|oil(?!s)|heating|benzin/],
  ["chemicals", /acid|chlor|chemic|ammonia|hydrogen|nitrogen|oxygen|methanol|ethanol|sulfur|sulph|fertil|pesticide|paint|solvent|resin|glue|caustic|peroxide|lye|polymer/],
  ["medical", /medic|pharma|vaccine|drug|hospital|surgical|pill/],
  ["meat-fish", /meat|beef|pork|chicken|poultry|sausage|fish|salmon|seafood|shrimp/],
  ["chilled", /chilled|dairy|milk|yog|cheese|butter|cream|fresh|refriger/],
  ["drinks", /beverage|beer|wine|water|juice|soda|drink|spirit|vodka|liquor|cider/],
  ["food", /food|fruit|vegetable|apple|banana|potato|onion|carrot|bread|bak|flour|sugar|canned|candy|chocolate|coffee|tea|nut|rice|pasta|egg|honey|grocer|lettuce|tomato|citrus|olive|berry|conserve/],
  ["grain", /grain|wheat|corn|barley|oat|rye|seed|soy|rapeseed|maize|feed/],
  ["agriculture", /tractor|harvester|plough|plow|seeder|sprayer|baler|agri|farm|hay|straw|silage|manure|livestock|animal|cattle|pig|sheep/],
  ["vehicles", /car|vehicle|van|bus|truck|trailer|forklift|boat|yacht|motorcycle|scooter|caravan|chassis/],
  ["construction", /excavat|bulldozer|crane|loader|backhoe|dozer|roller|asphalt|scaffold|construct|drill|pile|grader|dumper|mixer/],
  ["building-materials", /brick|cement|concrete|gravel|sand|stone|tile|plaster|insulat|roof|window|door|glass|marble|granite|beam|panel|girder|pipe|block|lime/],
  ["metal", /steel|iron|alumin|copper|metal|coil|ingot|rebar|scrap|zinc|tin|wire|sheet/],
  ["wood", /log|lumber|wood|timber|plank|board|pallet|cork|bark|pulp/],
  ["paper", /paper|cardboard|carton|newsprint|recycl|waste|garbage|rubbish/],
  ["electronics", /electr|computer|tv|television|phone|appliance|washing|fridge|refrigerator|server|battery|solar|cable|transformer|turbine|generator/],
  ["machinery", /machin|engine|pump|compressor|tool|press|lathe|robot|equipment|part|gear|bearing|valve|motor/],
  ["furniture", /furniture|sofa|chair|table|bed|mattress|kitchen|wardrobe|cabinet/],
  ["textiles", /cloth|textile|fabric|shoe|apparel|garment|cotton|wool|yarn/],
  ["containers", /container/],
];
const CARGO_LABEL = {
  general: "General cargo", food: "Food", chilled: "Chilled", frozen: "Frozen", "meat-fish": "Meat & fish", drinks: "Drinks", fuel: "Fuel",
  chemicals: "Chemicals", hazardous: "Hazardous", machinery: "Machinery", vehicles: "Vehicles", agriculture: "Agriculture", grain: "Grain & feed",
  wood: "Wood", construction: "Construction", "building-materials": "Building materials", metal: "Metal", electronics: "Electronics",
  medical: "Medical", containers: "Containers", furniture: "Furniture", textiles: "Textiles", paper: "Paper & recycling",
};
export function cargoCategory(cargoId, cargoName) {
  const text = `${cargoId || ""} ${cargoName || ""}`.toLowerCase().replace(/_/g, " ");
  const hit = CARGO_RULES.find(([, re]) => re.test(text));
  const id = hit ? hit[0] : "general";
  return { id, label: CARGO_LABEL[id], src: `${BASE}/cargo/${id}.svg` };
}

// Markup helpers. Brand and company logos sit on a light chip so dark logos stay readable on the dark theme.
const hide = raw(`onerror="this.closest('.logo-chip, .cargo-ico')?.remove()"`);
export const logoChip = (src, alt, size = "md") => (src ? html`<span class="logo-chip logo-chip--${size}" title="${alt}"><img src="${src}" alt="${alt}" loading="lazy" ${hide}></span>` : "");
export const cargoIcon = (cargoId, cargoName, size = "md") => {
  const c = cargoCategory(cargoId, cargoName);
  return html`<span class="cargo-ico cargo-ico--${size}" title="${c.label}"><img src="${c.src}" alt="${c.label}" ${hide}></span>`;
};
