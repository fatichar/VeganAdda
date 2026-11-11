# Chat encryption operations

The web process encrypts chat bodies and connection introductions with AES-GCM. This first release also retains plaintext to support rollback to the previous image. Database dumps still contain readable chat content during this window. The key ring is separate from both the database and ASP.NET Data Protection keys.

## Provision before rollout

Create the JSON ring outside the checkout and the deployment backup tree. Use a fresh random 32-byte key encoded as standard base64. The following shell command writes it directly to a new file without displaying the key. Choose a secure directory that already exists. Run the command only for the first ring.

```sh
umask 077
python3 - /secure/path/to/chat-message-keys.json <<'PY'
import base64, json, secrets, sys
with open(sys.argv[1], "x") as out:
    json.dump({"current": "v1", "keys": {"v1": base64.b64encode(secrets.token_bytes(32)).decode("ascii")}}, out)
PY
```

Set `CHAT_MESSAGE_KEYS_FILE` in the deployment environment to this absolute file path. Compose mounts it read-only in the migrator and both web slots. Restrict the file permissions while allowing the image's non-root app user (UID 1654 in the current image) to read it. File-backed Compose secrets use host file ownership and permissions.

Store an independently encrypted recovery copy with separate access controls before the migration. Keep that copy out of the deployment bundle and record which key IDs the bundle can be restored with. Use the existing test and staging workflow to rehearse a restore, including a known chat read, before the production cutover.

Local Development creates a ring under the current user's local application data directory, with a separate directory for each checkout. Development startup refuses a database whose rows reference a key missing from the ring. Use `Storage:MessageKeysPath` to point to a trusted existing ring when opening a development database that already uses one; earlier builds kept it in `message-keys/keys.json` under the web project. Generate production and staging rings independently. Treat any ring committed to Git as exposed and replace it wherever it was used.

## Cutover and recovery

GitHub Actions copies the rollout and backup scripts to the VPS as part of the release. The rollout checks the key ring and runs the maintenance backup automatically if it detects a pending migration or legacy chat writes to backfill. The backup stops the web slots and saves PostgreSQL, media and session keys before the migration. Keep any other database writers stopped for the cutover.

The application's `--migrate` command adds the nullable encrypted columns and backfills missing or incomplete copies in bounded batches. The release check samples complete copies. When a sample under a key fails, the migration verifies every row under that key and rewrites only the copies that disagree with the retained text. The migration records that key in `ChatKeyRepairs` before rewriting and clears it only after both tables are verified, so an interrupted run is resumed by the next release check. If a stored key is missing or all samples for the active key fail authentication, the check stops before maintenance so the operator can verify the ring. The app writes both forms and uses the retained text if a copy cannot be decrypted. Each such read logs an `Unable to decrypt chat message` or `Unable to decrypt a connection introduction` warning, which is forwarded to Better Stack when it is configured. The app exports no metrics. If the previous image changes a connection introduction, the database clears the stale encrypted copy so the next backfill picks up the replacement.

To roll back this release, keep the live database and restart the previous web image using the previous release configuration. Use the normal VPS slot and upstream checks. If the old container was replaced, recreate the web service from the previous immutable digest without running its migrator or starting Compose dependencies. The previous migration checker rejects migration IDs it does not recognize. If the rollout fails after migration starts, the script leaves web stopped for manual recovery.

When returning to the new image after rollback, the release check detects plaintext-only rows and schedules the backfill. Before a later release removes plaintext, it must backfill rollback writes and verify every message and introduction against the retained text while writers are stopped. Any mismatch must be repaired and verified before the columns are dropped. The current release samples rows, so a clean release check or the absence of fallback warnings cannot replace that full verification. Once plaintext columns are removed, restarting the original image will require a compatible database restore.

Column removal does not erase historical plaintext in pre-cutover dumps, WAL, dead tuples, replicas or snapshots. Apply the relevant retention policy to each copy. Normal deployment backup and restore scripts intentionally leave the chat ring out of the bundle. Supply it separately when restoring encrypted data, then validate a known history read with the matching application image.

## Rotate

Add a distinct key ID and fresh key material. Back up the updated ring independently. For overlapping web slots, first distribute the new key while leaving the old ID active and recreate both containers so each process can read both keys. Only then change the active ID and recreate the writers. A maintenance window can instead stop all writers before activation. A file-backed mount may keep pointing to an old inode after an atomic host file replacement, so recreate the containers instead of merely restarting the existing process.

Rotate at least every 90 days and before an estimated 16 million encryptions per key, whichever comes first. Include backfills, introductions and all writers in that estimate. Rotation changes future writes. Keep historical keys while any database row or restorable backup still needs them. If a key is lost, affected chat bodies remain unavailable until the exact key material is restored.
