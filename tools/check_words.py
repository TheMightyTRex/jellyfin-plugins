"""Stops banned words coming into the help (docs/) and help-centre.json: the design system's CONTENT-DESIGN.md
sections 2 and 5 and HELP-WRITING.md section 9. Adapted from Starboard's tools/check_words.py (17 November 2026).

Code (fenced blocks other than flow charts, `inline code`, link targets and comments) is left out, and so is a
banned word quoted on its own ("click"), which names the word rather than using it. A genuine exception, such as
Jellyfin's own label quoted exactly, goes in tools/words-allowed.txt, one per line: the file, a tab, a regular
expression for the line, a tab, and the reason.

Run: python3 tools/check_words.py [files]. It prints each hit as path:line and exits 1 when there is one."""
import json
import pathlib
import re
import sys

root = pathlib.Path(__file__).resolve().parent.parent

# Each banned word, with the rule it breaks.
BANNED = [
    (r"see|sees|seen|seeing", "2.6 no see or look"),
    (r"look|looks|looked|looking", "2.6 no see or look"),
    (r"click\w*|tap|taps|tapped|tapping", "2.5 select, not click"),
    (r"enabl(e|es|ed|ing)|disabl(e|es|ed|ing)|toggl(e|es|ed|ing)|turn(s|ed|ing)? (on|off)|switch(es|ed)? (it |them )?(on|off)",
     "2.9 activate and deactivate"),
    (r"typed|typing|typ(e|es) (a|an|the|it|its|your|in|into|them|that|this|something|here)", "2.18 enter, not type"),
    (r"above|below", "2.7 no positional directions"),
    (r"top right|top left|bottom right|bottom left|on the left|on the right|scroll down|scroll up", "2.7 no positional directions"),
    (r"screen|screens", "2.8 page, not screen"),
    (r"please", "1 voice: no please"),
    (r"just|simply|simple|simplest|easy|easier|easily|straightforward|obviously|complex|difficult|hard|advanced",
     "2.15 no difficulty words"),
    (r"drive|drives", "5 disk, not drive"),
    (r"click here|read more|learn more", "4 link text names the destination"),
]
PATTERN = re.compile(r"(?<![\w-])(" + "|".join(f"(?:{p})" for p, _ in BANNED) + r")(?![\w-])", re.I)
PUNCTUATION = [
    (re.compile(r"—"), "2.1 no em dashes"),
    (re.compile(r"\b(can't|won't|don't|doesn't|isn't|aren't|didn't|wasn't|haven't|hasn't|shouldn't|wouldn't|couldn't|"
                r"you're|you'll|you've|we're|they're|it's|that's|there's|what's|let's|I'm)\b", re.I), "2.2 no contractions"),
    (re.compile(r"(?<!\[)!(?!\[)"), "2.10 no exclamation marks"),
]
QUOTED_ALONE = re.compile(r"[\"'“‘]\s*(" + "|".join(p for p, _ in BANNED) + r")\s*[\"'”’]", re.I)
# Words that are part of a name, not a use of a banned word: "screen reader" is the name of a tool.
NAMES = re.compile(r"screen readers?|screen magnifiers?|full[- ]screen|on-screen|types it shrinks", re.I)


def rule_for(word):
    for p, rule in BANNED:
        if re.fullmatch(p, word, re.I):
            return rule
    return ""


def markdown_lines(text):
    fenced = in_comment = False
    for n, line in enumerate(text.splitlines(), 1):
        if not in_comment and line.lstrip().startswith("```"):
            # A ```flow block is a chart people read, so its words are checked; other code blocks are not.
            fenced = False if fenced else ("code" if not line.lstrip().startswith("```flow") else "flow")
            continue
        if fenced == "code":
            continue
        if fenced == "flow":
            line = re.sub(r"#[\w-]+\s*$", "", line)  # an answer's heading id
            line = re.sub(r"->\s*\S+\s*$", "", line)  # an answer's target
        out, i = [], 0
        while i < len(line):  # HTML comments, also across lines
            if in_comment:
                end = line.find("-->", i)
                if end < 0:
                    i = len(line)
                    break
                i, in_comment = end + 3, False
            else:
                start = line.find("<!--", i)
                if start < 0:
                    out.append(line[i:])
                    break
                out.append(line[i:start])
                i, in_comment = start + 4, True
        line = "".join(out)
        line = re.sub(r"`[^`]*`", " ", line)
        line = re.sub(r"\]\([^)]*\)", "]", line)
        line = re.sub(r"https?://\S+", " ", line)
        yield n, line


def json_lines(path):
    """help-centre.json: every string people read, one to a line, with its place."""
    def walk(node, where):
        if isinstance(node, dict):
            for k, v in node.items():
                if k not in ("to", "key", "icon", "tone", "repo", "site", "help_dir", "logo_file", "favicon_file", "flow"):
                    yield from walk(v, f"{where}.{k}" if where else k)
                elif k == "flow":
                    yield from ((where + ".flow", re.sub(r"#[\w-]+|->\s*\S+", "", l)) for l in v.splitlines())
        elif isinstance(node, list):
            for i, v in enumerate(node):
                yield from walk(v, f"{where}[{i}]")
        elif isinstance(node, str):
            yield where, re.sub(r"\]\([^)]*\)|https?://\S+", " ", node)
    yield from walk(json.loads(path.read_text(encoding="utf-8")), "")


def default_files():
    return sorted((root / "docs").rglob("*.md")) + [root / "help-centre.json"]


def allowed():
    rules = []
    allow_file = root / "tools" / "words-allowed.txt"
    if not allow_file.exists():
        return rules
    for line in allow_file.read_text(encoding="utf-8").splitlines():
        if not line.strip() or line.startswith("#"):
            continue
        parts = line.split("\t")
        if len(parts) < 3 or not parts[2].strip():
            sys.exit(f"words-allowed.txt: each line needs a file, a pattern and a reason: {line}")
        rules.append((parts[0], re.compile(parts[1])))
    return rules


def main(argv):
    files = [pathlib.Path(a).resolve() for a in argv] or default_files()
    exceptions = allowed()
    hits = 0
    for path in files:
        rel = path.relative_to(root).as_posix()
        lines = json_lines(path) if path.suffix == ".json" else markdown_lines(path.read_text(encoding="utf-8"))
        for n, line in lines:
            text = NAMES.sub(" ", QUOTED_ALONE.sub(" ", line))
            found = [(m.group(1), rule_for(m.group(1))) for m in PATTERN.finditer(text)]
            found += [(m.group(0), rule) for pat, rule in PUNCTUATION for m in pat.finditer(text)]
            if not found or any(f == rel and p.search(line) for f, p in exceptions):
                continue
            hits += 1
            words = ", ".join(sorted({f'"{w}" ({r})' for w, r in found}))
            print(f"{rel}:{n}: {words}: {line.strip()[:140]}")
    if hits:
        print(f"\n{hits} lines with banned words or punctuation. Fix each one, or add a genuine exception, with its "
              "reason, to tools/words-allowed.txt.")
        return 1
    print(f"Banned words: none in {len(files)} files.")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
