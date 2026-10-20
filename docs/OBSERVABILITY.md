# Production logging, monitoring and alerts

## Logging

The web host uses Serilog.AspNetCore 10 with compact JSON on stdout. Existing
`ILogger<T>` calls flow through Serilog. Docker retains three 10 MB web log files;
this is local troubleshooting storage, not a central log archive.

Each request completion includes method, route template, status, elapsed milliseconds,
request ID, application and environment. Trace/span IDs are included when an Activity
exists. Route values, query strings, request bodies, cookies, tokens, IP addresses and
member identities are not added by request logging. Successful health checks are Debug
(hidden by default), 4xx are Warning, and 5xx are Error. API failures include a safe
exception type and stack trace without exception messages or data values. Keep EF
sensitive-data logging disabled and review new log statements for private data.

Startup failures produce a Fatal event and shutdown flushes the logger. The process
also rethrows startup exceptions to preserve a nonzero exit; runtime stderr can contain
exception details and must have the same restricted access as application logs. Failures before the final logger is built are console-only; external uptime monitoring detects the resulting outage.

```sh
docker compose logs --since 30m web
```

Configure verbosity using `Serilog__MinimumLevel__Default` or category overrides such
as `Serilog__MinimumLevel__Override__Microsoft`. Do not enable broad Debug/Trace logging
in production. The previous `Logging:LogLevel` settings have been replaced.

## Better Stack setup

Uptime Kuma is intentionally unchanged. Better Stack provides monitoring from outside
this VPS. Its free personal-project plan currently includes email/Slack alerts and
3 GB of logs retained for 3 days; recheck [current pricing](https://betterstack.com/pricing)
before enabling paid features.

1. In Better Stack, create a VeganAdda .NET/Serilog log source. Copy its source token
   and HTTPS ingestion origin into `/opt/veganadda/.env` as `BETTERSTACK_SOURCE_TOKEN`
   and `BETTERSTACK_ENDPOINT`. Both must be set together. Keep tokens out of Git,
   public Actions variables, browser code, and screenshots.
2. The official `BetterStack.Logs.Serilog` sink forwards Warning and above from app
   categories and request completions. A property allowlist removes raw request scopes,
   exception messages, exception objects and unrelated properties. Framework logs stay
   local. Queue memory is capped at 5 MB, retries at 3, and shutdown flushing at 5 seconds.
   Delivery is best effort; keep stdout/Docker logs as the local fallback.
3. Review `deploy/betterstack-monitors.json` with
   `pwsh -File scripts/configure-betterstack.ps1`. It defines public readiness/liveness
   checks at a 180-second interval, 10-second timeout, a 180-second failure confirmation,
   and a 180-second recovery period. Readiness also checks certificate expiry at 14 days.
4. Set a team-scoped `BETTERSTACK_UPTIME_API_TOKEN` in your shell and run the same script
   with `-Apply`. It creates missing monitors, preserves existing ones, and does not
   store the token. This API token is distinct from the log source token and is never
   needed by the web container. Email is enabled; phone, SMS and push are disabled.
5. In the log source, create these initial alert rules. Select fields from a real ingested
   event in Live tail (the sink places structured fields under `properties`). Route to
   your owner-controlled email or an explicitly connected Slack channel.

| Signal | Initial rule | Action |
| --- | --- | --- |
| Host failure | Any Fatal in 5 minutes | Inspect startup/configuration and Docker logs |
| Request failures | At least 5 Error completions in 5 minutes | Investigate route template and request/trace IDs |
| Background work | At least 3 `Chat push batch failed` or `Data retention cleanup failed` warnings in 15 minutes | Inspect Firebase/database configuration and worker recovery |
| Backup freshness (later) | No verified daily-backup heartbeat in 26 hours | Inspect backup job; send heartbeat only after backup success |
| Host pressure (later) | Disk above 85% for 10 minutes, or OOM/repeated restarts | Add host metrics/agent only after reviewing resource needs |

Thresholds are starting points to tune during the pilot. Log rules and notification
routing must be saved in the account; application code alone does not create them.
After deploying, confirm a safe operational event appears in Live tail. Verify failure
and recovery notifications using staging, without stopping production just for testing.
No account tokens, live monitors, alert rules, or notification delivery have been
configured or verified by these changes.

Reference: [official Serilog integration](https://betterstack.com/docs/logs/net-c/),
[monitor API](https://betterstack.com/docs/uptime/api/create-a-new-monitor/).

## Umami analytics

The existing self-hosted instance is `https://analytics.greenfit.in`. A separate
**VeganAdda** property was created on 2026-10-21, owned by the same administrator as
Greenfit, with session recording disabled:

- Script: `https://analytics.greenfit.in/script.js`
- Website ID: `29d3948e-23fe-4394-b433-799a76a4b09d`

The script is included only in Production when both settings are present. It tracks
only `/`, `/events`, `/signin`, `/privacy`, `/terms`, and `/help`. The before-send hook
strips query strings, fragments, referrers, personal titles and custom data, and blocks
private routes, identify/session payloads and named events. DNT and GPC signals suppress
pageviews. No replays, user identification or custom click events are enabled. Loading
the tracker still makes a network request to the analytics host; do not describe this
as preventing all infrastructure processing.

Compose reads `UMAMI_SCRIPT_URL` and `UMAMI_WEBSITE_ID` from the deployment environment.
CI supplies these public values from optional `Prod` variables with the values above as
defaults, writing a release-local `analytics.env`. The VPS `.env` continues to hold secrets.
Manual releases can use the values in `.env.example`. Set both analytics variables to
empty in a manual deployment to disable tracking; for CI, remove the defaults as well
when disabling analytics globally.

CSP allows only the configured HTTPS analytics origin for scripts and connections.
The app privacy page explains analytics and operational monitoring. Deployment and a
real browser pageview visible in the Umami dashboard remain required validation steps.

Reference: [Umami tracker configuration](https://docs.umami.is/docs/tracker-configuration).

## Aspire decision

Keep Docker Compose for this web app and PostgreSQL deployment. Aspire can be evaluated
later for local orchestration and OpenTelemetry diagnostics as dependencies grow.
Its standalone dashboard can be used without adopting AppHost, but it targets development
and short-term diagnostics. It does not replace production telemetry retention,
external availability monitoring or critical notification delivery.

References: [Serilog ASP.NET Core](https://github.com/serilog/serilog-aspnetcore),
[Aspire standalone dashboard](https://aspire.dev/dashboard/standalone/).
