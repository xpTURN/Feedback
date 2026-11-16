using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using xpTURN.Feedback.Editor;
using Object = UnityEngine.Object;

namespace xpTURN.Feedback.Tests.Editor
{
    // the item graph: its values against what the runtime writes frame by frame (offsets, a camera shake's two values, a
    // Persist, a toggle, a list with blocks, turns, a background item, seeded skips and random lengths), the intensity chain
    // and the random output's band, where its inputs come from, the game's category settings, the columns of a fast shape,
    // a host's settings, the Persist forms, the cost, the direction's note, the blocks' axis and the body's Graph
    public sealed class ItemGraphTests
    {
        const float Dt = 1f / 60f;

        readonly List<Object> m_Made = new List<Object>();
        readonly List<FeedbackPlayer> m_Players = new List<FeedbackPlayer>();
        readonly List<FeedbackScheduler> m_Schedulers = new List<FeedbackScheduler>();
        FeedbackScheduler m_Default;
        PreviewBarTestWindow m_Window;
        string m_SettingsMade;
        bool m_UnconditionalSaved;
        bool? m_Unconditional;

        [SetUp]
        public void SetUp()
        {
            PlayerRuntime.ResetSession();
            m_Default = FeedbackScheduler.Default;
        }

        [TearDown]
        public void TearDown()
        {
            FeedbackScheduler.Default = m_Default;
            if (m_Window != null)
                m_Window.Close();
            foreach (FeedbackPlayer p in m_Players)
            {
                if (p != null)
                    p.ReleaseRuntime();
            }
            m_Players.Clear();
            foreach (Object o in m_Made)
            {
                if (o != null)
                    Object.DestroyImmediate(o);
            }
            m_Made.Clear();
            foreach (FeedbackScheduler s in m_Schedulers)
            {
                if (!s.IsShutdown)
                    s.Shutdown();
            }
            m_Schedulers.Clear();
            if (m_SettingsMade != null)
                AssetDatabase.DeleteAsset(m_SettingsMade);
            m_SettingsMade = null;
            if (m_UnconditionalSaved)
            {
                if (m_Unconditional.HasValue)
                    EditorPrefs.SetBool(PreviewBar.UnconditionalKey, m_Unconditional.Value);
                else
                    EditorPrefs.DeleteKey(PreviewBar.UnconditionalKey);
            }
            m_UnconditionalSaved = false;
            m_Unconditional = null;
        }

        static TargetRef Self => new TargetRef { m_Mode = TargetRefMode.Self, m_Path = "", m_Anchor = "" };

        static PositionItem Pos(FeedbackMotion m, float duration, StartRule rule = StartRule.WithPrevious, float delay = 0f, int repeats = 0, float gap = 0f)
        {
            var item = new PositionItem { m_Motion = m, m_Target = Self, m_Space = PositionSpace.Local };
            item.m_Timing.m_Duration = duration;
            item.m_Timing.m_Rule = rule;
            item.m_Timing.m_Delay = delay;
            item.m_Timing.m_RepeatCount = repeats;
            item.m_Timing.m_RepeatGap = gap;
            return item;
        }

        static FeedbackMotion Make(int kind)
        {
            switch (kind % 6)
            {
                case 0: { var m = new PunchMotion(); m.m_Params.m_Amplitude = new Vector4(1f, 0.5f, 0f, 0f); return m; }
                case 1: { var m = new ShakeMotion(); m.m_Params.m_Amplitude = new Vector4(0.3f, 0.3f, 0.1f, 0f); m.m_Params.m_Frequency = 10f; return m; }
                case 2: { var m = new OscillateMotion(); m.m_Params.m_Amplitude = new Vector4(0f, 0.2f, 0f, 0f); m.m_Params.m_Frequency = 3f; return m; }
                case 3: { var m = new BounceMotion(); m.m_Params.m_Amplitude = new Vector4(0f, 1f, 0f, 0f); return m; }
                case 4: { var m = new SpringMotion(); m.m_Params.m_Value = new Vector4(0f, 0.5f, 0f, 0f); return m; }
                default: { var m = new WiggleMotion(); m.m_Params.m_Amplitude = new Vector4(0.2f, 0f, 0.2f, 0f); m.m_Params.m_Frequency = 2f; return m; }
            }
        }

        static GraphInputs Inputs(uint seed, float intensity = 1f, bool free = false, bool live = false) => new GraphInputs(intensity, seed, free, live, false);

        static float ScaleOf(MotionGraphView view) => float.Parse(view.Scale.text.TrimStart('±'), System.Globalization.CultureInfo.InvariantCulture);

        FeedbackScheduler Scheduler()
        {
            var s = new FeedbackScheduler(ContextKind.Test);
            m_Schedulers.Add(s);
            return s;
        }

        GameObject Go(string name)
        {
            var go = new GameObject(name);
            m_Made.Add(go);
            return go;
        }

        FeedbackPlayer Player(GameObject go, FeedbackScheduler s, IEnumerable<FeedbackItem> items)
        {
            var p = go.AddComponent<FeedbackPlayer>();
            m_Players.Add(p);
            p.m_Items.AddRange(items);
            if (s != null)
            {
                p.Bind(s);
                p.EnsureRuntime();
            }
            return p;
        }

        static List<ItemGraphModel> Models(FeedbackPlayer p, in GraphInputs inputs, int channel = 0)
        {
            var models = new List<ItemGraphModel>();
            for (int i = 0; i < p.m_Items.Count; i++)
                models.Add(ItemGraphModel.Build(p.m_Items, i, p, p, in inputs, channel, 64));
            return models;
        }

        static Vector3 Sum(List<ItemGraphModel> models, double t)
        {
            Vector3 sum = default;
            foreach (ItemGraphModel m in models)
            {
                Vector4? v = m.ValueAt?.Invoke(t);
                if (v.HasValue)
                    sum += new Vector3(v.Value.x, v.Value.y, v.Value.z);
            }
            return sum;
        }

        static void Step(FeedbackScheduler s)
        {
            s.Tick(Dt);
            s.RunLateApply();
        }

        // offsets of six motions with delays, a repeat and a gap: their models' sum at n·dt is the frame's position
        [Test]
        public void MG10_TheGraphIsWhatTheRuntimeWrites()
        {
            FeedbackScheduler s = Scheduler();
            GameObject go = Go("offsets");
            FeedbackPlayer p = Player(go, s, new FeedbackItem[]
            {
                Pos(Make(0), 0.4f), Pos(Make(1), 0.5f, delay: 0.1f, repeats: 1, gap: 0.1f), Pos(Make(2), 0.6f, delay: 0.2f),
                Pos(Make(3), 0.5f, delay: 0.05f, repeats: 2, gap: 0.05f), Pos(Make(4), 0.3f, delay: 0.3f), Pos(Make(5), 0.8f),
            });
            const uint seed = 12345u;
            p.Play(PlayArgs.Default.WithSeed(seed).WithIntensity(0.7f));
            List<ItemGraphModel> models = Models(p, Inputs(seed, 0.7f));
            float worst = 0f, peak = 0f;
            for (int n = 1; n <= 80; n++)
            {
                Step(s);
                Vector3 actual = go.transform.localPosition;
                worst = Math.Max(worst, (actual - Sum(models, n * (double)Dt)).magnitude);
                peak = Math.Max(peak, actual.magnitude);
            }
            Assert.That(peak, Is.GreaterThan(0.1f), "the play moved");
            Assert.That(worst, Is.LessThan(1e-5f), "the offsets' sum");

            // a camera shake on its own transform: the position on its own axes, the rotation's copy
            FeedbackScheduler s2 = Scheduler();
            GameObject cam = Go("camera");
            var shake = new CameraShakeItem { m_Motion = Make(1), m_Target = Self, m_RotationAmplitude = new Vector3(4f, 3f, 2f) };
            shake.m_Timing.m_Duration = 0.6f;
            FeedbackPlayer pc = Player(cam, s2, new FeedbackItem[] { shake });
            pc.Play(PlayArgs.Default.WithSeed(seed));
            ItemGraphModel position = ItemGraphModel.Build(pc.m_Items, 0, pc, pc, Inputs(seed), 0, 64);
            ItemGraphModel rotation = ItemGraphModel.Build(pc.m_Items, 0, pc, pc, Inputs(seed), 1, 64);
            Assert.That(position.Channels.Count, Is.EqualTo(2), "two values");
            Assert.That(position.Shown.Space, Is.EqualTo(LayerSpace.Self));
            Assert.That(rotation.Shown.CopySeed, Is.True);
            float worstPosition = 0f, worstAngle = 0f;
            for (int n = 1; n <= 30; n++)
            {
                Step(s2);
                double t = n * (double)Dt;
                Vector4 pv = position.ValueAt(t) ?? default;
                Vector4 rv = rotation.ValueAt(t) ?? default;
                worstPosition = Math.Max(worstPosition, (cam.transform.localPosition - new Vector3(pv.x, pv.y, pv.z)).magnitude);
                worstAngle = Math.Max(worstAngle, Quaternion.Angle(cam.transform.localRotation, Quaternion.Euler(rv.x, rv.y, rv.z)));
            }
            Assert.That(worstPosition, Is.LessThan(1e-5f), "the shake's position");
            Assert.That(worstAngle, Is.LessThan(1e-2f), "the shake's rotation");

            // a Persist from From to To, held after its run
            FeedbackScheduler s3 = Scheduler();
            GameObject moved = Go("tween");
            var tween = new TweenMotion { m_Params = new TweenParams { m_Mode = TweenMode.FromTo, m_From = new Vector4(1f, 2f, 3f, 0f), m_To = new Vector4(-1f, 0f, 2f, 0f), m_Loops = 1 } };
            FeedbackPlayer pt = Player(moved, s3, new FeedbackItem[] { Pos(tween, 0.4f) });
            pt.Play(PlayArgs.Default.WithSeed(seed));
            ItemGraphModel persist = ItemGraphModel.Build(pt.m_Items, 0, pt, pt, Inputs(seed), 0, 64);
            Assert.That(persist.StartMatters, Is.False, "From To needs no start");
            float worstTween = 0f;
            for (int n = 1; n <= 20; n++)
            {
                Step(s3);
                Vector4 v = persist.ValueAt(n * (double)Dt) ?? new Vector4(float.NaN, 0f, 0f, 0f);
                worstTween = Math.Max(worstTween, (moved.transform.localPosition - new Vector3(v.x, v.y, v.z)).magnitude);
            }
            Assert.That(worstTween, Is.LessThan(1e-5f), "the tween");

            // a toggle's blink on a child: on where it drives
            FeedbackScheduler s4 = Scheduler();
            GameObject owner = Go("blinker");
            var child = new GameObject("c");
            child.transform.SetParent(owner.transform, false);
            var blink = new SetActiveItem { m_Motion = new BlinkMotion(), m_Target = new TargetRef { m_Mode = TargetRefMode.Child, m_ChildLookup = ChildLookup.Path, m_Path = "c", m_Anchor = "" } };
            blink.m_Timing.m_Duration = 0.5f;
            FeedbackPlayer pb = Player(owner, s4, new FeedbackItem[] { blink });
            pb.Play(PlayArgs.Default.WithSeed(seed));
            ItemGraphModel toggle = ItemGraphModel.Build(pb.m_Items, 0, pb, pb, Inputs(seed), 0, 64);
            Assert.That(toggle.Toggle, Is.True);
            int differ = 0, driven = 0;
            for (int n = 1; n <= 28; n++)
            {
                Step(s4);
                Vector4? v = toggle.ValueAt(n * (double)Dt);
                if (!v.HasValue)
                    continue;
                driven++;
                if ((v.Value.x != 0f) != child.activeSelf)
                    differ++;
            }
            Assert.That(driven, Is.GreaterThan(0), "the blink drives");
            Assert.That(differ, Is.Zero, "the blink's state");
        }

        // a list of blocks, turns, a background item, a seeded chance and a random length at Speed 2, eight seeds: the models'
        // sum is the frame's position; a skipped item says Chance
        [Test]
        public void MG11_TimelineShapesAndSeeds()
        {
            int skipped = 0, kept = 0;
            foreach (uint seed in new uint[] { 1u, 2u, 3u, 7u, 12345u, 99999u, 424242u, 0xDEADBEEFu })
            {
                var items = new List<FeedbackItem> { Pos(Make(0), 0.3f) };
                PositionItem chancy = Pos(Make(1), 0.4f, StartRule.AfterPrevious);
                chancy.m_Conditions.m_Chance = 50f;
                items.Add(chancy);
                items.Add(Pos(Make(2), 0.3f, StartRule.AfterPrevious));
                items.Add(new LoopStartItem());
                items.Add(Pos(Make(3), 0.2f, StartRule.AfterPrevious));
                PositionItem back = Pos(Make(5), 0.2f, StartRule.AfterPrevious);
                back.m_PlayDirection = ItemPlayDirection.Backward;
                items.Add(back);
                items.Add(new LoopEndItem { m_LoopCount = 3 });
                PositionItem bg = Pos(Make(4), 0.8f, StartRule.WithPrevious, 0.1f);
                bg.m_Timing.m_Background = true;
                items.Add(bg);
                PositionItem opposite = Pos(Make(0), 0.3f, StartRule.AfterPrevious);
                opposite.m_PlayDirection = ItemPlayDirection.Opposite;
                opposite.m_Random.m_RandomizeDuration = true;
                opposite.m_Random.m_DurationRange = new Vector2(0.5f, 2f);
                items.Add(opposite);
                FeedbackScheduler s = Scheduler();
                GameObject go = Go("shapes " + seed);
                FeedbackPlayer p = Player(go, s, items);
                p.m_Speed = 2f;
                p.Play(PlayArgs.Default.WithSeed(seed).WithIntensity(0.8f));
                List<ItemGraphModel> models = Models(p, Inputs(seed, 0.8f));
                if (models[1].Skip == "Chance")
                    skipped++;
                else
                    kept++;
                float worst = 0f;
                double total = TimelineTime.ToSeconds(p.Runtime.Nominal.Total);
                for (int n = 1; n * Dt * 2.0 < total; n++)
                {
                    Step(s);
                    worst = Math.Max(worst, (go.transform.localPosition - Sum(models, n * (double)Dt * 2.0)).magnitude);
                }
                Assert.That(worst, Is.LessThan(1e-5f), "seed " + seed);
            }
            Assert.That(skipped, Is.GreaterThan(0), "a seed skips the chance item");
            Assert.That(kept, Is.GreaterThan(0), "a seed keeps it");
        }

        // the remap, the player's intensity, an Ignore item with a random output — and the band is the model at the output's
        // two ends
        [Test]
        public void MG12_TheIntensityChainAndTheBand()
        {
            FeedbackScheduler s = Scheduler();
            GameObject go = Go("chain");
            PositionItem receive = Pos(Make(0), 0.5f);
            receive.m_Random.m_RandomizeOutput = true;
            receive.m_Random.m_OutputRange = new Vector2(0.4f, 1.2f);
            PositionItem ignore = Pos(Make(2), 0.5f);
            ignore.m_Intensity = IntensityResponse.Ignore;
            ignore.m_Random.m_RandomizeOutput = true;
            ignore.m_Random.m_OutputRange = new Vector2(0.3f, 0.9f);
            FeedbackPlayer p = Player(go, s, new FeedbackItem[] { receive, ignore });
            p.m_RemapIntensity = true;
            p.m_IntensityRemap = AnimationCurve.Linear(0f, 0f, 2f, 3f);
            p.m_Intensity = 0.6f;
            const uint seed = 4242u;
            p.Play(PlayArgs.Default.WithSeed(seed).WithIntensity(1.3f));
            List<ItemGraphModel> models = Models(p, Inputs(seed, 1.3f));
            float worst = 0f;
            for (int n = 1; n <= 30; n++)
            {
                Step(s);
                worst = Math.Max(worst, (go.transform.localPosition - Sum(models, n * (double)Dt)).magnitude);
            }
            Assert.That(worst, Is.LessThan(1e-5f), "the chain");
            foreach (int i in new[] { 0, 1 })
            {
                ItemGraphModel band = ItemGraphModel.Build(p.m_Items, i, p, p, Inputs(seed, 1.3f), 0, 32);
                Assert.That(band.Band, Is.True);
                var item = (PositionItem)p.m_Items[i];
                Vector2 range = item.m_Random.m_OutputRange;
                var ends = new List<ItemGraphModel>();
                foreach (float end in new[] { range.x, range.y })
                {
                    item.m_Random.m_OutputRange = new Vector2(end, end);
                    ends.Add(ItemGraphModel.Build(p.m_Items, i, p, p, Inputs(seed, 1.3f), 0, 32));
                }
                item.m_Random.m_OutputRange = range;
                for (int c = 0; c < band.Columns; c++)
                {
                    if (!band.HasBand[c])
                        continue;
                    for (int d = 0; d < band.Dimension; d++)
                    {
                        int at = c * 4 + d;
                        float lo = Math.Min(ends[0].Min[at], ends[1].Min[at]);
                        float hi = Math.Max(ends[0].Max[at], ends[1].Max[at]);
                        Assert.That(band.BandMin[at], Is.EqualTo(lo).Within(1e-6f), "item " + i + " column " + c);
                        Assert.That(band.BandMax[at], Is.EqualTo(hi).Within(1e-6f), "item " + i + " column " + c);
                    }
                }
            }
        }

        // no session: a Play's default argument, the sample seed and the bar's Unconditional — off shows the chance skip and
        // the random length; in play mode the game's conditions whatever the bar says
        [Test]
        public void MG13_WhereTheInputsComeFrom()
        {
            m_UnconditionalSaved = true;
            m_Unconditional = EditorPrefs.HasKey(PreviewBar.UnconditionalKey) ? EditorPrefs.GetBool(PreviewBar.UnconditionalKey, true) : (bool?)null;
            FeedbackPlayer p = Player(Go("inputs"), null, new FeedbackItem[] { Pos(Make(1), 0.4f) });
            EditorPrefs.SetBool(PreviewBar.UnconditionalKey, true);
            GraphInputs free = GraphInputs.Of(p, false);
            Assert.That((free.Intensity, free.Seed, free.Free, free.Live, free.FromSession), Is.EqualTo((1f, GraphInputs.SampleSeed, true, false, false)));
            EditorPrefs.SetBool(PreviewBar.UnconditionalKey, false);
            GraphInputs conditional = GraphInputs.Of(p, false);
            Assert.That(conditional.Free, Is.False);
            EditorPrefs.SetBool(PreviewBar.UnconditionalKey, true);
            GraphInputs playing = GraphInputs.Of(p, true);
            Assert.That((playing.Intensity, playing.Seed, playing.Free, playing.Live), Is.EqualTo((1f, GraphInputs.SampleSeed, false, true)));

            // a chance item and a random length: left out and longer only when the play is not free
            uint seed = 0;
            PositionItem chancy = Pos(Make(1), 0.4f);
            chancy.m_Conditions.m_Chance = 50f;
            chancy.m_Random.m_RandomizeDuration = true;
            chancy.m_Random.m_DurationRange = new Vector2(1.5f, 2f);
            var list = new List<FeedbackItem> { chancy };
            for (uint candidate = 1; candidate < 64; candidate++)
            {
                if (ItemGraphModel.Build(list, 0, p, p, Inputs(candidate), 0, 16).Skip == "Chance")
                {
                    seed = candidate;
                    break;
                }
            }
            Assert.That(seed, Is.Not.Zero, "a seed that skips");
            Assert.That(ItemGraphModel.Build(list, 0, p, p, Inputs(seed, free: true), 0, 16).Skip, Is.Null, "free: no chance");
            chancy.m_Conditions.m_Chance = 100f;
            double plain = ItemGraphModel.Build(list, 0, p, p, Inputs(seed, free: true), 0, 16).To;
            double random = ItemGraphModel.Build(list, 0, p, p, Inputs(seed), 0, 16).To;
            Assert.That(plain, Is.EqualTo(0.4).Within(1e-6), "free: the authored length");
            Assert.That(random, Is.GreaterThan(0.59), "not free: the random length");
        }

        // the project's settings: a scaled category scales the offsets, a turned-off one leaves its items out, a Persist is
        // not scaled; in play mode the live values
        [Test]
        public void MG14_TheGamesCategorySettings()
        {
            FeedbackPlayer p = Player(Go("settings"), null, new FeedbackItem[] { new CameraShakeItem { m_Motion = Make(1), m_Target = Self } });
            ((TargetItem)p.m_Items[0]).m_Timing.m_Duration = 0.5f;
            var tween = Pos(new TweenMotion { m_Params = new TweenParams { m_Mode = TweenMode.FromTo, m_From = Vector4.zero, m_To = Vector4.one, m_Loops = 1 } }, 0.5f);
            tween.m_Category = new CategoryOverride { m_Override = true, m_Category = FeedbackCategory.Camera };
            p.m_Items.Add(tween);
            p.m_Items.Add(Pos(Make(0), 0.5f));
            List<ItemGraphModel> plain = Models(p, Inputs(9u));
            Assert.That(plain[0].Scale, Is.EqualTo(1f), "no settings asset: ×1");

            string existing = AssetDatabase.FindAssets("t:" + nameof(FeedbackSettings)).Select(AssetDatabase.GUIDToAssetPath).FirstOrDefault(AssetMenus.IsRuntimeSettings);
            Assume.That(existing, Is.Null, "a project with its own runtime settings");
            FeedbackSettings settings = AssetMenus.Settings(out bool created);
            if (created)
                m_SettingsMade = AssetMenus.SettingsPath;
            settings.m_Categories = new[]
            {
                new CategoryDefault { m_Category = FeedbackCategory.Camera, m_Enabled = true, m_Scale = 0.5f },
                new CategoryDefault { m_Category = FeedbackCategory.Motion, m_Enabled = false, m_Scale = 1f },
            };
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();
            List<ItemGraphModel> scaled = Models(p, Inputs(9u));
            Assert.That(scaled[0].Scale, Is.EqualTo(0.5f));
            for (double t = 0.05; t < 0.5; t += 0.05)
            {
                Vector4 a = plain[0].ValueAt(t) ?? default, b = scaled[0].ValueAt(t) ?? default;
                Assert.That(((Vector3)b - (Vector3)a * 0.5f).magnitude, Is.LessThan(1e-6f), "×0.5 at " + t);
                Vector4 ta = plain[1].ValueAt(t) ?? default, tb = scaled[1].ValueAt(t) ?? default;
                Assert.That(tb, Is.EqualTo(ta), "a Persist is not scaled");
            }
            Assert.That(scaled[2].Skip, Is.EqualTo("category off"));
            Assert.That(scaled[2].ValueAt, Is.Null, "nothing drawn");

            // live: the game's code changed the scale
            FeedbackScheduler live = Scheduler();
            FeedbackScheduler.Default = live;
            FeedbackCategories.SetScale(FeedbackCategory.Camera, 0.25f);
            ItemGraphModel shaken = ItemGraphModel.Build(p.m_Items, 0, p, p, Inputs(9u, live: true), 0, 64);
            Assert.That(shaken.Scale, Is.EqualTo(0.25f), "the live scale, not the asset's");
        }

        // a fast shape's columns: eight places a column hold its peaks; one place a column misses them
        [Test]
        public void MG15_TheColumnsOfAFastShape()
        {
            var shake = new ShakeMotion();
            shake.m_Params.m_Amplitude = Vector4.one;
            shake.m_Params.m_Frequency = 60f;
            var square = new OscillateMotion();
            square.m_Params.m_Amplitude = Vector4.one;
            square.m_Params.m_Frequency = 60f;
            square.m_Params.m_Waveform = Waveform.Square;
            var sine = new OscillateMotion();
            sine.m_Params.m_Amplitude = Vector4.one;
            sine.m_Params.m_Frequency = 20f;
            FeedbackPlayer p = Player(Go("fast"), null, new FeedbackItem[] { Pos(shake, 5f), Pos(square, 5f), Pos(sine, 5f) });
            const int columns = 300;
            for (int i = 0; i < 3; i++)
            {
                ItemGraphModel m = ItemGraphModel.Build(p.m_Items, i, p, p, Inputs(777u), 0, columns);
                float lo = float.MaxValue, hi = float.MinValue, worst = 0f, single = 0f;
                var refMin = new float[columns];
                var refMax = new float[columns];
                for (int c = 0; c < columns; c++)
                {
                    refMin[c] = float.MaxValue;
                    refMax[c] = float.MinValue;
                    for (int q = 0; q <= 256; q++)
                    {
                        Vector4? v = m.ValueAt(m.From + (m.To - m.From) * (c + q / 256.0) / columns);
                        if (!v.HasValue)
                            continue;
                        refMin[c] = Math.Min(refMin[c], v.Value.x);
                        refMax[c] = Math.Max(refMax[c], v.Value.x);
                    }
                    if (refMin[c] > refMax[c])
                        continue;
                    lo = Math.Min(lo, refMin[c]);
                    hi = Math.Max(hi, refMax[c]);
                }
                float range = hi - lo;
                for (int c = 0; c < columns; c++)
                {
                    if (refMin[c] > refMax[c] || !m.Has[c])
                        continue;
                    int at = c * 4;
                    worst = Math.Max(worst, Math.Max(refMax[c] - m.Max[at], m.Min[at] - refMin[c]));
                    Vector4? centre = m.ValueAt(m.From + (m.To - m.From) * (c + 0.5) / columns);
                    if (centre.HasValue)
                        single = Math.Max(single, Math.Max(refMax[c] - centre.Value.x, centre.Value.x - refMin[c]));
                }
                Assert.That(worst / range, Is.LessThanOrEqualTo(0.03f), "item " + i + " eight a column");
                Assert.That(single / range, Is.GreaterThan(0.4f), "item " + i + " one a column misses (control)");
            }
        }

        // the host: the player's start delay and length multiplier move the item; a preset alone has neither
        [Test]
        public void MG16_TheHostsTimeline()
        {
            FeedbackPlayer p = Player(Go("host"), null, new FeedbackItem[] { Pos(Make(0), 0.3f), Pos(Make(2), 0.4f, StartRule.AfterPrevious, 0.1f) });
            p.m_StartDelay = 0.5f;
            p.m_DurationMultiplier = 2f;
            ItemGraphModel hosted = ItemGraphModel.Build(p.m_Items, 1, p, p, Inputs(1u), 0, 32);
            ItemGraphModel alone = ItemGraphModel.Build(p.m_Items, 1, p, null, Inputs(1u), 0, 32);
            Assert.That(hosted.Start, Is.EqualTo(0.5 + 2 * (0.3 + 0.1)).Within(1e-6), "the start delay, then ×2");
            Assert.That(alone.Start, Is.EqualTo(0.3 + 0.1).Within(1e-6), "no host: ×1, no start delay");
            Assert.That(hosted.To - hosted.Start, Is.EqualTo(0.8).Within(1e-6));
        }

        // the Persist forms: From To and By need no start; a To from an unknown start is drawn from 0, from a known one from it
        [Test]
        public void MG17_PersistForms()
        {
            var fromTo = new TweenMotion { m_Params = new TweenParams { m_Mode = TweenMode.FromTo, m_From = new Vector4(2f, 0f, 0f, 0f), m_To = new Vector4(4f, 0f, 0f, 0f), m_Loops = 1 } };
            var by = new TweenMotion { m_Params = new TweenParams { m_Mode = TweenMode.By, m_To = new Vector4(2f, 0f, 0f, 0f), m_Loops = 1 } };
            var to = new TweenMotion { m_Params = new TweenParams { m_Mode = TweenMode.To, m_To = new Vector4(3f, 0f, 0f, 0f), m_Loops = 1 } };
            var set = new ShaderGlobalFloatItem { m_Motion = new SetMotion { m_Params = new SetParams { m_Value = new Vector4(1f, 0f, 0f, 0f), m_Relative = true } } };
            FeedbackPlayer p = Player(Go("persist"), null, new FeedbackItem[] { Pos(fromTo, 0.5f), Pos(by, 0.5f), Pos(to, 0.5f), set });
            ItemGraphModel a = ItemGraphModel.Build(p.m_Items, 0, p, p, Inputs(1u), 0, 32);
            ItemGraphModel b = ItemGraphModel.Build(p.m_Items, 1, p, p, Inputs(1u, 0.5f), 0, 32);
            ItemGraphModel c = ItemGraphModel.Build(p.m_Items, 2, p, p, Inputs(1u), 0, 32);
            ItemGraphModel known = ItemGraphModel.Build(p.m_Items, 2, p, p, Inputs(1u), 0, 32, new Vector4(10f, 0f, 0f, 0f));
            ItemGraphModel d = ItemGraphModel.Build(p.m_Items, 3, p, p, Inputs(1u, 0.5f), 0, 32);
            Assert.That(a.StartMatters || b.StartMatters, Is.False);
            Assert.That(a.ValueAt(0.0).Value.x, Is.EqualTo(2f).Within(1e-5f));
            Assert.That(a.ValueAt(0.5).Value.x, Is.EqualTo(4f).Within(1e-5f));
            Assert.That(b.ValueAt(0.5).Value.x, Is.EqualTo(1f).Within(1e-5f), "By: the change × the intensity");
            Assert.That(c.StartMatters && !c.StartKnown, Is.True);
            Assert.That(c.ValueAt(0.0).Value.x, Is.EqualTo(0f).Within(1e-5f), "from 0");
            Assert.That(c.ValueAt(0.5).Value.x, Is.EqualTo(3f).Within(1e-5f));
            Assert.That(known.StartKnown, Is.True);
            Assert.That(known.ValueAt(0.0).Value.x, Is.EqualTo(10f).Within(1e-5f), "from the record's start");
            Assert.That(known.ValueAt(0.5).Value.x, Is.EqualTo(3f).Within(1e-5f));
            Assert.That(d.ValueAt(0.1).Value.x, Is.EqualTo(0.5f).Within(1e-5f), "a relative Set: the value × the intensity, held");
        }

        // a hundred items: one item's graph — its node sampled alone, eight places a column, the band — in 10 ms or less
        [Test]
        public void MG18_TheCost()
        {
            var items = new List<FeedbackItem>();
            for (int i = 0; i < 100; i++)
            {
                PositionItem it = Pos(Make(i), 0.2f + 0.01f * (i % 9), i % 4 == 0 ? StartRule.AfterPrevious : StartRule.WithPrevious, 0.02f * (i % 5), i % 3, 0.05f);
                it.m_Random.m_RandomizeOutput = i % 2 == 0;
                it.m_Random.m_RandomizeDuration = i % 5 == 0;
                it.m_Conditions.m_Chance = i % 7 == 0 ? 50f : 100f;
                items.Add(it);
            }
            FeedbackPlayer p = Player(Go("hundred"), null, items);
            var times = new List<double>();
            var sw = new Stopwatch();
            for (int r = 0; r < 7; r++)
            {
                sw.Restart();
                ItemGraphModel m = ItemGraphModel.Build(p.m_Items, 50, p, p, Inputs(1u), 0, 400);
                sw.Stop();
                Assert.That(m.Band, Is.True);
                if (r >= 2)
                    times.Add(sw.Elapsed.TotalMilliseconds);
            }
            times.Sort();
            double median = times[times.Count / 2];
            TestContext.WriteLine($"median {median:0.###} ms");
            Assert.That(median, Is.LessThanOrEqualTo(10.0), "one item's graph in a hundred-item list");
        }

        // a backward player's graph is the forward list's, with a note
        [UnityTest]
        public IEnumerator MG19_ABackwardPlayersNote()
        {
            FeedbackPlayer forward = Player(Go("forward"), null, new FeedbackItem[] { Pos(Make(1), 0.4f) });
            FeedbackPlayer backward = Player(Go("backward"), null, new FeedbackItem[] { Pos(Make(1), 0.4f) });
            backward.m_Direction = PlayerDirection.Backward;
            ItemGraphModel f = ItemGraphModel.Build(forward.m_Items, 0, forward, forward, Inputs(5u), 0, 64);
            ItemGraphModel b = ItemGraphModel.Build(backward.m_Items, 0, backward, backward, Inputs(5u), 0, 64);
            Assert.That(b.Min, Is.EqualTo(f.Min));
            Assert.That(b.Max, Is.EqualTo(f.Max));
            ItemGraphField field = OpenGraph(backward, 0);
            yield return InspectorTesting.Wait(0.3);
            Assert.That(field.Notes.text, Does.Contain("plays backward"));
        }

        // inside blocks the axis runs to the outermost block's end and marks each run; an endless block shows the first seconds
        [Test]
        public void MG20_TheBlocksAxis()
        {
            FeedbackPlayer p = Player(Go("blocks"), null, new FeedbackItem[]
            {
                new LoopStartItem(), new LoopStartItem(), Pos(Make(0), 0.2f), new LoopEndItem { m_LoopCount = 2 }, new LoopEndItem { m_LoopCount = 3 },
            });
            ItemGraphModel m = ItemGraphModel.Build(p.m_Items, 2, p, p, Inputs(1u), 0, 200);
            Assert.That(m.To, Is.EqualTo(1.2).Within(1e-6), "2 × 3 runs of 0.2 s");
            Assert.That(m.Starts.Count, Is.EqualTo(6), "a start per run");
            ((LoopEndItem)p.m_Items[4]).m_LoopCount = 0;
            ItemGraphModel endless = ItemGraphModel.Build(p.m_Items, 2, p, p, Inputs(1u), 0, 200);
            Assert.That(endless.Endless, Is.True);
        }

        // an endless item's note: a motion that does not keep moving stays at its start · one that keeps moving, and an item
        // endless through Repeat Forever, show the first seconds
        [UnityTest]
        public IEnumerator MG20b_AnEndlessItemsNote()
        {
            PositionItem held = Pos(Make(0), 0.2f);
            held.m_Timing.m_Infinite = true;
            PositionItem keeps = Pos(Make(2), 0.2f);
            keeps.m_Timing.m_Infinite = true;
            PositionItem looped = Pos(Make(0), 0.2f);
            looped.m_Timing.m_RepeatForever = true;
            FeedbackPlayer p = Player(Go("endless"), null, new FeedbackItem[] { held, keeps, looped });
            List<ItemGraphModel> m = Models(p, Inputs(1u));
            Assert.That(m.Select(x => (x.Endless, x.Stays)), Is.EqualTo(new[] { (true, "Punch"), (true, (string)null), (true, (string)null) }));
            ItemGraphField a = OpenGraph(p, 0);
            ItemGraphField b = OpenGraph(p, 1);
            ItemGraphField c = OpenGraph(p, 2);
            yield return InspectorTesting.Wait(0.3);
            Assert.That(a.Notes.text, Does.Contain("Endless — a Punch stays at its start; Repeat Forever loops it"));
            Assert.That(b.Notes.text, Does.Contain("Endless — the first").And.Not.Contain("stays at its start"));
            Assert.That(c.Notes.text, Does.Contain("Endless — the first").And.Not.Contain("stays at its start"));
        }

        ItemGraphField OpenGraph(FeedbackPlayer p, int index)
        {
            if (m_Window == null)
            {
                m_Window = ScriptableObject.CreateInstance<PreviewBarTestWindow>();
                m_Window.position = new Rect(0, 0, 500, 600);
                m_Window.Show();
            }
            var so = new SerializedObject(p);
            var body = new ItemBody();
            string key = "xpTURN.Feedback.Tests.Graph." + Guid.NewGuid().ToString("N") + "." + index;
            SessionState.SetBool(key + ItemGraphField.FoldSuffix, true);
            body.Build(so.FindProperty(ItemListEdits.ItemsPath).GetArrayElementAtIndex(index), key, p);
            m_Window.rootVisualElement.Add(body);
            SessionState.EraseBool(key + ItemGraphField.FoldSuffix);
            return body.Graph;
        }

        // a linked preset's list under its player: the list's object and the host are two — an edit of either builds again,
        // the host's start delay moves the item
        [UnityTest]
        public IEnumerator MG22_ALinkedListsGraph()
        {
            m_Window = ScriptableObject.CreateInstance<PreviewBarTestWindow>();
            m_Window.position = new Rect(0, 0, 500, 600);
            m_Window.Show();
            var preset = ScriptableObject.CreateInstance<FeedbackPreset>();
            m_Made.Add(preset);
            preset.m_Items.Add(Pos(Make(0), 0.4f));
            FeedbackPlayer p = Player(Go("linked"), null, new FeedbackItem[0]);
            p.m_Preset = preset;
            var so = new SerializedObject(preset);
            var body = new ItemBody();
            string key = "xpTURN.Feedback.Tests.GraphLinked." + Guid.NewGuid().ToString("N");
            SessionState.SetBool(key + ItemGraphField.FoldSuffix, true);
            body.Build(so.FindProperty(ItemListEdits.ItemsPath).GetArrayElementAtIndex(0), key, p);
            m_Window.rootVisualElement.Add(body);
            yield return InspectorTesting.Wait(0.3);
            ItemGraphField field = body.Graph;
            int built = field.Builds;
            Assert.That(built, Is.GreaterThanOrEqualTo(1), "open at first: built (again for its laid-out width)");
            Assert.That(field.Model.Start, Is.EqualTo(0.0).Within(1e-9));
            var host = new SerializedObject(p);
            host.FindProperty("m_StartDelay").floatValue = 0.25f;
            host.ApplyModifiedProperties();
            yield return InspectorTesting.Wait(0.3);
            Assert.That(field.Builds, Is.EqualTo(built + 1), "the host's edit builds again, once");
            Assert.That(field.Model.Start, Is.EqualTo(0.25).Within(1e-6), "the host's start delay");
            SessionState.EraseBool(key + ItemGraphField.FoldSuffix);
        }

        // the body's Graph: folded at first and idle while folded; open, it builds once a change; the components, the notes,
        // the tooltip and a live play's head; none for an item that draws nothing, a Motion Driver's Loop draws, its Follow not
        [UnityTest]
        public IEnumerator MG21_TheBodysGraph()
        {
            m_Window = ScriptableObject.CreateInstance<PreviewBarTestWindow>();
            m_Window.position = new Rect(0, 0, 500, 600);
            m_Window.Show();
            FeedbackScheduler s = Scheduler();
            FeedbackPlayer p = Player(Go("body"), s, new FeedbackItem[] { Pos(Make(0), 0.6f), new UnityEventItem() });
            var so = new SerializedObject(p);
            var body = new ItemBody();
            string key = "xpTURN.Feedback.Tests.GraphBody." + Guid.NewGuid().ToString("N");
            SessionState.EraseBool(key + ItemGraphField.FoldSuffix);
            body.Build(so.FindProperty(ItemListEdits.ItemsPath).GetArrayElementAtIndex(0), key, p);
            m_Window.rootVisualElement.Add(body);
            ItemGraphField field = body.Graph;
            Assert.That(field, Is.Not.Null, "a Position Punch draws");
            yield return InspectorTesting.Wait(0.3);
            Assert.That(field.Fold.value, Is.False, "folded at first");
            Assert.That(field.Builds, Is.Zero, "idle while folded");
            so.FindProperty(ItemListEdits.ItemsPath).GetArrayElementAtIndex(0).FindPropertyRelative("m_Timing.m_Duration").floatValue = 0.5f;
            so.ApplyModifiedProperties();
            yield return InspectorTesting.Wait(0.3);
            Assert.That(field.Builds, Is.Zero, "an edit while folded builds nothing");
            field.Fold.value = true;
            yield return InspectorTesting.Wait(0.3);
            Assert.That(field.Builds, Is.EqualTo(1), "open: built once");
            Assert.That(SessionState.GetBool(key + ItemGraphField.FoldSuffix, false), Is.True, "the fold's state kept");
            Assert.That(field.Components.Count, Is.EqualTo(3), "x · y · z");
            Assert.That(field.Notes.text, Does.Contain("Sample seed"));
            so.FindProperty(ItemListEdits.ItemsPath).GetArrayElementAtIndex(0).FindPropertyRelative("m_Timing.m_Duration").floatValue = 0.4f;
            so.ApplyModifiedProperties();
            yield return InspectorTesting.Wait(0.3);
            Assert.That(field.Builds, Is.EqualTo(2), "an edit builds again, once");
            Assert.That(field.Model.To, Is.EqualTo(0.4).Within(1e-6));
            float x = field.View.X((field.Model.From + field.Model.To) / 2);
            Assert.That(field.View.TooltipAt(x), Does.Contain(" s · "), "the tooltip's values");
            Assert.That(field.View.Scale.text, Does.StartWith("±"), "an offset's range");
            float one = ScaleOf(field.View);
            Assert.That(one, Is.GreaterThan(0f));
            so.FindProperty(ItemListEdits.ItemsPath).GetArrayElementAtIndex(0).FindPropertyRelative("m_Motion.m_Params.m_Amplitude").vector4Value = new Vector4(2f, 0.5f, 0f, 0f);
            so.ApplyModifiedProperties();
            yield return InspectorTesting.Wait(0.3);
            Assert.That(ScaleOf(field.View), Is.EqualTo(2f * one).Within(0.002f), "twice the amplitude, twice the range");
            field.Components[0].value = false;
            Assert.That(field.View.Shown[0], Is.False, "a component turned off");
            Assert.That(ScaleOf(field.View), Is.EqualTo(0.5f * one).Within(0.002f), "the shown components' range");
            p.Play(PlayArgs.Default.WithSeed(3u));
            s.Tick(0.1f);
            yield return InspectorTesting.Wait(0.3);
            Assert.That(field.View.Heads.Count, Is.EqualTo(1), "the live play's head");
            p.Stop();
            SessionState.EraseBool(key + ItemGraphField.FoldSuffix);

            var other = new ItemBody();
            other.Build(so.FindProperty(ItemListEdits.ItemsPath).GetArrayElementAtIndex(1), key + ".event", p);
            Assert.That(other.Graph, Is.Null, "an event draws nothing");

            GameObject driven = Go("driver");
            var d = driven.AddComponent<MotionDriver>();
            d.m_Item = Pos(Make(2), 0.4f);
            d.m_Mode = MotionDriverMode.Loop;
            var dso = new SerializedObject(d);
            var loop = new ItemBody();
            loop.Build(dso.FindProperty("m_Item"), key + ".driver", d);
            Assert.That(loop.Graph, Is.Not.Null, "a Loop draws");
            loop.Graph.Build();
            float full = ScaleOf(loop.Graph.View);
            d.m_Intensity = 0.5f;
            loop.Graph.Build();
            Assert.That(ScaleOf(loop.Graph.View), Is.EqualTo(0.5f * full).Within(0.002f), "the driver's intensity in the range");
            d.m_Mode = MotionDriverMode.Follow;
            var follow = new ItemBody();
            follow.Build(dso.FindProperty("m_Item"), key + ".follow", d);
            Assert.That(follow.Graph, Is.Null, "a Follow does not");
        }
    }
}
