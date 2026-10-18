#!/usr/bin/env bash
# Explicitly target a NEW, isolated Compose project with empty volumes.
set -euo pipefail
bundle="${1:?Usage: deploy/restore.sh /absolute/backup-bundle new-project-name}"
project="${2:?Supply a fresh Compose project name}"
[[ "$project" =~ ^veganadda-restore-[a-z0-9-]+$ ]] || { echo 'Project must start veganadda-restore-'; exit 1; }
[[ -z "$(docker volume ls --filter "label=com.docker.compose.project=$project" -q)" ]] || { echo 'Refusing to overwrite existing project volumes'; exit 1; }
(cd "$bundle" && sha256sum -c SHA256SUMS)
docker compose -p "$project" up -d db
until docker compose -p "$project" exec -T db pg_isready -U veganadda -d veganadda; do sleep 2; done
docker compose -p "$project" exec -T db pg_restore -U veganadda -d veganadda --exit-on-error --no-owner < "$bundle/database.dump"
docker compose -p "$project" run -T --rm --no-deps --user root --entrypoint tar web -C /data -xzf - < "$bundle/media-and-keys.tgz"
docker compose -p "$project" run -T --rm --no-deps --user root --entrypoint chown web -R 1654:1654 /data
echo "Restored isolated project $project. Review APP_PORT, proxy and Firebase authorized domain before starting its web service."
