---
name: xpturn-feedback
description: Game feel feedback in Unity projects that use the xpTURN.Feedback package (com.xpturn.feedback) — FeedbackPlayer, presets such as Hit Light or Screen Shake, playing feedback from C# or from UI events, Unity Timeline tracks and feedbacks of your own. Use when Packages/manifest.json lists com.xpturn.feedback.
license: Apache-2.0
compatibility: Unity 6000.0 or later with com.xpturn.feedback installed.
metadata:
  package: com.xpturn.feedback
  version: "0.1.0"
---

# xpTURN.Feedback

xpTURN.Feedback plays game feel effects in Unity — a hit's flash and shake, a button's pop, a hit stop — from a
`FeedbackPlayer` component and its list of feedback items. This skill says where the facts are, what you may change in
files and what you leave to the person at the editor. The facts themselves — the types and members, every feedback, the
validator's rules — are in the documentation inside the installed package. Read them there: they match the version the
project uses.

## Before you start

1. Check that `Packages/manifest.json` lists `com.xpturn.feedback`. If it does not, the package is not installed: say
   so. Its README says how to install it (`README.md#install` in the package) — add it only when the person asks.
2. Find the installed package's folder and read the version in its `package.json` —
   [Finding the package](references/finding-the-package.md).
3. Compare that version with `metadata.version` in this file's front matter. If they differ, say so once, and where this
   skill and the installed documentation disagree, follow the documentation.
4. Read `Documentation~/index.md` in the package, then the pages the task below points at.

## How it works

A player plays a list of items — its own, or the list of the preset its **Preset** field links — and each `Play`
starts one play of the whole list. Most items are target items: each drives one value of something in the scene, such
as a transform's scale, a renderer's material color or the camera, with one motion (Punch, Shake, Flash, Tween, Spring
and the others). Actions act once when the play reaches them — a hit stop, a UnityEvent, a signal — and flow items shape
the list with waits and loops. A preset is an item list in an asset; the package comes with
<!-- gen:count:presets -->21<!-- /gen:count:presets --> of them. Read `Documentation~/Concepts.md#players-and-items` and
`Documentation~/Concepts.md#targets-and-motions` before you choose items.

## The tasks

| Task | You do | The person does | Read |
|---|---|---|---|
| Choose an effect | Pick feedbacks or a preset and say why; check the packages they need | — | `Documentation~/Feedbacks.md` · [the presets](references/authoring-files.md#the-presets) |
| A player from a preset | Add a player that links the preset to an object in a scene or prefab file | Saves the scene first, opens it after | [Writing files](references/authoring-files.md) |
| A player with items of its own | Write the steps: each item's Add menu path and its values — not the items in a file | Adds and tunes the items, previews them | `Documentation~/GettingStarted.md#the-add-menu` · `Documentation~/Feedbacks.md` |
| Play from code | C#: a `FeedbackPlayer` field and `FeedbackPlayer.Play` with `PlayArgs`, or `FeedbackPlayer.PlayAsync` to wait for the end | Drags the player into the field | `Documentation~/ScriptingAPI.md#playing` |
| Play from a UnityEvent — a Button, a Toggle, a Slider, a Timeline Signal | Pick `FeedbackPlayer.PlayFromEvent`: the version without a value for a Button or a Signal, the `bool` version for a Toggle (a play running the other way turns around where it is), the `float` version for a Slider's value as the intensity | Wires the event in the inspector | `Documentation~/ScriptingAPI.md#feedbackplayer` |
| Play on a UI pointer, selection or submit event | Say which events and actions to give a Feedback UI Trigger on the UI object | Adds it and fills its entries | `Documentation~/ScriptingAPI.md#feedbackuitrigger` |
| Many players at once | C#: channels, signals and receivers | — | `Documentation~/Concepts.md#channels-and-signals` · `Documentation~/ScriptingAPI.md#broadcasting` |
| Unity Timeline | Explain the Feedback track and its clips | Makes the tracks and clips | `Documentation~/Concepts.md#unity-timeline` · `Documentation~/ScriptingAPI.md#unity-timeline` |
| Allocation and cost | Say what allocates and what a play costs | Measures with the Stress & Zero-GC sample | `Documentation~/Performance.md` |
| A sound, a middleware event, a vibration | C#: an action or an effect of your own | Adds it from the Add menu | `Documentation~/ScriptingAPI.md#your-own-feedbacks` |
| Accessibility, strength, time | C#: categories, the time scale, the settings | Edits the settings asset | `Documentation~/ScriptingAPI.md#global` · `Documentation~/Concepts.md#categories-and-accessibility` |
| An effect that does not show | Find the cause in the documentation; ask for the validator's findings | Runs the validator | [Checking the result](references/checking.md) |

## Rules

- Do not write or edit items in a scene, prefab or asset file — a player's or a preset's `m_Items` and its
  `references:` block. They are saved in a form that is not the package's API; the person adds items in the inspector.
- Add a player to a file only as a new component that links a preset, on an object that has no player. Do not set the
  Preset field of a player that already has items — it then plays the preset's list, and its own items no longer play —
  and do not offer a second player on that object: the person decides in the editor.
- Copy a preset's GUID from [the presets](references/authoring-files.md#the-presets). A wrong GUID leaves the player
  without a preset, and the Console says nothing about it.
- Do not change the package's own files, its presets included: the installed package is not part of the project's
  assets. To change a preset, the person copies it into `Assets/`, or uses **Unlink and Copy** on one player.
- Use only the public API that `Documentation~/ScriptingAPI.md` lists. A member that is not there is not API, whatever
  the package's source shows.
- Keep a feedback of your own where builds compile it: not in a folder named `Editor` or an assembly definition for
  the Editor only, and an assembly definition that holds it references `xpTURN.Feedback`
  (`Documentation~/ScriptingAPI.md#your-own-feedbacks`).
- When you rename a feedback of your own — its class, namespace or assembly — add `[MovedFrom]`
  (`UnityEngine.Scripting.APIUpdating`) with the old names, and do not make it abstract or move it off its base.
  Scenes, prefabs and presets store each item by those names; without it their items are dropped at the next save.
- Call the API from Unity's main thread, and keep players in fields instead of looking them up every frame.

## When you are done

Say what you changed and what the person does next: the steps in the inspector, then a check. In the editor,
`Tools > xpTURN > Feedback > Validate Open Scenes` (and `Tools > xpTURN > Feedback > Validate Prefabs` for a prefab)
lists the findings — ask for them when something looks wrong. [Checking the result](references/checking.md) says where
the documentation explains them.
