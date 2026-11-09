# Checking the result

You cannot see the editor. After a change, ask the person for what the editor shows, and read in the installed
documentation what it means.

## In the editor

- **Compile errors** are in the Console. A script that names a member `Documentation~/ScriptingAPI.md` does not list is
  the usual cause.
- **The validator** — `Tools > xpTURN > Feedback > Validate Open Scenes`, `Tools > xpTURN > Feedback > Validate Prefabs`
  and `Tools > xpTURN > Feedback > Validate Presets` write a count to the Console and open
  `Window > xpTURN > Feedback Validator` with the findings. Each finding has a rule id such as `FB-B08`;
  `Documentation~/Feedbacks.md#validation-rules` lists the rules by letter, and the editor's message says what the rule
  saw. `FB-B08` on a player you linked to a preset means the preset's GUID is wrong: copy it again from
  [the presets](authoring-files.md#the-presets).
- **The debugger** — `Window > xpTURN > Feedback Debugger` shows what plays, in Play mode or in an edit-mode preview:
  **Playbacks** lists each play with its position and state, and **Events** keeps the last events with each end's
  reason. Ask whether the play is there and how it ended. `Documentation~/GettingStarted.md#the-debugger` says what
  each panel shows, and `Documentation~/ScriptingAPI.md#playbackend` lists the end reasons.
- **The preview** — the player's ▶ plays its list in edit mode. Some feedbacks play only in Play mode: the Preview
  column of `Documentation~/Feedbacks.md` says which, and `Documentation~/Concepts.md#edit-mode-preview` says why.

## When an effect does not show

Read, in this order:

1. The validator's findings for the player.
2. The debugger: whether the play started, and the end reason in **Events** when it ended early.
3. The feedback's row and its group's notes in `Documentation~/Feedbacks.md` — the package it needs and what its target
   must be.
4. `Documentation~/Concepts.md#how-values-are-written` — a value another script or another player writes too.
5. `Documentation~/Concepts.md#time` — a player whose time follows `Time.timeScale` while the game's time scale is 0.
6. `Documentation~/Concepts.md#categories-and-accessibility` — a category turned off or scaled down.
7. `README.md#good-to-know` — builds, the linker and AssetBundles.
