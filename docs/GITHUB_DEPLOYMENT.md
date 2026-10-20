# GitHub Actions → existing VPS

The [workflow](../.github/workflows/deploy-vps.yml) follows Dinzer's GHCR + SSH approach, using the existing `Prod` environment name. It is prepared locally; no workflow has been triggered and no server configuration has been changed.

## What runs

A push to **master**, or **Run workflow** on master, runs PostgreSQL integration tests, builds a Linux amd64 image, then smoke-tests that image with disposable development fixtures. The smoke test exercises startup/migrations, authenticated CSRF and PNG → JPEG upload through the native Linux image library. Only a passing image is published to GHCR. The deploy job requires the `Prod` environment and uses the exact published **sha256 digest**, rather than a moving tag. Manual runs from other branches build/test but skip deployment.

For a build-only measurement, use **Run workflow** with **benchmark** checked. Tests and the container smoke check still run, but registry login, image publication and VPS deployment are skipped. Leave it unchecked for the existing release behavior.

The runner copies Compose and operations scripts into `/opt/veganadda/releases/COMMIT_SHA`, leaving `/opt/veganadda/.env` intact. The server pulls the image, starts only VeganAdda's database, stops only VeganAdda's web container, backs up database/media/session keys, runs migrations, starts web, and checks readiness. A successful deployment updates `/opt/veganadda/current`. Concurrency and a server-side lock prevent overlapping deployments. Expect a short maintenance window during backup/migration/startup.

The runtime image keeps NuGet dependencies, stable .NET browser-framework assets, other web assets, and VeganAdda application files in separate linked layers. Code-only releases can reuse the dependency and framework layers on the VPS; changes to dependencies or framework assets download their corresponding layer. The build copies project files before source files so ordinary code edits keep the NuGet restore cache.

Publishing and splitting now happen in one build layer. Temporary `/out` and source/compiler output are removed before that layer is cached, so `mode=max` does not upload duplicate publish output and compiler intermediates. The runtime still copies the same four directories from `/layers`.

The manual **cache_mode** selector allows `max` (the default, including pushes) or `min`. They use separate cache scopes so an experiment cannot overwrite the production cache. Compare both modes on the same commit with **benchmark** checked: run each once to warm its cache, then at least twice more. Compare total build-job time, restore/publish time, and the `exporting to GitHub Actions Cache` duration in **Build Linux image**. Repeat on a normal code-change commit; an unchanged rebuild alone overstates cache benefits. `min` exports only final-image layers and can lose NuGet/npm intermediate cache hits, so a faster export alone is insufficient reason to change the default. The first run after this Dockerfile change must rebuild the publish layer.

Failure before migration restarts the existing web container. Failure after migration begins leaves web stopped for review; automatically reverting the image could run incompatible code against a changed schema. Backups are under `/opt/veganadda/backups/veganadda-TIMESTAMP`. Neither Nginx nor other Compose projects are changed by this action.

## GitHub settings

Create repository **Settings → Environments → Prod** before enabling deployment. Add a required reviewer and prevent bypass so a push to `master` can test and publish an image but cannot deploy to the VPS without an explicit environment approval. Put these secrets there (repository secrets also work):

| Secret | Value |
| --- | --- |
| `VPS_SSH_PRIVATE_KEY` | Private key authorized for the deployment user. The currently supplied connection uses `~/.ssh/ovh_vps`; preferably use a dedicated GitHub deployment key with access scoped to this app. Do not commit it. The runner requires a key usable without an interactive passphrase. |
| `VPS_SSH_KNOWN_HOSTS` | Verified OpenSSH known_hosts entry for `[51.79.223.152]:22222`. Host checking is mandatory. |

Optional `Prod` variables override these defaults:

| Variable | Default |
| --- | --- |
| `VPS_HOST` | `51.79.223.152` |
| `VPS_PORT` | `22222` |
| `VPS_USER` | `ubuntu` |
| `UMAMI_SCRIPT_URL` | `https://analytics.greenfit.in/script.js` |
| `UMAMI_WEBSITE_ID` | `29d3948e-23fe-4394-b433-799a76a4b09d` (VeganAdda property) |

Retrieve the already trusted host entry on your own workstation, checking its fingerprint against the VPS console/provider or an independently trusted record first:

```powershell
ssh-keygen -F '[51.79.223.152]:22222' -f "$env:USERPROFILE/.ssh/known_hosts"
```

Copy the host-key line(s), not the informational comment, into `VPS_SSH_KNOWN_HOSTS`. Do not populate it with an unverified scan. A changed host key must fail deployment until investigated.

GitHub supplies `GITHUB_TOKEN` automatically: build gets `packages:write`, deploy gets `packages:read`, and repository contents are read-only. No personal registry token is needed. If a pre-existing GHCR package has separate access settings, grant this repository Actions access to that package. See [GitHub's container registry authentication guidance](https://docs.github.com/en/packages/working-with-a-github-packages-registry/working-with-the-container-registry). The server uses a temporary Docker credential directory removed on exit; existing registry credentials for other apps are preserved. Firebase public settings and database credentials live in the VPS `.env`; the Admin SDK credential stays in a separate protected file mounted only into the web container.

## One-time server preparation — after deployment authorization

Review [the read-only VPS inspection](VPS_INSPECTION.md) and recheck the chosen localhost port/subnet. The following is guidance, not a command already executed:

```bash
sudo install -d -o ubuntu -g ubuntu -m 750 /opt/veganadda
sudo install -d -o ubuntu -g ubuntu -m 700 /opt/veganadda/releases /opt/veganadda/backups
# Copy .env.example to /opt/veganadda/.env using your secure transfer/editor.
# Fill real values, then:
chmod 600 /opt/veganadda/.env
```

The deployment user needs Docker access and write access to this directory. Docker access is host-privileged; protect the SSH key accordingly. The script deliberately fails if `.env` is missing, a symlink, or not mode 600. Never reuse the development database or development flags.

Set Firebase web configuration, `ADMIN_FIREBASE_UID`, a private Firebase Admin service-account JSON file path, random database password, `APP_PORT=5080`, and the verified free subnet/trusted gateway. Keep the service-account file outside the deployment release and repository; Compose mounts it only into the web container. The web process runs as UID 1654, so install the file mode 0400 and assign that numeric owner (for example, `sudo install -m 0400 /secure-transfer/firebase-admin.json /opt/veganadda/firebase-admin.json && sudo chown +1654 /opt/veganadda/firebase-admin.json`). Set `FIREBASE_ADMIN_CREDENTIALS=/opt/veganadda/firebase-admin.json` in `.env`. The deployment script checks that the web user can read the mounted secret before it starts maintenance. Leave `IMAGE_REF=veganadda:local`; the workflow overrides it with the tested digest on every invocation. Changing `POSTGRES_PASSWORD` later does not change the password inside an existing PostgreSQL volume; rotate it explicitly in PostgreSQL as well.

Finish DNS, certificate issuance, and the **separate** VeganAdda Nginx vhost as described in the [README](../README.md#vps-deployment-requires-explicit-authorization). CI checks loopback readiness; public HTTPS, trusted proxy handling and real Firebase Google sign-in still require staging verification. The action does not create DNS, issue certificates, modify Nginx, prune Docker images, remove volumes, or restart unrelated services.

Once setup and deployment are authorized, push the workflow/source to master or run **Actions → Build and Deploy to VPS → Run workflow** on master. Its first actual run will validate Linux container behavior; that has not been executed on this workstation because Docker is stopped.

## Operations and recovery

The successful release's `image-ref` records its digest. For manual Compose operations, select the correct release and environment explicitly; never accidentally build a local image:

```bash
cd /opt/veganadda/current
export COMPOSE_PROJECT_NAME=veganadda
export COMPOSE_FILE="$PWD/compose.yaml"
export COMPOSE_ENV_FILES=/opt/veganadda/.env
if [ -s "$PWD/analytics.env" ]; then export COMPOSE_ENV_FILES="/opt/veganadda/.env,$PWD/analytics.env"; fi
export IMAGE_REF="$(cat image-ref)"
docker compose ps
docker compose logs --tail 100 web
bash backup.sh /opt/veganadda/backups
```

Regular backups restart web afterward; rollout's `BACKUP_KEEP_STOPPED=1` keeps the same maintenance window open until migration completes. Encrypt/copy backups off-host and remove those copies within 30 days. After a successful deployment, rollout deletes timestamped VeganAdda backup bundles older than 30 days. If deployments pause, expired bundles may remain until cleanup runs again. Monitor disk space on this shared VPS.

On a failed migration or readiness check, inspect the workflow log and that release's container logs; `/opt/veganadda/current` still identifies the last successful release. Do not rerun migration blindly or assume an app rollback reverses schema changes. If the previous image is compatible with the current schema, select that release's Compose file/digest and start web with `--no-deps --no-build --pull never --wait`. Otherwise restore the pre-deployment backup to a **new isolated project** with `restore.sh`, a distinct port and subnet, verify it, then switch only VeganAdda's upstream. See README backup/restore instructions.

The isolated restore and a production HTTPS/Firebase/chat smoke test remain required launch checks. This workflow is deployment preparation, not evidence of a completed live deployment.

See [observability setup](OBSERVABILITY.md) for Better Stack source credentials, monitor creation and notification verification. Better Stack secrets stay in the VPS .env. Public analytics settings are copied to a release-local analytics.env; no image rebuild is needed to change runtime settings.
