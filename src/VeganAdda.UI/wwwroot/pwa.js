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

    // The install banner waits until the member returns on a second day, and stays away for a
    // month after "Not now". Storage is best-effort: without it the banner is simply not shown.
    const daysKey = 'veganadda-install-days', lastDayKey = 'veganadda-install-last-day';
    const snoozeKey = 'veganadda-install-snoozed-until', snoozeMs = 30 * 24 * 60 * 60 * 1000;
    let visitDays = 0, snoozedUntil = 0;
    try {
        const local = window.localStorage, today = new Date().toDateString();
        visitDays = Number(local.getItem(daysKey)) || 0;
        if (local.getItem(lastDayKey) !== today) {
            local.setItem(lastDayKey, today);
            local.setItem(daysKey, String(++visitDays));
        }
        snoozedUntil = Number(local.getItem(snoozeKey)) || 0;
    } catch { visitDays = 0; }

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
        // What the install banner should show: 'available', 'ios' or 'hidden'.
        bannerState() {
            const current = state();
            if (current !== 'available' && current !== 'ios') return 'hidden';
            return visitDays >= 2 && Date.now() >= snoozedUntil ? current : 'hidden';
        },
        snooze() {
            snoozedUntil = Date.now() + snoozeMs;
            try { window.localStorage.setItem(snoozeKey, String(snoozedUntil)); } catch { }
            notify();
        },
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
