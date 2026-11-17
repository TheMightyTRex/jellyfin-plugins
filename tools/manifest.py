#!/usr/bin/env python3
"""manifest.json, the one Jellyfin plug-in repository for every plug-in here, and meta.json in each zip.

  python3 tools/manifest.py plugins                          prints the plug-in folders as a JSON list
  python3 tools/manifest.py PLUGIN meta VERSION              prints meta.json for the zip
  python3 tools/manifest.py PLUGIN add VERSION ZIP_URL MD5   adds VERSION to that plug-in's entry (newest first) and
                                                             brings its name, words and picture up to date from
                                                             build.yaml; every other plug-in's entry stays as it is
  python3 tools/manifest.py PLUGIN target VERSION            prints the Jellyfin the version is built for (10.11 or 12)
  python3 tools/manifest.py PLUGIN pair [VERSION]            prints the two versions of a release, 10.11 first:
                                                             VERSION is the Jellyfin 12 one (odd third number);
                                                             without it, the newest in the plug-in's CHANGELOG.md
  python3 tools/manifest.py PLUGIN changelog VERSION         prints the notes of VERSION from its CHANGELOG.md
  python3 tools/manifest.py PLUGIN project                   prints the project folder, such as
                                                             Jellyfin.Plugin.PictureShrinker
  python3 tools/manifest.py check                            checks manifest.json: one entry per plug-in folder at
                                                             most, each with its own imageUrl (its folder's
                                                             images/logo.png), each version with an address and a
                                                             checksum

PLUGIN is the plug-in's folder, such as picture-shrinker. Its build.yaml (Jellyfin's jprm format) gives its name,
guid, owner, category, overview (the short line in Jellyfin's catalogue), description (the longer text) and its DLL.
The changelog of a version is the section in the plug-in's CHANGELOG.md whose heading names it.

Two builds from one source: an even third number is the Jellyfin 10.11 build (targetAbi 10.11.0.0), an odd one the
Jellyfin 12 build (targetAbi 12.0.0.0). They are released together, the 12 build one higher, so Jellyfin 12 (which
lists every version whose targetAbi is not above its own) always picks the 12 build, and Jellyfin 10.11 never sees it.
"""
import datetime as dt
import json
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.normpath(os.path.join(HERE, ".."))
MANIFEST = os.path.join(ROOT, "manifest.json")
RAW = "https://raw.githubusercontent.com/TheMightyTRex/jellyfin-plugins/main"
TARGETS = {"10.11": "10.11.0.0", "12": "12.0.0.0"}


def plugins():
    """Every folder with a build.yaml is a plug-in."""
    return sorted(d for d in os.listdir(ROOT) if os.path.isfile(os.path.join(ROOT, d, "build.yaml")))


def build_yaml(plugin):
    """The few parts of build.yaml used here: plain and quoted values, folded (>) blocks and lists of strings."""
    path = os.path.join(ROOT, plugin, "build.yaml")
    if not os.path.isfile(path):
        raise SystemExit(f"{plugin}: no build.yaml, so it is not a plug-in folder")
    out, key, block = {}, None, None
    for line in open(path, encoding="utf-8").read().splitlines():
        if not line.strip() or line.lstrip().startswith("#"):
            if block is not None and not line.strip():
                block.append("")
            continue
        if line.startswith((" ", "\t")) and key:
            item = re.match(r"\s+-\s+(.*)$", line)
            if item and block is None:
                out.setdefault(key, []).append(item.group(1).strip().strip('"\''))
            elif block is not None:
                block.append(line.strip())
            continue
        m = re.match(r"([\w-]+):\s*(.*)$", line)
        if not m:
            continue
        if block is not None:
            out[key] = " ".join(x for x in block if x).strip()
        key, value, block = m.group(1), m.group(2).strip(), None
        if value in (">", ">-", "|", "|-"):
            block = []
        elif value:
            out[key] = value.strip('"\'')
    if block is not None:
        out[key] = " ".join(x for x in block if x).strip()
    for need in ("name", "guid", "owner", "overview", "description", "category", "artifacts"):
        if not out.get(need):
            raise SystemExit(f"{plugin}/build.yaml has no {need}")
    return out


def project(plugin):
    """The project folder, named by the plug-in's DLL."""
    return re.sub(r"\.dll$", "", build_yaml(plugin)["artifacts"][0])


def target(version):
    """10.11 for an even third number, 12 for an odd one."""
    parts = [int(x) for x in version.split(".")]
    if len(parts) != 4 or parts[3] != 0:
        raise SystemExit(f"{version}: a version has four numbers and the fourth is 0, such as 1.1.1.0")
    return "12" if parts[2] % 2 else "10.11"


def _changelog_text(plugin):
    return open(os.path.join(ROOT, plugin, "CHANGELOG.md"), encoding="utf-8").read()


def pair(plugin, version=None):
    """The 10.11 and 12 versions of one release, from the 12 version or from the newest heading in CHANGELOG.md."""
    if version is None:
        m = re.search(r"^## .*?\b(\d+\.\d+\.\d*[13579]\.0)\b", _changelog_text(plugin), re.M)
        if not m:
            raise SystemExit(f"{plugin}/CHANGELOG.md has no heading with a Jellyfin 12 version (odd third number)")
        version = m.group(1)
    if target(version) != "12":
        raise SystemExit(f"{version}: a release is named by its Jellyfin 12 version, with an odd third number")
    a, b, c, d = version.split(".")
    return f"{a}.{b}.{int(c) - 1}.{d}", version


# The rules version and the Starboard plug-in that shares it stay in SYNC.md and CHANGELOG.md, not in Jellyfin's notes
# (owner, 17 November 2026): a sentence such as "Rules 1.2, the same as the Starboard plug-in 1.2.0." is left out.
RULES_NOTES = [
    re.compile(r",? with rules \d+\.\d+ \(the same as the Starboard plug-in [\d.]*\d\)"),
    re.compile(r"\bRules \d+\.\d+\b[^\n]*?(?:\n(?!\n)[^\n]*?)*?Starboard plug-in[\s\S]*?\.(?=\s+[A-Z]|\s*$)[ \t]*\n?"),
]


def notice(plugin):
    """(from version, text) of the plug-in's jellyfin-notice.json, or None: a warning Jellyfin shows at the top of the
    release notes of that version and every later one, above a line."""
    path = os.path.join(ROOT, plugin, "jellyfin-notice.json")
    if not os.path.isfile(path):
        return None
    with open(path, encoding="utf-8") as f:
        data = json.load(f)
    return data["from"], data["text"].strip()


def _ver(v):
    return tuple(int(x) for x in str(v).split("."))


def with_notice(plugin, version, notes):
    """The notes with the plug-in's notice under the Jellyfin heading and a line before the release notes, when the
    version is at or after the notice's first version. Running it again changes nothing."""
    n = notice(plugin)
    if not n or _ver(version) < _ver(n[0]) or n[1] in notes:
        return notes
    m = re.match(r"(### For Jellyfin [\d.]+)\s*", notes)
    head, rest = (m.group(1), notes[m.end():]) if m else ("", notes)
    # A blank line before the --- matters: straight after a paragraph, Markdown reads --- as an underlined heading.
    return "\n\n".join(x for x in (head, n[1], "---", rest.strip()) if x)


def jellyfin_notes(version, body):
    """The text Jellyfin shows for one version: a heading naming the Jellyfin the build is for (Jellyfin shows the
    changelog as Markdown, so it reads larger), then the notes without the rules sentence."""
    body = body.strip()
    body = re.sub(r"\A### For Jellyfin [\d.]+\s*", "", body)
    for pattern in RULES_NOTES:
        body = pattern.sub("", body)
    body = re.sub(r"[ \t]+\n", "\n", body)
    body = re.sub(r"\n{3,}", "\n\n", body).strip()
    return f"### For Jellyfin {target(version)}\n\n{body}".strip()


def changelog(plugin, version):
    text = _changelog_text(plugin)
    m = re.search(rf"^## [^\n]*?(?<![\d.]){re.escape(version)}(?![\d.])[^\n]*$(.*?)(?=^## |\Z)", text, re.M | re.S)
    body = m.group(1) if m else ""
    if not body.strip():
        return ""
    # "### Added" reads "Added:", so the only heading is the Jellyfin one, and the link references at the end are
    # left out.
    body = re.sub(r"^### (\w+)[ \t]*$", r"\1:", body, flags=re.M)
    body = re.sub(r"^\[[^\]]+\]: \S+[ \t]*$", "", body, flags=re.M)
    return with_notice(plugin, version, jellyfin_notes(version, body.replace("**", "")))


def _now():
    return dt.datetime.now(dt.timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")


def meta(plugin, version):
    y = build_yaml(plugin)
    return {"category": y["category"], "changelog": changelog(plugin, version), "description": y["description"],
            "guid": y["guid"], "name": y["name"], "overview": y["overview"], "owner": y["owner"],
            "targetAbi": TARGETS[target(version)], "timestamp": _now(), "version": version}


def entry(plugin):
    """The plug-in's entry in manifest.json, without its versions."""
    y = build_yaml(plugin)
    out = {"guid": y["guid"], "name": y["name"], "overview": y["overview"], "description": y["description"],
           "owner": y["owner"], "category": y["category"]}
    # Each plug-in has its own picture in Jellyfin's catalogue: its own images/logo.png, never a shared one.
    if not os.path.isfile(os.path.join(ROOT, plugin, "images", "logo.png")):
        raise SystemExit(f"{plugin}: no images/logo.png; each plug-in folder has its own logo and the tool that draws it")
    out["imageUrl"] = f"{RAW}/{plugin}/images/logo.png"
    return out


def _load():
    return json.load(open(MANIFEST, encoding="utf-8")) if os.path.isfile(MANIFEST) else []


def _save(doc):
    with open(MANIFEST, "w", encoding="utf-8") as f:
        json.dump(doc, f, indent=2, ensure_ascii=False)
        f.write("\n")


def _key(v):
    return tuple(int(x) for x in v["version"].split("."))


def add(plugin, version, url, checksum):
    """Adds one version to one plug-in's entry, creating the entry the first time. Other entries are not touched."""
    doc, fresh = _load(), entry(plugin)
    plugin_entry = next((p for p in doc if p.get("guid", "").lower() == fresh["guid"].lower()), None)
    if plugin_entry is None:
        plugin_entry = dict(fresh, versions=[])
        doc.append(plugin_entry)
    versions = [v for v in plugin_entry.get("versions", []) if v["version"] != version]
    versions.append({"version": version, "changelog": changelog(plugin, version), "targetAbi": TARGETS[target(version)],
                     "sourceUrl": url, "checksum": checksum, "timestamp": _now()})
    plugin_entry.clear()
    plugin_entry.update(fresh)
    plugin_entry["versions"] = sorted(versions, key=_key, reverse=True)
    doc.sort(key=lambda p: p["name"].lower())
    _save(doc)


def check():
    doc, problems = _load(), []
    guids = {build_yaml(p)["guid"].lower(): p for p in plugins()}
    seen = set()
    for p in doc:
        g = str(p.get("guid", "")).lower()
        if g in seen:
            problems.append(f"{p.get('name')}: listed twice")
        seen.add(g)
        if g not in guids:
            problems.append(f"{p.get('name')}: no plug-in folder has its guid {g}")
        elif p.get("imageUrl") != f"{RAW}/{guids[g]}/images/logo.png":
            problems.append(f"{p.get('name')}: imageUrl is not its own logo, {RAW}/{guids[g]}/images/logo.png")
        elif not os.path.isfile(os.path.join(ROOT, guids[g], "images", "logo.png")):
            problems.append(f"{p.get('name')}: no {guids[g]}/images/logo.png")
        if g in guids and notice(guids[g]):
            first, text = notice(guids[g])
            for v in p.get("versions", []):
                if _ver(v["version"]) >= _ver(first) and not str(v.get("changelog", "")).startswith(
                        f"### For Jellyfin {target(v['version'])}\n\n{text}\n\n---\n\n"):
                    problems.append(f"{p.get('name')} {v['version']}: the notes do not start with its notice and a line")
        for v in p.get("versions", []):
            for need in ("version", "targetAbi", "sourceUrl", "checksum"):
                if not v.get(need):
                    problems.append(f"{p.get('name')} {v.get('version')}: no {need}")
            if v.get("targetAbi") and v.get("changelog") is not None and not str(v.get("changelog")).startswith(
                    f"### For Jellyfin {target(v['version'])}\n"):
                problems.append(f"{p.get('name')} {v.get('version')}: the changelog does not start with "
                                f"'### For Jellyfin {target(v['version'])}'")
            if "Starboard plug-in" in str(v.get("changelog")) and re.search(r"\b[Rr]ules \d", str(v.get("changelog"))):
                problems.append(f"{p.get('name')} {v.get('version')}: the changelog names the rules version")
            if v.get("checksum") and not re.fullmatch(r"[0-9a-f]{32}", v["checksum"]):
                problems.append(f"{p.get('name')} {v.get('version')}: the checksum is not an MD5")
    for line in problems:
        print(line)
    print("manifest.json is in order" if not problems else f"{len(problems)} problems in manifest.json")
    return 1 if problems else 0


def main(argv):
    if not argv:
        sys.exit(__doc__)
    if argv[0] == "plugins":
        print(json.dumps(plugins()))
        return 0
    if argv[0] == "check":
        return check()
    if len(argv) < 2:
        sys.exit(__doc__)
    plugin, cmd, rest = argv[0], argv[1], argv[2:]
    if cmd == "meta":
        print(json.dumps(meta(plugin, rest[0]), indent=2, ensure_ascii=False))
    elif cmd == "add":
        add(plugin, *rest[:3])
    elif cmd == "target":
        print(target(rest[0]))
    elif cmd == "pair":
        print(*pair(plugin, rest[0] if rest else None))
    elif cmd == "changelog":
        print(changelog(plugin, rest[0]))
    elif cmd == "project":
        print(project(plugin))
    else:
        sys.exit(__doc__)
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
