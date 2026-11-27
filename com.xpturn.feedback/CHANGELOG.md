# Changelog

## [0.3.0] - 2026-11-27

The Graph Preview in the player's, preset's and clip's inspectors, a menu that picks one of an object's Feedback
Players, and fixes for turned, sought and paused plays and their sounds. Requires Unity 6000.0.85f2 or later; no package
dependencies.

### Added
- A Feedback Player field in the inspector — a UI trigger row's, a receiver's, the Flow items' and your scripts' — shows
  a menu beside it while the player's object has more than one Feedback Player, and picks one by its number and
  **Alias**. On a UI trigger row and a receiver, an empty field's menu names the player it uses: the first on its
  object. A `Flow/Play Player` header shows the player's number and alias too. The package now draws `FeedbackPlayer`
  fields, so a property drawer of your own for that type is not used.
- A preset's inspector has the **Graph Preview** below **Add Feedback**, drawn on a **Graph Player** you pick — a player
  that links the preset or holds it in its Variants at first, or any scene or prefab player. The strip and the items'
  graphs take that player's start delay, length and targets.
- A Feedback clip's Inspector has a **Graph Preview** of the values the clip's play writes, on the clip's time: its
  **Clip In**, speed, **Loop**, **Seed** and **Intensity**, and in Play mode the director's `FeedbackTimelineOptions`. A
  press moves the Timeline window's time while its preview is on.

### Changed
- A player's Inspector **Graph** is now the **Graph Preview**: a box below **Add Feedback**, always shown, that the
  switch at its top right turns on. It is off at first, and off it builds nothing; a list that writes nothing it can draw
  shows a note only.
- During the Timeline window's preview, a player's Inspector **Graph Preview** starts from the values before the
  preview, as it does during the player's own preview.

### Fixed
- A backward play turned forward during its Start Delay never completed. It now completes on the next update.
- A forward play turned backward during its Start Delay never completed after a `Seek` past the delay. It now plays
  backward from the target.
- Turning a play twice during its Start Delay lost the time left of the delay. The play now goes on as if it had not
  been turned.
- A forward play could complete on a `Seek` to its current time when the previous backward play on that player had been
  stopped from one of its last events.
- A `Seek` called from a play's own last event completed the play even when the target was before its end. The play now
  goes on from the target, and a target at the end still completes it.
- A play that ended while paused, by `Skip`, a `Seek` to its end or a Pause in its last event, left its
  `Audio/Source Control` sound paused and its `Audio/Play At Point` voice out of the pool, and its Edit-mode preview did
  not end. The sound now stops and the voice goes back to the pool.
- A Pause called from a play's own event missed the sounds that the play's next items started right after it: they
  played on while the play was paused. They now pause with the play and go on with its Resume, and stop if the play
  ends there.
- With the **Overlap** policy, a Play At Point voice of an ended play was paused by a later backward play and stayed
  paused when that play ended. It now plays on.
- An item that threw inside `Seek`, `Skip()` or a turn (`PlayFromEvent(bool)`, a UI trigger) threw out of the call and
  left the play half moved, and on a Timeline track frozen. That play now ends as `PlaybackEnd.Faulted`, the call does
  not throw, and the player's other plays and Variants still move.
- `Rebuild()` called from a Variant's prepare released the player's runtime and left the Variants' runtimes unreleased.
  It now returns false and changes nothing.
- With the **Overlap** policy, a backward play started from inside another backward play's evaluation made that play's
  later items take the new play's place for one frame.
- An instant item whose category override is Flash was not undone by a `Seek` back over it or a backward play once the
  flash limit was reached, and below the limit its undo used up one of the limit's flash starts. The undo now always
  runs and does not count toward the limit.
- A **Background** item that its **Play Direction** runs against the play jumped to where it would have finished when
  the rest of the list ended and cut it. It now ends where it was cut, as other cut **Background** items do.
- A turn (`PlayFromEvent(bool)`) called from a play's last event returned that play's handle, yet the play completed
  instead of turning. The play now turns at its end and goes back.
- A **Reverse Only** instant passed the other way — by a backward play turned forward, or a `Seek` toward the end of a
  backward play — acted again: its Unity Event was called again, and a `Flow/Play Player` played its player forward
  again. It is now undone, as other instants are.
- With **Apply Initial State**, an item in a category turned off or outside its intensity range wrote its first frame
  when the player prepared, and its target stayed there, since the plays leave the item out. Such an item now writes
  nothing, as one its direction condition leaves out does.
- A player destroyed with `DestroyImmediate` from one of its items' prepare kept what that prepare made — its items
  stayed bound to their targets — and `Rebuild()` returned true and `Play` a handle. The prepare now releases it, and
  those calls return false and `default`.
- Under the game's `AudioListener.pause`, the player's Pause did not pause the sound its `Audio/Source Control`
  started: when the listener's pause ended, the sound played while the play stayed paused, or after the play had ended
  paused. It now waits for the player's Resume, and stops if the play ends paused.
- A turn back (`PlayFromEvent(bool)`) called right after a turn the other way from one of a play's own events, or
  after a `Seek` whose sweep asked a turn, started a new play. The play started over, on the player's own list if
  `Play` had named a Variant; with **Overlap** it turned and played beside the new one; from an event at the play's
  start the call was refused with a warning while the play turned and ended. The turn back now takes the first one
  back, and the play goes on as it was. Turning a play there and back after such a `Seek` no longer turns it again at
  the next update.

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
