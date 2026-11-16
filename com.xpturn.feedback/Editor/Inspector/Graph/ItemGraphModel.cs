using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace xpTURN.Feedback.Editor
{
    // what a graph plays with — the play's argument, its seed, whether the play is free (the preview's Unconditional) and
    // whether the categories are the live context's
    internal readonly struct GraphInputs
    {
        internal const uint SampleSeed = 1u;

        internal readonly float Intensity;
        internal readonly uint Seed;
        internal readonly bool Free;
        internal readonly bool Live;
        internal readonly bool FromSession;

        internal GraphInputs(float intensity, uint seed, bool free, bool live, bool fromSession)
        {
            Intensity = intensity;
            Seed = seed;
            Free = free;
            Live = live;
            FromSession = fromSession;
        }

        // in play mode the game's own rules: a Play's default argument, the sample seed, the conditions on, the live
        // categories. Else the host's preview session — its argument, its seed and its Unconditional — or, with none, what
        // the bar's next play takes: the default argument, the sample seed and the bar's Unconditional
        internal static GraphInputs Of(Component host) => Of(host, EditorApplication.isPlayingOrWillChangePlaymode);

        internal static GraphInputs Of(Component host, bool playing)
        {
            if (playing)
                return new GraphInputs(1f, SampleSeed, false, true, false);
            PreviewSession session = PreviewSession.Current;
            if (session != null && host != null && session.Subject == host)
            {
                PlayArgs args = session.Args;
                return new GraphInputs(args.Intensity, args.HasSeed ? args.Seed : session.Seed, args.Unconditional, false, true);
            }
            return new GraphInputs(1f, SampleSeed, EditorPrefs.GetBool(PreviewBar.UnconditionalKey, true), false, false);
        }

        // the scale the game puts on a category's offsets and overrides: the live context's in play mode, the project's
        // settings outside it. off: the category is turned off (scale 0, its items left out of new plays)
        internal static float CategoryScale(FeedbackCategory category, bool live, out bool off)
        {
            int i = (int)category;
            if (i <= 0 || i >= SettingsValues.CategoryCount)
            {
                off = false;
                return 1f;
            }
            if (live)
            {
                off = !FeedbackCategories.IsEnabled(category);
                return off ? 0f : FeedbackCategories.GetScale(category);
            }
            SettingsValues values = SettingsValues.Load(ContextKind.Runtime);
            off = !values.Enabled[i];
            return off ? 0f : values.Scale[i];
        }

        internal bool Same(in GraphInputs other)
            => BitConverter.SingleToInt32Bits(Intensity) == BitConverter.SingleToInt32Bits(other.Intensity) && Seed == other.Seed && Free == other.Free && Live == other.Live
               && FromSession == other.FromSession;
    }

    // one target item's values on its player's nominal axis — what the runtime evaluates: the item's description, the list's
    // program resolved with the seed's random lengths, the item's node sampled at eight places a column, its occurrences
    // begun with their evaluation seeds and the item's intensity, the category's scale on offsets and overrides. Per column
    // and component the least and the most value, the first and the last; the random output's band from its two ends
    internal sealed class ItemGraphModel
    {
        internal const int PerColumn = 8;
        internal const double EndlessSeconds = 2.0;
        internal const int MaxColumns = 512;

        internal readonly List<GraphChannel> Channels = new List<GraphChannel>();
        internal int Channel;
        internal int Columns;
        internal int Dimension;
        // seconds on the player's nominal axis: the first delay's start, the first run's start, the axis' end
        internal double From;
        internal double Start;
        internal double To;
        internal bool Endless;
        // the motion of an Infinite item that stays at its start — null when the motion keeps moving or the item ends
        internal string Stays;
        internal MotionWrite Write;
        // the values are on/off (a toggle) or a weight (an override, a shape that weighs toward the item's fields)
        internal bool Toggle;
        internal bool Weight;
        internal float[] Min;
        internal float[] Max;
        internal float[] First;
        internal float[] Last;
        internal bool[] Has;
        // a Persist's end held after its run
        internal bool[] Held;
        internal bool Band;
        internal float[] BandMin;
        internal float[] BandMax;
        internal bool[] HasBand;
        internal readonly List<double> Starts = new List<double>();
        // why this seed leaves the item out of the play — null when it plays
        internal string Skip;
        internal FeedbackCategory Category;
        internal float Scale = 1f;
        internal bool ScaleOff;
        // a Persist's start from the preview's record; without it a Persist whose start matters is drawn from 0
        internal bool StartKnown;
        internal bool StartMatters;
        internal GraphInputs Inputs;
        // the recorded play the values are of (its copy) — null: the inputs' play
        internal PlaybackRecord Play;

        internal GraphChannel Shown => Channels[Channel];

        static double Seconds(long micros) => TimelineTime.ToSeconds(micros);

        // a model another source fills (the slot graph): its axis, its form, the columns — Add puts a value in a column
        internal static ItemGraphModel Filled(int columns, int dimension, double from, double to, bool toggle)
        {
            var model = new ItemGraphModel { Dimension = dimension, From = from, Start = from, To = to, Toggle = toggle, Write = MotionWrite.Offset };
            model.Allocate(columns);
            return model;
        }

        internal void Add(int column, in Vector4 v) => Put(column, in v, false);

        void Allocate(int columns)
        {
            Columns = Math.Max(1, Math.Min(columns, MaxColumns));
            int n = Columns * 4;
            Min = new float[n];
            Max = new float[n];
            First = new float[n];
            Last = new float[n];
            BandMin = new float[n];
            BandMax = new float[n];
            Has = new bool[Columns];
            Held = new bool[Columns];
            HasBand = new bool[Columns];
        }

        // the item's description, else none: a non-target item, a binding that draws nothing
        static bool Describe(FeedbackItem item, ItemGraphModel model, int channel)
        {
            if (!(item is TargetItem target) || target.m_Motion == null)
                return false;
            ItemBinding binding = FeedbackImplementations.Create(target);
            binding?.Describe(model.Channels);
            if (model.Channels.Count == 0)
                return false;
            model.Channel = Math.Max(0, Math.Min(channel, model.Channels.Count - 1));
            return true;
        }

        // a Tween To · From, an absolute Set or Spring Move To starts from the target's value
        static bool StartMattersFor(FeedbackMotion motion)
        {
            switch (motion)
            {
                case TweenMotion t: return t.m_Params.m_Mode == TweenMode.To || t.m_Params.m_Mode == TweenMode.From;
                case SpringMotion s: return s.m_Params.m_Mode == SpringMode.MoveTo && !s.m_Params.m_Relative;
                default: return false;
            }
        }

        // the list's item at listIndex. owner: the list's owner (the item seeds' ids); player: its host — null for a preset
        // alone (multiplier 1, no start delay, the argument as the intensity). start: a Persist's start from the preview's
        // record of this item. follow: a recorded play — its copy's resolved timeline, final intensity, seed, outputs and turned
        // items, its runtime's seed keys, its context's categories
        internal static ItemGraphModel Build(List<FeedbackItem> items, int listIndex, Object owner, FeedbackPlayer player, in GraphInputs inputs, int channel, int columns,
            Vector4? start = null, RecordFollow follow = null)
        {
            PlaybackRecord play = follow != null && follow.Ready ? follow.Copy : null;
            CategoryGate gate = play != null ? follow.Gate : null;
            var model = new ItemGraphModel { Inputs = inputs, Play = play };
            if (items == null || listIndex < 0 || listIndex >= items.Count || !Describe(items[listIndex], model, channel))
                return model;
            var target = (TargetItem)items[listIndex];
            if (target.m_Timing.m_Infinite && ItemRules.InfiniteUnused(target))
                model.Stays = target.m_Motion.Kind.ToString();
            model.Allocate(columns);
            var descs = new List<TimelineItemDesc>(items.Count);
            var map = new List<int>(items.Count);
            TimelineConversion.DescribeList(items, descs, map);
            int k = map.IndexOf(listIndex);
            if (k < 0)
                return model;
            TimelineProgram program = TimelineProgram.Compile(descs.ToArray());
            int n = descs.Count;
            // a copy of another program's shape (the list changed since) is not this list's play
            if (play != null && (play.Resolved.Program.ItemCount != program.ItemCount || play.Resolved.Program.NodeCount != program.NodeCount
                || play.Resolved.Program.BlockCount != program.BlockCount))
            {
                play = null;
                model.Play = null;
            }
            uint seed = play != null ? play.Seed : inputs.Seed;
            var scale = new float[n];
            uint itemSeed = 0;
            for (int j = 0; j < n; j++)
            {
                scale[j] = 1f;
                FeedbackItem it = items[map[j]];
                uint key;
                bool isId;
                if (play != null && follow.Keys != null && j < follow.Keys.Length)
                {
                    key = follow.Keys[j];
                    isId = follow.KeyIds[j];
                }
                else
                    key = PlayMath.SeedKey(owner, it, map[j], out isId);
                uint s = PlayMath.ItemSeed(seed, key, isId);
                if (j == k)
                    itemSeed = s;
                if (!inputs.Free && it is EffectItem e && e.m_Random.m_RandomizeDuration)
                    scale[j] = PlayMath.DurationScale(in e.m_Random, s);
            }
            float global = player != null ? PlayMath.PlayerDurationScale(player, seed, inputs.Free) : 1f;
            float rootDelay = player != null ? PlayerRuntime.Offset(player.m_StartDelay) : 0f;
            ResolvedTimeline resolved;
            if (play != null)
                resolved = play.Resolved;
            else
            {
                resolved = new ResolvedTimeline(program);
                program.Resolve(scale, default, global, rootDelay, resolved);
            }
            float intensity = play != null ? play.Intensity : player != null ? PlayMath.PlayIntensity(player, inputs.Intensity, 1f) : inputs.Intensity;

            model.Category = target.EffectiveCategory;
            if (gate != null)
            {
                model.ScaleOff = !gate.IsEnabled(model.Category);
                model.Scale = model.ScaleOff ? 0f : gate.RawScale(model.Category);
            }
            else
                model.Scale = GraphInputs.CategoryScale(model.Category, inputs.Live, out model.ScaleOff);
            if (!target.m_Active)
                model.Skip = "off";
            else if (model.ScaleOff)
                model.Skip = "category off";
            else if (play != null)
            {
                if (play.Resolved.ItemSkipped(k))
                    model.Skip = "this play";
            }
            else if (!inputs.Free && PlayMath.ChanceSkips(in target.m_Conditions, itemSeed))
                model.Skip = "Chance";
            else if (PlayMath.ConditionSkips(in target.m_Conditions, false, intensity))
                model.Skip = target.m_Conditions.m_Direction == DirectionCondition.ReverseOnly ? "direction" : "intensity range";

            int node = program.NodeOfItem(k);
            model.Start = Seconds(resolved.NodeStart(node));
            model.From = model.Start - Seconds(resolved.NodeDelay(node));
            long end = resolved.NodeEnd(node);
            for (int scope = program.NodeScope(node); scope > 0;)
            {
                int blockNode = program.BlockNode(scope - 1);
                long blockEnd = resolved.NodeEnd(blockNode);
                if (TimelineTime.IsInfinite(blockEnd) || blockEnd > end)
                    end = blockEnd;
                scope = program.NodeScope(blockNode);
            }
            if (TimelineTime.IsInfinite(end))
            {
                model.Endless = true;
                double finite = 0;
                for (int i = 0; i < program.NodeCount; i++)
                {
                    long e = resolved.NodeEnd(i);
                    if (!TimelineTime.IsInfinite(e))
                        finite = Math.Max(finite, Seconds(e));
                }
                model.To = finite > model.Start + 0.05 ? finite : model.Start + EndlessSeconds;
            }
            else
                model.To = Seconds(end);
            if (model.To - model.From < 0.05)
                model.To = model.From + 0.5;

            GraphChannel ch = model.Shown;
            MotionEvaluator evaluator = ch.Evaluator;
            model.Write = evaluator.Write;
            bool weighs = model.Write == MotionWrite.Offset && target.Traits.ApplyMode == ApplyMode.WeightTowardTarget;
            model.Toggle = ch.Toggle;
            model.Weight = !ch.Toggle && (model.Write == MotionWrite.Override || weighs);
            model.Dimension = model.Toggle || model.Weight ? 1 : ch.Dimension;
            model.StartMatters = model.Write == MotionWrite.Persist && StartMattersFor(target.m_Motion);
            model.StartKnown = start.HasValue;
            if (model.Skip != null)
                return model;

            bool infinite = TimelineTime.IsInfinite(resolved.ItemDuration(k));
            bool turned = play != null ? play.Turned[k] : PlayMath.Turns(target, false, program.ItemOpen(k), infinite);
            float output = play != null ? play.Output[k] : PlayMath.Output(in target.m_Random, itemSeed);
            PlayMath.OutputBounds(in target.m_Random, out float low, out float high);
            float[] intensities =
            {
                PlayMath.ItemIntensity(target.m_Intensity, intensity, output),
                PlayMath.ItemIntensity(target.m_Intensity, intensity, low),
                PlayMath.ItemIntensity(target.m_Intensity, intensity, high),
            };
            model.Band = play == null && target.m_Random.m_RandomizeOutput && !model.Toggle;
            var sampler = new TimelineSampler(program, resolved);
            var sample = new TimelineSample(program);
            double natural = Seconds(resolved.ItemDuration(k));
            model.Sweep(new Probe
            {
                At = place =>
                {
                    sampler.SampleNode(node, TimelineTime.FromSeconds(place), false, sample);
                    TimelineStage stage = sample.Stage(k);
                    long occurrence = sample.Occurrence(k);
                    if (stage == TimelineStage.Running)
                    {
                        var time = turned ? new MotionTime(sample.LocalTimeFromEnd(k), sample.SecondsFromEnd(k)) : new MotionTime(sample.LocalTime(k), sample.Seconds(k));
                        return (true, false, occurrence, Seconds(sample.NodeStart(node)), time);
                    }
                    if (stage == TimelineStage.Done && occurrence >= 0 && model.Write == MotionWrite.Persist)
                        return (true, true, occurrence, Seconds(sample.NodeStart(node)), turned ? new MotionTime(0f, 0.0) : new MotionTime(1f, natural));
                    return (false, false, 0L, 0.0, default);
                },
                Evaluator = evaluator,
                Intensities = intensities,
                Start = start ?? Vector4.zero,
                Endless = infinite,
                Copy = ch.CopySeed,
                Toggle = ch.Toggle,
                Weighs = weighs,
                ItemSeed = itemSeed,
                Scale = model.Scale,
                Write = model.Write,
                Natural = infinite || model.Write == MotionWrite.Persist ? double.NaN : natural,
                Turned = turned,
            });
            return model;
        }

        // a Motion Driver's Loop: no timeline — the seconds since the enable from 0, T 0, one occurrence. Its seed is the live
        // driver's, else the sample seed; its intensity the driver's own with the random output. A Follow draws nothing
        internal static ItemGraphModel BuildDriver(MotionDriver driver, in GraphInputs inputs, int channel, int columns)
        {
            var model = new ItemGraphModel { Inputs = inputs };
            if (driver == null || driver.m_Mode != MotionDriverMode.Loop || !Describe(driver.m_Item, model, channel))
                return model;
            TargetItem target = driver.m_Item;
            model.Allocate(columns);
            model.From = 0.0;
            model.Start = 0.0;
            model.To = EndlessSeconds;
            model.Endless = true;
            model.Category = target.EffectiveCategory;
            model.Scale = GraphInputs.CategoryScale(model.Category, inputs.Live, out model.ScaleOff);
            if (model.ScaleOff)
                model.Skip = "category off";
            GraphChannel ch = model.Shown;
            model.Write = ch.Evaluator.Write;
            bool weighs = model.Write == MotionWrite.Offset && target.Traits.ApplyMode == ApplyMode.WeightTowardTarget;
            model.Toggle = ch.Toggle;
            model.Weight = !ch.Toggle && (model.Write == MotionWrite.Override || weighs);
            model.Dimension = model.Toggle || model.Weight ? 1 : ch.Dimension;
            if (model.Skip != null)
                return model;
            uint seed = driver.IsPrepared ? driver.Seed : inputs.Seed;
            PlayMath.OutputBounds(in target.m_Random, out float low, out float high);
            float own = target.m_Intensity == IntensityResponse.Ignore ? 1f : PlayerRuntime.Factor(driver.m_Intensity);
            float[] intensities = { PlayMath.DriverIntensity(target, driver.m_Intensity, seed), own * low, own * high };
            model.Band = target.m_Random.m_RandomizeOutput && !model.Toggle;
            model.Sweep(new Probe
            {
                At = place => (true, false, 0L, 0.0, new MotionTime(0f, place)),
                Evaluator = ch.Evaluator,
                Intensities = intensities,
                Start = Vector4.zero,
                Endless = true,
                Copy = ch.CopySeed,
                Toggle = ch.Toggle,
                Weighs = weighs,
                ItemSeed = seed,
                Scale = model.Scale,
                Write = model.Write,
            });
            return model;
        }

        // the value at a place on the axis by the columns' own path — null where the item writes nothing
        internal Func<double, Vector4?> ValueAt;

        sealed class Probe
        {
            internal Func<double, (bool on, bool held, long occurrence, double begun, MotionTime time)> At;
            internal MotionEvaluator Evaluator;
            internal readonly MotionState[] States = new MotionState[3];
            internal readonly long[] Current = { long.MinValue, long.MinValue, long.MinValue };
            internal float[] Intensities;
            internal Vector4 Start;
            internal bool Endless;
            internal bool Copy;
            internal bool Toggle;
            internal bool Weighs;
            internal uint ItemSeed;
            internal float Scale;
            internal MotionWrite Write;
            // a run's length in seconds (NaN: no end on the axis) and whether it runs turned — its end's limit closes the curve
            internal double Natural = double.NaN;
            internal bool Turned;

            // where the occurrence changes it begins again — its evaluation seed (a copy's tagged) at the band's intensity
            internal bool Evaluate(int band, long occurrence, in MotionTime time, out Vector4 v)
            {
                if (Current[band] != occurrence)
                {
                    Current[band] = occurrence;
                    uint seed = PlayMath.OccurrenceSeed(ItemSeed, occurrence);
                    if (Copy)
                        seed = Hash32.Tagged(seed, Hash32.Copy, 1u);
                    Evaluator.Begin(ref States[band], Start, new MotionBegin { Seed = seed, Intensity = Intensities[band], Endless = Endless });
                }
                return Value(Evaluator, in States[band], in time, Write, Weighs, Toggle, Scale, out v);
            }
        }

        // eight places a column; for the band the random output's two ends at the same time
        void Sweep(Probe probe)
        {
            int evaluations = Band ? 3 : 1;
            long last = long.MinValue;
            bool running = false;
            long runOccurrence = 0;
            double runBegun = 0.0;
            for (int c = 0; c < Columns; c++)
            {
                for (int q = 0; q < PerColumn; q++)
                {
                    double place = From + (To - From) * (c + q / (double)(PerColumn - 1)) / Columns;
                    (bool on, bool held, long occurrence, double begun, MotionTime time) = probe.At(place);
                    bool runs = on && !held;
                    if (running && (!runs || occurrence != runOccurrence))
                        EndOfRun(probe, runOccurrence, runBegun + probe.Natural, place, evaluations);
                    running = runs;
                    runOccurrence = occurrence;
                    runBegun = begun;
                    if (!on)
                        continue;
                    if (occurrence != last)
                    {
                        last = occurrence;
                        Starts.Add(begun);
                    }
                    for (int b = 0; b < evaluations; b++)
                    {
                        if (!probe.Evaluate(b, occurrence, in time, out Vector4 v))
                            continue;
                        if (b == 0)
                            Put(c, in v, held);
                        else
                            PutBand(c, in v);
                    }
                }
            }
            if (running)
                EndOfRun(probe, runOccurrence, runBegun + probe.Natural, To, evaluations);
            ValueAt = place =>
            {
                (bool on, bool _, long occurrence, double _, MotionTime time) = probe.At(place);
                if (!on || !probe.Evaluate(0, occurrence, in time, out Vector4 v))
                    return null;
                return v;
            };
        }

        // a run that ended between two places: its end's limit (T 1, a turned run's 0) in the end's column — the curve's last
        // stretch, which no place of that column reached
        void EndOfRun(Probe probe, long occurrence, double end, double next, int evaluations)
        {
            if (double.IsNaN(end) || end > next || end < From || end > To)
                return;
            int c = Math.Max(0, Math.Min(Columns - 1, ColumnOf(end)));
            var time = probe.Turned ? new MotionTime(0f, 0.0) : new MotionTime(1f, probe.Natural);
            for (int b = 0; b < evaluations; b++)
            {
                if (!probe.Evaluate(b, occurrence, in time, out Vector4 v))
                    continue;
                if (b == 0)
                    Put(c, in v, false);
                else
                    PutBand(c, in v);
            }
        }

        // what the runtime writes at this time: an offset × the category's scale, an override's weight × the scale, a Persist's
        // value (not scaled), a shape weighed toward the item's fields, a toggle's state while it drives. False: not driven
        static bool Value(MotionEvaluator evaluator, in MotionState s, in MotionTime time, MotionWrite write, bool weighs, bool toggle, float scale, out Vector4 v)
            => ItemBinding.GraphSample(evaluator, in s, in time, write, weighs, toggle, scale, out v);

        void Put(int c, in Vector4 v, bool held)
        {
            int at = c * 4;
            bool first = !Has[c];
            for (int d = 0; d < Dimension; d++)
            {
                float x = v[d];
                if (first)
                {
                    Min[at + d] = x;
                    Max[at + d] = x;
                    First[at + d] = x;
                }
                else
                {
                    if (x < Min[at + d])
                        Min[at + d] = x;
                    if (x > Max[at + d])
                        Max[at + d] = x;
                }
                Last[at + d] = x;
            }
            Has[c] = true;
            Held[c] |= held;
        }

        void PutBand(int c, in Vector4 v)
        {
            int at = c * 4;
            bool first = !HasBand[c];
            for (int d = 0; d < Dimension; d++)
            {
                float x = v[d];
                if (first)
                {
                    BandMin[at + d] = x;
                    BandMax[at + d] = x;
                }
                else
                {
                    if (x < BandMin[at + d])
                        BandMin[at + d] = x;
                    if (x > BandMax[at + d])
                        BandMax[at + d] = x;
                }
            }
            HasBand[c] = true;
        }

        // the least and the most of the drawn components, the band included
        internal void Range(bool[] shown, out float lo, out float hi)
        {
            lo = float.PositiveInfinity;
            hi = float.NegativeInfinity;
            for (int c = 0; c < Columns; c++)
            {
                if (!Has[c])
                    continue;
                for (int d = 0; d < Dimension; d++)
                {
                    if (shown != null && !shown[d])
                        continue;
                    int at = c * 4 + d;
                    lo = Math.Min(lo, Min[at]);
                    hi = Math.Max(hi, Max[at]);
                    if (Band && HasBand[c])
                    {
                        lo = Math.Min(lo, BandMin[at]);
                        hi = Math.Max(hi, BandMax[at]);
                    }
                }
            }
        }

        // the column of a place in seconds
        internal int ColumnOf(double seconds) => (int)Math.Floor((seconds - From) / (To - From) * Columns);
    }
}
