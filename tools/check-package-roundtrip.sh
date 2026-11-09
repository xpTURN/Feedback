#!/usr/bin/env bash
#
# check-package-roundtrip.sh — an optional package taken out of the project and put back, with a saved player whose item
# points at that package's component (Cinemachine's CinemachineCamera). The data must come back: the reference stands as a
# placeholder while the package is out and is the component again when it is back, an edit made while it was out stays, and
# nothing else in the files changes.
#
# For each Unity version that version's dev project (tests/FeedbackTests.<major.minor>, whose editor version must be that
# version — otherwise the version fails) is cloned and refers to the package by its absolute path.
# tools/package-roundtrip/Editor goes to Assets/PackageRoundtrip/.
#   the file path   Make → the package taken out of the manifest, the project opened, an edit saved → back → Check: the
#                   reference is the component again · the edit stayed · the scene file differs only in the edited line ·
#                   the prefab file not at all · no compile error while out
#   the open path   one editor session: the Package Manager window's call (Client.AddAndRemove) takes the package out, the
#                   edit is saved, the same call puts it back with the manifest's value — in memory and in the file again
#   the negative    a package copy with a saved type in the package's optional assembly (saved types belong in Core —
#   control         here on purpose; the item file is tools/package-roundtrip/Negative): on the open path the file loses
#                   that item while the package is out (the breach is seen); the file path keeps such data and so cannot
#                   show it
# Every log: no engine assertion (Assertion failed · m_MissingStrongRefMap · registeredId). A run with no result line is a
# FAIL. When the package registry does not answer, a changed manifest does not resolve at all ('An error occurred while
# resolving packages', the editor exits 1 — the global cache does not help): that version is reported ENV and skipped —
# not a PASS.
#
# Usage:
#   tools/check-package-roundtrip.sh                       # 6000.0.85f2, 6000.3.28f1 and 6000.6.8f1
#   tools/check-package-roundtrip.sh 6000.6.8f1            # one version
#   ROUNDTRIP_WORK=/path tools/check-package-roundtrip.sh  # where the cloned projects and logs go
#   ROUNDTRIP_PACKAGE=/path tools/check-package-roundtrip.sh
#   ROUNDTRIP_TIMEOUT=900 tools/check-package-roundtrip.sh # the outer limit of one editor run (s)
#   UNITY_EDITORS=/path tools/check-package-roundtrip.sh   # folder holding <version>/Unity.app
#
# Time — a version's clone imports first (on its dev project's Library); each run then 15 to 50 s.
# Exit code: 1 if any run fails, else 2 if a version was skipped (ENV), else 0. Projects and logs are kept under the work
# folder.
set -uo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PKG_DIR="${ROUNDTRIP_PACKAGE:-${REPO_ROOT}/com.xpturn.feedback}"
TOOL="${REPO_ROOT}/tools/package-roundtrip"
WORK="${ROUNDTRIP_WORK:-${TMPDIR:-/tmp}/feedback-roundtrip}"
TIMEOUT="${ROUNDTRIP_TIMEOUT:-900}"
EDITORS="${UNITY_EDITORS:-/Applications/Unity/Hub/Editor}"
PACKAGE="com.unity.cinemachine"

case "${1:-}" in -h|--help) sed -n '2,/^set /p' "${BASH_SOURCE[0]}"; exit 0 ;; esac
VERSIONS=("$@")
[[ ${#VERSIONS[@]} -gt 0 ]] || VERSIONS=(6000.0.85f2 6000.3.28f1 6000.6.8f1)
mkdir -p "${WORK}"

failed=0
skipped=0
pass() { echo "   PASS $*"; }
fail() { echo "   FAIL $*"; failed=1; }

# <project> <command…> — the command in the background, its editor ended at the outer limit
run_limited() {
  local project="$1"; shift
  "$@" &
  local pid=$! t=0
  while kill -0 "${pid}" 2>/dev/null; do
    sleep 3
    t=$((t + 3))
    if [[ ${t} -ge ${TIMEOUT} ]]; then
      echo "   outer limit ${TIMEOUT} s — the editor is ended"
      pkill -f "projectPath ${project}"
      sleep 5
      pkill -9 -f "projectPath ${project}"
      wait "${pid}" 2>/dev/null
      return 124
    fi
  done
  wait "${pid}"
}

# <project> <manifest package path> — the package by its path; the dev project's other relative file: packages (the sample
# fixtures) and its relative package links (the samples) by their absolute path, or the clone (away from the repo) could
# not resolve them
point_package() {
  python3 - "$1/Packages/manifest.json" "$2" "${DEV_PROJECT}/Packages" <<'EOF'
import json, os, sys
path, pkg, dev_packages = sys.argv[1:]
m = json.load(open(path))
deps = m["dependencies"]
deps["com.xpturn.feedback"] = "file:" + pkg
for name, value in deps.items():
    if name != "com.xpturn.feedback" and value.startswith("file:") and not os.path.isabs(value[5:]):
        deps[name] = "file:" + os.path.normpath(os.path.join(dev_packages, value[5:]))
packages = os.path.dirname(path)
for entry in os.listdir(packages):
    link = os.path.join(packages, entry)
    if os.path.islink(link) and not os.path.isabs(os.readlink(link)):
        target = os.path.normpath(os.path.join(dev_packages, os.readlink(link)))
        os.remove(link)
        os.symlink(target, link)
m["testables"] = ["com.xpturn.feedback"]
json.dump(m, open(path, "w"), indent=2)
EOF
}

# <project> <out|back> <value> — the optional package out of the manifest or back with its value
set_package() {
  python3 - "$1/Packages/manifest.json" "$2" "$3" "${PACKAGE}" <<'EOF'
import json, sys
path, mode, value, name = sys.argv[1:]
m = json.load(open(path))
if mode == "out":
    m["dependencies"].pop(name, None)
else:
    m["dependencies"][name] = value
json.dump(m, open(path, "w"), indent=2)
EOF
}

for version in "${VERSIONS[@]}"; do
  echo "== package roundtrip · ${version}"
  contents="${EDITORS}/${version}/Unity.app/Contents"
  unity="${contents}/MacOS/Unity"
  if [[ ! -x "${unity}" ]]; then
    fail "Unity not found: ${unity}"; continue
  fi
  DEV_PROJECT="${REPO_ROOT}/tests/FeedbackTests.${version%.*}"
  dev_version="$(sed -n 's/^m_EditorVersion: //p' "${DEV_PROJECT}/ProjectSettings/ProjectVersion.txt" 2>/dev/null)"
  if [[ "${dev_version}" != "${version}" ]]; then
    fail "no dev project for ${version} — tests/FeedbackTests.${version%.*} is ${dev_version:-missing}"; continue
  fi
  echo "   dev project tests/FeedbackTests.${version%.*} (${dev_version})"
  out="${WORK}/${version}"
  project="${WORK}/roundtrip-${version}"
  rm -rf "${out}" "${project}"
  mkdir -p "${out}"

  # a fresh clone of this version's dev project
  cp -c -R "${DEV_PROJECT}" "${project}" 2>/dev/null || cp -R "${DEV_PROJECT}" "${project}"
  rm -rf "${project}/Temp" "${project}/Logs"
  mkdir -p "${project}/Assets/PackageRoundtrip"
  cp -R "${TOOL}/Editor" "${project}/Assets/PackageRoundtrip/"
  point_package "${project}" "${PKG_DIR}"
  value="$(python3 -c 'import json,sys; print(json.load(open(sys.argv[1]))["dependencies"].get(sys.argv[2], ""))' "${project}/Packages/manifest.json" "${PACKAGE}")"
  if [[ -z "${value}" ]]; then
    fail "the dev project has no ${PACKAGE}"; continue
  fi

  # <label> <method> [args…] — one editor run; returns 1 when the registry did not answer (ENV)
  editor() {
    local label="$1" method="$2"; shift 2
    local start=$(date +%s)
    run_limited "${project}" "${unity}" -batchmode -projectPath "${project}" -executeMethod "PackageRoundtrip.${method}" "$@" -logFile "${out}/${label}.log"
    echo "   ${label}: editor exit $? after $(( $(date +%s) - start )) s"
    if grep -q "An error occurred while resolving packages" "${out}/${label}.log" 2>/dev/null; then
      return 1
    fi
    return 0
  }

  # <label> <want> — the run's result lines and logs: OK lines, no BAD, the DONE line(s), no compile error, no assertion
  verdict() {
    python3 - "${project}/PackageRoundtrip.txt" "${out}" "$1" "$2" <<'EOF'
import glob, os, re, sys
path, out, label, want = sys.argv[1:]
lines = open(path).read().splitlines() if os.path.exists(path) else []
problems = [l for l in lines if l.startswith("BAD ")]
for done in want.split(","):
    if not any(l.startswith("DONE " + done) for l in lines):
        problems.append("no result line for " + done)
for log in glob.glob(os.path.join(out, "*.log")):
    text = open(log, errors="replace").read()
    problems += [os.path.basename(log) + ": " + e for e in sorted(set(re.findall(r"error CS\d+: [^\n]*", text)))[:3]]
    asserts = re.findall(r"[^\n]*(?:Assertion failed|m_MissingStrongRefMap|registeredId)[^\n]*", text)
    problems += [os.path.basename(log) + ": " + a.strip()[:160] for a in asserts[:3]]
for p in problems:
    print(f"   FAIL {label}: {p}")
if not problems:
    print(f"   PASS {label}: " + str(sum(l.startswith('OK ') for l in lines)) + " checks")
sys.exit(1 if problems else 0)
EOF
  }

  env_skip() { echo "   ENV ${version}: the package registry did not answer — this version's roundtrip runs are skipped (not a PASS)"; skipped=1; set_package "${project}" back "${value}"; }

  # the file path
  echo "-- the file path"
  rm -f "${out}"/*.log
  editor make Make || { env_skip; continue; }
  cp "${project}/Assets/PackageRoundtrip/Roundtrip.unity" "${out}/A.unity"; cp "${project}/Assets/PackageRoundtrip/Roundtrip.prefab" "${out}/A.prefab"
  set_package "${project}" out ""
  editor out Out || { env_skip; continue; }
  set_package "${project}" back "${value}"
  editor back Back || { env_skip; continue; }
  cp "${project}/Assets/PackageRoundtrip/Roundtrip.unity" "${out}/C.unity"; cp "${project}/Assets/PackageRoundtrip/Roundtrip.prefab" "${out}/C.prefab"
  verdict "the file path" make,out,back || failed=1
  scene_diff="$(diff "${out}/A.unity" "${out}/C.unity" | grep '^[<>]' | grep -v 'm_StartDelay:' | wc -l | tr -d ' ')"
  edited="$(diff "${out}/A.unity" "${out}/C.unity" | grep -c '^>.*m_StartDelay: 0.25')"
  prefab_diff="$(diff "${out}/A.prefab" "${out}/C.prefab" | grep -c '^[<>]')"
  if [[ "${scene_diff}" == 0 && "${edited}" == 1 && "${prefab_diff}" == 0 ]]; then
    pass "the file path's files: the scene differs only in the edited line · the prefab not at all"
  else
    fail "the file path's files: scene lines other than the edit ${scene_diff} · the edit ${edited} · prefab lines ${prefab_diff}"
  fi
  mkdir -p "${out}/file-run"; mv "${out}"/*.log "${out}/file-run/" 2>/dev/null

  # the open path
  echo "-- the open path"
  editor make2 Make || { env_skip; continue; }
  editor open Open -roundtripVersion "${value}" || { env_skip; continue; }
  verdict "the open path" open || failed=1
  mkdir -p "${out}/open-run"; mv "${out}"/*.log "${out}/open-run/" 2>/dev/null

  # the negative control: a saved type in the package's optional assembly
  echo "-- the negative control"
  negative="${WORK}/package-negative"
  rm -rf "${negative}"
  rsync -a --exclude Tests~ "${PKG_DIR}/" "${negative}/"
  cp "${TOOL}/Negative/RoundtripNegativeItem.cs" "${negative}/Runtime/Cinemachine/"
  point_package "${project}" "${negative}"
  editor nmake Make || { env_skip; point_package "${project}" "${PKG_DIR}"; continue; }
  editor nopen Open -roundtripVersion "${value}" || { env_skip; point_package "${project}" "${PKG_DIR}"; continue; }
  line="$(grep '^NEGATIVE ' "${project}/PackageRoundtrip.txt" 2>/dev/null)"
  if [[ "${line}" == "NEGATIVE memory=3 file=null" ]]; then
    pass "the negative control: the open path's file lost the optional assembly's saved item (${line#NEGATIVE }) — the breach is seen"
  else
    fail "the negative control: ${line:-no line} — want the item in memory and lost from the file"
  fi
  verdict "the negative control's reference" open || failed=1
  mkdir -p "${out}/negative-run"; mv "${out}"/*.log "${out}/negative-run/" 2>/dev/null
  point_package "${project}" "${PKG_DIR}"
done

[[ ${failed} -ne 0 ]] && exit 1
[[ ${skipped} -ne 0 ]] && exit 2
exit 0
