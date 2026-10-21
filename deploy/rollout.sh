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
export COMPOSE_PROFILES=rollout
export COMPOSE_FILE="$release/compose.yaml"
export COMPOSE_ENV_FILES="$root/.env"
if [[ -s "$release/analytics.env" ]]; then
  export COMPOSE_ENV_FILES="$root/.env,$release/analytics.env"
fi
# CI always overrides the local IMAGE_REF value in .env.
export DOCKER_CONFIG
DOCKER_CONFIG=$(mktemp -d "$root/.docker-login.XXXXXX")
migration_started=false
maintenance=false
successful=false
upstream_changed=false
candidate=
upstream_file="$root/nginx-upstream.conf"
active_port=
active_service=web
switch_ready=false
cleanup() {
  result=$?
  # Only these exact temporary credentials are removed; other stacks are untouched.
  rm -f -- "$DOCKER_CONFIG/config.json"
  rmdir -- "$DOCKER_CONFIG" 2>/dev/null || true
  if [[ "$successful" != true ]]; then
    if [[ "$upstream_changed" == true ]]; then
      printf 'proxy_pass http://127.0.0.1:%s;\n' "$active_port" > "$upstream_file.rollback"
      mv -f -- "$upstream_file.rollback" "$upstream_file"
      sudo -n nginx -t && sudo -n systemctl reload nginx || true
    fi
    if [[ -n "$candidate" && "$candidate" != "$active_service" ]]; then docker compose stop "$candidate" || true; fi
    if [[ "$maintenance" == true && "$migration_started" != true ]]; then
      docker compose start "$active_service" || true
    elif [[ "$migration_started" == true ]]; then
      echo 'Deployment failed after migration began. Web is left stopped; review schema compatibility and backups before recovery.' >&2
      docker compose stop web web_standby || true
    fi
  fi
  exit "$result"
}
trap cleanup EXIT
docker login ghcr.io --username "$registry_user" --password-stdin
docker compose config --quiet
docker compose pull web web_standby migrate
# File-backed Compose secrets retain the host file's permissions. Check access as
# the image's non-root app user before stopping the existing web container.
docker compose run -T --rm --no-deps --pull never --entrypoint /bin/sh web \
  -c 'test -r /run/secrets/firebase_admin' || { echo 'Firebase Admin secret is not readable by the web container user'; exit 1; }
docker compose up -d --wait --wait-timeout 120 db

# An included upstream file enables an already-running slot to stay online while
# the other slot starts. Without it, use the original single-port cutover.
if [[ -f "$upstream_file" && ! -L "$upstream_file" ]] &&
   sudo -n nginx -T 2>/dev/null | grep -F "configuration file $upstream_file:" > /dev/null; then
  sudo -n nginx -t
  current_upstream=$(cat "$upstream_file")
  [[ "$current_upstream" =~ ^proxy_pass\ http://127\.0\.0\.1:([0-9]{1,5})\;$ ]] || { echo 'Invalid VeganAdda Nginx upstream file' >&2; exit 1; }
  active_port="${BASH_REMATCH[1]}"
  web_port=$(docker compose port web 8080 2>/dev/null || true)
  standby_port=$(docker compose port web_standby 8080 2>/dev/null || true)
  if [[ "$web_port" == "127.0.0.1:$active_port" ]]; then
    active_service=web
    candidate=web_standby
  elif [[ "$standby_port" == "127.0.0.1:$active_port" ]]; then
    active_service=web_standby
    candidate=web
  else
    echo 'Nginx upstream does not match a VeganAdda web slot' >&2
    exit 1
  fi
  [[ -n "$(docker compose ps --status running -q "$active_service")" ]] || { echo 'Active VeganAdda web slot is not running' >&2; exit 1; }
  switch_ready=true
elif [[ -e "$upstream_file" ]]; then
  echo 'VeganAdda Nginx upstream file exists but is not active; refusing an ambiguous cutover' >&2
  exit 1
fi

pending=false
if docker compose run -T --rm --no-deps --pull never migrate --check-migrations; then
  echo 'No pending database migrations; preparing a short cutover.'
else
  result=$?
  if (( result != 10 )); then echo "Migration check failed with exit code $result" >&2; exit "$result"; fi
  pending=true
fi

if [[ "$pending" == true ]]; then
  maintenance=true
  BACKUP_KEEP_STOPPED=1 bash "$release/backup.sh" "$root/backups"
  migration_started=true
  docker compose run -T --rm --no-deps --pull never migrate
fi

if [[ "$switch_ready" == true ]]; then
  docker compose up -d --no-deps --no-build --pull never --wait --wait-timeout 150 "$candidate"
  binding=$(docker compose port "$candidate" 8080)
  bash "$release/check-ready.sh" "$binding"
  [[ "$binding" =~ ^127\.0\.0\.1:([0-9]{1,5})$ ]]
  printf 'proxy_pass http://%s;\n' "$binding" > "$upstream_file.next"
  mv -f -- "$upstream_file.next" "$upstream_file"
  upstream_changed=true
  sudo -n nginx -t
  sudo -n systemctl reload nginx
  curl --fail --silent --show-error --max-time 15 \
    --resolve veganadda.com:443:127.0.0.1 \
    https://veganadda.com/health/ready > /dev/null
else
  docker compose up -d --no-deps --no-build --pull never --wait --wait-timeout 150 web
  binding=$(docker compose port web 8080)
  bash "$release/check-ready.sh" "$binding"
fi
printf '%s\n' "$IMAGE_REF" > "$release/image-ref"
ln -sfn -- "$release" "$root/current"
successful=true
if [[ "$switch_ready" == true && "$pending" == false ]]; then
  docker compose stop "$active_service" || echo 'Warning: old web slot could not be stopped.' >&2
fi
if [[ -d "$root/backups" && ! -L "$root/backups" ]]; then
  # Prune only timestamped VeganAdda backup bundles, and only after readiness passes.
  find "$root/backups" -mindepth 1 -maxdepth 1 -type d -name 'veganadda-????????T??????Z' -mmin +43200 -exec rm -rf -- {} + || echo 'Warning: could not prune expired backup bundles' >&2
fi
echo "Deployed $IMAGE_REF; readiness passed. Release: $release"
