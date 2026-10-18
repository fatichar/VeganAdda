# Progress and launch readiness

## Implemented
- Separate .NET 10 Blazor Web App, shared UI RCL and contracts; read-only Stitch reference retained.
- Responsive discovery, city/type/interest/intention/search filters, stable pagination and empty/error states.
- Firebase ID-token verification and VeganAdda cookie sessions, development-only fixture sign-in, onboarding with server-side 18+ confirmation, editable profile and validated image uploads.
- Server-side public profile and mutual dating projections, bidirectional blocks and suspended-user exclusion.
- Requests with introductions, sent/received/connected views, accept/decline/cancel/remove, report and unblock.
- Persistent one-to-one chat, sender UUID idempotency, conversation previews/unread counts, history pagination, failed-message retry, SignalR invalidation, reconnect catch-up and polling fallback.
- Curated events, independent attendance/attendee visibility, event attendees and private people/event saves.
- In-app notifications, settings, account deletion and simple protected event/report/member administration.
- EF Core migration, Docker/Compose, persistent volumes, health endpoints, structured logs, additive Nginx example and backup/restore scripts.
- Read-only inspection of the supplied VPS; [findings](VPS_INSPECTION.md).
- GitHub Actions workflow based on Dinzer: PostgreSQL tests, Linux image/upload smoke test, GHCR digest, pinned SSH host keys, isolated release directories, pre-migration backup and readiness gate. [Setup](GITHUB_DEPLOYMENT.md).

## Verification record
Focused PostgreSQL tests cover permission/visibility transitions, concurrent reciprocal/duplicate requests, duplicate sends, unauthorized history/sends, removal/blocking, old/missed-message pagination, attendee consent, saves, moderation and deletion. HTTP tests cover CSRF, safe uploads, public/signed-in prerendering, test-login disablement and authenticated SignalR invalidation. Additional regression tests cover rate limits independently of static downloads.

Before the Firebase auth switch, the Release build/publish and all 16 PostgreSQL/service/HTTP/SignalR tests passed. The current Firebase change builds in Release with no warnings; the updated PostgreSQL integration suite is pending because this workstation's local database connection is unavailable to the test process. Existing browser flow evidence covers public discovery, development sign-in, adult onboarding/edit/reload, filters/empty states, request/accept/chat/reload, blocking, attendance/attendee consent, saves and admin draft editing. The owner has now confirmed real local Firebase Google sign-in and server token verification work. Production-domain/HTTPS sign-in still needs staging verification. Screenshots are local ignored artifacts. See root `design-qa.md` for visual comparison and verification limits.

## Setup-dependent / not yet live verified
- Real Firebase Google sign-in, authorized-domain/provider configuration, browser session refresh with real Firebase identity.
- Production DNS, certificate issuance, trusted-proxy verification and HTTPS session/SignalR testing.
- First GitHub Actions run, Docker image execution and Linux-native image decoding (Docker daemon is stopped on this workstation; CI includes this smoke test).
- Live VPS deployment, resource soak, backup/restore rehearsal and Pune community pilot.
- Owner-approved legal text, grievance/support contact, report retention, image/font rights and official event data.

## Launch checklist
- [ ] Owner reviews terms/privacy, retention, grievance and feedback contacts and replaces draft copy.
- [ ] Configure Firebase Google provider and production authorized domain; bootstrap a known Firebase admin UID.
- [ ] Use a fresh production database with zero fictional profiles/events. Configure verified Ekatva event separately.
- [ ] Verify event dates, time zone, venue, organizer, registration link, image rights and affiliation wording.
- [ ] Resolve domain, obtain a certificate, and test the separate Nginx vhost without changing existing sites.
- [ ] Validate trusted proxy and Secure cookies; verify Firebase sign-in, sign-out and reload across a restart.
- [ ] Test two real accounts on Android browsers: public browsing → onboarding → save → request → accept → chat → reconnect → block/remove.
- [ ] Test legal/accessibility copy and keyboard flow with a screen reader; test phone software keyboard and poor network.
- [ ] Run image/Compose in Linux staging; verify native image decoding and media permissions.
- [ ] Rehearse database + media + key backup/restore into isolated volumes; encrypt and copy off-host; set retention.
- [ ] Run a load/soak test appropriate to the Pune pilot and monitor the shared VPS's resource use.
- [ ] Obtain explicit production deployment authorization.

## Deferred and practical limits
No mobile app, native token flow, posts/comments/likes, business accounts, compatibility scores, attachments, group chat, calls, typing indicators, presence, read receipts, email or push. No working controls imply those features exist. Chat retries remain in memory while the conversation page is open; unsent text does not survive browser reload. Initial WASM download is larger than static-only browsing. Admin views cap members at 200 and reports at 100. Report review is manual. Feedback/contact is an owner configuration task. New uploads not attached to a profile/event can accumulate; add periodic orphan-media cleanup as use grows. Cookie keys are persisted on disk and require host/backup access controls. No production services have been deployed.
