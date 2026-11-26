export async function shareLink(url, title, text) {
    const data = { url, title };
    if (text) data.text = text;

    try {
        if (navigator.share && (!navigator.canShare || navigator.canShare(data))) {
            await navigator.share(data);
            return "shared";
        }
    } catch (error) {
        if (error?.name === "AbortError") return "cancelled";
    }

    try {
        await navigator.clipboard.writeText(url);
        return "copied";
    } catch {
        return "manual";
    }
}
