let trigger;
const placeKey = 'veganadda-discovery-place';
export function loadPlace() {
    try { return JSON.parse(localStorage.getItem(placeKey) || 'null'); }
    catch { return null; }
}
export function savePlace(place) {
    try { localStorage.setItem(placeKey, JSON.stringify(place)); }
    catch { }
}
export function openPanel(panel) {
    trigger = document.activeElement;
    document.body.style.overflow = 'hidden';
    panel.focus();
}
export function closePanel() {
    document.body.style.overflow = '';
    if (trigger?.isConnected) trigger.focus();
    trigger = null;
}
export function focusPanel(panel, edge) {
    const controls = [...panel.querySelectorAll('button:not(:disabled), input:not(:disabled), select:not(:disabled), a[href]')];
    (edge === 'last' ? controls.at(-1) : controls[0])?.focus();
}
export async function currentPosition() {
    if (!window.isSecureContext || !navigator.geolocation)
        return {error:'Nearby search needs HTTPS (or localhost). Choose a city instead.'};
    let timer;
    try {
        const position = await new Promise((resolve, reject) => {
            timer = setTimeout(() => reject({code:3}), 20000);
            navigator.geolocation.getCurrentPosition(resolve, reject, {enableHighAccuracy:true,timeout:20000,maximumAge:60000});
        });
        return {latitude:position.coords.latitude,longitude:position.coords.longitude,error:''};
    } catch (e) {
        return {error:e.code === 1 ? 'Location permission was denied. Allow it in your browser settings, or choose a city.'
            : e.code === 3 ? 'Finding your location took too long. Try again or choose a city.'
            : "Your device couldn't determine its location. Try again or choose a city."};
    } finally {clearTimeout(timer);}
}

const scrollObservers = new Map();
export function observeScroll(element, dotNetHelper, enabled = true) {
    unobserveScroll(element);
    if (!element || !enabled || typeof IntersectionObserver === 'undefined') return;
    const observer = new IntersectionObserver(entries => {
        if (entries.some(entry => entry.isIntersecting) && scrollObservers.get(element) === observer) {
            unobserveScroll(element);
            // The component rearms after rendering; one observation loads one page.
            dotNetHelper.invokeMethodAsync('LoadNextPage').catch(() => {});
        }
    }, { rootMargin: '400px' });
    observer.observe(element);
    scrollObservers.set(element, observer);
}
export function unobserveScroll(element) {
    const observer = scrollObservers.get(element);
    if (observer) {
        observer.disconnect();
        scrollObservers.delete(element);
    }
}
