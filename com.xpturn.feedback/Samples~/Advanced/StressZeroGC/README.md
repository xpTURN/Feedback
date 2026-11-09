# Stress & Zero-GC

## Try

Pick a player count and watch the meter; switch re-trigger on (every cube plays again once a second), then the coroutine baseline on and off.

## See

The players are copies of one template (a cube with one long punch on itself — the package's performance test's shape), built 500 a frame, each played once, and destroyed when the count drops: that steady state is the test's. Re-trigger adds a start and a stop per cube a second. The meter's allocation is the frame's against the baseline taken when the comparison was last switched: the players stay at it, the coroutine baseline (a coroutine and a delay object per start) climbs while re-triggered. The three Feedback markers and the main thread are the profiler's — a development build reads them exactly, the editor adds its own work, a release build has none. Don't select the cubes in the Hierarchy while measuring (the inspector rebuilds what it shows). The players are made at run time — the HUD's Select Player does not pick them.

## Controls

Z · X · C · V · D-pad — player count · 2 · Gamepad West — coroutine baseline · 3 · Gamepad North — re-trigger · Mouse — buttons

## HUD

Tab · R3 — feedback on/off (Before/After)
[ ] · LB RB — intensity
, . · LT RT — time scale
Esc · Start · Select — back to the hub
H · L3 — fold the HUD
