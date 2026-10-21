import { initializeApp } from 'firebase/app';
import { getMessaging, onBackgroundMessage } from 'firebase/messaging/sw';

self.addEventListener('notificationclick', event => {
  event.stopImmediatePropagation();
  event.notification.close();
  // Request the window directly in the click handler, including when no tabs
  // exist. Keep the worker alive until opening and focusing have completed.
  const url = new URL('/messages', self.location.origin).href;
  event.waitUntil(self.clients.openWindow(url).then(client => client?.focus()));
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
