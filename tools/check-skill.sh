#!/usr/bin/env bash
#
# check-skill.sh — the agent skill's plugin (com.xpturn.feedback/Skills~) and the repository's marketplace
# (.claude-plugin/marketplace.json) as Claude Code reads them: the marketplace, the plugin and the plugin's skills pass
# Claude Code's strict validation (a warning fails too).
#   - each validation must say it read what it was given (the marketplace manifest, the plugin manifest, the skills) —
#     a validation of something else is a FAIL, not a PASS
#   - Claude Code's validation does not read a skill's front matter: the documents' tests check it (SkillTests)
#   - without the claude command the check is reported ENV and skipped — not a PASS
# With --eval, after the validations pass, the skill's behaviour evals (tools/skill-evals — claude plugin eval) run: each
# case's prompt in a fresh agent run against the fixture project, with the plugin and without it (the baseline arm).
#   - real model calls on your credential, so a cost: the run stops at the cost ceiling
#   - eval cases must sit under the plugin's root, so a plugin root is made in the work folder: the plugin's
#     .claude-plugin and skills (checked byte for byte against Skills~), the cases as evals/, the fixture beside them and
#     the package's copy in the fixture's Library/PackageCache (the folder its packages-lock.json names)
#   - the report stays local (--no-publish); the results JSON is <work>/results.json
#   - defaults: --model claude-sonnet-5-5 --judge-model haiku --threshold 0.8 --max-cost-usd 10; options after --eval
#     are passed to claude plugin eval after them (--runs 1 --ablation none --max-cost-usd 2 for a cheap first look,
#     --case <glob> for one case)
#
# Usage:
#   tools/check-skill.sh
#   tools/check-skill.sh --eval [claude plugin eval options…]
#   CLAUDE_BIN=/path/to/claude tools/check-skill.sh
#   SKILL_EVAL_WORK=/path tools/check-skill.sh --eval      # where the plugin root, the report and the results go
#
# Exit code: 1 if a validation fails or the evals fail (a case below the threshold, a case that does not load, the cost
# ceiling reached), 2 if Claude Code is not installed (ENV), else 0.
set -uo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PLUGIN="${REPO_ROOT}/com.xpturn.feedback/Skills~"
CLAUDE_BIN="${CLAUDE_BIN:-claude}"
EVALS="${REPO_ROOT}/tools/skill-evals"

case "${1:-}" in -h|--help) sed -n '2,/^set /p' "${BASH_SOURCE[0]}"; exit 0 ;; esac
EVAL=0
EXTRA=()
if [[ "${1:-}" == "--eval" ]]; then
  EVAL=1
  shift
  EXTRA=("$@")
fi

if ! command -v "${CLAUDE_BIN}" >/dev/null 2>&1; then
  echo "ENV Claude Code (${CLAUDE_BIN}) is not installed — the skill's manifests are not validated (not a PASS)"; exit 2
fi
echo "== skill · $("${CLAUDE_BIN}" --version 2>/dev/null)"

failed=0
# <label> <path> <what the validation must say it read> — one strict validation, its report printed when it fails
validate() {
  local out code
  out="$("${CLAUDE_BIN}" plugin validate --strict "$2" 2>&1)"
  code=$?
  if [[ ${code} -ne 0 ]]; then
    echo "   FAIL ${1}"; echo "${out}" | sed 's/^/      /'; failed=1
  elif ! grep -qF "$3" <<<"${out}"; then
    echo "   FAIL ${1} — the validation did not read it ('$3' missing)"; echo "${out}" | sed 's/^/      /'; failed=1
  else
    echo "   PASS ${1}"
  fi
}

validate "marketplace" "${REPO_ROOT}" "Validating marketplace manifest: ${REPO_ROOT}/.claude-plugin/marketplace.json"
validate "plugin" "${PLUGIN}" "Validating plugin manifest: ${PLUGIN}/.claude-plugin/plugin.json"
skills=$(find "${PLUGIN}/skills" -mindepth 2 -maxdepth 2 -name SKILL.md 2>/dev/null | wc -l | tr -d ' ')
if [[ "${skills}" -eq 0 ]]; then
  echo "   FAIL skills — no SKILL.md under ${PLUGIN}/skills"; failed=1
else
  validate "skills (${skills})" "${PLUGIN}/skills" "Validating components in: ${PLUGIN}/skills"
fi
if [[ ${EVAL} -eq 0 || ${failed} -ne 0 ]]; then
  exit "${failed}"
fi

echo "== skill evals"
WORK="${SKILL_EVAL_WORK:-${TMPDIR:-/tmp}/feedback-skill-eval}"
root="${WORK}/plugin"
rm -rf "${root}" "${WORK}/results.json"
mkdir -p "${root}"
cp -R "${PLUGIN}/.claude-plugin" "${PLUGIN}/skills" "${root}/"
if ! diff -r "${PLUGIN}/.claude-plugin" "${root}/.claude-plugin" >/dev/null || ! diff -r "${PLUGIN}/skills" "${root}/skills" >/dev/null; then
  echo "   FAIL the plugin root's copy differs from Skills~"; exit 1
fi
cp -R "${EVALS}/cases" "${root}/evals"
cp -R "${EVALS}/fixture" "${root}/fixture"
hash="$(python3 -c 'import json,sys; print(json.load(open(sys.argv[1]))["dependencies"]["com.xpturn.feedback"]["hash"][:12])' "${root}/fixture/project/Packages/packages-lock.json")"
cache="${root}/fixture/project/Library/PackageCache/com.xpturn.feedback@${hash}"
mkdir -p "${cache}"
rsync -a --exclude Tests --exclude Tests~ --exclude Samples~ --exclude SampleKit "${REPO_ROOT}/com.xpturn.feedback/" "${cache}/"
echo "   plugin root ${root} · cases $(find "${root}/evals" -mindepth 1 -maxdepth 1 -type d | wc -l | tr -d ' ') · package copy ${cache##*/}"

(cd "${root}" && "${CLAUDE_BIN}" plugin eval . --trust-plugin --no-publish --scaffold --allow-tools Write Edit \
  --model claude-sonnet-5-5 --judge-model haiku --threshold 0.8 --max-cost-usd 10 --json "${WORK}/results.json" ${EXTRA[@]+"${EXTRA[@]}"})
code=$?
python3 - "${WORK}/results.json" <<'EOF' || true
import json, sys
try:
    r = json.load(open(sys.argv[1]))
except Exception as e:
    print("   no results JSON:", e); sys.exit(0)
a = r.get("aggregates", {})
print(f"   score {a.get('overallScore')} · cases {a.get('casesPassed')}/{a.get('casesTotal')} · mean delta {a.get('meanDelta')} · cost {r.get('costUsd')} · partial {r.get('partial')}")
for c in r.get("cases", []):
    ca = c.get("aggregates", {})
    print(f"   {c.get('name', '?'):18} score {ca.get('score')} · delta {ca.get('delta')}")
EOF
case ${code} in
  0) echo "   PASS evals" ;;
  1) echo "   FAIL evals — a case below the threshold or one that did not load"; failed=1 ;;
  2) echo "   FAIL evals — the cost ceiling or the credential stopped the run (partial results)"; failed=1 ;;
  *) echo "   FAIL evals — claude plugin eval ended with ${code}"; failed=1 ;;
esac
exit "${failed}"
