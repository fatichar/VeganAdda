import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import vm from 'node:vm';

function setup(supported = true) {
    const observers = [];
    class Observer {
        constructor(callback) { this.callback = callback; observers.push(this); }
        observe(element) { this.element = element; }
        disconnect() { this.disconnected = true; }
    }
    const context = vm.createContext(supported ? { IntersectionObserver: Observer } : {});
    const source = readFileSync(new URL('../src/VeganAdda.UI/wwwroot/discovery.js', import.meta.url), 'utf8');
    vm.runInContext(source.replaceAll('export ', ''), context);
    return { context, observers };
}

test('scroll loads once per render and disconnects replaced observers', async () => {
    const { context, observers } = setup();
    const element = {};
    let calls = 0;
    const helper = { invokeMethodAsync: async name => { assert.equal(name, 'LoadNextPage'); calls++; } };
    context.observeScroll(element, helper);
    const first = observers[0];
    first.callback([{ isIntersecting: false }]);
    assert.equal(calls, 0);
    first.callback([{ isIntersecting: true }]);
    first.callback([{ isIntersecting: true }]);
    assert.equal(calls, 1);
    assert.equal(first.disconnected, true);
    context.observeScroll(element, helper);
    context.observeScroll(element, helper);
    assert.equal(observers[1].disconnected, true);
    context.unobserveScroll(element);
    observers[2].callback([{ isIntersecting: true }]);
    assert.equal(calls, 1);
});

test('disabled or unsupported observation leaves manual loading available', () => {
    const { context, observers } = setup();
    context.observeScroll({}, {}, false);
    assert.equal(observers.length, 0);
    assert.doesNotThrow(() => setup(false).context.observeScroll({}, {}));
});

test('navigation during a callback does not create an unhandled rejection', async () => {
    const { context, observers } = setup();
    context.observeScroll({}, { invokeMethodAsync: () => Promise.reject(new Error('Disposed')) });
    observers[0].callback([{ isIntersecting: true }]);
    await new Promise(resolve => setImmediate(resolve));
});
