export function initializeTabs(form) {
    const list = form.querySelector('[role="tablist"]');
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
