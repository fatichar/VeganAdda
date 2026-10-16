#!/usr/bin/env bash
# Run from repository root; maintenance window ensures DB and media are consistent.
set -euo pipefail
umask 077
target="${1:?Usage: deploy/backup.sh /absolute/backup-directory}"
[[ "$target" = /* ]] || { echo 'Use an absolute backup path'; exit 1; }
mkdir -p "$target"
stamp=$(date -u +%Y%m%dT%H%M%SZ)
bundle="$target/veganadda-$stamp"
mkdir "$bundle"
docker compose stop web
# Rollout keeps the maintenance window open through migration and health checks.
# A failed backup still restarts the existing container; no migration has run yet.
complete=false
trap 'if [[ "${BACKUP_KEEP_STOPPED:-0}" != 1 || "$complete" != true ]]; then docker compose start web >/dev/null; fi' EXIT
docker compose exec -T db pg_dump -U veganadda -d veganadda -Fc > "$bundle/database.dump"
docker compose run -T --rm --no-deps --user root --entrypoint tar web -C /data -czf - media keys > "$bundle/media-and-keys.tgz"
docker compose images --format json > "$bundle/images.json"
(cd "$bundle" && sha256sum database.dump media-and-keys.tgz > SHA256SUMS)
complete=true
echo "Backup created: $bundle. Encrypt and copy off-host; this includes private data and session keys."
