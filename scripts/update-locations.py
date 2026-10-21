"""Build the locally served country/state/city dropdown data (ODbL 1.0)."""
import gzip
import hashlib
import json
from pathlib import Path
from urllib.request import urlopen

RELEASE = "v5.0.0-export.1"
SOURCE = f"https://github.com/dr5hn/countries-states-cities-database/releases/download/{RELEASE}/json-countries%2Bstates%2Bcities.json.gz"
DEST = Path(__file__).resolve().parents[1] / "src/VeganAdda.UI/wwwroot/data/locations"


def main():
    raw = urlopen(SOURCE, timeout=90).read()
    countries = json.loads(gzip.decompress(raw))
    DEST.mkdir(parents=True, exist_ok=True)

    def write(name, data):
        (DEST / name).write_text(json.dumps(data, ensure_ascii=False, separators=(",", ":")) + "\n", encoding="utf-8")

    def write_gzip(name, data):
        encoded = json.dumps(data, ensure_ascii=False, separators=(",", ":")).encode("utf-8")
        with (DEST / name).open("wb") as stream:
            with gzip.GzipFile(fileobj=stream, mode="wb", mtime=0) as compressed:
                compressed.write(encoded)

    write("countries.json", sorted([
        {"code": c["iso2"], "name": c["name"]} for c in countries
    ], key=lambda c: c["name"]))
    time_zones = []
    for country in countries:
        states = []
        for state in country["states"]:
            states.append({"code": state["iso2"], "name": state["name"],
                           "cities": sorted(set(city["name"] for city in state["cities"]))})
            time_zones.extend({"country": country["name"], "state": state["name"],
                               "city": city["name"], "timeZone": city["timezone"]}
                              for city in state["cities"] if city.get("timezone"))
        write(country["iso2"] + ".json", sorted(states, key=lambda s: s["name"]))
    write_gzip("time-zones.json.gz", sorted(time_zones, key=lambda item: (item["country"], item["state"], item["city"])))
    for name, url in {
        "LICENSE.txt": f"https://raw.githubusercontent.com/dr5hn/countries-states-cities-database/{RELEASE}/LICENSE",
        "NOTICE-SOURCES.txt": f"https://github.com/dr5hn/countries-states-cities-database/releases/download/{RELEASE}/NOTICE-SOURCES.txt",
    }.items():
        (DEST / name).write_bytes(urlopen(url, timeout=90).read())
    (DEST / "README.md").write_text(
        "# Country, state and city dropdown database\n\n"
        "Source: https://github.com/dr5hn/countries-states-cities-database\n\n"
        f"Release: {RELEASE}\n\nSource SHA-256: {hashlib.sha256(raw).hexdigest()}\n\n"
        "Adaptation: retain country/state codes, names and city names; sort and deduplicate city names; "
        "split into one public JSON file per country, plus a city-to-IANA-time-zone lookup. No coordinates are included. "
        "This adapted database is available under ODbL 1.0; see LICENSE.txt and NOTICE-SOURCES.txt. "
        "The upstream data is community maintained and may omit small localities. "
        "Regenerate with python scripts/update-locations.py.\n", encoding="utf-8")
    print(f"Wrote {len(countries)} countries to {DEST}")


if __name__ == "__main__":
    main()
