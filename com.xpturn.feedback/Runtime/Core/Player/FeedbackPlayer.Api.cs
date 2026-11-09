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

        // a play that way: the newest live play turns around where it is when it runs the other way — the same play goes on,
        // with no policy, cooldown or chance and no Played; else Play with the direction
        internal PlaybackHandle PlayTurning(PlayerDirection direction)
        {
            if (m_Runtime != null && m_Runtime.Turn(direction == PlayerDirection.Backward, out PlaybackHandle turned))
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
            Refusal refusal = Prepare(null);
            if (refusal != Refusal.None)
            {
                Warn(refusal, null);
                return default;
            }
            m_InPlay = true;
            try
            {
                return m_Runtime.Play(in args, token);
            }
            finally
            {
                m_InPlay = false;
            }
        }

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
            return m_Runtime != null && m_Runtime.SetListener(handle, listener);
        }

        /// <summary>Stops the plays that run now with <paramref name="mode"/>; a play a Stopped handler starts keeps running.</summary>
        public void Stop(StopMode mode = StopMode.Hold)
        {
            if (Valid(mode))
                m_Runtime?.StopAll(mode);
        }

        /// <summary>Stops one play with <paramref name="mode"/>; false when the handle is not this player's live play. Stop(default) is the other overload and stops every play.</summary>
        public bool Stop(PlaybackHandle handle, StopMode mode = StopMode.Hold) => Valid(mode) && m_Runtime != null && m_Runtime.Stop(handle, mode);

        /// <summary>Pauses the plays that run now; paused plays still count as playing, so the Policy refuses or stops a later Play as it would a running one.</summary>
        public void Pause() => m_Runtime?.Pause();

        /// <summary>Resumes the paused plays.</summary>
        public void Resume() => m_Runtime?.Resume();

        /// <summary>True while any play runs, paused plays included.</summary>
        public bool IsPlaying => m_Runtime != null && m_Runtime.IsPlaying;

        // the waits Signal() would end now and how many of them have no timeout — a preview tells them
        internal int OpenSignalWaits() => OpenSignalWaits(out _);

        internal int OpenSignalWaits(out int untimed)
        {
            untimed = 0;
            return m_Runtime != null ? m_Runtime.OpenSignalWaits(out untimed) : 0;
        }

        // the preview's scrub and item play
        internal SeekRefusal SeekRange(PlaybackHandle handle, out long low, out long high)
        {
            low = 0;
            high = 0;
            return m_Runtime != null ? m_Runtime.SeekRange(handle, out low, out high) : SeekRefusal.NotLive;
        }

        internal bool SeekAt(long target) => m_Runtime != null && m_Runtime.SeekAt(target);

        // a track's: one play's seek and hold
        internal bool SeekAt(PlaybackHandle handle, long target) => m_Runtime != null && m_Runtime.SeekAt(handle, target);

        internal bool SetHeld(PlaybackHandle handle, bool held) => m_Runtime != null && m_Runtime.SetHeld(handle, held);

        // a track's: a driven play can seek the list — decided before the play; it prepares the player, as a Play does
        internal bool CanDrive() => EnsureRuntime() && m_Runtime.Drivable;

        internal bool HasPaused => m_Runtime != null && m_Runtime.HasPaused;

        internal long Position(PlaybackHandle handle) => m_Runtime != null ? m_Runtime.Position(handle) : -1;

        internal long ItemPlace(PlaybackHandle handle, int listIndex) => m_Runtime != null ? m_Runtime.ItemPlace(handle, listIndex) : -1;

        /// <summary>The list's nominal length — the duration multiplier and start delay in, no random part, infinity for an endless list; it prepares the player and is 0 when it cannot.</summary>
        public float TotalDuration => EnsureRuntime() ? m_Runtime.TotalDuration : 0f;

        /// <summary>That play's own length, with its random parts and without its speed; 0 when the handle is not this player's live play.</summary>
        public float GetDuration(PlaybackHandle handle) => m_Runtime != null ? m_Runtime.GetDuration(handle) : 0f;

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
            return m_Runtime != null && m_Runtime.Seek(time);
        }

        /// <summary>Ends the plays that run now: a seekable play runs its remaining events and completes, another stops with Complete; false when no play ended.</summary>
        public bool Skip() => m_Runtime != null && m_Runtime.SkipToEnd();

        /// <summary>Stops the plays that run now (Hold) and puts each value target back to its value before the first play since the player first prepared or last restored — a Rebuild keeps it, and with Apply Initial State it is the value before the first frame; it does not prepare.</summary>
        public void Restore() => m_Runtime?.Restore();

        // ends the signal waits of the plays that run now — outside a tick at the next tick's start, inside an event at its
        // instant. No wait ended warns once (a wait that has not begun does not take it)
        /// <summary>Ends the signal waits of the plays that run now — at the next tick's start, or at once inside an event; warns once when no wait ended.</summary>
        public void Signal()
        {
            if (m_Destroyed || (m_Runtime != null && m_Runtime.Signal() > 0))
                return;
            m_MissedSignals++;
            Warn(Refusal.Signal, null);
        }

        // a channel's Signal — a broadcast reaches players with no wait, so no warning. The number of waits it ended
        internal int SignalQuietly()
        {
            if (m_Destroyed)
                return 0;
            int ended = m_Runtime != null ? m_Runtime.Signal() : 0;
            if (ended == 0)
                m_MissedQuietSignals++;
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
                default: return;
            }
            ushort bit = (ushort)(1 << (int)refusal);
            if ((m_Warned & bit) != 0)
                return;
            m_Warned |= bit;
            Debug.LogWarning(message, this);
#endif
        }

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
