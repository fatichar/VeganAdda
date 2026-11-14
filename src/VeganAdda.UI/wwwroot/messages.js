const preference = "veganadda.enterToSend";
const listeners = new WeakMap();
const inboxObservers = new WeakMap();
let menuOutsideClickHandler;

export function observeInbox(inbox) {
    if (!inbox || typeof IntersectionObserver === "undefined") return;
    inboxObservers.get(inbox)?.disconnect();
    const buttons = inbox.querySelectorAll("[data-inbox-more]:not(:disabled)");
    if (!buttons.length) return;
    const observer = new IntersectionObserver(entries => {
        const button = entries.find(entry => entry.isIntersecting && !entry.target.disabled)?.target;
        if (!button?.isConnected || inboxObservers.get(inbox) !== observer) return;
        observer.disconnect();
        button.click();
    }, { root: inbox, rootMargin: "0px 0px 96px" });
    inboxObservers.set(inbox, observer);
    buttons.forEach(button => observer.observe(button));
}

export function scrollToLatest(history) {
    if (history) history.scrollTop = history.scrollHeight;
}

export function attachMenuOutsideClick(menu, dotnet) {
    detachMenuOutsideClick();
    menuOutsideClickHandler = event => {
        if (!menu.isConnected) {
            detachMenuOutsideClick();
            return;
        }
        if (event.target instanceof Node && menu.contains(event.target)) return;
        detachMenuOutsideClick();
        dotnet.invokeMethodAsync("CloseMenuFromOutside").catch(() => {});
    };
    document.addEventListener("pointerdown", menuOutsideClickHandler, true);
}

export function detachMenuOutsideClick() {
    if (!menuOutsideClickHandler) return;
    document.removeEventListener("pointerdown", menuOutsideClickHandler, true);
    menuOutsideClickHandler = undefined;
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
