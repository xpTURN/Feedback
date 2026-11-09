#!/usr/bin/env python3
# tools/check-samples.sh's player-tour verdict — <label> <tour.log> <dev|release|il2cpp> <cards.txt> (an available card's scene
# and its persistent UnityEvent calls a line). Every kind: one stop for each available card and one at the hub around each (2N + 1), the cards' scenes as the
# oracle says, the done line, no warning or error of ours (a Debug.LogWarning · LogError or an exception whose stack names
# xpTURN.Feedback — the engine's lines are listed apart), each card's persistent UnityEvent calls as many as its scene holds
# and every one's method found, the Showcase's game played, the Unity Timeline cutscene's players played (its stop's plays
# and players playing at its end — a saved timeline's tracks and clips through the player's stripping). A development player (Mono or IL2CPP) also: the counter at every stop · the HUD on − off
# ≤ 32 B at every stop (after its own retries) · the positive control ≥ 1 KB · Stress Zero GC at 1,000 players with its off
# window ≤ 32 B over the quietest hub stop's · the game's allocating frames ≤ 10 (the first of each kind of event warms up)
# with 30 plays or more. A release one: no counter and the HUD's 'development build only' at every stop
import os, re, sys

HUB = "SampleHub"


def value(line, key):
    m = re.search(rf"\b{key}=(-?\d+)", line)
    return int(m.group(1)) if m else None


def ours_and_engine(lines):
    ours, engine = [], []
    for i, line in enumerate(lines):
        stack = line.startswith("UnityEngine.Debug:LogWarning") or line.startswith("UnityEngine.Debug:LogError")
        exception = re.match(r"^[\w.]+Exception\b", line) is not None
        if not (stack or exception):
            continue
        j = i + 1
        block = []
        while j < len(lines) and lines[j].strip() and not lines[j].startswith("(Filename"):
            block.append(lines[j]); j += 1
        message = line if exception else next((lines[k] for k in range(i - 1, -1, -1) if lines[k].strip()), "")
        named = any("xpTURN.Feedback" in b for b in block) or "xpTURN.Feedback" in message
        (ours if named else engine).append(message.strip()[:160])
    return ours, engine


def verdict(label, log, kind, cards):
    lines = open(log, errors="replace").read().splitlines() if os.path.exists(log) else []
    rows = [c.split() for c in open(cards).read().splitlines() if c.strip()] if os.path.exists(cards) else []
    want = [r[0] for r in rows]
    held = {r[0]: int(r[1]) for r in rows if len(r) > 1}
    stops = [l for l in lines if "SAMPLES tour scene=" in l]
    scenes = [re.search(r"scene=(\S+)", l).group(1) for l in stops]
    visited = sorted(s for s in scenes if s != HUB)
    problems = []
    if not want:
        problems.append("no available card in the oracle")
    if not any("SAMPLES tour done" in l for l in lines):
        problems.append("no done line")
    if len(stops) != 2 * len(want) + 1 or scenes.count(HUB) != len(want) + 1:
        problems.append(f"{len(stops)} stops ({scenes.count(HUB)} at the hub) for {len(want)} cards")
    if visited != sorted(want):
        missing = sorted(set(want) - set(visited))
        extra = sorted(s for s in set(visited) if visited.count(s) != want.count(s))
        problems.append(f"the cards' scenes — missing {missing} · off count {extra}")
    ours, engine = ours_and_engine(lines)
    problems += ["ours: " + o for o in ours[:5]]
    events = [l for l in lines if "SAMPLES tour events " in l]
    calls = sum(value(l, "calls") or 0 for l in events)
    lacking = [re.search(r"scene=(\S+)", l).group(1) + " " + re.search(r"lacking=\[(.*)\]", l).group(1) for l in events if (value(l, "missing") or 0) > 0]
    if len(events) != len(stops) or calls == 0:
        problems.append(f"the UnityEvent check — {len(events)} lines for {len(stops)} stops · {calls} calls")
    for l in events:
        scene = re.search(r"scene=(\S+)", l).group(1)
        if scene in held and value(l, "calls") != held[scene]:
            problems.append(f"{scene}'s persistent UnityEvent calls {value(l, 'calls')} — its scene holds {held[scene]}")
    problems += ["a persistent call's method missing: " + x for x in lacking[:5]]
    game = next((l for l in lines if "SAMPLES tour game " in l), None)
    if "BrickBreaker" in want and (game is None or (value(game, "played") or 0) < 30):
        problems.append("the Showcase's game — " + (game or "no line"))
    cutscene = next((l for l in stops if "scene=TimelineCutscene " in l), None)
    if "TimelineCutscene" in want and (cutscene is None or (value(cutscene, "plays") or 0) + (value(cutscene, "playing") or 0) < 1):
        problems.append("the Unity Timeline cutscene played nothing — " + (cutscene or "no stop"))
    summary = f"{len(stops)} stops · {len(want)} cards · UnityEvent calls {calls}"
    if cutscene is not None:
        summary += f" · cutscene plays {value(cutscene, 'plays')} playing {value(cutscene, 'playing')}"
    if kind in ("dev", "il2cpp"):
        if any("recorder=True" not in l for l in stops):
            problems.append("the counter is missing at a stop")
        worst = max([value(l, "on") - value(l, "off") for l in stops] or [0])
        positive = max([value(l, "positive") for l in stops] or [-1])
        if worst > 32:
            problems.append(f"the HUD on − off {worst} B > 32")
        if positive < 1000:
            problems.append(f"the positive control read {positive} B")
        quiet = min([value(l, "off") for l in stops if f"scene={HUB} " in l] or [0])
        stress = next((l for l in stops if "scene=StressZeroGC " in l), None)
        players = next((value(l, "players") for l in lines if "SAMPLES tour stress " in l), None)
        if "StressZeroGC" in want:
            if stress is None or players is None or players < 1000:
                problems.append(f"Stress Zero GC at {players} players")
            elif value(stress, "off") - quiet > 32:
                problems.append(f"Stress Zero GC off {value(stress, 'off')} B over the quietest hub stop's {quiet} B")
        frames = value(game, "allocFrames") if game else None
        if game is not None and (frames is None or frames > 10):
            problems.append(f"the Showcase's game allocated in {frames} frames > 10 — {game[game.find('list='):][:200]}")
        retries = sum(1 for l in lines if "SAMPLES tour again " in l)
        summary += f" · HUD on − off ≤ {worst} B · positive {positive} B · Stress Zero GC {players} players off {value(stress, 'off') if stress else '?'} B (hub {quiet} B) · game played {value(game, 'played') if game else '?'} allocating frames {frames} · stops measured again {retries}"
    else:
        if any("recorder=False" not in l or "development build only" not in l for l in stops):
            problems.append("a release stop with a counter or without the HUD's 'development build only'")
        summary += " · counter absent · 'development build only'"
    summary += f" · engine lines {len(engine)}"
    for p in problems:
        print(f"   FAIL {label}: {p}")
    if not problems:
        print(f"   PASS {label}: {summary}")
    for e in sorted(set(engine))[:8]:
        print(f"   engine line ({label}): {e}")
    return 1 if problems else 0


if __name__ == "__main__":
    sys.exit(verdict(*sys.argv[1:5]))
