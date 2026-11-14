import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import vm from 'node:vm';

const source = readFileSync(new URL('../src/VeganAdda.UI/wwwroot/pwa.js', import.meta.url), 'utf8');
const iphone = 'Mozilla/5.0 (iPhone; CPU iPhone OS 18_0 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/18.0 Mobile/15E148 Safari/604.1';
const android = 'Mozilla/5.0 (Linux; Android 14) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0 Mobile Safari/537.36';

function storage(initial = {}) {
  const items = new Map(Object.entries(initial));
  return { items, getItem: k => items.has(k) ? items.get(k) : null, setItem: (k, v) => items.set(k, String(v)) };
}

// Loads pwa.js as a fresh page load on the given day.
function page({ userAgent = android, local = storage(), day = 'Mon Nov 16 2026', standalone = false, now = 1_000_000 } = {}) {
  const listeners = {};
  class FakeDate extends Date { toDateString() { return day; } static now() { return now; } }
  const window = {
    navigator: { userAgent, maxTouchPoints: 5 },
    localStorage: local,
    matchMedia: query => ({ matches: standalone && query.includes('standalone'), addEventListener() {} }),
    addEventListener(name, handler) { listeners[name] = handler; },
    isSecureContext: false
  };
  vm.runInNewContext(source, { window, document: { readyState: 'complete' }, Date: FakeDate, Number, String });
  return { api: window.veganAddaInstall, listeners, local };
}

function offerInstall(listeners, outcome = 'accepted') {
  const event = { prompted: false, async prompt() { event.prompted = true; }, userChoice: Promise.resolve({ outcome }) };
  listeners.beforeinstallprompt(event);
  return event;
}

test('banner waits for a second day of use', () => {
  const local = storage();
  const first = page({ local, day: 'Mon Nov 16 2026' });
  offerInstall(first.listeners);
  assert.equal(first.api.bannerState(), 'hidden');
  const sameDay = page({ local, day: 'Mon Nov 16 2026' });
  offerInstall(sameDay.listeners);
  assert.equal(sameDay.api.bannerState(), 'hidden', 'Reloading on the same day is not a return visit');
  const nextDay = page({ local, day: 'Tue Nov 17 2026' });
  assert.equal(nextDay.api.bannerState(), 'hidden', 'Android shows the banner only once the browser offers install');
  offerInstall(nextDay.listeners);
  assert.equal(nextDay.api.bannerState(), 'available');
});

test('iPhone returning members get instructions without a browser event', () => {
  const local = storage({ 'veganadda-install-days': '1', 'veganadda-install-last-day': 'Mon Nov 16 2026' });
  assert.equal(page({ userAgent: iphone, local, day: 'Tue Nov 17 2026' }).api.bannerState(), 'ios');
});

test('installed apps never show the banner', () => {
  const local = storage({ 'veganadda-install-days': '5' });
  assert.equal(page({ userAgent: iphone, local, standalone: true }).api.bannerState(), 'hidden');
});

test('not now hides the banner for thirty days', () => {
  const local = storage({ 'veganadda-install-days': '3' });
  const { api } = page({ userAgent: iphone, local, now: 1_000_000 });
  assert.equal(api.bannerState(), 'ios');
  api.snooze();
  assert.equal(api.bannerState(), 'hidden');
  const day = 24 * 60 * 60 * 1000;
  assert.equal(page({ userAgent: iphone, local, now: 1_000_000 + 29 * day }).api.bannerState(), 'hidden');
  assert.equal(page({ userAgent: iphone, local, now: 1_000_000 + 30 * day }).api.bannerState(), 'ios');
});

test('accepting the browser dialog hides the banner', async () => {
  const local = storage({ 'veganadda-install-days': '2' });
  const { api, listeners } = page({ local });
  const event = offerInstall(listeners);
  assert.equal(api.bannerState(), 'available');
  assert.equal(await api.prompt(), 'installed');
  assert.equal(event.prompted, true);
  assert.equal(api.bannerState(), 'hidden');
});

test('blocked storage keeps the banner hidden instead of failing', () => {
  const local = { getItem() { throw new Error('blocked'); }, setItem() { throw new Error('blocked'); } };
  const { api } = page({ userAgent: iphone, local });
  assert.equal(api.bannerState(), 'hidden');
  api.snooze();
});
