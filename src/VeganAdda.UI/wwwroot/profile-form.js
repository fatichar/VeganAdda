export function initializeTabs(form) {
    const editor = form.closest('.profile-editor');
    const fitEditor = () => {
        if (!editor.isConnected) { window.removeEventListener('resize', fitEditor); window.visualViewport?.removeEventListener('resize', fitEditor); return; }
        const bottomNav = document.querySelector('.bottom-nav');
        const bottom = bottomNav && getComputedStyle(bottomNav).display !== 'none' ? bottomNav.getBoundingClientRect().height : 0;
        const header = document.querySelector('.site-header');
        const banner = document.querySelector('.dev-banner');
        const page = editor.closest('.page');
        const top = (header?.getBoundingClientRect().bottom ?? 0) + (banner?.getBoundingClientRect().height ?? 0) + (page ? parseFloat(getComputedStyle(page).paddingTop) : 16);
        editor.style.setProperty('--profile-editor-top', `${top}px`);
        const viewport = window.visualViewport;
        const available = (viewport?.height ?? window.innerHeight) + (viewport?.offsetTop ?? 0) - top - bottom - 8;
        editor.style.setProperty('--profile-editor-height', `${Math.max(180, available)}px`);
    };
    fitEditor();
    window.addEventListener('resize', fitEditor);
    window.visualViewport?.addEventListener('resize', fitEditor);
    form.addEventListener('keydown', event => {
        // Selecting an autocomplete suggestion must not submit the profile form.
        if (event.target.id === 'verification-referrer' && event.key === 'Enter') event.preventDefault();
    });
    const list = form.querySelector('[role="tablist"]');
    list.addEventListener('click', event => {
        if (event.target.closest('[role="tab"]')) form.querySelector('.profile-scroll').scrollTop = 0;
    });
    list.addEventListener('keydown', event => {
        const tabs = [...list.querySelectorAll('[role="tab"]')];
        const current = tabs.indexOf(event.target);
        if (current < 0) return;
        let next;
        if (event.key === 'ArrowRight') next = (current + 1) % tabs.length;
        else if (event.key === 'ArrowLeft') next = (current + tabs.length - 1) % tabs.length;
        else if (event.key === 'Home') next = 0;
        else if (event.key === 'End') next = tabs.length - 1;
        else return;
        event.preventDefault();
        tabs[next].click();
        tabs[next].focus();
    });
}

function firstInvalid(form) {
    return [...form.elements].find(control => control.willValidate && !control.validity.valid);
}

export function invalidPanel(form) {
    const invalid = firstInvalid(form);
    return invalid ? Number(invalid.closest('[role="tabpanel"]').id.split('-').at(-1)) : null;
}

export function reportValidity(form) {
    const invalid = firstInvalid(form);
    if (!invalid) return;
    for (let parent = invalid.parentElement; parent && parent !== form; parent = parent.parentElement)
        if (parent.tagName === 'DETAILS') parent.open = true;
    invalid.focus();
    invalid.reportValidity();
}
