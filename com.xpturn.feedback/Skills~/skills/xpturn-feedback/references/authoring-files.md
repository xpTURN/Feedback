# Writing files

One change is safe to make in a scene or a prefab file: a `FeedbackPlayer` that links one of the package's presets,
added to an object the file already has. Everything else — the items and their values, a new object — is done in the
editor. Ask the person to save their work in the editor first, and to close the scene or the prefab you edit.

## The steps

1. Find the object's block in the file: a line `--- !u!1 &<id>` followed by `GameObject:`, with `m_Name:` and the
   object's name. That `<id>` is the object's fileID. When two objects have the name, ask which one.
2. If the object already has a `FeedbackPlayer` — a `MonoBehaviour` block whose `m_GameObject` is the object and whose
   `m_Script` has the GUID in the snippet below — stop: do not change it, do not add another, and do not offer to do
   either in the file. Say so, and leave the choice to the person in the editor.
3. Pick a fileID for the new component that no block in the file uses, such as `7700000001`; check the file for it.
4. Add the component's line at the end of the object's `m_Component` list, and the component's block at the end of the
   file — the snippet below, with your fileID, `GAME_OBJECT_ID` replaced by the object's fileID and `PRESET_GUID` by the
   preset's GUID from the table.
5. Tell the person to open the scene or the prefab, check that the player's **Preset** field names the preset, and run
   the validator ([Checking the result](checking.md)).

```yaml
# In the object's block, at the end of its m_Component list:
  - component: {fileID: 7700000001}
# At the end of the file:
--- !u!114 &7700000001
MonoBehaviour:
  m_GameObject: {fileID: GAME_OBJECT_ID}
  m_Enabled: 1
  m_Script: {fileID: 11500000, guid: 6dc1b1a00a6a44c3b159fc76ce605832, type: 3}
  m_Preset: {fileID: 11400000, guid: PRESET_GUID, type: 2}
```

The editor gives the player's other settings their defaults and writes them out the next time it saves the file. The
player plays the preset's list; the person can make the list its own with **Unlink and Copy** under the list.

## The presets

A preset's items target the object itself or the main camera. **The object needs** names the components the object must
have for the items that target it — a renderer for a material color, for example; **Packages** names an optional
package an item needs, without which the player skips that item.

<!-- gen:presets:all -->
| Preset | GUID | Items | The object needs | Packages | Use for |
|---|---|---|---|---|---|
| Button Hover | `8b2f2ef2bd72c4997a3431923d560557` | `Transform/Scale/Punch` · `UI/Color/Punch` | Graphic | uGUI | A pointer entering a UI button — a small pop and a color pulse on its graphic. |
| Button Press | `e0b6605e91bd9476e923fb7029ab500d` | `Transform/Scale/Punch` | — | — | A UI button pressed — a quick pop. |
| Charge | `8ce8fe75936024abba047104e2bb0eda` | `Transform/Scale/Oscillate` · `Transform/Position/Shake` · `Renderer/Material Color/Flash` | Renderer | — | An attack charging up — a pulsing scale, a tremble and a flash. |
| Coin | `c7cd62fb3fd1f4d6099fc0a235c35066` | `Transform/Rotation/Tween` · `Transform/Scale/Punch` · `Renderer/Material Color/Flash` | Renderer | — | A coin collected — a spin, a pop and a flash. |
| Damage Number | `08495ce6861574def8c8b2f2fb02e452` | `Screen/Floating Text/Set` | — | uGUI | A damage number that floats up where the player plays. |
| Dash | `d3c49571644064832a52e2712af0ae2a` | `Transform/Squash & Stretch/Punch` · `Camera/Field of View/Punch` (main camera) | — | — | A dash — a squash and stretch and a kick of the camera's field of view. |
| Denied | `6c1f60750f7374a13843ac1f113300e6` | `Transform/Position/Shake` · `Renderer/Material Color/Flash` | Renderer | — | A refused action on a scene object — a shake and a flash of its material. |
| Denied (UI) | `0148b1caac8dd4097ae896607263782a` | `Transform/Position/Shake` · `UI/Color/Flash` | Graphic | uGUI | A refused action on a UI control — a shake and a flash of its graphic. |
| Explosion | `970f599475d744f64bbc829f0719c3cc` | `Camera/Shake/Shake` (main camera) · `Time/Hit Stop` · `Transform/Scale/Punch` · `Renderer/Material Color/Flash` · `Camera/Field of View/Punch` (main camera) | Renderer | — | An explosion — a camera shake, a hit stop, a pop, a flash and a kick of the camera's field of view. |
| Heal | `f467ddb045f6f46009818073f10b8688` | `Renderer/Material Color/Flash` · `Transform/Scale/Punch` | Renderer | — | Healing — a flash and a soft pop. |
| Hit Critical | `27632a35e1c394eebb9e5b10cf35db5d` | `Transform/Scale/Punch` · `Renderer/Material Color/Flash` · `Camera/Shake/Shake` (main camera) · `Time/Hit Stop` · `Transform/Position/Punch` | Renderer | — | A critical hit — a pop, a flash, a camera shake, a hit stop and a jolt. |
| Hit Heavy | `13370bb6cce334d2588399721e57b401` | `Transform/Scale/Punch` · `Renderer/Material Color/Flash` · `Camera/Shake/Shake` (main camera) · `Time/Hit Stop` | Renderer | — | A heavy hit — a pop, a flash, a camera shake and a hit stop. |
| Hit Light | `63f1730fcfd7241aba5c695cdb3376aa` | `Transform/Scale/Punch` · `Renderer/Material Color/Flash` · `Camera/Shake/Shake` (main camera) | Renderer | — | A light hit — a pop, a flash and a camera shake. |
| Hit Stop | `c5c45be4bc4cc4520abadbe006efceeb` | `Time/Hit Stop` | — | — | A short freeze of the game's time at an impact. |
| Jump | `ff7dfac056d1b460fbcb4f11ec8ef554` | `Transform/Scale/Punch` | — | — | A jump — a stretch at take-off. |
| Land | `ec1373485be004789922c20683a79b58` | `Transform/Scale/Punch` | — | — | A landing — a squash on touch-down. |
| Level Up | `3b57b8bb2d0a048f78a80c382596aceb` | `Transform/Scale/Punch` · `Transform/Position/Punch` · `Renderer/Material Color/Flash` · `Camera/Shake/Shake` (main camera) | Renderer | — | A level up — a pop, a hop, a flash and a camera shake. |
| Pickup | `ff229b61b3c6049cfbccb65ac4e2805c` | `Transform/Scale/Punch` · `Transform/Rotation/Punch` · `Renderer/Material Color/Flash` | Renderer | — | An item picked up — a pop, a twist and a flash. |
| Screen Shake Heavy | `03f0d24f1f7dc4dceb402485bf206f3f` | `Camera/Shake/Shake` (main camera) | — | — | A heavy camera shake on its own. |
| Screen Shake Light | `4ec8187164e134741be3ad40c33f2997` | `Camera/Shake/Shake` (main camera) | — | — | A light camera shake on its own. |
| Screen Shake Medium | `840313432b0fd46ed92ed2f50dd49bfc` | `Camera/Shake/Shake` (main camera) | — | — | A medium camera shake on its own. |
<!-- /gen:presets:all -->

## Items of their own

For a player with items of its own, write the steps for the person: select the object and add a player
(`Component > xpTURN > Feedback > Feedback Player`), then, for each item, press **Add Feedback** under the list, type
the item's name and pick its path — `Group/Target/Motion` for a target item, such as `Transform/Scale/Punch`, the
paths `Documentation~/Feedbacks.md` lists — and set its values. The preview bar's ▶ plays the list in edit mode.
`Documentation~/GettingStarted.md#your-first-feedback` walks through it.
