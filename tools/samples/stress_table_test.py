#!/usr/bin/env python3
# stress_table.py on synthetic -stressTable logs — each case's exit code against the expected one. Run: python3
# tools/samples/stress_table_test.py (exit 0 when every case behaves). The cases: a non-zero baseline with a one-off in the
# first N 0 window · a players cell 40 B over in its window · a cell read again whose nearer window is 8 B over · two
# cells read again in a pass · the
# coroutine control under 1,000 B a frame · a missing cell · a cell without the recorder · a run of one pass only · the
# last N 0 window 40 B over the baseline · the last N 0 window over once and back at the baseline when read again
import os, subprocess, sys, tempfile

SCRIPT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "stress_table.py")
CELLS = [(n, m, d) for n in (100, 1000, 5000) for m in ("steady", "retrigger") for d in ("players", "coroutines")]


def log(path, base=30000, over=None, weak=False, drop=None, norec=None, retry_near=False, passes=2, drift=0, drift_back=False, also=None):
    out = ["STRESS env unity=6000.3.27f2 cpu='Apple M4 Pro' model=Mac16,8 arch=Arm64 debug=True il2cpp=True passes=%d" % passes]

    def cell(p, i, n, m, d, s, retry=0, rec="True"):
        out.append(f"STRESS cell pass={p} i={i} n={n} mode={m} drive={d} retry={retry} recorder={rec} gcSum={s} gcAvg={s // 300} "
                   f"gcMax=0 allocFrames=0 fps=59.7 Feedback.Revert=0.1 Feedback.Update=0.7 Feedback.LateApply=0.1 "
                   f"CoroutinesDelayedCalls=0.5 PlayerLoop=16.7 WaitForTargetFPS=14.0 GC.Collect=0.0 count={n} playing={n} heap=1 list=[]")

    for p in range(1, passes + 1):
        cell(p, 0, 0, "steady", "players", base + 594)
        cell(p, 1, 0, "steady", "players", base)
        for i, key in enumerate(CELLS, 2):
            if key == drop:
                continue
            n, m, d = key
            s = base
            if d == "coroutines" and m == "retrigger":
                s = base + (900 * 300 if weak and n == 1000 else n * 128 * 300 // 60)
            if key == over or key == also:
                s = base + 40
            cell(p, i, n, m, d, s, rec="False" if key == norec else "True")
            if (key == over or key == also) and retry_near:
                cell(p, i, n, m, d, base + 8, retry=1)
        cell(p, 14, 0, "steady", "players", base + (drift if p == 1 else 0))
        if drift_back and p == 1:
            cell(p, 14, 0, "steady", "players", base, retry=1)
    open(path, "w").write("\n".join(out) + "\n")
    return path


def main():
    tmp = tempfile.mkdtemp()
    cases = [
        ("a non-zero baseline · a one-off in the first N 0 window", {}, 0),
        ("a players cell 40 B over in its window", {"over": (1000, "retrigger", "players")}, 1),
        ("read again — the nearer window 8 B over", {"over": (1000, "steady", "players"), "retry_near": True}, 0),
        ("two cells read again in a pass, both nearer", {"over": (1000, "steady", "players"), "also": (100, "steady", "players"), "retry_near": True}, 1),
        ("the coroutine control 900 B a frame", {"weak": True}, 1),
        ("a cell missing", {"drop": (5000, "steady", "coroutines")}, 1),
        ("a cell without the recorder", {"norec": (100, "steady", "players")}, 1),
        ("one pass only", {"passes": 1}, 1),
        ("the last N 0 window 40 B over (the baseline moved)", {"drift": 40}, 1),
        ("the last N 0 window 76 kB over once, read again at the baseline", {"drift": 75800, "drift_back": True}, 0),
    ]
    bad = 0
    for i, (name, kw, want) in enumerate(cases):
        path = log(os.path.join(tmp, f"{i}.log"), **kw)
        got = subprocess.run([sys.executable, SCRIPT, "6000.3.27f2=" + path], capture_output=True, text=True).returncode
        ok = (got == 0) == (want == 0)
        bad += not ok
        print(("ok   " if ok else "BAD  ") + f"{name}: exit {got}")
    print("stress_table: all cases behave" if not bad else f"stress_table: {bad} case(s) misbehave")
    return 1 if bad else 0


if __name__ == "__main__":
    sys.exit(main())
