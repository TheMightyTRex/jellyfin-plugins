#!/usr/bin/env python3
"""Checks the help centre (docs/ and help-centre.json) before it is built. Exits 1 on a problem. Adapted from
Starboard's tools/check-help.py (17 November 2026) for the Jellyfin plug-ins.

- Every article starts with its trail line, "[Help centre](../README.md) · <group> · <kind>", then its
  "<!-- help: <area> / <group> -->" line, whose group is in help-centre.json; it has one title and "Find out more".
- Every article is listed in the help home, docs/README.md.
- Every link to another file goes somewhere, and every link to a heading (#its-id) reaches one.
- A link names where it goes: never "here", "click here" or "read more".
- Every troubleshooting article starts with a flow chart (a flow block).
- Every screenshot exists, has a text description, and is named <what>-en.png; every how-to has at least 3 marked-up
  screenshots (SCREENSHOTS.md section 1, the owner's minimum for tier one articles); every picture in docs/images is
  used, so none is left behind.
- Until Audio description is in manifest.json, every one of its pages says it is not released yet (the line under
  NOT_RELEASED); once it is released, the check fails until that line is taken out of each page.
- The words: tools/check_words.py.

    python3 tools/check_help.py
"""
import json
import pathlib
import re
import subprocess
import sys

ROOT = pathlib.Path(__file__).resolve().parent.parent
HELP = ROOT / "docs"
SCREENSHOTS_PER_HOW_TO = 3
KINDS = ("how-to", "reference", "troubleshooting")
NOT_RELEASED = "<!-- not-released: audio-description -->"
LINK = re.compile(r"(!?)\[([^\]]*)\]\(([^)\s]+)\)")


def slug(text):
    """A heading's id, as GitHub and the help centre builder make it."""
    text = re.sub(r"<[^>]+>|[*`]", "", text).strip().lower()
    text = re.sub(r"\[([^\]]*)\]\([^)]*\)", r"\1", text)
    return re.sub(r"[^\w\- ]", "", text).replace(" ", "-")


def headings(md):
    ids = set()
    for line in re.sub(r"```.*?```", "", md, flags=re.S).splitlines():
        m = re.match(r"^#{1,6} (.+?)\s*$", line)
        if m:
            ids.add(slug(m.group(1)))
    # A flow chart's numbered answer can name its heading's id: "1: Do this #its-id".
    return ids


def released(plugin_name):
    manifest = json.loads((ROOT / "manifest.json").read_text(encoding="utf-8"))
    return any(p.get("name") == plugin_name for p in manifest)


def main():
    problems = []
    config = json.loads((ROOT / "help-centre.json").read_text(encoding="utf-8"))
    groups = {(a["key"], g["key"]) for a in config.get("areas", []) for g in a.get("groups", [])}
    files = sorted(HELP.rglob("*.md"))
    index = (HELP / "README.md").read_text(encoding="utf-8")
    used_images = set()
    ad_released = released("Audio description")
    for path in files:
        rel = path.relative_to(HELP).as_posix()
        md = path.read_text(encoding="utf-8")
        prose = re.sub(r"```.*?```", "", md, flags=re.S)
        for bang, text, target in LINK.findall(prose):
            if re.match(r"^[a-z]+:", target):
                continue
            file, _, anchor = target.partition("#")
            dest = (path.parent / file).resolve() if file else path
            if not dest.exists():
                problems.append(f"{rel}: the link to {target} goes nowhere")
                continue
            if bang:
                used_images.add(dest)
                if not text.strip():
                    problems.append(f"{rel}: the picture {file} has no text description")
                if not re.fullmatch(r"[a-z0-9-]+-en\.png", dest.name):
                    problems.append(f"{rel}: {dest.name} is not named <what it shows>-en.png")
            elif re.fullmatch(r"\s*(here|click here|read more|learn more|this page|link)\s*", text, re.I):
                problems.append(f"{rel}: the link text '{text}' does not name where it goes")
            if anchor and dest.suffix == ".md" and anchor not in headings(dest.read_text(encoding="utf-8")):
                problems.append(f"{rel}: the link to {target} reaches no heading")
        if path.name == "README.md" and path.parent == HELP:
            continue
        lines = md.splitlines()
        first = lines[0] if lines else ""
        if not first.startswith("[Help centre](../README.md) · "):
            problems.append(f"{rel}: does not start with the Help centre line")
        kind = first.rsplit("·", 1)[-1].strip()
        if kind not in KINDS:
            problems.append(f"{rel}: the first line does not end with the kind ({', '.join(KINDS)})")
        m = re.match(r"<!--\s*help:\s*([\w-]+)\s*/\s*([\w-]+)\s*-->", lines[1] if len(lines) > 1 else "")
        if not m:
            problems.append(f"{rel}: has no <!-- help: <area> / <group> --> line under its first line")
        elif (m.group(1), m.group(2)) not in groups:
            problems.append(f"{rel}: {m.group(1)} / {m.group(2)} is not a group in help-centre.json")
        if len(re.findall(r"^# ", prose, flags=re.M)) != 1:
            problems.append(f"{rel}: needs exactly one title")
        if "## Find out more" not in md:
            problems.append(f"{rel}: has no 'Find out more' section")
        if f"]({rel})" not in index:
            problems.append(f"{rel}: not listed in the help home, docs/README.md")
        if kind == "troubleshooting":
            first_block = re.search(r"^```(\w*)", md, flags=re.M)
            if not first_block or first_block.group(1) != "flow":
                problems.append(f"{rel}: a troubleshooting article starts with a flow chart (a flow block)")
        if kind == "how-to":
            count = len(re.findall(r"!\[[^\]]+\]\([^)]*images/", prose))
            if count < SCREENSHOTS_PER_HOW_TO:
                problems.append(f"{rel}: {count} screenshots; a how-to has at least {SCREENSHOTS_PER_HOW_TO}")
        if re.search(r"still to be written|\bto be written\b|\bTODO\b|\bTBD\b|\bTBC\b|lorem ipsum|\[needs checking", prose, re.I):
            problems.append(f"{rel}: still has a placeholder")
        is_ad = "audio-description" in rel or path.name.startswith("audio-description")
        if is_ad and not ad_released and NOT_RELEASED not in md:
            problems.append(f"{rel}: Audio description is not in manifest.json yet, so the page says so ({NOT_RELEASED} and the line under it)")
        if NOT_RELEASED in md and ad_released:
            problems.append(f"{rel}: Audio description is released: take out {NOT_RELEASED} and the line under it")
    for target in re.findall(r"\]\(([^)#\s]+\.md)\)", index):
        if not (HELP / target).exists():
            problems.append(f"README.md: the help home lists {target}, which does not exist")
    for img in sorted((HELP / "images").glob("*.png")):
        if img.resolve() not in used_images:
            problems.append(f"images/{img.name}: not used by any article")
    for p in problems:
        print(p)
    print(f"{len(files)} articles checked, {len(problems)} problems")
    words = subprocess.run([sys.executable, str(ROOT / "tools" / "check_words.py")])
    return 1 if problems or words.returncode else 0


if __name__ == "__main__":
    sys.exit(main())
