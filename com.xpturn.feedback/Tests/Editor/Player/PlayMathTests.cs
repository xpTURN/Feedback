using System;
using System.Collections.Generic;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Serialization;
using Object = UnityEngine.Object;

namespace xpTURN.Feedback.Tests.Editor
{
    // the plays' random parts and intensities as the runtime makes them — the seed, the play's intensity, each item's skip,
    // turn, random output, resolved length, item seed, begin intensity and occurrence, a value item's evaluation seed, a
    // camera shake's two seeds and a loop driver's begin — over seeds, directions and arguments, on a list without ids and
    // on one with fixed ids. The digest's hash is a golden: these must not change
    public sealed class PlayMathTests
    {
        const long First = 0x0765432100000021L;
        const string Golden = "4787EAAAE8887274";

        readonly List<Object> m_Made = new List<Object>();
        readonly List<FeedbackPlayer> m_Players = new List<FeedbackPlayer>();
        readonly List<MotionDriver> m_Drivers = new List<MotionDriver>();
        readonly List<FeedbackScheduler> m_Schedulers = new List<FeedbackScheduler>();

        [SetUp]
        public void SetUp()
        {
            PlayerTestBinding.Reset();
            PlayerTestBinding.Register();
            PlayerRuntime.ResetSession();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (FeedbackPlayer player in m_Players)
            {
                if (player != null)
                    player.ReleaseRuntime();
            }
            m_Players.Clear();
            foreach (MotionDriver driver in m_Drivers)
            {
                if (driver != null)
                    driver.ReleaseBinding();
            }
            m_Drivers.Clear();
            foreach (Object o in m_Made)
            {
                if (o != null)
                    Object.DestroyImmediate(o);
            }
            m_Made.Clear();
            foreach (FeedbackScheduler scheduler in m_Schedulers)
            {
                if (!scheduler.IsShutdown)
                    scheduler.Shutdown();
            }
            m_Schedulers.Clear();
            PlayerTestBinding.Reset();
        }

        static TargetRef Self => new TargetRef { m_Mode = TargetRefMode.Self, m_Path = "", m_Anchor = "" };

        static PlayerTestAction Act(string label, float length) => new PlayerTestAction { m_Label = label, m_Length = length };

        static List<FeedbackItem> Items()
        {
            PlayerTestAction a = Act("a", 0.3f);
            a.m_Random.m_RandomizeOutput = true;
            a.m_Random.m_OutputRange = new Vector2(0.25f, 1f);
            PlayerTestAction b = Act("b", 0.2f);
            b.m_Conditions.m_Chance = 50f;
            b.m_Intensity = IntensityResponse.Ignore;
            b.m_Random.m_RandomizeOutput = true;
            b.m_Random.m_OutputRange = new Vector2(0.5f, 1.5f);
            PlayerTestAction c = Act("c", 0.4f);
            c.m_Random.m_RandomizeDuration = true;
            c.m_Random.m_DurationRange = new Vector2(0.5f, 2f);
            c.m_Conditions.m_Direction = DirectionCondition.ForwardOnly;
            PlayerTestAction d = Act("d", 0.2f);
            d.m_Conditions.m_Direction = DirectionCondition.ReverseOnly;
            PlayerTestAction e = Act("e", 0.3f);
            e.m_Conditions.m_UseIntensityRange = true;
            e.m_Conditions.m_IntensityRange = new Vector2(0.3f, 0.9f);
            PlayerTestAction f = Act("f", 0.3f);
            f.m_PlayDirection = ItemPlayDirection.Backward;
            PlayerTestAction g = Act("g", 0.3f);
            g.m_PlayDirection = ItemPlayDirection.Opposite;
            g.m_Timing.m_RepeatCount = 1;
            PlayerTestAction h = Act("h", 0.3f);
            h.m_PlayDirection = ItemPlayDirection.Forward;
            h.m_Conditions.m_Chance = 30f;
            var shake = new PositionItem { m_Motion = new ShakeMotion(), m_Target = Self, m_Space = PositionSpace.Local };
            shake.m_Timing.m_Duration = 0.3f;
            shake.m_Timing.m_RepeatCount = 1;
            var camera = new CameraShakeItem { m_Motion = new ShakeMotion(), m_Target = Self, m_RotationAmplitude = new Vector3(5f, 5f, 5f) };
            camera.m_Timing.m_Duration = 0.3f;
            return new List<FeedbackItem> { a, b, c, d, e, f, g, h, shake, camera };
        }

        FeedbackScheduler Scheduler()
        {
            var s = new FeedbackScheduler(ContextKind.Test);
            m_Schedulers.Add(s);
            return s;
        }

        FeedbackPlayer Player(FeedbackScheduler s, string name, bool ids)
        {
            var go = new GameObject(name);
            m_Made.Add(go);
            var p = go.AddComponent<FeedbackPlayer>();
            m_Players.Add(p);
            p.m_Items.AddRange(Items());
            if (ids)
            {
                for (int i = 0; i < p.m_Items.Count; i++)
                    Assert.That(ManagedReferenceUtility.SetManagedReferenceIdForObject(p, p.m_Items[i], First + i), Is.True, "the fixed id of item " + i);
            }
            p.m_Intensity = 0.7f;
            p.m_RandomizeDuration = true;
            p.m_DurationRange = new Vector2(0.5f, 1.5f);
            p.m_DurationMultiplier = 1.25f;
            p.Bind(s);
            p.EnsureRuntime();
            return p;
        }

        static string Bits(float f) => BitConverter.SingleToInt32Bits(f).ToString("X8");

        // one play run out: what it decided when accepted, then what each item began with
        static string Play(FeedbackPlayer p, FeedbackScheduler s, uint seed, float input, bool backward)
        {
            PlayArgs args = PlayArgs.Default.WithSeed(seed).WithIntensity(input);
            if (backward)
                args = args.WithDirection(PlayerDirection.Backward);
            PlaybackHandle h = p.Play(in args);
            if (h == default)
                return "refused\n";
            PlayerRuntime rt = p.Runtime;
            PlaybackRecord rec = null;
            int index = -1;
            for (int r = 0; r < rt.RecordCount; r++)
            {
                if (rt.Record(r).Active && rt.Record(r).Handle == h)
                {
                    rec = rt.Record(r);
                    index = r;
                }
            }
            var sb = new StringBuilder();
            sb.Append(rec.Seed.ToString("X8")).Append(' ').Append(Bits(rec.Intensity)).Append(' ').Append(rec.Resolved.Total).Append(':');
            for (int k = 0; k < rt.ItemCount; k++)
                sb.Append(' ').Append(rec.Resolved.ItemSkipped(k) ? 's' : '-').Append(rec.Turned[k] ? 't' : '-').Append(Bits(rec.Output[k])).Append('/').Append(rec.Resolved.ItemDuration(k));
            for (int t = 0; t < 400 && p.IsPlaying; t++)
                s.Tick(0.05f);
            sb.Append(" |");
            for (int k = 0; k < rt.ItemCount; k++)
            {
                if (rec.Resolved.ItemSkipped(k))
                    continue;
                switch (rt.Binding(k))
                {
                    case PlayerTestBinding b:
                        sb.Append(' ').Append(b.Item.m_Label).Append('=').Append(b.LastBegin.ItemSeed.ToString("X8")).Append(',').Append(Bits(b.LastBegin.Intensity)).Append(',').Append(b.LastBegin.Occurrence).Append(b.LastBegin.Reverse ? 'r' : 'f');
                        break;
                    case PositionBinding pb:
                        sb.Append(" p=").Append(pb.EvaluationSeed(index).ToString("X8"));
                        break;
                    case CameraShakeBinding cb:
                        sb.Append(" c=").Append(cb.PositionSeed(index).ToString("X8")).Append(',').Append(cb.RotationSeed(index).ToString("X8"));
                        break;
                }
            }
            p.Stop();
            return sb.Append('\n').ToString();
        }

        static readonly uint[] s_Seeds = { 1u, 2u, 3u, 777u, 4242u, 12345u, 99999u, 0xDEADBEEFu };
        static readonly float[] s_Inputs = { 0.25f, 0.6f, 1f, 1.7f };

        string Digest()
        {
            var sb = new StringBuilder();
            FeedbackScheduler s = Scheduler();
            foreach (bool ids in new[] { false, true })
            {
                FeedbackPlayer p = Player(s, ids ? "ids" : "plain", ids);
                for (int i = 0; i < s_Seeds.Length; i++)
                {
                    sb.Append(ids ? "I " : "P ");
                    sb.Append(Play(p, s, s_Seeds[i], s_Inputs[i % s_Inputs.Length], (i & 1) == 1));
                }
            }
            // a loop driver's begin: its seed is the session's (reset), the intensity its own and the output's
            foreach (IntensityResponse response in new[] { IntensityResponse.Receive, IntensityResponse.Ignore })
            {
                var go = new GameObject("driver " + response);
                m_Made.Add(go);
                var item = new PlayerTestTarget { m_Motion = new OscillateMotion(), m_Target = Self, m_Label = "drive" };
                item.m_Intensity = response;
                item.m_Random.m_RandomizeOutput = true;
                item.m_Random.m_OutputRange = new Vector2(0.3f, 1.2f);
                var d = go.AddComponent<MotionDriver>();
                d.m_Item = item;
                d.m_Mode = MotionDriverMode.Loop;
                d.m_Intensity = 0.6f;
                m_Drivers.Add(d);
                d.Bind(s);
                var b = (PlayerTestBinding)d.Binding;
                sb.Append("D ").Append(d.Seed.ToString("X8")).Append(' ').Append(b.LastBegin.ItemSeed.ToString("X8")).Append(' ').Append(Bits(b.LastBegin.Intensity)).Append('\n');
            }
            return sb.ToString();
        }

        static string Hash(string text)
        {
            ulong h = 14695981039346656037UL;
            foreach (char ch in text)
            {
                h ^= ch;
                h *= 1099511628211UL;
            }
            return h.ToString("X16");
        }

        [Test]
        public void MG01_ThePlaysRandomPartsAndIntensities()
        {
            string digest = Digest();
            Assert.That(digest.Contains("refused"), Is.False, "a play was refused:\n" + digest);
            Assert.That(Hash(digest), Is.EqualTo(Golden), "the digest:\n" + digest);
        }

        // PlayMath against what the runtime decided: each play's intensity, skips, turns, outputs and lengths, the items'
        // seeds and begin intensities, the evaluation seeds and a loop driver's begin
        [Test]
        public void MG02_PlayMathIsWhatTheRuntimeDoes()
        {
            var fails = new List<string>();
            FeedbackScheduler s = Scheduler();
            foreach (bool ids in new[] { false, true })
            {
                FeedbackPlayer p = Player(s, ids ? "ids" : "plain", ids);
                List<FeedbackItem> items = p.m_Items;
                for (int i = 0; i < s_Seeds.Length; i++)
                {
                    uint seed = s_Seeds[i];
                    float input = s_Inputs[i % s_Inputs.Length];
                    bool backward = (i & 1) == 1;
                    string at = (ids ? "ids" : "plain") + " seed " + seed + ": ";
                    PlayArgs args = PlayArgs.Default.WithSeed(seed).WithIntensity(input);
                    if (backward)
                        args = args.WithDirection(PlayerDirection.Backward);
                    PlaybackHandle h = p.Play(in args);
                    PlayerRuntime rt = p.Runtime;
                    PlaybackRecord rec = null;
                    int index = -1;
                    for (int r = 0; r < rt.RecordCount; r++)
                    {
                        if (rt.Record(r).Active && rt.Record(r).Handle == h)
                        {
                            rec = rt.Record(r);
                            index = r;
                        }
                    }
                    if (rec == null)
                    {
                        fails.Add(at + "refused");
                        continue;
                    }
                    float intensity = PlayMath.PlayIntensity(p, input, 1f);
                    if (Bits(intensity) != Bits(rec.Intensity))
                        fails.Add(at + "intensity " + intensity + " / " + rec.Intensity);
                    var seeds = new uint[items.Count];
                    var scales = new float[items.Count];
                    var skips = new bool[items.Count];
                    for (int k = 0; k < items.Count; k++)
                    {
                        var e = (EffectItem)items[k];
                        seeds[k] = PlayMath.ItemSeed(seed, PlayMath.SeedKey(p, e, k, out bool isId), isId);
                        scales[k] = PlayMath.DurationScale(in e.m_Random, seeds[k]);
                        skips[k] = PlayMath.SkipsBySeed(e, seeds[k], intensity, backward, true, false);
                        if (skips[k] != rec.Resolved.ItemSkipped(k))
                            fails.Add(at + "skip of " + k);
                        if (skips[k])
                            continue;
                        if (PlayMath.Turns(e, backward, false, false) != rec.Turned[k])
                            fails.Add(at + "turn of " + k);
                        if (Bits(PlayMath.Output(in e.m_Random, seeds[k])) != Bits(rec.Output[k]))
                            fails.Add(at + "output of " + k);
                    }
                    var descs = new List<TimelineItemDesc>();
                    var listIndex = new List<int>();
                    TimelineConversion.DescribeList(items, descs, listIndex);
                    TimelineProgram program = TimelineProgram.Compile(descs.ToArray());
                    var resolved = new ResolvedTimeline(program);
                    program.Resolve(scales, skips, PlayMath.PlayerDurationScale(p, seed, false), PlayerRuntime.Offset(p.m_StartDelay), resolved);
                    for (int k = 0; k < items.Count; k++)
                    {
                        if (resolved.ItemDuration(k) != rec.Resolved.ItemDuration(k))
                            fails.Add(at + "length of " + k + " " + resolved.ItemDuration(k) + " / " + rec.Resolved.ItemDuration(k));
                    }
                    if (resolved.Total != rec.Resolved.Total)
                        fails.Add(at + "total " + resolved.Total + " / " + rec.Resolved.Total);
                    for (int t = 0; t < 400 && p.IsPlaying; t++)
                        s.Tick(0.05f);
                    for (int k = 0; k < items.Count; k++)
                    {
                        if (skips[k])
                            continue;
                        switch (rt.Binding(k))
                        {
                            case PlayerTestBinding b:
                                if (b.LastBegin.ItemSeed != seeds[k])
                                    fails.Add(at + "item seed of " + b.Item.m_Label);
                                if (Bits(b.LastBegin.Intensity) != Bits(PlayMath.ItemIntensity(b.Item.m_Intensity, rec.Intensity, rec.Output[k])))
                                    fails.Add(at + "begin intensity of " + b.Item.m_Label);
                                break;
                            case PositionBinding pb:
                                // a repeat of two: the last begun occurrence is the second forward, the first backward
                                if (pb.EvaluationSeed(index) != PlayMath.OccurrenceSeed(seeds[k], backward ? 0 : 1))
                                    fails.Add(at + "evaluation seed of the shake");
                                break;
                            case CameraShakeBinding cb:
                                uint position = PlayMath.OccurrenceSeed(seeds[k], 0);
                                if (cb.PositionSeed(index) != position || cb.RotationSeed(index) != Hash32.Tagged(position, Hash32.Copy, 1u))
                                    fails.Add(at + "the camera shake's seeds");
                                break;
                        }
                    }
                    p.Stop();
                }
            }
            foreach (IntensityResponse response in new[] { IntensityResponse.Receive, IntensityResponse.Ignore })
            {
                var go = new GameObject("driver " + response);
                m_Made.Add(go);
                var item = new PlayerTestTarget { m_Motion = new OscillateMotion(), m_Target = Self, m_Label = "drive" };
                item.m_Intensity = response;
                item.m_Random.m_RandomizeOutput = true;
                item.m_Random.m_OutputRange = new Vector2(0.3f, 1.2f);
                var d = go.AddComponent<MotionDriver>();
                d.m_Item = item;
                d.m_Mode = MotionDriverMode.Loop;
                d.m_Intensity = 0.6f;
                m_Drivers.Add(d);
                d.Bind(s);
                var b = (PlayerTestBinding)d.Binding;
                if (Bits(b.LastBegin.Intensity) != Bits(PlayMath.DriverIntensity(item, d.m_Intensity, d.Seed)))
                    fails.Add("the driver's intensity (" + response + ")");
            }
            Assert.That(fails, Is.Empty);
        }

        [Test]
        public void MG03_TheOutputsBounds()
        {
            void Bounds(bool on, float x, float y, float low, float high)
            {
                var random = new ItemRandomness { m_RandomizeOutput = on, m_OutputRange = new Vector2(x, y) };
                PlayMath.OutputBounds(in random, out float l, out float h);
                Assert.That((l, h), Is.EqualTo((low, high)), on + " " + x + " ~ " + y);
                if (!on)
                    return;
                for (uint i = 0; i < 64; i++)
                {
                    float o = PlayMath.Output(in random, i * 2654435761u);
                    Assert.That(o >= l && o <= h, Is.True, "a draw within the bounds: " + o);
                }
            }
            Bounds(false, 0.25f, 1f, 1f, 1f);
            Bounds(true, 0.25f, 1f, 0.25f, 1f);
            Bounds(true, 1.5f, 0.5f, 0.5f, 1.5f);
            Bounds(true, -0.5f, 0.5f, 0f, 1f);
            Bounds(true, 2f, 2f, 2f, 2f);
        }
    }
}
