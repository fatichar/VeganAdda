import { initializeApp } from 'firebase/app';
import { getMessaging, onBackgroundMessage } from 'firebase/messaging/sw';

self.addEventListener('notificationclick', event => {
  event.notification.close();
  event.waitUntil((async () => {
    const windows = await self.clients.matchAll({ type: 'window', includeUncontrolled: true });
    const client = windows.find(w => new URL(w.url).origin === self.location.origin);
    if (client) { await client.navigate('/messages'); await client.focus(); }
    else await self.clients.openWindow('/messages');
  })());
});
onBackgroundMessage(getMessaging(initializeApp(self.firebaseConfig)), async payload => {
  if (payload.data?.kind !== 'chat') return;
  // Re-check this browser's signed-in account before displaying delayed pushes.
  const response = await fetch('/api/push/device', { credentials: 'include', cache: 'no-store' });
  if (!response.ok) return;
  const device = await response.json();
  if (!device.enabled || device.userId !== payload.data.userId) return;
  await self.registration.showNotification('VeganAdda', {
    body: 'You have a new chat message.', tag: 'chat-' + payload.data.id, data: { url: '/messages' }
  });
});
