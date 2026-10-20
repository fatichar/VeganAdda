# Chat push notifications

Chat stays in PostgreSQL and uses SignalR while the app is open. Firebase Cloud Messaging delivers generic browser notifications; message text and sender names are never sent to FCM or displayed on lock screens.

## Configuration

1. In the existing Firebase project's **Project settings > Cloud Messaging**, enable the FCM HTTP v1 API and generate a **Web Push certificate** key pair. Use its public key, not a server key or service-account private key.
2. Set `FIREBASE_MESSAGING_SENDER_ID` and `FIREBASE_WEB_PUSH_VAPID_KEY` in the VPS environment. Compose maps these to `Push:SenderId` and `Push:VapidKey`. Local development can use `Push__SenderId` and `Push__VapidKey` environment variables or .NET user secrets.
3. The existing Firebase Admin credential needs permission to send FCM messages in that same project. If the web API key has API restrictions, allow Firebase Installations and FCM Registration as well as the authentication APIs already in use.
4. Apply all pending migrations (including `PushDeliveryLeases`) using the normal release migration process, then restart the app. No production rollout is performed by adding this implementation.
5. Open Messages and choose **Enable chat notifications**. Permission is requested only after this click. Each browser/device opts in separately. Use HTTPS (localhost is allowed for development). On iPhone/iPad, install the site on the Home Screen first; browser and OS support/settings still control delivery.

Both push settings must be present before the control or background sender is enabled. No credentials are placed in the service worker; it receives only public Firebase configuration. Bundled local worker scripts avoid external script dependencies.

## Delivery and privacy

- Messages and their pending-delivery flags commit in the same transaction. The worker checks every five seconds and sends only to the recipient's registered devices, after checking read receipts, relationship state, blocks and suspension.
- Workers claim a message with a two-minute database lease and release database locks before contacting Firebase. A 45-second delivery budget bounds each claim. Successful devices get persisted receipts; failed devices retry after at least five seconds, up to four attempts. Queued messages older than an hour are discarded. Expired FCM registrations are removed; devices expire after 60 days without refresh. Opening the app refreshes an enabled registration.
- Delivery is at least once: a process crash between provider acknowledgement and receipt persistence can retry a successful send; normal partial-device retries skip successful receipts. Stable notification tags replace existing copies for the same message. Duplicate client message requests do not create another queue entry.
- Sign-out removes the current browser registration. Account deletion cascades device deletion. Browser account changes move the token to one owner. Before displaying an alert, the worker checks the current session and intended recipient; this check needs network access.
- Foreground alerts are suppressed while viewing Messages. Clicking an alert opens the inbox. SignalR and unread counts continue working independently of push configuration.

## Verification

Automated tests use a fake FCM transport with real PostgreSQL to check queue persistence, recipient-only delivery, retries, idempotency, read receipts and block suppression. HTTP tests cover authorization, CSRF and device ownership. Real browser/phone delivery requires the configured Firebase project and permission; a passing build does not prove delivery.

References: [Firebase web setup](https://firebase.google.com/docs/cloud-messaging/web/get-started), [receiving messages](https://firebase.google.com/docs/cloud-messaging/web/receive-messages).
