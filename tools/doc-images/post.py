#!/usr/bin/env python3
# post.py — the after-run steps of tools/doc-images.sh.
#
#   post.py build <out> <shots.json> <images> [--check] [--only <id>...]
#       each shot's capture (<out>/<id>.png, or the frames of frames-<id>.txt for a GIF) at one pixel per point, its colors
#       reduced, compared with the image in <images>: one that is the same within the threshold is left as it is (no new
#       bytes for a capture that moved a pixel), a new or changed one is written — or, with --check, only listed. Exit 1
#       when a shot failed or (--check) an image changed
#   post.py prefs <before.plist> <after.plist> <clone> <another editor ran: 0|1>
#       the editor preferences this run changed put back (defaults write · delete): every changed key when no other Unity
#       editor ran, else only those whose value names the clone. Exit 1 when a package key (xpTURN.Feedback.*) differed
#       after the editor — the driver puts those back itself. --recover: a run that did not finish, put back from its copy
#       (only the keys naming the clone; the package keys are the driver's). --driver-recovers: the driver took the
#       package keys from a killed run's backup — they are left as the driver put them
import json, os, plistlib, subprocess, sys

from PIL import Image, ImageChops, ImageFilter

DOMAIN = "com.unity3d.UnityEditor5.x"
PNG_COLORS = 64
GIF_COLORS = 128
# a pixel differs when a channel moved by more than this; an image differs when more differing pixels than this touch
# another differing pixel (a lone one is noise — a changed letter is two or more side by side)
CHANNEL = 40
PIXELS = 0


def one_x(img, ppp):
    img = img.convert("RGB")
    if ppp == 1:
        return img
    return img.resize((round(img.width / ppp), round(img.height / ppp)), Image.LANCZOS)


def reduced(img):
    return img.quantize(colors=PNG_COLORS, method=Image.Quantize.MEDIANCUT, dither=Image.Dither.NONE)


def differing(a, b):
    """the differing pixels that touch another differing pixel, or None when the sizes differ"""
    a, b = a.convert("RGB"), b.convert("RGB")
    if a.size != b.size:
        return None
    r, g, bl = ImageChops.difference(a, b).split()
    mask = ImageChops.lighter(ImageChops.lighter(r, g), bl).point(lambda v: 255 if v > CHANNEL else 0)
    # the 3 x 3 sum around each pixel, as 0..9 — 2 or more around a differing pixel means a neighbour differs too
    around = mask.point(lambda v: 1 if v else 0).filter(ImageFilter.Kernel((3, 3), [1] * 9, scale=1))
    touching = ImageChops.multiply(around.point(lambda v: 255 if v >= 2 else 0), mask)
    return touching.histogram()[255]


def same(a, b):
    d = differing(a, b)
    return d is not None and d <= PIXELS


def frames_of(path):
    """a GIF's frames as RGB images, with their times in ms"""
    out = []
    with Image.open(path) as gif:
        for i in range(getattr(gif, "n_frames", 1)):
            gif.seek(i)
            out.append((gif.convert("RGB"), gif.info.get("duration", 0)))
    return out


def same_gif(path_a, path_b):
    a, b = frames_of(path_a), frames_of(path_b)
    return len(a) == len(b) and all(x[1] == y[1] and same(x[0], y[0]) for x, y in zip(a, b))


def write_gif(frames, path):
    """frames: [(image, seconds)] — one palette for all of them"""
    sheet = Image.new("RGB", (frames[0][0].width, frames[0][0].height * len(frames)))
    for i, (img, _) in enumerate(frames):
        sheet.paste(img, (0, i * img.height))
    palette = sheet.quantize(colors=GIF_COLORS, method=Image.Quantize.MEDIANCUT, dither=Image.Dither.NONE)
    images = [img.quantize(palette=palette, dither=Image.Dither.NONE) for img, _ in frames]
    images[0].save(path, save_all=True, append_images=images[1:], duration=[round(t * 1000) for _, t in frames], loop=0,
                   optimize=False, disposal=1)


def ppp_of(out):
    for line in open(os.path.join(out, "driver.log")):
        f = line.split()
        if len(f) > 4 and f[1] == "unity" and f[3] == "ppp":
            return float(f[4])
    raise SystemExit("the driver's log has no pixels per point")


def build(out, shots_path, images, check, only):
    shots = [s for s in json.load(open(shots_path))["shots"] if not only or s["id"] in only]
    ppp = ppp_of(out)
    final = os.path.join(out, "final")
    os.makedirs(final, exist_ok=True)
    failed = changed = 0
    for s in shots:
        sid, name = s["id"], s["file"]
        fail = os.path.join(out, f"fail-{sid}.txt")
        if os.path.exists(fail):
            print(f"   FAIL {sid} {name} — {open(fail).read().strip().splitlines()[0]}")
            failed += 1
            continue
        made = os.path.join(final, name)
        target = os.path.join(images, name)
        if name.endswith(".gif"):
            listing = os.path.join(out, f"frames-{sid}.txt")
            if not os.path.exists(listing):
                print(f"   FAIL {sid} {name} — no frames")
                failed += 1
                continue
            frames = []
            for line in open(listing).read().splitlines():
                frame, seconds = line.split()
                with Image.open(os.path.join(out, frame + ".png")) as img:
                    frames.append((one_x(img, ppp), float(seconds)))
            write_gif(frames, made)
            unchanged = os.path.exists(target) and same_gif(made, target)
        else:
            capture = os.path.join(out, sid + ".png")
            if not os.path.exists(capture):
                print(f"   FAIL {sid} {name} — no capture")
                failed += 1
                continue
            with Image.open(capture) as img:
                reduced(one_x(img, ppp)).save(made, optimize=True)
            unchanged = False
            if os.path.exists(target):
                with Image.open(made) as a, Image.open(target) as b:
                    unchanged = same(a, b)
        size = os.path.getsize(made)
        if unchanged:
            print(f"   same {name} ({size // 1024} KB)")
            continue
        changed += 1
        if check:
            print(f"   CHANGED {name} — the editor shows something else now (new: {made})")
        else:
            os.makedirs(images, exist_ok=True)
            with open(made, "rb") as src, open(target, "wb") as dst:
                dst.write(src.read())
            print(f"   wrote {name} ({size // 1024} KB)")
    print(f"   {len(shots) - failed} of {len(shots)} shot(s) · {changed} {'changed' if check else 'written'} · {failed} failed")
    return 1 if failed or (check and changed) else 0


def prefs(before_path, after_path, clone, others, recover=False, driver_recovers=False):
    before, after = (plistlib.load(open(p, "rb")) for p in (before_path, after_path))
    owned = lambda k: k.startswith("xpTURN.Feedback.")
    package = [k for k in sorted(set(before) | set(after)) if owned(k) and before.get(k) != after.get(k)]
    if driver_recovers:
        print(f"   package preferences: {len(package)} put back by the driver from the last run's backup")
        recover = True
    restored, left = [], []
    for key in sorted(set(before) | set(after)):
        if before.get(key) == after.get(key) or (driver_recovers and owned(key)):
            continue
        ours = not others or clone in str(after.get(key, ""))
        if not ours:
            left.append(key)
            continue
        if key not in before:
            subprocess.run(["defaults", "delete", DOMAIN, key], check=True, capture_output=True)
        else:
            v = before[key]
            kind = "-bool" if isinstance(v, bool) else "-int" if isinstance(v, int) else "-float" if isinstance(v, float) \
                else "-data" if isinstance(v, bytes) else "-string" if isinstance(v, str) else None
            if kind is None:
                left.append(key)
                continue
            value = v.hex() if isinstance(v, bytes) else ("true" if v else "false") if isinstance(v, bool) else str(v)
            subprocess.run(["defaults", "write", DOMAIN, key, kind, value], check=True, capture_output=True)
        restored.append(key)
    print(f"   editor preferences: {len(restored)} put back" + (f" · {len(left)} left (another editor ran): {', '.join(left)}" if left else ""))
    if package and not recover:
        print(f"   FAIL the driver left {len(package)} package preference(s) changed: {', '.join(package)}")
    return 1 if package and not recover else 0


def main(args):
    if args[:1] == ["build"]:
        only = args[args.index("--only") + 1:] if "--only" in args else []
        return build(args[1], args[2], args[3], "--check" in args, only)
    if args[:1] == ["prefs"]:
        return prefs(args[1], args[2], args[3], args[4] == "1", "--recover" in args, "--driver-recovers" in args)
    raise SystemExit(__doc__ or "usage: post.py build|prefs …")


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
