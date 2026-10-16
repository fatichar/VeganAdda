# GitHub Actions → existing VPS

The [workflow](../.github/workflows/deploy-vps.yml) follows Dinzer's GHCR + SSH approach, using the existing `Prod` environment name. It is prepared locally; no workflow has been triggered and no server configuration has been changed.

## What runs

A push to **master**, or **Run workflow** on master, runs PostgreSQL integration tests, builds a Linux amd64 image, then smoke-tests that image with disposable development fixtures. The smoke test exercises startup/migrations, authenticated CSRF and PNG → JPEG upload through the native Linux image library. Only a passing image is published to GHCR. The deploy job requires the `Prod` environment and uses the exact published **sha256 digest**, rather than a moving tag. Manual runs from other branches build/test but skip deployment.

The runner copies Compose and operations scripts into `/opt/veganadda/releases/COMMIT_SHA`, leaving `/opt/veganadda/.env` intact. The server pulls the image, starts only VeganAdda's database, stops only VeganAdda's web container, backs up database/media/session keys, runs migrations, starts web, and checks readiness. A successful deployment updates `/opt/veganadda/current`. Concurrency and a server-side lock prevent overlapping deployments. Expect a short maintenance window during backup/migration/startup.

Failure before migration restarts the existing web container. Failure after migration begins leaves web stopped for review; automatically reverting the image could run incompatible code against a changed schema. Backups are under `/opt/veganadda/backups/veganadda-TIMESTAMP`. Neither Nginx nor other Compose projects are changed by this action.

## GitHub settings

Create repository **Settings → Environments → Prod** before enabling deployment. Configure protection/reviewers according to your release policy. Put these secrets there (repository secrets also work):

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

Retrieve the already trusted host entry on your own workstation, checking its fingerprint against the VPS console/provider or an independently trusted record first:

```powershell
ssh-keygen -F '[51.79.223.152]:22222' -f "$env:USERPROFILE/.ssh/known_hosts"
```

Copy the host-key line(s), not the informational comment, into `VPS_SSH_KNOWN_HOSTS`. Do not populate it with an unverified scan. A changed host key must fail deployment until investigated.

GitHub supplies `GITHUB_TOKEN` automatically: build gets `packages:write`, deploy gets `packages:read`, and repository contents are read-only. No personal registry token is needed. If a pre-existing GHCR package has separate access settings, grant this repository Actions access to that package. See [GitHub's container registry authentication guidance](https://docs.github.com/en/packages/working-with-a-github-packages-registry/working-with-the-container-registry). The server uses a temporary Docker credential directory removed on exit; existing registry credentials for other apps are preserved. OAuth and database secrets remain exclusively in the VPS `.env`.

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

Set Google credentials, admin Google subject, random database password, `APP_PORT=5080`, and the verified free subnet/trusted gateway. Leave `IMAGE_REF=veganadda:local`; the workflow overrides it with the tested digest on every invocation. Changing `POSTGRES_PASSWORD` later does not change the password inside an existing PostgreSQL volume; rotate it explicitly in PostgreSQL as well.

Finish DNS, certificate issuance, and the **separate** VeganAdda Nginx vhost as described in the [README](../README.md#vps-deployment-requires-explicit-authorization). CI checks loopback readiness; public HTTPS, trusted proxy handling and real Google OAuth still require staging verification. The action does not create DNS, issue certificates, modify Nginx, prune Docker images, remove volumes, or restart unrelated services.

Once setup and deployment are authorized, push the workflow/source to master or run **Actions → Build and Deploy to VPS → Run workflow** on master. Its first actual run will validate Linux container behavior; that has not been executed on this workstation because Docker is stopped.

## Operations and recovery

The successful release's `image-ref` records its digest. For manual Compose operations, select the correct release and environment explicitly; never accidentally build a local image:

```bash
cd /opt/veganadda/current
export COMPOSE_PROJECT_NAME=veganadda
export COMPOSE_FILE="$PWD/compose.yaml"
export COMPOSE_ENV_FILES=/opt/veganadda/.env
export IMAGE_REF="$(cat image-ref)"
docker compose ps
docker compose logs --tail 100 web
bash backup.sh /opt/veganadda/backups
```

Regular backups restart web afterward; rollout's `BACKUP_KEEP_STOPPED=1` keeps the same maintenance window open until migration completes. Encrypt/copy backups off-host and establish retention: the workflow intentionally never deletes them. Monitor disk space on this shared VPS and clean old release images/backups only after confirming retention and rollback needs.

On a failed migration or readiness check, inspect the workflow log and that release's container logs; `/opt/veganadda/current` still identifies the last successful release. Do not rerun migration blindly or assume an app rollback reverses schema changes. If the previous image is compatible with the current schema, select that release's Compose file/digest and start web with `--no-deps --no-build --pull never --wait`. Otherwise restore the pre-deployment backup to a **new isolated project** with `restore.sh`, a distinct port and subnet, verify it, then switch only VeganAdda's upstream. See README backup/restore instructions.

The isolated restore and a production HTTPS/Google/chat smoke test remain required launch checks. This workflow is deployment preparation, not evidence of a completed live deployment.
