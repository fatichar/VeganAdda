#!/usr/bin/env bash
# Runs on the VPS. All Compose operations explicitly select only VeganAdda.
set -euo pipefail
umask 077
root=/opt/veganadda
release=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P)
[[ "$release" =~ ^/opt/veganadda/releases/[a-f0-9]{40}$ ]] || { echo 'Unexpected release directory'; exit 1; }
export IMAGE_REF="${1:?Supply immutable GHCR image digest}"
registry_user="${2:?Supply GHCR user}"
[[ "$IMAGE_REF" =~ ^ghcr\.io/[a-z0-9._-]+/veganadda-web@sha256:[a-f0-9]{64}$ ]]
[[ -f "$root/.env" && ! -L "$root/.env" && "$(stat -c %a "$root/.env")" == 600 ]] || { echo 'Create /opt/veganadda/.env with mode 600 first'; exit 1; }
# Also serialize manual invocations, independently of GitHub concurrency.
exec 9>"$root/.deployment.lock"
flock -n 9 || { echo 'Another VeganAdda deployment is running'; exit 1; }
export COMPOSE_PROJECT_NAME=veganadda
export COMPOSE_FILE="$release/compose.yaml"
export COMPOSE_ENV_FILES="$root/.env"
# CI always overrides the local IMAGE_REF value in .env.
export DOCKER_CONFIG
DOCKER_CONFIG=$(mktemp -d "$root/.docker-login.XXXXXX")
migration_started=false
maintenance=false
successful=false
cleanup() {
  result=$?
  # Only these exact temporary credentials are removed; other stacks are untouched.
  rm -f -- "$DOCKER_CONFIG/config.json"
  rmdir -- "$DOCKER_CONFIG" 2>/dev/null || true
  if [[ "$successful" != true ]]; then
    if [[ "$maintenance" == true && "$migration_started" != true ]]; then
      docker compose start web || true
    elif [[ "$migration_started" == true ]]; then
      echo 'Deployment failed after migration began. Web is left stopped; review schema compatibility and backups before recovery.' >&2
      docker compose stop web || true
    fi
  fi
  exit "$result"
}
trap cleanup EXIT
docker login ghcr.io --username "$registry_user" --password-stdin
docker compose config --quiet
docker compose pull web migrate
# File-backed Compose secrets retain the host file's permissions. Check access as
# the image's non-root app user before stopping the existing web container.
docker compose run -T --rm --no-deps --pull never --entrypoint /bin/sh web \
  -c 'test -r /run/secrets/firebase_admin' || { echo 'Firebase Admin secret is not readable by the web container user'; exit 1; }
docker compose up -d --wait --wait-timeout 120 db
maintenance=true
BACKUP_KEEP_STOPPED=1 bash "$release/backup.sh" "$root/backups"
migration_started=true
docker compose run -T --rm --no-deps --pull never migrate
docker compose up -d --no-deps --no-build --pull never --wait --wait-timeout 150 web
binding=$(docker compose port web 8080)
bash "$release/check-ready.sh" "$binding"
printf '%s\n' "$IMAGE_REF" > "$release/image-ref"
ln -sfn -- "$release" "$root/current"
successful=true
if [[ -d "$root/backups" && ! -L "$root/backups" ]]; then
  # Prune only timestamped VeganAdda backup bundles, and only after readiness passes.
  find "$root/backups" -mindepth 1 -maxdepth 1 -type d -name 'veganadda-????????T??????Z' -mmin +43200 -exec rm -rf -- {} + || echo 'Warning: could not prune expired backup bundles' >&2
fi
echo "Deployed $IMAGE_REF; readiness passed. Release: $release"
