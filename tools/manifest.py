#!/usr/bin/env python3
"""manifest.json, the Jellyfin plug-in repository, and meta.json in each zip.

  python3 tools/manifest.py meta VERSION                   prints meta.json for the zip
  python3 tools/manifest.py add VERSION ZIP_URL CHECKSUM   adds VERSION to manifest.json (newest first)
The changelog of a version is its section in CHANGELOG.md.
"""
import datetime as dt
import json
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.join(HERE, "..")
GUID = "80bf9325-d299-4e24-ac74-c087aeec3011"
TARGET_ABI = "10.11.0.0"
NAME = "Picture shrinker"
OWNER = "TheMightyTRexUK"
DESCRIPTION = "Shrinks the posters and photos of people Jellyfin keeps, so they take less space."
OVERVIEW = ("Posters to 900 pixels tall and photos of cast and crew to 400 pixels wide, on the schedule you choose. "
            "Each original is backed up and each smaller copy checked before it replaces the original.")


def changelog(version):
    text = open(os.path.join(ROOT, "CHANGELOG.md"), encoding="utf-8").read()
    m = re.search(rf"^## {re.escape(version)}\b.*?$(.*?)(?=^## |\Z)", text, re.M | re.S)
    return (m.group(1).strip() if m else "").replace("**", "")


def meta(version):
    return {"category": "General", "changelog": changelog(version), "description": DESCRIPTION, "guid": GUID,
            "name": NAME, "overview": OVERVIEW, "owner": OWNER, "targetAbi": TARGET_ABI,
            "timestamp": dt.datetime.now(dt.timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ"), "version": version}


def add(version, url, checksum):
    path = os.path.join(ROOT, "manifest.json")
    doc = json.load(open(path, encoding="utf-8"))
    plugin = next(p for p in doc if p["guid"] == GUID)
    plugin["versions"] = [v for v in plugin["versions"] if v["version"] != version]
    plugin["versions"].insert(0, {"version": version, "changelog": changelog(version), "targetAbi": TARGET_ABI,
                                  "sourceUrl": url, "checksum": checksum,
                                  "timestamp": dt.datetime.now(dt.timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")})
    with open(path, "w", encoding="utf-8") as f:
        json.dump(doc, f, indent=2, ensure_ascii=False)
        f.write("\n")


if __name__ == "__main__":
    if sys.argv[1] == "meta":
        print(json.dumps(meta(sys.argv[2]), indent=2, ensure_ascii=False))
    elif sys.argv[1] == "add":
        add(*sys.argv[2:5])
    else:
        sys.exit(__doc__)
