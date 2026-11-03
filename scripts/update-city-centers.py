"""Build the server-only, pinned city coordinate catalog from the CSC ODbL export."""
import collections
import gzip
import hashlib
import json
from pathlib import Path
from urllib.request import urlopen

VERSION = "v5.0.0-export.1"
SOURCE = f"https://github.com/dr5hn/countries-states-cities-database/releases/download/{VERSION}/json-countries%2Bstates%2Bcities.json.gz"
SHA256 = "72cbb838c2004ccedc97fb18b10b2d8916fd9275bf227e9a4d26e814adc16eb8"
DEST = Path(__file__).resolve().parents[1] / "src/VeganAdda.Web/VeganAdda.Web/Data/city-centers.json.gz"


def main():
    raw = urlopen(SOURCE, timeout=90).read()
    if hashlib.sha256(raw).hexdigest() != SHA256:
        raise ValueError("The city dataset does not match the pinned source checksum.")
    countries = json.loads(gzip.decompress(raw))
    places = collections.defaultdict(set)
    for country in countries:
        for state in country["states"]:
            for city in state["cities"]:
                try:
                    latitude, longitude = float(city["latitude"]), float(city["longitude"])
                except (ValueError, TypeError, KeyError):
                    continue
                if -90 <= latitude <= 90 and -180 <= longitude <= 180:
                    key = (country["name"].strip(), state["name"].strip(), city["name"].strip())
                    places[key].add((latitude, longitude))
    rows = []
    for (country, state, city), coordinates in places.items():
        if len(coordinates) == 1:
            latitude, longitude = next(iter(coordinates))
            rows.append(dict(country=country, state=state, city=city, latitude=latitude, longitude=longitude))
    encoded = json.dumps(rows, ensure_ascii=False, separators=(",", ":")).encode("utf-8")
    with DEST.open("wb") as output:
        with gzip.GzipFile(fileobj=output, mode="wb", mtime=0) as compressed:
            compressed.write(encoded)
    print(f"Wrote {len(rows)} unambiguous city centers to {DEST}")


if __name__ == "__main__":
    main()
