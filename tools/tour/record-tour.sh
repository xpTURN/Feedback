#!/usr/bin/env bash
#
# record-tour.sh — a video of the samples' feature tour, from the dev project's open editor through OBS.
#
# The editor that has the dev project (tests/FeedbackTests.6000.3) open runs it: the tour's scripts (tools/tour/Unity) are copied
# into its Assets/PRTour, the editor is brought to the front to compile them, opens the hub and enters play mode with
# Play Maximized as it is set. The Game view's render area on screen (rect.txt) gives the capture's crop; OBS records the
# program scene while the tour opens the cards one by one (keys through a virtual keyboard, Esc back to the hub) — about
# 4.5 minutes. Then the editor leaves play mode, the scripts and Temp/PRTour are removed, and the editor compiles again.
#   - the video: OBS's recording folder and format · the canvas as OBS has it (1920 × 1080 for a 1:1 Game view at 1920 × 1080)
#   - chapters.txt (beside the logs): the segments' start times, for a description
#   - every OBS audio input is muted during the recording and put back after
#
# Needs: the dev project open in its editor (no unsaved scene — the tour opens the hub), OBS running with obs-websocket on,
# python3 with obsws-python (pip install obsws-python), ffmpeg (the crop's check). macOS Screen Recording permission for OBS.
# The Game view as it should be recorded (its resolution · scale · Play Maximized) — the crop follows it.
#
# Usage:
#   tools/tour/record-tour.sh                  # record
#   tools/tour/record-tour.sh --rehearse       # the tour without OBS
#   TOUR_WORK=/path tools/tour/record-tour.sh  # where rect.txt · log · chapters · the crop's screenshot go
#   TOUR_TIMEOUT=600 tools/tour/record-tour.sh # the outer limit of the tour (s)
#   OBS_WEBSOCKET_URL=obsws://localhost:4455/<password> tools/tour/record-tour.sh
#   TOUR_CROP_Y_FIX=49                         # rows (px) added to the computed top — measured on a 2× display; the
#                                              # screenshot check corrects the crop when the editor shows above or below
#   UNITY_EDITORS=/path tools/tour/record-tour.sh
#
# Exit code: 0 when the tour ran to its end (and, recording, OBS gave a file), else 1.
set -uo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
TOOL="${REPO_ROOT}/tools/tour"
DEV_PROJECT="${REPO_ROOT}/tests/FeedbackTests.6000.3"
WORK="${TOUR_WORK:-${TMPDIR:-/tmp}/feedback-tour}"
TIMEOUT="${TOUR_TIMEOUT:-600}"
EDITORS="${UNITY_EDITORS:-/Applications/Unity/Hub/Editor}"
T="${DEV_PROJECT}/Temp/PRTour"

case "${1:-}" in -h|--help) sed -n '2,/^set /p' "${BASH_SOURCE[0]}"; exit 0 ;; esac
REHEARSE=0
[[ "${1:-}" == "--rehearse" ]] && REHEARSE=1
VERSION="$(sed -n 's/^m_EditorVersion: //p' "${DEV_PROJECT}/ProjectSettings/ProjectVersion.txt")"
APP="${EDITORS}/${VERSION}/Unity.app"
mkdir -p "${WORK}"

fail() { echo "FAIL $*"; exit 1; }
obs_tour() { python3 "${TOOL}/obs_tour.py" "$@"; }
front() { open -a "${APP}"; }
refocus() { open -a Finder; sleep 1; open -a "${APP}"; }
wait_for() { # <seconds> <test …>
  local n="$1"; shift
  for ((i = 0; i < n; i++)); do "$@" && return 0; [[ -f "${T}/error" ]] && return 1; sleep 1; done
  return 1
}

pgrep -if -- "-projectPath ${DEV_PROJECT}" >/dev/null || fail "no Unity editor has ${DEV_PROJECT} open — open it first"
if [[ ${REHEARSE} == 0 ]]; then
  pgrep -x OBS >/dev/null || fail "OBS is not running"
  python3 -c 'import obsws_python' 2>/dev/null || fail "python3 lacks obsws-python (pip install obsws-python)"
fi

recording=0
cleanup() {
  if [[ ${recording} == 1 ]]; then
    obs_tour stop >/dev/null 2>&1
  fi
  [[ ${REHEARSE} == 0 ]] && obs_tour unmute "${WORK}/mute.json" >/dev/null 2>&1
  if [[ -d "${T}" ]]; then
    touch "${T}/stop"
    wait_for 30 test ! -f "${T}/active" || true
    cp -f "${T}/log.txt" "${WORK}/log.txt" 2>/dev/null
  fi
  rm -rf "${DEV_PROJECT}/Assets/PRTour" "${DEV_PROJECT}/Assets/PRTour.meta"
  refocus
  sleep 5
  rm -rf "${T}"
}
trap cleanup EXIT

# the scripts in, compiled (a focus change makes the editor refresh)
rm -rf "${T}" "${WORK}/log.txt" "${WORK}/chapters.txt"
mkdir -p "${DEV_PROJECT}/Assets/PRTour"
cp -R "${TOOL}/Unity/." "${DEV_PROJECT}/Assets/PRTour/"
refocus
wait_for 240 test -f "${T}/ready" || fail "the editor did not load the tour's scripts (compile errors? see the Console)"

# out of play mode if it is in it, then the hub in play mode
touch "${T}/stop"
sleep 3
front
touch "${T}/go"
if ! wait_for 120 test -f "${T}/waiting"; then
  [[ -f "${T}/error" ]] && fail "$(cat "${T}/error")"
  fail "the tour did not start"
fi
wait_for 30 test -f "${T}/rect.txt" || fail "no Game view rect"
cp "${T}/rect.txt" "${WORK}/rect.txt"

if [[ ${REHEARSE} == 0 ]]; then
  obs_tour source || fail "the OBS source"
  obs_tour crop "${WORK}/rect.txt" "${WORK}/crop-check.png" || fail "the crop"
  obs_tour mute "${WORK}/mute.json" || fail "muting"
  obs_tour start || fail "the recording did not start"
  recording=1
fi
front
touch "${T}/rec"
wait_for "${TIMEOUT}" test -f "${T}/done" || fail "the tour did not end in ${TIMEOUT} s (log: ${T}/log.txt)"
sleep 1
video=""
if [[ ${REHEARSE} == 0 ]]; then
  video="$(obs_tour stop)"
  recording=0
fi
cp -f "${T}/log.txt" "${WORK}/log.txt"
python3 - "${WORK}/log.txt" > "${WORK}/chapters.txt" <<'EOF'
import sys
for line in open(sys.argv[1]):
    t, _, what = line.strip().partition(" ")
    if what.startswith("open "):
        s = float(t)
        print(f"{int(s // 60)}:{int(s % 60):02d} {what[5:]}")
EOF
grep -h "^[0-9.]* error\|no card" "${WORK}/log.txt" && echo "WARN some segments failed — see ${WORK}/log.txt"
echo "chapters (${WORK}/chapters.txt):"
sed 's/^/   /' "${WORK}/chapters.txt"
[[ -n "${video}" ]] && echo "video: ${video}"
exit 0
