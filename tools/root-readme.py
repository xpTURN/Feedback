#!/usr/bin/env python3
# root-readme.py — the repository root's README.md from the package's README (com.xpturn.feedback/README.md, the source):
# a generated-file note, the language line after the title (and the badges, if any), the links into the package with
# com.xpturn.feedback/ in front and the generated regions' markers taken out. The translations beside it
# (README.<lang>.md, written by hand) get their generated regions — code and counts — from the package's README, and a
# line with the English text's fingerprint (the prose without the generated regions, whitespace collapsed): a translation
# whose fingerprint is not the English one's is behind. No Unity — run it after tools/write-docs.sh, or alone.
#
# Usage:
#   tools/root-readme.py                       # write the root README and the translations' regions
#   tools/root-readme.py --check               # nothing written — exit 1 when a write would change a file
#   tools/root-readme.py --translations        # exit 1 when a translation is behind the English text
#   tools/root-readme.py --accept ko-KR ...    # a translation brought up to date: its fingerprint becomes the English one's
import hashlib, os, re, sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
PACKAGE = "com.xpturn.feedback"
LANGUAGES = [("ko-KR", "한국어"), ("ja-JP", "日本語"), ("zh-CN", "简体中文")]
NOTE = f"<!-- Generated from {PACKAGE}/README.md by tools/root-readme.py — edit that file, not this one -->"
REGION = re.compile(r"<!-- gen:(?P<key>[^\n]+?) -->(?P<nl>\n?)(?P<body>.*?)<!-- /gen:(?P=key) -->", re.S)
FINGERPRINT = re.compile(r"<!-- english: (?P<fp>[0-9a-f]{12}) -->")
LINK = re.compile(r"(\[[^\]]*\])\(([^)\s]+)\)")


def language_line(current=None):
    names = [("README.md", "English", None)] + [(f"README.{code}.md", name, code) for code, name in LANGUAGES]
    return " | ".join(f"**{name}**" if code == current else f"[{name}]({file})" for file, name, code in names)


def transform(text):
    lines = text.split("\n")
    if not lines[0].startswith("# "):
        raise SystemExit("the package README does not start with its title")
    at = 1
    while at < len(lines) and lines[at].startswith("[!["):
        at += 1
    lines[at:at] = ["", language_line()]

    def into_package(m):
        target = m.group(2)
        path = target.split("#")[0]
        if re.match(r"^(https?:|mailto:|#)", target) or not path or not os.path.exists(os.path.join(ROOT, PACKAGE, path)):
            return m.group(0)
        return f"{m.group(1)}({PACKAGE}/{target})"

    text = LINK.sub(into_package, "\n".join(lines))
    text = re.sub(r"^<!-- /?gen:[^>]*-->\n", "", text, flags=re.M)
    text = re.sub(r"<!-- /?gen:[^>]*-->", "", text)
    return NOTE + "\n" + text


def fingerprint(text):
    prose = REGION.sub("", text)
    return hashlib.sha256(re.sub(r"\s+", " ", prose).strip().encode()).hexdigest()[:12]


def translations(english):
    regions = {m.group("key"): m.group("body") for m in REGION.finditer(english)}
    out = {}
    for code, _ in LANGUAGES:
        path = os.path.join(ROOT, f"README.{code}.md")
        if not os.path.exists(path):
            raise SystemExit(f"no README.{code}.md")
        text = open(path, encoding="utf-8").read()
        missing = [m.group("key") for m in REGION.finditer(text) if m.group("key") not in regions]
        if missing:
            raise SystemExit(f"README.{code}.md: regions the package README does not have: {missing}")
        out[code] = (path, text, REGION.sub(lambda m: f"<!-- gen:{m.group('key')} -->{m.group('nl')}{regions[m.group('key')]}<!-- /gen:{m.group('key')} -->", text))
    return out


def main(args):
    english = open(os.path.join(ROOT, PACKAGE, "README.md"), encoding="utf-8").read()
    root_path = os.path.join(ROOT, "README.md")
    if "--translations" in args or "--accept" in args:
        want = fingerprint(english)
        accept = args[args.index("--accept") + 1:] if "--accept" in args else []
        behind = 0
        for code, (path, text, _) in translations(english).items():
            m = FINGERPRINT.search(text)
            if code in accept:
                text = FINGERPRINT.sub(f"<!-- english: {want} -->", text) if m else text
                open(path, "w", encoding="utf-8").write(text)
                print(f"   README.{code}.md: accepted {want}")
            elif not m or m.group("fp") != want:
                behind += 1
                print(f"   README.{code}.md: behind the English text ({m.group('fp') if m else 'no fingerprint'} — the English is {want})")
            else:
                print(f"   README.{code}.md: up to date")
        return 1 if behind and not accept else 0
    writes = [(root_path, open(root_path, encoding="utf-8").read() if os.path.exists(root_path) else None, transform(english))]
    writes += [(path, text, new) for path, text, new in translations(english).values()]
    changed = [(p, new) for p, old, new in writes if old != new]
    for p, _ in changed:
        print(f"   {'would write' if '--check' in args else 'wrote'} {os.path.relpath(p, ROOT)}")
    if "--check" in args:
        return 1 if changed else 0
    for p, new in changed:
        open(p, "w", encoding="utf-8").write(new)
    print(f"   root README: {len(changed)} file(s) written")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
