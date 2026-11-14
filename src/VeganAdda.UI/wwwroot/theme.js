// Applies the color theme before first paint and wires the header toggle.
// A saved choice wins; with none saved the OS preference is followed live.
(() => {
    const key = 'veganadda-theme', root = document.documentElement;
    const media = window.matchMedia('(prefers-color-scheme: dark)');
    const colors = { light: '#fffdf9', dark: '#121b16' };
    const valid = v => v === 'light' || v === 'dark' ? v : null;
    const saved = () => { try { return valid(localStorage.getItem(key)); } catch { return null; } };
    // The in-memory choice is authoritative; persistence is best-effort so the toggle
    // still works for this page when storage is blocked or a write fails.
    let choice = saved();
    const store = v => { try { v ? localStorage.setItem(key, v) : localStorage.removeItem(key); } catch { } };
    const current = () => choice ?? (media.matches ? 'dark' : 'light');
    function apply() {
        const theme = current();
        if (root.dataset.theme !== theme) root.dataset.theme = theme;
        for (const meta of document.querySelectorAll('meta[name="theme-color"]')) if (meta.content !== colors[theme]) meta.content = colors[theme];
        for (const button of document.querySelectorAll('[data-theme-toggle]')) {
            const label = theme === 'dark' ? 'Switch to light theme' : 'Switch to dark theme';
            if (button.getAttribute('aria-label') !== label) { button.setAttribute('aria-label', label); button.title = label; }
        }
    }
    apply();
    media.addEventListener('change', apply);
    window.addEventListener('storage', e => { if (e.key === key) { choice = valid(e.newValue); apply(); } });
    document.addEventListener('click', e => {
        if (!e.target.closest?.('[data-theme-toggle]')) return;
        const next = current() === 'dark' ? 'light' : 'dark';
        // Choosing the OS theme again returns to following the OS.
        choice = next === (media.matches ? 'dark' : 'light') ? null : next;
        store(choice);
        apply();
    });
    // Blazor rendering and enhanced navigation can replace the attribute, metas or button; re-apply.
    let pending = false;
    const schedule = () => { if (!pending) { pending = true; requestAnimationFrame(() => { pending = false; apply(); }); } };
    new MutationObserver(schedule).observe(root, { attributes: true, attributeFilter: ['data-theme'], childList: true, subtree: true });
})();
