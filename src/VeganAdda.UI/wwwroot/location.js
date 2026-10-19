export async function locate() {
    const failure = error => ({ country: "", state: "", city: "", countryCode: "", stateCode: "", error });
    if (!window.isSecureContext || !navigator.geolocation)
        return failure("Automatic location needs HTTPS (or localhost). Choose your location below.");
    let position, positionTimer;
    try {
        position = await new Promise((resolve, reject) => {
            // Browser timeouts can exclude the permission prompt. Bound the whole wait.
            positionTimer = setTimeout(() => reject({ code: 3 }), 20000);
            navigator.geolocation.getCurrentPosition(resolve, reject,
                { enableHighAccuracy: false, timeout: 20000, maximumAge: 60000 });
        });
    } catch (error) {
        return failure(error.code === 1 ? "Location permission was denied. Allow it in your browser's site settings, or choose your location below."
            : error.code === 3 ? "Your device took too long to find a location. Try again or choose your location below."
            : "Your device couldn't determine its location. Check device location settings, or choose your location below.");
    } finally { clearTimeout(positionTimer); }
    let blockedByPolicy = false, requestTimedOut = false;
    const policyViolation = event => {
        if (event.effectiveDirective === "connect-src" && /^https:\/\/(api\.bigdatacloud\.net|api-bdc\.io)(\/|$)/.test(event.blockedURI)) blockedByPolicy = true;
    };
    document.addEventListener("securitypolicyviolation", policyViolation);
    const controller = new AbortController();
    const timer = setTimeout(() => { requestTimedOut = true; controller.abort(); }, 15000);
    try {
        const query = new URLSearchParams({ latitude: position.coords.latitude, longitude: position.coords.longitude, localityLanguage: "en" });
        const response = await fetch(`https://api.bigdatacloud.net/data/reverse-geocode-client?${query}`,
            { credentials: "omit", referrerPolicy: "no-referrer", signal: controller.signal });
        if (!response.ok) return failure(response.status === 429
            ? "The location service is busy. Try again later or choose your location below."
            : "The location service couldn't resolve your address. Choose your location below.");
        let place;
        try { place = await response.json(); }
        catch { return failure("The location service returned an unexpected response. Try again or choose your location below."); }
        const country = place.countryName || "", state = place.principalSubdivision || "", city = place.city || place.locality || "";
        return { country, state, city, countryCode: place.countryCode || "", stateCode: place.principalSubdivisionCode || "",
            error: !country || !state || !city ? "Only part of your location was found. Choose the remaining fields below." : "" };
    } catch {
        return failure(blockedByPolicy ? "This running app's security policy blocks location lookup. Restart VeganAdda and reload this page."
            : requestTimedOut ? "The location service took too long to respond. Try again or choose your location below."
            : navigator.onLine === false ? "You're offline. Reconnect or choose your location below."
            : "Couldn't reach the location service. Check your connection, or choose your location below.");
    } finally { clearTimeout(timer); document.removeEventListener("securitypolicyviolation", policyViolation); }
}
