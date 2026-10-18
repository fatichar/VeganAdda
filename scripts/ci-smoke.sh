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
  rm -f -- "$task_dir/cookies" "$task_dir/input.png" "$task_dir/output.jpg" \
    "$task_dir/signin.headers" "$task_dir/signin.html" "$task_dir/blazor.web.js"
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
curl --fail --silent --show-error --max-time 10 -D "$task_dir/signin.headers" \
  "$origin/signin" -o "$task_dir/signin.html"
boot_src=$(python3 - "$task_dir/signin.html" "$task_dir/signin.headers" <<'PY'
from html.parser import HTMLParser
from pathlib import Path
from urllib.parse import urljoin, urlsplit
import re, sys

class Scripts(HTMLParser):
    def __init__(self):
        super().__init__()
        self.import_map_nonce = None
        self.boot_src = None

    def handle_starttag(self, tag, attrs):
        if tag != "script":
            return
        attributes = dict(attrs)
        if attributes.get("type") == "importmap":
            self.import_map_nonce = attributes.get("nonce")
        src = attributes.get("src", "")
        if re.fullmatch(r"(?:.*/)?blazor\.web(?:\.[A-Za-z0-9]+)?\.js", urlsplit(src).path):
            self.boot_src = src

page = Path(sys.argv[1]).read_text(encoding="utf-8")
headers = Path(sys.argv[2]).read_text(encoding="iso-8859-1")
scripts = Scripts()
scripts.feed(page)
assert scripts.import_map_nonce, "Sign-in page import map is missing its CSP nonce"
assert scripts.boot_src, "Sign-in page is missing the Blazor boot script"
boot_url = urljoin("http://localhost:5081/", scripts.boot_src)
assert urlsplit(boot_url).netloc == "localhost:5081", "Blazor boot script must be served by the app"
policy = re.search(r"(?im)^content-security-policy:\s*(.*)$", headers)
assert policy, "Sign-in page is missing its Content-Security-Policy header"
assert f"'nonce-{scripts.import_map_nonce}'" in policy.group(1), "CSP does not allow the sign-in page import map nonce"
boot = urlsplit(boot_url)
print(boot.path + ("?" + boot.query if boot.query else ""))
PY
)
echo "Checking Blazor boot asset: $boot_src"
asset_type=$(curl --fail --silent --show-error --max-time 10 \
  -o "$task_dir/blazor.web.js" -w '%{content_type}' "$origin$boot_src")
[[ -s "$task_dir/blazor.web.js" && "$asset_type" == *javascript* ]] || {
  echo "Blazor boot asset failed to load as JavaScript: $boot_src ($asset_type)" >&2
  exit 1
}
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
