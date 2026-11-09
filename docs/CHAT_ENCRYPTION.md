# Chat encryption operations

The web process encrypts chat bodies and connection introductions with AES-GCM. PostgreSQL retains key IDs and conversation metadata. The key ring is separate from both the database and ASP.NET Data Protection keys.

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

Local Development creates a persistent development ring in a separate `message-keys` directory next to the Data Protection directory. Use `Storage:MessageKeysPath` to specify an existing file explicitly. Keep the development ring with any development database whose messages you want to retain.

## Cutover and recovery

Use the existing rollout script. It checks the candidate can read the secret before maintenance. The application's `--check-migrations` command loads the ring and authenticates representative data for each existing chat key ID. Then the normal maintenance backup stops both web slots. Keep any other database writers stopped for the cutover.

Use the application's `--migrate` command for the upgrade. It applies the expansion migration, backfills and verifies the legacy text in bounded batches, then drops the old columns. A retry can resume without rewriting complete envelopes. Generic EF migration tooling cannot backfill chat content and the final migration refuses to drop the legacy columns while the backfill is incomplete.

If migration fails before the drop, keep web stopped and correct the failure before retrying. The previous image can still read the legacy columns if you have verified that the final migration did not commit and that the schema is compatible. After the drop, use a compatible image with the matching key ring or restore an appropriate backup into an isolated Compose project. A pre-cutover backup will omit messages sent after it was taken.

Column removal does not erase historical plaintext in pre-cutover dumps, WAL, dead tuples, replicas or snapshots. Apply the relevant retention policy to each copy. Normal deployment backup and restore scripts intentionally leave the chat ring out of the bundle. Supply it separately when restoring encrypted data, then validate a known history read with the matching application image.

## Rotate

Add a distinct key ID and fresh key material. Back up the updated ring independently. For overlapping web slots, first distribute the new key while leaving the old ID active and recreate both containers so each process can read both keys. Only then change the active ID and recreate the writers. A maintenance window can instead stop all writers before activation. A file-backed mount may keep pointing to an old inode after an atomic host file replacement, so recreate the containers instead of merely restarting the existing process.

Rotate at least every 90 days and before an estimated 16 million encryptions per key, whichever comes first. Include backfills, introductions and all writers in that estimate. Rotation changes future writes. Keep historical keys while any database row or restorable backup still needs them. If a key is lost, affected chat bodies remain unavailable until the exact key material is restored.
