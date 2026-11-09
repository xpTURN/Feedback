# Feedback reference

Every feedback in the Add menu, by group: <!-- gen:count:feedbacks -->157<!-- /gen:count:feedbacks --> feedbacks —
<!-- gen:count:targets -->141<!-- /gen:count:targets --> targets that take motions, in
<!-- gen:count:combinations -->361<!-- /gen:count:combinations --> combinations, and the actions and flow items that act
once when they are reached. The Add menu lists them as <!-- gen:count:leaves -->377<!-- /gen:count:leaves --> entries,
one for each target and motion and one for each action. Each group's table is made from the package itself; the notes
under it say what to know before you use the group.

**The columns**

- **Feedback** — its path in the Add menu.
- **Motions** — the motions the target takes. An action or a flow item has none (—): it acts once. See
  [Targets and motions](Concepts.md#targets-and-motions).
- **Value** — the kind of value the target writes.
- **Category** — the feedback's own effect category, the one `FeedbackCategories` turns off or scales (— for none). An
  effect counts in its item's category override when one is set, else in Flash for a flash or blink of a flash-sensitive
  target, else in this one; Particles and UI come only from an override. See
  [Categories and accessibility](Concepts.md#categories-and-accessibility).
- **Preview** — what the inspector's preview does with it in Edit mode. **Full** plays it as Play mode does, and the
  preview can scrub it. **Simulated** plays it on the editor's clock — particles and animators without scrubbing.
  **Display only** shows it without its side effects — a hit stop slows the preview's clock, a force draws an arrow, an
  event logs a line instead of calling. **None** plays in Play mode only. See
  [Edit-mode preview](Concepts.md#edit-mode-preview).
- **Needs** — the package or module the feedback needs, as the README's Requirements name it. Without it the feedback
  stays in the Add menu, dimmed, and a player skips it.
- **Description** — what you see when it plays.

## Animation

<!-- gen:reference:Animation -->
| Feedback | Motions | Value | Category | Preview | Needs | Description |
|---|---|---|---|---|---|---|
| `Animation/Float Parameter` | Tween | Float | — | Simulated | Animation module | Drives an Animator float parameter. |
| `Animation/Layer Weight` | Tween | Float | — | Simulated | Animation module | Fades an Animator layer's weight in or out. |
| `Animation/Parameter` | Set | Bool | — | Simulated | Animation module | Sets an Animator parameter. |
| `Animation/Play State` | Set | Bool | — | Simulated | Animation module | Plays an Animator state with a transition. |
| `Animation/Speed` | Tween · Punch · Spring | Float | — | Simulated | Animation module | Speeds the Animator up and down. |
<!-- /gen:reference:Animation -->

**Notes**

- A write to a parameter the controller lacks or has as another kind has no effect; FB-L02 reports these and a parameter
  a clip's curve drives, which the runtime does not check.
- An item whose Animator's object is inactive when the player prepares writes nothing while the object stays inactive and
  plays once it is active; in a built player the inactive Animator lists no parameters, so a parameter it lacks is found
  at that first play and the item writes nothing. `Animation/Layer Weight` is skipped instead: prepare its player after
  the Animator is active (a **Lazy** player's first play, or **Rebuild**).
- On an Animator that updates in Animate Physics mode, an `Animation/Speed` Punch or a spring's kick shows only with
  **Direct Write**; a Tween shows either way. FB-L05 offers the fix.
- FB-L06 reports an Animator set to cull completely. The inspector's preview drives its parameters whatever the culling
  and never changes the culling mode.
- The inspector's preview logs the controller's state machine behaviours once — FB-L08. After the preview the pose comes
  back, and so do a generic rig's root position and rotation; a preview that a script reload cuts off before it can end
  brings the pose back but leaves the speed, parameters and event firing as it left them.

## Camera

<!-- gen:reference:Camera -->
| Feedback | Motions | Value | Category | Preview | Needs | Description |
|---|---|---|---|---|---|---|
| `Camera/Background Color` | Tween · Flash | Color | Camera | Full | — | Tints the camera's background color. |
| `Camera/Clipping Planes` | Tween | Vector2 | Camera | Full | — | Moves the camera's near and far planes. |
| `Camera/Field of View` | Tween · Punch · Shake · Spring | Float | Camera | Full | — | Kicks the camera's field of view. |
| `Camera/Orthographic Size` | Tween · Punch · Shake · Spring | Float | Camera | Full | — | Changes an orthographic camera's size. |
| `Camera/Shake` | Punch · Shake · Curve · Wiggle | Vector3 | Camera | Full | — | Shakes the camera's position — and its rotation, given a rotation amplitude. |
| `Camera/Zoom` | Tween · Punch | Float | Camera | Full | — | Zooms the camera in or out; a Punch comes back, a Tween stays. |
<!-- /gen:reference:Camera -->

**Notes**

- A Main Camera target follows the main camera: after it changes, the next play moves the new camera, and Persist values
  left on the old one stay there. Under **Overlap**, plays started while one of the item runs stay on the old camera;
  under **Restart**, the item moves to the new one.
- Without a main camera when the player prepares, plays do nothing for that item until a camera exists.
- `Camera/Zoom` reads whether the camera is perspective or orthographic when the player prepares; after switching the
  projection, call `FeedbackPlayer.Rebuild`.
- On a camera that a Cinemachine brain drives, FB-H05 reports a lasting Field of View, Orthographic Size or Zoom — the
  brain writes the lens. Use the Cinemachine lens feedbacks there.
- `Camera/Background Color` is for a camera that clears to a color — FB-H04 reports one that draws a skybox.
- A Motion Driver on the main camera keeps the camera it found until it is enabled again; without a main camera at its
  enable it warns and does not start.

## Cinemachine

<!-- gen:reference:Cinemachine -->
| Feedback | Motions | Value | Category | Preview | Needs | Description |
|---|---|---|---|---|---|---|
| `Cinemachine/Activate Camera` | Set | Bool | — | Display only | Cinemachine | Makes a Cinemachine camera the live one, blending to it. |
| `Cinemachine/Impulse` | — | — | Camera | Full | Cinemachine | Sends an impulse to Cinemachine's listeners, scaled by the play's intensity. |
| `Cinemachine/Impulse Clear` | — | — | — | Full | Cinemachine | Clears every Cinemachine impulse. |
| `Cinemachine/Lens Dutch` | Tween · Punch · Spring | Float | Camera | Full | Cinemachine | Tilts a Cinemachine camera's lens (its Dutch angle). |
| `Cinemachine/Lens FOV` | Tween · Punch · Spring | Float | Camera | Full | Cinemachine | Kicks a Cinemachine camera's field of view. |
| `Cinemachine/Lens Orthographic Size` | Tween · Punch · Spring | Float | Camera | Full | Cinemachine | Changes a Cinemachine camera's orthographic size. |
| `Cinemachine/Noise Amplitude Gain` | Tween · Punch · Flash | Float | Camera | Simulated | Cinemachine | Raises or lowers a Cinemachine camera's noise amplitude. |
| `Cinemachine/Noise Frequency Gain` | Tween · Punch · Flash | Float | Camera | Simulated | Cinemachine | Speeds up or slows down a Cinemachine camera's noise. |
<!-- /gen:reference:Cinemachine -->

**Notes**

- Camera shakes reach a Cinemachine camera through a
  [`CinemachineShakeExtension`](ScriptingAPI.md#cinemachineshakeextension) on it.
- `Cinemachine/Impulse` moves only the cameras whose impulse listener takes its channel — FB-R03 reports an impulse no
  listener hears. `Cinemachine/Impulse Clear` clears every impulse on every channel, others' too.
- The noise gains need a noise profile on the camera — FB-R02. A component that writes the lens as well (Follow Zoom,
  Recomposer, FreeLook Modifier) can cover the lens feedbacks — FB-R04.
- `Cinemachine/Activate Camera`'s blend, when it overrides, wins over the brain's custom blends. The inspector's preview
  does not switch cameras.
- A play keeps the Cinemachine camera it started on when another one goes live.
- Save During Play keeps no value a feedback wrote.

## Custom

<!-- gen:reference:Custom -->
| Feedback | Motions | Value | Category | Preview | Needs | Description |
|---|---|---|---|---|---|---|
| `Custom/Color Value` | Tween · Flash · Gradient · Spring | Color | — | Full | — | Drives a color of your own component through IFeedbackTarget<Color>. |
| `Custom/Float Event` | Tween · Punch · Shake · Curve · Oscillate | Float | — | Full | — | Sends a float to a UnityEvent<float> over time. |
| `Custom/Float Value` | Set · Tween · Punch · Shake · Curve · Flash · Oscillate · Wiggle · Spring | Float | — | Full | — | Drives a float of your own component through IFeedbackTarget<float>. |
| `Custom/Vector3 Value` | Tween · Punch · Shake · Curve · Oscillate · Wiggle · Spring | Vector3 | — | Full | — | Drives a Vector3 of your own component through IFeedbackTarget<Vector3>. |
<!-- /gen:reference:Custom -->

**Notes**

- The value feedbacks drive a component of yours through [`IFeedbackTarget<T>`](ScriptingAPI.md#ifeedbacktargett).
- A setter that throws detaches that target only — logged once — and the play goes on; a throw in a lasting (Persist)
  write ends the play, and a getter that throws at the start ends it Faulted.
- In the inspector's preview, `Custom/Float Event` calls nothing unless **Invoke In Preview** is on — then the listeners
  set to **Editor And Runtime**, not those set to **Runtime Only**.

## Flow

<!-- gen:reference:Flow -->
| Feedback | Motions | Value | Category | Preview | Needs | Description |
|---|---|---|---|---|---|---|
| `Flow/Debug` | — | — | — | Display only | — | Logs, warns, errors or pauses the editor when it is reached — in development builds only, by default. |
| `Flow/Hold` | — | — | — | Full | — | Makes the next item wait until everything before it has ended, plus its own delay. |
| `Flow/Loop End` | — | — | — | Full | — | Loops the items since Loop Start. |
| `Flow/Loop Start` | — | — | — | Full | — | Marks where a loop of items begins. |
| `Flow/Play Player` | — | — | — | Full | — | Plays another Feedback Player. |
| `Flow/Player Chain` | — | — | — | Full | — | Plays other players one after another. |
| `Flow/Player Control` | — | — | — | Full | — | Plays, stops, pauses or otherwise controls other players. |
| `Flow/Random Player` | — | — | — | Full | — | Plays one of several players by weight. |
| `Flow/Random Unity Event` | — | — | — | Display only | — | Invokes one of several UnityEvents by weight. |
| `Flow/Signal Broadcast` | — | — | — | Display only | — | Sends a signal to the receivers listening. |
| `Flow/Unity Event` | — | — | — | Display only | — | Invokes a UnityEvent when it is reached. |
| `Flow/Wait` | — | — | — | Full | — | Waits before the next item. |
| `Flow/Wait For Signal` | — | — | — | Display only | — | Waits for a signal, up to its timeout. |
<!-- /gen:reference:Flow -->

**Notes**

- `Flow/Play Player` with **Wait** on (the default) waits for the play it starts: pausing the parent does not pause that
  child, and a busy child whose **Policy** is Ignore refuses, ending the node at once. The node ends at the next tick
  after the child on the same scheduler, or inside the parent's `Play()` when the child ends at once. A listener cannot
  be attached to the waited play, and under overlapping parents a **Restart** child ends the earlier wait early.
- `Skip()` on a play that waits on its child stops it — it ends Stopped; skipping the parent ends the waited child with
  Complete.
- A waiting item in the background does not wait — the list's end ends it (FB-O07).

## GameObject

<!-- gen:reference:GameObject -->
| Feedback | Motions | Value | Category | Preview | Needs | Description |
|---|---|---|---|---|---|---|
| `GameObject/Component Enabled` | Set · Blink | Bool | — | Full | — | Turns a component on and off. |
| `GameObject/Despawn` | Set | Bool | — | Display only | — | Destroys the target, or returns it to its pool. |
| `GameObject/Set Active` | Set · Blink | Bool | — | Full | — | Turns the target on and off. |
| `GameObject/Set Layer` | Set | Bool | — | Full | — | Moves the target to another layer. |
| `GameObject/Spawn` | Set | Bool | — | Display only | — | Spawns a prefab at the target. |
<!-- /gen:reference:GameObject -->

**Notes**

- Set Active's **Toggle** flips the play's start state when its value is not 0 and keeps it at 0; the intensity does not
  enter.
- When Play mode ends during a Blink, the object it turned off comes back on: its `OnEnable` runs once more, and a
  `Play()` from there is refused.
- A toggle that a play lets go of mid-effect is put back at the start of the next frame; a normal end puts it back in
  the Update stage, so the object gets that frame's `LateUpdate`.
- `GameObject/Despawn` returns a live pooled instance to its pool and destroys anything else; in Play mode nothing
  undoes it — Revert, Restore or a backward play. The preview only hides the object. FB-J05 reports a despawn of its own
  player's object or a parent of it: the rest of the play does not run.
- A player inside a prefab that `GameObject/Spawn` pools prepares at its first take-out. **Restart** and
  `FeedbackPlayer.Rebuild` keep what an earlier play spawned.

## Haptics

<!-- gen:reference:Haptics -->
| Feedback | Motions | Value | Category | Preview | Needs | Description |
|---|---|---|---|---|---|---|
| `Haptics/Gamepad Rumble` | Tween · Curve · Flash | Vector2 | Haptics | None | Input System | Rumbles the gamepad's motors — a pad is needed. |
<!-- /gen:reference:Haptics -->

**Notes**

- Rumble plays in Play mode only. Its clock is unscaled by default, so a pause menu at time scale 0 does not hold the
  motors.
- **Device** picks the pad: **Current**, **All**, or **Player** — the pad paired with the PlayerInput on the **Player**
  reference (the parent by default). Without a PlayerInput there the item is refused when the player prepares; with one
  but no pad paired, no pad rumbles.
- Turning the Haptics category off stops a running rumble, a Tween's too; the category's scale does not reach a Tween.
- A Tween's end does not stay — the motors stop. A Tween To ignores the intensity; By takes it. Pausing the player sends
  0 to a Tween and resumes it; a Curve holds.
- Of two Tweens on one motor through the same **Device**, the later write wins; through different ones that reach one
  pad, they add up, kept within full strength. While the value is not 0, it is sent again every 5 seconds.

## Light

<!-- gen:reference:Light -->
| Feedback | Motions | Value | Category | Preview | Needs | Description |
|---|---|---|---|---|---|---|
| `Light/Color` | Tween · Flash · Gradient · Spring | Color | — | Full | — | Tints the light. |
| `Light/Intensity` | Tween · Punch · Shake · Flash · Oscillate · Blink · Spring | Float | — | Full | — | Flashes the light's intensity. |
| `Light/Range` | Tween · Punch · Oscillate · Spring | Float | — | Full | — | Changes the light's range. |
| `Light/Shadow Strength` | Tween | Float | — | Full | — | Changes the light's shadow strength. |
| `Light/Spot Angle` | Tween | Float | — | Full | — | Changes a spot light's angle. |
<!-- /gen:reference:Light -->

## Light 2D

<!-- gen:reference:Light 2D -->
| Feedback | Motions | Value | Category | Preview | Needs | Description |
|---|---|---|---|---|---|---|
| `Light 2D/Color` | Tween · Flash | Color | — | Full | Universal RP | Tints a 2D light's color. |
| `Light 2D/Falloff` | Tween · Oscillate | Float | — | Full | Universal RP | Changes a 2D light's falloff intensity. |
| `Light 2D/Intensity` | Tween · Flash · Oscillate · Blink | Float | — | Full | Universal RP | Brightens, dims or blinks a 2D light. |
| `Light 2D/Radius` | Tween · Oscillate | Float | — | Full | Universal RP | Grows or shrinks a point 2D light's radius. |
<!-- /gen:reference:Light 2D -->

**Notes**

- Light 2D shows on URP's 2D Renderer.
- `Light 2D/Radius` is a point light's outer radius; the inner radius is pushed below it as needed, and both come back
  at the end. An inner radius above the outer one puts the light out.
- `Light 2D/Falloff` is the falloff intensity, kept within 0 to 1; a shape light's falloff size is not a target.
- To turn a 2D light on and off, use `GameObject/Component Enabled`.

## Particles

<!-- gen:reference:Particles -->
| Feedback | Motions | Value | Category | Preview | Needs | Description |
|---|---|---|---|---|---|---|
| `Particles/Control` | Set | Bool | — | Simulated | Particle System module | Plays, pauses, stops or clears a particle system. |
| `Particles/Emission Rate` | Tween · Flash | Float | — | Simulated | Particle System module | Changes a particle system's emission rate. |
| `Particles/Emit` | Set | Bool | — | Simulated | Particle System module | Emits a burst of particles. |
| `Particles/Simulation Speed` | Tween · Flash | Float | — | Simulated | Particle System module | Speeds a particle system up and down. |
| `Particles/Spawn` | Set | Bool | — | Display only | Particle System module | Spawns a particle prefab at the target. |
<!-- /gen:reference:Particles -->

**Notes**

- A looping `Particles/Spawn` without **Stop After** never ends, so it never goes back to the pool — FB-K03 offers Stop
  after 1 s. With it, a looping instance off screen goes back without its tail.
- When the inspector's preview ends, the systems it played stop and clear. A preview that a script reload cuts off
  before it can end leaves its particle systems as they were.

## Physics

<!-- gen:reference:Physics -->
| Feedback | Motions | Value | Category | Preview | Needs | Description |
|---|---|---|---|---|---|---|
| `Physics/Explosion` | Set | Bool | — | Display only | Physics module | Pushes the bodies around the target outward. |
| `Physics/Force` | Set | Bool | — | Display only | Physics module | Pushes or spins a Rigidbody. |
| `Physics/Velocity` | Set | Bool | — | Display only | Physics module | Sets or adds to a Rigidbody's velocity. |
<!-- /gen:reference:Physics -->

**Notes**

- An instant with no delay runs inside `Play()`, wherever that is called; with a delay, in the feedback tick.
- A force or an impulse lands at the next physics step; a velocity write takes effect at once. The **Force** and
  **Acceleration** modes act for one step only — FB-M03.
- Local space uses the Rigidbody's rotation, not a turn of its Transform alone.
- `Physics/Explosion` measures each body's distance from its closest collider point. In Play mode it queries the last
  pose the physics scene was synced to; the preview syncs first.

## Physics 2D

<!-- gen:reference:Physics 2D -->
| Feedback | Motions | Value | Category | Preview | Needs | Description |
|---|---|---|---|---|---|---|
| `Physics 2D/Force` | Set | Bool | — | Display only | Physics 2D module | Pushes or spins a Rigidbody2D. |
| `Physics 2D/Velocity` | Set | Bool | — | Display only | Physics 2D module | Sets or adds to a Rigidbody2D's velocity. |
<!-- /gen:reference:Physics 2D -->

**Notes**

- An Impulse sets the velocities at once; a Force adds up until the next physics step.
- Torque is in radians; angular velocity reads in degrees per second.

## PostFX

<!-- gen:reference:PostFX -->
| Feedback | Motions | Value | Category | Preview | Needs | Description |
|---|---|---|---|---|---|---|
| `PostFX/Bloom` | Tween · Punch · Curve · Flash · Oscillate | Float | PostFX | Full | Universal RP | Blooms the screen (URP volume). |
| `PostFX/Channel Mixer` | Tween · Flash | Float | PostFX | Full | Universal RP | Mixes the color channels (URP volume). |
| `PostFX/Chromatic Aberration` | Tween · Punch · Curve · Flash · Oscillate | Float | PostFX | Full | Universal RP | Splits the colors at the edges (URP volume). |
| `PostFX/Color Adjustments` | Tween · Punch · Curve · Flash · Oscillate | Float | PostFX | Full | Universal RP | Adjusts exposure, contrast and saturation (URP volume). |
| `PostFX/Depth of Field` | Tween · Flash | Float | PostFX | Full | Universal RP | Blurs by distance (URP volume). |
| `PostFX/Film Grain` | Tween · Flash | Float | PostFX | Full | Universal RP | Adds film grain (URP volume). |
| `PostFX/Lens Distortion` | Tween · Punch · Flash · Oscillate | Float | PostFX | Full | Universal RP | Bends the image like a lens (URP volume). |
| `PostFX/Lift Gamma Gain` | Tween · Flash | Float | PostFX | Full | Universal RP | Grades shadows, midtones and highlights (URP volume). |
| `PostFX/Motion Blur` | Tween · Flash | Float | PostFX | None | Universal RP | Blurs with the camera's motion (URP volume). |
| `PostFX/Panini Projection` | Tween · Flash | Float | PostFX | Full | Universal RP | Projects a wide view (URP volume). |
| `PostFX/Shadows Midtones Highlights` | Tween · Flash | Float | PostFX | Full | Universal RP | Tints shadows, midtones and highlights (URP volume). |
| `PostFX/Split Toning` | Tween · Flash | Float | PostFX | Full | Universal RP | Tones the shadows and highlights apart (URP volume). |
| `PostFX/Vignette` | Tween · Punch · Flash · Oscillate | Float | PostFX | Full | Universal RP | Darkens the screen's edges (URP volume). |
| `PostFX/Volume Blend` | Tween · Flash | Float | PostFX | Full | Universal RP | Blends in a volume profile of your own. |
| `PostFX/Volume Weight` | Tween · Flash | Float | PostFX | Full | Universal RP | Fades a volume's weight. |
| `PostFX/White Balance` | Tween · Flash | Float | PostFX | Full | Universal RP | Warms or cools the image (URP volume). |
<!-- /gen:reference:PostFX -->

**Notes**

- Each item writes an overlay volume of its own, taken when the player prepares, on the settings' **Post FX Layer**, as
  a weight kept within 0 to 1; only the fields an item chooses change, and the others pass through from your volumes.
  FB-Q02 reports a layer outside the main camera's volume mask.
- Overlapping plays combine as 1 − (1 − a)(1 − b), never past full weight.
- A field that does not interpolate takes the item's value while the weight is above 0 and goes back at 0.
- A Tween's end stays — a Revert after the end changes nothing — and a lasting end goes with its player.
- Motion Blur shows in Play mode only, and the Scene view misses four effects; a notice says which.
- Cameras whose volumes update via scripting are updated too.
- A flash of a flash-sensitive effect counts in the Flash category, so turning PostFX off does not stop it.

## Rect Transform

<!-- gen:reference:Rect Transform -->
| Feedback | Motions | Value | Category | Preview | Needs | Description |
|---|---|---|---|---|---|---|
| `Rect Transform/Anchors` | Tween | Vector4 | Motion | Full | — | Moves a UI element's anchors. |
| `Rect Transform/Offsets` | Tween | Vector4 | Motion | Full | — | Moves a UI element's edges. |
| `Rect Transform/Pivot` | Tween | Vector2 | Motion | Full | — | Moves a UI element's pivot. |
| `Rect Transform/Size Delta` | Tween · Punch · Spring | Vector2 | Motion | Full | — | Resizes a UI element. |
<!-- /gen:reference:Rect Transform -->

## Renderer

<!-- gen:reference:Renderer -->
| Feedback | Motions | Value | Category | Preview | Needs | Description |
|---|---|---|---|---|---|---|
| `Renderer/Ambient Color` | Tween | Color | — | Full | — | Tints the scene's ambient light. |
| `Renderer/Ambient Intensity` | Tween | Float | — | Full | — | Brightens the scene's ambient light. |
| `Renderer/Emission` | Tween · Flash · Oscillate · Blink | Color | — | Full | — | Makes the material glow — its emission keyword must be on. |
| `Renderer/Fog Color` | Tween · Flash | Color | — | Full | — | Tints the scene's fog. |
| `Renderer/Fog Density` | Tween · Flash | Float | — | Full | — | Thickens the scene's fog. |
| `Renderer/Line Color` | Tween · Punch | Color | — | Full | — | Tints one end of a Line Renderer — its start or its end color. |
| `Renderer/Line Width` | Tween · Punch | Float | — | Full | — | Widens a Line Renderer. |
| `Renderer/Material Color` | Tween · Punch · Flash · Gradient · Oscillate · Blink · Spring | Color | — | Full | — | Flashes the material's color. |
| `Renderer/Material Float` | Tween · Punch · Shake · Curve · Flash · Oscillate · Spring | Float | — | Full | — | Drives a float property of the material. |
| `Renderer/Material Swap` | Set | Bool | — | Full | — | Swaps the material for another. |
| `Renderer/Material Vector` | Tween · Curve · Oscillate | Vector4 | — | Full | — | Drives a vector property of the material's shader. |
| `Renderer/Shader Global Color` | Set · Tween · Curve | Color | — | Full | — | Sets a global shader color (for shaders that read it). |
| `Renderer/Shader Global Float` | Set · Tween · Curve | Float | — | Full | — | Sets a global shader float (for shaders that read it). |
| `Renderer/Shader Global Vector` | Set · Tween · Curve | Vector4 | — | Full | — | Sets a global shader vector (for shaders that read it). |
| `Renderer/Skybox Exposure` | Tween · Oscillate | Float | — | Full | — | Brightens the skybox. |
| `Renderer/Skybox Rotation` | Tween · Oscillate | Float | — | Full | — | Turns the skybox. |
| `Renderer/Skybox Tint` | Tween · Oscillate | Color | — | Full | — | Tints the skybox. |
| `Renderer/Sprite Alpha` | Tween · Flash · Blink · Spring | Float | — | Full | — | Fades a sprite. |
| `Renderer/Sprite Color` | Tween · Flash · Gradient · Blink · Spring | Color | — | Full | — | Tints a sprite. |
| `Renderer/Sprite Flip` | Set | Bool | — | Full | — | Flips a sprite. |
| `Renderer/Sprite Sheet` | Tween | Float | — | Full | — | Plays sprite frames. |
| `Renderer/Sprite Swap` | Set | Bool | — | Full | — | Swaps a sprite for another. |
| `Renderer/Texture Offset` | Tween · Curve · Oscillate | Vector2 | — | Full | — | Scrolls the material's texture. |
| `Renderer/Texture Scale` | Tween · Curve · Oscillate | Vector2 | — | Full | — | Scales the material's texture. |
| `Renderer/Trail Clear` | Set | Bool | — | Full | — | Clears a Trail Renderer. |
| `Renderer/Trail Emitting` | Set | Bool | — | Full | — | Turns a Trail Renderer's emitting on and off. |
| `Renderer/Trail Time` | Set · Tween | Float | — | Full | — | Lengthens a Trail Renderer. |
| `Renderer/Trail Width` | Set · Tween | Float | — | Full | — | Widens a Trail Renderer. |
| `Renderer/Visibility` | Set · Blink | Bool | — | Full | — | Hides and shows a renderer. |
<!-- /gen:reference:Renderer -->

**Notes**

- **Write Mode** chooses how a material feedback writes. With **Property Block** (the default), a revert puts the
  renderer's MaterialPropertyBlock back as it was before the feedback wrote it that frame — a property another script
  set on it after the package's write, before the next frame, is erased; one set in `Update` or `LateUpdate` stays, and
  a script that removes the block is left alone.
- With **Instance**, the renderer gets one copy of each material its items write, its name ending in (Feedback), shared
  by its Instance items, which write only to it; the copy goes when the last of them lets go — a rebuild, a release or a
  destroy — so a material your game assigns in the meantime stays. Do not call `renderer.material` on that renderer
  while its player is prepared: the feedbacks keep writing their own copy.
- `Renderer/Emission` writes the emission color; neither mode turns the emission keyword (`_EMISSION`) on, and FB-F08
  reports a material with it off. A keyword's toggle property changes the value only, not the keyword.
- If your game turns a renderer off while a Blink shows it, it stays off; a turn-off during a blink's off frame is lost.
  `Renderer/Visibility` and `GameObject/Component Enabled` on one renderer share its switch; `GameObject/Set Active` is
  separate.
- `Renderer/Ambient Color` writes the ambient light color — FB-I03 reports an ambient feedback the scene's ambient
  source does not read. The ambient probe follows and comes back.
- A preview that a script reload cuts off before it can end gets the scene's render settings back, not shader globals.
- Sprite colors are not clamped. `Renderer/Line Color` reads the Line Renderer's colors back, and its end and revert use
  those values; on a gradient with more than two keys it changes only the end keys — FB-F14.

## Screen

<!-- gen:reference:Screen -->
| Feedback | Motions | Value | Category | Preview | Needs | Description |
|---|---|---|---|---|---|---|
| `Screen/Fade` | Tween | Float | — | Full | uGUI | Covers the screen — another fade takes it back. |
| `Screen/Flash` | Flash · Blink | Float | — | Full | uGUI | Flashes the whole screen. |
| `Screen/Floating Text` | Set | Bool | — | Display only | uGUI | Pops a text on the screen (TextMesh Pro). |
<!-- /gen:reference:Screen -->

**Notes**

- The screen overlay belongs to the play session: a cover stays after its player goes, and goes when Play mode ends or
  scripts reload.
- It sits on the UI layer, does not block input, shows in the Game view, and sorts by the settings'
  **Overlay Sorting Order**.
- `Screen/Fade` is a Tween: Tween To, From and From To fade to their values whatever the intensity, and only Tween By
  scales its change with it. A new Fade is a Tween To. `Screen/Flash` scales its opacity with the intensity, up to full.
- `Screen/Floating Text` is drawn in screen space — no occlusion or perspective — and hides when its place is behind the
  camera. Its **Source** is a **Text**, or the play's **Intensity** times **Multiplier** written with **Format**;
  `PlayArgs` carries no text. Prefer a static font — FB-P09.
- The bar component is [`ProgressBar`](ScriptingAPI.md#progressbar).

## TMP

<!-- gen:reference:TMP -->
| Feedback | Motions | Value | Category | Preview | Needs | Description |
|---|---|---|---|---|---|---|
| `TMP/Character Alpha` | Tween · Oscillate | Float | — | Full | uGUI | Fades a text's characters one by one. |
| `TMP/Character Offset` | Tween · Oscillate · Wiggle | Vector2 | Motion | Full | uGUI | Moves a text's characters one by one. |
| `TMP/Character Scale` | Tween · Oscillate · Wiggle | Float | Motion | Full | uGUI | Scales a text's characters one by one. |
| `TMP/Character Spacing` | Tween · Punch · Oscillate · Spring | Float | — | Full | uGUI | Spreads a text's characters. |
| `TMP/Count To` | Tween | Float | — | Full | uGUI | Counts a number in a text from one value to another. |
| `TMP/Dilate` | Tween · Flash · Oscillate · Spring | Float | — | Full | uGUI | Thickens a text's face. |
| `TMP/Font Size` | Tween · Punch · Spring | Float | — | Full | uGUI | Changes a text's size. |
| `TMP/Line Spacing` | Tween · Punch · Oscillate · Spring | Float | — | Full | uGUI | Spreads a text's lines. |
| `TMP/Outline Color` | Tween · Flash · Oscillate · Spring | Color | — | Full | uGUI | Tints a text's outline. |
| `TMP/Outline Width` | Tween · Flash · Oscillate · Spring | Float | — | Full | uGUI | Widens a text's outline. |
| `TMP/Paragraph Spacing` | Tween · Punch · Oscillate · Spring | Float | — | Full | uGUI | Spreads a text's paragraphs. |
| `TMP/Reveal` | Tween | Float | — | Full | uGUI | Reveals a text by characters, words or lines. |
| `TMP/Set Text` | Set | Bool | — | Full | uGUI | Sets a text's string. |
| `TMP/Softness` | Tween · Flash · Oscillate · Spring | Float | — | Full | uGUI | Softens a text's edges. |
| `TMP/Underlay Color` | Tween · Flash · Oscillate · Spring | Color | — | Full | uGUI | Tints a text's underlay. |
| `TMP/Underlay Offset` | Tween · Flash · Oscillate · Spring | Vector2 | — | Full | uGUI | Moves a text's underlay. |
| `TMP/Word Spacing` | Tween · Punch · Oscillate · Spring | Float | — | Full | uGUI | Spreads a text's words. |
<!-- /gen:reference:TMP -->

**Notes**

- The material feedbacks — outline, underlay, dilate, softness — write a copy of the font material for each text.
  An outline needs the font shader's keyword on the Mobile shaders, an underlay on every shader — FB-P07.
- With Auto Size on, `TMP/Font Size` does nothing — FB-P06.
- `TMP/Count To` formats with `{0:…}` and only `0 # , .`; its numbers are doubles, clamped at ±7.9e27 and shown exact to
  15 significant digits. It allocates nothing in a player.
- `UI/Texture Offset` and `UI/Texture Scale` refuse a TMP text; `UI/Material` on one is taken but never drawn.
- **Stagger** spreads `TMP/Character Alpha`, `TMP/Character Offset` and `TMP/Character Scale` over the characters. Under
  a Tween it is a share of the item, and every character ends inside it; under Oscillate it is turns of the wave — an
  item shorter than one turn spreads over the item instead; Wiggle ignores it.
- A Character Tween's end stays through the text's rebuilds. Character offsets of several items and players on one text
  add up; of two Tweens on one channel, the latest wins. A change in the character count moves the shares, and the cost
  grows with characters × effects.
- After the text changes, `TMP/Reveal` is one frame behind; of two Reveals on one text, the later write wins.
- Without TextMesh Pro's Essential Resources in the project, `TMP/Count To` and the three Character feedbacks are
  refused when the player prepares.

## Time

<!-- gen:reference:Time -->
| Feedback | Motions | Value | Category | Preview | Needs | Description |
|---|---|---|---|---|---|---|
| `Time/Hit Stop` | — | — | Time | Display only | — | Freezes time for a moment. |
| `Time/Time Scale` | Set · Tween · Flash | Float | Time | Display only | — | Changes the game's time scale; a Flash goes and comes back, a Tween or Set stays. |
<!-- /gen:reference:Time -->

**Notes**

- Pause or slow your game with [`FeedbackTimeScale.BaseScale`](ScriptingAPI.md#feedbacktimescale); a value your game
  writes to `Time.timeScale` while a time feedback runs becomes the new base, and releasing a pause there lets a running
  slow motion go on.
- While the game already runs slower than its **Minimum Time Scale**, a hit stop makes none and its item keeps its
  place — with the defaults, that is a second hit stop in one frame. A hit stop lasts in real time — the player's
  **Speed** divides it, and **Speed** 0 makes none (a warning in development builds). After the item before it, a hit
  stop starts a frame late at its own length, and time comes back after the next item starts.
- Pausing the player during a hit stop does not end it; `Skip()` withdraws it; a backward play passes without one.
- Of several Time Scale feedbacks, the later wins. The Time category's scale weighs a Flash of `Time/Time Scale`, not a
  Tween.

## Transform

<!-- gen:reference:Transform -->
| Feedback | Motions | Value | Category | Preview | Needs | Description |
|---|---|---|---|---|---|---|
| `Transform/Look At` | Set · Tween | Float | Motion | Full | — | Turns the target to look at another object. |
| `Transform/Match Transform` | Tween | Float | Motion | Full | — | Moves the target onto another object. |
| `Transform/Position` | Tween · Punch · Shake · Curve · Oscillate · Wiggle · Spring | Vector3 | Motion | Full | — | Moves the target. |
| `Transform/Rotate Around` | Tween · Oscillate | Float | Motion | Full | — | Turns the target around a pivot. |
| `Transform/Rotation` | Tween · Punch · Shake · Curve · Oscillate · Wiggle · Spring | Vector3 | Motion | Full | — | Turns the target. |
| `Transform/Scale` | Tween · Punch · Shake · Curve · Oscillate · Wiggle · Spring | Vector3 | Motion | Full | — | Scales the target. |
| `Transform/Set Parent` | Set | Bool | Motion | Full | — | Puts the target under another parent. |
| `Transform/Squash & Stretch` | Tween · Punch · Curve · Spring | Float | Motion | Full | — | Squashes and stretches the target, keeping its volume. |
<!-- /gen:reference:Transform -->

**Notes**

- Position, rotation and scale offsets and overrides are written after `LateUpdate`: your `FixedUpdate`, `Update` and
  `LateUpdate` read the transform without them. A Persist value (a Tween, a Spring Move To) is written in the Update
  stage, before `LateUpdate`. FB-G09 reports an item that moves a built-in constraint's source (Animation Rigging is
  not checked).
- Euler To, From and an absolute Spring MoveTo turn from the start's nearest equivalent, within ±180° on each axis; From
  To, By and a relative Spring do not. For several turns, use By.
- An authored x rotation outside ±90° can make an Euler path detour — FB-D06; **Shortest** keeps the start on the
  authored path (Tween only).
- `Transform/Rotate Around` orbits the pivot as it is each frame; a lasting one starts at 0° from the play's start
  place.
- `Transform/Set Parent`: Revert puts the target back at its start place, Restore under its first parent, at its first
  index and transform. A parent in another scene moves the target into that scene; Restore brings it back.
- FB-A03 checks the anchors set in the inspector; one that `SetAnchor` points into another scene at run time is not
  checked.

## UI

<!-- gen:reference:UI -->
| Feedback | Motions | Value | Category | Preview | Needs | Description |
|---|---|---|---|---|---|---|
| `UI/Alpha` | Tween · Punch · Flash · Blink · Spring | Float | — | Full | uGUI | Fades a UI graphic. |
| `UI/Canvas Group Alpha` | Tween · Flash · Blink | Float | — | Full | uGUI | Fades a canvas group. |
| `UI/Canvas Group Blocks Raycasts` | Set | Bool | — | Full | uGUI | Turns a canvas group's raycast blocking on and off. |
| `UI/Canvas Group Interactable` | Set | Bool | — | Full | uGUI | Turns a canvas group's interactable on and off. |
| `UI/Color` | Tween · Punch · Flash · Gradient · Blink · Spring | Color | — | Full | uGUI | Tints a UI graphic. |
| `UI/Cross Fade` | Tween | Color | — | Full | uGUI | Cross-fades a UI graphic's color. |
| `UI/Font Size` | Tween · Punch | Float | — | Full | uGUI | Changes a UI text's size. |
| `UI/Image Fill` | Tween · Punch · Spring | Float | — | Full | uGUI | Fills a UI image. |
| `UI/Image Sprite` | Set | Bool | — | Full | uGUI | Swaps a UI image's sprite. |
| `UI/Image Sprite Sheet` | Tween | Float | — | Full | uGUI | Plays frames on a UI image. |
| `UI/Interactable` | Set | Bool | — | Full | uGUI | Turns a UI control's interactable on and off. |
| `UI/Material` | Set | Bool | — | Full | uGUI | Swaps a UI graphic's material. |
| `UI/Raycast Target` | Set | Bool | — | Full | uGUI | Turns a UI graphic's raycast target on and off. |
| `UI/Text` | Set | Bool | — | Full | uGUI | Sets a UI text's string. |
| `UI/Texture Offset` | Tween | Vector2 | — | Full | uGUI | Scrolls a UI graphic's main texture (not TMP text). |
| `UI/Texture Scale` | Tween | Vector2 | — | Full | uGUI | Scales a UI graphic's main texture (not TMP text). |
<!-- /gen:reference:UI -->

**Notes**

- `UI/Cross Fade` writes the CanvasRenderer color — the one a Selectable's color tint writes, so on a tinted graphic
  they overwrite each other (FB-P01).
- `UI/Texture Offset` and `UI/Texture Scale` write a copy of the graphic's material; the default material is left alone,
  and a material swap on the same graphic replaces the copy (FB-P03).
- A layout that drives a RectTransform writes over size and position effects on it — FB-G08; for size, pivot and
  anchors, the axes it does not drive come back at the end.
- FB-P02 reports `UI/Font Size` on a dynamic font.
- To turn a UI component on and off, use `GameObject/Component Enabled`.

## Video

<!-- gen:reference:Video -->
| Feedback | Motions | Value | Category | Preview | Needs | Description |
|---|---|---|---|---|---|---|
| `Video/Player Control` | Set | Bool | — | None | Video module | Plays, pauses or seeks a Video Player. |
<!-- /gen:reference:Video -->

**Notes**

- `Video/Player Control` plays in Play mode only. A Stop with Revert undoes its Play and Pause, and `Restore()` puts the
  first state back; a Seek is not undone.

## Validation rules

The validator checks players and presets as you edit, and the project when a check runs from the Tools menu. In a
player's or a preset's inspector, an item's header shows the highest severity of its results — hover for the messages,
click for them and their fix buttons — and the bottom bar sums them up.
`Tools > xpTURN > Feedback > Validate Open Scenes`, `Tools > xpTURN > Feedback > Validate Prefabs` and
`Tools > xpTURN > Feedback > Validate Presets` check every player in the open scenes, the project's prefabs or its
presets, and list the results in `Window > xpTURN > Feedback Validator`. A fix is one Undo step, except one that writes
a file or reloads a scene, which says so on its button, and clearing a scene object's missing entries.

Each rule has one severity — **Error**, **Warning** or **Info** — and its message says what the finding means. A rule's
id — `FB-`, a letter and two digits — is never reused: a removed rule leaves its number empty. The table lists the rules
by letter; the editor's message says what each one saw.

<!-- gen:rules:all -->
| Rules | Severity | Checks | Description |
|---|---|---|---|
| `FB-A01` – `FB-A10` | Error 3 · Warning 7 | objects · the project | The target: one that does not resolve, an asset, another scene, anchors, a preset's direct reference and a transform item's second target. |
| `FB-B01` – `FB-B09` | Error 2 · Warning 6 · Info 1 | objects | The list: missing item types, a prefab instance's or variant's overrides, a missing optional package, a deleted linked preset and a custom feedback whose assembly builds do not have. |
| `FB-C01` – `FB-C07` | Warning 5 · Info 2 | objects | The timeline: compile issues, a backward play over a list that cannot play backward, endless repeats, clocks, and values outside their contract. |
| `FB-D01` – `FB-D08` | Warning 4 · Info 4 | objects · the project | Settings and playback: the first-frame write, play on enable, a spring under Restart or Ignore, motion parameters out of range, Euler rotations and the project's settings assets. |
| `FB-E01` – `FB-E10` | Warning 7 · Info 3 | objects | Toggles, blinks and the Motion Driver: a toggle or Component Enabled that turns off its own player, an Animator under a blinking object that restarts from its default state, blinks over the flash limit. |
| `FB-F01` – `FB-F15` (no F10) | Warning 9 · Info 5 | objects | Materials, sprites, lines and trails: the renderer, its materials and the shader properties an item writes. |
| `FB-G01` – `FB-G14` | Error 2 · Warning 3 · Info 9 | objects | The written transform: a Rigidbody or colliders on it, constraint sources and anchored UI. |
| `FB-H01` – `FB-H06` | Warning 1 · Info 5 | objects | Light and camera set-ups that hide an effect, including a Cinemachine-driven lens and Light 2D. |
| `FB-I01` – `FB-I07` | Warning 3 · Info 4 | objects | Render globals: fog, ambient light, the skybox and shader globals. |
| `FB-J01` – `FB-J07` | Warning 3 · Info 4 | objects | Spawns, despawns and layers, with pools. |
| `FB-K01` – `FB-K06` | Warning 4 · Info 2 | objects | Particle items. |
| `FB-L01` – `FB-L08` | Warning 5 · Info 3 | objects | Animator items. |
| `FB-M01` – `FB-M05` | Warning 4 · Info 1 | objects | Physics items: forces, velocities and explosions. |
| `FB-N01` – `FB-N05` | Warning 3 · Info 2 | objects | Time scale items, custom targets and your own feedbacks. |
| `FB-O01` – `FB-O08` | Error 2 · Warning 4 · Info 2 | objects | Flow items. |
| `FB-P01` – `FB-P13` | Warning 8 · Info 5 | objects | UI: Graphic and Canvas items, TextMesh Pro, floating text, the Progress Bar and the Feedback UI Trigger. |
| `FB-Q01` – `FB-Q04` | Warning 3 · Info 1 | objects | Post-processing volumes. |
| `FB-R01` – `FB-R04` | Warning 1 · Info 3 | objects | Cinemachine set-ups that hide an effect. |
| `FB-S01` | Warning 1 | objects | A rumble item while the project uses only the old Input Manager. |
<!-- /gen:rules:all -->

**Notes**

- FB-E03 reports a Blink of an object whose active state an animation drives — the animation wins each frame. It reads
  the clips of the Animator's controller, an override controller's too, and of a legacy Animation; Timeline and
  Playables graphs are outside it.
