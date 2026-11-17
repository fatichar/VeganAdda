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
| Acquisition | Public-page visitors, sign-in conversion (Umami, already present) |

## Approach A: server-side activity table + admin dashboard (recommended)

- New table `AccountActivity(AccountId, Day)` with a composite primary key. Rows are
  upserted at most once per account per UTC day from the authenticated request
  pipeline (cached per account/day in memory to avoid a write per request).
  Foreign key to `Account` with cascade delete, so account deletion removes history.
- All other metrics derive from existing tables (`Account.Created`,
  `Profile.ReviewedAt`, `VerificationRequest`, `Relationship`, `ChatMessage.SentAt`,
  `Gathering`, `Attendance`, `Report`). Only counts are read; message content is never
  touched.
- New `Features/Stats` service with explicit input/result types and named endpoints,
  admin-only, per AGENTS.md. Contracts in `VeganAdda.Contracts/Stats`, screen in
  `VeganAdda.UI/Pages` (admin route), charts rendered with inline SVG (no new package).
- Aggregation queries run in the database over date ranges (`GROUP BY day`), never
  materializing rows. Results are cached for a few minutes.
- Test-profile (`Demo`) and suspended accounts are excluded or filterable.
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
| Privacy change | Minimal | Material |
| Data leaves our DB | No | Yes (pseudonymous ID) |
| Effort | Medium | Low |
| Ongoing maintenance | Dashboards | Hook and event allowlist |

## Recommendation

Implement A for member metrics and keep Umami unchanged for public traffic and
acquisition. B is a fallback only if admin UI maintenance is unacceptable and the
privacy changes are approved.

## Rollout (A)

1. Migration and activity recorder with tests (dedupe per day, deletion cascade,
   retention).
2. Stats service and endpoints with authorization tests (admin only, no PII in DTOs,
   query budgets).
3. Admin dashboard UI, verified in a rendered browser.
4. Docs: `ARCHITECTURE.md`, `OBSERVABILITY.md`, privacy page retention note.

## Open questions

- Which roles may see the dashboard (admin only, or moderators too)?
- Are Demo/test accounts excluded by default?
- Is a 400-day activity retention acceptable?
- Should timezone for "day" be UTC or IST?
