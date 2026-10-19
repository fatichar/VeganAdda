const countryFiles = new Map();
let countryList;

async function read(file) {
    const response = await fetch(new URL(`./data/locations/${file}.json`, import.meta.url));
    if (!response.ok) throw new Error("Location lists could not be loaded. Please retry.");
    return response.json();
}

export async function countries() {
    if (!countryList) countryList = await read("countries");
    return countryList;
}

export async function states(countryCode) {
    if (!/^[A-Z]{2}$/.test(countryCode)) return [];
    if (!countryFiles.has(countryCode)) countryFiles.set(countryCode, await read(countryCode));
    return countryFiles.get(countryCode);
}

function normalized(name) {
    return (name || "").normalize("NFD").replace(/\p{M}/gu, "").toLowerCase().replace(/[^\p{L}\p{N}]/gu, "");
}

// Match stable ISO codes before provider-specific names; keep detected localities
// that are absent from the community-maintained city list.
export async function resolve(place) {
    const options = await countries();
    const country = options.find(c => c.code === place.countryCode)
        || options.find(c => normalized(c.name) === normalized(place.country));
    const subdivisions = country ? await states(country.code) : [];
    const stateCode = (place.stateCode || "").split("-").at(-1);
    const state = subdivisions.find(s => s.code === stateCode)
        || subdivisions.find(s => normalized(s.name) === normalized(place.state));
    const aliases = { bangalore: "Bengaluru", bombay: "Mumbai", calcutta: "Kolkata", madras: "Chennai" };
    const cityName = aliases[normalized(place.city)] || place.city;
    const city = state?.cities.find(c => normalized(c) === normalized(cityName)) || cityName || "";
    return { country: country?.name || place.country || "", state: state?.name || place.state || "", city };
}
