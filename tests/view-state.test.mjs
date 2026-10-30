import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import vm from 'node:vm';

const source = readFileSync(new URL('../src/VeganAdda.UI/wwwroot/view-state.js', import.meta.url), 'utf8');
function load(sessionStorage) {
    const context = vm.createContext({ sessionStorage });
    vm.runInContext(source.replaceAll('export ', ''), context);
    return context;
}
function storage() {
    const data = new Map();
    return { getItem: key => data.get(key) ?? null, setItem: (key, value) => data.set(key, value) };
}

test('view selections survive a fresh runtime without leaking across accounts, views or tabs', () => {
    const tab = storage();
    const before = load(tab);
    const filters = { search: 'running', genders: ['Woman'], verifiedVegansOnly: true, minAge: 25 };
    const admin = { tab: 'Reports', verification: 'all', membersPage: 2 };
    before.saveViewState('discover', 'alice', filters);
    before.saveViewState('admin', 'alice', admin);
    const after = load(tab);
    assert.equal(JSON.stringify(after.loadViewState('discover', 'alice')), JSON.stringify(filters));
    assert.equal(JSON.stringify(after.loadViewState('admin', 'alice')), JSON.stringify(admin));
    assert.equal(after.loadViewState('discover', 'bob'), null);
    assert.equal(after.loadViewState('discover', null), null);
    assert.equal(load(storage()).loadViewState('admin', 'alice'), null);
    after.saveViewState('discover', 'alice', { search: '', genders: [] });
    assert.equal(JSON.stringify(load(tab).loadViewState('discover', 'alice')), '{"search":"","genders":[]}');
});

test('unavailable storage and invalid JSON do not break the page', () => {
    const blocked = load({ getItem() { throw Error('Blocked'); }, setItem() { throw Error('Full'); } });
    assert.equal(blocked.loadViewState('admin', 'alice'), null);
    assert.doesNotThrow(() => blocked.saveViewState('admin', 'alice', {}));
    assert.equal(load({ getItem: () => '{bad json' }).loadViewState('admin', 'alice'), null);
});
