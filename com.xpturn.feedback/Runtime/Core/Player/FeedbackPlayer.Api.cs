using System;
using System.Collections.Generic;
using UnityEngine;

namespace xpTURN.Feedback
{
    // the playback API, the events and the event table. No saved field here. The public API is the main thread's
    public sealed partial class FeedbackPlayer
    {
        /// <summary>Raised when a play starts; the handle tells which play.</summary>
        public event Action<FeedbackPlayer, PlaybackHandle> Played;
        /// <summary>Raised when a play reaches its end; the handle tells which play.</summary>
        public event Action<FeedbackPlayer, PlaybackHandle> Completed;
        /// <summary>Raised when a play stops before its end; the handle tells which play.</summary>
        public event Action<FeedbackPlayer, PlaybackHandle> Stopped;
        /// <summary>Raised when a Loop block (Loop Start to Loop End) starts its next pass; the handle tells which play.</summary>
        public event Action<FeedbackPlayer, PlaybackHandle> Looped;
        /// <summary>Raised when the player pauses its plays.</summary>
        public event Action<FeedbackPlayer> Paused;
        /// <summary>Raised when the player resumes its paused plays.</summary>
        public event Action<FeedbackPlayer> Resumed;

        /// <summary>Plays the list with the default arguments; returns the play's handle, or default when the play is refused.</summary>
        public PlaybackHandle Play() => Play(default);

        /// <summary>Plays the list with the default arguments, as Play does, and returns nothing — the call a UnityEvent, a UI Button or a Timeline Signal Receiver can pick; listen to Played and the other events for the play.</summary>
        public void PlayFromEvent() => Play(default);

        /// <summary>Plays the list with <paramref name="intensity"/> as its intensity, as Play does with <see cref="PlayArgs.WithIntensity"/>, and returns nothing — a Slider's or a signal's value picked in a UnityEvent; a negative or NaN intensity plays nothing.</summary>
        public void PlayFromEvent(float intensity) => Play(PlayArgs.Default.WithIntensity(intensity));

        /// <summary>Plays the list forward when <paramref name="forward"/> is true and backward when it is false, and returns nothing — a Toggle's value picked in a UnityEvent; a play running the other way turns around where it is instead, the same play going on.</summary>
        public void PlayFromEvent(bool forward) => PlayTurning(forward ? PlayerDirection.Forward : PlayerDirection.Backward);

        /// <summary>Plays <paramref name="preset"/> — the linked preset or one of the Variants — as Play does with <see cref="PlayArgs.WithPreset"/>, and returns nothing — a preset picked in a UnityEvent; another preset plays nothing.</summary>
        public void PlayFromEvent(FeedbackPreset preset) => Play(PlayArgs.Default.WithPreset(preset));

        // a play that way: the newest live play turns around where it is when it runs the other way — the same play goes on,
        // with no policy, cooldown or chance and no Played; else Play with the direction
        internal PlaybackHandle PlayTurning(PlayerDirection direction)
        {
            PlayerRuntime newest = NewestPlaying();
            if (newest != null && newest.Turn(direction == PlayerDirection.Backward, out PlaybackHandle turned))
                return turned;
            return Play(PlayArgs.Default.WithDirection(direction));
        }

        /// <summary>Plays the list with <paramref name="args"/>; returns the play's handle, or default when the play is refused (a condition, the policy, an argument, not prepared) or faults at its start.</summary>
        public PlaybackHandle Play(in PlayArgs args) => Play(in args, default);

        // a lifetime bound with no await, and the entry of PlayAsync and the UniTask adapter
        /// <summary>Plays the list with <paramref name="args"/> and stops the play (Hold) when <paramref name="token"/> is canceled; an already canceled token refuses the play without a warning.</summary>
        public PlaybackHandle Play(in PlayArgs args, System.Threading.CancellationToken token)
        {
            // the t = 0 instants, Played and — for a list that ends at t = 0 — Completed run inside Play
            if (m_InPlay)
            {
                Warn(Refusal.Reentry, null);
                return default;
            }
            Refusal refusal = Prepare(null, null);
            if (refusal != Refusal.None)
            {
                Warn(refusal, null);
                return default;
            }
            PlayerRuntime runtime = Select(in args);
            if (runtime == null)
            {
                Warn(Refusal.Variant, null);
                return default;
            }
            m_InPlay = true;
            try
            {
                return runtime.Play(in args, token);
            }
            finally
            {
                m_InPlay = false;
            }
        }

        // the inspector's field: the plays from the next one on read it, one that runs keeps its own
        /// <summary>The player's Intensity: every play's strength is multiplied by it, from the next Play on — a play that runs keeps its own; a negative, NaN or infinite value is ignored with a warning.</summary>
        public float Intensity
        {
            get => m_Intensity;
            set
            {
                if (value >= 0f && value < float.PositiveInfinity)
                {
                    m_Intensity = value;
                    return;
                }
#if DEBUG
                WarnArgument("Intensity " + value);
#endif
            }
        }

        // read by each play that names no preset; Play mode's only — a preview never sees it
        /// <summary>The alias a play that names no preset picks among the player's lists — Play(), PlayFromEvent, a channel's play, a Timeline clip with an empty Preset; a key no list has, or none, plays the player's list. Set in Play mode only; a set outside it is ignored with a warning.</summary>
        public string VariantKey
        {
            get => m_VariantKey ?? "";
            set
            {
                if (!Application.isPlaying)
                {
                    Warn(Refusal.VariantKey, null);
                    return;
                }
                m_VariantKey = string.IsNullOrEmpty(value) ? null : value;
            }
        }

        // the lists in the order a key looks — the player's own (or its linked preset's), then the Variants' live slots; the
        // slot rule is the prepare's, so it answers before the prepare too
        /// <summary>True when one of the player's lists has <paramref name="alias"/> as its alias; <paramref name="preset"/> is that Variant, or null for the player's own list (or its linked preset's).</summary>
        public bool TryGetVariant(string alias, out FeedbackPreset preset)
        {
            preset = null;
            if (string.IsNullOrEmpty(alias))
                return false;
            if (string.Equals(ListAlias, alias, StringComparison.Ordinal))
                return true;
            List<FeedbackPreset> variants = m_Variants;
            for (int k = 0; variants != null && k < variants.Count; k++)
            {
                FeedbackPreset v = variants[k];
                if (v != null && !ReferenceEquals(v, m_Preset) && variants.IndexOf(v) == k && string.Equals(v.m_Alias, alias, StringComparison.Ordinal))
                {
                    preset = v;
                    return true;
                }
            }
            return false;
        }

        // the own list's alias — the linked preset's when linked
        internal string ListAlias => m_Preset != null ? m_Preset.m_Alias : m_ListAlias;

        /// <summary>Sets the listener that hears this live play's end, exactly once; false when the handle is not this player's live play or the play has a listener already — a play that ends inside Play is heard only through PlayArgs.WithListener.</summary>
        public bool SetListener(PlaybackHandle handle, IPlaybackListener listener)
        {
            if (listener == null)
            {
#if DEBUG
                WarnArgument("listener null");
#endif
                return false;
            }
            PlayerRuntime runtime = RuntimeOf(handle);
            return runtime != null && runtime.SetListener(handle, listener);
        }

        /// <summary>Stops the plays that run now with <paramref name="mode"/>; a play a Stopped handler starts keeps running.</summary>
        public void Stop(StopMode mode = StopMode.Hold)
        {
            if (Valid(mode))
                StopRuntimes(mode);
        }

        /// <summary>Stops one play with <paramref name="mode"/>; false when the handle is not this player's live play. Stop(default) is the other overload and stops every play.</summary>
        public bool Stop(PlaybackHandle handle, StopMode mode = StopMode.Hold)
        {
            if (!Valid(mode))
                return false;
            PlayerRuntime runtime = RuntimeOf(handle);
            return runtime != null && runtime.Stop(handle, mode);
        }

        /// <summary>Pauses the plays that run now; paused plays still count as playing, so the Policy refuses or stops a later Play as it would a running one.</summary>
        public void Pause()
        {
            PlayerRuntime runtime = m_Runtime;
            if (runtime == null)
                return;
            bool any = runtime.Pause();
            PlayerRuntime[] variants = m_VariantRuntimes;
            for (int i = 0; variants != null && i < variants.Length; i++)
            {
                if (variants[i] != null && variants[i].Pause())
                    any = true;
            }
            if (any)
                Raise(PlayerEventKind.Paused, default, runtime.Scheduler.Context);
        }

        /// <summary>Resumes the paused plays.</summary>
        public void Resume()
        {
            PlayerRuntime runtime = m_Runtime;
            if (runtime == null)
                return;
            bool any = runtime.Resume();
            PlayerRuntime[] variants = m_VariantRuntimes;
            for (int i = 0; variants != null && i < variants.Length; i++)
            {
                if (variants[i] != null && variants[i].Resume())
                    any = true;
            }
            if (any)
                Raise(PlayerEventKind.Resumed, default, runtime.Scheduler.Context);
        }

        /// <summary>True while any play runs, paused plays included.</summary>
        public bool IsPlaying => AnyActive();

        // the waits Signal() would end now and how many of them have no timeout — a preview tells them
        internal int OpenSignalWaits() => OpenSignalWaits(out _);

        internal int OpenSignalWaits(out int untimed)
        {
            untimed = 0;
            if (m_Runtime == null)
                return 0;
            int open = m_Runtime.OpenSignalWaits(out untimed);
            PlayerRuntime[] variants = m_VariantRuntimes;
            for (int i = 0; variants != null && i < variants.Length; i++)
            {
                if (variants[i] == null)
                    continue;
                open += variants[i].OpenSignalWaits(out int more);
                untimed += more;
            }
            return open;
        }

        // the preview's scrub and item play
        internal SeekRefusal SeekRange(PlaybackHandle handle, out long low, out long high)
        {
            low = 0;
            high = 0;
            PlayerRuntime runtime = RuntimeOf(handle);
            return runtime != null ? runtime.SeekRange(handle, out low, out high) : SeekRefusal.NotLive;
        }

        internal bool SeekAt(long target)
        {
            if (m_Runtime == null)
                return false;
            bool any = m_Runtime.SeekAt(target);
            PlayerRuntime[] variants = m_VariantRuntimes;
            for (int i = 0; variants != null && i < variants.Length; i++)
            {
                if (variants[i] != null && variants[i].SeekAt(target))
                    any = true;
            }
            return any;
        }

        // a track's: one play's seek and hold
        internal bool SeekAt(PlaybackHandle handle, long target)
        {
            PlayerRuntime runtime = RuntimeOf(handle);
            return runtime != null && runtime.SeekAt(handle, target);
        }

        internal bool SetHeld(PlaybackHandle handle, bool held)
        {
            PlayerRuntime runtime = RuntimeOf(handle);
            return runtime != null && runtime.SetHeld(handle, held);
        }

        // a track's: a driven play can seek the list — decided before the play; it prepares the player, as a Play does
        internal bool CanDrive() => EnsureRuntime() && m_Runtime.Drivable;

        // the play's arguments pick one of the player's lists, as Play picks it; it prepares the player
        internal bool Lists(in PlayArgs args) => EnsureRuntime() && Select(in args) != null;

        // the same for the list the arguments pick — false for arguments that pick none
        internal bool CanDrive(in PlayArgs args)
        {
            if (!EnsureRuntime())
                return false;
            PlayerRuntime runtime = Select(in args);
            return runtime != null && runtime.Drivable;
        }

        internal bool HasPaused
        {
            get
            {
                if (m_Runtime == null)
                    return false;
                if (m_Runtime.HasPaused)
                    return true;
                PlayerRuntime[] variants = m_VariantRuntimes;
                for (int i = 0; variants != null && i < variants.Length; i++)
                {
                    if (variants[i] != null && variants[i].HasPaused)
                        return true;
                }
                return false;
            }
        }

        internal long Position(PlaybackHandle handle)
        {
            PlayerRuntime runtime = RuntimeOf(handle);
            return runtime != null ? runtime.Position(handle) : -1;
        }

        internal long ItemPlace(PlaybackHandle handle, int listIndex)
        {
            PlayerRuntime runtime = RuntimeOf(handle);
            return runtime != null ? runtime.ItemPlace(handle, listIndex) : -1;
        }

        /// <summary>The nominal length of the list Play() plays (the one the VariantKey picks, else the player's list) — the duration multiplier and start delay in, no random part, infinity for an endless list; it prepares the player and is 0 when it cannot.</summary>
        public float TotalDuration => EnsureRuntime() ? (Keyed(m_VariantKey) ?? m_Runtime).TotalDuration : 0f;

        /// <summary>That play's own length, with its random parts and without its speed; 0 when the handle is not this player's live play.</summary>
        public float GetDuration(PlaybackHandle handle)
        {
            PlayerRuntime runtime = RuntimeOf(handle);
            return runtime != null ? runtime.GetDuration(handle) : 0f;
        }

        // the plays that run now — the paused ones too, they stay paused — go to `time` on the nominal axis: the instants
        // crossed run once (backward: undone, in reverse) and what runs is evaluated there. The axis does not turn with the
        // direction — +∞ is a backward play's start; Skip() or Seek(0) ends it. Inside a play's events it waits for the
        // pass's end. false = no play moved or waits (none · not seekable · off the nominal axis) — a negative or NaN time
        // warns
        /// <summary>Moves the plays that run now, paused ones too, to <paramref name="time"/> on the nominal axis, running the instants crossed forward once and taking back those crossed backward; false when no play moved.</summary>
        public bool Seek(float time)
        {
            if (!(time >= 0f))
            {
#if DEBUG
                WarnArgument("Seek " + time);
#endif
                return false;
            }
            if (m_Runtime == null)
                return false;
            bool any = m_Runtime.Seek(time);
            PlayerRuntime[] variants = m_VariantRuntimes;
            for (int i = 0; variants != null && i < variants.Length; i++)
            {
                if (variants[i] != null && variants[i].Seek(time))
                    any = true;
            }
            return any;
        }

        /// <summary>Ends the plays that run now: a seekable play runs its remaining events and completes, another stops with Complete; false when no play ended.</summary>
        public bool Skip()
        {
            if (m_Runtime == null)
                return false;
            bool any = m_Runtime.SkipToEnd();
            PlayerRuntime[] variants = m_VariantRuntimes;
            for (int i = 0; variants != null && i < variants.Length; i++)
            {
                if (variants[i] != null && variants[i].SkipToEnd())
                    any = true;
            }
            return any;
        }

        // the lists go back in the reverse order of their first plays: each one's snapshot is the value its first play met,
        // so the earliest list's goes back last
        /// <summary>Stops the plays that run now (Hold) and puts each value target back to its value before the first play since the player first prepared or last restored — a Rebuild keeps it, and with Apply Initial State it is the value before the first frame; it does not prepare.</summary>
        public void Restore()
        {
            PlayerRuntime runtime = m_Runtime;
            if (runtime == null)
                return;
            PlayerRuntime[] variants = m_VariantRuntimes;
            if (variants == null)
            {
                runtime.Restore();
                return;
            }
            Span<int> order = stackalloc int[1 + variants.Length];
            Order(runtime, variants, order, true);
            for (int i = 0; i < order.Length; i++)
                At(runtime, variants, order[i])?.Restore();
        }

        // ends the signal waits of the plays that run now — outside a tick at the next tick's start, inside an event at its
        // instant. No wait ended warns once (a wait that has not begun does not take it)
        /// <summary>Ends the signal waits of the plays that run now — at the next tick's start, or at once inside an event; warns once when no wait ended.</summary>
        public void Signal()
        {
            if (m_Destroyed || SignalRuntimes() > 0)
                return;
            m_MissedSignals++;
            Warn(Refusal.Signal, null);
        }

        // a channel's Signal — a broadcast reaches players with no wait, so no warning. The number of waits it ended
        internal int SignalQuietly()
        {
            if (m_Destroyed)
                return 0;
            int ended = SignalRuntimes();
            if (ended == 0)
                m_MissedQuietSignals++;
            return ended;
        }

        int SignalRuntimes()
        {
            if (m_Runtime == null)
                return 0;
            int ended = m_Runtime.Signal();
            PlayerRuntime[] variants = m_VariantRuntimes;
            for (int i = 0; variants != null && i < variants.Length; i++)
            {
                if (variants[i] != null)
                    ended += variants[i].Signal();
            }
            return ended;
        }

        // the calls that ended no wait, for the player's lifetime
        internal int MissedSignals => m_MissedSignals;

        internal int MissedQuietSignals => m_MissedQuietSignals;

        // the argument is checked before the prepare: an undefined mode warns on a player that is not prepared too
        bool Valid(StopMode mode)
        {
            if ((uint)mode <= (uint)StopMode.Complete)
                return true;
#if DEBUG
            WarnArgument("StopMode " + (int)mode);
#endif
            return false;
        }

#if DEBUG
        // the runtime's argument check warns through its player
        internal void WarnArgument(string what) => Warn(Refusal.Argument, what);

        // a backward play the list cannot have
        internal void WarnReverse() => Warn(Refusal.Reverse, null);
#endif

        // once per player and cause, development builds — the message is made there only. A closed scheduler and a
        // destroyed player are the usual way of a shutdown and a cleanup: no warning
        void Warn(Refusal refusal, string detail)
        {
#if DEBUG
            string message;
            switch (refusal)
            {
                case Refusal.Reentry: message = ReentryWarning; break;
                case Refusal.EditMode: message = EditModeWarning; break;
                case Refusal.NeverAwake: message = NeverAwakeWarning; break;
                case Refusal.Argument: message = ArgumentWarning + detail; break;
                case Refusal.Reverse: message = ReverseWarning; break;
                case Refusal.Signal: message = SignalWarning; break;
                case Refusal.Variant: message = VariantWarning; break;
                case Refusal.VariantKey: message = VariantKeyWarning; break;
                default: return;
            }
            ushort bit = (ushort)(1 << (int)refusal);
            if ((m_Warned & bit) != 0)
                return;
            m_Warned |= bit;
            Debug.LogWarning(message, this);
#endif
        }

        // ---- the runtimes: the list's and the Variants'

        // the runtime a Play's arguments pick: the list whose alias is the play's key, else the preset's (the list's for the
        // linked preset, null for a preset that is neither), else the list's when the play asks for it, else the list whose
        // alias is the player's key, else the list's
        internal PlayerRuntime Select(in PlayArgs args)
        {
            PlayerRuntime keyed = Keyed(args.VariantKey);
            if (keyed != null)
                return keyed;
            if (args.HasPreset)
                return RuntimeFor(args.Preset);
            if (args.HasPlayerList)
                return m_Runtime;
            return Keyed(m_VariantKey) ?? m_Runtime;
        }

        // the prepared list whose alias is `key` — the own list first, then the Variants in slot order; null for no key or none
        PlayerRuntime Keyed(string key)
        {
            if (string.IsNullOrEmpty(key) || m_Runtime == null)
                return null;
            if (string.Equals(ListAlias, key, StringComparison.Ordinal))
                return m_Runtime;
            PlayerRuntime[] variants = m_VariantRuntimes;
            for (int i = 0; variants != null && i < variants.Length; i++)
            {
                if (variants[i] != null && variants[i].ItemsOwner is FeedbackPreset preset && string.Equals(preset.m_Alias, key, StringComparison.Ordinal))
                    return variants[i];
            }
            return null;
        }

        internal PlayerRuntime RuntimeFor(FeedbackPreset preset)
        {
            if (ReferenceEquals(preset, null) || ReferenceEquals(preset, m_Preset))
                return m_Runtime;
            PlayerRuntime[] variants = m_VariantRuntimes;
            for (int i = 0; variants != null && i < variants.Length; i++)
            {
                if (variants[i] != null && ReferenceEquals(variants[i].ItemsOwner, preset))
                    return variants[i];
            }
            return null;
        }

        // the runtime whose live play the handle names — handles are the scheduler's slots, so at most one
        PlayerRuntime RuntimeOf(PlaybackHandle handle)
        {
            if (handle == default || m_Runtime == null)
                return null;
            if (m_Runtime.Owns(handle))
                return m_Runtime;
            PlayerRuntime[] variants = m_VariantRuntimes;
            for (int i = 0; variants != null && i < variants.Length; i++)
            {
                if (variants[i] != null && variants[i].Owns(handle))
                    return variants[i];
            }
            return null;
        }

        // a play of any list — the paused ones count
        internal bool AnyActive()
        {
            if (m_Runtime == null)
                return false;
            if (m_Runtime.IsPlaying)
                return true;
            PlayerRuntime[] variants = m_VariantRuntimes;
            for (int i = 0; variants != null && i < variants.Length; i++)
            {
                if (variants[i] != null && variants[i].IsPlaying)
                    return true;
            }
            return false;
        }

        // the list whose play is the newest — the one a turn reaches
        PlayerRuntime NewestPlaying()
        {
            PlayerRuntime runtime = m_Runtime;
            PlayerRuntime[] variants = m_VariantRuntimes;
            if (variants == null)
                return runtime;
            PlayerRuntime newest = null;
            for (int i = 0; i <= variants.Length; i++)
            {
                PlayerRuntime r = At(runtime, variants, i);
                if (r != null && r.IsPlaying && (newest == null || r.LastStamp > newest.LastStamp))
                    newest = r;
            }
            return newest;
        }

        // Restart: the other lists' plays end as the list's own do (Hold · Stopped) — one their Stopped starts keeps running
        internal void StopOthers(PlayerRuntime except)
        {
            PlayerRuntime runtime = m_Runtime;
            PlayerRuntime[] variants = m_VariantRuntimes;
            if (variants == null)
                return;
            Span<long> last = stackalloc long[1 + variants.Length];
            for (int i = 0; i < last.Length; i++)
                last[i] = At(runtime, variants, i)?.LastSerial ?? 0;
            for (int i = 0; i < last.Length; i++)
            {
                PlayerRuntime r = At(runtime, variants, i);
                if (r != null && r != except && !r.IsReleased)
                    r.StopAll(StopMode.Hold, last[i]);
            }
        }

        // the plays that run now, the latest-played list first: a Revert puts each play's start values back, so the earliest
        // list's go back last. One a Stopped handler starts keeps running
        void StopRuntimes(StopMode mode)
        {
            PlayerRuntime runtime = m_Runtime;
            if (runtime == null)
                return;
            PlayerRuntime[] variants = m_VariantRuntimes;
            if (variants == null)
            {
                runtime.StopAll(mode);
                return;
            }
            Span<long> last = stackalloc long[1 + variants.Length];
            Span<int> order = stackalloc int[1 + variants.Length];
            for (int i = 0; i < last.Length; i++)
                last[i] = At(runtime, variants, i)?.LastSerial ?? 0;
            Order(runtime, variants, order, false);
            for (int i = 0; i < order.Length; i++)
            {
                PlayerRuntime r = At(runtime, variants, order[i]);
                if (r != null && !r.IsReleased)
                    r.StopAll(mode, last[order[i]]);
            }
        }

        // 0 the list's runtime, 1 + k variant k's
        static PlayerRuntime At(PlayerRuntime runtime, PlayerRuntime[] variants, int index) => index == 0 ? runtime : variants[index - 1];

        // the runtimes' indices by a stamp — the first play's or the last's — highest first, a missing runtime last
        static void Order(PlayerRuntime runtime, PlayerRuntime[] variants, Span<int> order, bool first)
        {
            for (int i = 0; i < order.Length; i++)
                order[i] = i;
            for (int i = 1; i < order.Length; i++)
            {
                int index = order[i];
                long key = Stamp(At(runtime, variants, index), first);
                int j = i - 1;
                while (j >= 0 && Stamp(At(runtime, variants, order[j]), first) < key)
                {
                    order[j + 1] = order[j];
                    j--;
                }
                order[j + 1] = index;
            }
        }

        static long Stamp(PlayerRuntime runtime, bool first) => runtime == null ? -1 : first ? runtime.FirstStamp : runtime.LastStamp;

        // ---- the event table: the C# event, the inspector's UnityEvents of that kind, the diagnostics. A stage's exception
        // is logged and the rest goes on

        internal void Raise(PlayerEventKind kind, PlaybackHandle handle, FeedbackContext context)
        {
            // a destroyed player's detached plays raise the diagnostics alone: the UnityEvents' targets go with it, and a
            // C# subscriber destroyed with it would throw and stop the ones after it
            if (!m_Destroyed)
            {
                try
                {
                    RaiseEvent(kind, handle);
                }
                catch (Exception e)
                {
                    context.LogException(e);
                }
                List<PlayerUnityEvent> events = m_UnityEvents;
                if (events != null && events.Count > 0)
                    RaiseUnityEvents(events, kind, context);
            }
            try
            {
                FeedbackDiagnostics.Raise(this, kind, handle);
            }
            catch (Exception e)
            {
                context.LogException(e);
            }
        }

        void RaiseEvent(PlayerEventKind kind, PlaybackHandle handle)
        {
            switch (kind)
            {
                case PlayerEventKind.Played: Played?.Invoke(this, handle); break;
                case PlayerEventKind.Completed: Completed?.Invoke(this, handle); break;
                case PlayerEventKind.Stopped: Stopped?.Invoke(this, handle); break;
                case PlayerEventKind.Looped: Looped?.Invoke(this, handle); break;
                case PlayerEventKind.Paused: Paused?.Invoke(this); break;
                case PlayerEventKind.Resumed: Resumed?.Invoke(this); break;
            }
        }

        // the entries of that kind in list order — an entry's exception is its own. A kind with no entry touches no UnityEvent.
        // A preview counts them instead
        static void RaiseUnityEvents(List<PlayerUnityEvent> events, PlayerEventKind kind, FeedbackContext context)
        {
            for (int i = 0; i < events.Count; i++)
            {
                PlayerUnityEvent entry = events[i];
                if (entry.m_Kind != kind || entry.m_Event == null)
                    continue;
                if (context.Kind == ContextKind.Preview)
                {
                    context.SkipUnityEvent(entry.m_Event, "player " + kind);
                    continue;
                }
                try
                {
                    entry.m_Event.Invoke();
                }
                catch (Exception e)
                {
                    context.LogException(e);
                }
            }
        }
    }
}
