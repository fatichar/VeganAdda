let outsideClickHandler;

export function attachOutsideClick(dotnet) {
    detachOutsideClick();
    outsideClickHandler = event => {
        if (event.target instanceof Element && event.target.closest(".admin-action-wrap, .admin-bulk-action-wrap")) return;
        dotnet.invokeMethodAsync("CloseMemberActionsFromOutside").catch(() => {});
    };
    document.addEventListener("pointerdown", outsideClickHandler, true);
}

export function detachOutsideClick() {
    if (!outsideClickHandler) return;
    document.removeEventListener("pointerdown", outsideClickHandler, true);
    outsideClickHandler = undefined;
}
