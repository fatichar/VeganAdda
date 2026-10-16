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
ssh "${ssh_options[@]}" -p "$VPS_PORT" "$destination" \
  "test -d /opt/veganadda && test -r /opt/veganadda/.env && mkdir -p '$release'"
scp "${ssh_options[@]}" -P "$VPS_PORT" compose.yaml deploy/backup.sh deploy/restore.sh deploy/rollout.sh \
  "$destination:$release/"
# Token goes over SSH stdin, never in a remote command or persistent Docker config.
printf '%s' "$GHCR_TOKEN" | ssh "${ssh_options[@]}" -p "$VPS_PORT" "$destination" \
  "bash '$release/rollout.sh' '$IMAGE_REF' '$GHCR_USER'"
