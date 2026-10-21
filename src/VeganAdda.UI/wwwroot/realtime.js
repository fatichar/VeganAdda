window.veganAddaVisible = () => document.visibilityState !== "hidden";

// Runs independently of Blazor so a failed runtime download still has a way out.
// Successful hydration removes the notice, so slow startup can still finish normally.
window.setTimeout(() => {
    const status = document.querySelector("[data-startup-status]");
    const reload = document.querySelector("[data-startup-reload]");
    if (!status || !reload) return;
    status.textContent = "Loading is taking longer than expected. Check your connection or reload to try again.";
    reload.hidden = false;
}, 15000);
