# Unity Timeline Cutscene

## Try

Watch the six-second cutscene loop, then pause it and drag the slider: the camera shake and the hero's spring follow the timeline, the flash holds. In the Editor, select Cutscene and open Window > Sequencing > Timeline to scrub it in Edit mode (the burst plays in Play mode only).

## See

A Feedback track for each player on one timeline (the Unity Timeline package). Driven clips — the camera shake and the hero's looping spring — take the timeline's time: a scrub, a pause and the director's speed show the same values, and the clip's Seed gives the same shake each time. A Triggered clip — the screen flash — starts with its clip and runs on its player's clock. A Signal at 4.5 s calls the burst's PlayFromEvent through a Signal Receiver, in Play mode only: the Timeline window's preview sends no signal while scrubbing, and the receiver's call is Runtime Only. Each clip ends with Revert. The HUD's intensity does not reach the clips (a clip's Intensity is its own) and its time scale slows the director with the game. Before leaves out the camera and the motion, and starts the cutscene over (a play keeps the categories it began with); the flash is out of every category.

## Controls

Space · 1 · Gamepad South — play from the start · 2 · Gamepad West — pause · resume · Z · X · D-pad — scrub · Mouse — buttons · slider

## HUD

Tab · R3 — feedback on/off (Before/After)
[ ] · LB RB — intensity
, . · LT RT — time scale
Esc · Start · Select — back to the hub
H · L3 — fold the HUD

## Requires

Timeline 1.8 or later
