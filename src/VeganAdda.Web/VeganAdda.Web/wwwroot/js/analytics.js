// Only public, non-personal pageviews are allowed to leave the browser.
// This hook must exist before Umami starts (data-before-send).
(() => {
    const publicPages = new Set(['/', '/events', '/signin', '/privacy', '/terms', '/help']);
    window.veganAddaAnalyticsBeforeSend = (type, payload) => {
        if (navigator.doNotTrack === '1' || navigator.globalPrivacyControl === true ||
            type !== 'event' || payload.name || !publicPages.has(window.location.pathname)) return false;
        let url;
        try { url = new URL(payload.url, window.location.origin); } catch { return false; }
        if (url.origin !== window.location.origin || !publicPages.has(url.pathname)) return false;
        // Explicit allowlist: no titles containing member names, referrers, search,
        // fragments, event properties, distinct IDs, replays or session attributes.
        return {
            website: payload.website,
            hostname: window.location.hostname,
            screen: payload.screen,
            language: payload.language,
            url: url.pathname,
            title: `VeganAdda ${url.pathname}`,
            referrer: ''
        };
    };
})();
