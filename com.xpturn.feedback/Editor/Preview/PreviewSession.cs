using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace xpTURN.Feedback.Editor
{
    internal enum PreviewRefusal
    {
        None,
        Playing,        // in play mode or going into it
        Busy,           // compiling or importing
        Asset,          // a prefab asset's player
        NotInScene,     // not in an open scene or prefab stage — a destroyed player too
        Locked,         // someone else holds AnimationMode — Timeline, the Animation window
        PrepareFailed,
    }

    internal enum PreviewEndReason
    {
        None,
        Stopped,
        Replaced,           // another subject started
        AutoEnd,            // nothing plays
        LockLost,           // the driver is gone
        SubjectDestroyed,   // edit mode destroys without OnDestroy
        PrepareFailed,
        PlayMode,           // play mode is entered
        Reload,             // an assembly reload
        SceneClosing,       // the subject's scene or a recorded object's
        StageClosing,       // the prefab stage of either — it raises no sceneClosing
        UndoRedo,           // the step is done again at the next update
        UserEdit,           // a recorded property edited — it goes back on after the stop
        Deselected,         // the setting: the subject's GameObject left the selection
        Quit,               // the editor quits
        PrefabOperation,    // an instance applied, reverted or unpacked — before it acts
        ActiveSceneChanged, // RenderSettings it wrote are the active scene's
    }

    // one session — AnimationMode has one lock. A Preview scheduler of its own with the recorder on before any slot, the
    // player bound, prepared and played; EditorApplication.update ticks it on the editor clock. The end is the same order
    // every time — the layers revert their slots, the runtime goes, then the stop puts the recorded values back last (a
    // layer's write-back can differ in bits) — once, and never inside a stage. The editor's events end it through the same
    // End, and an edit of a recorded property is copied and goes back on after the stop, with Undo. The global states are
    // captured at the start, the slot types with no record rows named before the play, and the end puts back what
    // AnimationMode does not keep after the stop. The subject is a player or a MotionDriver; the open scenes' camera shake
    // receivers are bound before it and let go at the end. The clock runs at the session's speed and, while a hit stop runs,
    // its scale on the scaled time — no time value is written. Every replay — the same subject again, ▶, a new seed, the
    // item's ▶, the repeat, the tuning — puts the start values back first and plays the arguments it keeps with the
    // session's seed; a scrub pauses and seeks on the microsecond axis
    internal sealed class PreviewSession
    {
        internal static PreviewSession Current { get; private set; }

        // the UI's notice: why the last session ended — kept in SessionState with its subject and whether a refused replay
        // ended it, so it crosses a script reload
        internal static PreviewEndReason LastEnd
        {
            get => (PreviewEndReason)SessionState.GetInt(LastEndKey, 0);
            private set => SessionState.SetInt(LastEndKey, (int)value);
        }

        // observation: the ends that ran — once a session
        internal static int Ends { get; private set; }

        internal const string LastEndKey = "xpTURN.Feedback.Preview.LastEnd";
        internal const string LastEndSubjectKey = "xpTURN.Feedback.Preview.LastEndSubject";
        internal const string LastEndRefusedKey = "xpTURN.Feedback.Preview.LastEndRefused";

        // the last end came after a refused replay (nothing played again)
        internal static bool LastEndRefused => SessionState.GetBool(LastEndRefusedKey, false);

        // the subject of the last end — its entity id, the same across a domain reload
        internal static string LastEndSubject => SessionState.GetString(LastEndSubjectKey, "");

        internal static string IdOf(UnityEngine.Object subject) => subject.GetEntityId().ToString();

        // the setting: end when the subject's GameObject leaves the selection — off by default, the preview goes on. Its UI is
        // the bar
        internal const string EndOnDeselectKey = "xpTURN.Feedback.Preview.EndOnDeselect";

        internal static bool EndOnDeselect
        {
            get => EditorPrefs.GetBool(EndOnDeselectKey, false);
            set => EditorPrefs.SetBool(EndOnDeselectKey, value);
        }

        readonly FeedbackPlayer m_Player;
        readonly MotionDriver m_MotionDriver;
        readonly Component m_Subject;
        readonly string m_SubjectId;
        readonly PreviewReceivers m_Receivers;
        readonly PreviewDriver m_Driver;
        readonly FeedbackScheduler m_Scheduler;
        readonly PreviewClock m_Clock = new PreviewClock();
        readonly PreviewRecorder m_Recorder = new PreviewRecorder();
        readonly PreviewSimulation m_Simulation;

        // the session's simulated targets
        internal PreviewSimulation Simulation => m_Simulation;
        // copies — the engine's modification objects are not held past the callback
        readonly List<PropertyModification> m_Edits = new List<PropertyModification>();
        // the preflight's notice: the slot types with no record rows, as their slots were made
        readonly List<Type> m_Unrecorded = new List<Type>();
        readonly List<FeedbackPlayer> m_Adopted = new List<FeedbackPlayer>();
        PreviewEndReason m_Request;
        bool m_InTick;
        float m_Speed = 1f;
        // the last ▶ — the arguments, the session's seed, the item mode (its managed
        // reference id — its list index while it has none) — and the waits of the repeat and the tuning
        PlayArgs m_Args;
        uint m_Seed;
        PlaybackHandle m_Handle;
        bool m_ItemMode;
        long m_ItemId;
        int m_ItemIndex;
        bool m_IgnoreDelay;
        double m_RepeatAt = -1;
        double m_TuneAt = -1;
        static uint s_Seeds;
        static bool s_Seeded;

        // the Timeline window's preview — a track session: Timeline holds the lock (no driver of ours), its directors and the
        // players their Feedback tracks bind, a sentinel recorded under that lock: gone, the lock changed. No auto end, repeat,
        // tuning or deselect — the window turns it off
        readonly bool m_Track;
        readonly List<Component> m_Directors = new List<Component>();
        readonly List<FeedbackPlayer> m_TrackPlayers = new List<FeedbackPlayer>();
        GameObject m_Sentinel;
        const string SentinelPath = "m_LocalPosition.x";

        // set by the Timeline editor assembly: asks the window to build its graph again — a new lock and a new session
        internal static Action TrackRebuild;

        // a track session ended on our side while Timeline kept its lock: the recorded edits go back on after Timeline's stop
        // and the undo step is done again then — at the next lock's gather, or in an update with no lock
        static readonly List<PropertyModification> s_PendingEdits = new List<PropertyModification>();

        internal static bool TrackRebuildPending { get; private set; }

        internal bool IsTrack => m_Track;
        internal IReadOnlyList<FeedbackPlayer> TrackPlayers => m_TrackPlayers;

        internal const double RepeatGap = 0.2;
        internal const double TuneWait = 0.15;

        // the bar's toggle: an edit of the session's player plays again — on by default
        internal const string LiveTuningKey = "xpTURN.Feedback.Preview.LiveTuning";

        internal static bool LiveTuning
        {
            get => EditorPrefs.GetBool(LiveTuningKey, true);
            set => EditorPrefs.SetBool(LiveTuningKey, value);
        }

        // the last ▶'s arguments — the bar's intensity, direction and unconditional mark; from the next replay
        internal PlayArgs Args
        {
            get => m_Args;
            set => m_Args = value;
        }

        internal uint Seed => m_Seed;
        internal PlaybackHandle Handle => m_Handle;

        // the session's — off at its start (the bar puts it on after the start)
        internal bool Repeat { get; set; }

        // the last replay was refused by the player's conditions (no handle) — the bar's status line
        internal bool LastReplayRefused { get; private set; }

        // null in a MotionDriver's session
        internal FeedbackPlayer Player => m_Player;
        // null in a player's session
        internal MotionDriver MotionDriver => m_MotionDriver;
        internal Component Subject => m_Subject;
        internal PreviewReceivers Receivers => m_Receivers;
        internal PreviewDriver Driver => m_Driver;
        internal FeedbackScheduler Scheduler => m_Scheduler;
        internal int Ticks { get; private set; }

        // the UI's notice (the overlay): their items come back only in part
        internal IReadOnlyList<Type> Unrecorded => m_Unrecorded;

        // the clock's rate (0.1 ~ 2 — the bar's), after the tick's cap, from the next tick. The whole scene clock follows it:
        // the cooldowns, the timeouts, a hit stop's life, a driver
        internal float Speed
        {
            get => m_Speed;
            set => m_Speed = Mathf.Clamp(value, 0.1f, 2f);
        }

        PreviewSession(Component subject, PreviewDriver driver, bool track = false)
        {
            m_Track = track;
            m_Subject = subject;
            m_SubjectId = IdOf(subject);
            m_Player = subject as FeedbackPlayer;
            m_MotionDriver = subject as MotionDriver;
            m_Driver = driver;
            m_Scheduler = new FeedbackScheduler(ContextKind.Preview);
            // before any slot — the instance slot's swap is its first write, at the prepare
            m_Scheduler.Context.Layers.Recorder = m_Recorder;
            m_Receivers = new PreviewReceivers(m_Scheduler);
            m_Simulation = new PreviewSimulation(m_Scheduler.Context.Layers, m_Recorder);
            m_Seed = NextSeed();
            // the display-only items' marks in the Scene view
            PreviewMarksView.Attach(m_Scheduler.Context);
        }

        // a seed a session: seeded once by the clock, the next value each
        static uint NextSeed()
        {
            if (!s_Seeded)
            {
                s_Seeds = (uint)Environment.TickCount;
                s_Seeded = true;
            }
            s_Seeds = s_Seeds * 1664525u + 1013904223u;
            return s_Seeds;
        }

        // None when it plays. The same player plays again in its session; another one ends the session first —
        // InAnimationMode() is true for ours too. A player on an inactive object prepares on the bound path
        internal static PreviewRefusal Start(FeedbackPlayer player, in PlayArgs args)
        {
            // a step waiting to be done again goes first — a new session must not record the values from before it
            PreviewHooks.ReplayPending();
            PreviewSession current = Current;
            if (current != null && current.m_Track)
                return PreviewRefusal.Locked;
            if (current != null && player != null && current.m_Player == player)
            {
                current.m_Args = args;
                current.m_ItemMode = false;
                current.Replay();
                return PreviewRefusal.None;
            }
            current?.End(PreviewEndReason.Replaced);
            PreviewRefusal refusal = RefusalOf(player);
            if (refusal != PreviewRefusal.None)
                return refusal;
            PreviewDriver driver = PreviewDriver.Start();
            if (driver == null)
                return PreviewRefusal.Locked;
            // before anything binds — nothing is written yet
            PreviewGlobalState.CaptureAll();
            var session = new PreviewSession(player, driver);
            Current = session;
            // before the subject: a shake reaches the receivers bound at its Begin
            session.m_Receivers.BindOpenScenes();
            player.Bind(session.m_Scheduler);
            // the players its flow items play, before it prepares (their bindings prepare them)
            session.Adopt(player);
            // false leaves no runtime
            if (!player.EnsureRuntime())
            {
                session.End(PreviewEndReason.PrepareFailed);
                return PreviewRefusal.PrepareFailed;
            }
            session.Preflight();
            session.m_Args = args;
            session.Replay();
            return PreviewRefusal.None;
        }

        // a MotionDriver's session — the same order. Its Bind prepares and starts at once; the same driver again starts over
        internal static PreviewRefusal Start(MotionDriver motionDriver)
        {
            PreviewHooks.ReplayPending();
            PreviewSession current = Current;
            if (current != null && current.m_Track)
                return PreviewRefusal.Locked;
            if (current != null && motionDriver != null && current.m_MotionDriver == motionDriver)
            {
                current.Restart();
                return PreviewRefusal.None;
            }
            current?.End(PreviewEndReason.Replaced);
            PreviewRefusal refusal = RefusalOf(motionDriver);
            if (refusal != PreviewRefusal.None)
                return refusal;
            PreviewDriver driver = PreviewDriver.Start();
            if (driver == null)
                return PreviewRefusal.Locked;
            PreviewGlobalState.CaptureAll();
            var session = new PreviewSession(motionDriver, driver);
            Current = session;
            session.m_Receivers.BindOpenScenes();
            motionDriver.Bind(session.m_Scheduler);
            // not running: no item, or it could not prepare — the driver warns
            if (!motionDriver.IsRunning)
            {
                session.End(PreviewEndReason.PrepareFailed);
                return PreviewRefusal.PrepareFailed;
            }
            session.Preflight();
            return PreviewRefusal.None;
        }

        // Timeline gathers a Feedback track with its preview's lock held. The same lock (the sentinel still recorded) adds the
        // player; another one ends a track session first — Timeline's stop came before: its blocks go back now — and what
        // waited for the stop goes on, then the new session opens. An inspector session holds the lock itself: Timeline did
        // not take it, so it does not gather
        internal static void GatherTrack(Component director, FeedbackPlayer player)
        {
            PreviewSession current = Current;
            if (current != null && !current.m_Track)
                return;
            if (current == null || !current.TrackLockAlive)
            {
                current?.End(PreviewEndReason.LockLost, false);
                SettleTrackRebuild();
                if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating || director == null)
                    return;
                PreviewGlobalState.CaptureAll();
                current = new PreviewSession(director, null, true);
                Current = current;
                current.m_Receivers.BindOpenScenes();
                current.OpenSentinel();
            }
            current.AddTrackPlayer(director, player);
        }

        // after Timeline's stop — the pending edits and the undo step again, before anything is recorded under the next lock
        internal static void SettleTrackRebuild()
        {
            if (!TrackRebuildPending)
                return;
            TrackRebuildPending = false;
            PreviewHooks.ReplayPending();
            Reapply(s_PendingEdits);
            // the undo system reports the edits at the group's close — closed now, they reach no session
            Undo.IncrementCurrentGroup();
        }

        void OpenSentinel()
        {
            m_Sentinel = new GameObject(PreviewRecovery.TemporaryPrefix + "TrackSentinel") { hideFlags = HideFlags.HideAndDontSave };
            PreviewRecorder.Add(m_Sentinel.transform, SentinelPath);
        }

        // Timeline's lock, the one this session opened under
        internal bool TrackLockAlive => m_Sentinel != null && AnimationMode.InAnimationMode() && AnimationMode.IsPropertyAnimated(m_Sentinel.transform, SentinelPath);

        // a scene player joins — bound, its flow items' players adopted, prepared; one that cannot prepare stays out (its
        // clips write nothing)
        void AddTrackPlayer(Component director, FeedbackPlayer player)
        {
            if (!m_Directors.Contains(director))
                m_Directors.Add(director);
            if (player == null || m_TrackPlayers.Contains(player) || EditorUtility.IsPersistent(player) || !player.gameObject.scene.IsValid())
                return;
            player.Bind(m_Scheduler);
            Adopt(player);
            if (!player.EnsureRuntime())
            {
                player.Bind(null);
                return;
            }
            m_TrackPlayers.Add(player);
            Preflight();
        }

        internal bool HoldsTrackPlayer(FeedbackPlayer player) => m_Track && player != null && m_TrackPlayers.Contains(player);

        // a driven play's refusal: a simulated target (particles · animators) plays on the editor's clock — no scrub
        internal SeekRefusal TrackScrub(FeedbackPlayer player, PlaybackHandle handle)
        {
            if (!TrackDrives(player))
                return SeekRefusal.Simulated;
            return player.SeekRange(handle, out _, out _);
        }

        // the same refusal before a play
        internal bool TrackDrives(FeedbackPlayer player) => !(m_Simulation.Any || Simulates(player));

        internal static bool Simulates(FeedbackPlayer player)
        {
            foreach (FeedbackItem item in player.Items)
            {
                if (item != null && Attribute.GetCustomAttribute(item.GetType(), typeof(FeedbackTargetAttribute)) is FeedbackTargetAttribute target && target.Preview == PreviewGrade.Simulated)
                    return true;
            }
            return false;
        }

        // after a clip's seek: the values in that evaluation
        internal void TrackSeeked()
        {
            if (m_Track && !m_InTick)
                TickZero();
        }

        // a clip's graph goes, its play reverted: the layers take our block values off and the blocks are taken again — before
        // Timeline's stop clears them (a lock already lost took them clear: not taken)
        internal void TrackGraphDestroyed()
        {
            if (!m_Track || m_InTick || !TrackLockAlive)
                return;
            TickZero();
            m_Recorder.RetakeBlocks();
        }

        // the players the subject's flow items name — and theirs — bound to the session: the recorder is the context's, so
        // their targets come back with the subject's. Scene objects only (an asset never wakes)
        void Adopt(FeedbackPlayer subject)
        {
            var found = new List<FeedbackPlayer>();
            FlowPlayers.Collect(subject.Items, found);
            for (int i = 0; i < found.Count; i++)
            {
                FeedbackPlayer p = found[i];
                if (p == subject || m_Adopted.Contains(p) || EditorUtility.IsPersistent(p) || !p.gameObject.scene.IsValid())
                    continue;
                p.Bind(m_Scheduler);
                m_Adopted.Add(p);
                FlowPlayers.Collect(p.Items, found);
            }
        }

        bool AdoptedPlaying()
        {
            for (int i = 0; i < m_Adopted.Count; i++)
            {
                if (m_Adopted[i] != null && m_Adopted[i].IsPlaying)
                    return true;
            }
            return false;
        }

        internal IReadOnlyList<FeedbackPlayer> Adopted => m_Adopted;

        // after the prepare, before the play — every slot is made and nothing is written yet. The slot types the source has
        // no rows for are named and warned once (the recorder's gate): their items come back only in part. The start's only —
        // a slot of a later prepare is warned by the fallback
        void Preflight()
        {
            PropertyLayerRegistry layers = m_Scheduler.Context.Layers;
            for (int i = 0; layers.TryGetSlot(i, out LayerSlotInfo info); i++)
            {
                if (PreviewRecordPaths.Knows(info.SlotType) || m_Unrecorded.Contains(info.SlotType))
                    continue;
                m_Unrecorded.Add(info.SlotType);
                m_Recorder.Warn(info.SlotType);
            }
        }

        // the lock last: a previous session of ours has ended by now
        static PreviewRefusal RefusalOf(Component subject)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                return PreviewRefusal.Playing;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
                return PreviewRefusal.Busy;
            if (subject == null)
                return PreviewRefusal.NotInScene;
            if (EditorUtility.IsPersistent(subject))
                return PreviewRefusal.Asset;
            if (!subject.gameObject.scene.IsValid())
                return PreviewRefusal.NotInScene;
            if (AnimationMode.InAnimationMode())
                return PreviewRefusal.Locked;
            return PreviewRefusal.None;
        }

        // every replay — the start, ▶, a new seed, the item's ▶, the repeat. A tuning that waits goes first (its Rebuild)
        void Replay()
        {
            if (m_TuneAt >= 0)
            {
                Tune();
                return;
            }
            PlayNow();
        }

        // an auto end asked for goes · the values come back (a Persist starts from them) · the masks · the item mode's one Solo
        // around the Play · its delay ignored by a seek to it
        void PlayNow()
        {
            if (m_Request == PreviewEndReason.AutoEnd)
                m_Request = PreviewEndReason.None;
            m_RepeatAt = -1;
            m_Player.Restore();
            PreviewMasks.Apply(m_Player);
            int item = ItemIndex();
            m_Player.m_PreviewSoloOnce = item;
            try
            {
                PlayArgs args = m_Args.HasSeed ? m_Args : m_Args.WithSeed(m_Seed);
                m_Handle = m_Player.Play(in args);
            }
            finally
            {
                m_Player.m_PreviewSoloOnce = -1;
            }
            LastReplayRefused = m_Handle == default;
            if (item < 0 || !m_IgnoreDelay || m_Player.SeekRange(m_Handle, out _, out _) != SeekRefusal.None)
                return;
            long place = m_Player.ItemPlace(m_Handle, item);
            if (place > 0)
                m_Player.SeekAt(place);
        }

        // the item mode's list index now — found by its id (an item put before it moves it); gone → the whole list
        int ItemIndex()
        {
            if (!m_ItemMode)
                return -1;
            if (m_ItemId < 0)
                return m_ItemIndex < m_Player.Items.Count ? m_ItemIndex : -1;
            for (int i = 0; i < m_Player.Items.Count; i++)
            {
                if (PreviewMasks.ReferenceId(m_Player, i) == m_ItemId)
                    return i;
            }
            return -1;
        }

        // ▶: a paused play goes on, else the whole list again (the item mode goes). A MotionDriver's session starts over
        internal void PlayButton()
        {
            if (m_Player == null)
            {
                if (m_MotionDriver != null)
                    Restart();
                return;
            }
            if (m_TuneAt < 0 && m_Player.HasPaused)
            {
                m_Player.Resume();
                return;
            }
            m_ItemMode = false;
            Replay();
        }

        // a new seed and the last ▶ again — a paused play again at its place, paused
        internal void NewSeed()
        {
            m_Seed = NextSeed();
            if (m_Player == null)
                return;
            if (m_TuneAt >= 0)
            {
                Tune();
                return;
            }
            KeepPlace(out bool paused, out long at);
            PlayNow();
            BackToPlace(paused, at);
        }

        // the item's ▶: it alone in this play and the replays after it — its delay ignored by a seek to it
        internal void PlayItem(int listIndex, bool ignoreDelay)
        {
            if (m_Player == null || listIndex < 0 || listIndex >= m_Player.Items.Count)
                return;
            new SerializedObject(PresetLinks.Owner(m_Player)).Update();
            m_ItemMode = true;
            m_ItemId = PreviewMasks.ReferenceId(m_Player, listIndex);
            m_ItemIndex = listIndex;
            m_IgnoreDelay = ignoreDelay;
            Replay();
        }

        // the scrub: None when it moved — else why not, and nothing changed
        internal SeekRefusal Scrub(double seconds)
        {
            if (m_Player == null)
                return SeekRefusal.NotLive;
            if (m_Simulation.Any)
                return SeekRefusal.Simulated;
            SeekRefusal refusal = m_Player.SeekRange(m_Handle, out long low, out long high);
            if (refusal != SeekRefusal.None)
                return refusal;
            m_Player.Pause();
            m_Player.SeekAt(Math.Max(low, Math.Min(TimelineTime.FromSeconds(seconds), high)));
            TickZero();
            return SeekRefusal.None;
        }

        // the bar's slider: why it is off, else its range and the play's place — in seconds
        internal SeekRefusal ScrubState(out double low, out double high, out double place)
        {
            low = 0;
            high = 0;
            place = 0;
            if (m_Player == null)
                return SeekRefusal.NotLive;
            if (m_Simulation.Any)
                return SeekRefusal.Simulated;
            SeekRefusal refusal = m_Player.SeekRange(m_Handle, out long l, out long h);
            if (refusal != SeekRefusal.None)
                return refusal;
            low = TimelineTime.ToSeconds(l);
            high = TimelineTime.ToSeconds(h);
            place = TimelineTime.ToSeconds(m_Player.Position(m_Handle));
            return SeekRefusal.None;
        }

        void KeepPlace(out bool paused, out long at)
        {
            paused = m_Player.HasPaused;
            at = paused ? m_Player.Position(m_Handle) : -1;
        }

        void BackToPlace(bool paused, long at)
        {
            if (!paused || at < 0 || m_Player.SeekRange(m_Handle, out long low, out long high) != SeekRefusal.None)
                return;
            m_Player.Pause();
            m_Player.SeekAt(Math.Max(low, Math.Min(at, high)));
            TickZero();
        }

        // a zero tick: the layers write what a seek evaluated — or take a stopped play's writes off (the tuning)
        void TickZero()
        {
            m_InTick = true;
            try
            {
                m_Scheduler.Tick(new FrameTime(0f, 0f));
            }
            finally
            {
                m_InTick = false;
            }
            SceneView.RepaintAll();
            EditorApplication.QueuePlayerLoopUpdate();
        }

        // the values back and a zero tick first — the layers take our blocks off, so a renderer the Rebuild records late is
        // taken clean — then the new runtime and the last ▶; a paused play again at its place
        void Tune()
        {
            m_TuneAt = -1;
            KeepPlace(out bool paused, out long at);
            m_Player.Restore();
            TickZero();
            m_Player.Rebuild();
            PlayNow();
            BackToPlace(paused, at);
        }

        // the same driver again: its Bind starts it over — 0 s, a Loop's new seed
        void Restart()
        {
            if (m_Request == PreviewEndReason.AutoEnd)
                m_Request = PreviewEndReason.None;
            m_MotionDriver.Bind(m_Scheduler);
        }

        // EditorApplication.update: an end asked for inside a tick, the lock, the subject — each update; the tick at most
        // every 1/60 s
        internal void Update()
        {
            if (m_Request != PreviewEndReason.None)
            {
                End(m_Request);
                return;
            }
            if (m_Track ? !TrackLockAlive : !m_Driver.IsAlive)
            {
                End(PreviewEndReason.LockLost, false);
                return;
            }
            // a destroyed subject is Unity null — edit mode runs no OnDestroy
            if (m_Subject == null)
            {
                End(PreviewEndReason.SubjectDestroyed);
                return;
            }
            // the tuning's wait on the editor clock — then in this update
            if (m_TuneAt >= 0 && m_Player != null && PreviewClock.Source() >= m_TuneAt)
                Tune();
            if (!m_Clock.Advance(out float dt))
                return;
            // edit mode runs no OnDisable: a receiver turned off is let go here, when a tick is due
            m_Receivers.Check();
            float unscaled = dt * m_Speed;
            // a hit stop's scale when one made the service: the scaled items stop, the unscaled ones run
            TimeScaleService timeScale = m_Scheduler.Context.TimeScaleIfMade;
            float scaled = timeScale != null ? unscaled * timeScale.Effective : unscaled;
            m_InTick = true;
            try
            {
                // the simulation between the Update stage (the values · the play states) and LateApply —
                // the runtime's order (the particles update in PreLateUpdate)
                m_Scheduler.RunRevert();
                m_Scheduler.RunUpdate(new FrameTime(scaled, unscaled));
                m_Simulation.Step(m_Scheduler.Context, scaled, unscaled);
                m_Scheduler.RunLateApply();
            }
            finally
            {
                m_InTick = false;
            }
            Ticks++;
            // QueuePlayerLoopUpdate runs [ExecuteAlways] scripts as well — they can write over the preview
            SceneView.RepaintAll();
            EditorApplication.QueuePlayerLoopUpdate();
            // a track session ends with Timeline's lock only
            if (m_Track)
                return;
            // ends at the next update — a driver's when it stops running. The repeat waits on the session clock and plays
            // the last ▶ again; a tuning's wait holds the end
            bool ended = m_Player != null ? !m_Player.IsPlaying && !AdoptedPlaying() : m_MotionDriver != null && !m_MotionDriver.IsRunning;
            if (ended && m_Player != null && Repeat)
            {
                double now = m_Scheduler.Context.Now;
                if (m_RepeatAt < 0)
                    m_RepeatAt = now + RepeatGap;
                else if (now >= m_RepeatAt)
                    Replay();
                ended = false;
            }
            // a spawn's lifetime left holds the end — the spawns show as long as they live
            PoolService pools = m_Scheduler.Context.PoolsIfMade;
            if (ended && pools != null && pools.HoldsTimed)
                ended = false;
            // a simulated system that still shows something holds the end
            if (ended && m_Simulation.Alive)
                ended = false;
            // a live mark holds the end — the last instant's arrow shows its second
            PreviewMarks marks = m_Scheduler.Context.Marks;
            if (ended && marks != null && marks.AliveAt(m_Scheduler.Context.Now))
                ended = false;
            if (ended && m_TuneAt < 0)
                Request(PreviewEndReason.AutoEnd);
        }

        internal void Stop() => End(PreviewEndReason.Stopped);

        // EditorSceneManager.sceneOpened (PreviewHooks): the receivers of a scene opened during the session join it
        internal void SceneOpened(Scene scene) => m_Receivers.BindScene(scene);

        // the subject's scene and the scenes of what was recorded — and of the save states
        internal bool Touches(Scene scene) => (m_Subject != null && m_Subject.gameObject.scene == scene) || TouchesTrack(scene) || m_Recorder.Touches(scene) || PreviewSaveState.Touches(scene);

        bool TouchesTrack(Scene scene)
        {
            foreach (Component director in m_Directors)
            {
                if (director != null && director.gameObject.scene == scene)
                    return true;
            }
            foreach (FeedbackPlayer player in m_TrackPlayers)
            {
                if (player != null && player.gameObject.scene == scene)
                    return true;
            }
            return false;
        }

        // Undo.postprocessModifications — read only. An edit of a recorded property is copied and ends the session at the next
        // update: inside the callback the stop would write the recorded value over the edit, already applied
        internal void Intercept(UndoPropertyModification[] modifications)
        {
            for (int i = 0; i < modifications.Length; i++)
            {
                PropertyModification edit = modifications[i].currentValue;
                if (edit == null || edit.target == null)
                    continue;
                if (!AnimationMode.IsPropertyAnimated(edit.target, edit.propertyPath))
                {
                    // a line color the preview keeps (not recordable), edited
                    if (PreviewSlotStates.TakeUserEdit(edit.target, edit.propertyPath))
                    {
                        Request(PreviewEndReason.UserEdit);
                        continue;
                    }
                    // a track player's edit (its list, its settings): the preview opens again with it
                    if (m_Track && edit.target is FeedbackPlayer edited && m_TrackPlayers.Contains(edited))
                    {
                        Request(PreviewEndReason.UserEdit);
                        continue;
                    }
                    // a tuning: an edit of the session's player — 150 ms on the editor clock, each edit again
                    if (m_Player != null && (edit.target == m_Player || (m_Player.m_Preset != null && edit.target == m_Player.m_Preset)) && LiveTuning)
                        m_TuneAt = PreviewClock.Source() + TuneWait;
                    continue;
                }
                m_Edits.Add(new PropertyModification { target = edit.target, propertyPath = edit.propertyPath, value = edit.value, objectReference = edit.objectReference });
                Request(PreviewEndReason.UserEdit);
            }
        }

        // Selection.selectionChanged — the subject's GameObject only: a child or another object alone ends it,
        // the subject chosen with others goes on
        internal void SelectionChanged()
        {
            if (!m_Track && EndOnDeselect && m_Subject != null && !Selection.Contains(m_Subject.gameObject))
                End(PreviewEndReason.Deselected);
        }

        internal void End(PreviewEndReason reason) => End(reason, true);

        // stop: false when the lock is lost — a destroyed driver was reverted by the engine
        void End(PreviewEndReason reason, bool stop)
        {
            // once: the shutdown calls the remaining playbacks' listeners, and a listener can ask again
            if (Current != this)
                return;
            // inside a stage the shutdown has nowhere to compact — asked, it runs at the next update
            if (m_InTick)
            {
                Request(reason);
                return;
            }
            Current = null;
            LastEnd = reason;
            SessionState.SetString(LastEndSubjectKey, m_SubjectId);
            SessionState.SetBool(LastEndRefusedKey, LastReplayRefused);
            Ends++;
            // the simulated systems back to their start
            m_Simulation.End();
            PreviewMarksView.Detach();
            // 1. the playbacks left end (ContextShutdown), the layers revert their slots once (a toggle to its start value, an
            //    instance slot to its originals) and the services close. A Persist stays for the stop
            m_Scheduler.Shutdown();
            // 2. the runtime before the bind — on the C# objects: the subject can be destroyed. Then the receivers
            if (!ReferenceEquals(m_Player, null))
            {
                m_Player.ReleaseRuntime();
                m_Player.Bind(null);
                m_Player.m_PreviewMasks = null;
            }
            if (!ReferenceEquals(m_MotionDriver, null))
                m_MotionDriver.Bind(null);
            foreach (FeedbackPlayer tracked in m_TrackPlayers)
            {
                if (ReferenceEquals(tracked, null))
                    continue;
                tracked.ReleaseRuntime();
                tracked.Bind(null);
            }
            m_TrackPlayers.Clear();
            m_Directors.Clear();
            foreach (FeedbackPlayer adopted in m_Adopted)
            {
                if (ReferenceEquals(adopted, null))
                    continue;
                adopted.ReleaseRuntime();
                adopted.Bind(null);
            }
            m_Adopted.Clear();
            m_Receivers.UnbindAll();
            // 3. the recorded values come back last; the stop calls OnValidate on each component it reverts. It clears the
            //    blocks of a renderer whose property was recorded — taken again right before it, they go back right after it
            if (stop && !m_Track)
            {
                m_Recorder.RetakeBlocks();
                m_Driver.Stop();
            }
            m_Recorder.PutBlocksBack();
            // 4. what AnimationMode does not keep — the serialized states it cannot record, the global states the
            //    other way round, the temporaries — and then the driver
            PreviewSaveState.RevertAll();
            PreviewRenderSettings.Ended();
            PreviewLineColors.Ended();
            PreviewKeptValues.Ended();
            PreviewGlobalState.RestoreAll();
            PreviewTemporaries.DestroyAll();
            m_Driver?.Destroy();
            // 5. the edits of recorded properties go back on, after any end — with Undo: without it the scene is not dirtied
            //    and the edit is lost without a save prompt. A track session that ends while Timeline keeps its lock leaves
            //    them, and an undo step, for after Timeline's stop, and asks the window to build again (not on the way out)
            bool keeps = m_Track && TrackLockAlive;
            if (m_Sentinel != null)
                UnityEngine.Object.DestroyImmediate(m_Sentinel);
            if (keeps)
            {
                s_PendingEdits.AddRange(m_Edits);
                m_Edits.Clear();
                TrackRebuildPending = true;
            }
            else
            {
                Reapply(m_Edits);
                // a track session's next one can open in this call: the undo system reports the edits at the group's close —
                // closed now, they reach no session
                if (m_Track)
                    Undo.IncrementCurrentGroup();
            }
            m_Sentinel = null;
            if (keeps && reason != PreviewEndReason.PlayMode && reason != PreviewEndReason.Reload && reason != PreviewEndReason.Quit && reason != PreviewEndReason.SubjectDestroyed)
                TrackRebuild?.Invoke();
            SceneView.RepaintAll();
        }

        // in the order they came; a target gone is skipped and so is a type with no case here — a composite's edit comes leaf
        // by leaf
        static void Reapply(List<PropertyModification> edits)
        {
            for (int i = 0; i < edits.Count; i++)
            {
                PropertyModification edit = edits[i];
                if (edit.target == null)
                    continue;
                var serialized = new SerializedObject(edit.target);
                SerializedProperty property = serialized.FindProperty(edit.propertyPath);
                if (property != null && Assign(property, edit))
                    serialized.ApplyModifiedProperties();
            }
            edits.Clear();
        }

        static bool Assign(SerializedProperty property, PropertyModification edit)
        {
            switch (property.propertyType)
            {
                case SerializedPropertyType.Float:
                    if (!float.TryParse(edit.value, NumberStyles.Float, CultureInfo.InvariantCulture, out float f))
                        return false;
                    property.floatValue = f;
                    return true;
                case SerializedPropertyType.Integer:
                    if (!long.TryParse(edit.value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long l))
                        return false;
                    property.longValue = l;
                    return true;
                case SerializedPropertyType.Enum:
                    if (!int.TryParse(edit.value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int e))
                        return false;
                    property.intValue = e;
                    return true;
                case SerializedPropertyType.Boolean:
                    property.boolValue = edit.value != "0" && edit.value != "false";
                    return true;
                case SerializedPropertyType.ObjectReference:
                    property.objectReferenceValue = edit.objectReference;
                    return true;
                case SerializedPropertyType.String:
                    property.stringValue = edit.value;
                    return true;
                default:
                    return false;
            }
        }

        // the first wins
        void Request(PreviewEndReason reason)
        {
            if (m_Request == PreviewEndReason.None)
                m_Request = reason;
        }
    }
}
