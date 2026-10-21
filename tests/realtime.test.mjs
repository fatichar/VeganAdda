import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import vm from 'node:vm';

const source = readFileSync(new URL('../src/VeganAdda.Web/VeganAdda.Web.Client/wwwroot/js/realtime.js', import.meta.url), 'utf8')
    .replaceAll('export function', 'function');
function setup() {
    const document = new EventTarget(), window = new EventTarget(), timers = new Map();
    document.visibilityState = 'visible';
    let next = 0, calls = 0;
    const context = vm.createContext({
        document, window, console,
        setTimeout(fn) { const id = ++next; timers.set(id, fn); return id; },
        clearTimeout(id) { timers.delete(id); }
    });
    vm.runInContext(source, context);
    const receiver = { async invokeMethodAsync(method) { assert.equal(method, 'ResumeRealtime'); calls++; } };
    context.attach(receiver);
    return {
        document, window, context, receiver,
        get calls() { return calls; },
        flush() { for (const [id, fn] of timers) { timers.delete(id); fn(); } }
    };
}
test('returning to a visible tab requests recovery once for a burst of events', () => {
    const h = setup();
    h.document.dispatchEvent(new Event('visibilitychange'));
    h.window.dispatchEvent(new Event('focus'));
    h.window.dispatchEvent(new Event('pageshow'));
    h.flush();
    assert.equal(h.calls, 1);
});
test('hidden tabs do not start recovery; becoming visible catches up', () => {
    const h = setup();
    h.document.visibilityState = 'hidden';
    h.window.dispatchEvent(new Event('online'));
    h.flush();
    assert.equal(h.calls, 0);
    h.document.visibilityState = 'visible';
    h.document.dispatchEvent(new Event('visibilitychange'));
    h.flush();
    assert.equal(h.calls, 1);
});
test('returning online while visible requests recovery', () => {
    const h = setup();
    h.window.dispatchEvent(new Event('online'));
    h.flush();
    assert.equal(h.calls, 1);
});
test('detaching cancels queued work and removes lifecycle listeners', () => {
    const h = setup();
    h.window.dispatchEvent(new Event('focus'));
    h.context.detach();
    h.flush();
    h.window.dispatchEvent(new Event('online'));
    h.document.dispatchEvent(new Event('visibilitychange'));
    h.flush();
    assert.equal(h.calls, 0);
});
test('attaching again replaces the old receiver', () => {
    const h = setup();
    let replacementCalls = 0;
    h.context.attach({ async invokeMethodAsync() { replacementCalls++; } });
    h.window.dispatchEvent(new Event('focus'));
    h.flush();
    assert.equal(h.calls, 0);
    assert.equal(replacementCalls, 1);
});
