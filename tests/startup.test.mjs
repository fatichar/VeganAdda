import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import vm from 'node:vm';

const source = readFileSync(new URL('../src/VeganAdda.UI/wwwroot/realtime.js', import.meta.url), 'utf8');

function startup() {
    const elements = {
        '[data-startup-status]': { textContent: 'Loading…' },
        '[data-startup-reload]': { hidden: true }
    };
    let timeout;
    const window = { setTimeout: (callback, delay) => { timeout = { callback, delay }; } };
    const document = { querySelector: selector => elements[selector] ?? null };
    vm.runInNewContext(source, { window, document });
    return { elements, timeout };
}

test('stalled startup offers recovery without requiring the Blazor runtime', () => {
    const { elements, timeout } = startup();
    assert.equal(elements['[data-startup-reload]'].hidden, true);
    assert.equal(timeout.delay, 15000);
    timeout.callback();
    assert.match(elements['[data-startup-status]'].textContent, /Check your connection/);
    assert.equal(elements['[data-startup-reload]'].hidden, false);
});

test('completed hydration leaves no recovery notice', () => {
    const { elements, timeout } = startup();
    const status = elements['[data-startup-status]'];
    const reload = elements['[data-startup-reload]'];
    delete elements['[data-startup-status]'];
    delete elements['[data-startup-reload]'];
    assert.doesNotThrow(() => timeout.callback());
    assert.equal(status.textContent, 'Loading…');
    assert.equal(reload.hidden, true);
});
