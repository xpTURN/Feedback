#!/usr/bin/env bash
#
# check-minimal-manifest.sh — the package compiles with every optional dependency removed.
#
# For each Unity version a throwaway project is created whose manifest holds only the package (file:),
# com.unity.test-framework and the built-in modules of that version's dev project (tests/FeedbackTests.<major.minor>, whose
# editor version must be that version — otherwise the version fails) minus every package named in a
# Runtime asmdef versionDefine (and minus the modules that depend on those, or UPM would pull them back).
# Then OptionalAssemblyTests runs there without the DevProject category and the result is checked:
#   - the editor exits 0 and the log has no "error CS"
#   - packages-lock.json holds none of the removed packages
#   - Library/ScriptAssemblies has every unconstrained package assembly and none of the constrained ones (Runtime/, Editor/ and
#     the sample kit SampleKit/, each at any depth) — the constrained ones checked are named in the result line
#   - no conditional test assembly of a removed module compiled (a Tests asmdef whose versionDefines name a removed package —
#     its other constraint, UNITY_INCLUDE_TESTS, always holds)
#   - the filtered tests ran (not zero) and all passed, no fixture failed outside its tests (a OneTimeTearDown failure —
#     exit 0 and failed 0)
#
# Usage:
#   tools/check-minimal-manifest.sh                         # 6000.0.85f2, 6000.3.28f1 and 6000.6.8f1
#   tools/check-minimal-manifest.sh 6000.6.8f1              # one version
#   G10_WORK=/path tools/check-minimal-manifest.sh          # where the throwaway projects go
#   G10_TIMEOUT=1500 tools/check-minimal-manifest.sh        # the outer limit of the editor run (s)
#   UNITY_EDITORS=/path tools/check-minimal-manifest.sh     # folder holding <version>/Unity.app
#
# Exit code: non-zero if any version fails. Projects and logs are kept under the work folder.
set -uo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PKG_DIR="${REPO_ROOT}/com.xpturn.feedback"
WORK="${G10_WORK:-${TMPDIR:-/tmp}/feedback-g10}"
TIMEOUT="${G10_TIMEOUT:-1500}"
EDITORS="${UNITY_EDITORS:-/Applications/Unity/Hub/Editor}"
TEST_FILTER="xpTURN.Feedback.Tests.Editor.OptionalAssemblyTests"
TEST_CATEGORY="!DevProject"

case "${1:-}" in -h|--help) sed -n '2,/^set /p' "${BASH_SOURCE[0]}"; exit 0 ;; esac
VERSIONS=("$@")
[[ ${#VERSIONS[@]} -gt 0 ]] || VERSIONS=(6000.0.85f2 6000.3.28f1 6000.6.8f1)

failed=0
for version in "${VERSIONS[@]}"; do
  echo "== minimal manifest · ${version}"
  contents="${EDITORS}/${version}/Unity.app/Contents"
  unity="${contents}/MacOS/Unity"
  if [[ ! -x "${unity}" ]]; then
    echo "   FAIL Unity not found: ${unity}"; failed=1; continue
  fi
  dev_project="${REPO_ROOT}/tests/FeedbackTests.${version%.*}"
  dev_version="$(sed -n 's/^m_EditorVersion: //p' "${dev_project}/ProjectSettings/ProjectVersion.txt" 2>/dev/null)"
  if [[ "${dev_version}" != "${version}" ]]; then
    echo "   FAIL no dev project for ${version} — tests/FeedbackTests.${version%.*} is ${dev_version:-missing}"; failed=1; continue
  fi
  echo "   dev project tests/FeedbackTests.${version%.*} (${dev_version})"

  project="${WORK}/minimal-${version}"
  rm -rf "${project}"
  mkdir -p "${project}/Packages" "${project}/Assets" "${project}/ProjectSettings"
  # Without ProjectSettings the editor treats the folder as a new project and replaces the manifest.
  echo "m_EditorVersion: ${version}" > "${project}/ProjectSettings/ProjectVersion.txt"

  python3 - "${PKG_DIR}" "${dev_project}/Packages/manifest.json" "${contents}/Resources/PackageManager/BuiltInPackages" "${project}" <<'EOF' || { failed=1; continue; }
import glob, json, os, sys

pkg_dir, dev_manifest, builtin, project = sys.argv[1:]

removed = set()
for path in glob.glob(os.path.join(pkg_dir, "Runtime", "**", "*.asmdef"), recursive=True) + \
        glob.glob(os.path.join(pkg_dir, "SampleKit", "**", "*.asmdef"), recursive=True):
    removed.update(v["name"] for v in json.load(open(path)).get("versionDefines", []))

def deps(name):
    path = os.path.join(builtin, name, "package.json")
    return json.load(open(path)).get("dependencies", {}) if os.path.exists(path) else {}

def pulls_removed(name, seen=()):
    return any(d in removed or (d not in seen and pulls_removed(d, seen + (name,))) for d in deps(name))

dev = json.load(open(dev_manifest))["dependencies"]
modules = sorted(m for m in dev if m.startswith("com.unity.modules.") and os.path.isdir(os.path.join(builtin, m)))
kept = [m for m in modules if m not in removed and not pulls_removed(m)]
dropped = [m for m in modules if m not in removed and m not in kept]

manifest = {
    "dependencies": dict(
        [("com.xpturn.feedback", "file:" + pkg_dir),
         ("com.unity.test-framework", json.load(open(os.path.join(builtin, "com.unity.test-framework", "package.json")))["version"])]
        + [(m, dev[m]) for m in kept]),
    "testables": ["com.xpturn.feedback"],
}
with open(os.path.join(project, "Packages", "manifest.json"), "w") as f:
    json.dump(manifest, f, indent=2)
with open(os.path.join(project, "removed.json"), "w") as f:
    json.dump(sorted(removed), f)
print("   removed:  " + ", ".join(sorted(removed)))
print("   dependent modules also off: " + (", ".join(dropped) or "-"))
EOF

  perl -e 'alarm shift; exec @ARGV' "${TIMEOUT}" "${unity}" -batchmode -projectPath "${project}" -runTests -testPlatform EditMode \
    -testFilter "${TEST_FILTER}" -testCategory "${TEST_CATEGORY}" \
    -testResults "${project}/results.xml" -logFile "${project}/editor.log"
  code=$?

  python3 - "${PKG_DIR}" "${project}" "${code}" <<'EOF' || failed=1
import glob, json, os, re, sys, xml.etree.ElementTree as ET

pkg_dir, project, code = sys.argv[1], sys.argv[2], int(sys.argv[3])
problems = []

if code != 0:
    problems.append(f"editor exit code {code}" + (" — the outer limit" if code == 142 else ""))

log = open(os.path.join(project, "editor.log"), errors="replace").read()
errors = sorted(set(re.findall(r"error CS\d+: [^\n]*", log)))
problems += ["compile: " + e for e in errors[:10]]

removed = json.load(open(os.path.join(project, "removed.json")))
lock_path = os.path.join(project, "Packages", "packages-lock.json")
lock = json.load(open(lock_path))["dependencies"] if os.path.exists(lock_path) else {}
if not lock:
    problems.append("packages-lock.json missing")
problems += ["resolved anyway: " + p for p in removed if p in lock]

assemblies = os.path.join(project, "Library", "ScriptAssemblies")
constrained = []
for path in glob.glob(os.path.join(pkg_dir, "Runtime", "**", "*.asmdef"), recursive=True) + \
        glob.glob(os.path.join(pkg_dir, "Editor", "**", "*.asmdef"), recursive=True) + \
        glob.glob(os.path.join(pkg_dir, "SampleKit", "**", "*.asmdef"), recursive=True):
    asmdef = json.load(open(path))
    present = os.path.exists(os.path.join(assemblies, asmdef["name"] + ".dll"))
    expected = not asmdef.get("defineConstraints")
    if not expected:
        constrained.append(asmdef["name"])
    if present != expected:
        problems.append(f"{asmdef['name']}: compiled={present}, expected={expected}")

conditional = 0
for path in glob.glob(os.path.join(pkg_dir, "Tests", "**", "*.asmdef"), recursive=True):
    asmdef = json.load(open(path))
    if not {v["name"] for v in asmdef.get("versionDefines", [])} & set(removed):
        continue
    conditional += 1
    if os.path.exists(os.path.join(assemblies, asmdef["name"] + ".dll")):
        problems.append(f"{asmdef['name']}: a conditional test assembly of a removed module compiled")
if conditional == 0:
    problems.append("no conditional test assembly of a removed module found")

results = os.path.join(project, "results.xml")
run = re.search(r'<test-run [^>]*', open(results).read()) if os.path.exists(results) else None
counts = {k: int(re.search(fr'\b{k}="(\d+)"', run.group(0)).group(1)) for k in ("total", "passed", "failed")} if run else None
if not counts:
    problems.append("no test results")
elif counts["total"] == 0 or counts["passed"] != counts["total"]:
    problems.append(f"tests: {counts}")
if counts:
    problems += ["suite failed: " + f'{s.get("fullname")} @{s.get("site")}' for s in ET.parse(results).iter("test-suite")
                 if s.get("result") == "Failed" and s.get("site") != "Child"][:5]

for p in problems:
    print("   FAIL " + p)
if problems:
    print(f"   log: {os.path.join(project, 'editor.log')}")
    sys.exit(1)
print(f"   PASS tests {counts['passed']}/{counts['total']} · no removed package resolved · constrained assemblies absent · {conditional} conditional test assemblies absent")
print("   constrained: " + ", ".join(sorted(constrained)))
EOF
done

exit "${failed}"
