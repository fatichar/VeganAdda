# VeganAdda implementation plan

## Reference inspection (2026-10-17)
`design-reference/` is read-only. The ZIP has already been extracted; no ZIP is present. There are eight screen PNGs with HTML, a duplicate desktop discovery HTML, four standalone raster assets, and `warm_earth_social/DESIGN.md`.

| Export | Application mapping |
| --- | --- |
| public_discovery_feed / veganadda_community_platform | Public and signed-in discovery; two-column desktop cards, city tabs, spotlight, visitor CTA |
| discover_mobile | Single-column cards, compact filters, persistent bottom navigation |
| sign_in_profile_setup | Google sign-in and short profile form, intent checkboxes |
| member_profile_ananya | Profile detail, interests, intentions, external links |
| connections_manager | Received, sent and connected tabs, introductions |
| chat_rohan | Text conversation, timestamps, composer |
| community_events_ekatva_festival | Featured event, event list, attendance and private attendee visibility |
| settings_privacy | Profile edit, visibility, dating, blocked members, deletion |

Missing: saved items, notifications, admin, report forms, legal/help, loading/error/empty states. Extend the same components and tokens. Supplied portraits and festival art are demonstration assets, not real members or official festival photography. Seed fixtures are Development-only and visibly marked. Production starts empty. The export's dates, venues, verification badges, presence, compatibility language, muted/push controls and invented counts are excluded.

## Architecture decisions
1. .NET 10 LTS, ASP.NET Core modular monolith, EF Core/PostgreSQL. Use current servicing packages; Docker runtime follows .NET 10 servicing.
2. Blazor Web App with prerendered Interactive WebAssembly. Server-rendered discovery is available before WASM downloads; UI interactivity does not require a long-lived Blazor Server circuit. SignalR is only for chat/notification invalidation. Tradeoff: larger first download. No Auto mode complexity.
3. Razor Class Library owns reusable screens and components; Contracts owns DTOs and service interfaces. Browser adapter owns cookies, CSRF transport and SignalR. Server adapter invokes the same application service for prerendering. Future MAUI supplies its own authenticated transport and platform services; no mobile app is included.
4. Firebase Authentication in the browser; backend verifies project-scoped Firebase ID tokens with Firebase Admin, then issues an opaque HttpOnly SameSite=Lax VeganAdda cookie. No provider token is persisted. Same-origin JSON API with antiforgery header on all mutations. No cross-origin API enabled. Firebase phone provider can be enabled later while retaining the same account/session boundary.
5. Explicit public DTO projection; private intents removed before serialization. Dating only when both viewer and subject opted in. Blocks are bidirectional exclusions. Nonpublic profiles available to active authenticated members, not visitors.
6. Canonical pair key and PostgreSQL transaction advisory lock serialize requests, acceptance, removal, blocking and message writes. Sender/client-message UUID uniqueness makes retries idempotent. Persist before acknowledgement. Reconnect invalidates and refetches persistent state.
7. Removing/blocking closes messaging immediately at the service boundary. Existing history remains viewable only by its participants, with blocked identities masked; suspension prevents all account API/hub access. Reconnecting later does not erase history. Account deletion removes the account's profile, relationships, saves, attendance and messages; abuse reports retain no deleted identity.
8. Curated events only; unpublished Ekatva draft has no assumed dates/venue/affiliation. Publishing requires complete verified details. Event attendance is not registration.
9. Production requires PostgreSQL, persistent media and data-protection keys, HTTPS, Firebase web configuration and a private Admin SDK credential. No local dev authentication or seed configuration is accepted outside Development. No production deployment authorized.

Current implementation and module ownership: [architecture map](ARCHITECTURE.md).

## Vertical slices
- [x] Foundation, tokens, responsive navigation and API contracts
- [x] Authentication, onboarding, profile editing and uploads
- [x] Public/member discovery and visibility
- [x] Connection transitions, block/report
- [x] Persistent chat, retry, reconnect, in-app notifications
- [x] Events, saves, simple admin and settings
- [x] Migrations, focused integration tests, browser QA, deployment and operations docs

Live OAuth, GitHub/VPS deployment and launch verification remain setup-dependent; see PROGRESS.md.

## Verification
Compile Release; run focused tests against an isolated local PostgreSQL cluster (installed PostgreSQL 18 is available; Docker daemon is currently stopped). Test real browser flows at desktop and Android-sized widths, including two users, onboarding, filtered public results, save/connect/accept, chat/retry, block and attendance. Check permission and DTO tests for anonymous, non-dating, suspended and blocked users. Record external OAuth/VPS verification separately.
