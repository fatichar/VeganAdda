import { initializeApp, getApps } from 'firebase/app';
import { getMessaging, getToken, deleteToken, isSupported, onMessage } from 'firebase/messaging';

let messaging, registration, listening;
async function request(method, body) {
  const csrf = await (await fetch('/api/csrf')).json();
  const response = await fetch('/api/push/device', { method, credentials: 'same-origin', headers: { 'Content-Type': 'application/json', 'X-CSRF-TOKEN': csrf.token }, body: body ? JSON.stringify(body) : undefined });
  if (!response.ok) throw new Error('Could not update notifications. Please try again.');
}
async function setup() {
  const config = await (await fetch('/api/push/config')).json();
  if (!config.enabled) throw new Error('Push notifications are not configured yet.');
  registration = await navigator.serviceWorker.register('/firebase-messaging-sw.js');
  await navigator.serviceWorker.ready;
  const app = getApps().find(a => a.name === 'chat-push') || initializeApp(config.firebase, 'chat-push');
  messaging = getMessaging(app);
  if (!listening) {
    listening = onMessage(messaging, async payload => {
      const response = await fetch('/api/push/device');
      if (!response.ok) return;
      const device = await response.json();
      if (!location.pathname.startsWith('/messages') && device.enabled && device.userId === payload.data?.userId)
        await registration.showNotification('VeganAdda', { body: 'You have a new chat message.', tag: 'chat-' + payload.data.id, data: { url: '/messages' } });
    });
  }
  const token = await getToken(messaging, { vapidKey: config.vapidKey, serviceWorkerRegistration: registration });
  await request('POST', { token });
}
export async function status() {
  if (!window.isSecureContext || !('Notification' in window) || !await isSupported()) return 'unsupported';
  const config = await (await fetch('/api/push/config')).json();
  if (!config.enabled) return 'unconfigured';
  if (Notification.permission === 'denied') { await request('DELETE'); return 'blocked'; }
  const response = await fetch('/api/push/device');
  if (!response.ok) return 'off';
  return (await response.json()).enabled && Notification.permission === 'granted' ? 'enabled' : 'off';
}
export async function refresh() {
  const current = await status();
  if (current === 'enabled') await setup();
  return current;
}
export async function enable() {
  // Keep the browser permission prompt directly in the user's click gesture.
  if (!('Notification' in window)) throw new Error('Push notifications are unavailable in this browser.');
  if (await Notification.requestPermission() !== 'granted') return 'blocked';
  await setup();
  return 'enabled';
}
export async function disable() {
  await request('DELETE');
  if (messaging) await deleteToken(messaging);
  const worker = await navigator.serviceWorker.getRegistration('/');
  if (worker) for (const notification of await worker.getNotifications()) notification.close();
  return 'off';
}
