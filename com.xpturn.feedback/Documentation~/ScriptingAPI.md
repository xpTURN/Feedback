# Scripting API

The types a script calls: playing a player and waiting for its end, sending to channels, the global switches, motions
and springs outside a player, and the interfaces and bases your own code can implement or derive from. Most sections
start with the type's members — their descriptions are the package's own documentation comments, the ones your IDE
shows — then say when to use the type, with notes on its edges; most groups end with an example.

Every type is in the `xpTURN.Feedback` namespace, and four rules hold for all of them:

- **Main thread.** Call the API from Unity's main thread. The Editor and development builds check some calls with an
  assertion that logs and does not throw — not every call. A `CancellationToken` you pass in may be canceled from any
  thread.
- **Enum values are added at the end.** A later version adds a value to a public enum such as `PlaybackEnd` only after
  its last value, so saved numbers keep their meaning; give a `switch` over one a `default` branch.
- **Quit handlers must not throw.** An `Application.quitting` handler that throws keeps the handlers after it from
  running, and the package ends its plays in one of them; the package's own handlers do not throw.
- **The samples' namespaces are not API.** Types under `xpTURN.Feedback.Samples` support the samples and may change in
  any version.

## Playing

A `FeedbackPlayer` plays its list of feedback items. Each `Play` starts one play and returns the `PlaybackHandle` that
names it; `PlayArgs` carry what changes from play to play — strength, place, random seed, direction and a listener. A
play ends once, for one `PlaybackEnd` reason, which an `IPlaybackListener` and an awaited `FeedbackPlayer.PlayAsync`
report; the player's `Completed` and `Stopped` events tell the two most common ends.

### FeedbackPlayer

<!-- gen:api:xpTURN.Feedback.FeedbackPlayer -->
Plays a list of feedback items: play, stop, pause, seek and wait for the end of each play.

| Member | Description |
|---|---|
| `event Action<FeedbackPlayer, PlaybackHandle> Completed` | Raised when a play reaches its end; the handle tells which play. |
| `event Action<FeedbackPlayer, PlaybackHandle> Looped` | Raised when a Loop block (Loop Start to Loop End) starts its next pass; the handle tells which play. |
| `event Action<FeedbackPlayer> Paused` | Raised when the player pauses its plays. |
| `event Action<FeedbackPlayer, PlaybackHandle> Played` | Raised when a play starts; the handle tells which play. |
| `event Action<FeedbackPlayer> Resumed` | Raised when the player resumes its paused plays. |
| `event Action<FeedbackPlayer, PlaybackHandle> Stopped` | Raised when a play stops before its end; the handle tells which play. |
| `bool IsPlaying { get; }` | True while any play runs, paused plays included. |
| `float TotalDuration { get; }` | The list's nominal length — the duration multiplier and start delay in, no random part, infinity for an endless list; it prepares the player and is 0 when it cannot. |
| `float GetDuration(PlaybackHandle handle)` | That play's own length, with its random parts and without its speed; 0 when the handle is not this player's live play. |
| `void Pause()` | Pauses the plays that run now; paused plays still count as playing, so the Policy refuses or stops a later Play as it would a running one. |
| `PlaybackHandle Play()` | Plays the list with the default arguments; returns the play's handle, or default when the play is refused. |
| `PlaybackHandle Play(in PlayArgs args)` | Plays the list with `args`; returns the play's handle, or default when the play is refused (a condition, the policy, an argument, not prepared) or faults at its start. |
| `PlaybackHandle Play(in PlayArgs args, CancellationToken token)` | Plays the list with `args` and stops the play (Hold) when `token` is canceled; an already canceled token refuses the play without a warning. |
| `Awaitable<PlaybackEnd> PlayAsync()` | Plays the list and returns an Awaitable that ends with the play's end reason; a refused play ends at once with `PlaybackEnd.Refused`. |
| `Awaitable<PlaybackEnd> PlayAsync(CancellationToken token)` | Plays the list and returns an Awaitable that ends with the play's end reason; canceling `token` stops the play. |
| `Awaitable<PlaybackEnd> PlayAsync(in PlayArgs args, CancellationToken token = default)` | Plays the list with `args` and returns an Awaitable that ends with the play's end reason; canceling `token` stops the play. |
| `void PlayFromEvent()` | Plays the list with the default arguments, as Play does, and returns nothing — the call a UnityEvent, a UI Button or a Timeline Signal Receiver can pick; listen to Played and the other events for the play. |
| `void PlayFromEvent(bool forward)` | Plays the list forward when `forward` is true and backward when it is false, and returns nothing — a Toggle's value picked in a UnityEvent; a play running the other way turns around where it is instead, the same play going on. |
| `void PlayFromEvent(float intensity)` | Plays the list with `intensity` as its intensity, as Play does with `PlayArgs.WithIntensity`, and returns nothing — a Slider's or a signal's value picked in a UnityEvent; a negative or NaN intensity plays nothing. |
| `bool Rebuild()` | Rebuilds the player from its list and settings as they are now and stops the plays that run (Hold); false when it cannot be prepared, or when called inside its own Play, where nothing stops. |
| `void Restore()` | Stops the plays that run now (Hold) and puts each value target back to its value before the first play since the player first prepared or last restored — a Rebuild keeps it, and with Apply Initial State it is the value before the first frame; it does not prepare. |
| `void Resume()` | Resumes the paused plays. |
| `bool Seek(float time)` | Moves the plays that run now, paused ones too, to `time` on the nominal axis, running the instants crossed forward once and taking back those crossed backward; false when no play moved. |
| `void SetAnchor(string name, Object target)` | Points the anchor `name` at `target`, adding it when there is none; distance gates see it at the next Play, item targets after Rebuild. |
| `bool SetListener(PlaybackHandle handle, IPlaybackListener listener)` | Sets the listener that hears this live play's end, exactly once; false when the handle is not this player's live play or the play has a listener already — a play that ends inside Play is heard only through PlayArgs.WithListener. |
| `void Signal()` | Ends the signal waits of the plays that run now — at the next tick's start, or at once inside an event; warns once when no wait ended. |
| `bool Skip()` | Ends the plays that run now: a seekable play runs its remaining events and completes, another stops with Complete; false when no play ended. |
| `void Stop(StopMode mode = StopMode.Hold)` | Stops the plays that run now with `mode`; a play a Stopped handler starts keeps running. |
| `bool Stop(PlaybackHandle handle, StopMode mode = StopMode.Hold)` | Stops one play with `mode`; false when the handle is not this player's live play. Stop(default) is the other overload and stops every play. |
<!-- /gen:api:xpTURN.Feedback.FeedbackPlayer -->

Add it with `Component > xpTURN > Feedback > Feedback Player`, build its list in the inspector and call `Play` from a
script, or `PlayFromEvent` from a UnityEvent — a UI Button, a Timeline Signal Receiver — or send a channel command. Each
call starts its own play on the player's clock (**Time Mode**). **Policy** decides what a new play does to the plays
that run: **Ignore** refuses it, **Restart** stops them first (Hold), and **Overlap** runs them side by side, ending the
oldest when **Max Overlap** plays run. See [Playback](Concepts.md#playback).

A UnityEvent can pick `Pause`, `Resume`, `Restore` and `Signal` too; `Signal` ends the waits of `Flow/Wait For Signal`
items, so a Timeline Signal takes a list past its wait on the timeline's beat. A UnityEvent's call is **Runtime Only**
by default and plays in Play Mode only; set to **Editor And Runtime**, it also reaches the player in edit mode, where a
player with no preview warns once and plays nothing.

Two more versions take the event's value. `PlayFromEvent(float)` plays with it as the intensity — a Slider's value —
through the player's **Remap Intensity**; a negative or NaN value plays nothing. `PlayFromEvent(bool)` plays forward on
true and backward on false — a Toggle's value; while a play runs the other way, it turns that play around where it is —
the same play goes on, with no new `Played`, and **Policy**, **Cooldown** and **Chance** do not apply. A Slider calls on
each change, so **Cooldown** spaces the plays. uGUI's pointer, selection and submit events have no UnityEvent of their own: a
[`FeedbackUITrigger`](#feedbackuitrigger) plays from them.

<!-- gen:snippet:api-playing -->
```csharp
public class Chest : MonoBehaviour
{
    [SerializeField] FeedbackPlayer open;
    PlaybackHandle opening;

    void OnEnable() => open.Completed += OnOpened;
    void OnDisable() => open.Completed -= OnOpened;

    public void Open(float strength)
        => opening = open.Play(PlayArgs.Default.WithIntensity(strength).WithPosition(transform.position));

    public void Interrupt() => open.Stop(opening, StopMode.Revert);

    void OnOpened(FeedbackPlayer player, PlaybackHandle handle)
    {
        if (handle == opening)
            Debug.Log("The chest is open.");
    }
}
```
<!-- /gen:snippet:api-playing -->

`PlayAsync` plays the list and returns an `Awaitable<PlaybackEnd>`: `await` it in an `async` method, and the code after
it runs once the play has ended, with the reason. Pass a token that ends with the caller, such as its
`destroyCancellationToken`, and the play stops with the caller — the `await` then gives `PlaybackEnd.Stopped`.

<!-- gen:snippet:api-await -->
```csharp
public class LevelExit : MonoBehaviour
{
    [SerializeField] FeedbackPlayer outro;

    public async Awaitable LeaveAsync()
    {
        PlaybackEnd end = await outro.PlayAsync(destroyCancellationToken);
        if (end == PlaybackEnd.Completed)
            Debug.Log("Load the next level.");
    }
}
```
<!-- /gen:snippet:api-await -->

**Notes**

- **Initialization** sets when the player prepares its list: **Awake**, **Start** (the default) or **Lazy**. A `Play` or
  `TotalDuration` before then prepares it, and so does `Rebuild()` — call it on a loading screen to keep a **Lazy**
  player's first preparation out of play.
- A player on an object that has never been active — a pooled object created inactive, or a prefab asset — is not
  prepared: `Play` returns `default` with one warning. The player prepares by its **Initialization** the first time its
  object becomes active.
- After it prepares, the player plays what it read then. The list, an item's timing, targets, motion values, enabled
  toggles and category override, **Max Overlap** and a switch to or from **Overlap**, the anchors' item targets,
  **Apply Initial State** and the linked **Preset** take effect at `Rebuild()`, which stops the plays that run (Hold).
  The next `Play` takes the other player settings (the **Policy** among them) and an item's **Intensity**,
  **Play Direction**, conditions and randomness without it.
- Events follow the play: a list that ends at its start raises `Played` then `Completed` inside `Play`, and a play that
  its own first instant stops raises `Stopped` before `Played`.
- A `Play` called while the same player is inside `Play` — from a `Played` handler, say — is refused with a warning.
  Called from `Completed` or a listener when the play ends in the player's tick, it plays.
- Unsubscribe in your `OnDisable` or `OnDestroy`: a live player keeps calling a destroyed subscriber. An exception in a
  handler is logged and does not reach the player's caller, but while a handler throws, the handlers subscribed after it
  are not called — nor are the listeners after a throwing one in the same UnityEvent of the player (its other
  UnityEvents still run). Catch inside your handler.
- `Stop(default)` calls `Stop(StopMode)` and stops every play. To stop one play, pass its handle;
  `default(PlaybackHandle)` names no play, so that call returns false.
- **Disable Behavior** decides what a disabled or destroyed player does with its plays. **Continue** (the default) lets
  them run on — after a destroy, a play runs to its end detached from the player: it raises no events, `Stop` cannot
  reach it, `IsPlaying` is false, and its listener still hears its end (the notes of [`PlaybackEnd`](#playbackend) list
  the plays that end otherwise). **Stop Hold** and **Stop Revert** stop them inside the disable or `Destroy` call.
  Choose a Stop behavior for objects that go back to a pool.
- Under **Stop Revert**, an item that turned off the player's own object or a parent of it leaves it off — Unity does
  not turn an object on while it is being disabled; a child it turned off comes back on. A play that a `Stopped` handler
  starts during that stop runs to its end while the object is off.
- A play whose item throws at its start returns `default`, yet its listener hears `PlaybackEnd.Faulted`, and the play
  still turns a **Toggle** direction and starts the **Cooldown**.
- `Seek` takes a time on the list's own axis, whatever the direction: in a backward play `Seek(0)` is its end and a time
  past the list's length is its start. `Skip()` ends a play in either direction.
- Called from inside an event, a `Seek` during a seek runs at the next tick, and a `Skip()` of a paused play waits for
  `Resume()`.
- `Restore()` puts the start values back at once; a play that a `Stopped` handler starts during it keeps running and
  writes over them at its next tick.
- With a `CancellationToken`, an already canceled token refuses the play without a warning. Canceling on the main thread
  stops the play inside `Cancel()`; from another thread, at the play's next tick. A detached play still stops with its
  token.
- Under **Continue**, a play given the player's own `destroyCancellationToken` ends `PlaybackEnd.Stopped` at the end of
  the frame the object is destroyed in, before `OnDestroy`; under a Stop behavior the `Destroy` call stops it first.
- A token canceled inside the `Stopped` handler of a play the **Policy** stops refuses the new play.
- The player keeps a token's registration — and with it the token's source — after the play ends, until its place is
  needed for a new token while all places are taken, or the player rebuilds or is destroyed.
- Await a `PlayAsync` once. Its end is a value, never an exception: a refused play ends at once with
  `PlaybackEnd.Refused` without calling the listener in its `PlayArgs`; otherwise that listener runs first, then the
  awaiting code.
- The awaiting code runs where the play ends — wherever its listener would run (see
  [`IPlaybackListener`](#iplaybacklistener)), or at the `await` itself when the play ended inside `PlayAsync`. If that
  is inside the same player's `Play`, a `Play` it calls there is refused.
- Awaiting a play of a destroyed **Continue** player gives `PlaybackEnd.Completed` when the detached play runs to its
  end.
- Outside Play mode and outside the inspector's preview, `Play` and `PlayAsync` are refused.
- A `PlayAsync` whose result is not awaited still allocates on each call; call `Play` when you do not wait.

### PlayArgs

<!-- gen:api:xpTURN.Feedback.PlayArgs -->
What one Play carries — intensity, seed, position, direction and listener; default is a play of intensity 1 with the player's own settings.

| Member | Description |
|---|---|
| `static PlayArgs Default { get; }` | The default play: intensity 1, no seed, no position, the player's direction, no listener. |
| `PlayerDirection Direction { get; }` | The play's direction, when `PlayArgs.HasDirection` is true; Toggle reads and turns the player's own toggle. |
| `bool HasDirection { get; }` | True when a direction is set; without one the player's own direction applies. |
| `bool HasPosition { get; }` | True when a position is set. |
| `bool HasSeed { get; }` | True when a seed is set. |
| `float Intensity { get; }` | The play's intensity; 1 unless set. |
| `IPlaybackListener Listener { get; }` | The listener that hears the play's end — hooked before the play starts, so a play that ends inside Play is heard too; null for none. |
| `Vector3 Position { get; }` | The play's position, when `PlayArgs.HasPosition` is true. |
| `uint Seed { get; }` | The seed of the play's random parts, when `PlayArgs.HasSeed` is true. |
| `PlayArgs WithDirection(PlayerDirection direction)` | A copy with the direction set; a value that is not a defined direction makes Play refuse it. |
| `PlayArgs WithIntensity(float intensity)` | A copy with the intensity set. |
| `PlayArgs WithListener(IPlaybackListener listener)` | A copy with the listener set; null for none. |
| `PlayArgs WithPosition(Vector3 position)` | A copy with the position set. |
| `PlayArgs WithSeed(uint seed)` | A copy with the seed set. |
<!-- /gen:api:xpTURN.Feedback.PlayArgs -->

Start from `PlayArgs.Default` and chain the `With…` methods — each returns a changed copy, so one value can be kept and
reused. A seed repeats a play's random parts, a position places its position-based effects and distance gates, and a
direction replaces the player's **Direction** for that play.

### PlaybackHandle

<!-- gen:api:xpTURN.Feedback.PlaybackHandle -->
Names one play; an ended handle never matches a live play, and default — what a refused Play returns — names none.

| Member | Description |
|---|---|
| `bool Equals(PlaybackHandle other)` | True when both name the same play. |
| `bool Equals(object obj)` | True when `obj` is a handle that names the same play. |
| `int GetHashCode()` | A hash of the play's generation. |
| `string ToString()` | PlaybackHandle(index:generation), or PlaybackHandle(none) for default. |
| `static bool operator ==(PlaybackHandle a, PlaybackHandle b)` | True when both name the same play. |
| `static bool operator !=(PlaybackHandle a, PlaybackHandle b)` | True when they name different plays. |
<!-- /gen:api:xpTURN.Feedback.PlaybackHandle -->

Keep the handle of a play you want to reach later: stop it, ask its duration or give it a listener. Handles compare with
`==`; once its play ends, a handle matches no play.

### IPlaybackListener

<!-- gen:api:xpTURN.Feedback.IPlaybackListener -->
Hears a play's end exactly once for each accepted play, after the end's event and on the main thread; set it with PlayArgs.WithListener or FeedbackPlayer.SetListener.

| Member | Description |
|---|---|
| `void OnPlaybackEnded(PlaybackHandle handle, PlaybackEnd end)` | Called once when the play `handle` ends, with the reason `end`. |
<!-- /gen:api:xpTURN.Feedback.IPlaybackListener -->

Implement it on a class that reacts to one play's end without subscribing to the player's events. Give it with
`PlayArgs.WithListener`, which also hears a play that ends inside `Play`, or later with `FeedbackPlayer.SetListener`.

**Notes**

- Each accepted play calls its listener exactly once, after the end's event, on the main thread; a refused play calls
  none.
- It runs where the play ends: in the player's tick (after your scripts' `Update`, before their `LateUpdate`), inside
  `Play`, inside `Stop`, `Skip`, `Seek`, `Restore` or `Rebuild`, inside a main-thread `Cancel()`, inside the `Destroy`
  call or `OnDestroy`, when Play mode ends, and before a recompile reloads the scripts.
- A play that its own first instant stops calls its listener before the `Played` event.
- An exception in a listener is logged; the play still ends.
- `SetListener` returns false for a null listener, a `default` or another player's handle, a play that has ended, a play
  that has a listener already (from `SetListener`, `PlayArgs.WithListener` or `PlayAsync`) and a player that is not
  prepared or is destroyed. A listener once set cannot be removed.
- Implement it on a class: a struct is boxed each time it is passed — an allocation — and the player calls the boxed
  copy.

### FeedbackPlayerUniTaskExtensions

<!-- gen:api:xpTURN.Feedback.FeedbackPlayerUniTaskExtensions -->
Awaits a FeedbackPlayer's play as a UniTask.

| Member | Description |
|---|---|
| `UniTask<PlaybackEnd> PlayAsUniTask(this FeedbackPlayer player, CancellationToken token = default)` | Plays the list and returns a UniTask that ends with the play's end reason; a refused play ends at once with `PlaybackEnd.Refused`. |
| `UniTask<PlaybackEnd> PlayAsUniTask(this FeedbackPlayer player, in PlayArgs args, CancellationToken token = default)` | Plays the list with `args` and returns a UniTask that ends with the play's end reason; a refused play ends at once with `PlaybackEnd.Refused`. |
<!-- /gen:api:xpTURN.Feedback.FeedbackPlayerUniTaskExtensions -->

With UniTask installed (`com.cysharp.unitask` 2.0.0 or later), `PlayAsUniTask` gives the end of a play as a
`UniTask<PlaybackEnd>` — for code that already works in UniTask and for its combinators. It ends as
[`PlayAsync`](#feedbackplayer) does.

<!-- gen:snippet:api-unitask -->
```csharp
public class Fireworks : MonoBehaviour
{
    [SerializeField] FeedbackPlayer left;
    [SerializeField] FeedbackPlayer right;

    public async UniTask CelebrateAsync()
    {
        var (leftEnd, rightEnd) = await UniTask.WhenAll(
            left.PlayAsUniTask(destroyCancellationToken),
            right.PlayAsUniTask(PlayArgs.Default.WithIntensity(0.5f), destroyCancellationToken));
        Debug.Log($"Both ended: {leftEnd}, {rightEnd}");
    }
}
```
<!-- /gen:snippet:api-unitask -->

### StopMode

<!-- gen:api:xpTURN.Feedback.StopMode -->
How a Stop ends the plays it reaches.

| Value | Description |
|---|---|
| `Hold = 0` | Keeps what Persist effects wrote. |
| `Revert = 1` | Puts the play's start values back. |
| `Complete = 2` | Evaluates the end of each item that runs now (not the endless ones or those not started yet), then stops; offsets and overrides go back at once. |
<!-- /gen:api:xpTURN.Feedback.StopMode -->

The mode a `Stop` gives the plays it reaches. A channel's Stop command carries one, and the player's
**Disable Behavior** offers Hold and Revert for a disabled or destroyed player.

### PlaybackEnd

<!-- gen:api:xpTURN.Feedback.PlaybackEnd -->
Why a play ended — what its listener hears, once.

| Value | Description |
|---|---|
| `Completed = 0` | The play reached its end. |
| `Stopped = 1` | The play stopped before its end: a Stop, a canceled token, a Restore, a Rebuild, a later Play the Policy made room for, the player turned off with Disable Behavior Stop Hold or Stop Revert, or a Skip of a play that cannot seek. |
| `Faulted = 2` | An exception in one of the play's items ended it. |
| `Destroyed = 3` | The player was destroyed while the play ran and the play could not run on by itself; with Disable Behavior Continue, a play that has an end finishes instead. |
| `ContextShutdown = 4` | Play mode ended, or a recompile reloaded the scripts, while the play ran. |
| `Refused = 5` | PlayAsync's own: the play was not accepted (a condition, the policy, an argument, not prepared, a closed scheduler, a nested Play, a canceled token); a listener never hears it. |
<!-- /gen:api:xpTURN.Feedback.PlaybackEnd -->

What a listener hears and what `PlayAsync` ends with — a play ends once, with one of these. `Refused` comes only from
`PlayAsync` and `PlayAsUniTask`: a refused `Play` has no play to end.

**Notes**

- `Destroyed` comes when the player's object is destroyed: under **Continue**, for an endless or paused play or one that
  waits on a signal without a timeout or on another player, and at its next tick for a detached play whose targets were
  all destroyed — by a scene unload, say; under **Stop Hold** and **Stop Revert**, for a play started after their stop.
  A finite play under **Continue** runs on detached and ends `Completed`.
- A play that still runs when Play mode ends, a detached one too, ends `ContextShutdown` without a `Stopped` event — in
  the editor, before `OnApplicationQuit` and `Application.quitting` run.

### PlayerDirection

<!-- gen:api:xpTURN.Feedback.PlayerDirection -->
The way a player plays its list.

| Value | Description |
|---|---|
| `Forward = 0` | From the start to the end. |
| `Backward = 1` | From the end to the start. |
| `Toggle = 2` | Turns at each accepted play; the first play is Forward. |
<!-- /gen:api:xpTURN.Feedback.PlayerDirection -->

The player's **Direction**, and one play's with `PlayArgs.WithDirection`. Inside the list, an item's **Play Direction**
— **Follow Player**, **Forward**, **Backward** or **Opposite** — sets how that item plays.

### FeedbackPreset

Create one with `Assets > Create > xpTURN > Feedback Preset`, or from a player's items with **Save Items as Preset...**
in the menu beside the player's **Preset** field; **Apply Items to Preset...** writes a player's items over a preset. A
preset holds an item list only. A player whose **Preset** field links it plays the preset's list and keeps its own
settings; **Unlink and Copy** turns the link into the player's own copy.

Drop a preset on the Hierarchy to add a player to the object under it, or to a new object named after the preset —
linked when the preset inspector's **Link on hierarchy drop (all presets)** is on and one preset lands where there is no
player, a copy otherwise; on an object that has a player, the items are added to the end of its list, and on a linked
player the drop is refused with a warning. In Play mode, an edit to a preset reaches the players that link it as an edit
to their own list does: an item's **Intensity**, **Play Direction**, conditions and randomness at their next `Play`,
curves and gradients at once, and the rest — the list, an item's timing, targets, motion values and enabled toggles — at
their next `Rebuild()`, which the preset inspector's **Rebuild Them** calls for them.

## Broadcasting

Channels reach players a script does not reference. A receiver component listens on a channel — a number or a
`FeedbackChannel` asset — and `FeedbackChannels.Send` sends it a command: play, stop, seek and the rest. Signals are
lighter: a value sent to the `IFeedbackSignalReceiver` objects registered on a channel, by code or by a
`Flow/Signal Broadcast` item.

<!-- gen:snippet:api-broadcasting -->
```csharp
public class Explosion : MonoBehaviour
{
    [SerializeField] FeedbackChannel shakes;

    public void Explode(float power)
        => FeedbackChannels.Send(FeedbackCommand.Play(ChannelRef.Of(shakes), PlayArgs.Default.WithIntensity(power)), transform.position);
}

public class ComboMeter : MonoBehaviour, IFeedbackSignalReceiver
{
    [SerializeField] FeedbackChannel combo;
    float total;

    void OnEnable() => FeedbackSignals.Register(ChannelRef.Of(combo), this);
    void OnDisable() => FeedbackSignals.Unregister(ChannelRef.Of(combo), this);

    public void OnFeedbackSignal(in FeedbackSignal signal) => total += signal.Value;
}
```
<!-- /gen:snippet:api-broadcasting -->

### FeedbackChannels

<!-- gen:api:xpTURN.Feedback.FeedbackChannels -->
Sends commands to the receivers of a channel.

| Member | Description |
|---|---|
| `static int Send(in FeedbackCommand command)` | Sends `command` to its channel's receivers and returns how many took it; outside play nothing is sent (one warning). |
| `static int Send(in FeedbackCommand command, Vector3 position)` | Sends `command` from `position`: a receiver with a range takes it only within its range, and a Play command without a position of its own plays there. |
<!-- /gen:api:xpTURN.Feedback.FeedbackChannels -->

`Send` reaches the receivers enabled on the command's channel now and returns how many took it. With a position, a
receiver with a **Range** takes it only within that distance, and a Play without a position of its own plays there.

### FeedbackCommand

<!-- gen:api:xpTURN.Feedback.FeedbackCommand -->
One call for the players on a channel; default is a Play on channel 0.

| Member | Description |
|---|---|
| `FeedbackCommand(FeedbackCommandKind kind, ChannelRef channel)` | A command of `kind` for `channel`, with the default arguments. |
| `PlayArgs Args { get; }` | The arguments of a Play. |
| `ChannelRef Channel { get; }` | The channel whose receivers take it. |
| `FeedbackCommandKind Kind { get; }` | What the command asks. |
| `StopMode Mode { get; }` | The mode of a Stop. |
| `float Time { get; }` | The time of a Seek. |
| `static FeedbackCommand Play(ChannelRef channel, in PlayArgs args = default)` | A Play command for `channel`. |
| `static FeedbackCommand Seek(ChannelRef channel, float time)` | A Seek command for `channel`. |
| `static FeedbackCommand Stop(ChannelRef channel, StopMode mode = StopMode.Hold)` | A Stop command for `channel`. |
<!-- /gen:api:xpTURN.Feedback.FeedbackCommand -->

Make one with `FeedbackCommand.Play`, `FeedbackCommand.Stop` or `FeedbackCommand.Seek`, or with the constructor for the
other kinds.

### FeedbackCommandKind

<!-- gen:api:xpTURN.Feedback.FeedbackCommandKind -->
What a FeedbackCommand asks the players on its channel to do.

| Value | Description |
|---|---|
| `Play = 0` | Play, with the command's `FeedbackCommand.Args`. |
| `Stop = 1` | Stop, with the command's `FeedbackCommand.Mode`. |
| `Pause = 2` | Pause the plays. |
| `Resume = 3` | Resume the paused plays. |
| `Seek = 4` | Seek to the command's `FeedbackCommand.Time`. |
| `Skip = 5` | Skip the plays to their end. |
| `Restore = 6` | Restore the start values. |
| `Signal = 7` | End the plays' signal waits. |
<!-- /gen:api:xpTURN.Feedback.FeedbackCommandKind -->

Each kind is the player call of the same name; **Signal** ends no wait quietly, without the call's warning.

### ChannelRef

<!-- gen:api:xpTURN.Feedback.ChannelRef -->
Names a channel by a number or by a FeedbackChannel asset.

| Member | Description |
|---|---|
| `static ChannelRef Of(FeedbackChannel channel)` | A channel named by a FeedbackChannel asset. |
| `static ChannelRef Of(int id)` | A channel named by a number. |
<!-- /gen:api:xpTURN.Feedback.ChannelRef -->

A receiver's **Channel** field is one too — a number or an asset. Senders and receivers that name the same number, or
the same asset, share a channel.

### FeedbackChannel

Create one with `Assets > Create > xpTURN > Feedback Channel`. The asset has no fields — it is the channel: receivers,
items and code that name the same asset share it, with no number to agree on. Name it in code with `ChannelRef.Of`.

### FeedbackSignals

<!-- gen:api:xpTURN.Feedback.FeedbackSignals -->
Registers signal receivers on channels and sends signals to them.

| Member | Description |
|---|---|
| `static bool Register(in ChannelRef channel, IFeedbackSignalReceiver receiver)` | Registers `receiver` on `channel` — call it from OnEnable, since each play session and each recompile starts with no receivers; false outside play, for a null receiver or for an asset channel without its asset. |
| `static int Send(in FeedbackSignal signal)` | Sends `signal` to its channel's receivers and returns how many it reached — a receiver that throws or drops it out of range counts; 0 when sends nest too deep. |
| `static void Unregister(in ChannelRef channel, IFeedbackSignalReceiver receiver)` | Takes `receiver` off `channel`. |
<!-- /gen:api:xpTURN.Feedback.FeedbackSignals -->

Register a receiver in its `OnEnable` and unregister it in `OnDisable` — each play session and each recompile starts
with no receivers. `Send` calls the receivers registered on the signal's channel and returns how many it called — a
`FeedbackSignalReceiver` that drops the signal out of its range counts too.

### FeedbackSignal

<!-- gen:api:xpTURN.Feedback.FeedbackSignal -->
What one signal carries: its value, intensity, place and sender.

| Member | Description |
|---|---|
| `FeedbackSignal(in ChannelRef channel, float value, float intensity = 1, bool hasPosition = false, Vector3 position = default, FeedbackPlayer sender = default)` | A signal for `channel`, to send from code. |
| `readonly ChannelRef Channel` | The channel it is sent to. |
| `readonly bool HasPosition` | True when the signal has a place. |
| `readonly float Intensity` | The occurrence's intensity: the play's intensity times the item's random output, or the output alone when the item ignores intensity. |
| `readonly Vector3 Position` | The place, when `FeedbackSignal.HasPosition` is true. |
| `readonly FeedbackPlayer Sender` | The player that sent it; null when code sends it without one. |
| `readonly float Value` | The item's value times the occurrence's intensity (see Intensity). |
<!-- /gen:api:xpTURN.Feedback.FeedbackSignal -->

A `Flow/Signal Broadcast` item sends one with its value times the play's intensity and its player as the sender; code
makes one with the constructor.

### IFeedbackSignalReceiver

<!-- gen:api:xpTURN.Feedback.IFeedbackSignalReceiver -->
Takes the signals sent to a channel it is registered on — a Signal Broadcast item's or FeedbackSignals.Send's.

| Member | Description |
|---|---|
| `void OnFeedbackSignal(in FeedbackSignal signal)` | Called with each signal sent to the channel. |
<!-- /gen:api:xpTURN.Feedback.IFeedbackSignalReceiver -->

Implement it on any class and register it with `FeedbackSignals.Register`. `FeedbackSignalReceiver` is the ready
component that turns signals into a UnityEvent.

### FeedbackRange

<!-- gen:api:xpTURN.Feedback.FeedbackRange -->
The center that distance gates on the global reference measure from.

| Member | Description |
|---|---|
| `static Transform Center { get; set; }` | The transform distance gates on the global reference measure from; null outside play, where a write is ignored with a warning. |
<!-- /gen:api:xpTURN.Feedback.FeedbackRange -->

Set it to what the game is experienced from — the camera, or the character — and players whose distance gate uses the
**Global** reference play only near it. Set it in each play session: outside play it is null.

### FeedbackReceiver

Add it with `Component > xpTURN > Feedback > Feedback Receiver`. It takes the commands sent to its **Channel** (**Int**
with an **Id**, or **Asset**) and gives them to its **Player** — the player on its own object when the field is empty.
With a **Range** above 0 it takes a command sent from a position only within that distance; 0 takes every command. In
Play mode its inspector shows whether it is registered.

### FeedbackSignalReceiver

Add it with `Component > xpTURN > Feedback > Feedback Signal Receiver`. It raises **On Signal (Single)** with the value
of each signal sent to its **Channel** while it is enabled. With a **Range** above 0, a signal with a place reaches it
only within that distance.

## Global

Switches for the whole game: turn categories of effects off or down — an options menu, accessibility — limit flashes,
pause and slow the game through the base time scale, and set the project's start values in the settings asset.

<!-- gen:snippet:api-global -->
```csharp
public class GameOptions : MonoBehaviour
{
    public void SetFlashes(bool on) => FeedbackCategories.SetEnabled(FeedbackCategory.Flash, on);

    public void SetShakeStrength(float strength) => FeedbackCategories.SetScale(FeedbackCategory.Camera, strength);

    public void SetPaused(bool paused) => FeedbackTimeScale.BaseScale = paused ? 0f : 1f;
}
```
<!-- /gen:snippet:api-global -->

### FeedbackCategories

<!-- gen:api:xpTURN.Feedback.FeedbackCategories -->
Turns effect categories on and off, scales them and limits flashes, for the play session.

| Member | Description |
|---|---|
| `static int FlashLimit { get; }` | The most flashes that start in any second; 3 outside play. |
| `static float GetScale(FeedbackCategory category)` | The scale of `category`; 1 outside play. |
| `static bool IsEnabled(FeedbackCategory category)` | True when `category` is on; true outside play. |
| `static void SetEnabled(FeedbackCategory category, bool enabled)` | Turns `category` on or off; while it is off, new plays leave it out, the offsets and overrides of running plays weigh 0 and its rumble stops — Persist writes go on. |
| `static void SetFlashLimit(int perSecond)` | Lets a flash start while fewer than `perSecond` flashes started in the last second, then counts all of its own (a Blink counts each blink); 0 lets none start. |
| `static void SetScale(FeedbackCategory category, float scale)` | Scales the strength of `category`'s offsets and overrides; 1 leaves them as they are. Persist writes (tweens, sets) are not scaled. |
<!-- /gen:api:xpTURN.Feedback.FeedbackCategories -->

The switches last for the play session — apply your saved options when the game starts. A category that is off leaves
its effects out of new plays and weighs the running ones' offsets and overrides 0, a scale multiplies the strength of
offsets and overrides (not Persist writes), and the flash limit counts the flashes of flash-sensitive targets and of
items whose category is Flash, from all players. The settings asset gives their start values. See
[Categories and accessibility](Concepts.md#categories-and-accessibility).

### FeedbackCategory

<!-- gen:api:xpTURN.Feedback.FeedbackCategory -->
The category an effect belongs to — its item's override first, then Flash for a flash, then the feedback's own.

| Value | Description |
|---|---|
| `None = 0` | In no category: never turned off or scaled. |
| `Motion = 1` | Moving, turning and scaling objects. |
| `Camera = 2` | Camera moves, shakes and lens changes. |
| `Flash = 3` | Flashes and blinks of a flash-sensitive target; the flash limit counts them. |
| `PostFX = 4` | Post-processing effects. |
| `Time = 5` | Hit stops and time scale changes. |
| `Particles = 6` | Particle effects; an item's category override puts an effect here. |
| `UI = 7` | UI effects; an item's category override puts an effect here. |
| `Haptics = 8` | Gamepad rumble, and your own haptic feedbacks. |
<!-- /gen:api:xpTURN.Feedback.FeedbackCategory -->

The **Category** column of the [feedback reference](Feedbacks.md) gives each feedback's own category. An effect counts
in its item's category override when one is set, else in `FeedbackCategory.Flash` for a flash or blink of a
flash-sensitive target, else in its feedback's own.

### FeedbackTimeScale

<!-- gen:api:xpTURN.Feedback.FeedbackTimeScale -->
The game's own time scale that hit stops and slow motion work from.

| Member | Description |
|---|---|
| `static float BaseScale { get; set; }` | The game's own time scale (0 to 100) that time effects work from; set it here instead of Time.timeScale to pause or slow the game. |
<!-- /gen:api:xpTURN.Feedback.FeedbackTimeScale -->

Hit stops and slow-motion items scale time from `BaseScale` and put it back when they end. Pause or slow your game by
setting `BaseScale`, not `Time.timeScale`: while a time item runs, the package writes `Time.timeScale` itself. See
[Time](Concepts.md#time).

**Notes**

- `BaseScale` takes 0 to 100; another value, or a write outside Play mode, is ignored with a warning.
- While a time item runs, a value written to `Time.timeScale` that differs from the package's becomes the new base. A
  write of the value it already has goes unnoticed, and the item's end puts the old base back.

### FeedbackSettings

Create it with `Assets > Create > xpTURN > Feedback Settings`: the menu selects the project's
`Resources/FeedbackSettings` asset, or makes `Assets/Resources/FeedbackSettings.asset` when there is none. The package
reads that asset in each play session and never writes it; without one, the defaults below apply.

| Field | Default | What it sets |
|---|---|---|
| **Categories** | every category on, scale 1 | Each category's start state; a category the list leaves out starts on at scale 1. |
| **Flash Limit** | 3 | The flashes that may start in any second; 0 lets none start. |
| **Time Scale Capacity** | 8 | The time requests held at once; when it is full, the oldest is dropped with a warning. |
| **Adjust Fixed Delta Time** | off | Whether time items scale the physics step too. |
| **Shake Receivers Per Play** | 4 | The camera shake receivers one channel shake reaches. |
| **Receiver Contributions** | 8 | The shakes one camera shake receiver keeps room for at once. |
| **Maximum Unscaled Delta** | 0 | The cap on an unscaled frame's time; 0 follows `Time.maximumDeltaTime`. |
| **Overlay Sorting Order** | 32000 | The sorting order of the screen overlay's canvas. |
| **Post FX Layer** | 0 | The layer of the package's post-processing volumes — your cameras' volume masks must take it. |
| **Post FX Priority** | 1000 | The base priority of those volumes. |

**Notes**

- Keep one `Resources/FeedbackSettings`: only one is read, and the validator's FB-D08 reports a second.
- A value outside a field's range is read as the field's default; the inspector cuts most fields to their range as you
  edit.
- **Adjust Fixed Delta Time** scales `Time.fixedDeltaTime` and `Time.maximumDeltaTime` with the time scale while a time
  item runs, overwriting other code's writes to them meanwhile, and puts them back when it ends; at scale 0 it leaves
  both as they are.
- A layer warning about a binding's maximum overlap on a camera shake receiver is fixed by raising
  **Receiver Contributions**, not a player's **Max Overlap**.
- Because of **Maximum Unscaled Delta**, the package reads the settings in each play session even in scenes without
  feedbacks.

### FeedbackPlayerLoop

<!-- gen:api:xpTURN.Feedback.FeedbackPlayerLoop -->
Runs the feedback stages in Unity's PlayerLoop.

| Member | Description |
|---|---|
| `static void Reinstall()` | Puts the feedback stages back at the front of the current PlayerLoop — call it after code that changes the PlayerLoop after the first frame; calling it again is safe. Main thread only. |
<!-- /gen:api:xpTURN.Feedback.FeedbackPlayerLoop -->

The package puts its stages at the front of Unity's PlayerLoop when the game starts. Call `Reinstall` only when your own
code replaces the PlayerLoop after that, or the feedback stages stop running. See
[Under the hood](Concepts.md#under-the-hood).

## Motion outside players

Motion that belongs to no list: a `MotionDriver` component drives one target item on its own — an endless loop, or a
spring that follows a value — and the spring structs move values in your own code.

### MotionDriver

<!-- gen:api:xpTURN.Feedback.MotionDriver -->
Drives one target item without a player: Loop plays its endless motion, Follow springs its value toward a target.

| Member | Description |
|---|---|
| `void SetTarget(Transform target)` | Follow: follows the same property of `target`; null stops following, and the spring keeps moving toward the last value it took. |
| `void SetTarget(Vector4 target)` | Follow: sets the value the spring moves toward, in the motion's space (Euler angles for a rotation); it is kept across enables. While a follow transform is set, the target takes the followed value each frame. |
<!-- /gen:api:xpTURN.Feedback.MotionDriver -->

Add it with `Component > xpTURN > Feedback > Motion Driver`, then choose its **Item** and **Mode**: **Loop** plays the
item's motion endlessly — an idle bob, a pulsing light — and **Follow** springs the item's value toward a target, the
**Follow Target** transform or a value from `MotionDriver.SetTarget`. It runs in Play mode while enabled, with its own
**Time Mode** and **Intensity**.

**Notes**

- It prepares at its first enable in Play mode and keeps its binding while disabled; an inspector edit in Play mode
  starts it again from its beginning.
- In **Follow** the value is the driver's: a script's write is replaced at the next tick. To move the object at once,
  disable the driver, move the object and enable the driver again.
- Of two drivers that follow the same property, the one enabled later wins.
- **Follow** reads only the item's spring frequency and damping, and its clock.
- A value set with `SetTarget(Vector4)` is lost when scripts recompile in Play mode; a transform set with
  `SetTarget(Transform)` is kept — give a driver created at runtime its scene target that way. Only Position, Rotation
  and Scale items follow a transform; others warn once.
- **Loop** counts in its item's category; **Follow** counts in none.

### SpringFloat

<!-- gen:api:xpTURN.Feedback.SpringFloat -->
A spring for game code: Step moves Value toward Target along the same path for any dt.

| Member | Description |
|---|---|
| `SpringFloat(float value, float frequency = 6, float damping = 0.4)` | A spring at rest at `value`; new and default have Frequency and Damping 0, which barely moves. |
| `float Damping` | The damping ratio: below 1 it overshoots; from 1 a spring at rest settles without overshooting, though one already moving toward the target can pass it once. |
| `float Frequency` | The natural frequency in hertz. |
| `float Target` | The value it moves toward. |
| `float Value` | The current value. |
| `float Velocity` | The current velocity. |
| `void Bump(float velocity)` | Adds `velocity` to the velocity — a kick. |
| `bool IsSettled(float threshold)` | True when every component is within `threshold` of the target and slower than it. |
| `void MoveTo(float target)` | Sets the target and keeps the velocity. |
| `void Snap()` | Jumps to the target and stops. |
| `void Step(float dt)` | Advances the spring by `dt` seconds. |
<!-- /gen:api:xpTURN.Feedback.SpringFloat -->

Use it where your code moves a value toward a target and should overshoot and settle — a camera offset, a UI bounce, a
gauge. Call `Step` each frame with the frame's time; the motion is the same at any frame rate.

<!-- gen:snippet:api-spring -->
```csharp
public class Bobber : MonoBehaviour
{
    SpringFloat height = new SpringFloat(0f, frequency: 4f, damping: 0.3f);

    public void Kick(float speed) => height.Bump(speed);

    void Update()
    {
        height.Step(Time.deltaTime);
        transform.localPosition = new Vector3(0f, height.Value, 0f);
    }
}
```
<!-- /gen:snippet:api-spring -->

**Notes**

- The springs are mutable structs, and `Step` on a copy moves only the copy. A spring reached through a `List<T>`
  indexer, a property, a `readonly` field, a `foreach` variable or an `in` parameter never moves, without a warning —
  keep springs in fields or arrays and call them there.

### SpringVector2

<!-- gen:api:xpTURN.Feedback.SpringVector2 -->
A SpringFloat for each component of a Vector2.

| Member | Description |
|---|---|
| `SpringVector2(Vector2 value, float frequency = 6, float damping = 0.4)` | A spring at rest at `value`; new and default have Frequency and Damping 0, which barely moves. |
| `float Damping` | The damping ratio: below 1 it overshoots; from 1 a spring at rest settles without overshooting, though one already moving toward the target can pass it once. |
| `float Frequency` | The natural frequency in hertz. |
| `Vector2 Target` | The value it moves toward. |
| `Vector2 Value` | The current value. |
| `Vector2 Velocity` | The current velocity. |
| `void Bump(Vector2 velocity)` | Adds `velocity` to the velocity — a kick. |
| `bool IsSettled(float threshold)` | True when every component is within `threshold` of the target and slower than it. |
| `void MoveTo(Vector2 target)` | Sets the target and keeps the velocity. |
| `void Snap()` | Jumps to the target and stops. |
| `void Step(float dt)` | Advances the spring by `dt` seconds. |
<!-- /gen:api:xpTURN.Feedback.SpringVector2 -->

Works as [`SpringFloat`](#springfloat) does, on each component; its notes hold.

### SpringVector3

<!-- gen:api:xpTURN.Feedback.SpringVector3 -->
A SpringFloat for each component of a Vector3.

| Member | Description |
|---|---|
| `SpringVector3(Vector3 value, float frequency = 6, float damping = 0.4)` | A spring at rest at `value`; new and default have Frequency and Damping 0, which barely moves. |
| `float Damping` | The damping ratio: below 1 it overshoots; from 1 a spring at rest settles without overshooting, though one already moving toward the target can pass it once. |
| `float Frequency` | The natural frequency in hertz. |
| `Vector3 Target` | The value it moves toward. |
| `Vector3 Value` | The current value. |
| `Vector3 Velocity` | The current velocity. |
| `void Bump(Vector3 velocity)` | Adds `velocity` to the velocity — a kick. |
| `bool IsSettled(float threshold)` | True when every component is within `threshold` of the target and slower than it. |
| `void MoveTo(Vector3 target)` | Sets the target and keeps the velocity. |
| `void Snap()` | Jumps to the target and stops. |
| `void Step(float dt)` | Advances the spring by `dt` seconds. |
<!-- /gen:api:xpTURN.Feedback.SpringVector3 -->

Works as [`SpringFloat`](#springfloat) does, on each component; its notes hold.

### SpringColor

<!-- gen:api:xpTURN.Feedback.SpringColor -->
A SpringFloat for each component of a Color, not clamped — cut what leaves 0 to 1.

| Member | Description |
|---|---|
| `SpringColor(Color value, float frequency = 6, float damping = 0.4)` | A spring at rest at `value`; new and default have Frequency and Damping 0, which barely moves. |
| `float Damping` | The damping ratio: below 1 it overshoots; from 1 a spring at rest settles without overshooting, though one already moving toward the target can pass it once. |
| `float Frequency` | The natural frequency in hertz. |
| `Color Target` | The value it moves toward. |
| `Color Value` | The current value. |
| `Color Velocity` | The current velocity. |
| `void Bump(Color velocity)` | Adds `velocity` to the velocity — a kick. |
| `bool IsSettled(float threshold)` | True when every component is within `threshold` of the target and slower than it. |
| `void MoveTo(Color target)` | Sets the target and keeps the velocity. |
| `void Snap()` | Jumps to the target and stops. |
| `void Step(float dt)` | Advances the spring by `dt` seconds. |
<!-- /gen:api:xpTURN.Feedback.SpringColor -->

Works as [`SpringFloat`](#springfloat) does, on each component; its notes hold.

## UI

A component and an asset for uGUI; they need the uGUI package, and the text parts TextMesh Pro.

### ProgressBar

<!-- gen:api:xpTURN.Feedback.ProgressBar -->
A bar that moves to a value with a delayed decrease bar, a delayed increase bar and a bump; it runs while enabled in play.

| Member | Description |
|---|---|
| `event Action<ProgressBar> Bumped` | Raised when a change bumps the bar. |
| `event Action<ProgressBar> MoveEnded` | Raised when every bar has reached the value and a bump has settled. |
| `event Action<ProgressBar> MoveStarted` | Raised when the bars start moving toward a new value. |
| `float Value { get; }` | The value the bar moves toward, 0 to 1. |
| `void SetValue(float value, bool animate = true)` | Moves the bar to `value`, clamped to 0 to 1, with its delays and bump; without `animate`, or while it does not run, it shows the value at once. |
| `void SetValueImmediate(float value)` | Shows `value`, clamped to 0 to 1, on every bar at once. |
<!-- /gen:api:xpTURN.Feedback.ProgressBar -->

Add it with `Component > xpTURN > Feedback > Progress Bar` and give it a **Foreground** bar — with **Delayed Decrease**
and **Delayed Increase** bars if you want them: on a loss the foreground moves at once and the decrease bar follows
after **Decrease Delay**; on a gain the increase bar shows it at once and the foreground follows after
**Increase Delay**. **Fill** chooses how a bar shows the value — **Local Scale**, **Fill Amount**, **Width**, **Height**
or **Anchor** — and **From** the side it grows from. Call `SetValue` from your game code; **Bump On Increase** and
**Bump On Decrease** scale the bar along the **Bump Scale** curve for **Bump Duration**, and **Text** shows the value
times **Text Multiplier** with **Format**. The bars move in the Update stage.

**Notes**

- With **Fill Amount**, the image's own fill method and origin decide the direction and **From** is ignored; the bar
  needs a Filled image with a sprite.
- With **Local Scale**, **Width** or **Height**, each bar's pivot sits on its start side: 0 for Left and Bottom, 1 for
  Right and Top. The validator's FB-P10 reports a bar set up otherwise.
- It runs in Play mode while enabled, on unscaled time by default (**Unscaled** — capped by the settings'
  **Maximum Unscaled Delta**); with **Unscaled** off it follows `Time.deltaTime`. Outside Play mode `SetValue` shows the
  value at once.
- In a project without TextMesh Pro's Essential Resources, a bar with a **Text** logs a warning in the Editor and
  development builds.

### FeedbackUITrigger

Add it with `Component > xpTURN > Feedback > Feedback UI Trigger` to a UI object — a Button, a Toggle, an image — and
fill its **Entries**: each names an **Event** — **Pointer Enter**, **Pointer Exit**, **Pointer Down**, **Pointer Up**,
**Pointer Click**, **Select**, **Deselect**, **Submit** or **Cancel** — an **Action** — **Play**, **Play Forward**,
**Play Backward**, **Stop** with its **Stop Mode**, or **Signal** — and a **Player**, the Feedback Player on the
trigger's object when it is empty. An event runs its entries in list order. A hover is two entries: **Pointer Enter**
with **Play Forward** and **Pointer Exit** with **Play Backward**. A **Play Forward** or **Play Backward** entry that
comes while the player's latest play runs the other way turns that play around where it is, so the motion retraces
without a jump however quickly the pointer comes and goes.

**Notes**

- Put the trigger on the object that takes the presses — the Button itself. uGUI sends a press and a click to the first
  object up the hierarchy that handles them, so a trigger below a Button takes them: the Button loses its press, its
  selection and its On Click. The validator's FB-P12 reports a trigger placed so. **Pointer Enter** and
  **Pointer Exit** reach every object under the pointer.
- It has no drag or scroll handler: in a ScrollRect, drags and scrolls still reach the ScrollRect.
- **Pointer Down**, **Pointer Up** and **Pointer Click** answer the left button only, as a Button does.
- With **Require Interactable** on and a Selectable on the object that is not interactable — its own setting or a
  Canvas Group above it — the entries of **Pointer Enter**, **Pointer Down**, **Pointer Click**, **Select**, **Submit**
  and **Cancel** do not run; those of **Pointer Exit**, **Pointer Up** and **Deselect** still do, so a hover that began
  ends.
- **Source** filters **Select** and **Deselect**: **Pointer** (a press selected the object), **Navigation** (a gamepad
  or the keys) or **Other** (code, the first selection). A press on a Selectable selects it, so a hover played from
  **Pointer Enter** and **Select** plays again on a click unless the **Select** entry takes **Navigation**.
- A touchscreen has no hover: a touch brings **Pointer Enter** as it begins and **Pointer Exit** after
  **Pointer Click** as it ends.
- A turned play is not a new play: no `Played` or `Stopped` comes, **Policy**, **Cooldown** and **Chance** do not apply,
  and what sits at the list's start does not run again on a re-entry. A sound for every entry goes in an entry of its
  own that plays another player.
- A Selectable's Color Tint transition and a color feedback on the same graphic write over each other (FB-P01): set the
  transition to None when the feedback is the hover.
- The events come in Play mode only, and handling them allocates nothing.

### FloatingTextStyle

Create one with `Assets > Create > xpTURN > Feedback > Floating Text Style` and give it to a `Screen/Floating Text`
item. It sets how the text looks — **Font** and **Font Size**, or a **Prefab** to use instead — and how it moves over
its **Lifetime**, a random range: the **Distance** on each axis that the **Move X** and **Move Y** curves travel,
**Spread** and **Scatter**, the **Scale** and **Alpha** curves from **Scale Start** and **Alpha Start**, and the
**Color** over its life. Sizes and distances are in pixels of a 1920 × 1080 screen.

## Cameras

Camera shakes sent to a channel move the cameras that receive them: a `CameraShakeReceiver` on a camera or any
transform. A camera that a Cinemachine brain drives takes shakes through a `CinemachineShakeExtension` on its
Cinemachine cameras.

### CameraShakeReceiver

Add it with `Component > xpTURN > Feedback > Camera Shake Receiver` to a camera, or to any transform a `Camera/Shake`
sent to a channel should move. It takes the shakes on its **Channel** (**Int** with an **Id**, or **Asset**); with a
**Range** above 0, only shakes sent from within that distance. The settings' **Shake Receivers Per Play** and
**Receiver Contributions** set how many receivers one shake reaches and how many shakes one receiver mixes.

### CinemachineShakeExtension

Add it with `Component > xpTURN > Feedback > Cinemachine Shake Extension` to each Cinemachine camera that can go live. A
`Camera/Shake` on a camera that a Cinemachine brain drives then goes into Cinemachine's own pipeline — after the noise
and the other extensions, and into its blends — in Play mode and in the inspector's preview. Without it, the preview
shows no shake, since the brain writes the camera over it, and Play mode shows the shake on the camera's transform,
outside Cinemachine's blends. It has no fields.

**Notes**

- A `Cinemachine/Impulse` moves only the cameras whose impulse listener takes its channel; the validator's FB-R03
  reports an impulse no listener hears.
- The impulse manager's ignore-time-scale switch stays yours; a preview turns it off for its duration and puts it back.
- `Cinemachine/Impulse Clear` clears every impulse on every channel, others' too.
- The lens and noise values a Cinemachine feedback changes are not kept by Save During Play.

## Unity Timeline

A track and its clips play a `FeedbackPlayer` from a timeline; they need the Timeline package, 1.8 or later.

### FeedbackTrack

<!-- gen:api:xpTURN.Feedback.FeedbackTrack -->
A Timeline track bound to a FeedbackPlayer; each of its clips is one play of that player.

| Member | Description |
|---|---|
| `void GatherProperties(PlayableDirector director, IPropertyCollector driver)` | Timeline calls it when its window's preview starts; the bound player joins that preview. |
<!-- /gen:api:xpTURN.Feedback.FeedbackTrack -->

Add it from the Timeline window's track menu, **xpTURN > Feedback Track**, and bind it to a `FeedbackPlayer`. Each clip
on the track is one play of that player — its list, its settings and its events. A clip made on the track draws its own
**Seed** and takes the player's name.

### FeedbackClip

<!-- gen:api:xpTURN.Feedback.FeedbackClip -->
A clip of a Feedback track: one play of the track's FeedbackPlayer, moved by the timeline's time or started with the clip.

| Member | Description |
|---|---|
| `double duration { get; }` | The length of the track's FeedbackPlayer's list while the Timeline window shows the clip in the Editor's Edit mode — a new clip, Match Content and the loop marks take it; infinite otherwise. |
| `Playable CreatePlayable(PlayableGraph graph, GameObject owner)` | Creates the clip's playable; the timeline calls it when it builds its graph. |
<!-- /gen:api:xpTURN.Feedback.FeedbackClip -->

**Timing** chooses how the clip plays its player:

- **Driven** — the clip's time moves one play. Scrubbing, pausing, the director's speed, the clip's **Clip In** and
  **Speed Multiplier**, playing backward and **Loop** all follow the timeline, and the same time shows the same values:
  the play uses the clip's **Seed**, passes the player's **Chance**, **Cooldown** and distance gate, takes the nominal
  lengths and leaves `Time.timeScale` alone. A `Time/Hit Stop` in it still stops the game — the director with it — for
  its length.
- **Triggered** — the play starts when the clip starts and runs on the player's own clock and rules; it pauses while the
  director pauses, and **Loop** plays the list again until the clip ends.
- **Auto**, the default — Driven when the list can be scrubbed, Triggered when it cannot: an endless list or a
  `Flow/Wait For Signal` without a timeout. Played as Triggered, the player's Chance, Cooldown, distance gate and
  direction apply and its Time Scale items write.

**End** — **Revert**, **Hold** or **Complete** — is how the clip ends its play when the timeline leaves it; stopping
or destroying the director reverts. **Intensity** is the play's intensity.

**Notes**

- In Edit mode the track plays in the Timeline window's preview: Driven clips follow scrubbing, Triggered clips play
  while the Timeline window plays, and turning the preview off puts the scene back. With the window's **Scene Preview**
  off the track leaves the scene as it is.
- The track and a player's Inspector **Preview** do not run together: while the Timeline window previews, the Inspector's
  Preview is refused; while an Inspector preview runs, the track leaves the scene as it is.
- An edit during the preview — of a value the preview changes, or of the bound player — and an Undo keep what they
  change, and the preview is built again from it.
- A play that ends inside its clip, such as a Triggered list shorter than its clip, still takes **Revert** at the clip's
  end: the player goes back as it was before the plays, as `Restore` does (with the other values it wrote since), unless
  it plays something else then. With **Hold** or **Complete** it keeps what it persisted. In the preview, entering that
  clip again starts it from the values before the plays, as each play of a player's Inspector **Preview** does.
- A Driven clip whose list cannot be scrubbed plays as Triggered, with the player's rules, and a warning once in the
  Editor and development builds. In the Timeline window's preview a list with a simulated target plays as Triggered too.
- A play another call ends — `Stop`, `Skip`, or a play of the same player with **Restart** — starts again only when the
  timeline enters the clip again.
- A clip offers extrapolation only with Driven timing.
- In the Timeline window a clip's length is its player's list: a new clip takes it, **Match Content** fits the clip to it
  and a looping clip marks each run of the list. An endless list keeps Timeline's default length. In the Editor a clip
  made from a script takes it too while the Timeline window shows its timeline — set `TimelineClip.duration` to choose
  another.
- The Timeline window marks a clip that does not play as set — no player bound, a Driven list that cannot be scrubbed,
  Time Scale items in a Driven clip, an overlap with another clip of the same player under **Restart** or **Ignore** —
  and the clip's tooltip tells its timing, the list's length and its seed. The track shows its player's validation errors
  and warnings, and a clip's background draws its list's bars.
- While the Timeline window's preview plays a player, the player's Inspector **Preview** says so.
- A clip duplicated or pasted in the Timeline window draws a new **Seed**; set the **Seed** to give two clips the same
  random parts.

## Your own feedbacks

Add what the package does not do — a sound, an FMOD or Wwise event, a phone's vibration — as a feedback of your own.
Derive an item from `CustomActionItem` or `CustomEffectItem`, name it in the Add menu with `CustomFeedbackAttribute`,
and put your code in the `CustomAction` or `CustomEffect` the item makes. The player plays it like its own items —
timing, chance, intensity, direction, Seek, Skip and the stops — and calls your code. The package itself ships no sound,
middleware or phone haptics code.

| You want | Use |
|---|---|
| An act at one moment: a one-shot sound, a haptic pulse | `CustomActionItem` with `Duration` 0 — your action's `Fire` |
| An act with a length: a sound until the end, a vibration | `CustomActionItem` with a `Duration` — `Begin`, `Update`, `End` |
| A value over time: a mixer or FMOD parameter, a Wwise RTPC, a vibration's strength | `CustomEffectItem` — `Apply` takes the motion's value |
| A value of your own component, composed with other feedbacks | `IFeedbackTarget<T>` — [Your own targets](#your-own-targets) |
| A call with no code of its own | `Flow/Unity Event`, `Custom/Float Event` |

**From a class to a played feedback.**

1. Put the class where builds compile it. A script in `Assets` without an assembly definition sees the package as it
   is; an assembly definition that holds it needs a reference to `xpTURN.Feedback`. A feedback compiled only for the
   Editor — in a folder named `Editor` without an assembly definition, or in an assembly definition for the Editor
   platform — has **(not in builds)** after its name in the Add menu, and the validator reports FB-B09.
2. Derive the item and give it a path in the Add menu with [`CustomFeedbackAttribute`](#customfeedbackattribute):
   `Requires` for the target it needs, or `Global` for none.
3. Choose it in a player's [Add menu](GettingStarted.md#the-add-menu) and set its **Target** as for the package's
   items, or drag an object with the `Requires` component onto the list.
4. The inspector's [preview](GettingStarted.md#the-preview-bar) skips your code unless the attribute's `Preview` is
   `Run`; Play mode calls it.
5. When nothing happens, select the play in the [debugger](GettingStarted.md#the-debugger) — see **In the debugger**
   below — and run the [validator](GettingStarted.md#the-validator): a missing type is FB-B01, a declared Flash FB-N05.

### CustomActionItem

<!-- gen:api:xpTURN.Feedback.CustomActionItem -->
The base of a custom action: an instant or a timed act whose code is the CustomAction it makes.

| Member | Description |
|---|---|
| `FeedbackCategory Category { get; }` | The category the action counts in, read once for its type, so return a constant; Flash reads as None. |
| `float Duration { get; }` | The act's length in seconds on the item's clock, 0 for an instant; read before a player prepares, so compute it from serialized fields. |
| `protected CustomAction CreateAction()` | Makes the code object for one preparation; a player calls it each time it prepares the item. |
<!-- /gen:api:xpTURN.Feedback.CustomActionItem -->

Derive a sealed, `[Serializable]` class with a public parameterless constructor (the inspector and the serializer use
it) and keep your settings in its serialized fields. The player reads `Duration` before it prepares, and `Category`
once for the type: return values from serialized fields and a constant. A declared Flash counts as None — the flash
limit cannot count a feedback it does not draw. A feedback without the attribute appears under Project in the Add menu.

### CustomAction

<!-- gen:api:xpTURN.Feedback.CustomAction -->
The code of a custom action; override the calls you need, each does nothing by default.

| Member | Description |
|---|---|
| `protected void Begin(in CustomPlay play)` | A timed act starts, backward too (Reverse); after an exception the slot gets no Update until its next Begin. |
| `protected void End(int slot, CustomEnd end)` | A timed act ends, or an executed instant is reverted; it comes after an exception too. |
| `protected void Fire(in CustomPlay play)` | An instant's forward act; an exception is logged and the play goes on. |
| `protected bool Prepare(in CustomPrepare context)` | Gets what the plays need; false leaves the item out of this player. Release follows even after false or an exception. |
| `protected void Release()` | Gives back what Prepare got: the player is released, rebuilt or destroyed. |
| `protected void Restore()` | The player's Restore, after its plays stopped with Hold. |
| `protected void Undo(in CustomPlay play)` | An instant's backward act, undoing what Fire did; it can come without a Fire (a backward Seek, an item turned by its Play Direction). |
| `protected void Update(in CustomTick tick)` | A timed act's frame, every frame its play runs. |
<!-- /gen:api:xpTURN.Feedback.CustomAction -->

The item makes one action each time a player prepares it, so the action's state is its own: a preset's players share
the item, never an action. Overlapping plays use different slots — size what each play holds by `Plays` in `Prepare`.
Your calls run on the play path: allocate in `Prepare`, not in `Fire` or `Update`. An exception in a call is logged and
the play goes on; a slot whose `Begin` or `Update` threw gets no more `Update` until its next `Begin`, and its `End`
still comes. `Undo` is an instant's backward act — it comes without a `Fire` before it too (a backward Seek, an item
turned by its Play Direction), so undo only what your `Fire` left. A timed act runs backward as well: `Begin` with
`Reverse`, then `T` from 1 to 0.

<!-- gen:snippet:api-custom-action -->
```csharp
[CustomFeedback("Audio/One Shot", Aliases = "sound sfx", Requires = typeof(AudioSource))]
[Serializable]
public sealed class OneShotSound : CustomActionItem
{
    [SerializeField] AudioClip clip;
    [SerializeField] float volume = 1f;

    protected override CustomAction CreateAction() => new Action(this);

    sealed class Action : CustomAction
    {
        readonly OneShotSound item;
        AudioSource source;

        public Action(OneShotSound item) => this.item = item;

        protected override bool Prepare(in CustomPrepare context) => context.TryFindTarget(out source);

        protected override void Fire(in CustomPlay play)
        {
            // a Seek or a Skip that sweeps over the moment does not play it
            if (play.Swept)
                return;
            source.PlayOneShot(item.clip, item.volume * play.Intensity * play.CategoryScale);
        }
    }
}
```
<!-- /gen:snippet:api-custom-action -->

A timed act keeps a slot's state from `Begin` to `End`:

<!-- gen:snippet:api-custom-timed -->
```csharp
[CustomFeedback("Haptics/Phone Vibration", Global = true)]
[Serializable]
public sealed class PhoneVibration : CustomActionItem
{
    [SerializeField] float length = 0.3f;

    public override float Duration => length;
    public override FeedbackCategory Category => FeedbackCategory.Haptics;

    protected override CustomAction CreateAction() => new Action();

    sealed class Action : CustomAction
    {
        bool[] running;

        protected override bool Prepare(in CustomPrepare context)
        {
            running = new bool[context.Plays];
            return true;
        }

        protected override void Begin(in CustomPlay play)
        {
            if (play.Swept || play.Reverse)
                return;
            running[play.Slot] = true;
            MyHaptics.Start(play.Intensity * play.CategoryScale);
        }

        protected override void Update(in CustomTick tick)
        {
            // fades out; 0 at once when the Haptics category is turned off
            if (running[tick.Slot])
                MyHaptics.SetStrength((1f - tick.T) * tick.CategoryScale);
        }

        protected override void End(int slot, CustomEnd end)
        {
            if (!running[slot])
                return;
            running[slot] = false;
            MyHaptics.Stop();
        }
    }
}
```
<!-- /gen:snippet:api-custom-timed -->

`End` tells why the act ended, which differs from why the whole play ended (`PlaybackEnd`): a player turned off with
Stop Hold, a Restore or a release end with Hold; a player destroyed with Continue lets a finite play finish on its own.

### CustomEffectItem

<!-- gen:api:xpTURN.Feedback.CustomEffectItem -->
The base of a custom effect: a motion's value handed to the CustomEffect it makes, every frame the play runs.

| Member | Description |
|---|---|
| `FeedbackCategory Category { get; }` | The category the effect counts in, read once for its type, so return a constant; Flash reads as None. |
| `protected CustomEffect CreateEffect()` | Makes the code object for one preparation; a player calls it each time it prepares the item. |
<!-- /gen:api:xpTURN.Feedback.CustomEffectItem -->

An effect takes the motions a `Custom/Float Event` takes — Tween, Punch, Shake, Curve and Oscillate — and the value is
the same: an offset motion's value sits on the item's **Base** and takes the category's scale; a Tween does not take the
scale. In a Motion Driver, an effect loops with Oscillate or Shake. When the Haptics category is turned off, a Haptics
effect gets its base once and no more calls in that play.

### CustomEffect

<!-- gen:api:xpTURN.Feedback.CustomEffect -->
The code of a custom effect: Apply takes the motion's value every frame its play runs.

| Member | Description |
|---|---|
| `protected void Apply(int slot, float value)` | The value for a slot: an offset motion's value sits on the item's base and takes the category's scale; slot −1 puts the base back for a Revert or a Restore. |
| `protected void Begin(in CustomPlay play)` | An occurrence starts, before its first Apply. |
| `protected void End(int slot, CustomEnd end)` | An occurrence ends; it comes after an exception too. |
| `protected bool Prepare(in CustomPrepare context)` | Gets what the plays need; false leaves the item out of this player. Release follows even after false or an exception. |
| `protected void Release()` | Gives back what Prepare got: the player is released, rebuilt or destroyed. |
<!-- /gen:api:xpTURN.Feedback.CustomEffect -->

<!-- gen:snippet:api-custom-effect -->
```csharp
[CustomFeedback("Audio/Mixer Parameter", Global = true)]
[Serializable]
public sealed class MixerParameter : CustomEffectItem
{
    [SerializeField] AudioMixer mixer;
    [SerializeField] string parameter = "Volume";

    protected override CustomEffect CreateEffect() => new Effect(this);

    sealed class Effect : CustomEffect
    {
        readonly MixerParameter item;

        public Effect(MixerParameter item) => this.item = item;

        // every frame the play runs; slot −1 puts the item's base back after a Revert or a Restore
        protected override void Apply(int slot, float value) => item.mixer.SetFloat(item.parameter, value);
    }
}
```
<!-- /gen:snippet:api-custom-effect -->

Two effects that drive the same value each call their own `Apply` — the package does not add them up. For a value that
composes with other feedbacks, use [`IFeedbackTarget<T>`](#ifeedbacktargett).

### CustomPrepare

<!-- gen:api:xpTURN.Feedback.CustomPrepare -->
What a custom feedback prepares against, once for each player that prepares it.

| Member | Description |
|---|---|
| `bool InPreview { get; }` | True in the inspector's preview. |
| `GameObject Owner { get; }` | The GameObject that holds the player or the motion driver. |
| `FeedbackPlayer Player { get; }` | The player that prepares the item; null in a motion driver. |
| `int Plays { get; }` | How many plays of the item can overlap: the slots run 0 to Plays − 1. |
| `bool TryFindTarget<T>(out T target)` | Finds the item's target as the type T, a component type or an interface; the item ends when the found object is destroyed. |
<!-- /gen:api:xpTURN.Feedback.CustomPrepare -->

`TryFindTarget` finds the item's **Target** the way the package's items find theirs — Self, a child, a parent, an anchor
or a dragged object; a found object that is destroyed ends the item. Set `Requires` on the attribute so the inspector
offers the right objects and a dropped component makes the item.

### CustomPlay

<!-- gen:api:xpTURN.Feedback.CustomPlay -->
One occurrence of a custom feedback as it starts or fires.

| Member | Description |
|---|---|
| `readonly float CategoryScale` | The category's scale, 0 when the category is off; multiply your act's strength by it to follow the accessibility settings. |
| `readonly bool HasPosition` | True when the play has a place: Play's position, or the player's own once the player is gone. |
| `readonly float Intensity` | The play's intensity times the item's random output. |
| `readonly double Length` | The occurrence's real length in seconds, its length divided by the player's speed; infinity when it has no end. |
| `readonly long Occurrence` | The occurrence's number within the play: repeats and loops count up. |
| `readonly Vector3 Position` | The play's place when HasPosition is true. |
| `readonly bool Reverse` | True when the occurrence runs backward: a backward play, a backward Seek, or an item turned by its Play Direction. |
| `readonly uint Seed` | The item's seed for this play, the same for the same play's seed. |
| `readonly int Slot` | The occurrence's slot, 0 to Plays − 1: overlapping plays use different slots. |
| `readonly bool Swept` | True when a Seek or a Skip swept over the occurrence rather than a frame reaching it; a sound or a haptic usually returns. |
<!-- /gen:api:xpTURN.Feedback.CustomPlay -->

### CustomTick

<!-- gen:api:xpTURN.Feedback.CustomTick -->
A timed custom action's frame.

| Member | Description |
|---|---|
| `readonly float CategoryScale` | The category's scale this frame, 0 once the category is turned off. |
| `readonly double Elapsed` | Seconds into the occurrence on the timeline, the player's speed applied; an item turned by its Play Direction counts from its end. |
| `readonly int Slot` | The occurrence's slot, the one its Begin had. |
| `readonly float T` | Where the act is, 0 to 1; a backward play goes from 1 to 0. |
<!-- /gen:api:xpTURN.Feedback.CustomTick -->

A play that turns around where it is — a [`FeedbackUITrigger`](#feedbackuitrigger) hover, `PlayFromEvent(bool)` —
keeps the acts that run: their `CustomPlay.Reverse` stays the one their Begin had, and `T` goes back from where it was.

### CustomEnd

<!-- gen:api:xpTURN.Feedback.CustomEnd -->
Why a custom feedback's act ended; it differs from PlaybackEnd, which tells why a whole play ended.

| Value | Description |
|---|---|
| `Completed = 0` | The act reached its end. |
| `Hold = 1` | The play stopped and kept its values: Stop(Hold), the player turned off with Stop Hold, a later play the policy made room for, a canceled token, a Rebuild, a Restore, the player destroyed or released. |
| `Revert = 2` | The play stopped and put its values back: Stop(Revert) or the player turned off with Stop Revert; an executed instant hears it too, and may hear it again. |
| `Complete = 3` | The play jumped to its end: Stop(Complete), or a Skip of a play that cannot seek. |
| `Aborted = 4` | The act ended without its play: its target was destroyed, an item threw, or play mode ended. |
<!-- /gen:api:xpTURN.Feedback.CustomEnd -->

### CustomFeedbackAttribute

<!-- gen:api:xpTURN.Feedback.CustomFeedbackAttribute -->
Names a custom feedback in the inspector's Add menu and tells the editor about its target and its preview.

| Member | Description |
|---|---|
| `CustomFeedbackAttribute(string path)` | Puts the feedback at `path` in the Add menu, groups separated by '/'. |
| `string Aliases { get; set; }` | Search words for the Add menu, space separated. |
| `bool Global { get; set; }` | True when the feedback has no target: the inspector hides the target field. |
| `string Path { get; }` | The Add menu path; an effect's leaves are this path and each motion it takes. |
| `CustomPreview Preview { get; set; }` | Whether the inspector's preview calls the feedback's code; Skip unless set. |
| `Type Requires { get; set; }` | What a Direct target or a dropped object must be, a component type or an interface; null takes any object. |
<!-- /gen:api:xpTURN.Feedback.CustomFeedbackAttribute -->

### CustomPreview

<!-- gen:api:xpTURN.Feedback.CustomPreview -->
Whether the inspector's preview runs a custom feedback's code.

| Value | Description |
|---|---|
| `Skip = 0` | The preview does not call it; the preview bar counts the plays it skipped. |
| `Run = 1` | The preview calls it with InPreview true; what it changes outside the package is not put back. |
<!-- /gen:api:xpTURN.Feedback.CustomPreview -->

With Skip, the inspector's preview neither makes nor prepares the feedback's code and counts the calls it skipped.
With Run, the preview calls it with `InPreview` true; what your code changes outside the package does not come back
when the preview ends — put it back in `End` and `Release`.

**Filling what the package leaves out.** Which call goes where; the calls are the middleware's or the platform's, so
check them against the version you use.

| Fill in | Base and call | What your code calls |
|---|---|---|
| A Unity sound | An instant action's `Fire` (return when `Swept`) | AudioSource.PlayOneShot, its volume times `Intensity` and `CategoryScale` |
| An FMOD one-shot | An instant action's `Fire` | RuntimeManager.PlayOneShot with an EventReference and the play's position (FMOD for Unity 2.02 to 2.04); it cannot be stopped |
| An FMOD event with a length | A timed action's `Begin`, `End` and `Release` | RuntimeManager.CreateInstance, start, then stop — IMMEDIATE when the end is Revert or Aborted, ALLOWFADEOUT otherwise — and release |
| An FMOD parameter | An effect's `Apply` | EventInstance.setParameterByID with a cached id, or the Studio system's setParameterByName for a global one |
| A Wwise event | An instant action's `Fire`, or a timed one's `Begin` and `End` | AK.Wwise.Event.Post with a GameObject, stopped by the playing id it returns; the static class is AkUnitySoundEngine from Wwise 2024.1, AkSoundEngine before |
| A Wwise RTPC | An effect's `Apply` | AK.Wwise.RTPC.SetValue on a GameObject, or SetGlobalValue |
| A haptic pulse on a phone | An instant action's `Fire`, category Haptics | The platform's call through a plugin (Android's one-shot vibration with an amplitude of 1 to 255, iOS Core Haptics' transient event), or Handheld.Vibrate, which has no strength and exists only in Android and iOS builds — guard it with `#if UNITY_ANDROID \|\| UNITY_IOS` |
| A phone vibration over time | A global effect's `Apply` and `End`, category Haptics | The vibration's strength each frame (Android 0 to 255, iOS 0 to 1), stopped in `End` |

FMOD and Wwise pause their sounds themselves when the application is paused; a phone's haptics are yours to stop
(Android vibrates only in the foreground, and iOS stops its haptics engine in the background). FMOD's runtime does not
run outside play mode, so leave an FMOD feedback's preview on Skip.

**Changing a feedback that assets use.** Scenes, prefabs and presets store each item by its class, namespace and
assembly. To rename any of the three, add `[MovedFrom]` (UnityEngine.Scripting.APIUpdating) with the old name, and the
saved items follow. Without it the items' type is missing: their list is locked, and a save in the session the type went
missing drops them, so restore the type before you save. Do not make a used feedback abstract or move it off its base —
Unity then reads its slots as empty, and the next save drops them; the console says so after the scripts reload. Keep
the feedback in an assembly that builds include: a FeedbackPlayer that holds a feedback from an editor-only assembly
does not load in a build at all (FB-B09).

**In the debugger.** In **Playbacks**, select a play: your feedbacks' item rows read like the package's, and their
**Note** reads `faulted` once a call of yours threw in that play (only its `End` still comes), or `not called in the
preview` when the preview skips the type. The time your calls take counts in the **Stages** panel's `Feedback.Update`;
wrap a costly call in a `ProfilerMarker` of your own to see it apart. A custom effect writes through your code, not a
property slot, so it has no **Layers** row.

## Your own targets

Drive a value of your own component: implement `IFeedbackTarget<T>` for a float, a `Vector3` or a `Color` and point a
`Custom/Float Value`, `Custom/Vector3 Value` or `Custom/Color Value` item at that component. The item composes its
motions over the base value your getter gives and writes the result through your setter.

### IFeedbackTarget<T>

<!-- gen:api:xpTURN.Feedback.IFeedbackTarget`1 -->
A component a Custom Value item drives: the getter gives the base value, the setter takes the composed value. The items drive float, Vector3 and Color; another T is driven by none.

| Member | Description |
|---|---|
| `T FeedbackValue { get; set; }` | The value — read as the base (a revert reads it back), written with the composed value. |
<!-- /gen:api:xpTURN.Feedback.IFeedbackTarget`1 -->

<!-- gen:snippet:api-custom-target -->
```csharp
public class EngineHum : MonoBehaviour, IFeedbackTarget<float>
{
    [SerializeField] AudioSource hum;

    public float FeedbackValue
    {
        get => hum.volume;
        set => hum.volume = value;
    }
}
```
<!-- /gen:snippet:api-custom-target -->

The getter gives the base value the item's motions add to and is read each frame the item plays; the setter takes the
composed value when it differs from the base, and the base again when the effect is taken off. See
[How values are written](Concepts.md#how-values-are-written).

### IFeedbackTargetStage

<!-- gen:api:xpTURN.Feedback.IFeedbackTargetStage -->
Chooses when a custom target's value is written; without it, Update.

| Member | Description |
|---|---|
| `FeedbackTargetStage Stage { get; }` | Update or LateUpdate — read once when the player prepares. |
<!-- /gen:api:xpTURN.Feedback.IFeedbackTargetStage -->

Implement it beside `IFeedbackTarget<T>` when the value must be written after `LateUpdate` — when a `LateUpdate` of
yours would overwrite it, say. The player reads `Stage` once, when it prepares.

### FeedbackTargetStage

<!-- gen:api:xpTURN.Feedback.FeedbackTargetStage -->
When a custom target's composed value is written.

| Value | Description |
|---|---|
| `Update = 0` | In the Update stage; the frame's LateUpdate sees the value. |
| `LateUpdate = 1` | After LateUpdate, before rendering. |
<!-- /gen:api:xpTURN.Feedback.FeedbackTargetStage -->
