#!/usr/bin/env bash
#
# doc-images.sh — the documents' screenshots (com.xpturn.feedback/Documentation~/images), taken from the editor itself.
# The shot list is tools/doc-images/shots.json: an id, the image's file and the state the driver makes.
#   - the package and the dev project (tests/FeedbackTests.6000.3 — its Unity version takes the shots) are copied to the work
#     folder (APFS copies; the dev project's Library too unless an editor has it open) and an editor runs there NOT in batch
#     mode, so its windows show for a few minutes: leave them alone — a pointer over a window can show in its image
#   - the driver (xpTURN.Feedback.Tests.Editor.Docs.DocImages, in the package's editor tests) makes each shot's state —
#     a scene, a selection, a floating window at a fixed place, a preview or a play paused — and checks it; a shot whose
#     check fails fails alone. tools/doc-images/capture.py captures the window with screencapture -l (macOS) until two
#     captures in a row are the same and keeps the shot's part of it
#   - tools/doc-images/post.py makes each image: one pixel per point, 64 colors (a GIF: 128 colors, one palette), and
#     compares it with the image there: the same within the threshold is left as it is, a new or changed one is written
#   - the package's editor preferences (xpTURN.Feedback.*) are at their defaults while the shots are taken and come back
#     at the end; the editor preferences this run changed (the last project, the floating windows' places, the session)
#     are put back from a copy taken before — all of them when no other Unity editor ran meanwhile, else only those that
#     name the copy. A run that did not finish leaves that copy in the work folder: the next run first puts back the keys
#     that still name the copy, and the driver takes the package's keys from the backup it left (the tool leaves those
#     keys to it then)
#   - the editor's skin and version are recorded (tools/doc-images/record.txt); --check refuses another skin or version,
#     whose images would all differ
#   - the display must be on (caffeinate keeps it awake); the app running this needs no Screen Recording permission for an
#     editor it started — if screencapture is refused, the shot says so
#
# Usage:
#   tools/doc-images.sh                    # take every shot, write the images that changed
#   tools/doc-images.sh --check            # take every shot, write nothing — exit 1 when an image no longer matches
#   tools/doc-images.sh --only R2 E3       # these shots only (with or without --check)
#   DOC_IMAGES_WORK=/path tools/doc-images.sh      # where the copies, the captures and the logs go
#   DOC_IMAGES_PACKAGE=/path tools/doc-images.sh   # another copy of the package
#   DOC_IMAGES_COVER=1 tools/doc-images.sh         # another window over each window while it is captured (not a menu)
#   DOC_IMAGES_TIMEOUT=1800 tools/doc-images.sh    # seconds before the editor is killed
#   UNITY_EDITORS=/path tools/doc-images.sh        # folder holding <version>/Unity.app
#
# Exit code: 0 when every shot was taken (and, with --check, every image matches), else 1.
set -uo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PKG="com.xpturn.feedback"
PKG_DIR="${DOC_IMAGES_PACKAGE:-${REPO_ROOT}/${PKG}}"
DEV="tests/FeedbackTests.6000.3"
TOOL="${REPO_ROOT}/tools/doc-images"
IMAGES="${REPO_ROOT}/${PKG}/Documentation~/images"
WORK="${DOC_IMAGES_WORK:-${TMPDIR:-/tmp}/feedback-doc-images}"
TIMEOUT="${DOC_IMAGES_TIMEOUT:-1800}"
EDITORS="${UNITY_EDITORS:-/Applications/Unity/Hub/Editor}"
DOMAIN="com.unity3d.UnityEditor5.x"

case "${1:-}" in -h|--help) sed -n '2,/^set /p' "${BASH_SOURCE[0]}"; exit 0 ;; esac
CHECK=0
ONLY=()
while [[ $# -gt 0 ]]; do
  case "$1" in
    --check) CHECK=1 ;;
    --only) shift; while [[ $# -gt 0 && "$1" != --* ]]; do ONLY+=("$1"); shift; done; continue ;;
    *) echo "unknown argument: $1"; exit 1 ;;
  esac
  shift
done

if [[ "$(uname)" != "Darwin" ]] || ! command -v screencapture >/dev/null; then
  echo "FAIL the shots are taken with macOS screencapture"; exit 1
fi
if ! python3 -c "import PIL" 2>/dev/null; then
  echo "FAIL python3 has no Pillow (pip3 install pillow)"; exit 1
fi
VERSION="$(sed -n 's/^m_EditorVersion: //p' "${REPO_ROOT}/${DEV}/ProjectSettings/ProjectVersion.txt")"
UNITY="${EDITORS}/${VERSION}/Unity.app/Contents/MacOS/Unity"
if [[ ! -x "${UNITY}" ]]; then
  echo "FAIL the dev project's Unity ${VERSION} is not installed: ${UNITY}"; exit 1
fi

mkdir -p "${WORK}/bin" "${WORK}/state"
WINDOWS="${WORK}/bin/windows-$(shasum "${TOOL}/windows.swift" | cut -c1-12)"
if [[ ! -x "${WINDOWS}" ]]; then
  xcrun swiftc -O -o "${WINDOWS}" "${TOOL}/windows.swift" || { echo "FAIL the window lister did not build (xcrun swiftc)"; exit 1; }
fi

# the copies — the package, the dev project and what its manifest reaches by relative path
CLONE="${WORK}/repo"
OUT="${WORK}/out"
rm -rf "${CLONE}" "${OUT}"
mkdir -p "${CLONE}/tests" "${OUT}"
cp -c -R "${PKG_DIR}" "${CLONE}/${PKG}"
cp -c -R "${REPO_ROOT}/tests/SampleFixtures" "${CLONE}/tests/SampleFixtures"
project="${CLONE}/${DEV}"
if lsof -t "${REPO_ROOT}/${DEV}/Temp/UnityLockfile" >/dev/null 2>&1; then
  echo "   the dev project is open in an editor — copied without its Library (the first import takes minutes)"
  mkdir -p "${project}"
  for entry in "${REPO_ROOT}/${DEV}"/*; do
    [[ "$(basename "${entry}")" == Library ]] || cp -c -R "${entry}" "${project}/"
  done
else
  cp -c -R "${REPO_ROOT}/${DEV}" "${project}"
fi
rm -rf "${project}/Temp" "${project}/Logs"

# the editor preferences: a run that did not finish first, then before this run — and the Unity editors already running
STATE="${WORK}/state"
if [[ -f "${STATE}/prefs-before.plist" ]]; then
  echo "   the last run did not finish — its editor preferences that name its copy are put back first"
  defaults export "${DOMAIN}" "${OUT}/prefs-left.plist"
  python3 "${TOOL}/post.py" prefs "${STATE}/prefs-before.plist" "${OUT}/prefs-left.plist" "${CLONE}" 1 --recover
  rm -f "${STATE}/prefs-before.plist"
fi
defaults export "${DOMAIN}" "${OUT}/prefs-before.plist"
cp "${OUT}/prefs-before.plist" "${STATE}/prefs-before.plist"
recovering=()
if [[ -f "${STATE}/prefs-backup.txt" ]]; then
  echo "   the driver takes the package's preferences from the backup the last run left"
  recovering=(--driver-recovers)
fi
others=0
if pgrep -f "Unity.app/Contents/MacOS/Unity" >/dev/null; then
  others=1
fi

echo "== doc images · Unity ${VERSION} · ${#ONLY[@]} shot(s) asked ($([[ ${#ONLY[@]} -gt 0 ]] && echo "${ONLY[*]}" || echo all))"
echo "   an editor opens for a few minutes — leave its windows alone"
start=$(date +%s)
DOCIMG_OUT="${OUT}" DOCIMG_STATE="${STATE}" DOCIMG_SHOTS="${TOOL}/shots.json" DOCIMG_ONLY="${ONLY[*]:-}" \
  DOCIMG_COVER="${DOC_IMAGES_COVER:-0}" \
  caffeinate -u -d perl -e 'alarm shift; exec @ARGV' "${TIMEOUT}" "${UNITY}" -projectPath "${project}" \
  -executeMethod xpTURN.Feedback.Tests.Editor.Docs.DocImages.Run -logFile "${OUT}/editor.log" &
launcher=$!
python3 "${TOOL}/capture.py" "${OUT}" "${launcher}" "${WINDOWS}" &
watcher=$!
wait "${launcher}"; code=$?
wait "${watcher}"
echo "   editor exit ${code} after $(( $(date +%s) - start )) s"
[[ -s "${OUT}/others.txt" ]] && others=1

defaults export "${DOMAIN}" "${OUT}/prefs-after.plist"
failed=0
python3 "${TOOL}/post.py" prefs "${OUT}/prefs-before.plist" "${OUT}/prefs-after.plist" "${CLONE}" "${others}" ${recovering[@]+"${recovering[@]}"} || failed=1
rm -f "${STATE}/prefs-before.plist"

if [[ ! -f "${OUT}/end.txt" ]]; then
  echo "   FAIL the driver did not finish — ${OUT}/driver.log · ${OUT}/editor.log"; failed=1
fi
skin="$(sed -n 's/.* pro True .*/dark/p; s/.* pro False .*/light/p' "${OUT}/driver.log" 2>/dev/null | head -1)"
record="editor ${VERSION} · skin ${skin:-unknown}"
if [[ ${CHECK} == 1 && -f "${TOOL}/record.txt" && "$(cat "${TOOL}/record.txt")" != "${record}" ]]; then
  echo "   FAIL the images were taken with $(cat "${TOOL}/record.txt") — this run is ${record}: every image would differ"; exit 1
fi
if [[ -f "${OUT}/driver.log" ]]; then
  args=(build "${OUT}" "${TOOL}/shots.json" "${IMAGES}")
  [[ ${CHECK} == 1 ]] && args+=(--check)
  [[ ${#ONLY[@]} -gt 0 ]] && args+=(--only "${ONLY[@]}")
  python3 "${TOOL}/post.py" "${args[@]}" || failed=1
fi
if [[ ${CHECK} == 0 && ${failed} == 0 ]]; then
  echo "${record}" > "${TOOL}/record.txt"
fi
exit ${failed}
