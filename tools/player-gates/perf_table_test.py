#!/usr/bin/env python3
# perf_table.py on synthetic perf-<version>.txt files — each case's exit code against the expected one. Run: python3
# tools/player-gates/perf_table_test.py (exit 0 when every case behaves). The cases: a row whose first window took a
# sporadic 14 kB and was measured again · a picked row missing · a row 48 B over in every window · a cycle line with red 1
import os, subprocess, sys, tempfile

HERE = os.path.dirname(os.path.abspath(__file__))
sys.dont_write_bytecode = True
sys.path.insert(0, HERE)
from perf_table import ROWS  # noqa: E402

SCRIPT = os.path.join(HERE, "perf_table.py")


def perf(path, sporadic=True, missing=None, over=None, red=0):
    out = []
    for build in ("faster-runtime", "faster-smaller"):
        out.append(f"{build} | (j) no play n=1 perPlayNs=300 p95=1 split=1/1/1 frameMs=0.2 allocB=114048/300f heapPerPlayerB=0")
        out.append(f"{build} | (j) no play again n=1 perPlayNs=300 p95=1 split=1/1/1 frameMs=0.2 allocB=114000/300f heapPerPlayerB=0")
        for row in ROWS:
            name = row[0]
            if name == missing:
                continue
            a = 114000 + (48 if name == over else 0)
            if sporadic and name == ROWS[0][0]:
                out.append(f"{build} | {name} n=100 perPlayNs=650 p95=1 split=1/1/1 frameMs=0.5 allocB={114000 + 14183}/300f heapPerPlayerB=0")
                out.append(f"{build} | {name} again n=100 perPlayNs=700 p95=1 split=1/1/1 frameMs=0.5 allocB={a}/300f heapPerPlayerB=0")
            else:
                out.append(f"{build} | {name} n=1000 perPlayNs=400 p95=1 split=1/1/1 frameMs=0.5 allocB={a}/300f heapPerPlayerB=0")
        out.append(f"{build} | cycle 0 combos=361 red={red} ends=0")
    open(path, "w").write("\n".join(out) + "\n")
    return path


def main():
    cases = [
        ("a sporadic first window, measured again", {}, 0),
        ("a picked row missing", {"missing": ROWS[5][0]}, 1),
        ("a row 48 B over in every window", {"over": ROWS[10][0]}, 1),
        ("a cycle line with red 1", {"red": 1}, 1),
    ]
    bad = 0
    for i, (name, kw, want) in enumerate(cases):
        d = tempfile.mkdtemp()
        a = perf(os.path.join(d, "perf-6000.3.27f2.txt"), **kw)
        b = perf(os.path.join(d, "perf-6000.6.7f1.txt"), sporadic=False)
        got = subprocess.run([sys.executable, SCRIPT, a, b], capture_output=True, text=True).returncode
        ok = (got == 0) == (want == 0)
        bad += not ok
        print(("ok   " if ok else "BAD  ") + f"{name}: exit {got}")
    print("perf_table: all cases behave" if not bad else f"perf_table: {bad} case(s) misbehave")
    return 1 if bad else 0


if __name__ == "__main__":
    sys.exit(main())
