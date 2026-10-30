// Keep view preferences across reloads, isolated by tab and account.
function key(view, account) {
    return `veganadda-view:${view}:${account || 'visitor'}`;
}
export function loadViewState(view, account) {
    try { return JSON.parse(sessionStorage.getItem(key(view, account)) || 'null'); }
    catch { return null; }
}
export function saveViewState(view, account, state) {
    try { sessionStorage.setItem(key(view, account), JSON.stringify(state)); }
    catch { }
}
