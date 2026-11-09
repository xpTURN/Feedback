#!/usr/bin/env python3
# The per-target table of Documentation~/Performance.md — <perf-<version>.txt> … (tools/check-player-gates.sh's PERF and GATE
# lines, one file a version). A test class's baseline is the smaller of its 'no play' and 'no play again' windows; a row
# measured again keeps the window nearest the baseline (the gate's rules). The time is the Faster runtime build's per play,
# a range over the versions; the allocation holds in every build. Fails when a picked row is missing, a row's nearest
# window is more than 32 B past its baseline, or a cycle line (every target × motion) has red above 0
import re, sys

# (the row's name in the PERF lines, the feedback or what plays, its motion, the players) — the items and motions are the
# performance tests' own
ROWS = [
    ("(a) leaf punch 100", "`Transform/Position`", "Punch", "100"),
    ("(a) leaf punch 1000", "`Transform/Position`", "Punch", "1,000"),
    ("(a) leaf punch 5000", "`Transform/Position`", "Punch", "5,000"),
    ("(r) driver loop", "`Transform/Position` in a `MotionDriver`", "Oscillate", "1,000"),
    ("(s) shake direct", "`Camera/Shake`", "Shake", "1,000"),
    ("(b) mpb flash", "`Renderer/Material Color` (property block)", "Flash", "1,000"),
    ("(t) material float 1000", "`Renderer/Material Float`", "Oscillate", "1,000"),
    ("(u) sprite color 1000", "`Renderer/Sprite Color`", "Flash", "1,000"),
    ("(c) light shake", "`Light/Intensity`", "Shake", "1,000"),
    ("(x) light 2d intensity 1000", "`Light 2D/Intensity`", "Oscillate", "1,000"),
    ("(x) ui graphic color 1000", "`UI/Color`", "Punch", "1,000"),
    ("(x) ui canvas group alpha 1000", "`UI/Canvas Group Alpha`", "Tween", "1,000"),
    ("(x) tmp color 1000", "`UI/Color` on a TextMeshPro text", "Punch", "1,000"),
    ("(x) tmp character offset 1000", "`TMP/Character Offset`", "Oscillate", "1,000"),
    ("(x) screen flash 1000", "`Screen/Flash`", "Flash", "1,000"),
    ("(x) floating text 1000", "`Screen/Floating Text`", "Set", "1,000"),
    ("(x) postfx weight 1000", "`PostFX/Vignette`", "Oscillate", "1,000"),
    ("(x) cinemachine lens 1000", "`Cinemachine/Lens FOV`", "Punch", "1,000"),
    ("(x) impulse 1000", "`Cinemachine/Impulse`", "—", "1,000"),
    ("(w) animator float 1000", "`Animation/Float Parameter`", "Tween", "1,000"),
    ("(v) particle emission rate 1000", "`Particles/Emission Rate`", "Flash", "1,000"),
    ("(x) gamepad rumble 1000", "`Haptics/Gamepad Rumble`", "Curve", "1,000"),
    ("(d) empty runner 1000", "(the scheduler alone)", "—", "1,000"),
]


def read(path):
    builds = {}
    for line in open(path, errors="replace"):
        build, _, rest = line.partition("|")
        build, rest = build.strip(), rest.strip()
        if not rest:
            continue
        b = builds.setdefault(build, {"rows": {}, "cycles": [], "base": None, "first": None})
        cycle = re.match(r"cycle (\d+) combos=(\d+) red=(\d+)", rest)
        if cycle:
            b["cycles"].append(tuple(int(x) for x in cycle.groups()))
            continue
        alloc = re.search(r"allocB=(\d+)/300f", rest)
        if not alloc:
            continue
        name = rest.split(" n=")[0]
        a = int(alloc.group(1))
        if name.endswith("no play"):
            b["first"] = a
        elif name.endswith("no play again"):
            b["base"] = min(b["first"], a) if b["first"] is not None else a
        elif not name.startswith("(j)"):
            key = name[:-len(" again")] if name.endswith(" again") else name
            r = b["rows"].setdefault(key, {"windows": [], "ns": [], "base": b["base"]})
            r["windows"].append(a)
            r["ns"].append(float(re.search(r"perPlayNs=([\d.]+)", rest).group(1)))
    return builds


def main(paths):
    problems, ns, cycles = [], {}, []
    versions = []
    for path in paths:
        version = re.search(r"perf-(.+)\.txt", path).group(1)
        versions.append(version)
        for build, b in read(path).items():
            for cycle, combos, red in b["cycles"]:
                cycles.append(combos)
                if red:
                    problems.append(f"{version} {build}: cycle {cycle} — {red} of {combos} allocated")
            for key, _, _, _ in ROWS:
                r = b["rows"].get(key)
                if r is None or r["base"] is None:
                    problems.append(f"{version} {build}: no row '{key}'")
                    continue
                near = min(r["windows"], key=lambda w: abs(w - r["base"]))
                if abs(near - r["base"]) > 32:
                    problems.append(f"{version} {build}: '{key}' {near - r['base']:+d} B over 300 frames")
                if build == "faster-runtime":
                    ns.setdefault(key, []).append(min(r["ns"]))
    for p in problems:
        print(f"   FAIL perf table: {p}")
    if not problems:
        print(f"   PASS perf table: {len(ROWS)} rows · every build within 32 B a window · {len(cycles)} cycle lines of "
              f"{max(cycles) if cycles else 0} combinations, none allocating · {' · '.join(versions)}")
    print("| Feedback | Motion | Players | Per play (ns) | Frame allocation |")
    print("|---|---|---:|---:|---:|")
    for key, what, motion, n in ROWS:
        if key not in ns:
            continue
        lo, hi = min(ns[key]), max(ns[key])
        cost = f"{lo:,.0f}" if round(lo) == round(hi) else f"{lo:,.0f} ~ {hi:,.0f}"
        print(f"| {what} | {motion} | {n} | {cost} | 0 B |")
    return 1 if problems else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
