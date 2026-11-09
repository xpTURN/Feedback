#!/usr/bin/env bash
#
# check-player-gates.sh — the PlayMode suite in IL2CPP macOS arm64 players, the linker's negative control, the release probe
# and a scripts-only build.
#
# For each Unity version that version's dev project (tests/FeedbackTests.<major.minor>, whose editor version must be that
# version — otherwise the version fails) is cloned and refers to the package by its absolute path. tools/player-gates/Editor
# and Runtime go to Assets/PlayerGate/. Then, one run at a time:
#   the development players through the test framework's player run (always a development build — the architecture and
#        the backend come from tools/player-gates/arm64.json, the code generation and the stripping are written into the
#        clone's ProjectSettings): Faster runtime · Faster (smaller) builds with Minimal on every version, and Low (Faster
#        runtime) on one version (GATES_LOW_VERSION — the Low results of the two versions are the same). Each: no failure,
#        no compile error, no compile warning of ours (the compiler's or an analyzer's), the player's architecture line
#        (Arm64), the same test count in each run of a version; the PERF and GATE lines go to a table (perf-<version>.txt)
#   the negative control: the package without the linker processor (and its editor tests), Minimal — GP13_Linker red on its
#        five paths (an engine method nothing else calls vanishes with no error), every other row green
#   the release player: a non-development IL2CPP player with the test assemblies runs the release probe
#        (-feedbackReleaseProbe) — not a debug build, no reason string on any refused pair, the exit's order (OnApplicationQuit
#        → the exit token → the token's play Stopped with the Default open → the other ContextShutdown, closed → quitting →
#        OnDestroy) · a Volume Blend without a profile refused with no reason · the PostFX overlay on with a Via Scripting camera
#        before any render logs no error · an impulse from no source refused with no reason · an activation's blend override
#        ours while it waits, gone after the Default's close · a rumble's Player reference with no PlayerInput refused with no
#        reason, a rumble on a recording gamepad driven while playing and (0, 0) after the Default's close. The same probe in a
#        development player gives every pair a reason (the positive control). An Input
#        System of 1.20 or later does not compile in a non-development build with the test assemblies (its test hooks call
#        a DEBUG-only member) — that version's release clone drops the package and takes the old input handling (the rumble
#        is absent there); GATES_DROPPED_INPUT also runs that branch on an earlier version, after the kept one (its files end
#        in -dropped)
#   a development build of the build scenes and the package's scene (the saved sets cleared first), then a script
#        changed and a scripts-only build somewhere new: in both players the scene's engine method survives (the processor
#        merges the set the full build left for its target — Camera.Reset: the field of view 30 → 60)
# A test-framework player run starts only when no other Unity editor runs — another editor's profiler takes a test player
# over and its results never come, and the timings shake with another run. A run has the player heartbeat
# (GATES_HEARTBEAT) and an outer limit that ends its editor and its player (a dead player leaves the editor waiting past the
# heartbeat); a run with no results is a FAIL.
#
# Usage:
#   tools/check-player-gates.sh                          # 6000.0.85f2, 6000.3.28f1 and 6000.6.8f1
#   tools/check-player-gates.sh 6000.6.8f1               # one version
#   GATES_WORK=/path tools/check-player-gates.sh         # where the cloned projects, players and logs go
#   GATES_PACKAGE=/path tools/check-player-gates.sh      # another copy of the package
#   GATES_LOW_VERSION=6000.3.28f1 tools/check-player-gates.sh
#   GATES_DROPPED_INPUT=1 tools/check-player-gates.sh    # the release probe also on a clone without the Input System (the 1.20 branch on an earlier version)
#   GATES_TIMEOUT=1500 GATES_IDLE_WAIT=600 GATES_HEARTBEAT=120 tools/check-player-gates.sh
#   UNITY_EDITORS=/path tools/check-player-gates.sh      # folder holding <version>/Unity.app
#
# Time — a version's first build is minutes; each other run one to three. Keep the machine to itself (no other Unity).
# Exit code: non-zero if any run fails. Projects, players and logs are kept under the work folder.
set -uo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PKG_DIR="${GATES_PACKAGE:-${REPO_ROOT}/com.xpturn.feedback}"
GATE="${REPO_ROOT}/tools/player-gates"
WORK="${GATES_WORK:-${TMPDIR:-/tmp}/feedback-gates}"
TIMEOUT="${GATES_TIMEOUT:-1500}"
IDLE_WAIT="${GATES_IDLE_WAIT:-600}"
HEARTBEAT="${GATES_HEARTBEAT:-120}"
LOW_VERSION="${GATES_LOW_VERSION:-6000.6.8f1}"
EDITORS="${UNITY_EDITORS:-/Applications/Unity/Hub/Editor}"

case "${1:-}" in -h|--help) sed -n '2,/^set /p' "${BASH_SOURCE[0]}"; exit 0 ;; esac
VERSIONS=("$@")
[[ ${#VERSIONS[@]} -gt 0 ]] || VERSIONS=(6000.0.85f2 6000.3.28f1 6000.6.8f1)
mkdir -p "${WORK}"

failed=0
pass() { echo "   PASS $*"; }
fail() { echo "   FAIL $*"; failed=1; }

# no other Unity editor (or its import workers) — ours run one at a time, so any is another
wait_idle() {
  local waited=0
  while pgrep -f "Unity.app/Contents/MacOS/Unity " >/dev/null; do
    if [[ ${waited} -ge ${IDLE_WAIT} ]]; then
      echo "   another Unity runs: $(pgrep -fl "Unity.app/Contents/MacOS/Unity " | head -2 | cut -c1-120 | tr '\n' ';')"
      return 1
    fi
    [[ $((waited % 60)) -eq 0 ]] && echo "   waiting for another Unity to end (${waited} s)"
    sleep 15
    waited=$((waited + 15))
  done
  return 0
}

# <project> <command…> — the command in the background, its editor and the project's player ended at the outer limit
run_limited() {
  local project="$1"; shift
  "$@" &
  local pid=$! t=0
  while kill -0 "${pid}" 2>/dev/null; do
    sleep 5
    t=$((t + 5))
    if [[ ${t} -ge ${TIMEOUT} ]]; then
      echo "   outer limit ${TIMEOUT} s — the editor and its player are ended"
      pkill -f "projectPath ${project}"; pkill -f "${project}/Temp/UnityTempFile"
      sleep 5
      pkill -9 -f "projectPath ${project}"; pkill -9 -f "${project}/Temp/UnityTempFile"
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

# <project> <code generation 0 Faster runtime · 1 Faster (smaller) builds> <stripping - Minimal · 1 Low>
player_settings() {
  python3 - "$1/ProjectSettings/ProjectSettings.asset" "$2" "$3" <<'EOF'
import re, sys
path, codegen, strip = sys.argv[1:]
s = open(path, encoding="utf-8").read()
def put(s, key, value):
    m = re.search(r"^(  " + key + r":)( \{\}|\n(?:    [^\n]+\n)*)", s, re.M)
    if m is None:
        raise SystemExit("no " + key + " in ProjectSettings.asset")
    body = m.group(2)
    entries = dict(re.findall(r"    ([^:\n]+): ([^\n]+)\n", body)) if body.strip() != "{}" else {}
    if value is None:
        entries.pop("Standalone", None)
    else:
        entries["Standalone"] = value
    new = m.group(1) + (" {}" if not entries else "\n" + "".join(f"    {k}: {v}\n" for k, v in entries.items()))
    if body.strip() == "{}" and entries:
        new = new.rstrip("\n")
    return s[:m.start()] + new + s[m.end():]
s = put(s, "scriptingBackend", "1")
s = put(s, "il2cppCodeGeneration", codegen)
s = put(s, "managedStrippingLevel", None if strip == "-" else strip)
open(path, "w", encoding="utf-8").write(s)
EOF
}

# <unity> <project> <out base> [filter] — the test framework's player run; the results checked by the caller
utf_run() {
  local unity="$1" project="$2" base="$3" filter="${4:-}"
  rm -f "${base}.xml" "${base}.log"
  if ! wait_idle; then
    return 3
  fi
  pgrep -f "projectPath ${project}" >/dev/null || rm -rf "${project}/Temp" "${project}"/Assets/InitTestScene*
  local args=(-batchmode -projectPath "${project}" -runTests -testPlatform StandaloneOSX -buildTarget StandaloneOSX
    -testSettingsFile "${GATE}/arm64.json" -playerHeartbeatTimeout "${HEARTBEAT}"
    -testResults "${base}.xml" -logFile "${base}.log")
  [[ -n "${filter}" ]] && args+=(-testFilter "${filter}")
  local start=$(date +%s)
  run_limited "${project}" "${unity}" "${args[@]}"
  local code=$?
  echo "   editor exit ${code} after $(( $(date +%s) - start )) s"
  return 0
}

# <base> <mode dev|negative> <label> <expected total or 0> — prints the verdict lines; the total on the last line
check_run() {
  python3 - "$1" "$2" "$3" "$4" "${PKG_DIR}" <<'EOF'
import os, re, sys, xml.etree.ElementTree as ET
base, mode, label, want_total, pkg = sys.argv[1], sys.argv[2], sys.argv[3], int(sys.argv[4]), sys.argv[5]
problems, notes = [], []
log = open(base + ".log", errors="replace").read() if os.path.exists(base + ".log") else ""
errors = sorted(set(re.findall(r"error CS\d+: [^\n]*", log)))
problems += ["compile: " + e for e in errors[:5]]
ours = sorted(set(l for l in re.findall(r"[^\n]*warning (?:CS|UAC)\d+[^\n]*", log) if "com.xpturn.feedback" in l or "xpTURN.Feedback" in l or "PlayerGate" in l or "SampleFixtures" in l))
problems += ["our warning: " + w[-160:] for w in ours[:5]]
total = 0
if not os.path.exists(base + ".xml"):
    problems.append("no results")
else:
    root = ET.parse(base + ".xml").getroot()
    cases = list(root.iter("test-case"))
    total = len(cases)
    failed = [(c.get("fullname") or c.get("name"), (c.findtext("failure/message") or "").strip().replace("\n", " ")) for c in cases if c.get("result") == "Failed"]
    problems += ["suite failed: " + f'{s.get("fullname")} @{s.get("site")}' for s in root.iter("test-suite")
                 if s.get("result") == "Failed" and s.get("site") != "Child"][:5]
    text = "\n".join(o.text or "" for o in root.iter("output"))
    arch = re.search(r"PERF player arch=(\S+) platform=(\S+)", text)
    if mode == "dev":
        if total == 0:
            problems.append("no test ran")
        if want_total and total != want_total:
            problems.append(f"{total} tests (the version's first run had {want_total})")
        problems += [f"{n.split('.')[-1]}: {m[:200]}" for n, m in failed[:8]]
        if not arch or arch.group(1) != "Arm64":
            problems.append("the player's architecture — " + (arch.group(1) if arch else "no line"))
        perf = [l for l in text.splitlines() if l.startswith("PERF player ")]
        gate = [l for l in text.splitlines() if l.startswith("GATE player ")]
        with open(base + ".perf.txt", "w") as f:
            f.write("\n".join(perf + gate) + "\n")
        notes.append(f"{total} tests · {len(perf)} PERF · {len(gate)} GATE lines · {arch.group(1) if arch else '?'}")
    else:
        gp13 = [m for n, m in failed if "GP13_Linker" in n]
        others = [n for n, m in failed if "GP13_Linker" not in n]
        if not gp13:
            problems.append("GP13_Linker green without the processor")
        else:
            for clause in ("Resources — children 1 → 1", "intensity 7 → 7", "field of view 30 → 30", "near plane 0.2 → 0.2", "aspect 5 → 5"):
                if clause not in gp13[0]:
                    problems.append("GP13_Linker path still kept — " + clause)
        problems += ["red without cause: " + n.split(".")[-1] for n in others[:8]]
        notes.append(f"{total} tests · GP13_Linker red on " + str(sum(c in (gp13[0] if gp13 else "") for c in ("children 1 → 1", "intensity 7 → 7", "field of view 30 → 30", "near plane 0.2 → 0.2", "aspect 5 → 5"))) + " paths")
for p in problems:
    print(f"   FAIL {label}: {p}")
if not problems:
    print(f"   PASS {label}: " + "; ".join(notes))
print(f"TOTAL {total} {1 if problems else 0}")
EOF
}

# <app> <args…> — runs a built player with -batchmode -nographics, at most 60 s
run_player() {
  local app="$1"; shift
  local exe
  exe="$(ls "${app}/Contents/MacOS/" 2>/dev/null | head -1)"
  [[ -n "${exe}" ]] || return 1
  "${app}/Contents/MacOS/${exe}" -batchmode -nographics "$@" >/dev/null 2>&1 &
  local pid=$! i
  for i in $(seq 1 60); do
    kill -0 "${pid}" 2>/dev/null || break
    sleep 1
  done
  kill -0 "${pid}" 2>/dev/null && kill "${pid}"
  return 0
}

for version in "${VERSIONS[@]}"; do
  echo "== player gates · ${version}"
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
  rm -rf "${out}"
  mkdir -p "${out}"

  # <project> [drop the Input System] — a fresh clone of this version's dev project
  make_clone() {
    local project="$1" drop="${2:-}"
    rm -rf "${project}"
    cp -c -R "${DEV_PROJECT}" "${project}" 2>/dev/null || cp -R "${DEV_PROJECT}" "${project}"
    rm -rf "${project}/Temp" "${project}/Logs"
    mkdir -p "${project}/Assets/PlayerGate"
    cp -R "${GATE}/Editor" "${GATE}/Runtime" "${project}/Assets/PlayerGate/"
    python3 - "${project}" "${drop}" <<'EOF' || return 1
import json, os, re, sys
project, drop = sys.argv[1:]
manifest_path = os.path.join(project, "Packages", "manifest.json")
manifest = json.load(open(manifest_path))
deps = manifest["dependencies"]
if drop:
    del deps["com.unity.inputsystem"]
    settings = os.path.join(project, "ProjectSettings", "ProjectSettings.asset")
    s = open(settings, encoding="utf-8").read()
    open(settings, "w", encoding="utf-8").write(re.sub(r"^(  activeInputHandler:) \d+", r"\1 0", s, flags=re.M))
json.dump(manifest, open(manifest_path, "w"), indent=2)
print("   input system " + deps.get("com.unity.inputsystem", "dropped"))
EOF
    point_package "${project}" "${PKG_DIR}"
  }

  project="${WORK}/gates-${version}"
  make_clone "${project}" || { fail "the clone"; continue; }
  input="$(python3 -c 'import json,sys; print(json.load(open(sys.argv[1]))["dependencies"].get("com.unity.inputsystem",""))' "${project}/Packages/manifest.json")"

  # the development players
  runs=("faster-runtime 0 -" "faster-smaller 1 -")
  [[ "${version}" == "${LOW_VERSION}" ]] && runs+=("low 0 1")
  first_total=0
  for spec in "${runs[@]}"; do
    set -- ${spec}
    label="$1"; player_settings "${project}" "$2" "$3" || { fail "${label}: the settings"; continue; }
    echo "-- development player ${label}"
    if ! utf_run "${unity}" "${project}" "${out}/${label}"; then
      fail "${label}: another Unity kept running"; continue
    fi
    verdict="$(check_run "${out}/${label}" dev "development player ${label}" "${first_total}")"
    echo "${verdict}" | grep -v "^TOTAL "
    set -- $(echo "${verdict}" | sed -n 's/^TOTAL //p')
    [[ "${2:-1}" == "0" ]] || failed=1
    [[ ${first_total} -eq 0 ]] && first_total="${1:-0}"
    cat "${out}/${label}.perf.txt" 2>/dev/null | sed -E "s/^(PERF|GATE) player +/${label} | /" >> "${out}/perf-${version}.txt"
  done

  # the negative control: the package without the processor
  echo "-- negative control"
  noproc="${WORK}/package-without-processor"
  rm -rf "${noproc}"
  rsync -a --exclude Tests~ --exclude 'Editor/Build/UnityEventLinkerProcessor.cs*' --exclude 'Tests/Editor/Player/UnityEventLinkerTests.cs*' --exclude 'Tests/Editor/Presets/PresetOpsTests.cs*' "${PKG_DIR}/" "${noproc}/"
  point_package "${project}" "${noproc}"
  player_settings "${project}" 0 -
  if utf_run "${unity}" "${project}" "${out}/negative"; then
    verdict="$(check_run "${out}/negative" negative "negative control" 0)"
    echo "${verdict}" | grep -v "^TOTAL "
    [[ "$(echo "${verdict}" | sed -n 's/^TOTAL [0-9]* //p')" == "0" ]] || failed=1
  else
    fail "negative control: another Unity kept running"
  fi
  point_package "${project}" "${PKG_DIR}"

  # a full development build, then a scripts-only build somewhere new
  echo "-- scripts-only build"
  rm -rf "${project}/Library/xpTURN.Feedback" "${project}/Temp"
  for mode in full scripts; do
    [[ "${mode}" == scripts ]] && echo "// changed $(date +%s)" >> "${project}/Assets/PlayerGate/Runtime/GateSceneProbe.cs"
    run_limited "${project}" "${unity}" -batchmode -projectPath "${project}" -executeMethod PlayerGates.ScriptsOnlyBuild.Build \
      -soOut "${out}/so-${mode}.app" -soMode "${mode}" -logFile "${out}/so-${mode}.log"
    rm -f "${out}/so-${mode}.txt"
    run_player "${out}/so-${mode}.app" -gateLinkerScene "${out}/so-${mode}.txt" -logFile "${out}/so-${mode}-player.log"
    line="$(cat "${out}/so-${mode}.txt" 2>/dev/null || echo "no probe line")"
    if [[ "${line}" == "scene fov before=30 after=60" ]]; then
      pass "scripts-only ${mode}: ${line}"
    else
      fail "scripts-only ${mode}: ${line} (want the field of view 30 → 60)"
    fi
  done

  # the release player and its development twin
  echo "-- release probe"
  releases=("${project}")
  if python3 -c 'import sys; v=sys.argv[1].split("."); sys.exit(0 if len(v) > 1 and (int(v[0]), int(v[1])) >= (1, 20) else 1)' "${input:-0.0}"; then
    releases=("${WORK}/gates-${version}-release")
  elif [[ -n "${GATES_DROPPED_INPUT:-}" ]]; then
    releases+=("${WORK}/gates-${version}-release")
  fi
  for release in "${releases[@]}"; do
  tag=""
  if [[ "${release}" != "${project}" ]]; then
    make_clone "${release}" drop || { fail "the release clone"; continue; }
    [[ ${#releases[@]} -gt 1 ]] && tag="-dropped"
  fi
  player_settings "${release}" 0 -
  for kind in release development; do
    dev=""; [[ "${kind}" == development ]] && dev="-releaseDev"
    run_limited "${release}" "${unity}" -batchmode -projectPath "${release}" -executeMethod PlayerGates.ReleaseBuild.Build \
      -releaseOut "${out}/${kind}${tag}.app" ${dev:+"${dev}"} -logFile "${out}/${kind}${tag}-build.log"
    rm -f "${out}/${kind}${tag}.txt"
    run_player "${out}/${kind}${tag}.app" -feedbackReleaseProbe "${out}/${kind}${tag}.txt" -logFile "${out}/${kind}${tag}-player.log"
    python3 - "${out}/${kind}${tag}.txt" "${kind}" "$([[ "${release}" != "${project}" ]] && echo dropped || echo kept)" <<'EOF' || failed=1
import os, re, sys
path, kind, input_system = sys.argv[1:]
lines = open(path).read().splitlines() if os.path.exists(path) else []
problems = []
start = lines[0] if lines else ""
debug = kind == "development"
if start != ("start debug=True DEBUG" if debug else "start debug=False"):
    problems.append("the build — " + (start or "no probe file"))
pairs = next((re.match(r"refused pairs=(\d+) joined=(\d+)", l) for l in lines if l.startswith("refused pairs=")), None)
if not pairs or int(pairs.group(1)) == 0:
    problems.append("no refused pair")
elif int(pairs.group(2)) != (int(pairs.group(1)) if debug else 0):
    problems.append(f"reasons on {pairs.group(2)} of {pairs.group(1)} refused pairs")
# a Volume Blend without a profile refused (its reason a development build's only) · the PostFX overlay on with a Via
# Scripting camera before any render — no error
blend = next((l for l in lines if l.startswith("postfx refused ") or l == "postfx absent"), None)
want = "postfx refused prepared=False reason=" + ("'the profile is not a Volume Profile'" if debug else "null")
if blend != want:
    problems.append("the PostFX refusal — " + (blend or "no line"))
overlay = next((l for l in lines if l.startswith("postfx errors=")), None)
if overlay != "postfx errors=0 initialized=False":
    problems.append("the PostFX overlay before a render — " + (overlay or "no line"))
# an impulse from no source refused (its reason a development build's only) · an activation's blend override ours while it
# waits, the one before (none) after the Default's close
impulse = next((l for l in lines if l.startswith("cinemachine refused ") or l == "cinemachine absent"), None)
want = "cinemachine refused prepared=False reason=" + ("'the source is not a Cinemachine Impulse Source'" if debug else "null")
if impulse != want:
    problems.append("the Cinemachine refusal — " + (impulse or "no line"))
blends = [l for l in lines if l.startswith("cinemachine override=") or l.startswith("cinemachine closed ")]
if blends != ["cinemachine override=CinemachineFeedbackService", "cinemachine closed override=null"]:
    problems.append("the Cinemachine blend override — " + (" → ".join(blends) or "no line"))
# a rumble's Player reference with no PlayerInput refused (its reason a development build's only) · a rumble on a recording
# gamepad driven while playing, (0, 0) after the Default's close — absent where the release clone drops the Input System
rumble = [l for l in lines if l.startswith("rumble ")]
want = ["rumble absent"] if input_system == "dropped" else ["rumble refused prepared=False reason=" + ("'no parent'" if debug else "null"),
                                                            "rumble playing last=0.50,0.25", "rumble closed last=0.00,0.00"]
if rumble != want:
    problems.append("the rumble — " + (" → ".join(rumble) or "no line"))
order = ["quit", "OnApplicationQuit default=open", "exit token default=open", "end Stopped default=open",
         "end ContextShutdown default=closed", "quitting", "OnDestroy default=closed"]
tail = [l for l in lines[lines.index("quit"):] if not l.startswith("cinemachine ") and not l.startswith("rumble ")] if "quit" in lines else []
if tail != order:
    problems.append("the exit's order — " + " → ".join(tail or ["none"]))
label = "release probe " + kind
for p in problems:
    print(f"   FAIL {label}: {p}")
if problems:
    sys.exit(1)
print(f"   PASS {label}: {pairs.group(1)} refused pairs, reasons {pairs.group(2)} · the PostFX refusal · no error before a render · the Cinemachine refusal · its blend override · the rumble ({input_system}) · the exit's order")
EOF
  done
  done

  [[ -s "${out}/perf-${version}.txt" ]] && echo "   PERF table: ${out}/perf-${version}.txt ($(wc -l < "${out}/perf-${version}.txt" | tr -d ' ') lines)"
done

exit "${failed}"
