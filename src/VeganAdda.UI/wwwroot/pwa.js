// Registers the app's service worker and tracks whether VeganAdda can be installed here.
// Chromium browsers (Android, Windows, macOS, Linux, ChromeOS) offer a prompt event; iOS/iPadOS
// and Safari on macOS only install from their own menus, so those get instructions instead.
(() => {
    const nav = window.navigator, ua = nav.userAgent;
    const ios = /iPad|iPhone|iPod/.test(ua) || (/Macintosh/.test(ua) && nav.maxTouchPoints > 1);
    const safariVersion = Number((/Version\/(\d+)/.exec(ua) || [])[1] || 0);
    const macSafari = !ios && /Macintosh/.test(ua) && /Safari\//.test(ua)
        && !/Chrome|Chromium|CriOS|Edg|Firefox|FxiOS|OPR/.test(ua) && safariVersion >= 17;
    const standaloneQuery = window.matchMedia('(display-mode: standalone)');
    const listeners = new Map();
    let deferred = null, installed = false, nextId = 0;

    function state() {
        if (installed || standaloneQuery.matches || nav.standalone === true
            || window.matchMedia('(display-mode: minimal-ui)').matches) return 'installed';
        if (deferred) return 'available';
        if (ios) return 'ios';
        if (macSafari) return 'mac-safari';
        return 'unavailable';
    }
    function notify() {
        const current = state();
        for (const listener of listeners.values()) listener(current);
    }

    // Keep the browser's own install UI; the event is stored so Settings can offer it too.
    window.addEventListener('beforeinstallprompt', e => { deferred = e; notify(); });
    window.addEventListener('appinstalled', () => { deferred = null; installed = true; notify(); });
    standaloneQuery.addEventListener?.('change', notify);

    window.veganAddaInstall = {
        state,
        async prompt() {
            const event = deferred;
            if (!event) return state();
            deferred = null; // A prompt event can only be used once.
            await event.prompt();
            if ((await event.userChoice).outcome === 'accepted') installed = true;
            notify();
            return state();
        },
        subscribe(dotnet) {
            const id = ++nextId;
            listeners.set(id, current => dotnet.invokeMethodAsync('InstallStateChanged', current).catch(() => listeners.delete(id)));
            return id;
        },
        unsubscribe(id) { listeners.delete(id); }
    };

    if ('serviceWorker' in nav && window.isSecureContext) {
        const register = () => nav.serviceWorker.register('/sw.js').catch(() => { });
        if (document.readyState === 'complete') register(); else window.addEventListener('load', register, { once: true });
    }
})();
