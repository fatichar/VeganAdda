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

    write("countries.json", sorted([
        {"code": c["iso2"], "name": c["name"]} for c in countries
    ], key=lambda c: c["name"]))
    for country in countries:
        states = [{"code": s["iso2"], "name": s["name"],
                   "cities": sorted(set(c["name"] for c in s["cities"]))}
                  for s in country["states"]]
        write(country["iso2"] + ".json", sorted(states, key=lambda s: s["name"]))
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
        "split into one public JSON file per country. No coordinates are included. "
        "This adapted database is available under ODbL 1.0; see LICENSE.txt and NOTICE-SOURCES.txt. "
        "The upstream data is community maintained and may omit small localities. "
        "Regenerate with python scripts/update-locations.py.\n", encoding="utf-8")
    print(f"Wrote {len(countries)} countries to {DEST}")


if __name__ == "__main__":
    main()
