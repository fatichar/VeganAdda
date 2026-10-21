let cleanup;

export function attach(receiver) {
    detach();
    let timer;
    const resume = () => {
        clearTimeout(timer);
        if (document.visibilityState !== 'visible') return;
        timer = setTimeout(() => {
            receiver.invokeMethodAsync('ResumeRealtime').catch(() => {
                console.warn('Could not refresh the realtime connection.');
            });
        }, 150);
    };
    document.addEventListener('visibilitychange', resume);
    window.addEventListener('online', resume);
    window.addEventListener('pageshow', resume);
    window.addEventListener('focus', resume);
    cleanup = () => {
        clearTimeout(timer);
        document.removeEventListener('visibilitychange', resume);
        window.removeEventListener('online', resume);
        window.removeEventListener('pageshow', resume);
        window.removeEventListener('focus', resume);
    };
}

export function detach() {
    cleanup?.();
    cleanup = undefined;
}
