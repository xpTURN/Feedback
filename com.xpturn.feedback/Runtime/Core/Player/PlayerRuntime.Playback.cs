using System;
using UnityEngine;

namespace xpTURN.Feedback
{
    // why a play cannot be sought — in the scrub's order
    internal enum SeekRefusal : byte
    {
        None,
        NotLive,        // not this player's live play
        UntimedWait,    // a signal wait without a timeout (its program is endless too) · a wait on another player
        Endless,
        NotSeekable,    // its confirmed result — a zero-period block, a saturated total
        OffNominal,     // a forward play off the nominal axis — a hit stop, a signal before its timeout
        Simulated,      // the preview drives a simulated target
    }

    // the scheduler's runner — the pipeline of a Play, the tick and its events, the ends, Stop · Pause · Resume and the
    // lengths, the direction, Seek, Skip, Restore and Signal
    internal sealed partial class PlayerRuntime
    {
        // ---- the records

        int ActiveCount()
        {
            int n = 0;
            PlaybackRecord[] records = m_Records;
            for (int r = 0; r < records.Length; r++)
            {
                if (records[r].Active)
                    n++;
            }
            return n;
        }

        // the paused plays count
        internal bool IsPlaying => ActiveCount() > 0;

        int Free()
        {
            PlaybackRecord[] records = m_Records;
            for (int r = 0; r < records.Length; r++)
            {
                if (!records[r].Active)
                    return r;
            }
            return -1;
        }

        int Oldest()
        {
            int found = -1;
            PlaybackRecord[] records = m_Records;
            for (int r = 0; r < records.Length; r++)
            {
                if (records[r].Active && (found < 0 || records[r].Serial < records[found].Serial))
                    found = r;
            }
            return found;
        }

        int Newest()
        {
            int found = -1;
            PlaybackRecord[] records = m_Records;
            for (int r = 0; r < records.Length; r++)
            {
                if (records[r].Active && (found < 0 || records[r].Serial > records[found].Serial))
                    found = r;
            }
            return found;
        }

        // ---- the turn: the newest live play turns around where it is — the same play goes on. No policy, cooldown or chance
        // and no event: no new play. Inside the play's events it turns at the pass's end. false = no play to turn — none, the
        // same way, ended, driven by a track, not seekable, off the nominal axis
        internal bool Turn(bool reverse, out PlaybackHandle handle)
        {
            handle = default;
            int index = Newest();
            if (index < 0)
                return false;
            PlaybackRecord record = m_Records[index];
            if (!Turnable(record, reverse))
                return false;
            handle = record.Handle;
            if (record.InEvents)
            {
                record.HasPendingTurn = true;
                record.PendingTurnReverse = reverse;
                return true;
            }
            TurnRecord(record, index, reverse);
            return true;
        }

        static bool Turnable(PlaybackRecord record, bool reverse)
            => record.Active && record.Reverse != reverse && !record.Finished && !record.Driven && record.Resolved.IsSeekable && (!reverse || !record.Cursor.IsOffNominal);

        // a forward play's place p has done the instants up to p, a backward head h has undone those from h up: the head goes
        // one µs past the cursor's place, the cursor one µs below the head — nothing swept. What runs keeps running, each
        // binding takes the new direction, and the place is evaluated once
        void TurnRecord(PlaybackRecord record, int index, bool reverse)
        {
            TimelineClock baseClock = m_Player.m_TimeMode == PlayerTimeMode.Unscaled ? TimelineClock.Unscaled : TimelineClock.Scaled;
            if (reverse)
            {
                record.ReverseClock = m_Program.PositionClock(baseClock);
                record.Sampler.PlaceReverse(TimelineTime.Add(record.Cursor.Position, 1));
            }
            else
                record.Cursor.BeginAt(baseClock, record.Sampler.ReversePosition - 1);
            record.Reverse = reverse;
            long serial = record.Serial;
            ItemBinding[] bindings = m_Bindings;
            for (int i = 0; i < bindings.Length; i++)
                bindings[i]?.Turn(index, serial, reverse);
            EvaluateRunning(record, index);
        }

        bool Find(PlaybackHandle handle, out int index)
        {
            PlaybackRecord[] records = m_Records;
            for (int r = 0; r < records.Length; r++)
            {
                if (records[r].Active && records[r].Handle == handle)
                {
                    index = r;
                    return true;
                }
            }
            index = -1;
            return false;
        }

        // the list index, not the program's: an inactive flow item ahead moves the program's numbers
        uint ItemSeed(uint seed, int programItem) => Hash32.Of(seed, (uint)m_ListIndex[programItem]);

        static float Range(Vector2 range, uint hash) => Factor(Mathf.Lerp(range.x, range.y, Hash32.ToUnit(hash)));

        // a remap's or a falloff's result: finite and not negative, else 0
        static float Factor0(float value) => value >= 0f && value < float.PositiveInfinity ? value : 0f;

        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        static bool Finite(Vector3 v) => Finite(v.x) && Finite(v.y) && Finite(v.z);

        // ---- the pipeline. The player has refused a nested Play and prepared already

        internal PlaybackHandle Play(in PlayArgs args, System.Threading.CancellationToken token)
        {
            // a released runtime and a closed scheduler refuse quietly; an argument outside the contract warns
            if (m_Released || m_Scheduler.IsShutdown)
                return default;
            FeedbackPlayer player = m_Player;
            FeedbackContext context = m_Scheduler.Context;
            float input = args.Intensity;
            if (!(input >= 0f && input < float.PositiveInfinity))
            {
#if DEBUG
                player.WarnArgument("intensity " + input);
#endif
                return default;
            }
            if (args.HasPosition && !Finite(args.Position))
            {
#if DEBUG
                player.WarnArgument("position " + args.Position);
#endif
                return default;
            }
            // a direction past the last value (the saved enum grows at its end: move this bound with it)
            if (args.HasDirection && (uint)args.Direction > (uint)PlayerDirection.Toggle)
            {
#if DEBUG
                player.WarnArgument("direction " + (int)args.Direction);
#endif
                return default;
            }
            // the decided refusals first, the chance last: the only refusal that draws. A canceled token is a quiet refusal
            if (token.IsCancellationRequested)
                return default;
            double now = context.Now;
            // a preview's unconditional play passes the player's cooldown, gate (its falloff too) and chance, the items'
            // chance, cooldown and plays, and the random lengths — a track's driven play too, in any kind
            bool free = (args.Unconditional && context.Kind == ContextKind.Preview) || args.Driven;
            if (!free && now - m_LastPlay < player.m_Cooldown)
                return default;
            float falloff = 1f;
            if (!free && player.m_UseDistanceGate && !Gate(in args, out falloff))
                return default;
            PlayPolicy policy = player.m_Policy;
            if (policy == PlayPolicy.Ignore && ActiveCount() > 0)
                return default;
            // a refused backward play puts the random back to here
            XorShiftRandom random = m_Random;
            if (!free && player.m_Chance < 100f && m_Random.NextUnit() * 100f >= player.m_Chance)
                return default;
            // the direction: the argument, else the setting (an unknown saved value is Forward). Toggle reads the cell — it
            // turns when the play is accepted
            PlayerDirection direction = args.HasDirection ? args.Direction : player.m_Direction;
            bool toggle = direction == PlayerDirection.Toggle;
            bool reverse = toggle ? m_ToggleBackward : direction == PlayerDirection.Backward;
            // the seed and each item's inputs: the items' random draws are the seed's hashes
            uint seed = args.HasSeed ? args.Seed : m_Random.Next();
            float intensity = input;
            if (player.m_RemapIntensity && player.m_IntensityRemap != null)
                intensity = Factor0(player.m_IntensityRemap.Evaluate(input));
            intensity *= Factor(player.m_Intensity) * falloff;
            // a preview's masks — a masked item keeps its place
            bool preview = context.Kind == ContextKind.Preview;
            byte[] masks = preview ? player.m_PreviewMasks : null;
            int soloOnce = preview ? player.m_PreviewSoloOnce : -1;
            bool anySolo = AnySolo(masks);
            for (int i = 0; i < m_PlaySkip.Length; i++)
            {
                m_Scale[i] = 1f;
                // a driven play leaves the time scale alone — its director's clock reads it
                m_PlaySkip[i] = m_Skip[i] || (preview && Masked(i, masks, anySolo, soloOnce)) || (args.Driven && m_Effects[i] is TimeScaleItem) || Skips(i, seed, intensity, now, reverse, free);
                EffectItem effect = m_Effects[i];
                if (!free && effect != null && effect.m_Random.m_RandomizeDuration)
                    m_Scale[i] = Range(effect.m_Random.m_DurationRange, Hash32.Tagged(ItemSeed(seed, i), Hash32.Duration, 0u));
            }
            // the random part of the player's length is the seed's hash too — the backward check and the timeline take
            // the same
            float global = Factor(player.m_DurationMultiplier);
            if (!free && player.m_RandomizeDuration)
                global *= Range(player.m_DurationRange, Hash32.Tagged(seed, Hash32.Duration, 0u));
            float delay = Offset(player.m_StartDelay);
            // a backward play is confirmed in the runtime's own buffer first: a list that cannot play backward is refused
            // before the policy stops anything, the random put back (the confirmed result: a zero-period endless block and a
            // saturated total pass the static check)
            if (reverse)
            {
                m_Program.Resolve(m_Scale, m_PlaySkip, global, delay, m_Judge);
                if (!m_Judge.IsSeekable)
                {
                    m_Random = random;
#if DEBUG
                    player.WarnReverse();
#endif
                    return default;
                }
            }
            // the policy makes room: user code (Stopped) runs here and may release this runtime
            StopForPolicy(policy);
            if (m_Released)
                return default;
            // the token's entry — registered when new. Canceled since the refusals (the policy's Stopped, another thread):
            // refused like there
            int tokenEntry = AcquireToken(token);
            if (token.IsCancellationRequested)
                return default;
            int index = Free();
            if (index < 0)
                return default;
            PlaybackRecord record = m_Records[index];
            // this play's timeline
            m_Program.Resolve(m_Scale, m_PlaySkip, global, delay, record.Resolved);
            // accepted: what a refused play must leave alone is written now
            m_LastPlay = now;
            if (toggle)
                m_ToggleBackward = !m_ToggleBackward;
            player.NoteAccepted();
            for (int i = 0; i < m_PlaySkip.Length; i++)
            {
                record.Running[i] = false;
                record.Touched[i] = false;
                record.Output[i] = 1f;
                record.Turned[i] = false;
                EffectItem effect = m_Effects[i];
                if (effect == null || m_PlaySkip[i])
                    continue;
                record.Turned[i] = Turns(i, reverse, record.Resolved.ItemDuration(i));
                m_ItemLastPlay[i] = now;
                m_ItemPlays[i]++;
                if (effect.m_Random.m_RandomizeOutput)
                    record.Output[i] = Range(effect.m_Random.m_OutputRange, Hash32.Tagged(ItemSeed(seed, i), Hash32.Output, 0u));
            }
            record.Active = true;
            record.Paused = false;
            record.Driven = args.Driven;
            record.Held = args.Driven;
            record.Finished = false;
            record.Intensity = intensity;
            record.Seed = seed;
            record.Serial = ++m_Serial;
            record.StartedAt = now;
            record.EndedCount = 0;
            record.Reverse = reverse;
            record.InEvents = false;
            record.PendingSkip = false;
            record.HasPendingSeek = false;
            record.HasPendingTurn = false;
            record.InBegin = true;
            record.BeginLooped = 0;
            // the play's place, and the player's — a channel shake measures from it once the player is gone
            record.HasPosition = args.HasPosition;
            record.Position = args.Position;
            record.OwnerPlace = m_Transform != null ? m_Transform.position : default;
            // the scheduler's slot. None when the policy's Stopped closed the scheduler: the next entry releases this runtime
            PlaybackHandle handle = m_Scheduler.Add(this, index);
            record.Handle = handle;
            if (handle == default)
            {
                record.Active = false;
                return default;
            }
            // the listener and the token after the acceptance, before t = 0
            record.Listener = args.Listener;
            record.Token = tokenEntry;
            // t = 0, here: the events of the Begin window and the first evaluation. A binding's exception ends this play
            // alone and the caller gets no handle. Backward the sampler's head starts at the total (a backward Started
            // evaluates the T = 1 side) on ν's clock — the cursor's own function
            long serial = record.Serial;
            try
            {
                TimelineClock baseClock = player.m_TimeMode == PlayerTimeMode.Unscaled ? TimelineClock.Unscaled : TimelineClock.Scaled;
                bool passed;
                if (reverse)
                {
                    record.ReverseClock = m_Program.PositionClock(baseClock);
                    record.Sampler.BeginReverse();
                    passed = SamplerPass(record, index, serial, true);
                }
                else
                {
                    record.Cursor.Begin(baseClock);
                    passed = Pass(record, index, serial, true);
                }
                if (passed && !record.Finished)
                    EvaluateRunning(record, index);
            }
            catch (Exception e)
            {
                context.LogException(e);
                if (record.Active && record.Serial == serial)
                    End(record, index, EndReason.Aborted, PlaybackEnd.Faulted, false);
                return default;
            }
            if (record.Serial == serial)
                record.InBegin = false;
            if (m_Released)
                return default;
            // Played: once for every accepted play, after t = 0 and before its end. A play its own t = 0 stopped has had its
            // Stopped already
            player.Raise(PlayerEventKind.Played, handle, context);
            // the Begin window's repeat boundaries come after Played — none when Played's subscriber ended the play: a play's
            // end is its last event
            for (int n = record.BeginLooped; n > 0 && record.Active && record.Serial == serial; n--)
                player.Raise(PlayerEventKind.Looped, handle, context);
            if (record.Active && record.Serial == serial && record.Finished)
                Finish(record, index);
            return handle;
        }

        // the distance gate: true = in range, falloff = the intensity's factor. No reference is not out of range
        bool Gate(in PlayArgs args, out float falloff)
        {
            falloff = 1f;
            FeedbackPlayer player = m_Player;
            float range = player.m_GateDistance;
            if (!(range > 0f))
                return true;
            Vector3 from;
            Vector3 to;
            switch (player.m_GateReference)
            {
                case DistanceReference.PlayPosition:
                    if (!args.HasPosition)
                        return true;
                    from = m_Transform.position;
                    to = args.Position;
                    break;
                case DistanceReference.Anchor:
                    // by name at each Play — an anchor set after the prepare counts at once
                    Transform anchor = AnchorTransform(player, player.m_GateAnchor);
                    if (anchor == null)
                        return true;
                    from = args.HasPosition ? args.Position : m_Transform.position;
                    to = anchor.position;
                    break;
                case DistanceReference.Global:
                    // the context's range center — none or a destroyed one passes
                    Transform center = m_Scheduler.Context.RangeCenter;
                    if (center == null)
                        return true;
                    from = args.HasPosition ? args.Position : m_Transform.position;
                    to = center.position;
                    break;
                default:
                    Camera camera = Camera.main;
                    if (camera == null)
                        return true;
                    from = args.HasPosition ? args.Position : m_Transform.position;
                    to = camera.transform.position;
                    break;
            }
            float distance = Vector3.Distance(from, to);
            if (distance > range)
                return false;
            if (player.m_UseGateFalloff && player.m_GateFalloff != null)
                falloff = Factor0(player.m_GateFalloff.Evaluate(distance / range));
            return true;
        }

        // an anchor's object as a place: a component's or a game object's transform
        static Transform AnchorTransform(FeedbackPlayer player, string name)
        {
            if (string.IsNullOrEmpty(name) || !player.TryGetAnchor(name, out UnityEngine.Object found))
                return null;
            if (found is Component component)
                return component.transform;
            return found is GameObject go ? go.transform : null;
        }

        // the flashes one start makes: a target item's motion tells (Blink counts its periods), anything else is one
        static int FlashCount(EffectItem effect, double seconds)
            => effect is TargetItem target && target.m_Motion != null ? target.m_Motion.FlashCount(seconds) : 1;

        // an effect item's skips in this play: it keeps its place, its events still come
        bool Skips(int i, uint seed, float intensity, double now, bool reverse, bool free)
        {
            EffectItem effect = m_Effects[i];
            if (effect == null)
                return false;
            // a category turned off leaves the item out of new plays — first, before its random draws. No warning: the
            // user's choice
            if (!m_Gate.IsEnabled(m_Category[i]))
                return true;
            ref ItemConditions c = ref effect.m_Conditions;
            if (!free)
            {
                if (c.m_Chance < 100f && Hash32.ToUnit(Hash32.Tagged(ItemSeed(seed, i), Hash32.Chance, 0u)) * 100f >= c.m_Chance)
                    return true;
                if (now - m_ItemLastPlay[i] < c.m_Cooldown)
                    return true;
                if (c.m_MaxPlays > 0 && m_ItemPlays[i] >= c.m_MaxPlays)
                    return true;
            }
            // the direction's condition (the items' inputs)
            if (c.m_Direction == DirectionCondition.ReverseOnly ? !reverse : c.m_Direction == DirectionCondition.ForwardOnly && reverse)
                return true;
            if (c.m_UseIntensityRange && (intensity < c.m_IntensityRange.x || intensity > c.m_IntensityRange.y))
                return true;
            return false;
        }

        // a preview's mask of an effect item by its list index — a flow item never: the item's ▶ leaves its item alone, a Solo
        // hides the items that are not, a Mute hides its own
        bool Masked(int i, byte[] masks, bool anySolo, int soloOnce)
        {
            if (m_Effects[i] == null)
                return false;
            int list = m_ListIndex[i];
            if (soloOnce >= 0)
                return list != soloOnce;
            byte mask = masks != null && list < masks.Length ? masks[list] : (byte)0;
            return mask == 1 || (anySolo && mask != 2);
        }

        static bool AnySolo(byte[] masks)
        {
            if (masks == null)
                return false;
            for (int i = 0; i < masks.Length; i++)
            {
                if (masks[i] == 2)
                    return true;
            }
            return false;
        }

        // the policy: Overlap ends the oldest play when it is full, Restart every play that runs — Hold, their end is Stopped
        void StopForPolicy(PlayPolicy policy)
        {
            if (policy == PlayPolicy.Overlap)
            {
                if (Free() >= 0)
                    return;
                int oldest = Oldest();
                if (oldest >= 0)
                    End(m_Records[oldest], oldest, EndReason.Hold, PlaybackEnd.Stopped, true);
                return;
            }
            long last = m_Serial;
            for (int r = 0; r < m_Records.Length; r++)
            {
                if (m_Records[r].Active && m_Records[r].Serial <= last)
                    End(m_Records[r], r, EndReason.Hold, PlaybackEnd.Stopped, true);
            }
        }

        // ---- the tick

        public bool Advance(int state, in FrameTime time)
        {
            PlaybackRecord record = m_Records[state];
            if (!record.Active)
                return false;
            // a token canceled off the main thread stops its play here — Hold, Stopped
            if (record.Token >= 0 && m_Tokens.IsCanceled(record.Token))
            {
                End(record, state, EndReason.Hold, PlaybackEnd.Stopped, true);
                return false;
            }
            // a detached play with no live item left ends quietly — Destroyed
            if (m_Detached && !AnyAlive(record))
            {
                End(record, state, EndReason.Hold, PlaybackEnd.Destroyed, false);
                return false;
            }
            if (record.Paused || record.Held)
                return true;
            // the speed on both clocks — 0 stands, it does not pause
            double speed = Factor(m_Player.m_Speed);
            long serial = record.Serial;
            if (record.Reverse)
            {
                // the head moves on ν's clock
                double dt = record.ReverseClock == TimelineClock.Unscaled ? time.UnscaledDelta : time.ScaledDelta;
                record.Sampler.AdvanceReverse(dt * speed);
                if (!SamplerPass(record, state, serial, true))
                    return false;
            }
            else
            {
                record.Cursor.BeginAdvance(time.ScaledDelta * speed, time.UnscaledDelta * speed);
                if (!Pass(record, state, serial, true))
                    return false;
            }
            if (record.Finished)
            {
                Finish(record, state);
                return false;
            }
            EvaluateRunning(record, state);
            return true;
        }

        // the scheduler ended the playback — its slot is dead already. No event
        public void Abort(int state, AbortReason reason)
        {
            PlaybackRecord record = m_Records[state];
            if (record.Active)
                End(record, state, EndReason.Aborted, reason == AbortReason.Faulted ? PlaybackEnd.Faulted : PlaybackEnd.ContextShutdown, false);
        }

        public UnityEngine.Object DebugOwner => m_Player;

        // the events of one pass — the Begin window or a tick. false = the play ended inside its own events (a Stop from
        // user code, the runtime's release): End has settled it and aborted the cursor. pending: a Seek · Skip the events
        // asked for runs at the end (not after a seek's or a skip's own pass: what those ask waits a pass)
        bool Pass(PlaybackRecord record, int index, long serial, bool pending)
        {
            TimelineCursor cursor = record.Cursor;
            record.InEvents = true;
            while (cursor.TryNext(out TimelineEvent e))
            {
                Handle(record, index, in e);
                if (!record.Active || record.Serial != serial)
                    return false;
            }
            cursor.EndAdvance();
            record.InEvents = false;
            Settle(record, index);
            return !pending || RunPending(record, index, serial);
        }

        // the sampler's events — the backward head, a seek's sweep, a backward skip
        bool SamplerPass(PlaybackRecord record, int index, long serial, bool pending)
        {
            TimelineSampler sampler = record.Sampler;
            record.InEvents = true;
            while (sampler.TryNext(out TimelineEvent e))
            {
                Handle(record, index, in e);
                if (!record.Active || record.Serial != serial)
                    return false;
            }
            record.InEvents = false;
            Settle(record, index);
            return !pending || RunPending(record, index, serial);
        }

        // the Seek · Skip a play's own events asked for, at the pass's end — Skip first, once (a seek in the middle of the
        // cursor's tick runs an instant twice and the tick's end writes over its place) — then a turn they asked for
        bool RunPending(PlaybackRecord record, int index, long serial)
        {
            if (record.PendingSkip)
            {
                record.PendingSkip = false;
                record.HasPendingSeek = false;
                SkipRecord(record, index);
            }
            else if (record.HasPendingSeek)
            {
                record.HasPendingSeek = false;
                SeekRecord(record, index, record.PendingSeek);
            }
            if (record.HasPendingTurn)
            {
                record.HasPendingTurn = false;
                if (record.Active && record.Serial == serial && Turnable(record, record.PendingTurnReverse))
                    TurnRecord(record, index, record.PendingTurnReverse);
            }
            return record.Active && record.Serial == serial;
        }

        void Handle(PlaybackRecord record, int index, in TimelineEvent e)
        {
            switch (e.Kind)
            {
                case TimelineEventKind.Started:
                case TimelineEventKind.Ended:
                case TimelineEventKind.Fired:
                    // a skipped item's events come for the items after it
                    if (e.Skipped)
                        return;
                    int item = e.Item;
                    ItemBinding binding = m_Bindings[item];
                    if (binding == null)
                        return;
                    if (!binding.IsAlive)
                    {
                        // the target is gone: this item ends, the rest go on — one that never started is not called
                        if (record.Running[item])
                        {
                            record.Running[item] = false;
                            binding.End(index, EndReason.Aborted);
                            Mark(record, item);
                        }
                        return;
                    }
                    if (e.Kind == TimelineEventKind.Ended)
                    {
                        if (!record.Running[item])
                            return;
                        // a backward end is the T = 0 side — a turned item's the other, its seconds from the occurrence's end
                        if (record.Turned[item])
                            binding.Evaluate(index, new MotionTime(e.Reverse ? 1f : 0f, (TimelineTime.Add(e.OccurrenceStart, record.Resolved.ItemDuration(item)) - e.Time) / TimelineTime.MicrosPerSecond));
                        else
                            binding.Evaluate(index, EventTime(record, in e, e.Reverse ? 0f : 1f));
                        record.Running[item] = false;
                        binding.End(index, EndReason.Completed);
                        Mark(record, item);
                        return;
                    }
                    EffectItem effect = m_Effects[item];
                    float intensity = effect.m_Intensity == IntensityResponse.Ignore ? record.Output[item] : record.Intensity * record.Output[item];
                    // a one-direction instant does its forward act in its own direction
                    // a turned item runs the other way
                    bool reverse = (e.Reverse != record.Turned[item]) && !(e.Kind == TimelineEventKind.Fired && effect.m_Conditions.m_Direction == DirectionCondition.ReverseOnly);
                    // the occurrence's real length — the resolved length / Speed, +∞ at Speed 0 (a hit stop asks for it). The
                    // flash limit counts each occurrence of an item it covers on the item's clock: a refused one does not run —
                    // no binding call, not running, so its end goes nowhere either
                    double itemSeconds = TimelineTime.ToSeconds(record.Resolved.ItemDuration(item));
                    double speed = Factor(m_Player.m_Speed);
                    double seconds = speed > 0.0 ? itemSeconds / speed : double.PositiveInfinity;
                    if (m_Flashes[item] && !m_Gate.TryFlash(FlashCount(effect, itemSeconds)))
                        return;
                    // the play's place — once the player is gone, the Play's
                    bool hasPosition = record.HasPosition;
                    Vector3 position = record.Position;
                    if (!hasPosition && m_Transform == null)
                    {
                        hasPosition = true;
                        position = record.OwnerPlace;
                    }
                    var context = new BeginContext(intensity, ItemSeed(record.Seed, item), e.Occurrence, record.Serial, reverse, seconds, hasPosition, position, record.Sweeping);
                    record.Touched[item] = true;
                    if (e.Kind == TimelineEventKind.Fired)
                    {
                        binding.Execute(index, in context);
                        return;
                    }
                    // the end of the pass evaluates what runs; an item that ends in this pass is evaluated by its end
                    binding.Begin(index, in context);
                    record.Running[item] = true;
                    return;
                case TimelineEventKind.LoopIteration:
                    // user Loop blocks only — the timeline gives none for an item's repeats. The Begin window's wait for
                    // Played
                    if (record.InBegin)
                    {
                        record.BeginLooped++;
                        return;
                    }
                    m_Player.Raise(PlayerEventKind.Looped, record.Handle, m_Scheduler.Context);
                    return;
                case TimelineEventKind.Finished:
                    record.Finished = true;
                    return;
            }
        }

        // the item runs against the play's way — never an item with a direction condition, one its binding ends, one without
        // an end
        bool Turns(int item, bool reverse, long natural)
        {
            EffectItem effect = m_Effects[item];
            if (effect == null || effect.m_Conditions.m_Direction != DirectionCondition.Always || m_Program.ItemOpen(item) || natural == TimelineTime.Infinite)
                return false;
            switch (effect.m_PlayDirection)
            {
                case ItemPlayDirection.Forward: return reverse;
                case ItemPlayDirection.Backward: return !reverse;
                case ItemPlayDirection.Opposite: return true;
                default: return false;
            }
        }

        static void Mark(PlaybackRecord record, int item)
        {
            if (record.InEnded[item])
                return;
            record.InEnded[item] = true;
            record.Ended[record.EndedCount++] = item;
        }

        // the end of a pass: what the pass ended lets its contribution go unless it started again. An item is out
        // of the list before its binding is called
        void Settle(PlaybackRecord record, int index)
        {
            while (record.EndedCount > 0)
            {
                int item = record.Ended[--record.EndedCount];
                record.InEnded[item] = false;
                m_Bindings[item].Settle(index);
            }
        }

        // an event's own time: T = 1 at an end, where it was cut for a truncated one, 0 for an item without an end
        static MotionTime EventTime(PlaybackRecord record, in TimelineEvent e, float t)
        {
            long natural = record.Resolved.ItemDuration(e.Item);
            if (natural == TimelineTime.Infinite)
                t = 0f;
            else if (e.Truncated && natural > 0)
                t = Mathf.Clamp01((float)((double)(e.Time - e.OccurrenceStart) / natural));
            return new MotionTime(t, (e.Time - e.OccurrenceStart) / TimelineTime.MicrosPerSecond);
        }

        // backward: the sample at the head's place — the forward coordinate, not turned
        void EvaluateRunning(PlaybackRecord record, int index)
        {
            ItemBinding[] bindings = m_Bindings;
            TimelineCursor cursor = record.Cursor;
            TimelineSample sample = null;
            if (record.Reverse)
            {
                sample = m_Sample;
                record.Sampler.Sample(record.Sampler.ReversePosition, true, sample);
            }
            for (int i = 0; i < bindings.Length; i++)
            {
                if (!record.Running[i])
                    continue;
                ItemBinding binding = bindings[i];
                if (!binding.IsAlive)
                {
                    record.Running[i] = false;
                    binding.End(index, EndReason.Aborted);
                    binding.Settle(index);
                    continue;
                }
                if (record.Turned[i])
                {
                    if (sample != null)
                        binding.Evaluate(index, new MotionTime(sample.LocalTimeFromEnd(i), sample.SecondsFromEnd(i)));
                    else
                        binding.Evaluate(index, new MotionTime(cursor.LocalTimeFromEnd(i), cursor.SecondsFromEnd(i)));
                }
                else if (sample != null)
                    binding.Evaluate(index, new MotionTime(sample.LocalTime(i), sample.Seconds(i)));
                else
                    binding.Evaluate(index, new MotionTime(cursor.LocalTime(i), cursor.Seconds(i)));
            }
        }

        // ---- the ends

        // the record is let go first — inside Completed the player is not playing and a Play takes this record again.
        // The running items left are a defence: the queue ends the background before Finished
        void Finish(PlaybackRecord record, int index)
        {
            PlaybackHandle handle = record.Handle;
            ItemBinding[] bindings = m_Bindings;
            for (int i = bindings.Length - 1; i >= 0; i--)
            {
                if (!record.Running[i])
                    continue;
                record.Running[i] = false;
                EndItem(bindings[i], index, EndReason.Completed);
                Mark(record, i);
            }
            SettleEnded(record, index);
            record.Active = false;
            m_Scheduler.Remove(handle);
            // the listener is taken before the event — a Play inside it takes this record
            IPlaybackListener listener = Take(record);
            m_Player.Raise(PlayerEventKind.Completed, handle, m_Scheduler.Context);
            Ended(handle, PlaybackEnd.Completed, false);
            Notify(listener, handle, PlaybackEnd.Completed);
            ReleaseIfDone();
        }

        // a stop — from outside, from inside the play's own events (the pass sees the record), the policy, an abort, the
        // release. Complete evaluates the end first; End in reverse list order, then Settle, then the event
        void End(PlaybackRecord record, int index, EndReason reason, PlaybackEnd end, bool stopped)
        {
            PlaybackHandle handle = record.Handle;
            ItemBinding[] bindings = m_Bindings;
            if (reason == EndReason.Complete)
            {
                // what runs goes to its end — backward the T = 0 side; an item without an end has none to go to
                for (int i = 0; i < bindings.Length; i++)
                {
                    if (!record.Running[i])
                        continue;
                    long natural = record.Resolved.ItemDuration(i);
                    if (natural != TimelineTime.Infinite)
                        EvaluateItem(bindings[i], index, record.Reverse != record.Turned[i] ? default : new MotionTime(1f, TimelineTime.ToSeconds(natural)));
                }
            }
            for (int i = bindings.Length - 1; i >= 0; i--)
            {
                ItemBinding binding = bindings[i];
                if (binding == null)
                    continue;
                if (record.Running[i])
                {
                    record.Running[i] = false;
                    EndItem(binding, index, reason);
                    Mark(record, i);
                }
                else if (reason == EndReason.Revert && record.Touched[i])
                    EndItem(binding, index, EndReason.Revert);
            }
            SettleEnded(record, index);
            record.Active = false;
            record.Cursor.Abort();
            m_Scheduler.Remove(handle);
            IPlaybackListener listener = Take(record);
            if (stopped)
                m_Player.Raise(PlayerEventKind.Stopped, handle, m_Scheduler.Context);
            Ended(handle, end, !stopped);
            Notify(listener, handle, end);
            ReleaseIfDone();
        }

        // once — the cell is emptied first
        static IPlaybackListener Take(PlaybackRecord record)
        {
            IPlaybackListener listener = record.Listener;
            record.Listener = null;
            record.Token = -1;
            return listener;
        }

        // after the end's event: the listener's exception is its own
        // the diagnostics' end — every end, with a listener or without
        void Ended(PlaybackHandle handle, PlaybackEnd end, bool quiet)
        {
            try
            {
                FeedbackDiagnostics.RaiseEnded(m_Player, handle, end, quiet);
            }
            catch (Exception e)
            {
                m_Scheduler.Context.LogException(e);
            }
        }

        void Notify(IPlaybackListener listener, PlaybackHandle handle, PlaybackEnd end)
        {
            if (listener == null)
                return;
            try
            {
                listener.OnPlaybackEnded(handle, end);
            }
            catch (Exception e)
            {
                m_Scheduler.Context.LogException(e);
            }
        }

        // a live play of this player takes a listener when its cell is empty
        internal bool SetListener(PlaybackHandle handle, IPlaybackListener listener)
        {
            if (handle == default || !Find(handle, out int index) || m_Records[index].Listener != null)
                return false;
            m_Records[index].Listener = listener;
            return true;
        }

        // inside an end every binding call is its own: one that throws is logged and the end goes on to the slot's removal
        void EndItem(ItemBinding binding, int index, EndReason reason)
        {
            try
            {
                binding.End(index, reason);
            }
            catch (Exception e)
            {
                m_Scheduler.Context.LogException(e);
            }
        }

        // what the pass and this end ended lets go — before the end's event
        void SettleEnded(PlaybackRecord record, int index)
        {
            while (record.EndedCount > 0)
            {
                int item = record.Ended[--record.EndedCount];
                record.InEnded[item] = false;
                try
                {
                    m_Bindings[item].Settle(index);
                }
                catch (Exception e)
                {
                    m_Scheduler.Context.LogException(e);
                }
            }
        }

        void EvaluateItem(ItemBinding binding, int index, in MotionTime time)
        {
            try
            {
                binding.Evaluate(index, in time);
            }
            catch (Exception e)
            {
                m_Scheduler.Context.LogException(e);
            }
        }

        // ---- the API

        static EndReason ReasonOf(StopMode mode) => mode == StopMode.Revert ? EndReason.Revert : mode == StopMode.Complete ? EndReason.Complete : EndReason.Hold;

        internal bool Stop(PlaybackHandle handle, StopMode mode)
        {
            if (handle == default || !Find(handle, out int index))
                return false;
            End(m_Records[index], index, ReasonOf(mode), PlaybackEnd.Stopped, true);
            return true;
        }

        // the plays that run now — one a Stopped subscriber starts is not among them
        internal void StopAll(StopMode mode)
        {
            long last = m_Serial;
            EndReason reason = ReasonOf(mode);
            for (int r = 0; r < m_Records.Length; r++)
            {
                if (m_Records[r].Active && m_Records[r].Serial <= last)
                    End(m_Records[r], r, reason, PlaybackEnd.Stopped, true);
            }
        }

        // a mark on each play that runs — Paused once when one changed
        internal void Pause()
        {
            bool any = false;
            for (int r = 0; r < m_Records.Length; r++)
            {
                PlaybackRecord record = m_Records[r];
                if (!record.Active || record.Paused || record.Held)
                    continue;
                record.Paused = true;
                any = true;
            }
            if (any)
                m_Player.Raise(PlayerEventKind.Paused, default, m_Scheduler.Context);
        }

        internal void Resume()
        {
            bool any = false;
            for (int r = 0; r < m_Records.Length; r++)
            {
                PlaybackRecord record = m_Records[r];
                if (!record.Active || !record.Paused)
                    continue;
                record.Paused = false;
                any = true;
            }
            if (any)
                m_Player.Raise(PlayerEventKind.Resumed, default, m_Scheduler.Context);
        }

        // the play's own length — 0 for a handle that is not a live play of this player
        internal float GetDuration(PlaybackHandle handle)
        {
            if (handle == default || !Find(handle, out int index))
                return 0f;
            return (float)m_Records[index].Resolved.TotalSeconds;
        }

        // ---- Seek · Skip · Restore · Signal

        // the plays that run when it is called, the paused ones too. Not a seekable play (its confirmed result) and not a
        // forward play off the nominal axis — two clocks lived at different speeds, a signal before its timeout. Inside a
        // play's events the play is marked and moves at the pass's end. true = a play moved or is marked
        internal bool Seek(float time) => SeekAt(TimelineTime.FromSeconds(time));

        // the same plays and judgment, the target in microseconds — a float's seconds lose a microsecond past 32 s (the
        // scrub's end)
        internal bool SeekAt(long target)
        {
            bool any = false;
            long last = m_Serial;
            for (int r = 0; r < m_Records.Length; r++)
            {
                PlaybackRecord record = m_Records[r];
                if (!record.Active || record.Serial > last || !record.Resolved.IsSeekable || (!record.Reverse && record.Cursor.IsOffNominal))
                    continue;
                any = true;
                if (record.InEvents)
                {
                    record.HasPendingSeek = true;
                    record.PendingSeek = target;
                    continue;
                }
                SeekRecord(record, r, target);
            }
            return any;
        }

        // the sampler sweeps from the play's place to the target — forward crossed instants run, backward ones are undone,
        // in reverse. A forward play's cursor stands again at the target; a target at a backward play's start ends it now,
        // the same as a forward play reaching its end (the head's own end would wait a tick)
        void SeekRecord(PlaybackRecord record, int index, long target)
        {
            long serial = record.Serial;
            TimelineSampler sampler = record.Sampler;
            long from = record.Reverse ? sampler.ReversePosition : record.Cursor.Position;
            long to = sampler.SeekBegin(from, target, record.Reverse);
            bool passed;
            record.Sweeping = true;
            try
            {
                passed = SamplerPass(record, index, serial, false);
            }
            finally
            {
                record.Sweeping = false;
            }
            if (!passed)
                return;
            if (!record.Reverse)
                record.Cursor.Rebuild(to);
            else if (to <= record.Resolved.RootDelay)
                record.Finished = true;
            if (record.Finished)
                FinishOutsideBegin(record, index);
            else
                EvaluateRunning(record, index);
        }

        // a live play's refusal for a seek, else the range that keeps it — forward [0, total − 1 µs], backward [the start
        // delay + 1 µs, total]: a forward play's end or a backward play's start delay ends it
        internal SeekRefusal SeekRange(PlaybackHandle handle, out long low, out long high)
        {
            low = 0;
            high = 0;
            if (handle == default || !Find(handle, out int index))
                return SeekRefusal.NotLive;
            if (m_Program.HasOpenItem)
                return SeekRefusal.UntimedWait;
            if (m_Program.IsInfinite)
                return SeekRefusal.Endless;
            PlaybackRecord record = m_Records[index];
            if (!record.Resolved.IsSeekable)
                return SeekRefusal.NotSeekable;
            if (!record.Reverse && record.Cursor.IsOffNominal)
                return SeekRefusal.OffNominal;
            if (record.Reverse)
            {
                low = record.Resolved.RootDelay + 1;
                high = record.Resolved.Total;
            }
            else
                high = record.Resolved.Total - 1;
            return SeekRefusal.None;
        }

        // a play paused now
        internal bool HasPaused
        {
            get
            {
                for (int r = 0; r < m_Records.Length; r++)
                {
                    if (m_Records[r].Active && m_Records[r].Paused)
                        return true;
                }
                return false;
            }
        }

        // one play's seek — the same judgment as SeekAt(long). false = not this player's live play, or one it does not move
        internal bool SeekAt(PlaybackHandle handle, long target)
        {
            if (handle == default || !Find(handle, out int index))
                return false;
            PlaybackRecord record = m_Records[index];
            if (!record.Resolved.IsSeekable || (!record.Reverse && record.Cursor.IsOffNominal))
                return false;
            if (record.InEvents)
            {
                record.HasPendingSeek = true;
                record.PendingSeek = target;
                return true;
            }
            SeekRecord(record, index, target);
            return true;
        }

        // a track holds one play while its director stands — the clock does not move it; Pause · Resume pass it
        internal bool SetHeld(PlaybackHandle handle, bool held)
        {
            if (handle == default || !Find(handle, out int index))
                return false;
            m_Records[index].Held = held;
            return true;
        }

        // the play's place on the nominal axis in microseconds — −1 for a handle that is not a live play
        internal long Position(PlaybackHandle handle)
        {
            if (handle == default || !Find(handle, out int index))
                return -1;
            PlaybackRecord record = m_Records[index];
            return record.Reverse ? record.Sampler.ReversePosition : record.Cursor.Position;
        }

        // where a seek starts the item at once: forward its node's start, backward its node's end (where a backward play meets
        // it) — −1 when not a live play or not in the program
        internal long ItemPlace(PlaybackHandle handle, int listIndex)
        {
            if (handle == default || !Find(handle, out int index))
                return -1;
            int item = Array.IndexOf(m_ListIndex, listIndex);
            if (item < 0)
                return -1;
            PlaybackRecord record = m_Records[index];
            int node = m_Program.NodeOfItem(item);
            return record.Reverse ? record.Resolved.NodeEnd(node) : record.Resolved.NodeStart(node);
        }

        // the plays that run when it is called, the paused ones too: a seekable one through its remaining events, each once
        // in causal order — forward by the cursor's event-driven completion, backward by a sweep to the start — and Completed;
        // another by Stop(Complete) (Stopped). Inside a play's events at the pass's end. true = a play ended or is marked
        internal bool SkipToEnd()
        {
            bool any = false;
            long last = m_Serial;
            for (int r = 0; r < m_Records.Length; r++)
            {
                PlaybackRecord record = m_Records[r];
                if (!record.Active || record.Serial > last)
                    continue;
                any = true;
                if (record.InEvents)
                {
                    record.PendingSkip = true;
                    continue;
                }
                SkipRecord(record, r);
            }
            return any;
        }

        void SkipRecord(PlaybackRecord record, int index)
        {
            // the cursor's completion takes seekable plays only — Unity's assertion is quiet
            if (!record.Resolved.IsSeekable)
            {
                End(record, index, EndReason.Complete, PlaybackEnd.Stopped, true);
                return;
            }
            long serial = record.Serial;
            if (record.Reverse)
            {
                record.Sampler.SeekBegin(record.Sampler.ReversePosition, 0, true);
                bool passed;
                record.Sweeping = true;
                try
                {
                    passed = SamplerPass(record, index, serial, false);
                }
                finally
                {
                    record.Sweeping = false;
                }
                if (!passed)
                    return;
                record.Finished = true;
            }
            else
            {
                record.Cursor.BeginComplete();
                bool passed;
                record.Sweeping = true;
                try
                {
                    passed = Pass(record, index, serial, false);
                }
                finally
                {
                    record.Sweeping = false;
                }
                if (!passed)
                    return;
            }
            if (record.Finished)
                FinishOutsideBegin(record, index);
        }

        // a play its own Begin window finished (a seek · skip its t = 0 events asked for) ends after Played
        void FinishOutsideBegin(PlaybackRecord record, int index)
        {
            if (!record.InBegin)
                Finish(record, index);
        }

        // the plays stop (Hold — Stopped), then each binding puts its first value back in reverse list order: the earlier
        // item wins. A play a Stopped subscriber starts goes on and writes over it at its next tick
        internal void Restore()
        {
            StopAll(StopMode.Hold);
            if (m_Released)
                return;
            for (int i = m_Bindings.Length - 1; i >= 0; i--)
            {
                ItemBinding binding = m_Bindings[i];
                if (binding == null)
                    continue;
                try
                {
                    binding.Restore();
                }
                catch (Exception e)
                {
                    m_Scheduler.Context.LogException(e);
                }
            }
        }

        // the waits Signal would end now — the forward plays' — and how many of them have no timeout
        internal int OpenSignalWaits(out int untimed)
        {
            int open = 0;
            untimed = 0;
            for (int r = 0; r < m_Records.Length; r++)
            {
                PlaybackRecord record = m_Records[r];
                if (record.Active && !record.Reverse)
                {
                    open += record.Cursor.OpenSignalWaits(out int u);
                    untimed += u;
                }
            }
            return open;
        }

        // ---- the open items

        // the depth of the waiting chain a flow item walks — a net for long chains, a cycle is found before it
        internal const int WaitDepth = 16;

        // the record's serial while it plays, else −1
        internal long Serial(int record) => m_Records[record].Active ? m_Records[record].Serial : -1;

        // an open item's node ends now — its binding heard its end. The serial guards a record a later play reuses; a backward
        // play has no open item (the backward check refuses it). true = ended
        internal bool EndOpenItem(int record, int listIndex, long serial)
        {
            if (m_Released)
                return false;
            PlaybackRecord r = m_Records[record];
            if (!r.Active || r.Serial != serial || r.Reverse)
                return false;
            int item = Array.IndexOf(m_ListIndex, listIndex);
            return item >= 0 && r.Cursor.EndItem(item) > 0;
        }

        // true = `player` plays this record or a play that waits on it (a waiting cycle), or the chain is past WaitDepth (deep).
        // The links are the waiting plays' listeners — a play's listener that is a wait link names the record that waits on it;
        // a link whose record plays another play now is no link
        internal bool InWaitChain(FeedbackPlayer player, int record, out bool deep)
        {
            deep = false;
            PlayerRuntime runtime = this;
            int index = record;
            for (int depth = 0; ; depth++)
            {
                if (ReferenceEquals(runtime.m_Player, player))
                    return true;
                if (depth + 1 >= WaitDepth)
                {
                    deep = true;
                    return true;
                }
                if (!(runtime.m_Records[index].Listener is IWaitLink link))
                    return false;
                PlayerRuntime parent = link.Runtime;
                if (parent == null || parent.m_Released || parent.Serial(link.Record) != link.ChainSerial)
                    return false;
                runtime = parent;
                index = link.Record;
            }
        }

        // the forward plays that run when it is called — a backward play has no open wait (the backward check refuses it).
        // The number of waits ended: 0 is the warning's cue
        internal int Signal()
        {
            int ended = 0;
            long last = m_Serial;
            for (int r = 0; r < m_Records.Length; r++)
            {
                PlaybackRecord record = m_Records[r];
                if (record.Active && record.Serial <= last && !record.Reverse)
                    ended += record.Cursor.Signal();
            }
            return ended;
        }
    }
}
