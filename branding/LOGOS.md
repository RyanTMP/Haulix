# Logo library

HAULIX shows logos for truck brands, trailer brands, the companies you haul for in ETS2 and cargo categories.
The files here are the originals; `tools/brand/sync-logos.ps1` copies them into the app
(`src/Haulix.App/wwwroot/assets/logos`, company logos scaled down) and writes `index.json`.

| Folder | Contents | Source |
|---|---|---|
| `truck-brands/` | DAF, Iveco, MAN, Mercedes-Benz, Renault Trucks, Scania, Volvo | Wikimedia Commons / Wikipedia (see below) |
| `trailer-brands/` | Krone, Kögel, Wielton, Feldbinder, Schmitz Cargobull, Tirsan, Schwarzmüller | Wikimedia Commons, manufacturers' websites |
| `companies/` | 201 job companies of Euro Truck Simulator 2 (`_index.tsv`: file, name, source URL) | Truck Simulator Wiki (Fandom), images from the game |
| `cargo/` | 23 cargo category icons (food, chilled, frozen, fuel, chemicals, machinery …) | Made for HAULIX from Lucide icons (ISC) |

## Owners and rights

**None of the brand and company logos belong to HAULIX or RyanTMP.** They are trademarks and/or copyrighted works of
their owners and are used only to identify the brands and companies in the game. They are **not** covered by the
HAULIX license, and HAULIX is not affiliated with or endorsed by any of these companies.

| File | Owner | Source | Copyright status of the file at the source |
|---|---|---|---|
| truck-brands/daf.svg | DAF Trucks N.V. | commons.wikimedia.org/wiki/File:DAF_logo.svg | Public domain (text logo); trademark of DAF |
| truck-brands/iveco.svg | Iveco Group N.V. | commons.wikimedia.org/wiki/File:Iveco_Logo_2023.svg | Public domain (text logo); trademark of Iveco |
| truck-brands/man.svg | MAN Truck & Bus SE | commons.wikimedia.org/wiki/File:MAN_Truck_%26_Bus_-_Logo.svg | Public domain (text logo); trademark of MAN |
| truck-brands/mercedes-benz.svg | Mercedes-Benz Group AG | commons.wikimedia.org/wiki/File:Mercedes-Benz_Star_2022.svg | Public domain (simple geometry); trademark of Mercedes-Benz |
| truck-brands/renault.svg | Renault Trucks SAS | commons.wikimedia.org/wiki/File:Renault_Trucks_logo.svg | Public domain (text logo); trademark of Renault |
| truck-brands/volvo.svg | Volvo Trademark Holding AB | commons.wikimedia.org/wiki/File:Volvo_logo.svg | Public domain (text logo); trademark of Volvo |
| truck-brands/scania.svg | Scania CV AB | en.wikipedia.org/wiki/File:Scania_Logo.svg | **Not free** – copyrighted and trademarked by Scania (used on Wikipedia under fair use) |
| trailer-brands/krone.svg | Fahrzeugwerk Bernard Krone GmbH & Co. KG | commons.wikimedia.org/wiki/File:KRONE_NFZ_Logo_fbg_2023.svg | Public domain (text logo); trademark of Krone |
| trailer-brands/koegel.svg | Kögel Trailer GmbH | en.wikipedia.org/wiki/File:Kögel_Fahrzeugwerke_logo.svg | Public domain (text logo); trademark of Kögel |
| trailer-brands/wielton.svg | Wielton S.A. | commons.wikimedia.org/wiki/File:WieltonGroup.svg | Public domain (text logo); trademark of Wielton |
| trailer-brands/feldbinder.svg | Feldbinder Spezialfahrzeugwerke GmbH | feldbinder.com (website header) | **Not free** – © and trademark of Feldbinder |
| trailer-brands/schmitz-cargobull.svg | Schmitz Cargobull AG | cargobull.com (website header) | **Not free** – © and trademark of Schmitz Cargobull |
| trailer-brands/tirsan.png | Tırsan Treyler A.Ş. | tirsan.com.tr (website header) | **Not free** – © and trademark of Tırsan |
| trailer-brands/schwarzmueller.png | Schwarzmüller Gruppe | schwarzmueller.com (website header) | **Not free** – © and trademark of Schwarzmüller |
| companies/*.png | SCS Software s.r.o. (fictional companies of Euro Truck Simulator 2) | truck-simulator.fandom.com | **Not free** – game artwork © SCS Software |
| cargo/*.svg | RyanTMP (HAULIX), based on Lucide icons (ISC) | – | © RyanTMP; icons ISC |

Kässbohrer trailers exist in ETS2 too; their logo could not be obtained and HAULIX shows the trailer without a logo.

If a rights holder asks for a logo to be removed, delete the file here, run `tools/brand/sync-logos.ps1` and
release an update – HAULIX then simply shows the name without a logo.
