# UI Trigger

## Try

Hover, press and click the menu, flip the toggle, drag the slider, hold and release the button — then lower the time scale and change your mind halfway.

## See

No script plays anything here: each control's Feedback UI Trigger rows and its own UnityEvent call the players. Pointer Exit and Deselect turn the running hover back where it is (Play Backward); the toggle calls PlayFromEvent(bool) and the slider PlayFromEvent(float), its value the intensity. Hold and Release plays its press half and waits for a signal: Pointer Up sends it (after 3 s the wait ends by itself), Pointer Exit stops with Revert, and Submit runs two rows — Play, then Signal — for one whole press. The locked button turns Require Interactable off to answer a click. Before turns the categories off; the color touches are put in UI, while the selection frame and the toggle's knob are in no category — they show the state.

## Controls

Mouse · Arrows · WASD · D-pad · Left stick — pick · Click · Enter · Gamepad South — press · Gamepad East — cancel

## HUD

Tab · R3 — feedback on/off (Before/After)
[ ] · LB RB — intensity
, . · LT RT — time scale
Esc · Start · Select — back to the hub
H · L3 — fold the HUD
