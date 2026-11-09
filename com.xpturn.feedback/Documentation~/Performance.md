# Performance

Once a player has prepared, its plays allocate no managed memory, so they leave nothing for the garbage collector. This
page shows what that covers, measured in players built with IL2CPP: the allocation and time against a coroutine per
play, the cost of a play for each kind of target, how the numbers were taken, and how to take them yourself. When a
player does allocate — when it prepares, for one — is in [Concepts](Concepts.md#under-the-hood).

## What no allocation covers

In a development player, every combination of a target and a motion the package registers — 361 of them — went
through two cycles: play, pause, seek, resume, skip, play backward and stop in each stop mode; play with a listener,
skip and stop with Complete. None allocated. Lists that mix waits, loops and signals went through the same: playing
forward with a signal, a wait running out, skipping, playing backward, seeking and stopping, and loops of no length.

## Against a coroutine per play

The Stress & Zero-GC sample plays one long punch on each of its cubes, and can drive the same cubes with a coroutine
instead: one coroutine and one `WaitForSeconds` for each start, the way a hand-rolled tween is often written. In
**Played once** each cube starts once — after its start, the coroutine allocates nothing either. In **Played again
every second** every cube starts again once a second: the player restarts its punch, the coroutine starts a one-second
punch, and each coroutine start allocates 120 to 128 bytes. The garbage stays until the next collection.

| Cubes | Mode | Feedback — bytes a frame | Coroutines — bytes a frame | Feedback — ms a frame | Coroutines — ms a frame |
|---:|---|---:|---:|---:|---:|
| 100 | Played once | 0 | 0 | 0.25 ~ 0.29 | 0.10 ~ 0.11 |
| 100 | Played again every second | 0 | 200 ~ 213 | 0.23 ~ 0.28 | 0.10 ~ 0.13 |
| 1,000 | Played once | 0 | 0 | 0.90 ~ 1.43 | 0.55 ~ 0.59 |
| 1,000 | Played again every second | 0 | 2,000 ~ 2,133 | 0.99 ~ 1.35 | 0.57 ~ 0.61 |
| 5,000 | Played once | 0 | 0 | 4.12 ~ 5.10 | 1.23 ~ 1.52 |
| 5,000 | Played again every second | 0 | 10,000 ~ 10,667 | 4.36 ~ 4.93 | 1.35 ~ 1.82 |

The bytes are each frame's allocation over 300 frames, less the same run's frames with no cubes; with the players, no
frame of any window allocated. The times are the package's three profiler markers against Unity's coroutine marker. A
play does more each frame than the coroutine: it puts the value back and writes it again on top of the other plays on
that value, where the coroutine writes the position directly. The cubes are rendered children of one object, so each
write also pays Unity's cost of a moved renderer — the table below plays on bare objects and costs less.

## Cost per play by target

Each row is that many players, one item each on an object of its own, all playing at once. A play's cost is the
package's three markers over a frame, divided by the players; the frame allocation is against the same run's window
with nothing playing.

| Feedback | Motion | Players | Per play (ns) | Frame allocation |
|---|---|---:|---:|---:|
| `Transform/Position` | Punch | 100 | 302 ~ 331 | 0 B |
| `Transform/Position` | Punch | 1,000 | 315 ~ 352 | 0 B |
| `Transform/Position` | Punch | 5,000 | 621 ~ 740 | 0 B |
| `Transform/Position` in a `MotionDriver` | Oscillate | 1,000 | 147 ~ 168 | 0 B |
| `Camera/Shake` | Shake | 1,000 | 632 ~ 639 | 0 B |
| `Renderer/Material Color` (property block) | Flash | 1,000 | 732 ~ 995 | 0 B |
| `Renderer/Material Float` | Oscillate | 1,000 | 962 ~ 1,342 | 0 B |
| `Renderer/Sprite Color` | Flash | 1,000 | 347 ~ 362 | 0 B |
| `Light/Intensity` | Shake | 1,000 | 373 ~ 475 | 0 B |
| `Light 2D/Intensity` | Oscillate | 1,000 | 260 ~ 282 | 0 B |
| `UI/Color` | Punch | 1,000 | 573 ~ 720 | 0 B |
| `UI/Canvas Group Alpha` | Tween | 1,000 | 897 ~ 1,104 | 0 B |
| `UI/Color` on a TextMeshPro text | Punch | 1,000 | 809 ~ 934 | 0 B |
| `TMP/Character Offset` | Oscillate | 1,000 | 1,792 ~ 2,165 | 0 B |
| `Screen/Flash` | Flash | 1,000 | 338 ~ 344 | 0 B |
| `Screen/Floating Text` | Set | 1,000 | 539 ~ 653 | 0 B |
| `PostFX/Vignette` | Oscillate | 1,000 | 404 ~ 419 | 0 B |
| `Cinemachine/Lens FOV` | Punch | 1,000 | 252 ~ 256 | 0 B |
| `Cinemachine/Impulse` | — | 1,000 | 186 ~ 192 | 0 B |
| `Animation/Float Parameter` | Tween | 1,000 | 484 ~ 542 | 0 B |
| `Particles/Emission Rate` | Flash | 1,000 | 411 ~ 413 | 0 B |
| `Haptics/Gamepad Rumble` | Curve | 1,000 | 194 ~ 197 | 0 B |
| (the scheduler alone) | — | 1,000 | 4 | 0 B |

## How it was measured

- An Apple M4 Pro Mac, macOS; development players for Apple silicon built with IL2CPP, code generation **Faster
  runtime** and managed stripping **Minimal** — a new project's defaults; Unity 6000.3.28f1 and 6000.6.8f1, November
  2026. A range is the two versions — for the comparison, two passes of the matrix in each.
- 60 frames a second, 120 frames to settle and 300 read, with no other Unity work on the machine. The window with
  nothing playing is read twice and the smaller kept. A window more than 32 bytes over it is read again, twice at
  most, and the nearest kept; **0 B** means within those 32 bytes over 300 frames, and the comparison's **0** means no
  frame allocated at all.
- Times move between runs of the same build — by up to about half for the cost per play — so read them as a scale, not
  a promise. These are not a phone's numbers, nor a release build's: the counters need a development build.

## Measure it yourself

Import the Advanced samples, run `Tools > xpTURN > Feedback > Samples > Build Samples`, choose IL2CPP in the
Player settings and turn on **Development Build**, then build and open Stress & Zero-GC from the hub. Its meter shows
the frame's allocation against the moment you last switched between players and coroutines, the package's markers and
the main thread. In the Editor the numbers include the Editor's own work, a player built with Mono allocates
differently, and in a release build the meter reads zero.
