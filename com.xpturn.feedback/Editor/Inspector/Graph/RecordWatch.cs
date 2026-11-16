using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace xpTURN.Feedback.Editor
{
    // an open graph that takes the record of its player's list
    internal interface IRecordView
    {
        FeedbackPlayer Player { get; }

        // the list's owner — the player, or the preset it shows
        Object Owner { get; }

        // a new play is followed — null: the follow was let go (an edit, a mode change)
        void Began(RecordFollow follow);

        // the followed play's frame was written
        void Framed(RecordFollow follow);
    }

    // one list's followed play: the latest accepted play of its runtime, a copy of its record, a holder of the values its slots
    // started from (a Persist's start, else the base) and a dry shadow played from the copy — moved by the live frames' time,
    // so its slots hold what this list alone would write there
    internal sealed class RecordFollow
    {
        internal readonly FeedbackPlayer Player;
        internal readonly Object Owner;
        internal PlayerRuntime Live;
        internal int Record = -1;
        internal long Serial;
        internal PlaybackRecord Copy;
        // one more at each play followed
        internal int Version;
        // the live play's place at its last frame (nominal microseconds)
        internal long Place;
        internal bool OffNominal;
        internal bool Ended;
        // the shadow could not stay at the live place — no comparison from there
        internal bool Lost;
        internal int Frames;
        internal int Reseeks;
        // the live runtime's item seed keys when the play began — the ids its list had when it was made
        internal uint[] Keys;
        internal bool[] KeyIds;
        // a Persist start the shadow took this frame — its slot wrote the frame before it: not compared
        bool m_Took;
        bool[] m_Taken;
        internal PlayerRuntime Holder;
        internal PlayerRuntime Shadow;
        internal int ShadowRecord = -1;
        FeedbackScheduler m_HolderScheduler;
        FeedbackScheduler m_ShadowScheduler;
        readonly List<LayerSlot> m_Slots = new List<LayerSlot>();
        readonly List<LayerSlot> m_Others = new List<LayerSlot>();

        internal RecordFollow(FeedbackPlayer player, Object owner)
        {
            Player = player;
            Owner = owner;
        }

        // a copy and its start values to draw the line from
        internal bool Ready => Copy != null && Holder != null && Live != null;

        // the frames compared: after the first, on the nominal axis, while the shadow keeps up
        internal bool Comparable => Shadow != null && Frames > 1 && !OffNominal && !Lost && !m_Took;

        // the categories the play's context had when it began
        internal CategoryGate Gate => m_HolderScheduler?.Context.Categories;

        internal static PlayerRuntime RuntimeOf(FeedbackPlayer player, Object owner)
            => player == null ? null : owner is FeedbackPreset preset ? player.RuntimeFor(preset) : player.Runtime;

        // a dry scheduler of the live play's context: its settings and its categories as they are now
        internal FeedbackScheduler NewScheduler()
        {
            FeedbackContext live = Live.Scheduler.Context;
            FeedbackScheduler scheduler = SlotGraphModel.DryScheduler(live.Settings);
            CategoryGate from = live.CategoriesIfMade;
            if (from != null)
            {
                CategoryGate to = scheduler.Context.Categories;
                for (int i = 1; i < SettingsValues.CategoryCount; i++)
                {
                    var category = (FeedbackCategory)i;
                    to.SetEnabled(category, from.IsEnabled(category));
                    to.SetScale(category, from.RawScale(category));
                }
            }
            return scheduler;
        }

        // the list's program item of a list index — −1: not in the program
        internal int ProgramItem(int listIndex)
        {
            if (Live == null)
                return -1;
            for (int k = 0; k < Live.ItemCount; k++)
            {
                if (Live.ListIndex(k) == listIndex)
                    return k;
            }
            return -1;
        }

        // a runtime's slot of a key among its bindings' — a direct write's own slot too
        internal LayerSlot SlotOf(PlayerRuntime runtime, in LayerKey key)
        {
            if (runtime == null)
                return null;
            for (int k = 0; k < runtime.ItemCount; k++)
            {
                m_Slots.Clear();
                runtime.Binding(k)?.GraphSlots(m_Slots);
                foreach (LayerSlot s in m_Slots)
                {
                    if (s.Key.Equals(key))
                        return s;
                }
            }
            return null;
        }

        // a play accepted (at its Played — after t = 0): its copy, the holder of its slots' start values and the shadow
        internal void Begin(PlayerRuntime live, int record)
        {
            Drop();
            PlaybackRecord from = live.Record(record);
            Live = live;
            Record = record;
            Serial = from.Serial;
            Place = live.Position(record);
            OffNominal = false;
            Ended = false;
            Lost = false;
            Frames = 0;
            Reseeks = 0;
            Version++;
            Copy = new PlaybackRecord(live.Program);
            from.Resolved.CopyTo(Copy.Resolved);
            Copy.Seed = from.Seed;
            Copy.Intensity = from.Intensity;
            Copy.Reverse = from.Reverse;
            Copy.Serial = from.Serial;
            Array.Copy(from.Output, Copy.Output, Copy.Output.Length);
            Array.Copy(from.Turned, Copy.Turned, Copy.Turned.Length);
            Keys = new uint[live.ItemCount];
            KeyIds = new bool[live.ItemCount];
            for (int k = 0; k < live.ItemCount; k++)
                Keys[k] = live.SeedKey(k, out KeyIds[k]);
            m_HolderScheduler = NewScheduler();
            Holder = new PlayerRuntime(Player, live.Items, live.ItemsOwner, m_HolderScheduler, null, false);
            Holder.UseSeedKeys(Keys, KeyIds);
            // each slot at the live base, then a Persist binding's start over its slot
            Pair(live, Holder);
            for (int k = 0; k < live.ItemCount && k < Holder.ItemCount; k++)
            {
                ItemBinding held = Holder.Binding(k);
                if (held != null)
                    live.Binding(k)?.SeedStart(record, held);
            }
            m_ShadowScheduler = NewScheduler();
            Shadow = new PlayerRuntime(Player, live.Items, live.ItemsOwner, m_ShadowScheduler, null, false);
            Shadow.UseSeedKeys(Keys, KeyIds);
            Pair(Holder, Shadow);
            PlaybackHandle handle = Shadow.Replay(Copy);
            ShadowRecord = -1;
            for (int r = 0; r < Shadow.RecordCount; r++)
            {
                if (handle != default && Shadow.Record(r).Handle == handle)
                    ShadowRecord = r;
            }
            if (ShadowRecord < 0)
            {
                DropShadow();
                return;
            }
            m_Taken = new bool[Shadow.ItemCount];
            TakeStarts(Shadow, ShadowRecord, m_Taken);
        }

        // a dry play of this list takes the followed play's Persist starts as its items begin them (the shadow's, else the
        // live one's) — true when one was taken now
        internal bool TakeStarts(PlayerRuntime dry, int dryRecord, bool[] taken)
        {
            PlayerRuntime from = Shadow != null && dry != Shadow ? Shadow : Live;
            int fromRecord = from == Shadow ? ShadowRecord : Record;
            if (from == null || fromRecord < 0)
                return false;
            bool any = false;
            for (int k = 0; k < dry.ItemCount && k < from.ItemCount && k < taken.Length; k++)
            {
                if (taken[k])
                    continue;
                ItemBinding binding = dry.Binding(k);
                if (binding != null && binding.TakeStart(dryRecord, from.Binding(k), fromRecord))
                {
                    taken[k] = true;
                    any = true;
                }
            }
            return any;
        }

        // every slot of `to` seeded from `from` — binding by binding, slot by slot
        void Pair(PlayerRuntime from, PlayerRuntime to)
        {
            for (int k = 0; k < from.ItemCount && k < to.ItemCount; k++)
            {
                m_Slots.Clear();
                m_Others.Clear();
                from.Binding(k)?.GraphSlots(m_Slots);
                to.Binding(k)?.GraphSlots(m_Others);
                for (int j = 0; j < m_Slots.Count && j < m_Others.Count; j++)
                    m_Others[j].SeedFrom(m_Slots[j]);
            }
        }

        // after the live scheduler's LateApply: a frame when the live play moved or ended — the shadow moved by the frame's
        // time, sought back to the live place when they part on the nominal axis. False: nothing new
        internal bool Step()
        {
            if (Live == null || Live.IsReleased || Record < 0 || Ended)
                return false;
            PlaybackRecord record = Live.Record(Record);
            if (record.Serial != Serial)
            {
                Ended = true;
                return false;
            }
            bool active = record.Active;
            long place = Live.Position(Record);
            if (active && place == Place)
                return false;
            if (!active)
                Ended = true;
            OffNominal = active && !record.Reverse && record.Cursor.IsOffNominal;
            m_Took = false;
            if (Shadow != null)
            {
                // a signal the live play took this frame ends the shadow's waits before its tick — the shadow hears none
                TimelineCursor cursor = Shadow.Record(ShadowRecord).Cursor;
                if (!record.Reverse && record.Cursor.OpenSignalWaits() < cursor.OpenSignalWaits())
                    cursor.Signal();
                m_ShadowScheduler.Tick(Live.Scheduler.LastTime);
                m_Took = TakeStarts(Shadow, ShadowRecord, m_Taken);
                if (active && !OffNominal && !Lost && Shadow.Position(ShadowRecord) != place)
                {
                    Shadow.SeekAt(place);
                    m_ShadowScheduler.Tick(new FrameTime(0f, 0f));
                    Reseeks++;
                    if (Shadow.Position(ShadowRecord) != place)
                        Lost = true;
                }
            }
            Place = place;
            Frames++;
            return true;
        }

        void DropShadow()
        {
            if (Shadow != null)
                Shadow.Release(PlaybackEnd.Stopped);
            if (m_ShadowScheduler != null && !m_ShadowScheduler.IsShutdown)
                m_ShadowScheduler.Shutdown();
            Shadow = null;
            m_ShadowScheduler = null;
            ShadowRecord = -1;
        }

        // the dry runtimes go — the copy stays for the line until the next play
        internal void Drop()
        {
            DropShadow();
            if (Holder != null)
                Holder.Release(PlaybackEnd.Stopped);
            if (m_HolderScheduler != null && !m_HolderScheduler.IsShutdown)
                m_HolderScheduler.Shutdown();
            Holder = null;
            m_HolderScheduler = null;
        }
    }

    // the record's frames for the open graphs: a follow per watched list, begun at each accepted play (the diagnostics'
    // Played) and stepped after each LateApply of its scheduler. Subscribed while a graph is open
    internal static class RecordWatch
    {
        static readonly List<IRecordView> s_Views = new List<IRecordView>();
        static readonly List<RecordFollow> s_Follows = new List<RecordFollow>();
        static readonly Action<FeedbackPlayer, PlayerEventKind, PlaybackHandle> s_OnPlayer = OnPlayer;
        static readonly Action<FeedbackScheduler> s_OnApplied = OnApplied;
        static bool s_On;

        internal static bool Subscribed => s_On;

        internal static int ViewCount => s_Views.Count;

        internal static void Add(IRecordView view)
        {
            if (view == null || s_Views.Contains(view))
                return;
            s_Views.Add(view);
            if (s_On)
                return;
            s_On = true;
            FeedbackDiagnostics.PlayerEvent += s_OnPlayer;
            FeedbackDiagnostics.LayersApplied += s_OnApplied;
            EditorApplication.playModeStateChanged += OnMode;
        }

        internal static void Remove(IRecordView view)
        {
            if (!s_Views.Remove(view))
                return;
            for (int i = s_Follows.Count - 1; i >= 0; i--)
            {
                if (Watched(s_Follows[i]))
                    continue;
                s_Follows[i].Drop();
                s_Follows.RemoveAt(i);
            }
            if (s_Views.Count > 0 || !s_On)
                return;
            s_On = false;
            FeedbackDiagnostics.PlayerEvent -= s_OnPlayer;
            FeedbackDiagnostics.LayersApplied -= s_OnApplied;
            EditorApplication.playModeStateChanged -= OnMode;
        }

        internal static RecordFollow FollowOf(FeedbackPlayer player, Object owner)
        {
            foreach (RecordFollow f in s_Follows)
            {
                if (f.Player == player && f.Owner == owner)
                    return f;
            }
            return null;
        }

        // an edit of the player or its list: its follows are let go — their copies are of the data before
        internal static void Forget(FeedbackPlayer player)
        {
            for (int i = s_Follows.Count - 1; i >= 0; i--)
            {
                RecordFollow f = s_Follows[i];
                if (f.Player != player)
                    continue;
                f.Drop();
                s_Follows.RemoveAt(i);
                Tell(f, null);
            }
        }

        static bool Watched(RecordFollow f)
        {
            foreach (IRecordView v in s_Views)
            {
                if (v.Player == f.Player && v.Owner == f.Owner)
                    return true;
            }
            return false;
        }

        // the views of a follow's list: Began with the follow (null: let go)
        static void Tell(RecordFollow of, RecordFollow follow)
        {
            for (int i = 0; i < s_Views.Count; i++)
            {
                IRecordView v = s_Views[i];
                if (v.Player == of.Player && v.Owner == of.Owner)
                    v.Began(follow);
            }
        }

        static void OnPlayer(FeedbackPlayer player, PlayerEventKind kind, PlaybackHandle handle)
        {
            if (kind != PlayerEventKind.Played || player == null)
                return;
            for (int i = 0; i < s_Views.Count; i++)
            {
                IRecordView view = s_Views[i];
                if (view.Player != player)
                    continue;
                PlayerRuntime runtime = RecordFollow.RuntimeOf(player, view.Owner);
                if (runtime == null || runtime.IsReleased)
                    continue;
                int record = -1;
                for (int r = 0; r < runtime.RecordCount; r++)
                {
                    if (runtime.Record(r).Active && runtime.Record(r).Handle == handle)
                        record = r;
                }
                if (record < 0)
                    continue;
                RecordFollow follow = FollowOf(player, view.Owner);
                if (follow != null && follow.Live == runtime && follow.Record == record && follow.Serial == runtime.Record(record).Serial)
                    continue;
                // a list edit waits for the Rebuild: the runtime plays the data before it — nothing followed
                if (PlayModeEdits.Pending(player))
                {
                    if (follow != null)
                    {
                        follow.Drop();
                        s_Follows.Remove(follow);
                        Tell(follow, null);
                    }
                    continue;
                }
                if (follow == null)
                {
                    follow = new RecordFollow(player, view.Owner);
                    s_Follows.Add(follow);
                }
                follow.Begin(runtime, record);
                Tell(follow, follow);
            }
        }

        static void OnApplied(FeedbackScheduler scheduler)
        {
            for (int i = 0; i < s_Follows.Count; i++)
            {
                RecordFollow f = s_Follows[i];
                if (f.Live == null || f.Live.Scheduler != scheduler || !f.Step())
                    continue;
                for (int v = 0; v < s_Views.Count; v++)
                {
                    IRecordView view = s_Views[v];
                    if (view.Player == f.Player && view.Owner == f.Owner)
                        view.Framed(f);
                }
            }
        }

        static void OnMode(PlayModeStateChange change)
        {
            for (int i = s_Follows.Count - 1; i >= 0; i--)
            {
                RecordFollow f = s_Follows[i];
                f.Drop();
                s_Follows.RemoveAt(i);
                Tell(f, null);
            }
        }
    }
}
