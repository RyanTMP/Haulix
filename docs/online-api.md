# HAULIX online service – API contract (draft)

HAULIX 0.0.x has **no online service**. The client side is prepared so the server can be built against a fixed
contract and the app only needs a real `IOnlineApi` implementation plus `OnlineService.Available = true`.

| Piece | Where |
|---|---|
| Data types | `src/Haulix.Core/Online/OnlineModels.cs` |
| Client interface | `src/Haulix.Core/Online/IOnlineApi.cs` (`NoOnlineApi` = today, `SampleOnlineApi` = developer preview) |
| Service / state | `src/Haulix.Core/Online/OnlineService.cs` |
| UI | `wwwroot/js/pages/soon.js` (VTC and cloud sync pages), Settings → Online |

## Principles

- **Offline first.** Everything works without an account; online is opt-in per feature (for example cloud sync).
- **No passwords in HAULIX.** Sign-in through Discord or Steam (OAuth 2.0 with PKCE, loopback redirect to `http://127.0.0.1:<port>/callback`); HAULIX stores only the refresh token, protected with Windows DPAPI.
- **Idempotent uploads.** Deliveries are identified by their `dedupe_key`, so re-sending is harmless.
- **Small payloads.** Live positions at most every 5 s, only while sharing is on.

## Endpoints (REST, JSON, `Authorization: Bearer <token>`)

| Method | Path | Maps to | Notes |
|---|---|---|---|
| `GET` | `/v1/me` | `MeAsync` | Account incl. `vtcId` |
| `GET` | `/v1/vtcs?q=&language=&region=` | `SearchVtcsAsync` | Public VTC directory |
| `GET` | `/v1/vtcs/{id}` | `GetVtcAsync` | |
| `GET` | `/v1/vtcs/{id}/members` | `GetMembersAsync` | Members only |
| `POST` | `/v1/vtcs/{id}/join-requests` | `RequestJoinAsync` | `{ "message": "…" }` |
| `GET` | `/v1/vtcs/{id}/jobs` | `GetJobsAsync` | Job board |
| `GET` | `/v1/events?vtcId=` | `GetEventsAsync` | Public events when `vtcId` is empty |
| `GET` | `/v1/leaderboards/{metric}?period=week\|month\|all&vtcId=` | `GetLeaderboardAsync` | metric: `km`, `deliveries`, `income`, `score` |
| `POST` | `/v1/sync/deliveries` | `UploadDeliveriesAsync` | Array of logbook rows; returns the number stored |
| `GET` | `/v1/me/export` | `ExportMyDataAsync` | Everything stored about the user as JSON (GDPR Art. 15/20) |
| `DELETE` | `/v1/me` | `DeleteAccountAsync` | Deletes the account and all its data within 30 days (GDPR Art. 17) |

## Terms, consent and privacy

The online services are covered by **Part B of the RyanTMP Software License Agreement** ([LICENSE](../LICENSE)) and the
**HAULIX Privacy Policy** ([PRIVACY.md](../PRIVACY.md)).

- **Consent before sign-in.** The sign-in screen shows both documents; signing in is only possible after the user
  accepted the current `OnlineService.TermsVersion`. The accepted version and date are stored locally
  (`settings.online.acceptedTermsVersion` / `acceptedTermsUtc`, engine command `online.acceptTerms`) and sent to the
  server with the first sign-in, which stores them with the account.
- **New terms.** Raising `TermsVersion` makes HAULIX ask again before the next online request; the server rejects
  requests from accounts that have not accepted the current version (`409 terms_required`).
- **Privacy by default.** Leaderboards, sharing with the VTC and cloud sync are off (`settings.online.*`) until the
  user turns them on. Revoking consent (`online.revokeTerms`) turns them off and signs the user out.
- **Minimum age 16** (or with a guardian's permission) – asked once at sign-in.
- **Before launch:** add the controller's name and postal address to the Privacy Policy, sign a data processing
  agreement with the host, keep request logs for at most 14 days, and have both documents reviewed by a lawyer.

## Turning it on later

1. Implement `IOnlineApi` against the server (`HttpOnlineApi`).
2. Add sign-in (OAuth PKCE loopback) with the terms screen above, and token storage.
3. Set `OnlineService.Available = true` and switch `NoOnlineApi` for the real client.
4. Replace the "coming later" pages in `soon.js` with the live views (the developer-preview views are the starting point).
