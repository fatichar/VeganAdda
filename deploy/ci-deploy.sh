#!/usr/bin/env bash
# GitHub runner only. No production configuration or credentials are copied.
set -euo pipefail
[[ "$GITHUB_SHA" =~ ^[a-f0-9]{40}$ ]]
[[ "$IMAGE_REF" =~ ^ghcr\.io/[a-z0-9._-]+/veganadda-web@sha256:[a-f0-9]{64}$ ]]
[[ "$GHCR_USER" =~ ^[a-zA-Z0-9][a-zA-Z0-9_-]*(\[bot\])?$ ]]
ssh_options=(-i "$RUNNER_TEMP/veganadda-key" -o BatchMode=yes -o StrictHostKeyChecking=yes
  -o "UserKnownHostsFile=$RUNNER_TEMP/veganadda-known-hosts" -o ConnectTimeout=30
  -o ServerAliveInterval=15 -o ServerAliveCountMax=4)
destination="$VPS_USER@$VPS_HOST"
release="/opt/veganadda/releases/$GITHUB_SHA"
# Public analytics settings travel with the release; monitoring tokens stay in VPS .env.
analytics_env="$RUNNER_TEMP/veganadda-analytics.env"
: > "$analytics_env"
if [[ -n "${UMAMI_SCRIPT_URL:-}" || -n "${UMAMI_WEBSITE_ID:-}" ]]; then
  [[ "$UMAMI_SCRIPT_URL" =~ ^https://[a-zA-Z0-9.-]+(/[a-zA-Z0-9._~/%-]*)?$ ]]
  [[ "$UMAMI_WEBSITE_ID" =~ ^[a-fA-F0-9]{8}-[a-fA-F0-9]{4}-[a-fA-F0-9]{4}-[a-fA-F0-9]{4}-[a-fA-F0-9]{12}$ ]]
  printf 'UMAMI_SCRIPT_URL=%s\nUMAMI_WEBSITE_ID=%s\n' "$UMAMI_SCRIPT_URL" "$UMAMI_WEBSITE_ID" > "$analytics_env"
fi
ssh "${ssh_options[@]}" -p "$VPS_PORT" "$destination" \
  "test -d /opt/veganadda && test -r /opt/veganadda/.env && mkdir -p '$release'"
scp "${ssh_options[@]}" -P "$VPS_PORT" compose.yaml deploy/backup.sh deploy/restore.sh deploy/rollout.sh deploy/check-ready.sh \
  "$destination:$release/"
scp "${ssh_options[@]}" -P "$VPS_PORT" "$analytics_env" "$destination:$release/analytics.env"
# Token goes over SSH stdin, never in a remote command or persistent Docker config.
printf '%s' "$GHCR_TOKEN" | ssh "${ssh_options[@]}" -p "$VPS_PORT" "$destination" \
  "bash '$release/rollout.sh' '$IMAGE_REF' '$GHCR_USER'"
