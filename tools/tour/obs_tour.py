#!/usr/bin/env python3
# obs_tour.py <command> … — the OBS side of tools/tour/record-tour.sh, through obs-websocket 5 (obsws-python).
#
#   source                 the capture source on the program scene: macOS screen capture of the Unity application on the main
#                          display, no cursor (made once, its settings set every run)
#   crop <rect.txt> <png>  the source cropped to the Game view's render area (rect.txt from the tour's editor side) and fitted
#                          to the canvas; a screenshot (<png>) checks the top and bottom rows for the editor's flat colour and
#                          corrects the crop by that many rows; in studio mode the preview is transitioned to the program
#   mute <state.json>      every audio input muted, the earlier states kept in <state.json>
#   unmute <state.json>    those states put back
#   start · stop           the recording (stop prints the file)
#
# The connection: OBS_WEBSOCKET_URL (obsws://host:port/password), else the URL in ~/.local/bin/obsc, else
# obsws://localhost:4455 without a password — the password is never printed. Not used here: GetInputPropertiesListPropertyItems
# on a screen capture source — OBS 33.0.1's obs-websocket crashes on it.
import ctypes
import json
import os
import re
import subprocess
import sys

import obsws_python as obs

SOURCE = os.environ.get("TOUR_OBS_SOURCE", "Unity (tour)")
UNITY_APP = "com.unity3d.UnityEditor5.x"
Y_FIX = int(os.environ.get("TOUR_CROP_Y_FIX", "49"))


def connect():
    url = os.environ.get("OBS_WEBSOCKET_URL", "")
    obsc = os.path.expanduser("~/.local/bin/obsc")
    if not url and os.path.exists(obsc):
        m = re.search(r"obsws://[^\"'\s]+", open(obsc).read())
        url = m.group(0) if m else ""
    m = re.match(r"obsws://([^:/]+):(\d+)(?:/(.*))?$", url or "obsws://localhost:4455")
    return obs.ReqClient(host=m.group(1), port=int(m.group(2)), password=m.group(3) or "", timeout=10)


def main_display_uuid():
    cg = ctypes.CDLL("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")
    cs = ctypes.CDLL("/System/Library/Frameworks/ColorSync.framework/ColorSync")
    cf = ctypes.CDLL("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")
    cg.CGMainDisplayID.restype = ctypes.c_uint32
    cs.CGDisplayCreateUUIDFromDisplayID.restype = ctypes.c_void_p
    cs.CGDisplayCreateUUIDFromDisplayID.argtypes = [ctypes.c_uint32]
    cf.CFUUIDCreateString.restype = ctypes.c_void_p
    cf.CFUUIDCreateString.argtypes = [ctypes.c_void_p, ctypes.c_void_p]
    cf.CFStringGetCString.argtypes = [ctypes.c_void_p, ctypes.c_char_p, ctypes.c_long, ctypes.c_uint32]
    s = cf.CFUUIDCreateString(None, cs.CGDisplayCreateUUIDFromDisplayID(cg.CGMainDisplayID()))
    buf = ctypes.create_string_buffer(64)
    cf.CFStringGetCString(s, buf, 64, 0x08000100)
    return buf.value.decode()


def scene(c):
    return c.get_current_program_scene().current_program_scene_name


def source(c):
    sc = scene(c)
    settings = {"type": 2, "application": UNITY_APP, "display_uuid": main_display_uuid(), "show_cursor": False}
    if SOURCE not in [i["inputName"] for i in c.get_input_list().inputs]:
        c.create_input(sc, SOURCE, "screen_capture", settings, True)
    else:
        c.set_input_settings(SOURCE, settings, True)
    print("source", SOURCE, "on", sc)


def parse_rect(path):
    values = {}
    for line in open(path):
        parts = line.split()
        if parts[0] == "ppp":
            values["ppp"] = float(parts[1])
        elif parts[0] == "window" and "window" not in values:
            values["window"] = [float(v) for v in parts[1:5]]
        elif parts[0] == "prop" and parts[1] in ("GameView.viewInWindow", "GameView.targetInView"):
            values.setdefault(parts[1].split(".")[1], [float(v) for v in parts[2:6]])
    return values


def set_crop(c, x, y, w, h):
    sc = scene(c)
    sid = c.get_scene_item_id(sc, SOURCE).scene_item_id
    t = c.get_scene_item_transform(sc, sid).scene_item_transform
    sw, sh = int(t["sourceWidth"]), int(t["sourceHeight"])
    v = c.get_video_settings()
    c.set_scene_item_transform(sc, sid, {
        "cropLeft": x, "cropTop": y, "cropRight": max(0, sw - x - w), "cropBottom": max(0, sh - y - h),
        "boundsType": "OBS_BOUNDS_SCALE_INNER", "boundsWidth": float(v.base_width), "boundsHeight": float(v.base_height),
        "positionX": 0.0, "positionY": 0.0, "alignment": 5, "boundsAlignment": 0})
    return sc, int(v.base_width), int(v.base_height)


# rows from the top (or the bottom) that are one flat colour across the whole width — the editor around the Game view
def flat_rows(png, w, h, from_top):
    raw = subprocess.run(["ffmpeg", "-v", "error", "-i", png, "-f", "rawvideo", "-pix_fmt", "gray", "-"], capture_output=True).stdout
    if len(raw) != w * h:
        return 0
    rows = range(h) if from_top else range(h - 1, -1, -1)
    n = 0
    for y in rows:
        r = raw[y * w:(y + 1) * w]
        if min(r) != max(r):
            break
        n += 1
    return n if n < h // 2 else 0


def crop(c, rect_path, png):
    r = parse_rect(rect_path)
    ppp, win, view, target = r["ppp"], r["window"], r["viewInWindow"], r["targetInView"]
    x = round((win[0] + view[0] + target[0]) * ppp)
    y = round((win[1] + view[1] + target[1]) * ppp) + Y_FIX
    w, h = round(target[2] * ppp), round(target[3] * ppp)
    for _ in range(3):
        sc, cw, ch = set_crop(c, x, y, w, h)
        c.save_source_screenshot(sc, "png", os.path.abspath(png), cw, ch, -1)
        top, bottom = flat_rows(png, cw, ch, True), flat_rows(png, cw, ch, False)
        print("crop", x, y, w, h, "| flat rows top", top, "bottom", bottom)
        if top == 0 and bottom == 0:
            break
        y += round((top - bottom) * h / ch)
    if c.get_studio_mode_enabled().studio_mode_enabled:
        c.set_current_preview_scene(sc)
        c.trigger_studio_mode_transition()
        print("studio mode: the preview transitioned to the program")


def mute(c, state):
    kept = {}
    for i in c.get_input_list().inputs:
        try:
            kept[i["inputName"]] = c.get_input_mute(i["inputName"]).input_muted
        except Exception:
            continue
        c.set_input_mute(i["inputName"], True)
    json.dump(kept, open(state, "w"), ensure_ascii=False)
    print("muted", ", ".join(kept) or "-")


def unmute(c, state):
    if not os.path.exists(state):
        return
    for name, was in json.load(open(state)).items():
        try:
            c.set_input_mute(name, was)
        except Exception:
            pass
    os.remove(state)
    print("audio inputs as before")


def main():
    c = connect()
    cmd, args = sys.argv[1], sys.argv[2:]
    if cmd == "source":
        source(c)
    elif cmd == "crop":
        crop(c, args[0], args[1])
    elif cmd == "mute":
        mute(c, args[0])
    elif cmd == "unmute":
        unmute(c, args[0])
    elif cmd == "start":
        c.start_record()
        print("recording")
    elif cmd == "stop":
        print(c.stop_record().output_path)
    else:
        sys.exit("unknown command " + cmd)


if __name__ == "__main__":
    main()
