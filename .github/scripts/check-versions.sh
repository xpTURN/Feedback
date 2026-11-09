#!/usr/bin/env bash
#
# check-versions.sh — verify the package version is consistent across the manifests.
#
# Sources of truth compared here:
#   com.xpturn.feedback/package.json        (UPM package version — the one the tag must match)
#   com.xpturn.feedback/CHANGELOG.md        (must have a "## [<version>]" heading, or "## [Unreleased]"
#                                            while nothing has been released)
#   com.xpturn.feedback/Skills~/.claude-plugin/plugin.json and
#   com.xpturn.feedback/Skills~/skills/xpturn-feedback/SKILL.md (metadata.version) — the agent skill's version
#   the git URL install line (…?path=/com.xpturn.feedback#v<version>) in the package README, the
#   repository's README.md and its translations
#
# Usage:
#   .github/scripts/check-versions.sh              # manifests only
#   .github/scripts/check-versions.sh --tag v0.1.0 # also require the tag to match
#
# Exit code: non-zero on any mismatch.
set -uo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
TAG=""

while [[ $# -gt 0 ]]; do
  case "$1" in
    --tag) TAG="${2:-}"; shift 2 ;;
    -h|--help) sed -n '2,/^set /p' "${BASH_SOURCE[0]}"; exit 0 ;;
    *) echo "Unknown option: $1" >&2; exit 2 ;;
  esac
done

PACKAGE="${REPO_ROOT}/com.xpturn.feedback"
PKG_JSON="${PACKAGE}/package.json"
CHANGELOG="${PACKAGE}/CHANGELOG.md"
PLUGIN_JSON="${PACKAGE}/Skills~/.claude-plugin/plugin.json"
SKILL_MD="${PACKAGE}/Skills~/skills/xpturn-feedback/SKILL.md"
[[ -f "${PKG_JSON}" ]] || { echo "package.json not found: ${PKG_JSON}" >&2; exit 1; }
[[ -f "${CHANGELOG}" ]] || { echo "CHANGELOG.md not found: ${CHANGELOG}" >&2; exit 1; }

# The manifest has to be valid JSON with the three fields UPM requires.
read -r NAME VERSION UNITY < <(python3 - "${PKG_JSON}" <<'PY'
import json, sys
m = json.load(open(sys.argv[1], encoding="utf-8"))
missing = [k for k in ("name", "version", "unity") if not m.get(k)]
if missing:
    print("MISSING " + ",".join(missing) + " -"); sys.exit(0)
print(m["name"], m["version"], m["unity"])
PY
) || { echo "package.json is not valid JSON" >&2; exit 1; }
if [[ "${NAME}" == "MISSING" ]]; then
  echo "package.json lacks required field(s): ${VERSION}" >&2
  exit 1
fi
echo "package.json: ${NAME} ${VERSION} (unity ${UNITY})"

FAILED=0

if ! [[ "${VERSION}" =~ ^[0-9]+\.[0-9]+\.[0-9]+(-[0-9A-Za-z.-]+)?$ ]]; then
  echo "  version '${VERSION}' is not a semantic version (MAJOR.MINOR.PATCH[-pre])" >&2
  FAILED=1
fi

# The CHANGELOG must carry the version — or still be unreleased.
if grep -qE "^## \[${VERSION//./\\.}\]" "${CHANGELOG}"; then
  echo "  CHANGELOG.md: heading [${VERSION}] found"
elif grep -qE '^## \[Unreleased\]' "${CHANGELOG}"; then
  echo "  CHANGELOG.md: [Unreleased] (nothing released yet)"
else
  echo "  CHANGELOG.md has neither a [${VERSION}] heading nor [Unreleased]" >&2
  FAILED=1
fi

# The agent skill carries the package's version twice: its plugin manifest and its front matter.
SKILL_PROBLEMS="$(python3 - "${VERSION}" "${PLUGIN_JSON}" "${SKILL_MD}" <<'PY'
import json, re, sys
version, plugin, skill = sys.argv[1:]
problems = []
try:
    v = json.load(open(plugin, encoding="utf-8")).get("version")
    if v != version:
        problems.append(f"Skills~/.claude-plugin/plugin.json: version '{v}'")
except (OSError, ValueError) as e:
    problems.append(f"Skills~/.claude-plugin/plugin.json: {e}")
try:
    text = open(skill, encoding="utf-8").read()
    head = text.split("---", 2)[1] if text.startswith("---") else ""
    m = re.search(r'^  version: "?([^"\n]+)"?$', head, re.M)
    if not m or m.group(1) != version:
        problems.append(f"Skills~/skills/xpturn-feedback/SKILL.md: metadata.version '{m.group(1) if m else None}'")
except OSError as e:
    problems.append(f"Skills~/skills/xpturn-feedback/SKILL.md: {e}")
print("\n".join(problems))
PY
)"
if [[ -n "${SKILL_PROBLEMS}" ]]; then
  echo "${SKILL_PROBLEMS}" | sed "s/^/  /;s/$/ — the package is ${VERSION}/" >&2
  FAILED=1
else
  echo "  agent skill: plugin.json and SKILL.md carry ${VERSION}"
fi

# The git URL install line pins the version by its tag, in the package README and the repository's READMEs.
README_PROBLEMS="$(python3 - "${VERSION}" "${PACKAGE}/README.md" "${REPO_ROOT}"/README*.md <<'PY'
import os, re, sys
version, readmes = sys.argv[1], sys.argv[2:]
problems = []
for path in readmes:
    name = os.path.relpath(path, os.path.dirname(os.path.dirname(readmes[0])))
    tags = re.findall(r"\?path=/com\.xpturn\.feedback#(v[0-9A-Za-z.+-]+)", open(path, encoding="utf-8").read())
    if not tags:
        problems.append(f"{name}: no git URL install line")
    for t in tags:
        if t != "v" + version:
            problems.append(f"{name}: install line pins {t}")
print("\n".join(problems))
PY
)"
if [[ -n "${README_PROBLEMS}" ]]; then
  echo "${README_PROBLEMS}" | sed "s/^/  /;s/$/ — expected v${VERSION}/" >&2
  FAILED=1
else
  echo "  README install lines pin v${VERSION}"
fi

if [[ -n "${TAG}" ]]; then
  if [[ "${TAG}" == "v${VERSION}" ]]; then
    echo "  tag ${TAG} matches package.json"
    if ! grep -qE "^## \[${VERSION//./\\.}\]" "${CHANGELOG}"; then
      echo "  a release tag needs a [${VERSION}] heading in CHANGELOG.md (not just [Unreleased])" >&2
      FAILED=1
    fi
  else
    echo "  tag ${TAG} does not match package.json version ${VERSION} (expected v${VERSION})" >&2
    FAILED=1
  fi
fi

if [[ ${FAILED} -ne 0 ]]; then
  echo "Version check failed." >&2
  exit 1
fi
echo "Version manifests agree."
