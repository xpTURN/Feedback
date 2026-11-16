using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace xpTURN.Feedback.Editor
{
    // the items of a list that write one slot — the slot's key, a name and the items' list indices
    internal sealed class SlotGroup
    {
        internal LayerKey Key;
        internal string Name;
        internal readonly List<int> Items = new List<int>();
    }

    // a list's values on one slot as the game composes them: the real runtime played in a dry scheduler — its slots keep the
    // values they would write and never write the scene, no event, no admission, the player's own state untouched — and ticked
    // eight times a column on the strip's nominal axis. Drawn: the composed value less the slot's start (a product's factor
    // less one, a rotation's turn from the start unwrapped), with the size of the capped group's slot as one more component
    internal sealed class SlotGraphModel
    {
        internal const int PerColumn = 8;

        internal readonly List<SlotGroup> Groups = new List<SlotGroup>();
        // the graph items no slot takes: a channel's (its receivers come when it plays), one that could not be prepared
        internal readonly List<string> NotDrawn = new List<string>();
        internal int Group = -1;
        internal ItemGraphModel Sum;
        internal Composition Composition;
        internal bool Toggle;
        internal int Dimension;
        internal bool Capped;
        internal float Cap;
        internal double Span;
        internal int Columns;
        internal int TickCount;
        // the composed value less the start at each tick (four floats a tick) — the alone lines' sum is compared with it
        internal float[] Ticks;
        internal GraphInputs Inputs;
        internal bool Seeded;
        internal bool RandomLengths;
        internal string Refused;
        // the shown group takes a Persist item of the list — its form starts from the play's start, not each frame's base
        internal bool PersistGroup;
        // the play the line is of (its copy and start values) — null: the inputs' play
        internal RecordFollow Follow;
        // a followed play's place at each tick — −1 once it ended: the line ends with it
        long[] m_Places;
        // the dry context's settings — the game's unless a test gives its own
        SettingsValues m_Settings;

        internal SlotGroup Shown => Group >= 0 && Group < Groups.Count ? Groups[Group] : null;

        // the graph's scheduler: dry, of the preview's kind (the bar's Unconditional, quiet warnings), the game's settings
        internal static FeedbackScheduler DryScheduler(SettingsValues settings)
        {
            var scheduler = new FeedbackScheduler(ContextKind.Preview);
            scheduler.Context.UseSettings(settings);
            scheduler.Context.Layers.Dry = true;
            return scheduler;
        }

        // the project's settings — in play mode with the live categories
        internal static SettingsValues GameSettings(bool live)
        {
            SettingsValues values = SettingsValues.Load(ContextKind.Runtime);
            if (!live)
                return values;
            for (int i = 1; i < SettingsValues.CategoryCount; i++)
            {
                var category = (FeedbackCategory)i;
                values.Enabled[i] = FeedbackCategories.IsEnabled(category);
                values.Scale[i] = FeedbackCategories.GetScale(category);
            }
            return values;
        }

        // the live slots a dry one starts from: the host's preview session's, else in play mode the default scheduler's
        internal static PropertyLayerRegistry LiveLayers(Component host, bool playing)
        {
            PreviewSession session = PreviewSession.Current;
            if (session != null && session.Subject == host && session.Scheduler != null)
                return session.Scheduler.Context.Layers;
            return playing ? FeedbackScheduler.Default?.Context.Layers : null;
        }

        static PlayArgs Args(in GraphInputs inputs)
        {
            PlayArgs args = PlayArgs.Default.WithSeed(inputs.Seed).WithDirection(PlayerDirection.Forward);
            if (inputs.Intensity != 1f)
                args = args.WithIntensity(inputs.Intensity);
            return inputs.Free ? args.WithUnconditional() : args;
        }

        // one dry play: the list's runtime (all its graph items, or one alone) on its own dry scheduler
        sealed class DryPlay : IDisposable
        {
            internal readonly FeedbackScheduler Scheduler;
            internal readonly PlayerRuntime Runtime;
            readonly List<LayerSlot> m_Slots = new List<LayerSlot>();

            internal DryPlay(FeedbackPlayer player, List<FeedbackItem> items, Object owner, SettingsValues settings, int only)
                : this(player, items, owner, DryScheduler(settings), only)
            {
            }

            internal DryPlay(FeedbackPlayer player, List<FeedbackItem> items, Object owner, FeedbackScheduler scheduler, int only)
            {
                Scheduler = scheduler;
                Runtime = new PlayerRuntime(player, items, owner, Scheduler, null, false, only);
            }

            // the slots of a program item's binding
            internal List<LayerSlot> SlotsOf(int programItem)
            {
                m_Slots.Clear();
                Runtime.Binding(programItem)?.GraphSlots(m_Slots);
                return m_Slots;
            }

            internal LayerSlot Find(in LayerKey key)
            {
                for (int k = 0; k < Runtime.ItemCount; k++)
                {
                    foreach (LayerSlot s in SlotsOf(k))
                    {
                        if (s.Key.Equals(key))
                            return s;
                    }
                }
                return null;
            }

            // every dry slot starts from the live slot of its key, when there is one
            internal bool Seed(PropertyLayerRegistry live)
            {
                bool any = false;
                if (live == null)
                    return false;
                for (int k = 0; k < Runtime.ItemCount; k++)
                {
                    foreach (LayerSlot s in SlotsOf(k))
                    {
                        if (live.TryFind(s.Key, out LayerSlot found) && found != null)
                            any |= s.SeedFrom(found);
                    }
                }
                return any;
            }

            // every dry slot starts where a followed play's holder keeps it — binding by binding, slot by slot
            internal bool SeedFrom(PlayerRuntime holder)
            {
                bool any = false;
                var held = new List<LayerSlot>();
                for (int k = 0; k < Runtime.ItemCount && k < holder.ItemCount; k++)
                {
                    held.Clear();
                    holder.Binding(k)?.GraphSlots(held);
                    List<LayerSlot> slots = SlotsOf(k);
                    for (int j = 0; j < slots.Count && j < held.Count; j++)
                        any |= slots[j].SeedFrom(held[j]);
                }
                return any;
            }

            public void Dispose()
            {
                Runtime.Release(PlaybackEnd.Stopped);
                Scheduler.Shutdown();
            }
        }

        // a slot value's form against a start: a toggle's state, a turn from the start unwrapped toward the last (degrees), a
        // product's factor less one, else the difference — and the size the capped group's limit holds
        internal static Vector4 Form(Composition composition, bool toggle, int dimension, in Vector4 v, in Vector4 start, ref Vector3 last, out float size)
        {
            if (toggle)
            {
                size = 0f;
                return new Vector4(v.x, 0f, 0f, 0f);
            }
            if (composition == Composition.Rotation)
            {
                Quaternion from = Quaternion.Euler(start.x, start.y, start.z);
                Quaternion q = Quaternion.Euler(v.x, v.y, v.z);
                Vector3 e = (Quaternion.Inverse(from) * q).eulerAngles;
                for (int c = 0; c < 3; c++)
                    e[c] -= 360f * Mathf.Round((e[c] - last[c]) / 360f);
                last = e;
                size = Quaternion.Angle(from, q);
                return new Vector4(e.x, e.y, e.z, 0f);
            }
            if (composition == Composition.Multiplicative)
            {
                Vector4 d = default;
                for (int c = 0; c < dimension; c++)
                    d[c] = start[c] != 0f ? v[c] / start[c] - 1f : v[c] - start[c];
                size = 0f;
                return d;
            }
            Vector4 a = v - start;
            size = new Vector3(a.x, a.y, a.z).magnitude;
            return a;
        }

        // an item whose value is written through the door
        static bool Persists(FeedbackItem item)
        {
            if (!(item is TargetItem target) || target.m_Motion == null)
                return false;
            var channels = new List<GraphChannel>();
            FeedbackImplementations.Create(target)?.Describe(channels);
            foreach (GraphChannel ch in channels)
            {
                if (ch.Evaluator.Write == MotionWrite.Persist)
                    return true;
            }
            return false;
        }

        static string NameOf(LayerSlot slot)
        {
            string type = slot.GetType().Name;
            if (type.EndsWith("Slot", StringComparison.Ordinal))
                type = type.Substring(0, type.Length - 4);
            if (type.StartsWith("Local", StringComparison.Ordinal))
                type = type.Substring(5);
            string target = slot.Key.Target != null ? slot.Key.Target.name : "Global";
            return target + " · " + ObjectNames.NicifyVariableName(type);
        }

        static string ItemName(FeedbackItem item) => string.IsNullOrEmpty(item.m_Label) ? ItemWords.Name(item.GetType()) : item.m_Label;

        // a value slot's T as a component count
        static int DimensionOf(LayerSlot slot)
        {
            for (Type t = slot.GetType(); t != null; t = t.BaseType)
            {
                if (!t.IsGenericType || t.GetGenericTypeDefinition() != typeof(ValueSlot<,>))
                    continue;
                Type value = t.GetGenericArguments()[0];
                if (value == typeof(float))
                    return 1;
                if (value == typeof(Vector2))
                    return 2;
                if (value == typeof(Vector3) || value == typeof(Quaternion))
                    return 3;
                return 4;
            }
            return 1;
        }

        static bool Draws(FeedbackItem item)
        {
            if (!(item is TargetItem target) || target.m_Motion == null)
                return false;
            var channels = new List<GraphChannel>();
            FeedbackImplementations.Create(target)?.Describe(channels);
            return channels.Count > 0;
        }

        // the list's slots and the chosen one's values. span · columns: the strip's axis. preferred: the group shown before
        // (its name). live: the slots the dry ones start from. settings: the game's when null. follow: a recorded play — the
        // line is that play's (its copy, its start values, its context's settings)
        internal static SlotGraphModel Build(FeedbackPlayer player, List<FeedbackItem> items, Object owner, in GraphInputs inputs, double span, int columns, string preferred,
            PropertyLayerRegistry live, SettingsValues settings = null, RecordFollow follow = null)
        {
            var model = new SlotGraphModel { Inputs = inputs, Span = span, Columns = Math.Max(1, Math.Min(columns, ItemGraphModel.MaxColumns)) };
            model.m_Settings = settings ?? GameSettings(inputs.Live);
            model.Follow = follow != null && follow.Ready ? follow : null;
            if (player == null || items == null)
            {
                model.Refused = "no player";
                return model;
            }
            using (DryPlay play = model.NewPlay(player, items, owner, -1))
            {
                PlayerRuntime rt = play.Runtime;
                for (int k = 0; k < rt.ItemCount; k++)
                {
                    int listIndex = rt.ListIndex(k);
                    FeedbackItem item = items[listIndex];
                    List<LayerSlot> slots = play.SlotsOf(k);
                    if (slots.Count == 0)
                    {
                        if (item != null && item.m_Active && Draws(item))
                            model.NotDrawn.Add(ItemName(item));
                        continue;
                    }
                    foreach (LayerSlot s in slots)
                    {
                        SlotGroup group = null;
                        foreach (SlotGroup g in model.Groups)
                        {
                            if (g.Key.Equals(s.Key))
                                group = g;
                        }
                        if (group == null)
                        {
                            group = new SlotGroup { Key = s.Key, Name = NameOf(s) };
                            model.Groups.Add(group);
                        }
                        if (!group.Items.Contains(listIndex))
                            group.Items.Add(listIndex);
                    }
                }
                if (model.Groups.Count == 0)
                    return model;
                model.Group = model.Groups.FindIndex(g => g.Name == preferred);
                if (model.Group < 0)
                {
                    model.Group = 0;
                    for (int g = 1; g < model.Groups.Count; g++)
                    {
                        if (model.Groups[g].Items.Count > model.Groups[model.Group].Items.Count)
                            model.Group = g;
                    }
                }
                model.Seeded = model.Follow != null ? play.SeedFrom(model.Follow.Holder) : play.Seed(live);
                foreach (int listIndex in model.Shown.Items)
                    model.PersistGroup |= Persists(items[listIndex]);
                LayerSlot slot = play.Find(model.Shown.Key);
                model.Toggle = slot is ToggleSlot;
                model.Composition = slot.GraphComposition;
                model.Dimension = model.Toggle ? 1 : DimensionOf(slot);
                model.RandomLengths = !inputs.Free && HasRandomLengths(player, items, owner, inputs.Seed);
                model.Ticks = model.Run(play, slot, player, inputs);
            }
            int dimension = model.Dimension + (model.Capped ? 1 : 0);
            model.Sum = model.Fill(model.Ticks, dimension);
            return model;
        }

        // an item's line alone: the same list with only that item's binding — the same axis, the same seed
        internal ItemGraphModel Alone(FeedbackPlayer player, List<FeedbackItem> items, Object owner, int listIndex, PropertyLayerRegistry live, out float[] ticks)
        {
            ticks = null;
            SlotGroup group = Shown;
            if (group == null)
                return null;
            using (DryPlay play = NewPlay(player, items, owner, listIndex))
            {
                if (Follow != null)
                    play.SeedFrom(Follow.Holder);
                else
                    play.Seed(live);
                LayerSlot slot = play.Find(group.Key);
                if (slot == null)
                    return null;
                ticks = Run(play, slot, player, Inputs);
            }
            return Fill(ticks, Dimension + (Capped ? 1 : 0));
        }

        // the turn of a rotation slot's composition from its parts' sum: true when they differ by more than a degree somewhere
        internal bool AxesInteract(List<float[]> alone)
        {
            if (Composition != Composition.Rotation || Ticks == null || alone == null || alone.Count < 2)
                return false;
            for (int n = 0; n < TickCount; n++)
            {
                for (int d = 0; d < 3; d++)
                {
                    float sum = 0f;
                    foreach (float[] a in alone)
                        sum += a[n * 4 + d];
                    if (Math.Abs(Ticks[n * 4 + d] - sum) > 1f)
                        return true;
                }
            }
            return false;
        }

        // a dry play of the list — of the followed play's context when there is one
        DryPlay NewPlay(FeedbackPlayer player, List<FeedbackItem> items, Object owner, int only)
        {
            if (Follow == null)
                return new DryPlay(player, items, owner, m_Settings, only);
            var play = new DryPlay(player, items, owner, Follow.NewScheduler(), only);
            play.Runtime.UseSeedKeys(Follow.Keys, Follow.KeyIds);
            return play;
        }

        static bool HasRandomLengths(FeedbackPlayer player, List<FeedbackItem> items, Object owner, uint seed)
        {
            if (PlayMath.PlayerDurationScale(player, seed, false) != 1f)
                return true;
            for (int i = 0; i < items.Count; i++)
            {
                if (items[i] is EffectItem e && e.m_Random.m_RandomizeDuration
                    && PlayMath.DurationScale(in e.m_Random, PlayMath.ItemSeed(seed, PlayMath.SeedKey(owner, e, i, out bool isId), isId)) != 1f)
                    return true;
            }
            return false;
        }

        // played and ticked over the axis: each tick's composed value less the start
        float[] Run(DryPlay play, LayerSlot slot, FeedbackPlayer player, in GraphInputs inputs)
        {
            Vector4 start = slot.GraphValue;
            float speed = PlayerRuntime.Factor(player.m_Speed);
            int ticks = Columns * PerColumn;
            TickCount = ticks;
            var values = new float[ticks * 4];
            if (!(speed > 0f))
            {
                Refused = "Speed 0";
                return values;
            }
            PlaybackHandle handle = Follow != null ? play.Runtime.Replay(Follow.Copy) : play.Runtime.Play(Args(in inputs), default);
            if (handle == default)
            {
                Refused = "the play was refused";
                return values;
            }
            int record = -1;
            if (Follow != null)
            {
                m_Places = new long[ticks];
                for (int r = 0; r < play.Runtime.RecordCount; r++)
                {
                    if (play.Runtime.Record(r).Handle == handle)
                        record = r;
                }
            }
            float dt = (float)(Span / ticks / speed);
            var last = Vector3.zero;
            bool[] taken = Follow != null && record >= 0 ? new bool[play.Runtime.ItemCount] : null;
            if (taken != null)
                Follow.TakeStarts(play.Runtime, record, taken);
            for (int n = 0; n < ticks; n++)
            {
                play.Scheduler.Tick(dt);
                if (taken != null)
                    Follow.TakeStarts(play.Runtime, record, taken);
                play.Scheduler.RunLateApply();
                if (m_Places != null)
                    m_Places[n] = record >= 0 && play.Runtime.Record(record).Active ? play.Runtime.Position(record) : -1;
                Vector4 v = slot.GraphValue;
                Vector4 d = Form(Composition, Toggle, Dimension, in v, in start, ref last, out float size);
                if (slot.GraphLimit(out float cap) && cap > 0f && Dimension == 3)
                {
                    Capped = true;
                    Cap = cap;
                    d.w = size;
                }
                for (int c = 0; c < 4; c++)
                    values[n * 4 + c] = d[c];
            }
            return values;
        }

        ItemGraphModel Fill(float[] ticks, int dimension)
        {
            ItemGraphModel m = ItemGraphModel.Filled(Columns, Math.Min(4, dimension), 0.0, Span, Toggle);
            if (ticks == null)
                return m;
            int n = Math.Min(TickCount, ticks.Length / 4);
            for (int i = 0; i < n; i++)
            {
                int column = i / PerColumn;
                if (m_Places != null)
                {
                    if (i >= m_Places.Length || m_Places[i] < 0)
                        continue;
                    column = Math.Max(0, Math.Min(Columns - 1, m.ColumnOf(TimelineTime.ToSeconds(m_Places[i]))));
                }
                m.Add(column, new Vector4(ticks[i * 4], ticks[i * 4 + 1], ticks[i * 4 + 2], ticks[i * 4 + 3]));
            }
            return m;
        }
    }
}
