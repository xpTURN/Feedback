using System;
using UnityEngine;
using UnityEngine.Playables;

namespace xpTURN.Feedback
{
    // one clip's play of its track's player — in edit mode only a player the Timeline window's preview holds (the track
    // session), its zero tick after each seek, else nothing and no warning. The timeline calls a frame's state
    // callbacks before any ProcessFrame: the play starts at the first ProcessFrame after the clip is entered, so a clip left in
    // the same frame has ended its play first, backward too. A triggered play follows its director's state there — a clip
    // entered by a paused director's Evaluate hears no OnBehaviourPlay when the director goes on. A driven play lost to its
    // player (a Restart, a Stop, a Skip) waits for the next entry. A Driven or Auto clip whose list cannot be scrubbed — in the
    // preview, a simulated one too — plays as a Triggered one does, decided before its play: the player's rules, its direction,
    // its Time Scale items and a seed of its own. A play that ended inside its clip takes the clip's Revert at
    // the clip's end: the player goes back as it was before the plays, when it plays nothing else. In the preview a clip whose
    // last play ended inside it starts again from the values before the plays; Play mode goes on from the values as they are
    internal sealed class FeedbackClipBehaviour : PlayableBehaviour
    {
        internal const string NotScrubbedWarning = "A Feedback clip set to Driven plays a list that cannot be scrubbed — it plays as Triggered.";

        internal FeedbackClip m_Clip;
        FeedbackPlayer m_Player;
        PlaybackHandle m_Handle;
        bool m_In;
        bool m_Entering;
        bool m_Driving;
        bool m_Held;
        bool m_Started;
        bool m_Ended;

        public override void OnBehaviourPlay(Playable playable, FrameData info)
        {
            if (m_In || (!Application.isPlaying && TrackPreview.Hook == null))
                return;
            m_In = true;
            m_Entering = true;
        }

        // Paused (weight 0) — the clip is left, either way; Playing — the director paused, or stops and goes away
        public override void OnBehaviourPause(Playable playable, FrameData info)
        {
            if (!m_In)
                return;
            if (info.effectivePlayState == PlayState.Paused)
            {
                Leave(m_Clip != null ? m_Clip.m_End : FeedbackClipEnd.Revert);
                return;
            }
            if (!m_Driving)
                Hold(true);
        }

        public override void OnPlayableDestroy(Playable playable)
        {
            if (m_In)
                Leave(FeedbackClipEnd.Revert);
            if (!Application.isPlaying)
                TrackPreview.Hook?.GraphDestroyed();
        }

        public override void ProcessFrame(Playable playable, FrameData info, object playerData)
        {
            if (!m_In)
                return;
            bool edit = !Application.isPlaying;
            if (m_Entering)
            {
                var player = playerData as FeedbackPlayer;
                // edit mode: a player the preview holds — the clip waits for its session
                if (edit && (TrackPreview.Hook == null || player == null || !TrackPreview.Hook.Owns(player)))
                    return;
                m_Entering = false;
                Start(playable, player);
            }
            if (m_Player == null || (edit && (TrackPreview.Hook == null || !TrackPreview.Hook.Owns(m_Player))))
                return;
            if (m_Driving)
            {
                if (m_Handle != default)
                    Drive(playable);
                return;
            }
            if (m_Handle != default && m_Player.Position(m_Handle) < 0)
            {
                m_Handle = default;
                m_Ended = true;
            }
            if (m_Handle == default)
            {
                if (m_Started && m_Clip.m_Loop && DirectorPlays(playable))
                {
                    FromRestored();
                    PlayTriggered(playable);
                }
                return;
            }
            Hold(!DirectorPlays(playable));
        }

        void Start(Playable playable, FeedbackPlayer player)
        {
            m_Player = player;
            if (player == null || m_Clip == null)
                return;
            FromRestored();
            FeedbackClipTiming timing = m_Clip.m_Timing;
            if (timing == FeedbackClipTiming.Triggered)
            {
                PlayTriggered(playable);
                return;
            }
            if (!player.CanDrive() || (!Application.isPlaying && !TrackPreview.Hook.Drives(player)))
            {
                WarnNotScrubbed(player);
                PlayTriggered(playable);
                return;
            }
            // the timeline's time is the direction: a driven play is forward
            m_Handle = player.Play(PlayArgs.Default.WithIntensity(m_Clip.m_Intensity).WithSeed(m_Clip.m_Seed).WithDirection(PlayerDirection.Forward).WithDriven());
            if (m_Handle == default)
                return;
            SeekRefusal refusal = Application.isPlaying ? player.SeekRange(m_Handle, out _, out _) : TrackPreview.Hook.Scrub(player, m_Handle);
            if (refusal == SeekRefusal.None)
            {
                m_Driving = true;
                Drive(playable);
                return;
            }
            if (refusal == SeekRefusal.NotLive)
            {
                m_Handle = default;
                return;
            }
            // not to come once decided before the play: a driven play refused here goes on the player's clock and keeps a
            // driven play's rules
            WarnNotScrubbed(player);
            m_Started = true;
            m_Held = true;
            Hold(!DirectorPlays(playable));
        }

        void WarnNotScrubbed(FeedbackPlayer player)
        {
#if DEBUG
            if (m_Clip.m_Timing == FeedbackClipTiming.Driven && !m_Clip.m_Warned)
            {
                m_Clip.m_Warned = true;
                Debug.LogWarning(NotScrubbedWarning, player);
            }
#endif
        }

        void PlayTriggered(Playable playable)
        {
            m_Handle = m_Player.Play(PlayArgs.Default.WithIntensity(m_Clip.m_Intensity));
            m_Started = true;
            m_Held = false;
            if (m_Handle != default)
                Hold(!DirectorPlays(playable));
        }

        // the clip's local time — its clip-in and speed in — on the play's range; a looping clip wraps it by the list's length
        void Drive(Playable playable)
        {
            if (m_Player.SeekRange(m_Handle, out long low, out long high) != SeekRefusal.None)
            {
                m_Handle = default;
                return;
            }
            long target = (long)Math.Round(playable.GetTime() * 1e6);
            if (m_Clip.m_Loop)
            {
                long length = high + 1;
                if (length > 0)
                {
                    target %= length;
                    if (target < 0)
                        target += length;
                }
            }
            if (target < low)
                target = low;
            else if (target > high)
                target = high;
            m_Player.SeekAt(m_Handle, target);
            if (!Application.isPlaying)
                TrackPreview.Hook.Seeked();
        }

        void Hold(bool held)
        {
            if (held == m_Held || m_Handle == default || m_Player == null)
                return;
            m_Held = held;
            m_Player.SetHeld(m_Handle, held);
        }

        // the preview: the clip's last play ended inside it and left what it persisted — the clip starts again from the
        // values before the plays, as an Inspector Preview's Play does, when the player plays nothing else
        void FromRestored()
        {
            if (m_Ended && !Application.isPlaying && !m_Player.IsPlaying)
                m_Player.Restore();
            m_Ended = false;
        }

        void Leave(FeedbackClipEnd end)
        {
            if (m_Handle != default && m_Player != null && m_Player.Position(m_Handle) < 0)
            {
                m_Handle = default;
                m_Ended = true;
            }
            if (m_Handle != default && m_Player != null)
                m_Player.Stop(m_Handle, Mode(end));
            // a play that ended inside the clip left what it persisted: Revert puts the player back as it was before the
            // plays, when it plays nothing else
            else if (m_Ended && end == FeedbackClipEnd.Revert && m_Player != null && !m_Player.IsPlaying)
            {
                m_Player.Restore();
                m_Ended = false;
            }
            m_Handle = default;
            m_In = false;
            m_Entering = false;
            m_Driving = false;
            m_Held = false;
            m_Started = false;
        }

        static StopMode Mode(FeedbackClipEnd end)
        {
            switch (end)
            {
                case FeedbackClipEnd.Hold: return StopMode.Hold;
                case FeedbackClipEnd.Complete: return StopMode.Complete;
                default: return StopMode.Revert;
            }
        }

        // a timeline graph's resolver is its director — none reads as playing
        static bool DirectorPlays(Playable playable)
            => !(playable.GetGraph().GetResolver() is PlayableDirector director) || director.state == PlayState.Playing;
    }
}
