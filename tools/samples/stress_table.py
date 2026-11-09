#!/usr/bin/env python3
# The stress table's verdict and Markdown — <label>=<stress.log> … (a -stressTable player log each: tools/check-samples.sh
# --stress-table). A log holds an env line and two passes of cells: N 0 twice, N 100 · 1,000 · 5,000 × played once ·
# played again every second × players · coroutines, N 0 again. A pass's baseline is the smaller of its first two N 0
# windows; a cell read again — a players one, or the last N 0 — keeps the window nearest the baseline (the player gate's
# rules). The verdict: every pass has
# all twelve cells with a live recorder · every players cell's 300-frame window within 32 B of the baseline · the last
# N 0 window too (the baseline holds) · at most one window read again in a pass (a stray allocation; more means the run
# was not quiet) · the coroutine cell played again at N 1,000 more than 1,000 B a frame over it (the counter lives). The per-frame difference is reported, not judged. Prints the verdict, the table (a range over the logs
# and passes) and the bytes per coroutine start
import re, sys

FRAMES = 300
CELLS = [(n, m, d) for n in (100, 1000, 5000) for m in ("steady", "retrigger") for d in ("players", "coroutines")]
FEEDBACK = ("Feedback.Revert", "Feedback.Update", "Feedback.LateApply")
COROUTINES = "CoroutinesDelayedCalls"


def parse(path):
    env, cells = None, []
    for line in open(path, errors="replace"):
        if line.startswith("STRESS env "):
            env = dict(re.findall(r"(\w+)=('[^']*'|\S+)", line))
        elif line.startswith("STRESS cell "):
            body = line.strip()[len("STRESS cell "):]
            c = dict(re.findall(r"([\w.]+)=(\S+)", body.split(" list=")[0]))
            c["sum"] = int(c["gcSum"])
            cells.append(c)
    return env, cells


def judge(label, cells, problems, rows):
    passes = sorted({int(c["pass"]) for c in cells})
    if len(passes) < 2:
        problems.append(f"{label}: {len(passes)} pass(es) — two wanted")
    for p in passes:
        pc = [c for c in cells if int(c["pass"]) == p]
        start = [c for c in pc if c["n"] == "0" and c["i"] in ("0", "1")]
        end = [c for c in pc if c["n"] == "0" and c["i"] == "14"]
        if len(start) < 2 or not end:
            problems.append(f"{label} pass {p}: N 0 windows {len(start)} first · {len(end)} last — two and one wanted")
            continue
        base = min(z["sum"] for z in start)
        again = sum(1 for c in pc if int(c["retry"]) > 0)
        if again > 1:
            problems.append(f"{label} pass {p}: {again} windows read again — at most one (the run was not quiet)")
        last = min(end, key=lambda c: abs(c["sum"] - base))
        if abs(last["sum"] - base) > 32:
            problems.append(f"{label} pass {p}: the last N 0 window {last['sum'] - base:+d} B — the baseline did not hold")
        for key in CELLS:
            tried = [c for c in pc if (int(c["n"]), c["mode"], c["drive"]) == key]
            if not tried:
                problems.append(f"{label} pass {p}: no cell {key}")
                continue
            if any(c["recorder"] != "True" for c in tried):
                problems.append(f"{label} pass {p}: cell {key} without the recorder")
            best = min(tried, key=lambda c: abs(c["sum"] - base))
            total = best["sum"] - base
            frame = total / FRAMES
            n, mode, drive = key
            if drive == "players":
                if abs(total) > 32:
                    problems.append(f"{label} pass {p}: {key} {total:+d} B over 300 frames")
                if frame > 32:
                    print(f"   note {label} pass {p}: {key} {frame:+.1f} B a frame")
            if key == (1000, "retrigger", "coroutines") and frame <= 1000:
                problems.append(f"{label} pass {p}: the coroutine control {frame:+.1f} B a frame")
            ms = sum(float(best.get(k, 0)) for k in FEEDBACK) if drive == "players" else float(best.get(COROUTINES, 0))
            rows.setdefault(key, []).append((label, frame, ms, int(best["allocFrames"])))


def span(values, fmt):
    lo, hi = fmt.format(min(values)), fmt.format(max(values))
    return lo if lo == hi else f"{lo} ~ {hi}"


def main(args):
    problems, rows, envs = [], {}, []
    for arg in args:
        label, path = arg.split("=", 1)
        env, cells = parse(path)
        if env is None:
            problems.append(f"{label}: no env line")
            continue
        envs.append((label, env))
        judge(label, cells, problems, rows)
    for p in problems:
        print(f"   FAIL stress table: {p}")
    if not problems:
        quiet = all(r[3] == 0 for k, v in rows.items() if k[2] == "players" for r in v)
        print(f"   PASS stress table: {len(envs)} log(s) · {sum(len(v) for v in rows.values())} cells · players within 32 B a window"
              + (" · no players frame allocated" if quiet else ""))
    print("| Cubes | Mode | Feedback — bytes a frame | Coroutines — bytes a frame | Feedback — ms a frame | Coroutines — ms a frame |")
    print("|---:|---|---:|---:|---:|---:|")
    for n in (100, 1000, 5000):
        for mode in ("steady", "retrigger"):
            p, c = rows.get((n, mode, "players")), rows.get((n, mode, "coroutines"))
            if not p or not c:
                continue
            print(f"| {n:,} | {'Played once' if mode == 'steady' else 'Played again every second'} | "
                  f"{span([max(0.0, r[1]) for r in p], '{:,.0f}')} | {span([max(0.0, r[1]) for r in c], '{:,.0f}')} | "
                  f"{span([r[2] for r in p], '{:.2f}')} | {span([r[2] for r in c], '{:.2f}')} |")
    for label, env in envs:
        starts = [r[1] * 60 / n for n in (100, 1000, 5000) for r in rows.get((n, "retrigger", "coroutines"), []) if r[0] == label]
        per = span(starts, "{:.0f}") if starts else "?"
        print(f"   {label}: unity={env.get('unity')} cpu={env.get('cpu')} model={env.get('model')} arch={env.get('arch')} "
              f"il2cpp={env.get('il2cpp')} debug={env.get('debug')} · bytes a coroutine start {per}")
    return 1 if problems else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
