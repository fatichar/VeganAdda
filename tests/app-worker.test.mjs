import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import vm from 'node:vm';

const source = readFileSync(new URL('../src/VeganAdda.Web/VeganAdda.Web/wwwroot/js/app-worker.js', import.meta.url), 'utf8');
const offline = new Response('offline page');
const page = { mode: 'navigate', method: 'GET', url: 'https://veganadda.com/events' };

function worker({ fetch, cached = offline } = {}) {
  const listeners = {}, opened = [], deleted = [], matched = [];
  const caches = {
    async open(name) { opened.push(name); return { async addAll(requests) { opened.push(Array.from(requests, r => r.url)); } }; },
    async keys() { return ['veganadda-offline-v0', 'veganadda-offline-v1', 'other-app']; },
    async delete(name) { deleted.push(name); },
    async match(url, options) { matched.push(url); assert.equal(options.cacheName, 'veganadda-offline-v1'); return cached; }
  };
  const self = {
    addEventListener(name, handler) { listeners[name] = handler; },
    location: { origin: 'https://veganadda.com' },
    skipWaiting: async () => {},
    clients: { claim: async () => {} },
    registration: { navigationPreload: { enabled: false, async enable() { this.enabled = true; } } }
  };
  vm.runInNewContext(source, { self, caches, fetch, URL, Request: class { constructor(url, init) { this.url = url; this.init = init; } }, Response });
  return { listeners, opened, deleted, matched, self };
}

function dispatchFetch(listeners, request, preloadResponse = Promise.resolve(undefined)) {
  let response;
  listeners.fetch({ request, preloadResponse, respondWith(value) { response = value; } });
  return response;
}

test('install precaches only the static offline page and its assets', async () => {
  const { listeners, opened } = worker();
  let done;
  listeners.install({ waitUntil(p) { done = p; } });
  await done;
  assert.deepEqual(opened, ['veganadda-offline-v1', ['/offline.html', '/offline.css', '/offline.js', '/icon-192.png']]);
});

test('activate removes old app caches only and enables navigation preload', async () => {
  const { listeners, deleted, self } = worker();
  let done;
  listeners.activate({ waitUntil(p) { done = p; } });
  await done;
  assert.deepEqual(deleted, ['veganadda-offline-v0']);
  assert.equal(self.registration.navigationPreload.enabled, true);
});

test('page loads use the network and are never cached', async () => {
  const live = new Response('live');
  const { listeners } = worker({ fetch: async () => live });
  assert.equal(await dispatchFetch(listeners, page), live);
  const preloaded = new Response('preloaded');
  assert.equal(await dispatchFetch(listeners, page, Promise.resolve(preloaded)), preloaded);
});

test('failed page loads show the offline page', async () => {
  const { listeners, matched } = worker({ fetch: async () => { throw new TypeError('offline'); } });
  assert.equal(await dispatchFetch(listeners, page), offline);
  const rejectedPreload = Promise.reject(new TypeError('offline'));
  assert.equal(await dispatchFetch(listeners, page, rejectedPreload), offline);
  assert.deepEqual(matched, ['/offline.html', '/offline.html']);
});

test('the offline page assets fall back to their cached copies', async () => {
  const live = new Response('live css');
  assert.equal(await dispatchFetch(worker({ fetch: async () => live }).listeners, { mode: 'no-cors', method: 'GET', url: 'https://veganadda.com/offline.css' }), live);
  const { listeners, matched } = worker({ fetch: async () => { throw new TypeError('offline'); } });
  for (const path of ['/offline.css', '/offline.js', '/icon-192.png'])
    assert.equal(await dispatchFetch(listeners, { mode: 'no-cors', method: 'GET', url: 'https://veganadda.com' + path }), offline);
  assert.deepEqual(matched, ['/offline.css', '/offline.js', '/icon-192.png']);
});

test('API, realtime, asset and non-GET requests are left to the browser', () => {
  const { listeners } = worker({ fetch: () => assert.fail('must not fetch') });
  for (const request of [
    { mode: 'cors', method: 'GET', url: 'https://veganadda.com/api/profile' },
    { mode: 'no-cors', method: 'GET', url: 'https://veganadda.com/_content/VeganAdda.UI/app.css' },
    { mode: 'no-cors', method: 'GET', url: 'https://veganadda.com/offline.css?v=2' },
    { mode: 'no-cors', method: 'GET', url: 'https://other.example/offline.css' },
    { mode: 'navigate', method: 'POST', url: 'https://veganadda.com/events' }])
    assert.equal(dispatchFetch(listeners, request), undefined);
});
