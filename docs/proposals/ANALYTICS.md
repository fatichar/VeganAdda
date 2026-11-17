# Proposal: member analytics (DAU, MAU and core product metrics)

Status: draft for review. No code changes; this document only.

## Problem

Umami (`docs/OBSERVABILITY.md`) tracks pageviews on six public paths only. Nothing
records member activity, so DAU/MAU, retention and funnel metrics cannot be computed.

## Must-have metrics

| Group | Metrics |
| --- | --- |
| Engagement | DAU, WAU, MAU, stickiness (DAU/MAU) |
| Growth | New accounts/day, sign-in vs. registration, profile completion rate |
| Trust funnel | Profile created, submitted, approved, vegan-verification requested, verified (with median time between steps) |
| Retention | D1/D7/D30 cohort retention by signup week |
| Core loop | Connection requests sent/accepted, chats started, messages sent (counts only), events created/attended |
| Health | Reports opened/resolved, moderation queue age, push delivery success |
| Acquisition | Public-page visitors and visits to `/signin` (Umami, already present). Successful sign-ins come from approach A's transition counters, not Umami |

## Approach A: server-side activity table + admin dashboard (recommended)

- New table `AccountActivity(AccountId, Day)` with a composite primary key. Rows are
  upserted at most once per account per UTC day from the authenticated request
  pipeline (cached per account/day in memory to avoid a write per request).
  Foreign key to `Account` with cascade delete, so account deletion removes history.
- Metrics that existing tables can derive reliably: signups (`Account.Created`),
  messages sent (`ChatMessage.SentAt`, counts only), events created (`Gathering`),
  reports (`Report.Created`/`ResolvedAt`), verification requests
  (`VerificationRequest.Submitted`/`Resolved`).
- Existing tables cannot reconstruct history for others: `Relationship` keeps only
  current state and the last `Updated` (overwritten on accept, cancel, remove),
  `Attendance` has no join timestamp, `Profile.ReviewedAt` is overwritten by feedback
  and verification decisions, and there is no sign-in history. Add a privacy-safe
  append-only `ActivityCounter(Day, Kind, Count)` table (no account IDs), incremented
  in the same transaction as each transition: sign-in, connection
  requested/accepted, event joined, profile submitted/approved/revoked, verification
  approved. Funnel step timings need per-account transition timestamps; until those are
  added, report funnel counts per day only and drop median-time-between-steps.
  Retention cohorts use `AccountActivity` plus `Account.Created` and are unaffected.
  Counters start at deploy time with no backfill.
- New `Features/Stats` service with explicit input/result types and named endpoints,
  admin-only, per AGENTS.md. Contracts in `VeganAdda.Contracts/Stats`, screen in
  `VeganAdda.UI/Pages` (admin route), charts rendered with inline SVG (no new package).
- Aggregation queries run in the database over date ranges (`GROUP BY day`), never
  materializing rows. Results are cached for a few minutes.
- Exclude both `Account.Demo` and `Profile.TestProfile` (the moderation `test-profile`
  action sets only the latter; `ProfileVisibility` isolates on it), plus suspended
  accounts, by default. For relationships and messages, exclude a row if either
  participant is excluded. Counters take the same exclusion at write time.
- Retention: activity rows are small (about 20 bytes/member/day); keep 400 days via
  `DataRetentionWorker`.

Pros: accurate (no adblock or DNT loss), no third party, no privacy-policy change
beyond one retention line, covers DB-derived metrics. Cons: new migration and screens;
custom UI to maintain.

## Approach B: Umami with member identification

- Extend `analytics.js` so authenticated pages send `umami.identify(<AccountId>)` and a
  small allowlist of named events (`signup`, `profile_submitted`, `verified`,
  `connection_sent`, `message_sent`, `event_joined`) with no custom properties.
- Use Umami's built-in Retention, Funnels and Journeys reports and its existing dashboard.
- Required policy changes:
  - Hook currently blocks identify, named events and private routes: change the
    allowlist and its tests (`tests/analytics.test.mjs`).
  - An internal account ID is pseudonymous personal data. Update the privacy page and
    `OBSERVABILITY.md`, which currently promise no user identification and no custom events.
  - Decide DNT/GPC handling: honoring it (current behaviour) undercounts DAU.
  - Account deletion: Umami cannot easily purge one member's events; document or
    restrict retention.
  - Route redaction first: the hook returns `url.pathname` unchanged, and private
    routes such as `/messages/{Key}` embed both participants' account IDs. Tracking one
    alongside an identified viewer would export the conversation pair. Require
    route-template normalization (e.g. `/messages/:key`) or forbid resource-specific
    private pageviews before relaxing the allowlist.
  - CSP already allows the Umami origin.
- Must never include precise location, birth details, message content or credentials
  in events (AGENTS.md).

Pros: no schema or UI work, mature reports. Cons: client-side undercounting (adblock,
DNT, failed loads), funnel steps limited to emitted events, ID leaves our database,
privacy and legal review needed.

## Comparison

| | A: in-app | B: Umami identify |
| --- | --- | --- |
| DAU/MAU accuracy | High | Medium (client-side loss) |
| DB-derived metrics | Native | Only if emitted as events |
| Privacy change | Minimal (counters hold no account IDs) | Material, incl. route redaction |
| Data leaves our DB | No | Yes (pseudonymous ID) |
| Effort | Medium | Low |
| Ongoing maintenance | Dashboards | Hook and event allowlist |

## Recommendation

Implement A for member metrics and keep Umami unchanged for public traffic and
acquisition. B is a fallback only if admin UI maintenance is unacceptable and the
privacy changes are approved.

## Rollout (A)

1. Migration, activity recorder and transition counters with tests (dedupe per day,
   deletion cascade, retention, test-profile exclusion).
2. Stats service and endpoints with authorization tests (admin only, no PII in DTOs,
   query budgets).
3. Admin dashboard UI, verified in a rendered browser.
4. Docs: `ARCHITECTURE.md`, `OBSERVABILITY.md`, privacy page retention note.

## Open questions

- Which roles may see the dashboard (admin only, or moderators too)?
- Is excluding Demo and TestProfile accounts by default correct?
- Is no backfill for transition counters acceptable?
- Is a 400-day activity retention acceptable?
- Should timezone for "day" be UTC or IST?
