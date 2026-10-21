import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import vm from 'node:vm';

const source = readFileSync(new URL('../src/VeganAdda.Web/VeganAdda.Web.Client/src/push-worker.js', import.meta.url), 'utf8')
  .replace(/^import .*;\r?\n/gm, '');
const inbox = 'https://veganadda.com/messages';

function worker(openWindow) {
  let listener;
  // A fresh worker context represents startup with no page/window available.
  vm.runInNewContext(source, {
    URL, initializeApp() {}, getMessaging() {}, onBackgroundMessage() {},
    self: {
      location: { origin: 'https://veganadda.com' },
      addEventListener(name, handler) { if (name === 'notificationclick') listener = handler; },
      clients: { openWindow }
    }
  });
  return () => {
    const calls = [];
    let completion;
    listener({
      notification: { close() { calls.push('close'); } },
      stopImmediatePropagation() { calls.push('stop'); },
      waitUntil(promise) { completion = promise; }
    });
    return { calls, completion };
  };
}

test('click requests an inbox window synchronously with no existing browser windows', async () => {
  let opened = false, focused = false;
  const click = worker(async url => {
    assert.equal(url, inbox);
    opened = true;
    return { async focus() { focused = true; } };
  });
  const { calls, completion } = click();
  assert.equal(opened, true, 'Do not defer the window request behind tab lookup or navigation');
  assert.deepEqual(calls, ['stop', 'close']);
  await completion;
  assert.equal(focused, true);
});

test('click keeps the worker alive until the returned window is focused', async () => {
  let finishFocus;
  const focused = new Promise(resolve => { finishFocus = resolve; });
  const click = worker(async () => ({ focus: () => focused }));
  let completed = false;
  const { completion } = click();
  completion.then(() => { completed = true; });
  await Promise.resolve();
  assert.equal(completed, false);
  finishFocus();
  await completion;
  assert.equal(completed, true);
});

test('click handles openWindow returning no same-origin client', async () => {
  await worker(async () => null)().completion;
});

test('browser launch failures remain observable through the click lifetime promise', async () => {
  const error = new Error('Browser refused the window request');
  await assert.rejects(worker(async () => { throw error; })().completion, e => e === error);
});
