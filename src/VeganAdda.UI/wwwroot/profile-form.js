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

let fieldError = null;
const fieldErrorForms = new WeakSet();

function fieldErrorNote() {
    const note = document.createElement('small');
    note.className = 'field-error';
    note.style.color = 'var(--error)';
    return note;
}

function clearFieldError() {
    fieldError?.note?.remove();
    fieldError = null;
}

function findFieldControl(form) {
    if (!fieldError) return null;
    if (fieldError.id) {
        const byId = form.querySelector('#' + CSS.escape(fieldError.id));
        if (byId) return byId;
    }
    if (!fieldError.name) return null;
    const named = form.elements.namedItem(fieldError.name);
    if (named instanceof RadioNodeList) return named[0] ?? null;
    return named;
}

function placeFieldError(form) {
    if (!fieldError) return;
    const control = findFieldControl(form);
    if (!control || !form.contains(control)) return;
    fieldError.note ??= fieldErrorNote();
    fieldError.note.textContent = fieldError.message;
    (control.closest('label') ?? control).insertAdjacentElement('afterend', fieldError.note);
}

function bindFieldErrorClear(form) {
    if (fieldErrorForms.has(form)) return;
    fieldErrorForms.add(form);
    form.addEventListener('input', event => {
        if (!fieldError) return;
        const control = event.target;
        const sameField = fieldError.id ? control?.id === fieldError.id : fieldError.name !== '' && control?.name === fieldError.name;
        if (sameField) clearFieldError();
    });
    // Blazor can drop the note or remove the invalid control without an input event.
    const observer = new MutationObserver(() => {
        if (!fieldError) return;
        const control = findFieldControl(form);
        if (!control || !form.contains(control)) {
            clearFieldError();
            return;
        }
        const anchor = control.closest('label') ?? control;
        if (anchor.nextSibling !== fieldError.note) placeFieldError(form);
    });
    observer.observe(form, { childList: true, subtree: true });
}

export function reportValidity(form) {
    const invalid = firstInvalid(form);
    if (!invalid) {
        clearFieldError();
        return;
    }
    for (let parent = invalid.parentElement; parent && parent !== form; parent = parent.parentElement)
        if (parent.tagName === 'DETAILS') parent.open = true;
    invalid.focus();
    invalid.reportValidity();
    bindFieldErrorClear(form);
    fieldError = { id: invalid.id, name: invalid.name || '', message: invalid.validationMessage, note: fieldError?.note ?? fieldErrorNote() };
    placeFieldError(form);
}
