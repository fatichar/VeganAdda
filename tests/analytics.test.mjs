import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import vm from 'node:vm';

const source = readFileSync(new URL('../src/VeganAdda.Web/VeganAdda.Web/wwwroot/js/analytics.js', import.meta.url), 'utf8');
function hook(path = '/', navigator = {}) {
    const window = { location: { origin: 'https://veganadda.com', hostname: 'veganadda.com', pathname: path } };
    vm.runInNewContext(source, { window, navigator, URL });
    return window.veganAddaAnalyticsBeforeSend;
}
const payload = { website: 'test-website', url: '/?city=private#token', title: 'Private member name', referrer: '/referrals/secret', screen: '1280x720', language: 'en', data: { userId: 'private-id' }, id: 'private-id' };

test('public pageview strips personal fields, search, fragment, title and referrer', () => {
    const result = hook()('event', payload);
    assert.equal(result.url, '/');
    assert.equal(result.title, 'VeganAdda /');
    assert.equal(result.referrer, '');
    assert.equal(result.website, 'test-website');
    assert.equal(JSON.stringify(result).includes('private'), false);
});
test('private routes and transitions from private routes cannot emit pageviews', () => {
    for (const path of ['/messages', '/members/private-id', '/profile/private-id', '/settings', '/admin', '/referrals/secret']) {
        assert.equal(hook(path)('event', { ...payload, url: path }), false);
        assert.equal(hook()('event', { ...payload, url: path }), false);
    }
});
test('events, session/replay data, external URLs and privacy opt-outs are blocked', () => {
    assert.equal(hook()('event', { ...payload, name: 'custom-event' }), false);
    assert.equal(hook()('identify', payload), false);
    assert.equal(hook()('event', { ...payload, url: 'https://other.example/' }), false);
    assert.equal(hook('/', { doNotTrack: '1' })('event', payload), false);
    assert.equal(hook('/', { globalPrivacyControl: true })('event', payload), false);
});
