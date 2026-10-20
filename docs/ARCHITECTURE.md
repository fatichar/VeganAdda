# Architecture map

VeganAdda uses feature-oriented services inside one ASP.NET Core host, one
PostgreSQL database, and a Blazor WebAssembly UI. Registered-user count alone is
not a reason to add independently deployed services. Concurrency, chat volume,
discovery-query latency, and storage are the measurements that guide scaling.

## Projects and request flow

- **Contracts:** transport-neutral DTOs and typed interfaces, grouped by feature.
- **UI:** Razor screens/components; depends only on Contracts and browser UI APIs.
- **Web.Client:** browser transport, Firebase sign-in, uploads and realtime adapter.
- **Web:** composition root, feature services, policies, persistence and workers.
- **Tests:** real PostgreSQL, HTTP/SignalR tests, query budgets and architecture rules.

HTTP requests use explicit routes in `Features/<Feature>/Endpoints.cs` and call
typed feature operations. `FeatureRequestFilter` enforces the existing CSRF/body
limits and active-account check; authenticated routes also declare authorization.
Resource ownership and business permissions remain inside operations/policies.

Profiles, discovery, saved items and inbox screens use typed clients. Browser
adapters call HTTP; `ServerFeatures` calls the same operations directly in a fresh
scope for each prerender operation. That prevents concurrent DbContext use and
avoids a serialization round-trip. Remaining screens retain `ICommunityApi` and
the thin `CommunityService` path adapter for incremental migration. The HTTP host
does not use the legacy catch-all route. New behavior belongs in feature services.
The legacy connection/conversation list adapters return at most 20 items;
the current inbox uses the paged, typed `IMessagingApi` contract.

## Feature ownership

| Feature | Responsibility and permitted collaboration |
| --- | --- |
| Accounts | Session projection; Firebase exchange remains host infrastructure |
| Profiles | Profile edits/deletion, visibility and public/member projection; uses referral eligibility |
| Discovery | Filtered, paged profile search; uses profile visibility/projection |
| Connections | Requests, acceptance, blocking/reporting; uses profile visibility/projection |
| Messaging | Participant-only history, writes/read markers, paged inbox; composes connection requests |
| Events | Published events, attendance/check-in, submissions and event review |
| Moderation | Administrative/member review workflows; uses Events and Referrals |
| Referrals | Invitation eligibility and scoped ancestry/descendant queries |
| Notifications | Notices and unread projections |
| Saved | Paged saved profiles/events; uses their visibility/projection policies |
| Shared | Request context, common guards/validation, transaction locks, endpoint support |

Shared infrastructure must not depend on concrete features. Foundational features
must not depend on their consumers. Contracts/UI cannot depend on server or EF Core.
`ArchitectureTests` uses NetArchTest.Rules to enforce these boundaries and forbid
feature operations returning `Task<object>`. Keep these tests current when making
an intentional dependency change. A separate project per feature is unnecessary now.

## Privacy and consistency

`ProfileVisibility.Query` governs listing/profile eligibility. Its photo policy
retains the additional owner/reviewer rules; a related pending profile does not
automatically make its photo public. `PeopleProjection` is the member-facing
projection, excluding private coordinates, exact birth details and identity data.
Messaging checks participation, connection state, suspension, blocks and test
profile isolation. UI visibility is never authorization. Preserve the existing
HTTP/privacy lifecycle tests when refactoring these rules.

Pair/user advisory locks and unique constraints protect connection/message writes.
Client message IDs provide write idempotency. Referral eligibility walks only the
relevant lineage; unanchored cycles and revoked/suspended/test ancestors stop trust.
Explicitly verified/admin anchors retain their existing semantics.

## Queries and realtime

Discovery, saved cards, connections and conversation pages perform a bounded number
of queries per page. Filter visibility before counting/paging; order with a stable
tie-breaker. The inbox fetches selected-conversation metadata separately, so a deep
link works when that chat/request is not on the current list page. Message history
retains cursor-based paging. `FeatureQueryTests` verifies representative query
budgets and privacy-aware pagination rather than assuming that a build proves scale.

SignalR sends only invalidation categories, never member IDs/names or message bodies.
The browser coalesces bursts for 150 ms; screens refresh only relevant categories.
Two-minute recovery polling pauses in hidden tabs, and a disconnected hub can be
restarted after automatic reconnect is exhausted. Authorized API queries supply data.

## Push worker and releases

`PushDeliveryLeases` adds nullable message leases, per-device receipts and a partial
pending-message index. A worker claims one message in a short transaction using
`FOR UPDATE SKIP LOCKED`, commits, and sends outside that transaction. A 45-second
delivery budget is shorter than the two-minute lease; expired claims can be recovered.
Completion is fenced by lease ID. Retries wait at least five seconds, stop after
four attempts, and skip devices with successful receipts. Read/blocked/suspended/
expired-message suppression still applies.

Delivery remains at least once: a crash after Firebase acknowledges but before the
receipt is stored can redeliver. Stable message IDs/tags allow client deduplication.
Rolling the migration back preserves chat history but discards lease/receipt state;
stop workers first, and expect that pending work can redeliver after reapplying.
Production migration/deployment remains an explicit release operation.

## Growth decisions

Measure realistic concurrent use and query plans before introducing caches or more
infrastructure. Consider PostGIS/Npgsql NetTopologySuite if radius searches become
expensive, Testcontainers if local database provisioning becomes a recurring obstacle,
and a SignalR backplane/shared media/key storage strategy before adding replicas.
Do not cache member-specific projections without preserving viewer/permission scope.
No claim of 100,000-user throughput follows from the regression tests alone.
