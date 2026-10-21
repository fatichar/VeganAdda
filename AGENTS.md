# Working on VeganAdda

VeganAdda is a launch-stage modular monolith: .NET 10, Blazor Interactive
WebAssembly, ASP.NET Core, EF Core/PostgreSQL, Firebase identity, and SignalR.
Keep one deployment and one database until measured constraints justify more.

## Find the change

| Concern | Owner |
| --- | --- |
| Feature operations and explicit routes | `src/VeganAdda.Web/VeganAdda.Web/Features/<Feature>/` |
| DTOs and typed API interfaces | `src/VeganAdda.Contracts/<Feature>/Contracts.cs` |
| Screens and reusable components | `src/VeganAdda.UI/Pages/`, `Components/` |
| HTTP/browser adapters | `src/VeganAdda.Web/VeganAdda.Web.Client/BrowserFeatures.cs`, `BrowserServices.cs` |
| Prerender adapters | `src/VeganAdda.Web/VeganAdda.Web/Services/ServerFeatures.cs`, `Boundary.cs` |
| Persistence and schema | `src/VeganAdda.Web/VeganAdda.Web/Data/` |
| Push delivery and retention | `src/VeganAdda.Web/VeganAdda.Web/Services/ChatPush.cs`, `DataRetentionWorker.cs` |
| Behavior, query budgets, module boundaries | `tests/VeganAdda.Tests/` |

Read [the architecture map](docs/ARCHITECTURE.md) for dependency rules. Prefer
targeted symbol queries or `rg` over reading all features, generated migrations,
bundled JavaScript, fonts, or location datasets. The canonical solution is the
root `VeganAdda.sln`; confirm the loaded Roslyn project paths before using results.

## Change rules

- Use the globally installed .NET skills when relevant. Match existing SDK,
  framework, UI conventions and architecture; add packages only for a concrete need.
- New operations belong in a feature service with explicit input/result types and
  a named endpoint. Prefer typed browser/prerender clients. `CommunityService`
  is a compatibility adapter, not a place for new business rules.
- UI/contracts must not depend on server or EF Core. Features must not call the
  compatibility dispatcher. Keep foundational features independent of consumers.
- Preserve owner/anonymous/member/admin/moderator distinctions, vegan verification lifecycle,
  bidirectional blocking and test-profile isolation. Profile and photo policy lives
  in `ProfileVisibility`; public projections live in `PeopleProjection`.
- Never expose precise coordinates, exact birth details, identity credentials, or
  message content through another DTO, logs, analytics or realtime invalidation.
- Page/filter before materializing. Batch card projections. DbContext operations
  within a scope are sequential; overlapping prerenders require separate scopes.
- Keep advisory-lock transactions for account/pair writes. Do not hold database
  transactions during external delivery. Push is at least once, not exactly once.
- If a running app locks build outputs, ask the user to stop it. Do not redirect
  output or build/copy the project in another directory to bypass the lock.

## Verify

```powershell
dotnet build VeganAdda.sln --no-restore -m:1 /nr:false
dotnet test tests/VeganAdda.Tests --no-build --no-restore
node --test tests/analytics.test.mjs
git diff --check
```

Restore first when packages change. Test builds/configurations must match.
Database tests require PostgreSQL with permission to create uniquely named test
databases; set `TEST_DATABASE` to the test-server connection string (never a
production server). The default is `127.0.0.1:55432` with role `veganadda`.
Use the existing CI PostgreSQL service or local test instance; do not weaken tests.
For UI changes verify rendered behavior, not just compilation. Report real Firebase
delivery, production rollout and load-test coverage separately. Do not deploy or
write persistent agent memories unless requested.
