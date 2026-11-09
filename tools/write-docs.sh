#!/usr/bin/env bash
#
# write-docs.sh — rewrites the generated regions of the package's documents (README.md, Documentation~/ and the agent
# skill under Skills~/) from their sources, in the dev project (tests/FeedbackTests.6000.3 reads the package from this
# repository), then the repository root's README and its translations' regions (tools/root-readme.py — no Unity, it also
# runs alone), then runs the documents' tests there.
#   - the dev project's Unity version only: another version would upgrade the dev project's settings on import
#   - not while a Unity editor has the dev project open — that editor is left alone; close it and run again
#   - afterwards the repository may differ from before only in the documents, the root README.md and README.<lang>.md files
#     and new .meta files; anything else fails
#   - the tests: the results file holds tests (not zero), none failed, no fixture failed outside its tests
#
# Usage:
#   tools/write-docs.sh                 # write, then test
#   tools/write-docs.sh --check         # test only
#   UNITY_EDITORS=/path tools/write-docs.sh
#   WRITE_DOCS_WORK=/path tools/write-docs.sh    # where the logs and the results go
#
# Exit code: 0 when the write (if any) kept to the documents and the tests passed, else 1.
set -uo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PKG="com.xpturn.feedback"
DEV_PROJECT="${REPO_ROOT}/tests/FeedbackTests.6000.3"
EDITORS="${UNITY_EDITORS:-/Applications/Unity/Hub/Editor}"
WORK="${WRITE_DOCS_WORK:-${TMPDIR:-/tmp}/feedback-write-docs}"
TIMEOUT="${WRITE_DOCS_TIMEOUT:-900}"
FILTER='xpTURN\.Feedback\.Tests\.Editor\.Docs\.'

case "${1:-}" in -h|--help) sed -n '2,/^set /p' "${BASH_SOURCE[0]}"; exit 0 ;; esac
CHECK=0
[[ "${1:-}" == "--check" ]] && CHECK=1
VERSION="$(sed -n 's/^m_EditorVersion: //p' "${DEV_PROJECT}/ProjectSettings/ProjectVersion.txt")"
UNITY="${EDITORS}/${VERSION}/Unity.app/Contents/MacOS/Unity"
mkdir -p "${WORK}"

if [[ ! -x "${UNITY}" ]]; then
  echo "FAIL the dev project's Unity ${VERSION} is not installed: ${UNITY}"; exit 1
fi
# a batch run on an open project ends at once with exit code 1 and no reason — say it before. The editor that has the
# project open holds Temp/UnityLockfile (a lock file left by a killed editor is held by nobody and does not block); the
# command line is the fallback for an editor that is still starting (a Hub editor's has projectPath or projectpath)
if lsof -t "${DEV_PROJECT}/Temp/UnityLockfile" >/dev/null 2>&1 || pgrep -if -- "-projectPath ${DEV_PROJECT}" >/dev/null; then
  echo "FAIL a Unity editor has ${DEV_PROJECT} open — close it and run again"; exit 1
fi

# <log> <args…> — one editor run at the outer limit
editor() {
  local log="$1"; shift
  perl -e 'alarm shift; exec @ARGV' "${TIMEOUT}" "${UNITY}" -batchmode -projectPath "${DEV_PROJECT}" "$@" -logFile "${log}"
}

# the repository's changed files with a digest of each — before and after the write
# porcelain paths start at the git top level, which may be above this folder (PREFIX is this folder from there)
GIT_TOP="$(git -C "${REPO_ROOT}" rev-parse --show-toplevel)"
PREFIX="$(git -C "${REPO_ROOT}" rev-parse --show-prefix)"
state() {
  git -C "${REPO_ROOT}" status --porcelain --untracked-files=all -- "${PKG}" tests/FeedbackTests.6000.3 README.md 'README.*.md' | while IFS= read -r line; do
    path="${line:3}"
    digest="$( [ -f "${GIT_TOP}/${path}" ] && shasum "${GIT_TOP}/${path}" | cut -c1-16 || echo gone)"
    echo "${line:0:2} ${digest} ${path}"
  done | sort
}

failed=0
if [[ ${CHECK} == 0 ]]; then
  state > "${WORK}/before.txt"
  editor "${WORK}/write.log" -executeMethod xpTURN.Feedback.Tests.Editor.Docs.DocsWriter.Write
  code=$?
  grep -h '^DOCS wrote' "${WORK}/write.log" | head -1 | sed 's/^/   /'
  if [[ ${code} != 0 ]]; then
    echo "   FAIL the write: editor exit ${code} — ${WORK}/write.log"; failed=1
  fi
  python3 "${REPO_ROOT}/tools/root-readme.py" > "${WORK}/root.txt" || { echo "   FAIL the root README"; failed=1; }
  cat "${WORK}/root.txt"
  state > "${WORK}/after.txt"
  python3 - "${WORK}/before.txt" "${WORK}/after.txt" "${PREFIX}${PKG}" "${PREFIX}" "${WORK}/root.txt" <<'EOF' || failed=1
import sys
before = open(sys.argv[1]).read().splitlines(); after = open(sys.argv[2]).read().splitlines(); pkg = sys.argv[3]; top = sys.argv[4]
if any(" gone " in l and "D" not in l[:2] for l in after):
    print("   FAIL a listed file is not where git says it is — the check would see nothing"); sys.exit(1)
# "XY digest path" — X or Y may be a space. A file the write put back as it is in git leaves the list: it moved too
path_of = lambda l: l[3:].split(" ", 1)[1]
now = {path_of(l) for l in after}
moved = [l for l in after if l not in before] + [l[:3] + "back-to-git " + path_of(l) for l in before if path_of(l) not in now]
# the root files the tool wrote are in the list — the check sees them
wrote = [l.split("wrote ", 1)[1].strip() for l in open(sys.argv[5]) if l.strip().startswith("wrote ")]
unseen = [w for w in wrote if f"{top}{w}" not in {path_of(l) for l in moved}]
if unseen:
    print(f"   FAIL the root README tool wrote {', '.join(unseen)}, which the check does not see"); sys.exit(1)
def allowed(line):
    status, path = line[:2], path_of(line)
    if path == f"{pkg}/README.md" or path.startswith(f"{pkg}/Documentation~/") or path.startswith(f"{pkg}/Skills~/"):
        return True
    if path == f"{top}README.md" or (path.startswith(f"{top}README.") and path.endswith(".md") and "/" not in path[len(top):]):
        return True
    return status == "??" and path.endswith(".meta") and "back-to-git" not in line
bad = [l for l in moved if not allowed(l)]
for l in moved:
    print(f"   {'ok ' if allowed(l) else 'BAD'} {l}")
if bad:
    print(f"   FAIL the write changed {len(bad)} file(s) outside the documents and new .meta files")
sys.exit(1 if bad else 0)
EOF
fi

editor "${WORK}/tests.log" -runTests -testPlatform EditMode -testFilter "${FILTER}" -testResults "${WORK}/docs.xml"
python3 - "${WORK}/docs.xml" <<'EOF' || failed=1
import os, re, sys, xml.etree.ElementTree as ET
f = sys.argv[1]
if not os.path.exists(f):
    print(f"   FAIL no test results ({f})"); sys.exit(1)
run = re.search(r"<test-run [^>]*", open(f).read()).group(0)
c = {k: int(re.search(fr'\b{k}="(\d+)"', run).group(1)) for k in ("total", "passed", "failed")}
suites = [f'{s.get("fullname")} @{s.get("site")}' for s in ET.parse(f).iter("test-suite") if s.get("result") == "Failed" and s.get("site") != "Child"]
for t in ET.parse(f).iter("test-case"):
    if t.get("result") != "Passed":
        print(f"   {t.get('name')}: {(t.findtext('failure/message') or '').strip().splitlines()[0][:300] if t.findtext('failure/message') else t.get('result')}")
ok = c["total"] > 0 and c["failed"] == 0 and not suites
print(f"   {'PASS' if ok else 'FAIL'} the documents' tests: {c['passed']}/{c['total']}" + (f" · suite failed {suites[:3]}" if suites else ""))
sys.exit(0 if ok else 1)
EOF
exit ${failed}
