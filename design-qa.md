# Birth details follow-up - 2026-10-20

**final result: passed**

- Birth month and year are required in both profile UI and server validation. Day is optional. Existing profiles without birth details must fill month/year on their next save; missing data is not fabricated.
- Month-only values use month end internally for conservative age calculations. The precision flag prevents a placeholder day from being returned or treated as a birthday.
- Existing full dates retain their known day through the OptionalBirthDay migration. The migration was applied to the existing local fixture database and existing values were verified in the profile response.
- Adding an optional day enables Birthday today highlighting on Discover cards and profile views. Member-facing responses omit birth year, month, day, date and precision flag.
- New form retains existing fonts, cream/forest palette and native accessible controls. Month/year share a row on mobile and stack at 320px. Optional day and birthday explanation are explicit.
- Browser checks: required fields, day omitted save/reload, leap-day invalidation, real birthday badge, zero horizontal overflow at 320/390/1440, no console/page errors. Used the local fictional QA member and restored its original birth details afterward.
- Full suite: 90 passed. Build succeeded; EF model/migration consistency and whitespace checks passed.
- Evidence directory: C:/Users/pravi/.codex/visualizations/2026/10/20/01a157fb-6127-7c13-8b88-60a67d36d4b9/. Files: birth-details-small.png, birth-details-mobile.png, birth-details-desktop.png, birthday-highlight-mobile.png. Harness: birth-details-qa.cjs.

---

# Discover redesign QA - 2026-10-20

**final result: passed**

Scope: the selected Discover mock, implemented in the existing Blazor application, with working country/state/city, nearby radius, age, intention, interest, and non-vegan filters. This pass covers the existing member-data fallback as well as responsive behavior; it does not claim pixel identity with fictional mock content.

## Source and rendered evidence

- Selected source: C:/Users/pravi/.codex/generated_images/01a157fb-6127-7c13-8b88-60a67d36d4b9/exec-b68044b3-5a51-4eac-bd26-d6beb2280827.png (853 x 1844).
- Local implementation: http://localhost:5093/ using the isolated veganadda_discover_qa fixture database.
- Source normalized to 390 x 843; actual browser capture at 390 x 844 CSS pixels, deviceScaleFactor 1. One-pixel ratio difference is intentional; no stretching.
- Combined comparison opened and inspected: C:/Users/pravi/.codex/visualizations/2026/10/20/01a157fb-6127-7c13-8b88-60a67d36d4b9/discover-comparison.png.
- Same evidence directory contains discover-mobile.png, discover-desktop.png, discover-filters-mobile.png, discover-place-mobile.png and discover-radius-mobile.png.
- In-app browser used for live desktop inspection and filtering; isolated Playwright test harness used for simulated browser locations, viewport checks and automated interaction evidence.

## Fidelity and intentional differences

- Typography: retained locally hosted Plus Jakarta Sans and Material Symbols, with readable body text and existing forest heading weights. Mock labels and control hierarchy preserved.
- Layout: city control and Near me share the top row; search and Filters share the next row; active filter chips precede people. Single-column mobile cards, existing responsive desktop grid, full-width card action, mobile bottom sheets and existing bottom navigation.
- Colors: existing cream, forest green, soft neutral and mint tokens retained. Focus outlines remain visible.
- Imagery: existing logo reused. Member-supplied photos render above card content; members without a photo retain initials. Fixture members have no photos, so the photographed fictional mock cards are intentionally represented by initials. No mock portraits or invented ages were added to production data. Photo-present visual polish remains a follow-up data-state check.
- Content: real profile names/bios/interests and adult ages replace mock copy; the local-only fixture banner and sample labels remain visible. An age chip is present in the comparison because it demonstrates the requested age filter. Preserved privacy and missing-data explanations.

## Findings and repairs

1. The first rendered pass exposed a missing scoped-stylesheet link, leaving dialog headings and age controls stacked incorrectly. Added the generated stylesheet in App.razor; recaptured and inspected the corrected panels.
2. Age values entered through the in-app browser did not reliably apply with change-only binding. Changed age inputs to update on input. Re-tested actual filtered rows and cancellation of draft edits.
3. Moved discovery card styling after the base shared rules so mobile card padding is applied consistently. Rebuilt and reran all interaction checks against the final output.

No unresolved P0/P1/P2 findings within the tested scope. Remaining P3/follow-up: verify uploaded-photo crops with representative member photos and screen-reader behavior on physical devices.

## Checks

| Check | Result |
| --- | --- |
| Page identity, nonblank rendering, no framework overlay | Passed |
| Default member city and global fallback | Passed |
| Country/state/city optional, parent changes clear children, manual names supported | Passed |
| Age and mutual dating filters, invalid age feedback, chips and draft cancellation | Passed |
| Non-vegan profiles excluded by default; explicit inclusion works | Passed |
| Radius request in POST body; no coordinates in URL | Passed |
| Search never updates any saved profile fields | Passed (entire profile JSON compared before/after) |
| Permission denial gives location-picker alternative; no saved-coordinate fallback | Passed with simulated browser location/denial |
| Radius boundaries, antimeridian/poles, unknown coordinates, visibility/blocks, pagination | Passed in PostgreSQL integration tests |
| Focus trap, Escape, trigger focus restoration | Passed |
| 320, 390, 1440 CSS widths, panels and main screen | Passed; no horizontal overflow |
| Browser console/page errors | None |
| Full test suite and final build | 77 passed, zero failures |
| EF model/migration consistency and whitespace check | Passed |

Key commands: serialized dotnet test with MSBUILDDISABLENODEREUSE=1 and SkipFirebaseClientBuild=true; discovery-qa.cjs through the bundled Node/Playwright runtime; dotnet ef migrations has-pending-model-changes; git diff --check. Node scripts and screenshots remain outside tracked source.

New BirthDate migration and the preceding precise-coordinate migration were exercised on a fresh test database. Existing production database was not modified. Preview remains local and uses fictional fixtures.

---

# Previous QA record (historical)

# VeganAdda visual and interaction QA

**final result: passed**

Passed for the requested MVP visual language and responsive behavior, with the intentional scope/data differences below. This does not certify production readiness, pixel identity, external OAuth, Linux execution or full assistive-technology coverage.

## Source truth and normalization

- Desktop source: `design-reference/public_discovery_feed/screen.png` (1920 × 3268); exported HTML uses a bounded 1280px layout. Reviewed alongside `code.html` and `warm_earth_social/DESIGN.md`.
- Mobile source: `design-reference/discover_mobile/screen.png` (304 × 1600). This is a full-content design export rather than an Android device screenshot.
- Other supplied screens (onboarding, member profile, connections, chat, events and settings) were mapped in `docs/IMPLEMENTATION_PLAN.md` and reviewed in `artifacts/reference-contact-sheet.png`.
- Browser: Codex in-app browser. Desktop CSS viewport 1280 × 900. Mobile CSS viewport approximately 392 × 852. At the app's existing browser zoom, viewport requests 1024 × 720 and 314 × 682 produced these CSS sizes; DOM geometry was measured rather than assuming the requested physical dimensions.
- Browser screenshot capture padded the CSS content with extra canvas at this zoom: desktop raw 1600 × 3831 vs DOM 1280 × 3065; mobile raw 490 × 5059 vs DOM 392 × 4047. `artifacts/desktop-meta.json` and `mobile-meta.json` record geometry. Crop the extra right/bottom canvas to those measured CSS bounds before comparison; do not stretch the content.
- Implementation evidence: `artifacts/desktop-normalized.png`, `artifacts/mobile-normalized.png`, raw `desktop-discovery-final.jpg` / `mobile-discovery-final.jpg` and `mobile-blocked-chat.jpg`.
- Combined source/implementation comparison inputs actually opened: `artifacts/desktop-comparison-normalized.png` and `artifacts/mobile-comparison-normalized.png`. Desktop columns normalized to 640px width; mobile columns normalized to 304px width. Full-page captures place the live fixed mobile navigation at the viewport boundary within the full-page image; the export depicts it at the bottom of its design frame. This capture artifact is not page-content overlap.

## Fidelity surfaces

| Surface | Evidence and assessment |
| --- | --- |
| Fonts/typography | Actual Plus Jakarta Sans, locally hosted weights 400–800. Hero headline, bold forest headings and smaller terracotta metadata match the supplied hierarchy. Increased body base to 16px and corrected overly tight initial headline tracking. Mobile form/card copy remains readable. |
| Spacing/layout | Desktop 1280px bounded canvas, 48px gutters, two-column people cards; mobile 16px gutters, single-column cards, compact search/filter affordance and fixed bottom navigation. Rounded cards and pill controls follow the export. All measured mobile content fits viewport width without horizontal document overflow. |
| Colors/tokens | Cream #fcf9f3, forest #012d1d / #1b4332, terracotta #9d4225, soft neutral layers and mint accents retained. Clear focus outline, text errors and success notices; no color-only validation. |
| Assets/icons | Supplied portraits and gathering image reused only as labeled development fixtures. Initial avatars follow the mobile export. Self-hosted Material Symbols match the reference icon family; no drawn replacement logo artwork. Placeholder broken logo from the desktop export replaced with the export's mobile eco symbol and wordmark. |
| Copy/content | Product scope overrides fake online counts, verified badges, compatibility rankings, attendance totals, official festival dates and affiliations. Development fixtures are prominently labeled. Legal/support copy explicitly requires owner review. |

Focused checks used the actual DOM card/composer boxes together with the readable mobile viewport captures and full source/implementation boards. Mobile composer bottom was above the fixed navigation after the fix; a standalone blocked-conversation screenshot verifies the closed-chat state.

## Comparison and repair history

1. Initial desktop: corrected headline tracking, body scale and gutters to the design specification. Removed programmatic heading focus outlines while retaining visible keyboard focus on controls.
2. Initial mobile: composer extended under bottom navigation. Reduced the conversation heading region and sized the panel against the usable viewport. Recaptured mobile chat and measured composer/nav rectangles; no overlap remained.
3. Initial mobile discovery: expanded advanced filters consumed too much of the first viewport. Moved advanced controls into a working compact filter panel, kept city and People/Events controls, and recaptured. Selecting Sanctuaries returned the Rohan fixture alone; city selection and no-match search also worked.
4. Form verification: prevent editing while prerendered forms wait for WebAssembly, update text bindings on input, verify profile data and introductions after full reload. Added keyboard focus guards and Escape handling to the connection dialog.
5. Final boards: no outstanding P0/P1/P2 layout or token findings within the tested states. Larger total page length is intentional: five public/six member fixture profiles and actual event discovery vs four decorative reference cards. The development banner, privacy wording and required type filter account for additional mobile vertical space.

## Interaction evidence

| Check | Result |
| --- | --- |
| Page identity and meaningful content | Pass: correct title/route and prerendered discovery, then interactive rendering |
| Framework error overlay / blank page | Pass after repairs |
| Console | Final fresh browser checks clean; one historical WebSocket warning occurred while stopping/replacing the local server during testing. Automated authenticated SignalR delivery passed. |
| Anonymous save → sign-in → intended profile | Pass: private save visible in Saved items |
| Connection introduction → sent request → other account accepts | Pass; introduction text persisted |
| Message send → sent timestamp → reload history | Pass in browser; idempotent retries, pagination, missed-message batches and realtime delivery tested against PostgreSQL/HTTP/SignalR |
| Attendance vs visible attendees | Pass: attending alone left roster empty; checking visibility added the attendee |
| Block → closed chat with existing history → unblock | Pass; peer masked, composer absent, history retained; unblocking did not restore the connection |
| Profile editing / 18+ confirmation | Pass with fictional Development account; name, bio and interests persisted across reload |
| Administration | Pass: administrator accessed events, saved Ekatva draft, left it unpublished |
| Mobile viewport | Pass: 392 × 852 CSS, no document horizontal overflow, fixed navigation and composer controls visible |

## Deliberate differences and remaining coverage

- Production starts empty. Ekatva needs verified owner-supplied details before publication; no fake festival date/venue/count is used to mimic the screenshot.
- Profile fields and cards use the supplied product scope; no verified identity, presence, mutual-connection counts, compatibility, posts, group chat or push/mute controls are implied.
- Additional required pages extend the existing visual system; the export has no saved/admin/notification/legal/error references.
- Full screen-reader audit, Android hardware/software keyboard, browser offline UI retry, real Google consent/callback and production HTTPS are not fully verified. Automated duplicate retry, server permissions and authenticated SignalR checks passed. Linux-native upload decoding and live backup restore require staging.

## Implementation checklist

- [x] Inspect/export map before building
- [x] Preserve layout, font, tokens and supplied assets
- [x] Responsive desktop/mobile rendering and real interactions
- [x] Repair critical browser-found issues, recapture and compare
- [x] Record release/tests/deployment constraints honestly
- [ ] Complete the owner and staging checks in `docs/PROGRESS.md` before public launch
