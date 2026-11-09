# Changelog

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
