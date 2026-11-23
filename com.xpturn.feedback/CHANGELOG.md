# Changelog

## [0.2.3] - 2026-11-23

The Replay Monitor sample, Cinemachine feedbacks on a brain that updates in `FixedUpdate`, and test assemblies that
compile with newer C# compilers.

### Added
- **Replay Monitor** sample in Game Feel Labs: one video on an in-game monitor, frozen on a hit, moved between chapters and
  replayed by **Video/Player Control** items beside flashes and shakes. It needs the Video module.

### Fixed
- Camera shakes and Cinemachine lens and noise feedbacks did not reach a brain that updated its camera in `FixedUpdate`:
  always on **Fixed Update**, and on **Smart Update** for a camera following a Rigidbody without interpolation or, at 60
  fps or lower, a camera without a target that moves itself. They now reach it on every update method; a camera updated
  in `FixedUpdate` shows them a frame or two late, and code in `FixedUpdate` reads these values with the offset.
- The Physics, URP and Samples test assemblies failed with CS0012 on Roslyn 4.14 or later in a build that passes only
  direct references. They now reference `xpTURN.Feedback.Math` directly.

## [0.2.2] - 2026-11-21

Fixes for clearing missing items on a prefab, replayed floating texts and the Showcase's boss defeat.

### Fixed
- **Clear missing entries** on a prefab asked before saving, and **Don't Save** left the entries in place. It now saves
  without asking, unless a player or a Motion Driver it does not clear still holds a missing item.
- **Clear missing entries** on a Motion Driver in a prefab cleared nothing; it now clears the driver's item.
- A floating text played again in quick succession added to the list of live texts, which grew during play; a text
  reused from its pool now keeps its one entry.
- The Showcase's boss defeat cut its slow motion and color drain off after 0.4 seconds, when the boss is hidden; they
  now play their full 1.2 seconds.

## [0.2.1] - 2026-11-16

The Infinite field only where a motion keeps moving, and the validator on the rest. Requires Unity 6000.0.85f2 or later;
no package dependencies.

### Changed
- **Infinite** shows only on an item whose motion keeps moving — Oscillate, Wiggle, Shake or Blink — or that has no
  motion; another motion stays at its start on an endless item. FB-D07 now also tells an Infinite that leaves a Curve,
  Punch, Tween, Flash, Gradient or Bounce at its start, or that a Spring or a Set ignores, with **Clear Infinite** and
  **Loop with Repeat Forever**.

## [0.2.0] - 2026-11-16

Unity's audio, new motion shapes, camera trauma, player variants and graphs in the inspector. Requires Unity
6000.0.85f2 or later; no package dependencies.

### Added
- **Audio** — with the Audio module, 14 feedbacks for Unity's own audio: an Audio Source's volume, pitch and pan, the
  five audio filters, the listener's volume, playing a source or a clip, mixer parameters and snapshots, and sounds at a
  point from a pool of voices. A new category, Audio, turns them off or scales them, and the player's Pause pauses the
  sounds its plays started.
- **Sample sounds** — the gallery's Audio tab, Combat Hit Lab, Accessibility's Sound row and the Showcase play sounds
  made for them.
- **Motion reference** — a page with each motion's shape and parameters and the eases, and pictures of the shapes and
  the eases.
- **Bounce** — a new motion: the value hops up and falls back **Count** times, each hop lower, like a ball settling.
  Every target that takes a Punch takes it, and so do your own effects.
- **Steps** — a new ease that climbs in even steps; its parameter sets how many.
- **Oscillate per axis** — with **Per Axis**, a phase and a frequency multiplier for each component, for a circle or a
  figure eight.
- **Fades** — Oscillate and Wiggle fade in and out (**Fade In**, **Fade Out**, **Fade Ease**), and Shake fades in. An
  endless item, or a Motion Driver's Loop, plays without them.
- **Hit Stop Overlap** — with **Extend**, a hit stop also runs over other hit stops, so a heavy hit right after a light
  one freezes for the heavy one's length; **Skip**, the default, keeps the earlier behavior.
- **Maximum Shake Offset** and **Maximum Shake Angle** — settings that softly cap the camera shakes summed on one
  camera, so many shakes at once stay within a size you choose; 0, the default, leaves them uncapped.
- **Trauma** — `Camera/Trauma` and `Transform/Trauma` add to a camera's or any transform's trauma: hits stack up to a
  ceiling, fade on their own and shake through one noise without a jump. `FeedbackTrauma` adds, reads and clears it from
  code, a **Trauma Shaker** sets how it shakes, and the gallery shows both.
- **Variants** — a player holds presets besides its list, and each play picks one: `PlayArgs.WithPreset`,
  `PlayFromEvent(FeedbackPreset)` or a Timeline clip's **Preset**. Variants play on the player's anchors and settings,
  its **Policy**, **Cooldown** and **Toggle** span them all, the Inspector's **Show** previews each, the debugger and
  the validator read each, and FB-B10 tells a slot that plays nothing. The Unity Timeline Cutscene sample plays a light,
  a normal and a heavy hit on one player.
- **Intensity from a script** — `FeedbackPlayer.Intensity` sets the player's **Intensity**; the next plays take it.
- **Variant keys** — a preset's **Alias** and a player's own **Alias** name lists, and a text key picks a list by alias:
  `PlayArgs.WithVariant` for one play (a channel command carries it to every receiver), `FeedbackPlayer.VariantKey` for
  the plays that name no preset, `TryGetVariant` to ask. A player with no list of the key plays as it would, and FB-B11
  tells two lists of one alias.
- **Feedback Timeline Options** — next to a director, its **Variant Key** and **Intensity** reach every Feedback clip of
  the timeline in Play mode, and a clip's **Hit Key** names a row of its **Hits** — one hit of a combo heavier than the
  rest. The Unity Timeline Cutscene sample names its three hits and their lists.
- **Graphs** — an item's **Graph** draws the value it writes over its time: the motion, the ease, the timing, the
  intensity and the category's scale. The **Graph** under the timeline strip draws what the list writes to one property:
  the sum, each item alone and a camera shake's cap. Press and drag either to scrub the preview.
- **Recorded plays** — a graph that is open follows the next play of its player, in the preview or in Play mode: the
  line becomes that play's, **Dots** draws the frames it wrote, and a warning tells when the frames leave the line —
  another player, a Motion Driver or a script writes the same property.

### Changed
- The documentation no longer says the package plays no sound. The examples of feedbacks of your own call a middleware
  (`Audio/Middleware One Shot`, `Audio/Middleware Parameter`); a feedback made from the earlier `Audio/Mixer Parameter`
  example now sits beside the package's in the Add menu, with its class name after it — give it a path of its own.
- The Feedback Gallery, Combat Hit Lab, Accessibility and Showcase samples need the Audio module.
- On a player with Variants, `Stop`, `Pause`, `Resume`, `IsPlaying`, `Seek`, `Skip`, `Restore` and `Signal` reach the
  plays of every list, and **Restart** stops them too; a player without Variants plays as before.
- Of several hit stops at once, the slowest now holds time, each to its own end; a later, weaker one no longer lifts a
  freeze for its length.
- The random parts of a play given a seed change once: each item now takes them from itself rather than from its place
  in the list, so adding, removing or moving items keeps the others'. A copy made with Duplicate, Paste, Save As Preset
  or Unlink takes new ones.
- The item list no longer colors its selected row.

### Fixed
- A player with a running hit stop, played again under **Restart**, let time run on in Play mode; the new play's hit
  stop now holds the freeze.
- A floating text played with the same seed took another lifetime, angle and scatter each time; the same seed now gives
  the same text.
- A Motion Driver's inspector showed an empty item after scripts reloaded.

## [0.1.0] - 2026-11-08

The first public release. Requires Unity 6000.0.85f2 or later; no package dependencies.

### Added
- **Feedback Player** — a component that holds a list of feedback items and plays them from one scheduler in the
  PlayerLoop. Most items are a target (the value it writes) with a motion (how that value moves over time); the rest
  are actions and steps that shape the list. Play it from code with `PlayArgs` (position, intensity) or from a
  UnityEvent.
- **Targets and motions** — 141 targets take 11 motions in 361 combinations. Shakes, punches and flashes write on top
  of your values and put them back; tweens and sets leave a value where they end.
- **No allocation per play** once a player is warmed up, measured against a coroutine per play in the Performance page.
- **Edit-mode preview** — ▶ on a player plays it in the Scene and Game views; the values come back when it stops.
- **The Add menu** — 377 entries with search, favorites and recent items.
- **Presets** — 21 presets to drop on an object or link from a player.
- **Feedback Validator** (`Window > xpTURN > Feedback Validator`) with one-click fixes, and **Feedback Debugger**
  (`Window > xpTURN > Feedback Debugger`) for what is playing now.
- **Optional groups** that compile when their package or built-in module is installed: Universal RP (volumes, Light
  2D), uGUI and TextMesh Pro, Cinemachine 3, Input System (gamepad rumble), UniTask, Timeline (a Feedback track and
  clips, previewed in the Timeline window), and the Animation, Particle System, Physics, Physics 2D and Video modules.
- **A build step** that keeps the engine methods called by UnityEvents inside feedback items from IL2CPP stripping.
- **Samples** — five bundles (Basics, Game Feel Labs, UI, Advanced, Showcase) and a hub scene that reaches every
  imported sample.
- **Documentation** — Getting Started, Concepts, Scripting API, Feedback reference and Performance, in
  `Documentation~`.
- **A Claude Code skill**, `xpturn-feedback`, in `Skills~`.
