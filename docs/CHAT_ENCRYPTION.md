# Chat encryption operations

The web process encrypts chat bodies and connection introductions with AES-GCM. The cleanup release removes the plaintext copies retained during the rollback window. The key ring is separate from both the database and ASP.NET Data Protection keys.

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

GitHub Actions copies the rollout and backup scripts to the VPS. Before maintenance, the release check confirms that the ring holds every stored key and verifies every retained message and introduction against its encrypted copy. Rows without a complete copy (for example, writes made during a rollback) and keys with a recorded repair are skipped there, because `--migrate` rebuilds them from plaintext. Any other damaged or mismatched copy stops the rollout while the previous image is still serving; investigate it and repair it with the previous release before retrying.

When the check passes, the backup stops the web slots and saves PostgreSQL, media and session keys. Keep other database writers stopped. The application's `--migrate` command first finishes any staged backfill and recorded repair from plaintext. It then locks the chat tables, verifies every row again, drops the plaintext columns and rewrites both tables in the same transaction. `DROP COLUMN` alone leaves the old bytes in every existing row on disk; the rewrite (`CLUSTER`) stores them as NULL and removes the old data files at commit. The rewrite needs free disk space about the size of both tables and holds the tables for its duration. If verification fails, the transaction leaves both columns intact.

If the rollout fails before the transaction commits, verify that both plaintext columns remain before restarting the previous image. After the drop, recover forward with a compatible image and the matching key ring. To return to the previous image, restore the pre-cutover database in an isolated project and verify it before switching traffic. That backup cannot recover writes made after it was taken.

Column removal does not erase historical plaintext in pre-cutover dumps, WAL, dead tuples, replicas or snapshots. Apply the relevant retention policy to each copy. Normal deployment backup and restore scripts intentionally leave the chat ring out of the bundle. Supply it separately when restoring encrypted data, then validate a known history read with the matching application image.

## Rotate

Add a distinct key ID and fresh key material. Back up the updated ring independently. For overlapping web slots, first distribute the new key while leaving the old ID active and recreate both containers so each process can read both keys. Only then change the active ID and recreate the writers. A maintenance window can instead stop all writers before activation. A file-backed mount may keep pointing to an old inode after an atomic host file replacement, so recreate the containers instead of merely restarting the existing process.

Rotate at least every 90 days and before an estimated 16 million encryptions per key, whichever comes first. Include backfills, introductions and all writers in that estimate. Rotation changes future writes. Keep historical keys while any database row or restorable backup still needs them. If a key is lost, affected chat bodies remain unavailable until the exact key material is restored.
