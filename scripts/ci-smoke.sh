#!/usr/bin/env bash
# Disposable GitHub runner containers only; never uses production data/secrets.
set -euo pipefail
image="${1:?Supply the Linux image to verify}"
task_dir=$(mktemp -d)
cleanup() {
  result=$?
  if (( result != 0 )); then docker logs veganadda-ci-web 2>&1 || true; fi
  docker rm -f veganadda-ci-web veganadda-ci-db > /dev/null 2>&1 || true
  docker network rm veganadda-ci > /dev/null 2>&1 || true
  rm -f -- "$task_dir/cookies" "$task_dir/input.png" "$task_dir/output.jpg"
  rmdir -- "$task_dir" || true
  exit "$result"
}
trap cleanup EXIT
docker network create veganadda-ci > /dev/null
docker run -d --name veganadda-ci-db --network veganadda-ci \
  -e POSTGRES_USER=veganadda -e POSTGRES_PASSWORD=ci-only-password -e POSTGRES_DB=veganadda_ci \
  postgres:18-bookworm > /dev/null
for attempt in {1..60}; do
  if docker exec veganadda-ci-db pg_isready -U veganadda -d veganadda_ci > /dev/null 2>&1; then break; fi
  if [[ "$attempt" == 60 ]]; then echo 'PostgreSQL did not become ready in time' >&2; exit 1; fi
  sleep 2
done
docker exec veganadda-ci-db pg_isready -U veganadda -d veganadda_ci
docker run -d --name veganadda-ci-web --network veganadda-ci -p 127.0.0.1:5081:8080 \
  -e ASPNETCORE_ENVIRONMENT=Development -e Development__Enabled=true -e Development__Seed=true \
  -e 'ConnectionStrings__Database=Host=veganadda-ci-db;Database=veganadda_ci;Username=veganadda;Password=ci-only-password' \
  "$image" > /dev/null
origin=http://localhost:5081
for attempt in {1..60}; do
  if curl --fail --silent --max-time 3 "$origin/health/ready" > /dev/null; then break; fi
  if [[ "$attempt" == 60 ]]; then echo 'Application did not become ready in time' >&2; exit 1; fi
  sleep 2
done
curl --fail --silent --show-error --max-time 10 "$origin/health/ready"
csrf=$(curl --fail --silent --show-error -c "$task_dir/cookies" "$origin/api/csrf" | python3 -c 'import json,sys; print(json.load(sys.stdin)["token"])')
curl --fail --silent --show-error -b "$task_dir/cookies" -c "$task_dir/cookies" -X POST \
  -H "X-CSRF-TOKEN: $csrf" "$origin/auth/dev/aarav" > /dev/null
# Antiforgery tokens are identity bound; obtain a new token after sign-in.
csrf=$(curl --fail --silent --show-error -b "$task_dir/cookies" -c "$task_dir/cookies" "$origin/api/csrf" | python3 -c 'import json,sys; print(json.load(sys.stdin)["token"])')
python3 - "$task_dir/input.png" <<'PY'
import struct, sys, zlib
def chunk(kind, data):
    return struct.pack('!I', len(data)) + kind + data + struct.pack('!I', zlib.crc32(kind + data))
pixels = b''.join(b'\x00' + b'\x20\x60\x40' * 8 for _ in range(8))
with open(sys.argv[1], 'wb') as f:
    f.write(b'\x89PNG\r\n\x1a\n' + chunk(b'IHDR', struct.pack('!2I5B', 8, 8, 8, 2, 0, 0, 0)) + chunk(b'IDAT', zlib.compress(pixels)) + chunk(b'IEND', b''))
PY
path=$(curl --fail --silent --show-error -b "$task_dir/cookies" -H "X-CSRF-TOKEN: $csrf" \
  -F "file=@$task_dir/input.png;type=image/png" "$origin/api/upload" | python3 -c 'import json,sys; print(json.load(sys.stdin)["path"])')
[[ "$path" =~ ^/media/[a-f0-9]{32}\.jpg$ ]]
curl --fail --silent --show-error -b "$task_dir/cookies" "$origin$path" -o "$task_dir/output.jpg"
python3 - "$task_dir/output.jpg" <<'PY'
import sys
with open(sys.argv[1], 'rb') as f:
    assert f.read(3) == b'\xff\xd8\xff', 'Expected normalized JPEG'
PY
echo 'Linux container startup, migrations, sign-in, CSRF and native image decoding passed.'
