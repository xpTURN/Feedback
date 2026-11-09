#!/usr/bin/env bash
#
# check-recompile-during-play.sh — a script recompile during play.
#
# For each Unity version that version's dev project (tests/FeedbackTests.<major.minor> — UniTask included; its editor version
# must be that version, otherwise the version fails) is cloned and refers to the package by its absolute path. The driver in
# tools/recompile-during-play/ is copied in and the editor
# runs NOT in batch mode (batch mode does not compile during play), so its window shows for a while. The driver enters
# play, adds an observer object, edits one script after 60 frames, runs 60 frames after the reload and exits play.
# Then the record is checked:
#   - "Script Changes While Playing" is Recompile And Continue Playing (else the tool stops — it never changes it)
#   - at the reload EditorApplication.isPlaying is true
#   - the observer's OnDisable before the reload sees a closed Default (beforeAssemblyReload)
#   - its OnEnable after the reload sees an open Default, and our three systems are in the loop
#   - in the 60 frames after the reload the new Default's frame number rises once a frame and the three fronts are ours
#   - at the exit OnApplicationQuit sees it open and OnDestroy closed (quitting before the cleanup)
# The players: eight, made at the first frame of play — all Awake-initialized but lz (Lazy).
# Then also:
#   - right after the reload no player has a runtime; after OnEnable the six Awake ones are prepared again on the new
#     Default (reserved 6 — det's object is gone) and the Lazy one waits for its next Play
#   - the OnEnable and OnStart auto plays play again · Restore() has nothing to put back · the Toggle cell is lost (Forward)
#   - the play that ran at the reload is cut with no event and stays there · the detached play (det's object destroyed at
#     frame 30) ended at the shutdown before the reload · the first frame is not written again over ini's play
# The receivers: rx (a FeedbackReceiver on its own Awake player — the reservation is 7 with it) and shk (a
# CameraShakeReceiver). Before the reload a send reaches rx; right after it neither is registered; after OnEnable each is on
# the new Default once and a send reaches rx again
# The listeners: four Lazy players (they add nothing to the reservation). Before the reload ls (a listener) and tk (a
# listener and a token): the shutdown before the reload ends both — ContextShutdown once each, the Default closed then. After
# the reload ex waits on Application.exitCancellationToken and ex2 has a listener only: at the exit the engine cancels the
# exit token before OnApplicationQuit — ex is Stopped with the Default open and the token's raw callback runs before
# OnApplicationQuit; ex2 ends at the shutdown after it (ContextShutdown, closed). The exit token's order is the engine's
# fact — each version is seen again (PlayAsync takes the token)
# The drivers: dl (a Loop MotionDriver — Oscillate on its own position) and df (a Follow one — SetTarget (5, 0, 0)), made at
# the first frame. Before the reload both run; right after it neither is prepared — dl's value is home and df's stays where
# it stood (written through the door), its SetTarget value lost; after OnEnable both run on the new Default (the reservation
# 9 with them, 10 with sp's below) from 0 s, df's target the value it stands at
# The pools: sp (an Awake player — the reservation 10 with it) with one pooled Spawn of a scene template (pool 3, lifetime
# 0), played twice at the first frame. Before the reload one container, 3 made, 2 live; the shutdown before the reload
# destroys them at a frame's end, so right after the reload they are still there and the first frame after it has the new
# Default's pool beside them (2 containers, 6 made); a spawn plays on the new Default; the next frame and 60 frames later one
# container, 3 made, 1 live; nothing is left after the exit
# The services: three Awake players (the reservation 13 with them) — a screen flash (the overlay canvas), a bloom (the
# overlay volume) and a rumble on a recording gamepad (this assembly's layout — the reload drops it). Their objects are made
# at the first play; the Default's close before the reload destroys them at once (none left right after the reload) and the
# rumble's close sends (0, 0), the Default closed then; a play after the reload makes them again; nothing is left after the
# exit. Cinemachine's service owns no object and its static fields go with the domain — not here
# The samples: a second editor session on the same clone (tools/recompile-during-play/Samples — SampleRecompileDriver) opens
# the sample fixtures' template scene, sets the HUD's Before and intensity 0.5 and recompiles during play: one HUD across the
# reload, its Tab heard on a recording keyboard, the samples' state and the categories start over (the new Default's
# settings), the Console list holds no warning and no error but the engine's QuickSearch index line, none left
# The tool's premises:
#   - the display must be on — a non-batch editor stops at frame 1 when it is off, the driver's timeout included: keep it
#     awake for the run (caffeinate -u -d)
#   - a killed run leaves Temp/, which stops the next non-batch run of the same project — this tool makes a new clone each run
#   - a clone's first run is slow (minutes); the next ones take tens of seconds
#
# Usage:
#   tools/check-recompile-during-play.sh                        # 6000.0.85f2, 6000.3.28f1 and 6000.6.8f1
#   tools/check-recompile-during-play.sh 6000.6.8f1             # one version
#   PT8_WORK=/path tools/check-recompile-during-play.sh         # where the cloned projects go
#   PT8_PACKAGE=/path tools/check-recompile-during-play.sh      # another copy of the package
#   PT8_TIMEOUT=600 tools/check-recompile-during-play.sh        # seconds before the editor is killed
#   UNITY_EDITORS=/path tools/check-recompile-during-play.sh    # folder holding <version>/Unity.app
#
# Exit code: non-zero if any version fails. Projects and logs are kept under the work folder.
set -uo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PKG_DIR="${PT8_PACKAGE:-${REPO_ROOT}/com.xpturn.feedback}"
DRIVER="${REPO_ROOT}/tools/recompile-during-play"
WORK="${PT8_WORK:-${TMPDIR:-/tmp}/feedback-pt8}"
TIMEOUT="${PT8_TIMEOUT:-600}"
EDITORS="${UNITY_EDITORS:-/Applications/Unity/Hub/Editor}"

case "${1:-}" in -h|--help) sed -n '2,/^set /p' "${BASH_SOURCE[0]}"; exit 0 ;; esac
VERSIONS=("$@")
[[ ${#VERSIONS[@]} -gt 0 ]] || VERSIONS=(6000.0.85f2 6000.3.28f1 6000.6.8f1)

failed=0
for version in "${VERSIONS[@]}"; do
  echo "== recompile during play · ${version}"
  contents="${EDITORS}/${version}/Unity.app/Contents"
  unity="${contents}/MacOS/Unity"
  if [[ ! -x "${unity}" ]]; then
    echo "   FAIL Unity not found: ${unity}"; failed=1; continue
  fi
  DEV_PROJECT="${REPO_ROOT}/tests/FeedbackTests.${version%.*}"
  dev_version="$(sed -n 's/^m_EditorVersion: //p' "${DEV_PROJECT}/ProjectSettings/ProjectVersion.txt" 2>/dev/null)"
  if [[ "${dev_version}" != "${version}" ]]; then
    echo "   FAIL no dev project for ${version} — tests/FeedbackTests.${version%.*} is ${dev_version:-missing}"; failed=1; continue
  fi
  echo "   dev project tests/FeedbackTests.${version%.*} (${dev_version})"

  project="${WORK}/recompile-${version}"
  rm -rf "${project}"
  mkdir -p "${WORK}"
  cp -c -R "${DEV_PROJECT}" "${project}" 2>/dev/null || cp -R "${DEV_PROJECT}" "${project}"
  rm -rf "${project}/Temp" "${project}/Logs"
  mkdir -p "${project}/Assets/RecompileCheck"
  cp -R "${DRIVER}/." "${project}/Assets/RecompileCheck/"

  python3 - "${project}/Packages/manifest.json" "${PKG_DIR}" "${DEV_PROJECT}/Packages" <<'EOF' || { failed=1; continue; }
import json, os, sys

manifest_path, pkg_dir, dev_packages = sys.argv[1:]
manifest = json.load(open(manifest_path))
deps = manifest["dependencies"]
deps["com.xpturn.feedback"] = "file:" + pkg_dir
# the dev project's other relative file: packages (the sample fixtures) and its relative package links (the samples) by their
# absolute path — the clone is away from the repo
for name, value in deps.items():
    if name != "com.xpturn.feedback" and value.startswith("file:") and not os.path.isabs(value[5:]):
        deps[name] = "file:" + os.path.normpath(os.path.join(dev_packages, value[5:]))
packages = os.path.dirname(manifest_path)
for entry in os.listdir(packages):
    link = os.path.join(packages, entry)
    if os.path.islink(link) and not os.path.isabs(os.readlink(link)):
        target = os.path.normpath(os.path.join(dev_packages, os.readlink(link)))
        os.remove(link)
        os.symlink(target, link)
with open(manifest_path, "w") as f:
    json.dump(manifest, f, indent=2)
EOF

  start=$(date +%s)
  perl -e 'alarm shift; exec @ARGV' "${TIMEOUT}" "${unity}" -projectPath "${project}" \
    -executeMethod RecompileCheck.RecompileDriver.Run -logFile "${project}/editor.log"
  code=$?
  echo "   editor exit ${code} after $(( $(date +%s) - start )) s"

  python3 - "${project}/RecompileCheck.txt" "${code}" <<'EOF' || failed=1
import os, re, sys

record_path, code = sys.argv[1], int(sys.argv[2])
OURS = "FeedbackRevert/FeedbackUpdate/FeedbackLateApply"
if not os.path.exists(record_path):
    print("   FAIL no record: " + record_path)
    sys.exit(1)
events = []
for line in open(record_path).read().splitlines():
    name, _, rest = line.partition(" ")
    events.append((name, dict(re.findall(r"(\w+)=(\S+)", rest)), line))

def index(name, after=-1):
    return next((i for i, (n, _, _) in enumerate(events) if n == name and i > after), None)

def last(name, before):
    return next((i for i in range(before - 1, -1, -1) if events[i][0] == name), None)

problems = []
def check(ok, message):
    if not ok:
        problems.append(message)

start = index("start")
if start is None:
    print("   FAIL the driver did not start — see editor.log")
    sys.exit(1)
if events[start][1].get("scriptChangesWhilePlaying") != "0":
    print("   STOP Preferences > General > Script Changes While Playing is not 'Recompile And Continue Playing' "
          f"({events[start][1].get('scriptChangesWhilePlaying')}) — the tool does not change it")
    sys.exit(1)
problems += [line for name, _, line in events if name == "stop:"]
check(code == 0, f"editor exit code {code}")
check(index("done") is not None, "the driver did not finish")

edit = index("before-edit")
reload = index("reload", edit if edit is not None else -1)
check(edit is not None and events[edit][1].get("default") == "open" and events[edit][1].get("ours") == "3",
      "before the edit the stages are not running: " + (events[edit][2] if edit is not None else "no line"))
check(reload is not None and events[reload][1].get("isPlaying") == "True",
      "at the reload EditorApplication.isPlaying is not true: " + (events[reload][2] if reload is not None else "no reload"))
if reload is not None:
    disable = last("observer-disable", reload)
    check(disable is not None and edit is not None and disable > edit and events[disable][1].get("default") == "closed",
          "OnDisable before the reload: " + (events[disable][2] if disable is not None else "none"))
    enable = index("observer-enable", reload)
    check(enable is not None and events[enable][1].get("default") == "open",
          "OnEnable after the reload: " + (events[enable][2] if enable is not None else "none"))
    after = index("after-reload", reload)
    check(after is not None and events[after][1].get("ours") == "3",
          "our systems after the reload: " + (events[after][2] if after is not None else "none"))
    frames = index("after-frames", reload)
    if frames is None:
        problems.append("no line 60 frames after the reload")
    else:
        f = events[frames][1]
        check(int(f.get("frames", 0)) >= 60 and f.get("stages") == f.get("frames"),
              f"the stages after the reload: {f.get('stages')} in {f.get('frames')} frames")
        check(f.get("ours") == "3" and f.get("fronts") == OURS, "the fronts 60 frames after the reload: " + events[frames][2])
        quit_ = index("observer-quit", frames)
        destroy = index("observer-destroy", frames)
        check(quit_ is not None and events[quit_][1].get("default") == "open",
              "OnApplicationQuit at the exit: " + (events[quit_][2] if quit_ is not None else "none"))
        check(destroy is not None and quit_ is not None and destroy > quit_ and events[destroy][1].get("default") == "closed",
              "OnDestroy at the exit: " + (events[destroy][2] if destroy is not None else "none"))

# the players
check(index("players-made") is not None, "the players were not made")
if reload is not None:
    check(events[reload][1].get("runtimes") == "0", "right after the reload a player has a runtime: " + events[reload][2])
    back = index("after-reload", reload)
    acts = index("after-actions", reload)
    later = index("after-frames", reload)
    gone = last("observer-disable", reload)
    if back is not None:
        a = events[back][1]
        check(a.get("reserved") == "13", "the players' reservation after the reload: " + str(a.get("reserved")) + " (13 — the Awake ones, rx's, sp's, the two drivers and the three services)")
        for name in ("cont", "en", "st", "res", "ini", "tog"):
            check(a.get(name, "").startswith("r"), name + " is not prepared again after the reload: " + a.get(name, "none"))
        check(a.get("lz", "").startswith("_"), "lz (Lazy) is prepared before its Play: " + a.get("lz", "none"))
        check(a.get("en", "")[1:2] == "p" and a.get("st", "")[1:2] == "p", "an auto play does not play again: en " + a.get("en", "none") + " · st " + a.get("st", "none"))
        check(a.get("det", "").startswith("-"), "det's object: " + a.get("det", "none"))
    check(acts is not None and events[acts][1].get("res") == "10,0" and events[acts][1].get("tog") == "F" and events[acts][1].get("lz") == "01",
          "after the reload — Restore · Toggle · Lazy: " + (events[acts][2] if acts is not None else "none"))
    if back is not None and later is not None:
        a, f = events[back][1], events[later][1]
        for name in ("cont", "det"):
            check(a.get(name, "a").split("@")[-1] == f.get(name, "f").split("@")[-1] and f.get(name, "")[1:2] != "p",
                  name + " moved after the reload: " + a.get(name, "none") + " → " + f.get(name, "none"))
        ini = a.get("ini", "").split("@")[-1]
        check(gone is not None and ini == events[gone][1].get("ini") and ini != "0,5",
              "ini's first frame written again: " + ini + " · before the reload " + (events[gone][1].get("ini", "none") if gone is not None else "none"))

# the receivers
made = index("receivers-made")
check(made is not None and events[made][1].get("rxCount") == "1" and events[made][1].get("shkCount") == "1" and events[made][1].get("send") == "1"
      and events[made][1].get("rxPlaying") == "True", "the receivers before the reload: " + (events[made][2] if made is not None else "none"))
if reload is not None:
    gone_ = index("receivers-reload", reload - 1)
    check(gone_ is not None and events[gone_][1].get("rxReg") == "False" and events[gone_][1].get("shkLive") == "False"
          and events[gone_][1].get("rxCount") in ("0", "-") and events[gone_][1].get("shkCount") in ("0", "-"),
          "the receivers right after the reload: " + (events[gone_][2] if gone_ is not None else "none"))
    again = index("receivers-after", reload)
    check(again is not None and events[again][1].get("rxCount") == "1" and events[again][1].get("shkCount") == "1" and events[again][1].get("rxReg") == "True"
          and events[again][1].get("shkLive") == "True" and events[again][1].get("send") == "1" and events[again][1].get("rxPlaying") == "True",
          "the receivers after the reload: " + (events[again][2] if again is not None else "none"))

# the listeners
def listener(name):
    return [i for i, (n, a, _) in enumerate(events) if n == "listener" and a.get("name") == name]

made_l = index("listeners-made")
check(made_l is not None and events[made_l][1].get("ls") == "True" and events[made_l][1].get("tk") == "True",
      "the listeners' plays before the reload: " + (events[made_l][2] if made_l is not None else "none"))
for name in ("ls", "tk"):
    hits = listener(name)
    check(len(hits) == 1 and reload is not None and hits[0] < reload and events[hits[0]][1].get("end") == "ContextShutdown"
          and events[hits[0]][1].get("default") == "closed",
          name + "'s listener — once, before the reload, ContextShutdown, closed: " + " | ".join(events[i][2] for i in hits))
plays = index("exit-plays")
check(plays is not None and events[plays][1].get("ex") == "True" and events[plays][1].get("ex2") == "True" and events[plays][1].get("exitToken") == "True",
      "the exit plays after the reload: " + (events[plays][2] if plays is not None else "none"))
quit_l = index("observer-quit", plays if plays is not None else -1)
raw = index("exit-token", plays if plays is not None else -1)
ex, ex2 = listener("ex"), listener("ex2")
check(len(ex) == 1 and quit_l is not None and ex[0] < quit_l and events[ex[0]][1].get("end") == "Stopped" and events[ex[0]][1].get("default") == "open",
      "ex's listener — once, before OnApplicationQuit, Stopped, open: " + " | ".join(events[i][2] for i in ex))
check(raw is not None and quit_l is not None and raw < quit_l and events[raw][1].get("default") == "open",
      "the exit token's raw callback before OnApplicationQuit: " + (events[raw][2] if raw is not None else "none"))
check(len(ex2) == 1 and quit_l is not None and ex2[0] > quit_l and events[ex2[0]][1].get("end") == "ContextShutdown" and events[ex2[0]][1].get("default") == "closed",
      "ex2's listener — once, after OnApplicationQuit, ContextShutdown, closed: " + " | ".join(events[i][2] for i in ex2))

# the drivers: name=r(unning)p(repared)@seconds:x (df also :t<target x>)
def driver(i, name):
    v = events[i][1].get(name, "-") if i is not None else "-"
    m = re.fullmatch(r"([r_])([p_])@([-\d.]+):([-\d.]+)(?::t([-\d.]+))?", v)
    return m.groups() if m else None

def driver_line(i):
    return events[i][2] if i is not None else "none"

# df's spring reads 4.999 when 60 frames run in under 0.94 s (6000.0): its position within 0.01 of 5
def near5(x):
    return abs(float(x) - 5) < 0.01

made_d = index("drivers-made")
before_d = index("drivers-before")
dl, df = driver(before_d, "dl"), driver(before_d, "df")
check(made_d is not None and dl is not None and df is not None and dl[:2] == ("r", "p") and df[:2] == ("r", "p")
      and float(dl[2]) > 0 and near5(df[3]) and df[4] == "5",
      "the drivers before the reload — both run, df at its target: " + driver_line(before_d))
if reload is not None:
    gone_d = index("drivers-reload", reload - 1)
    dl, df = driver(gone_d, "dl"), driver(gone_d, "df")
    check(dl is not None and df is not None and dl[:2] == ("_", "_") and df[:2] == ("_", "_") and dl[3] == "0" and near5(df[3]) and df[4] == "0",
          "the drivers right after the reload — not prepared, dl home, df where it stood, its target lost: " + driver_line(gone_d))
    again_d = index("drivers-after", reload)
    dl, df = driver(again_d, "dl"), driver(again_d, "df")
    check(dl is not None and df is not None and dl[:2] == ("r", "p") and df[:2] == ("r", "p") and dl[2] == "0" and df[2] == "0"
          and df[4] == df[3],
          "the drivers after OnEnable — both run on the new Default from 0 s, df's target the value it stands at: " + driver_line(again_d))
    later_d = index("drivers-frames", reload)
    dl, df = driver(later_d, "dl"), driver(later_d, "df")
    check(dl is not None and df is not None and dl[:2] == ("r", "p") and df[:2] == ("r", "p") and float(dl[2]) > 0 and near5(df[3]) and near5(df[4]),
          "the drivers 60 frames after the reload — dl goes on, df stands: " + driver_line(later_d))

# the pools: containers · clones · active ones of sp's pool
def pools(name, after=-1):
    i = index(name, after)
    return (events[i][1], events[i][2]) if i is not None else ({}, name + " none")

def counts(a):
    return tuple(a.get(k) for k in ("containers", "clones", "active"))

a, line = pools("pools-made")
check(a.get("plays") == "True,True" and counts(a) == ("1", "3", "2"), "the pool before the reload — one container, 3 made, 2 live: " + line)
if reload is not None:
    a, line = pools("pools-reload", reload - 1)
    check(counts(a)[:2] == ("1", "3"), "right after the reload — the close's Destroy waits for a frame's end: " + line)
    a, line = pools("pools-after", reload)
    check(counts(a) == ("2", "6", "2"), "the first frame after the reload — the new Default's pool made, the old one not yet gone: " + line)
    a, line = pools("pools-respawn", reload)
    check(a.get("play") == "True" and counts(a)[2] == "3", "a spawn on the new Default: " + line)
    a, line = pools("pools-next", reload)
    check(counts(a) == ("1", "3", "1"), "the next frame — the old pool gone: " + line)
    a, line = pools("pools-frames", reload)
    check(counts(a) == ("1", "3", "1"), "60 frames after the reload: " + line)
a, line = pools("pools-exited")
check(counts(a) == ("0", "0", "0"), "after the exit — nothing left: " + line)

# the extension services — the overlay canvas, the overlay volume and the rumble
def live(a):
    return int(a.get("overlay", "0")) > 0 and int(a.get("postfx", "0")) > 0
a, line = pools("services-made")
check(a.get("plays") == "True,True,True", "the services' plays before the reload: " + line)
a, line = pools("services-before")
check(live(a) and a.get("last", "-") not in ("-", "0.00,0.00"), "60 frames in — the services live, the pad driven: " + line)
if reload is not None:
    a, line = pools("services-reload", reload - 1)
    check(a.get("overlay") == "0" and a.get("postfx") == "0", "right after the reload — the close destroyed the services' objects at once: " + line)
    zero = next((events[i][2] for i in range(index("services-before") or 0, reload) if events[i][0] == "rumble-zero"), None)
    check(zero is not None, "the rumble's close sent (0, 0) before the reload: " + (zero or "no rumble-zero line"))
    a, line = pools("services-after", reload)
    check(a.get("plays") == "True,True", "a play after the reload: " + line)
    a, line = pools("services-next", reload)
    check(live(a), "the next frame — the new Default's services made: " + line)
a, line = pools("services-exited")
check(a.get("overlay") == "0" and a.get("postfx") == "0", "after the exit — no service object left: " + line)

for p in problems:
    print("   FAIL " + p)
if problems:
    print("   record: " + record_path)
    sys.exit(1)
f = events[index("after-frames")][1]
print(f"   PASS reload while playing · OnDisable closed / OnEnable open · stages {f['stages']} in {f['frames']} frames · fronts ours · quit open / destroy closed · players, receivers, listeners and drivers as expected · the spawn pool gone a frame after the reload · the services' objects gone at the reload, (0, 0) at the close")
EOF

  # the samples: a second session on this clone recompiles during play in the sample fixtures' template scene
  # (SampleRecompileDriver)
  echo "-- samples"
  rm -f "${project}/SampleRecompileCheck.txt"
  start=$(date +%s)
  perl -e 'alarm shift; exec @ARGV' "${TIMEOUT}" "${unity}" -projectPath "${project}" \
    -executeMethod RecompileCheck.SampleRecompileDriver.Run -logFile "${project}/editor-samples.log"
  code=$?
  echo "   editor exit ${code} after $(( $(date +%s) - start )) s"
  python3 - "${project}/SampleRecompileCheck.txt" "${code}" <<'EOF' || failed=1
import os, re, sys
path, code = sys.argv[1], int(sys.argv[2])
lines = open(path).read().splitlines() if os.path.exists(path) else []
def find(prefix):
    return next((l for l in lines if l.startswith(prefix)), "")
problems = []
if code != 0:
    problems.append(f"editor exit code {code}")
if find("option") != "option 0":
    problems.append("'Script Changes While Playing' is not Recompile And Continue Playing: " + find("option"))
if find("reloaded") != "reloaded playing=True":
    problems.append("no reload while playing: " + find("reloaded"))
before, first, tab, after = find("before "), find("first "), find("tab "), find("after ")
if "after=False" not in before or "intensity=0.5" not in before:
    problems.append("the state before the reload: " + before)
for k in ("huds=1", "instance=True", "gc=True", "actions=True", "afterEnabled=True", "after=True", "intensity=1", "camera=True", "cameraScale=1"):
    if k not in first:
        problems.append(f"right after the reload — {k}: " + first)
for k in ("huds=1", "instance=True", "after=False", "camera=False", "cameraScale=1"):
    if k not in tab:
        problems.append(f"the HUD's Tab after the reload — {k}: " + tab)
m = re.search(r"errors=(\d+) warnings=(\d+) engine=(\d+)", after)
if not m or int(m.group(2)) != 0 or int(m.group(1)) > int(m.group(3)):
    problems.append("the Console list: " + after)
if find("exit ") != "exit huds=0":
    problems.append("after the exit: " + find("exit "))
for p in problems:
    print("   FAIL samples " + p)
if problems:
    print("   record: " + path)
    sys.exit(1)
print("   PASS samples · a HUD across the reload · its Tab heard · the samples' state and the categories start over · the Console list holds the engine's QuickSearch line at most")
EOF
done

exit "${failed}"
