# RyanTMP legal kit – reuse for new software

HAULIX uses the **RyanTMP Software License Agreement** ([../LICENSE](../LICENSE)). It is written for *all* software by
RyanTMP, so a new program does not need a new licence – only a short product schedule.

## For a new program

1. **Copy the licence.** Put the current `LICENSE` (RyanTMP Software License Agreement) into the new project unchanged.
2. **Add a schedule.** Replace "Schedule 1 – HAULIX" with a schedule for the new program, using
   [PRODUCT-SCHEDULE-TEMPLATE.md](PRODUCT-SCHEDULE-TEMPLATE.md). Keep the numbering if you ship several products
   together (Schedule 1, 2, …).
3. **Privacy policy.** Copy [../PRIVACY.md](../PRIVACY.md), adjust section 1 to what the program really does with data
   (files it reads, network requests, voices/downloads) and keep section 2 if it will have online features.
4. **Notices.** Copy [../NOTICE.md](../NOTICE.md) (copyright, name/logo, trademarks of others) and create a
   `THIRD-PARTY-NOTICES.md` listing every library, font, icon set and asset with its owner and licence.
5. **Show it.** Let the installer show the licence and privacy policy with a consent checkbox (see
   `src/Haulix.Installer/ui/installer.js`), and link both documents in the program's About page.
6. **Metadata.** Set `<Copyright>Copyright © YEAR RyanTMP</Copyright>` and `<Authors>RyanTMP</Authors>` in the project
   files.

## When you start charging for something

Part C of the licence (paid features, subscriptions, withdrawal right) is already in place. Before the first sale:

- use a payment provider that handles VAT/OSS for you (for example Paddle or Lemon Squeezy as merchant of record) or
  register for VAT yourself;
- show price, term, cancellation and the **withdrawal information** at checkout, and ask for consent to the immediate
  start of digital content (§ 356 (5) German Civil Code);
- add an **imprint (Impressum)** with your name and a contact address to the website (§ 5 DDG), and your name and
  address to the Privacy Policy;
- have the texts reviewed by a lawyer or a legal-text service (for example IT-Recht Kanzlei, Händlerbund, eRecht24).

## When you change the licence

Raise the version at the top of `LICENSE`, update `OnlineService.TermsVersion` (online consent) and the
`LICENSE_SINCE` version in the installer, so users are asked to accept the new version on their next update.

> These documents are carefully written templates, not legal advice.
