# HAULIX online service – API contract (draft)

HAULIX 0.0.x has **no online service**. The client side is prepared so the server can be built against a fixed
contract and the app only needs a real `IOnlineApi` implementation plus `OnlineService.Available = true`.

| Piece | Where |
|---|---|
| Data types | `src/Haulix.Core/Online/OnlineModels.cs` |
| Client interface | `src/Haulix.Core/Online/IOnlineApi.cs` (`NoOnlineApi` = today, `SampleOnlineApi` = developer preview) |
| Service / state | `src/Haulix.Core/Online/OnlineService.cs` |
| UI | `wwwroot/js/pages/soon.js` (VTC + Online pages), Settings → Online |

## Principles

- **Offline first.** Everything works without an account; online is opt-in per feature (live position sharing, cloud sync).
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
| `PUT` | `/v1/live/me` | `SendPositionAsync` | `LivePosition`; 204 |
| `GET` | `/v1/live?scope=friends\|vtc` | `GetLivePositionsAsync` | Positions younger than 60 s |
| `POST` | `/v1/sync/deliveries` | `UploadDeliveriesAsync` | Array of logbook rows; returns the number stored |

## Turning it on later

1. Implement `IOnlineApi` against the server (`HttpOnlineApi`).
2. Add sign-in (OAuth PKCE loopback) and token storage.
3. Set `OnlineService.Available = true` and switch `NoOnlineApi` for the real client.
4. Replace the "coming later" pages in `soon.js` with the live views (the developer-preview views are the starting point).
