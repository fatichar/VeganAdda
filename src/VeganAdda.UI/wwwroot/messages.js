const preference = "veganadda.enterToSend";
const listeners = new WeakMap();

export function scrollToLatest(history) {
    if (history) history.scrollTop = history.scrollHeight;
}

export function attach(form) {
    if (listeners.has(form)) return;
    const checkbox = form.querySelector("[data-enter-send]");
    const textarea = form.querySelector("textarea");
    try { checkbox.checked = localStorage.getItem(preference) !== "false"; } catch { }
    const save = () => { try { localStorage.setItem(preference, String(checkbox.checked)); } catch { } };
    const keydown = event => {
        if (event.key !== "Enter" || !checkbox.checked || event.shiftKey || event.ctrlKey || event.altKey || event.metaKey || event.isComposing || event.keyCode === 229) return;
        event.preventDefault();
        if (!event.repeat) form.requestSubmit();
    };
    checkbox.addEventListener("change", save);
    textarea.addEventListener("keydown", keydown);
    listeners.set(form, { checkbox, textarea, save, keydown });
}

export function detach(form) {
    const handlers = listeners.get(form);
    if (!handlers) return;
    handlers.checkbox.removeEventListener("change", handlers.save);
    handlers.textarea.removeEventListener("keydown", handlers.keydown);
    listeners.delete(form);
}
