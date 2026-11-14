// Installed-app worker: shows a static offline page when a page load cannot reach the server.
// Pages are prerendered per member, so they are never cached here; API calls, SignalR and
// fingerprinted assets go straight to the network and the browser's HTTP cache.
const OFFLINE_CACHE = 'veganadda-offline-v1';
const OFFLINE_PAGE = '/offline.html';
const OFFLINE_ASSETS = [OFFLINE_PAGE, '/offline.css', '/offline.js', '/icon-192.png'];

self.addEventListener('install', event => {
  event.waitUntil(caches.open(OFFLINE_CACHE)
    .then(cache => cache.addAll(OFFLINE_ASSETS.map(url => new Request(url, { cache: 'reload' }))))
    .then(() => self.skipWaiting()));
});

self.addEventListener('activate', event => {
  event.waitUntil((async () => {
    for (const key of await caches.keys())
      if (key.startsWith('veganadda-') && key !== OFFLINE_CACHE) await caches.delete(key);
    // Preload starts the page request while the worker boots, so online loads stay fast.
    if (self.registration.navigationPreload) await self.registration.navigationPreload.enable();
    await self.clients.claim();
  })());
});

const offlineCopy = path => caches.match(path, { cacheName: OFFLINE_CACHE }).then(cached => cached || Response.error());

self.addEventListener('fetch', event => {
  const request = event.request;
  if (request.method !== 'GET') return;
  if (request.mode === 'navigate') {
    event.respondWith((async () => {
      try {
        return (await event.preloadResponse) || await fetch(request);
      } catch {
        return offlineCopy(OFFLINE_PAGE);
      }
    })());
    return;
  }
  // The offline page's own stylesheet, script and icon: network first, cached copy when offline.
  const url = new URL(request.url);
  if (url.origin === self.location.origin && url.search === '' && OFFLINE_ASSETS.includes(url.pathname))
    event.respondWith(fetch(request).catch(() => offlineCopy(url.pathname)));
});
