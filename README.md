# VeganAdda

**Find your vegan people.** India-first, mobile-first community website for adults 18+. Blazor, ASP.NET Core, PostgreSQL/EF Core, SignalR and Firebase Authentication (Google sign-in). A small modular monolith; no mobile app is included.

## Design and architecture

The Google Stitch export in `design-reference/` is read-only visual reference, never the application. [Design map and decisions](docs/IMPLEMENTATION_PLAN.md) explain the screen mapping and scope corrections. Forest green, warm cream, terracotta, Plus Jakarta Sans and Material Symbols are retained. Fonts are self-hosted.

- `src/VeganAdda.UI`: reusable Razor Class Library with screens, cards, forms and responsive shell.
- `src/VeganAdda.Contracts`: explicit DTOs plus `ICommunityApi` / `IPlatform` abstractions.
- `src/VeganAdda.Web/VeganAdda.Web`: ASP.NET Core host, Firebase token verification and secure cookie sessions, JSON API, application modules, EF Core migrations and SignalR.
- `src/VeganAdda.Web/VeganAdda.Web.Client`: WebAssembly transport, antiforgery, browser navigation, uploads and realtime adapter.
- `tests/VeganAdda.Tests`: real PostgreSQL service/HTTP/SignalR tests.

Use **.NET 10 LTS**, with current servicing packages. Public routes prerender on the server, then hydrate as Interactive WebAssembly. This gives public discovery initial HTML and keeps general UI independent of a permanent server circuit. SignalR invalidates authenticated client state after committed writes; authorized API queries supply the actual data. A future MAUI host can reuse the UI/contracts with a native auth/transport adapter. Native token exchange is future work; browser cookie handling is kept out of shared components.

## Local development

Prerequisites: .NET 10 SDK, Node.js 22/npm, PostgreSQL 18 (local or Docker), and a browser. The client build bundles Firebase's browser SDK; Rider builds it through the project target.

Development reads `ConnectionStrings:Database` from `src/VeganAdda.Web/VeganAdda.Web/appsettings.Development.json`; it defaults to `127.0.0.1:5432`. EF Core applies pending migrations automatically at startup. Configure the local PostgreSQL server with the `veganadda` database and role, or override the connection string with `ConnectionStrings__Database` for your local credentials.

```powershell
# Optional Docker database (alternative to the local PostgreSQL server on port 5432).
docker compose -f compose.dev.yaml up -d
$env:ConnectionStrings__Database='Host=127.0.0.1;Port=55432;Database=veganadda;Username=veganadda;Password=local-development-only'
# Explicitly enables FICTIONAL local accounts and sample events.
./scripts/dev.ps1 -Fixtures
```

Open [localhost:5080](http://localhost:5080). Sign-in page → Development-only test accounts. `new` exercises onboarding, `aarav` / `ananya` / `rohan` etc. exercise member flows, and `admin` exercises administration. Fixtures are only created in Development with `Development__Seed=true`. `Development__Enabled=true` separately enables test login. Both flags cause a hard startup failure outside Development. Production also refuses a database containing demo accounts or events. **Never use the development database in production.** Production starts empty.

Without fixtures, omit `-Fixtures` and configure Firebase (see below). Development applies EF migrations automatically. Outside Development, run the explicit migration command before starting the web process.

The development script uses `-m:1 /nr:false` and builds before `dotnet run --no-build` to avoid an intermittent WebAssembly task-host failure with older .NET 10 SDKs. Use a current serviced SDK for ongoing development.

## Firebase Authentication setup

The app defaults to the registered Firebase Web app in `veganadda-app`. Its public web configuration is in the host's `appsettings.json` and the deployment settings in `.env.example`. Enable Google under Authentication → Sign-in method, and add `localhost`, `veganadda.com` and `www.veganadda.com` to Firebase Authentication's authorized domains. Google's OAuth consent screen must allow the intended audience/test users. To use a different Firebase project locally, override the web configuration with user-secrets:

```powershell
dotnet user-secrets init --project src/VeganAdda.Web/VeganAdda.Web
dotnet user-secrets set 'Authentication:Firebase:ProjectId' 'YOUR_PROJECT_ID' --project src/VeganAdda.Web/VeganAdda.Web
dotnet user-secrets set 'Authentication:Firebase:ApiKey' 'YOUR_WEB_API_KEY' --project src/VeganAdda.Web/VeganAdda.Web
dotnet user-secrets set 'Authentication:Firebase:AuthDomain' 'YOUR_PROJECT_ID.firebaseapp.com' --project src/VeganAdda.Web/VeganAdda.Web
dotnet user-secrets set 'Authentication:Firebase:AppId' 'YOUR_WEB_APP_ID' --project src/VeganAdda.Web/VeganAdda.Web
```

For local backend token verification, create a Firebase Admin service-account credential in Firebase Console → Project settings → Service accounts → Generate new private key. Keep the downloaded private JSON outside the repository. Set the VeganAdda-specific `FIREBASE_ADMIN_CREDENTIALS_VEGANADDA` variable to its path; the Development server maps it to Google's standard credential variable inside its own process, without changing your PowerShell session or other apps. A production web container receives the credential as a Docker secret; migrations do not need it. On Google-hosted infrastructure, prefer Application Default Credentials instead. The browser Firebase configuration is public client configuration, not a secret.

```powershell
$env:FIREBASE_ADMIN_CREDENTIALS_VEGANADDA='C:\secure\veganadda-firebase-admin.json'
./scripts/dev.ps1
```

The browser signs in with Firebase and sends an ID token to the backend. The server verifies the signature, expiry and project through Firebase Admin, then issues VeganAdda's own HttpOnly, SameSite=Lax cookie. VeganAdda stores a project-scoped Firebase UID and display name, not an email address or Google access token. Cookies last seven days with sliding expiry and are Secure in production; data-protection keys must persist across container replacements. All writes use same-origin antiforgery protection. OTP is not enabled in this MVP; Firebase phone sign-in can be enabled later without replacing this application session/API boundary.

The supplied storage bucket, messaging sender ID and Analytics measurement ID are retained in `appsettings.json` for future integrations. The current browser module initializes Authentication only; Analytics collection is not enabled.

`ADMIN_FIREBASE_UID` is an optional exact Firebase UID and grants administrator access when the account is first created. Never use an email address. Admin pages are at `/admin`. Firebase account deletion is separate from VeganAdda data deletion and must be handled by the user or a later Admin SDK deletion integration.

Real Firebase consent and sign-in still need staging verification with the chosen project and authorized domains.

## Behavior and security

- Anonymous queries return only opted-in public profiles and published events. Profile DTOs never contain account email/subject/private contact fields. Dating is removed server-side unless both subject and authenticated viewer opted in. Private profiles can be discovered by active members. Optional social links are not verification.
- Public visibility and feature consent are independent. Withdrawing either immediately prevents featuring. Profile creation requires an explicit 18+ confirmation.
- One canonical relationship per pair. PostgreSQL advisory transaction locks serialize simultaneous requests and writes. Reciprocal requests accept the connection. Self/duplicate requests are prevented; only recipients accept/decline, only senders cancel, either participant can remove.
- Chat is accepted-connections-only. A `(sender, client UUID)` unique constraint and payload check make retries idempotent. Persistence commits before acknowledgement. Realtime events contain no message content. Reconnect fetches missed messages in ascending batches; older history uses stable ID pagination. A 20-second refresh is a fallback if realtime is unavailable.
- Removing/blocking stops future sends. Existing history stays viewable by its participants; blocked or suspended peer identity is masked. Unblocking does not restore a connection. Reconnecting later retains previous history. Messages are server stored and **not end-to-end encrypted**.
- Account deletion requires a `DELETE` request and explicit confirmation. It removes personal data and complete conversations involving the account (including the other participant's copies), uploaded images, saves and attendance. Reports retain text but deleted reporter/subject foreign keys become null. Unresolved reports are retained until review; resolved reports are deleted after 12 months. After a successful rollout, deployment backups older than 30 days are pruned. If deployments pause, expired bundles may remain until cleanup runs again. The operator must expire any off-host copies within 30 days.
- Suspended accounts fail service access and new SignalR connections. Existing SignalR sockets have no privileged methods and receive no private content; API re-fetches enforce current permissions. Cross-origin hub requests are rejected.
- All writes, uploads and sign-out validate a session-bound antiforgery header. Public pages and API/media responses are not cached. Browser HTML never renders untrusted markup. URLs accept HTTPS only, with host checks for Instagram and LinkedIn.
- Uploads accept JPG/PNG/WebP only, max 2 MB, max 16 megapixels/6000px per side. Skia decodes, normalizes orientation, resizes to 1200px and writes fresh JPEG pixels under random storage names, discarding metadata and animation. Original filenames are not trusted. Media queries enforce visibility and ownership.
- Limits per identity/IP: 15 connection requests/minute, 60 message sends/minute, 30 other writes/minute and 240 API reads/minute. SignalR transport has a separate allowance. Static downloads do not consume API quotas.
- JSON console logs report operational failures without request bodies, messages, tokens or sensitive field values. Keep EF sensitive-data logging disabled. Nginx example disables access logs to avoid storing query strings that could contain personal discovery terms. Use `/health/live` and `/health/ready` with existing monitoring.

## Events and Ekatva

Only admins create/edit events. Publishing requires title, description, verified start/end, time zone, city, venue and organizer. Save, attendance and attendee visibility are separate choices. Public event DTOs expose no attendee identities or attendance counts. Only explicitly visible attendees appear to active signed-in members, with normal block/suspension rules. Attendance is not a ticket or official registration.

Ekatva is an **unpublished development draft** until the owner provides verified details and appropriate image/affiliation rights. No invented official dates or venue are published. Production has no seeded festival or fictional members. Add a verified Ekatva event through `/admin` before launch.

## Tests and validation

Tests create a uniquely named PostgreSQL database, apply migrations, and drop only that test database afterward. The test role needs CREATEDB. `TEST_DATABASE` is a connection to the PostgreSQL maintenance database, not an existing application database.

```powershell
$env:TEST_DATABASE='Host=127.0.0.1;Port=55432;Database=postgres;Username=veganadda'
dotnet restore VeganAdda.sln
dotnet test tests/VeganAdda.Tests -m:1 /nr:false
dotnet publish src/VeganAdda.Web/VeganAdda.Web -c Release -m:1 /nr:false -o artifacts/publish
docker compose --env-file .env.example config --quiet
```

See [progress and launch checklist](docs/PROGRESS.md), [visual QA](design-qa.md), and [VPS inspection](docs/VPS_INSPECTION.md). Browser evidence and verification limits are recorded there. Automated tests are a focused safety net, not a substitute for the Pune pilot.

## VPS deployment (requires explicit authorization)

The existing VPS was inspected read-only; see [inspection evidence](docs/VPS_INSPECTION.md). Existing sites and containers must remain intact. This repository provides an additive Nginx example and an isolated Compose stack, **not an applied server change**.

1. Verify DNS for `veganadda.com` and `www` against the VPS IP, choose the canonical host, and obtain a certificate through the existing Certbot setup. No VeganAdda certificate was present at inspection. Do not enable an SSL vhost that references nonexistent certificate files. Bootstrap a separate HTTP challenge vhost first, issue the certificate, then enable HTTPS. Preserve all existing Certbot renewals and virtual hosts.
2. After authorization, create a separate deployment directory. Copy `.env.example` to `.env`, restrict it to the deployment owner (`chmod 600 .env`), and fill real secrets. Use a strong DB password. Confirm localhost port 5080 and subnet 172.30.87.0/24 remain unused. Set `TRUSTED_PROXY_IP` to the actual host-to-container source; never trust arbitrary forwarded headers.
3. Use the [GitHub Actions deployment guide](docs/GITHUB_DEPLOYMENT.md) for the prepared test → Linux image smoke test → GHCR → SSH workflow. It deploys a tested immutable digest after a backup and explicit migration. Configure `Prod` secrets and the VPS `.env` first. Alternatively set `IMAGE_REF` and build/pull a release manually; building in CI avoids pressure on this shared VPS.
4. `docker compose up -d db` then `docker compose run --rm migrate`. Inspect migration exit status. `docker compose up -d web` also requires a successful migration service. Production never auto-migrates on ordinary startup.
5. Create only `/etc/nginx/sites-available/veganadda.com`, adapted from `deploy/nginx.veganadda.conf.example`; symlink it into `sites-enabled`. The existing VPS defines `$connection_upgrade`, which the example reuses. Run `sudo nginx -t`; reload only after a successful test. Nginx terminates HTTPS; the app trusts only the explicit Docker gateway for forwarded scheme/address.
6. Confirm readiness locally, then HTTPS public browsing, Firebase Google sign-in, correct Secure cookies, connection requests, message delivery/reconnect, uploads and privacy. Confirm the app observes `Request.IsHttps` behind the proxy and there are no redirect loops. Check actual proxy source before changing trusted-proxy configuration.
7. Run the Pune pilot. Measure memory, message throughput and backups on this shared 3.7 GiB VPS. Monitor readiness with the existing Uptime Kuma instance after owner authorization. No monitoring changes were made.

PostgreSQL is not host-exposed. Compose uses named persistent database, media and data-protection key volumes. The application runs as the .NET `app` user. Protect host disk, Docker access and off-host backups because persistent session keys and private data are stored there. `.NET 10` and `postgres:18-bookworm` tags receive servicing updates; resolve and record image digests for each production release. Rebuild routinely for security updates.

Rollback: keep the prior immutable application image/tag, take a backup before every schema migration, and restart the prior web image only if its schema is compatible. Otherwise restore the backup to **new isolated volumes**, verify it, and switch the dedicated upstream. Never run `docker compose down -v` on production or modify another project's volumes. An app rollback alone does not reverse a schema migration.

## Backup and restore

`deploy/backup.sh /secure/backups` stops **only this Compose project's web service** briefly, dumps its PostgreSQL database and archives media plus data-protection keys together, then restarts the web service via a trap. Run from the deployment directory with its `.env`; CI releases use the explicit Compose environment shown in the [deployment guide](docs/GITHUB_DEPLOYMENT.md#operations-and-recovery). The rollout keeps web stopped through migration. Store the backup directory outside the repository. Encrypt and copy it off-host; the operator must remove off-host copies within 30 days.

`deploy/restore.sh /secure/backups/veganadda-TIMESTAMP veganadda-restore-check` restores into a **new** Compose project and refuses existing volumes. Use a free, non-overlapping `DOCKER_SUBNET` and separate `APP_PORT` in an isolated copy of `.env` first. It restores DB/media/keys and leaves web stopped for review. Use the backup's matching image version, run migrations only after compatibility review, start the isolated web service and test Firebase sign-in/media/history before any upstream switch. Use a staging Firebase project for test accounts.

Test a restore before launch, then periodically. Keep the backup outside the source directory. Production backup/restore scripts have not been executed on the live VPS because deployment is not authorized.

## Launch limits

No posts, comments, likes, compatibility scores, business accounts, attachments, groups, calls, presence, typing indicators, read receipts, email or push notifications. Notifications are in-app only. Legal copy and support contact are explicitly marked for owner review. Current admin lists show up to 200 accounts and 100 reports; suitable for the pilot, expand pagination when needed. A MAUI app, native authentication, richer moderation tooling and background notifications are deferred.
