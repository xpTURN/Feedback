#!/usr/bin/env bash
#
# check-samples.sh — the samples as a user gets them.
#
# For each Unity version that version's dev project (tests/FeedbackTests.<major.minor>, whose editor version must be that
# version — otherwise the version fails) is cloned without the fixtures' dev package. The package is copied beside the clone
# with the fixture bundle (tests/SampleFixtures/Fixture — a
# positive-control #warning added) put in its Samples~ and a samples entry, the clone refers to that copy, and every sample is
# imported (Sample.Import — over a previous import). Then the samples tests (xpTURN.Feedback.Tests.Editor.Samples) and FE08
# run there:
#   - no "error CS" · our warnings (a line naming com.xpturn.feedback · xpTURN.Feedback · SampleFixtures) are the positive
#     control's CS1030 only, and it is seen
#   - the tests ran (not zero) and all passed, no fixture failed outside its tests (a OneTimeTearDown failure — exit 0 and
#     failed 0) · the hub round trip opened every available card — the cards whose required assembly is in
#     Library/ScriptAssemblies (no empty round trip)
#   - ProjectSettings · EditorBuildSettings are byte for byte as before the tests ('Build Samples' puts them back in the run)
# Branches, each on a copy of the imported clone:
#   --no-cm      Cinemachine out of the manifest — its card grey, its assembly absent, the round trip of the others
#   --no-timeline  Timeline out of the manifest (on 6000.6 its built-in package too) — S14's card grey, its assembly absent,
#                the round trip of the others, Console 0
#   --old-input  activeInputHandler 0 — the HUD's old-input notice and the round trip, Console 0
#   --no-tmp     the clone's TMP Essential Resources removed — the round trip and the gallery's TMP parts off, Console 0
#                (S4 without numbers · S7's popup without its TMP part)
#   --player     macOS players of 'Build Samples' — Mono development and release, IL2CPP arm64 development — toured with
#                -sampleTour (tools/samples/tour_verdict.py): every one visits the available cards' scenes (2N + 1 stops), finds
#                the method of every persistent UnityEvent call, plays the Showcase's game and logs no warning or error of ours;
#                a development one reads the same run's HUD on − off ≤ 32 B at every stop (a stop over it measured again, twice
#                at most) and a 1 KB positive control, Stress Zero GC at 1,000 players ≤ 32 B over the quietest hub stop, the
#                game's allocating frames ≤ 10 with 30 plays or more; in release the counter absent and the HUD's 'GC:
#                development build only'
#   --stress-table  the IL2CPP arm64 development player of 'Build Samples' (--player's when it built one) run with
#                -stressTable (tests/SampleFixtures/Fixture/Tour/StressTable.cs): Stress Zero GC's matrix twice, judged by
#                tools/samples/stress_table.py — every players window within 32 B of the no-cube baseline over 300 frames and the
#                coroutine control over 1 KB a frame; it prints the table of Documentation~/Performance.md (both versions' logs
#                together: python3 tools/samples/stress_table.py 6000.3.28f1=<log> 6000.6.8f1=<log>). No other Unity work while
#                it runs — the times are in the table
#   --minimal    the minimal manifest (tools/check-minimal-manifest.sh for the version)
#
# Usage:
#   tools/check-samples.sh                                   # 6000.0.85f2, 6000.3.28f1 and 6000.6.8f1, no branch
#   tools/check-samples.sh --no-cm --old-input --player 6000.6.8f1
#   SAMPLES_WORK=/path tools/check-samples.sh                # where the clones, package copies and logs go
#   SAMPLES_TIMEOUT=1500 tools/check-samples.sh              # the outer limit of one editor run (s)
#   UNITY_EDITORS=/path tools/check-samples.sh               # folder holding <version>/Unity.app
#
# Players need the display on (caffeinate -u -d) and no other Unity open on them. Exit code: non-zero if anything fails.
set -uo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PKG_DIR="${REPO_ROOT}/com.xpturn.feedback"
FIXTURES="${REPO_ROOT}/tests/SampleFixtures"
DRIVER="${REPO_ROOT}/tools/samples/Editor"
WORK="${SAMPLES_WORK:-${TMPDIR:-/tmp}/feedback-samples}"
TIMEOUT="${SAMPLES_TIMEOUT:-1500}"
EDITORS="${UNITY_EDITORS:-/Applications/Unity/Hub/Editor}"
FILTER='xpTURN\.Feedback\.Tests\.Editor\.Samples\.|FE08'

case "${1:-}" in -h|--help) sed -n '2,/^set /p' "${BASH_SOURCE[0]}"; exit 0 ;; esac
NO_CM=0; NO_TIMELINE=0; OLD_INPUT=0; NO_TMP=0; PLAYER=0; STRESS=0; MINIMAL=0; VERSIONS=()
for a in "$@"; do
  case "${a}" in
    --no-cm) NO_CM=1 ;;
    --no-timeline) NO_TIMELINE=1 ;;
    --old-input) OLD_INPUT=1 ;;
    --no-tmp) NO_TMP=1 ;;
    --player) PLAYER=1 ;;
    --stress-table) STRESS=1 ;;
    --minimal) MINIMAL=1 ;;
    *) VERSIONS+=("${a}") ;;
  esac
done
[[ ${#VERSIONS[@]} -gt 0 ]] || VERSIONS=(6000.0.85f2 6000.3.28f1 6000.6.8f1)
mkdir -p "${WORK}"

failed=0
pass() { echo "   PASS $*"; }
fail() { echo "   FAIL $*"; failed=1; }

# <log> <command…> — one editor run at the outer limit
editor() {
  local log="$1"; shift
  local start=$(date +%s)
  perl -e 'alarm shift; exec @ARGV' "${TIMEOUT}" "$@" -logFile "${log}"
  local code=$?
  echo "   $(basename "${log}" .log): editor exit ${code} after $(( $(date +%s) - start )) s"
  return ${code}
}

# <project> — byte digests of the two settings files the samples may touch
settings() { shasum "$1/ProjectSettings/ProjectSettings.asset" "$1/ProjectSettings/EditorBuildSettings.asset" | cut -c1-16 | tr '\n' ' '; }

# <label> <project> <results.xml> <want the positive control 1|0> <log…> — the test run's verdict: results, the cards opened
# against the available ones, compile errors, our warnings against the positive control (a branch's run may compile nothing anew)
tests_verdict() {
  python3 - "$@" <<'EOF'
import glob, os, re, sys, xml.etree.ElementTree as ET
label, project, results, want, logs = sys.argv[1], sys.argv[2], sys.argv[3], sys.argv[4] == "1", sys.argv[5:]
problems = []
text = "".join(open(l, errors="replace").read() for l in logs if os.path.exists(l))
problems += ["compile: " + e for e in sorted(set(re.findall(r"error CS\d+: [^\n]*", text)))[:5]]
warnings = sorted(set(l.strip() for l in re.findall(r"[^\n]*warning (?:CS|UAC)\d+[^\n]*", text)
                      if any(k in l for k in ("com.xpturn.feedback", "xpTURN.Feedback", "SampleFixtures"))))
positive = [w for w in warnings if "KQ9Positive" in w and "CS1030" in w]
ours = [w for w in warnings if "KQ9Positive" not in w]
if want and not positive:
    problems.append("the positive-control warning (KQ9Positive · CS1030) was not seen")
problems += ["our warning: " + w[-180:] for w in ours[:5]]
run = re.search(r"<test-run [^>]*", open(results).read()) if os.path.exists(results) else None
if not run:
    problems.append("no test results")
else:
    counts = {k: int(re.search(fr'\b{k}="(\d+)"', run.group(0)).group(1)) for k in ("total", "passed", "failed")}
    if counts["total"] == 0 or counts["failed"] != 0:
        failing = re.findall(r'<test-case [^>]*fullname="([^"]*)"[^>]*result="Failed"', open(results).read())
        problems.append(f"tests {counts} failing {failing[:5]}")
    suites = [f'{s.get("fullname")} @{s.get("site")}' for s in ET.parse(results).iter("test-suite")
              if s.get("result") == "Failed" and s.get("site") != "Child"]
    problems += ["suite failed: " + s for s in suites[:5]]
    print(f"   {label}: tests {counts['passed']}/{counts['total']}")
# the oracle: a card is available when its required assembly compiled (Library/ScriptAssemblies)
assemblies = os.path.join(project, "Library", "ScriptAssemblies")
available = 0
for card in glob.glob(os.path.join(project, "Assets", "Samples", "**", "*.asset"), recursive=True):
    m = re.search(r"RequiredType: [^,\n]*, ([^\n]+)", open(card, errors="replace").read())
    if m and os.path.exists(os.path.join(assemblies, m.group(1).strip() + ".dll")):
        available += 1
opened = len(re.findall(r"KQ3 card ", text))
if run and (opened != available or available == 0):
    problems.append(f"the round trip opened {opened} cards, {available} available")
for p in problems:
    print(f"   FAIL {label}: {p}")
if not problems:
    print(f"   PASS {label}: {opened} of {available} available cards round-tripped · our warnings: " + ("the positive control only" if positive else "none"))
sys.exit(1 if problems else 0)
EOF
}

for version in "${VERSIONS[@]}"; do
  echo "== samples · ${version}"
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
  project="${WORK}/samples-${version}"
  # the copy keeps the package's name in its path — compile messages show a file: package's physical path, which the
  # our-warnings filter matches
  pkg="${WORK}/package-${version}/com.xpturn.feedback"
  rm -rf "${out}" "${project}" "${WORK}/package-${version}"
  mkdir -p "${out}"

  # the package copy with the fixture bundle as a sample (and the positive-control warning in it)
  mkdir -p "${pkg}"
  rsync -a --exclude Tests~ "${PKG_DIR}/" "${pkg}/"
  mkdir -p "${pkg}/Samples~"
  rsync -a "${FIXTURES}/Fixture/" "${pkg}/Samples~/Fixture/"
  printf '#warning KQ9 positive control\n' > "${pkg}/Samples~/Fixture/Template/KQ9Positive.cs"
  python3 - "${pkg}/package.json" <<'EOF'
import json, sys
p = sys.argv[1]; m = json.load(open(p))
m["samples"] = m.get("samples", []) + [{"displayName": "Fixture", "description": "check-samples fixture bundle", "path": "Samples~/Fixture"}]
json.dump(m, open(p, "w"), indent=2)
EOF

  # a fresh clone of this version's dev project, without the fixtures' dev package
  cp -c -R "${DEV_PROJECT}" "${project}" 2>/dev/null || cp -R "${DEV_PROJECT}" "${project}"
  rm -rf "${project}/Temp" "${project}/Logs" "${project}/Packages/packages-lock.json"
  mkdir -p "${project}/Assets/SamplesCheck"
  cp -R "${DRIVER}" "${project}/Assets/SamplesCheck/"
  python3 - "${project}" "${pkg}" <<'EOF' || { fail "the clone"; continue; }
import json, os, sys
project, pkg = sys.argv[1:]
manifest_path = os.path.join(project, "Packages", "manifest.json")
manifest = json.load(open(manifest_path))
deps = manifest["dependencies"]
deps["com.xpturn.feedback"] = "file:" + pkg
deps.pop("com.xpturn.feedback.samples-fixtures", None)
# the dev package of the bundles (a link in Packages) — the clone imports them as a user does
link = os.path.join(project, "Packages", "com.xpturn.feedback.samples-dev")
if os.path.islink(link):
    os.remove(link)
manifest["testables"] = ["com.xpturn.feedback"]
json.dump(manifest, open(manifest_path, "w"), indent=2)
EOF

  editor "${out}/import.log" "${unity}" -batchmode -projectPath "${project}" -executeMethod SamplesCheck.SamplesDriver.Import
  grep -h '^SAMPLES ' "${out}/import.log" | sed 's/^/   /'
  before="$(settings "${project}")"
  editor "${out}/tests.log" "${unity}" -batchmode -projectPath "${project}" -runTests -testPlatform EditMode -testFilter "${FILTER}" -testResults "${out}/tests.xml"
  tests_verdict "imported" "${project}" "${out}/tests.xml" 1 "${out}/import.log" "${out}/tests.log" || failed=1
  [[ "$(settings "${project}")" == "${before}" ]] && pass "the settings files byte for byte as before" || fail "the settings files changed in the test run"

  if [[ ${NO_CM} == 1 ]]; then
    echo "-- no Cinemachine"
    branch="${WORK}/samples-nocm-${version}"
    rm -rf "${branch}"; cp -c -R "${project}" "${branch}"; rm -rf "${branch}/Temp" "${branch}/Packages/packages-lock.json"
    python3 - "${branch}/Packages/manifest.json" <<'EOF'
import json, sys
p = sys.argv[1]; m = json.load(open(p)); m["dependencies"].pop("com.unity.cinemachine", None); json.dump(m, open(p, "w"), indent=2)
EOF
    editor "${out}/nocm.log" "${unity}" -batchmode -projectPath "${branch}" -runTests -testPlatform EditMode -testFilter "${FILTER}" -testResults "${out}/nocm.xml"
    tests_verdict "no Cinemachine" "${branch}" "${out}/nocm.xml" 0 "${out}/nocm.log" || failed=1
  fi

  if [[ ${NO_TIMELINE} == 1 ]]; then
    echo "-- no Timeline"
    branch="${WORK}/samples-notl-${version}"
    rm -rf "${branch}"; cp -c -R "${project}" "${branch}"; rm -rf "${branch}/Temp" "${branch}/Packages/packages-lock.json"
    python3 - "${branch}/Packages/manifest.json" <<'EOF'
import json, sys
p = sys.argv[1]; m = json.load(open(p)); m["dependencies"].pop("com.unity.timeline", None); json.dump(m, open(p, "w"), indent=2)
EOF
    editor "${out}/notl.log" "${unity}" -batchmode -projectPath "${branch}" -runTests -testPlatform EditMode -testFilter "${FILTER}" -testResults "${out}/notl.xml"
    tests_verdict "no Timeline" "${branch}" "${out}/notl.xml" 0 "${out}/notl.log" || failed=1
    [[ ! -e "${branch}/Library/ScriptAssemblies/xpTURN.Feedback.Samples.Advanced.TimelineCutscene.dll" && ! -e "${branch}/Library/ScriptAssemblies/Unity.Timeline.dll" ]] \
      && pass "no Timeline: Timeline and S14's assembly absent" || fail "no Timeline: Timeline or S14's assembly still compiled"
  fi

  if [[ ${OLD_INPUT} == 1 ]]; then
    echo "-- old input only"
    branch="${WORK}/samples-old-${version}"
    rm -rf "${branch}"; cp -c -R "${project}" "${branch}"; rm -rf "${branch}/Temp"
    sed -i '' 's/^\(  activeInputHandler:\) [0-9]*/\1 0/' "${branch}/ProjectSettings/ProjectSettings.asset"
    editor "${out}/old.log" "${unity}" -batchmode -projectPath "${branch}" -runTests -testPlatform EditMode -testFilter 'SamplePlayTests\.(KQ15|KQ3)' -testResults "${out}/old.xml"
    python3 - "${out}/old.xml" <<'EOF' && pass "old input: the notice · the round trip · Console 0" || fail "old input"
import os, re, sys, xml.etree.ElementTree as ET
f = sys.argv[1]
run = re.search(r"<test-run [^>]*", open(f).read()) if os.path.exists(f) else None
c = {k: int(re.search(fr'\b{k}="(\d+)"', run.group(0)).group(1)) for k in ("total", "failed")} if run else None
suites = [f'{s.get("fullname")} @{s.get("site")}' for s in ET.parse(f).iter("test-suite")
          if s.get("result") == "Failed" and s.get("site") != "Child"] if run else []
print(f"   old input: {c}" + (f" · suite failed {suites[:5]}" if suites else ""))
sys.exit(0 if c and c["total"] == 2 and c["failed"] == 0 and not suites else 1)
EOF
  fi

  if [[ ${NO_TMP} == 1 ]]; then
    echo "-- no TMP Essential Resources"
    branch="${WORK}/samples-notmp-${version}"
    rm -rf "${branch}"; cp -c -R "${project}" "${branch}"; rm -rf "${branch}/Temp" "${branch}/Assets/TextMesh Pro" "${branch}/Assets/TextMesh Pro.meta"
    editor "${out}/notmp.log" "${unity}" -batchmode -projectPath "${branch}" -runTests -testPlatform EditMode -testFilter 'SamplePlayTests\.KQ3|BasicsTests\.BQ09a|LabsTests\.LQ06|UIJuiceTests\.JQ08|ShowcaseTests\.WQ11' -testResults "${out}/notmp.xml"
    python3 - "${out}/notmp.xml" <<'EOF' && pass "no TMP: the round trip · the gallery's TMP parts off · S4 · S7 · S13 without them · Console 0" || fail "no TMP"
import os, re, sys, xml.etree.ElementTree as ET
f = sys.argv[1]
run = re.search(r"<test-run [^>]*", open(f).read()) if os.path.exists(f) else None
c = {k: int(re.search(fr'\b{k}="(\d+)"', run.group(0)).group(1)) for k in ("total", "failed")} if run else None
suites = [f'{s.get("fullname")} @{s.get("site")}' for s in ET.parse(f).iter("test-suite")
          if s.get("result") == "Failed" and s.get("site") != "Child"] if run else []
print(f"   no TMP: {c}" + (f" · suite failed {suites[:5]}" if suites else ""))
sys.exit(0 if c and c["total"] == 5 and c["failed"] == 0 and not suites else 1)
EOF
  fi

  if [[ ${PLAYER} == 1 ]]; then
    # the oracle: the scenes of the available cards (their required assembly compiled)
    python3 - "${project}" > "${out}/cards.txt" <<'EOF'
import glob, os, re, sys
project = sys.argv[1]
for card in sorted(glob.glob(os.path.join(project, "Assets", "Samples", "**", "*.asset"), recursive=True)):
    text = open(card, errors="replace").read()
    m, n = re.search(r"RequiredType: [^,\n]*, ([^\n]+)", text), re.search(r"SceneName: ([^\n]+)", text)
    if m and n and os.path.exists(os.path.join(project, "Library", "ScriptAssemblies", m.group(1).strip() + ".dll")):
        # the scene's persistent calls: a method name with a target (an Object reference, not none) a few lines above
        scene = n.group(1).strip()
        paths = [p for p in glob.glob(os.path.join(project, "Assets", "Samples", "**", scene + ".unity"), recursive=True)]
        lines = open(paths[0], errors="replace").read().splitlines() if paths else []
        calls = 0
        for i, line in enumerate(lines):
            if re.match(r"\s*m_MethodName: \S", line):
                target = next((re.search(r"m_Target: \{fileID: (\d+)", l) for l in reversed(lines[max(0, i - 6):i]) if "m_Target:" in l), None)
                calls += 1 if target and target.group(1) != "0" else 0
        print(scene, calls)
EOF
    for kind in dev release il2cpp; do
      echo "-- player ${kind}"
      app="${WORK}/players/${version}-${kind}.app"
      rm -rf "${app}"; mkdir -p "${WORK}/players"
      SAMPLES_KIND=${kind} SAMPLES_OUT="${app}" editor "${out}/player-${kind}.log" "${unity}" -batchmode -projectPath "${project}" -executeMethod SamplesCheck.SamplesDriver.Player
      bin="$(ls "${app}/Contents/MacOS/" 2>/dev/null | head -1)"
      if [[ -z "${bin}" ]]; then
        fail "player ${kind}: no build"; continue
      fi
      caffeinate -u -d -t 300 & keep=$!
      perl -e 'alarm shift; exec @ARGV' 240 "${app}/Contents/MacOS/${bin}" -sampleTour -screen-fullscreen 0 -screen-width 640 -screen-height 360 -logFile "${out}/tour-${kind}.log"
      kill ${keep} 2>/dev/null
      python3 "${REPO_ROOT}/tools/samples/tour_verdict.py" "tour ${kind}" "${out}/tour-${kind}.log" "${kind}" "${out}/cards.txt" || failed=1
    done
  fi

  if [[ ${STRESS} == 1 ]]; then
    echo "-- stress table"
    app="${WORK}/players/${version}-il2cpp.app"
    if [[ ${PLAYER} != 1 || -z "$(ls "${app}/Contents/MacOS/" 2>/dev/null)" ]]; then
      rm -rf "${app}"; mkdir -p "${WORK}/players"
      SAMPLES_KIND=il2cpp SAMPLES_OUT="${app}" editor "${out}/player-stress.log" "${unity}" -batchmode -projectPath "${project}" -executeMethod SamplesCheck.SamplesDriver.Player
    fi
    bin="$(ls "${app}/Contents/MacOS/" 2>/dev/null | head -1)"
    if [[ -z "${bin}" ]]; then
      fail "stress table: no build"
    else
      caffeinate -u -d -t 660 & keep=$!
      perl -e 'alarm shift; exec @ARGV' 600 "${app}/Contents/MacOS/${bin}" -stressTable -screen-fullscreen 0 -screen-width 640 -screen-height 360 -logFile "${out}/stress.log"
      kill ${keep} 2>/dev/null
      python3 "${REPO_ROOT}/tools/samples/stress_table.py" "${version}=${out}/stress.log" || failed=1
    fi
  fi

  if [[ ${MINIMAL} == 1 ]]; then
    echo "-- minimal manifest"
    G10_WORK="${WORK}/g10" "${REPO_ROOT}/tools/check-minimal-manifest.sh" "${version}" | sed 's/^/   /'
    [[ ${PIPESTATUS[0]} == 0 ]] || failed=1
  fi
done

exit ${failed}
