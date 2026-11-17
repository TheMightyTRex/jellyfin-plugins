#!/usr/bin/env python3
"""manifest.json, the Jellyfin plug-in repository, and meta.json in each zip.

  python3 tools/manifest.py meta VERSION                   prints meta.json for the zip
  python3 tools/manifest.py add VERSION ZIP_URL CHECKSUM   adds VERSION to manifest.json (newest first)
  python3 tools/manifest.py target VERSION                 prints the Jellyfin the version is built for (10.11 or 12)
  python3 tools/manifest.py pair [VERSION]                 prints the two versions of a release, 10.11 first:
                                                           VERSION is the Jellyfin 12 one (odd third number); without
                                                           it, the newest in CHANGELOG.md
The changelog of a version is the section in CHANGELOG.md whose heading names it.

Two builds from one source (SYNC.md): an even third number is the Jellyfin 10.11 build (targetAbi 10.11.0.0), an odd
one the Jellyfin 12 build (targetAbi 12.0.0.0). They are released together, the 12 build one higher, so Jellyfin 12
(which lists every version whose targetAbi is not above its own) always picks the 12 build, and Jellyfin 10.11 never
sees it.
"""
import datetime as dt
import json
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.join(HERE, "..")
GUID = "80bf9325-d299-4e24-ac74-c087aeec3011"
TARGETS = {"10.11": "10.11.0.0", "12": "12.0.0.0"}
NAME = "Picture shrinker"
OWNER = "TheMightyTRexUK"
DESCRIPTION = "Shrinks the posters, backdrops, logos, photos of people and other pictures Jellyfin keeps, so they take less space."
OVERVIEW = ("Posters to 900 pixels tall, backdrops to 1,920 wide, photos of cast and crew to 400 wide and the other "
            "picture types to their own sizes, on the schedule you choose. "
            "Each smaller copy is checked before it replaces the original, and you choose whether the originals are backed up first.")


def target(version):
    """10.11 for an even third number, 12 for an odd one."""
    parts = [int(x) for x in version.split(".")]
    if len(parts) != 4 or parts[3] != 0:
        raise SystemExit(f"{version}: a version has four numbers and the fourth is 0, such as 1.1.1.0")
    return "12" if parts[2] % 2 else "10.11"


def pair(version=None):
    """The 10.11 and 12 versions of one release, from the 12 version or from the newest heading in CHANGELOG.md."""
    if version is None:
        text = open(os.path.join(ROOT, "CHANGELOG.md"), encoding="utf-8").read()
        m = re.search(r"^## .*?\b(\d+\.\d+\.\d*[13579]\.0)\b", text, re.M)
        if not m:
            raise SystemExit("CHANGELOG.md has no heading with a Jellyfin 12 version (odd third number)")
        version = m.group(1)
    if target(version) != "12":
        raise SystemExit(f"{version}: a release is named by its Jellyfin 12 version, with an odd third number")
    a, b, c, d = version.split(".")
    return f"{a}.{b}.{int(c) - 1}.{d}", version


def changelog(version):
    text = open(os.path.join(ROOT, "CHANGELOG.md"), encoding="utf-8").read()
    m = re.search(rf"^## [^\n]*?(?<![\d.]){re.escape(version)}(?![\d.])[^\n]*$(.*?)(?=^## |\Z)", text, re.M | re.S)
    return (m.group(1).strip() if m else "").replace("**", "")


def meta(version):
    return {"category": "General", "changelog": changelog(version), "description": DESCRIPTION, "guid": GUID,
            "name": NAME, "overview": OVERVIEW, "owner": OWNER, "targetAbi": TARGETS[target(version)],
            "timestamp": dt.datetime.now(dt.timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ"), "version": version}


def add(version, url, checksum):
    path = os.path.join(ROOT, "manifest.json")
    doc = json.load(open(path, encoding="utf-8"))
    plugin = next(p for p in doc if p["guid"] == GUID)
    plugin["versions"] = [v for v in plugin["versions"] if v["version"] != version]
    plugin["versions"].append({"version": version, "changelog": changelog(version),
                               "targetAbi": TARGETS[target(version)], "sourceUrl": url, "checksum": checksum,
                               "timestamp": dt.datetime.now(dt.timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")})
    plugin["versions"].sort(key=lambda v: tuple(int(x) for x in v["version"].split(".")), reverse=True)
    with open(path, "w", encoding="utf-8") as f:
        json.dump(doc, f, indent=2, ensure_ascii=False)
        f.write("\n")


if __name__ == "__main__":
    if sys.argv[1] == "meta":
        print(json.dumps(meta(sys.argv[2]), indent=2, ensure_ascii=False))
    elif sys.argv[1] == "add":
        add(*sys.argv[2:5])
    elif sys.argv[1] == "target":
        print(target(sys.argv[2]))
    elif sys.argv[1] == "pair":
        print(*pair(sys.argv[2] if len(sys.argv) > 2 else None))
    else:
        sys.exit(__doc__)
