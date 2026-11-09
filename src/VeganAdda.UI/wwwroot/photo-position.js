export function enablePhotoPositioning(target, position) {
    const image = target.querySelector("img");
    const marker = target.querySelector(".photo-marker");
    let press;

    function setPosition(x, y) {
        position.value = `${toPercent(x)},${toPercent(y)}`;
        position.dispatchEvent(new Event("input", { bubbles: true }));
    }

    image.addEventListener("pointerdown", event => {
        if (event.isPrimary && event.button === 0) press = { x: event.clientX, y: event.clientY };
    });

    image.addEventListener("click", event => {
        // Browsers can dispatch a click when a drag starts and ends on the same image.
        const moved = press && Math.hypot(event.clientX - press.x, event.clientY - press.y) > 6;
        press = undefined;
        if (position.disabled || moved) return;
        const { left, top, width, height } = image.getBoundingClientRect();
        if (!width || !height) return;
        setPosition(100 * (event.clientX - left) / width, 100 * (event.clientY - top) / height);
    });
    image.addEventListener("dragstart", event => event.preventDefault());

    target.addEventListener("keydown", event => {
        const step = arrowSteps.get(event.key);
        if (!step || position.disabled || event.altKey || event.ctrlKey || event.metaKey) return;
        event.preventDefault();
        setPosition(Number(marker.dataset.photoX) + step[0], Number(marker.dataset.photoY) + step[1]);
    });
}

const arrowSteps = new Map([
    ["ArrowLeft", [-1, 0]],
    ["ArrowRight", [1, 0]],
    ["ArrowUp", [0, -1]],
    ["ArrowDown", [0, 1]]
]);
const toPercent = value => Math.round(Math.max(0, Math.min(100, value)));
