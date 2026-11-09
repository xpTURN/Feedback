#!/usr/bin/env python3
# capture.py — the capture side of tools/doc-images.sh, beside the editor run. The driver writes ready-<name>.txt
# ("<pid> <pixels per point> <x> <y> <width> <height> <crop x> <crop y> <crop width> <crop height>": the window's content
# and the part of it to keep, in points); this finds the window among the editor's (windows <pid>), captures it with
# screencapture -l until two crops in a row are the same, keeps <name>.png (the crop, at the screen's pixels) and answers
# done-<name>.txt ("ok <captures>" or why not). It ends when the driver writes end.txt or the editor's process is gone,
# and adds to others.txt the Unity editors outside this run's that it saw meanwhile.
#
# Usage: capture.py <out folder> <editor launcher pid> <windows binary>
import glob, os, subprocess, sys, time

from PIL import Image

TRIES = 24
PERMISSION = ("screencapture could not capture the editor's window — give the app that runs this tool Screen Recording "
              "permission (System Settings > Privacy & Security > Screen & System Audio Recording), then run it again")


def alive(pid):
    try:
        os.kill(pid, 0)
        return True
    except OSError:
        return False


def unity_pids():
    r = subprocess.run(["pgrep", "-f", "Unity.app/Contents/MacOS/Unity"], capture_output=True, text=True)
    return {int(p) for p in r.stdout.split()}


def window(windows, pid, x, y, w, h):
    """the editor's window holding that content: (number, x, y, width, height) or None, and the list read"""
    lines = subprocess.run([windows, str(pid)], capture_output=True, text=True).stdout.splitlines()
    found = []
    for line in lines:
        f = line.split()
        # a closed or hidden window is off screen (a covered one is on screen)
        if len(f) < 7 or not f[0].isdigit() or f[6] != "true":
            continue
        wid, X, Y, W, H = int(f[0]), *map(float, f[1:5])
        # the same left and width, the content inside it (an editor window's place starts at its tab strip, its height is
        # the content's — the window's bottom is lower by the tabs)
        below = (Y + H) - (y + h)
        if abs(X - x) < 2 and abs(W - w) < 2 and Y <= y + 1 and below > -1:
            found.append((round(below), -wid, (wid, X, Y, W, H)))
    # the nearest bottom, then the newest window
    return (min(found)[2] if found else None), lines


def handle(out, name, windows):
    f = open(os.path.join(out, f"ready-{name}.txt")).read().split()
    pid, ppp = int(f[0]), float(f[1])
    x, y, w, h, cx, cy, cw, ch = map(float, f[2:10])
    shot = os.path.join(out, f"capture-{name}.png")
    previous = None
    why = ""
    for k in range(1, TRIES + 1):
        found, lines = window(windows, pid, x, y, w, h)
        with open(os.path.join(out, f"windows-{name}.txt"), "w") as fh:
            fh.write("\n".join(lines) + "\n")
        if found is None:
            why = f"no window of the editor at {x:g},{y:g} {w:g}x{h:g} (windows-{name}.txt)"
            previous = None
            time.sleep(0.3)
            continue
        why = ""
        wid, X, Y, W, H = found
        r = subprocess.run(["screencapture", "-x", "-o", f"-l{wid}", shot], capture_output=True, text=True)
        if r.returncode != 0 or not os.path.exists(shot):
            return PERMISSION + f" ({(r.stderr or r.stdout).strip()})"
        with Image.open(shot) as img:
            size = img.size
            # the content is the window's bottom h points
            top = H - h
            box = tuple(round(v * ppp) for v in (x - X + cx, top + cy, x - X + cx + cw, top + cy + ch))
            crop = img.convert("RGB").crop(box) if size == (round(W * ppp), round(H * ppp)) else None
        os.remove(shot)
        if crop is None:
            why = f"the capture is {size[0]}x{size[1]}, the window {W:g}x{H:g} points at {ppp:g}"
            previous = None
            time.sleep(0.3)
            continue
        if previous is not None and crop.tobytes() == previous.tobytes():
            crop.save(os.path.join(out, f"{name}.png"))
            return f"ok {k}"
        if previous is not None:
            previous.save(os.path.join(out, f"unstable-{name}-a.png"))
            crop.save(os.path.join(out, f"unstable-{name}-b.png"))
        previous = crop
        time.sleep(0.15)
    return why or f"not the same twice in {TRIES} captures"


def main(out, launcher, windows):
    others = set()
    last_check = 0.0
    while True:
        for ready in sorted(glob.glob(os.path.join(out, "ready-*.txt"))):
            name = os.path.basename(ready)[len("ready-"):-len(".txt")]
            done = os.path.join(out, f"done-{name}.txt")
            if os.path.exists(done):
                continue
            try:
                answer = handle(out, name, windows)
            except Exception as e:  # the driver waits for an answer — give it the error
                answer = f"capture error {type(e).__name__}: {e}"
            with open(done + ".tmp", "w") as fh:
                fh.write(answer + "\n")
            os.replace(done + ".tmp", done)
        now = time.time()
        if now - last_check > 2:
            last_check = now
            ours = {int(p) for p in subprocess.run(["pgrep", "-P", str(launcher)], capture_output=True, text=True).stdout.split()}
            # the launcher's children and theirs are this run's editor — anything else is another editor
            for p in list(ours):
                ours |= {int(q) for q in subprocess.run(["pgrep", "-P", str(p)], capture_output=True, text=True).stdout.split()}
            seen = unity_pids() - ours - {launcher}
            if seen - others:
                others |= seen
                with open(os.path.join(out, "others.txt"), "a") as fh:
                    fh.write(" ".join(map(str, sorted(seen))) + "\n")
        if os.path.exists(os.path.join(out, "end.txt")) or not alive(launcher):
            break
        time.sleep(0.05)
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1], int(sys.argv[2]), sys.argv[3]))
