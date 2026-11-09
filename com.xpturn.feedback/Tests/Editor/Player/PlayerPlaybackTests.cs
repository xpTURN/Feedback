using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;
using UIs = UnityEngine.TestTools.Constraints.Is;

namespace xpTURN.Feedback.Tests.Editor
{
    // EditMode — the pipeline, the tick, the ends, the events, the API, the allocations, the goldens. A player is bound to a
    // test scheduler that the row ticks directly — the clock is the sum of the ticked unscaled dt. The test items are
    // registered with the test binding, whose log holds the calls and the events in order. A row checks everything and
    // judges at the end. Warnings are counted by a log listener; a logged exception is expected with LogAssert. The rows
    // that read Camera.main turn the open scene's main camera off and use their own
    public sealed class PlayerPlaybackTests
    {
        static readonly List<string> s_Warnings = new List<string>();
        readonly List<string> m_Fail = new List<string>();
        readonly List<Object> m_Made = new List<Object>();
        readonly List<FeedbackPlayer> m_Players = new List<FeedbackPlayer>();
        readonly List<FeedbackScheduler> m_Schedulers = new List<FeedbackScheduler>();
        readonly List<Camera> m_Silenced = new List<Camera>();
        readonly Dictionary<PlaybackHandle, int> m_HandleNo = new Dictionary<PlaybackHandle, int>();
        Action<FeedbackPlayer, PlayerEventKind, PlaybackHandle> m_Diagnostics;

        static void OnLog(string condition, string stackTrace, LogType type)
        {
            if (type == LogType.Warning)
                s_Warnings.Add(condition);
        }

        [SetUp]
        public void SetUp()
        {
            m_Fail.Clear();
            s_Warnings.Clear();
            m_HandleNo.Clear();
            PlayerTestBinding.Reset();
            PlayerTestBinding.Register();
            PlayerRuntime.ResetSession();
            Application.logMessageReceived += OnLog;
        }

        [TearDown]
        public void TearDown()
        {
            Application.logMessageReceived -= OnLog;
            if (m_Diagnostics != null)
            {
                FeedbackDiagnostics.PlayerEvent -= m_Diagnostics;
                m_Diagnostics = null;
            }
            // edit mode's destruction brings no OnDestroy — the one that bound releases first
            foreach (FeedbackPlayer player in m_Players)
            {
                if (player != null)
                    player.ReleaseRuntime();
            }
            m_Players.Clear();
            foreach (Object o in m_Made)
            {
                if (o != null)
                    Object.DestroyImmediate(o);
            }
            m_Made.Clear();
            foreach (Camera camera in m_Silenced)
            {
                if (camera != null)
                    camera.enabled = true;
            }
            m_Silenced.Clear();
            foreach (FeedbackScheduler scheduler in m_Schedulers)
            {
                if (!scheduler.IsShutdown)
                    scheduler.Shutdown();
            }
            m_Schedulers.Clear();
            m_HandleNo.Clear();
            PlayerTestBinding.Reset();
        }

        void Check(bool ok, string what)
        {
            if (!ok)
                m_Fail.Add(what);
        }

        void End() => Assert.That(m_Fail, Is.Empty);

        // ---- helpers

        FeedbackScheduler Scheduler(ContextKind kind = ContextKind.Test)
        {
            var scheduler = new FeedbackScheduler(kind);
            m_Schedulers.Add(scheduler);
            return scheduler;
        }

        FeedbackPlayer Player(FeedbackScheduler scheduler, params FeedbackItem[] items)
        {
            var go = new GameObject("player");
            m_Made.Add(go);
            var player = go.AddComponent<FeedbackPlayer>();
            m_Players.Add(player);
            player.m_Items.AddRange(items);
            if (scheduler != null)
                player.Bind(scheduler);
            return player;
        }

        GameObject Made(string name)
        {
            var go = new GameObject(name);
            m_Made.Add(go);
            return go;
        }

        // the open scene's main camera off, a camera of the row's own at the origin
        GameObject OwnMainCamera()
        {
            foreach (Camera other in Camera.allCameras)
            {
                if (!other.CompareTag("MainCamera"))
                    continue;
                other.enabled = false;
                m_Silenced.Add(other);
            }
            GameObject camera = Made("camera");
            camera.tag = "MainCamera";
            camera.AddComponent<Camera>();
            return camera;
        }

        static PlayerTestAction Act(string label, float length, float delay = 0f, StartRule rule = StartRule.WithPrevious)
        {
            var action = new PlayerTestAction { m_Label = label, m_Length = length };
            action.m_Timing.m_Delay = delay;
            action.m_Timing.m_Rule = rule;
            return action;
        }

        static LoopEndItem LoopEnd(int count)
        {
            var end = new LoopEndItem { m_LoopCount = count };
            end.m_Timing.m_Rule = StartRule.AfterPrevious;
            return end;
        }

        static PlayerTestBinding B(string label) => PlayerTestBinding.Made.LastOrDefault(b => b.Item.m_Label == label);

        static string TakeLog()
        {
            string text = string.Join(" ", PlayerTestBinding.Log);
            PlayerTestBinding.Log.Clear();
            return text;
        }

        static string Events() => string.Join(" ", PlayerTestBinding.Log.Where(l => l.Length > 0 && l[0] == '|'));

        // the labels that began since the log was cleared — and the log cleared
        static string Begun()
        {
            string text = string.Join(" ", PlayerTestBinding.Log.Where(l => l.Contains(".Begin")).Select(l => l.Substring(0, l.IndexOf('.'))));
            PlayerTestBinding.Log.Clear();
            return text;
        }

        static bool Near(double a, double b, double eps = 1e-4) => Math.Abs(a - b) < eps;

        static string Fmt(float value) => value.ToString("0.###", CultureInfo.InvariantCulture);

        string No(PlaybackHandle handle)
        {
            if (handle == default)
                return "0";
            if (!m_HandleNo.TryGetValue(handle, out int n))
            {
                n = m_HandleNo.Count + 1;
                m_HandleNo[handle] = n;
            }
            return n.ToString(CultureInfo.InvariantCulture);
        }

        // the events into the binding's log, numbered by the handle's first appearance
        void Watch(FeedbackPlayer p)
        {
            p.Played += (pl, h) => PlayerTestBinding.Log.Add("|Played" + No(h));
            p.Completed += (pl, h) => PlayerTestBinding.Log.Add("|Completed" + No(h));
            p.Stopped += (pl, h) => PlayerTestBinding.Log.Add("|Stopped" + No(h));
            p.Looped += (pl, h) => PlayerTestBinding.Log.Add("|Looped" + No(h));
            p.Paused += pl => PlayerTestBinding.Log.Add("|Paused");
            p.Resumed += pl => PlayerTestBinding.Log.Add("|Resumed");
        }

        // ---- the order of the conditions, seen in the trace of the accepted plays

        string Trace(int extra, bool gate)
        {
            FeedbackScheduler s = Scheduler();
            FeedbackPlayer p = Player(s, Act("a", 0.1f));
            p.m_Chance = 50f;
            p.m_Cooldown = 1f;
            if (gate)
            {
                p.m_UseDistanceGate = true;
                p.m_GateReference = DistanceReference.PlayPosition;
                p.m_GateDistance = 10f;
            }
            p.EnsureRuntime();
            p.Runtime.Reseed(1357u);
            PlayArgs near = PlayArgs.Default.WithPosition(new Vector3(0f, 0f, 1f));
            PlayArgs far = PlayArgs.Default.WithPosition(new Vector3(0f, 0f, 50f));
            var sb = new StringBuilder();
            for (int k = 0; k < 12; k++)
            {
                bool taken = p.Play(gate ? near : PlayArgs.Default) != default;
                sb.Append(taken ? '1' : '0');
                // inside the cooldown after a taken call · out of range after any call
                if (gate || taken)
                {
                    for (int x = 0; x < extra; x++)
                        p.Play(gate ? far : PlayArgs.Default);
                }
                p.Stop();
                s.Tick(1.5f);
            }
            return sb.ToString();
        }

        static uint SeedRefuseThenAccept()
        {
            for (uint seed = 1; seed < 100000; seed++)
            {
                var r = new XorShiftRandom(seed);
                if (r.NextUnit() * 100f >= 50f && r.NextUnit() * 100f < 50f)
                    return seed;
            }
            throw new InvalidOperationException("no seed");
        }

        uint SeedAfterIgnored(int calls)
        {
            FeedbackScheduler s = Scheduler();
            FeedbackPlayer p = Player(s, Act("a", 10f));
            p.m_Policy = PlayPolicy.Ignore;
            p.EnsureRuntime();
            p.Runtime.Reseed(2468u);
            p.Play();
            for (int k = 0; k < calls; k++)
                p.Play();
            p.Stop();
            p.Play();
            return p.Runtime.Record(0).Seed;
        }

        string IgnoreTrace(int during)
        {
            FeedbackScheduler s = Scheduler();
            FeedbackPlayer p = Player(s, Act("a", 10f));
            p.m_Policy = PlayPolicy.Ignore;
            p.m_Chance = 50f;
            p.EnsureRuntime();
            p.Runtime.Reseed(2468u);
            var sb = new StringBuilder();
            for (int k = 0; k < 12; k++)
            {
                bool taken = p.Play() != default;
                sb.Append(taken ? '1' : '0');
                if (taken)
                {
                    for (int x = 0; x < during; x++)
                        p.Play();
                }
                p.Stop();
            }
            return sb.ToString();
        }

        [Test]
        public void BE01_ConditionOrder()
        {
            string plain = Trace(0, false);
            Check(plain.IndexOf('0') >= 0 && plain.IndexOf('1') >= 0, "the plain trace has both: " + plain);
            string cooled = Trace(3, false);
            Check(cooled == plain, "calls inside the cooldown change the trace: " + plain + " → " + cooled);
            string near = Trace(0, true);
            string far = Trace(3, true);
            Check(far == near, "calls out of range change the trace: " + near + " → " + far);
            // a call the chance refused does not start the cooldown
            FeedbackScheduler s = Scheduler();
            FeedbackPlayer p = Player(s, Act("a", 0.1f));
            p.m_Chance = 50f;
            p.m_Cooldown = 1f;
            p.EnsureRuntime();
            p.Runtime.Reseed(SeedRefuseThenAccept());
            bool first = p.Play() != default;
            bool second = p.Play() != default;
            Check(!first && second, "refused, then taken at the same time: " + first + " · " + second);
            uint quiet = SeedAfterIgnored(0);
            uint busy = SeedAfterIgnored(4);
            Check(busy == quiet, "Ignore's refusals move the next play's seed: " + quiet + " → " + busy);
            // Ignore refuses before the chance draws
            string none = IgnoreTrace(0);
            string calls = IgnoreTrace(5);
            Check(none.IndexOf('0') >= 0 && none.IndexOf('1') >= 0, "the Ignore trace has both: " + none);
            Check(calls == none, "calls Ignore refused move the chance: " + none + " → " + calls);
            End();
        }

        // ---- the policies

        [Test]
        public void BE02_Policies()
        {
            FeedbackScheduler s = Scheduler();
            FeedbackPlayer p = Player(s, Act("a", 1f));
            Watch(p);
            PlaybackHandle h1 = p.Play();
            PlaybackHandle h2 = p.Play();
            Check(h1 != default && h2 != default && h1 != h2, "Restart gives a new handle");
            Check(Events() == "|Played1 |Stopped1 |Played2", "Restart's events: " + Events());
            Check(!p.Stop(h1) && p.IsPlaying, "the old handle's Stop is false");
            PlayerTestBinding.Log.Clear();
            m_HandleNo.Clear();
            FeedbackPlayer q = Player(s, Act("b", 1f));
            q.m_Policy = PlayPolicy.Overlap;
            q.m_MaxOverlap = 2;
            Watch(q);
            PlaybackHandle q1 = q.Play();
            PlaybackHandle q2 = q.Play();
            PlaybackHandle q3 = q.Play();
            PlaybackHandle q4 = q.Play();
            Check(Events() == "|Played1 |Played2 |Stopped1 |Played3 |Stopped2 |Played4", "Overlap's events: " + Events());
            Check(!q.Stop(q1) && !q.Stop(q2) && q.Stop(q3) && q.Stop(q4), "Overlap keeps the two newest");
            FeedbackPlayer g = Player(s, Act("c", 1f));
            g.m_Policy = PlayPolicy.Ignore;
            bool first = g.Play() != default;
            bool playing = g.Play() != default;
            g.Pause();
            bool paused = g.Play() != default;
            Check(first && !playing && !paused, "Ignore refuses while playing and paused: " + first + " · " + playing + " · " + paused);
            // five plays in one frame on a scheduler of its own (reservation 1): one warning, at the third
            FeedbackScheduler own = Scheduler();
            FeedbackPlayer r = Player(own, Act("d", 1f));
            r.EnsureRuntime();
            own.RunRevert();
            s_Warnings.Clear();
            var counts = new List<int>();
            for (int k = 0; k < 5; k++)
            {
                r.Play();
                counts.Add(s_Warnings.Count);
            }
            own.RunUpdate(new FrameTime(0.016f, 0.016f));
            own.RunLateApply();
            own.RunRevert();
            for (int k = 0; k < 5; k++)
            {
                r.Play();
                counts.Add(s_Warnings.Count);
            }
            Check(string.Join(",", counts) == "0,0,1,1,1,1,1,1,1,1", "the growth warning: " + string.Join(",", counts));
            End();
        }

        // ---- the item skips one by one

        [Test]
        public void BE03_ItemSkips()
        {
            FeedbackScheduler s = Scheduler();
            PlayerTestAction c0 = Act("c0", 0.05f);
            c0.m_Conditions.m_Chance = 0f;
            PlayerTestAction c100 = Act("c100", 0.05f);
            c100.m_Conditions.m_Chance = 100f;
            PlayerTestAction cd = Act("cd", 0.05f);
            cd.m_Conditions.m_Cooldown = 2f;
            PlayerTestAction mp = Act("mp", 0.05f);
            mp.m_Conditions.m_MaxPlays = 2;
            PlayerTestAction ro = Act("ro", 0.05f);
            ro.m_Conditions.m_Direction = DirectionCondition.ReverseOnly;
            PlayerTestAction fo = Act("fo", 0.05f);
            fo.m_Conditions.m_Direction = DirectionCondition.ForwardOnly;
            PlayerTestAction ir = Act("ir", 0.05f);
            ir.m_Conditions.m_UseIntensityRange = true;
            ir.m_Conditions.m_IntensityRange = new Vector2(0.5f, 1f);
            FeedbackPlayer p = Player(s, c0, c100, cd, mp, ro, fo, ir, new PlayerTestUnregistered(), Act("z", 0.05f, 0.25f));
            float[] input = { 0.5f, 1f, 0.49f, 1.01f, 0.75f };
            string[] expected = { "c100 cd mp fo ir", "c100 mp fo ir", "c100 cd fo", "c100 fo", "c100 cd fo ir" };
            for (int k = 0; k < 5; k++)
            {
                PlayerTestBinding.Log.Clear();
                p.Play(PlayArgs.Default.WithIntensity(input[k]));
                string begun = Begun();
                Check(begun == expected[k], "play " + (k + 1) + " began " + begun + " — expected " + expected[k]);
                s.Tick(0.5f);
                string later = Begun();
                Check(later == "z", "play " + (k + 1) + " — the item after the skipped ones at its own time: " + later);
                p.Stop();
                s.Tick(0.5f);
            }
            // Rebuild clears the player's cooldown and the items' cooldowns and counts
            PlayerTestAction once = Act("once", 0.1f);
            once.m_Conditions.m_MaxPlays = 1;
            PlayerTestAction cool = Act("cool", 0.1f);
            cool.m_Conditions.m_Cooldown = 10f;
            FeedbackPlayer q = Player(s, once, cool);
            q.m_Cooldown = 10f;
            q.Play();
            q.Stop();
            bool again = q.Play() != default;
            q.Rebuild();
            PlayerTestBinding.Log.Clear();
            bool rebuilt = q.Play() != default;
            string began = Begun();
            Check(!again && rebuilt && began == "once cool", "Rebuild clears the cooldowns and the counts: " + again + " · " + rebuilt + " · " + began);
            End();
        }

        // ---- the confirmed inputs

        [Test]
        public void BE04_ConfirmedInputs()
        {
            FeedbackScheduler s = Scheduler();
            FeedbackPlayer d = Player(s, Act("a", 1f));
            d.m_RandomizeDuration = true;
            d.m_DurationRange = new Vector2(2f, 2f);
            d.m_StartDelay = 0.5f;
            PlaybackHandle h = d.Play();
            Check(Near(d.GetDuration(h), 2.5), "the play's length " + d.GetDuration(h));
            Check(Near(d.TotalDuration, 1.5), "the nominal " + d.TotalDuration);
            PlayerTestAction b = Act("b", 1f);
            b.m_Random.m_RandomizeDuration = true;
            b.m_Random.m_DurationRange = new Vector2(3f, 3f);
            FeedbackPlayer e = Player(s, b);
            float itemLength = e.GetDuration(e.Play());
            Check(Near(itemLength, 3), "the item's random length " + itemLength);
            PlayerTestAction g = Act("g", 1f);
            g.m_Intensity = IntensityResponse.Ignore;
            PlayerTestAction o = Act("o", 1f);
            o.m_Random.m_RandomizeOutput = true;
            o.m_Random.m_OutputRange = new Vector2(0.25f, 0.25f);
            FeedbackPlayer i = Player(s, Act("n", 1f), g, o);
            i.m_Intensity = 3f;
            i.Play(PlayArgs.Default.WithIntensity(0.5f));
            Check(Near(B("n").LastBegin.Intensity, 1.5) && Near(B("g").LastBegin.Intensity, 1) && Near(B("o").LastBegin.Intensity, 0.375),
                "intensity " + Fmt(B("n").LastBegin.Intensity) + " · " + Fmt(B("g").LastBegin.Intensity) + " · " + Fmt(B("o").LastBegin.Intensity));
            i.m_RemapIntensity = true;
            i.m_IntensityRemap = AnimationCurve.Constant(0f, 10f, 0.25f);
            i.Play(PlayArgs.Default.WithIntensity(0.5f));
            Check(Near(B("n").LastBegin.Intensity, 0.75), "the remapped intensity " + Fmt(B("n").LastBegin.Intensity));
            PlayerTestAction sc = Act("sc", 1f);
            sc.m_Timing.m_Clock = ItemClock.Scaled;
            PlayerTestAction un = Act("un", 1f);
            un.m_Timing.m_Clock = ItemClock.Unscaled;
            FeedbackPlayer f = Player(s, sc, un);
            f.m_Speed = 2f;
            f.Play();
            s.Tick(new FrameTime(0.1f, 0.25f));
            Check(Near(B("sc").LastTime.T, 0.2) && Near(B("un").LastTime.T, 0.5), "speed on both clocks " + Fmt(B("sc").LastTime.T) + " · " + Fmt(B("un").LastTime.T));
            FeedbackPlayer t = Player(s, Act("pl", 1f));
            t.Play();
            s.Tick(new FrameTime(0f, 0.1f));
            float scaled = B("pl").LastTime.T;
            t.Stop();
            t.m_TimeMode = PlayerTimeMode.Unscaled;
            t.Play();
            s.Tick(new FrameTime(0f, 0.1f));
            Check(Near(scaled, 0) && Near(B("pl").LastTime.T, 0.1), "the time mode follows without a prepare: " + Fmt(scaled) + " · " + Fmt(B("pl").LastTime.T));
            FeedbackPlayer z = Player(s, Act("zz", 1f));
            z.m_Speed = 0f;
            z.Play();
            s.Tick(0.1f);
            s.Tick(0.1f);
            bool stands = Near(B("zz").LastTime.T, 0) && z.IsPlaying;
            z.m_Speed = float.NaN;
            s.Tick(0.1f);
            Check(stands && Near(B("zz").LastTime.T, 0.1), "speed 0 stands · NaN reads 1: " + stands + " · " + Fmt(B("zz").LastTime.T));

            // the remap curve's end: past the last key the value stays
            FeedbackPlayer r = Player(s, Act("r", 1f));
            r.m_RemapIntensity = true;
            r.Play(PlayArgs.Default.WithIntensity(2f));
            Check(Near(B("r").LastBegin.Intensity, 1), "the remap curve's end: " + Fmt(B("r").LastBegin.Intensity));
            // an item's random duration stretches its runs and gaps
            PlayerTestAction rep = Act("rep", 0.2f);
            rep.m_Timing.m_RepeatCount = 2;
            rep.m_Timing.m_RepeatGap = 0.1f;
            rep.m_Random.m_RandomizeDuration = true;
            rep.m_Random.m_DurationRange = new Vector2(2f, 2f);
            FeedbackPlayer q = Player(s, rep);
            PlaybackHandle hq = q.Play();
            Check(Near(q.GetDuration(hq), 1.6), "the item's random duration stretches the runs and the gaps: " + q.GetDuration(hq));
            // after the prepare: the timing waits for Rebuild, the condition does not
            PlayerTestAction ea = Act("ea", 0.2f);
            PlayerTestAction eb = Act("eb", 0.2f);
            FeedbackPlayer u = Player(s, ea, eb);
            u.EnsureRuntime();
            ea.m_Timing.m_Delay = 0.5f;
            eb.m_Conditions.m_Chance = 0f;
            PlayerTestBinding.Log.Clear();
            u.Play();
            string before = Begun();
            u.Stop();
            u.Rebuild();
            PlayerTestBinding.Log.Clear();
            u.Play();
            string after = Begun();
            Check(before == "ea" && after == "", "the timing waits for Rebuild · the condition does not: " + before + " / " + after);
            // TotalDuration follows a changed multiplier and start delay
            FeedbackPlayer n = Player(s, Act("nm", 1f));
            float nominal = n.TotalDuration;
            n.m_DurationMultiplier = 3f;
            float tripled = n.TotalDuration;
            n.m_StartDelay = 0.25f;
            float delayed = n.TotalDuration;
            Check(Near(nominal, 1) && Near(tripled, 3) && Near(delayed, 3.25), "TotalDuration follows the multiplier and the delay: " + nominal + " · " + tripled + " · " + delayed);
            End();
        }

        // ---- a refusal leaves nothing

        [Test]
        public void BE05_RefusalLeavesNothing()
        {
            FeedbackScheduler s = Scheduler();
            // the four calls come after the cooldown, while the first play runs — Ignore refuses them, not the cooldown
            PlayerTestAction m = Act("m", 1f);
            m.m_Conditions.m_MaxPlays = 2;
            FeedbackPlayer p = Player(s, m);
            p.m_Policy = PlayPolicy.Ignore;
            p.m_Cooldown = 0.5f;
            bool first = p.Play() != default;
            s.Tick(0.625f);
            int refused = 0;
            for (int k = 0; k < 4; k++)
            {
                if (p.Play() == default)
                    refused++;
            }
            s.Tick(0.375f);
            PlayerTestBinding.Log.Clear();
            bool second = p.Play() != default;
            string began = Begun();
            Check(first && refused == 4 && second && began == "m", "after four refusals: " + first + " · " + refused + " · " + second + " · " + began);
            p.Stop();
            s.Tick(1f);
            bool third = p.Play() != default;
            string thirdBegan = Begun();
            Check(third && thirdBegan == "", "the item's third play is skipped (max plays 2): " + third + " · " + thirdBegan);
            End();
        }

        // ---- the seeds

        string SessionSeeds()
        {
            PlayerRuntime.ResetSession();
            FeedbackScheduler s = Scheduler();
            FeedbackPlayer p1 = Player(s, Act("s1", 1f));
            p1.EnsureRuntime();
            FeedbackPlayer p2 = Player(s, Act("s2", 1f));
            p2.EnsureRuntime();
            string Seeds(FeedbackPlayer p)
            {
                var list = new List<uint>();
                for (int k = 0; k < 3; k++)
                {
                    p.Play();
                    list.Add(p.Runtime.Record(0).Seed);
                }
                return string.Join(",", list);
            }
            return Seeds(p1) + "|" + Seeds(p2);
        }

        string Shape(uint seed, bool inactiveFlowAhead)
        {
            FeedbackScheduler s = Scheduler();
            var items = new List<FeedbackItem>();
            var wait = new WaitItem();
            wait.m_Timing.m_Duration = 0f;
            items.Add(wait);
            for (int k = 0; k < 6; k++)
            {
                PlayerTestAction a = Act("w" + k, 0.5f);
                a.m_Conditions.m_Chance = 50f;
                a.m_Random.m_RandomizeDuration = true;
                a.m_Random.m_DurationRange = new Vector2(0.5f, 2f);
                a.m_Random.m_RandomizeOutput = true;
                a.m_Random.m_OutputRange = new Vector2(0.25f, 1f);
                items.Add(a);
            }
            FeedbackPlayer p = Player(s, items.ToArray());
            p.m_RandomizeDuration = true;
            if (inactiveFlowAhead)
                wait.m_Active = false;
            p.EnsureRuntime();
            PlayerTestBinding.Log.Clear();
            PlaybackHandle h = p.Play(PlayArgs.Default.WithSeed(seed));
            PlaybackRecord r = p.Runtime.Record(0);
            var sb = new StringBuilder();
            sb.Append(Begun()).Append(" | ");
            for (int i = 0; i < p.Runtime.ItemCount; i++)
            {
                if (!(p.Runtime.Binding(i) is PlayerTestBinding b))
                    continue;
                sb.Append(b.Item.m_Label).Append(':').Append(TimelineTime.ToSeconds(r.Resolved.ItemDuration(i)).ToString("0.####", CultureInfo.InvariantCulture))
                    .Append('x').Append(r.Output[i].ToString("0.####", CultureInfo.InvariantCulture)).Append(' ');
            }
            sb.Append("| ").Append(p.GetDuration(h).ToString("0.####", CultureInfo.InvariantCulture));
            p.Stop();
            return sb.ToString();
        }

        [Test]
        public void BE06_Seeds()
        {
            string a = SessionSeeds();
            string b = SessionSeeds();
            Check(a == b, "the session's default seeds repeat: " + a + " / " + b);
            string[] two = a.Split('|');
            Check(two.Length == 2 && two[0] != two[1], "two players' seeds differ: " + a);
            string s42 = Shape(42u, false);
            string again = Shape(42u, false);
            string s43 = Shape(43u, false);
            Check(s42 == again, "the same seed, the same play: " + s42 + " / " + again);
            Check(s42 != s43, "another seed, another play: " + s43);
            string ahead = Shape(42u, true);
            Check(ahead == s42, "an inactive flow item ahead moves the item seeds: " + s42 + " / " + ahead);
            End();
        }

        // ---- the distance gate

        string Gate(FeedbackPlayer p, string label, in PlayArgs args)
        {
            PlaybackHandle h = p.Play(in args);
            string r = h == default ? "refused" : Fmt(B(label).LastBegin.Intensity);
            p.Stop();
            return r;
        }

        [Test]
        public void BE07_DistanceGate()
        {
            FeedbackScheduler s = Scheduler();
            GameObject camera = OwnMainCamera();
            FeedbackPlayer p = Player(s, Act("g", 1f));
            p.m_UseDistanceGate = true;
            p.m_GateDistance = 10f;
            p.m_UseGateFalloff = true;
            var results = new List<string>();
            p.transform.position = new Vector3(0f, 0f, 8f);
            results.Add(Gate(p, "g", PlayArgs.Default));
            results.Add(Gate(p, "g", PlayArgs.Default.WithPosition(new Vector3(0f, 0f, 6f))));
            p.transform.position = new Vector3(0f, 0f, 12f);
            results.Add(Gate(p, "g", PlayArgs.Default));
            p.transform.position = new Vector3(0f, 0f, 10f);
            results.Add(Gate(p, "g", PlayArgs.Default));
            p.transform.position = new Vector3(0f, 0f, 10.01f);
            results.Add(Gate(p, "g", PlayArgs.Default));
            p.transform.position = new Vector3(0f, 0f, 8f);
            p.m_UseGateFalloff = false;
            results.Add(Gate(p, "g", PlayArgs.Default));
            p.m_UseGateFalloff = true;
            Check(string.Join(" ", results) == "0.2 0.4 refused 0 refused 1", "MainCamera: " + string.Join(" ", results));
            results.Clear();
            GameObject anchor = Made("anchor");
            anchor.transform.position = new Vector3(0f, 0f, 8f);
            p.transform.position = Vector3.zero;
            p.m_GateReference = DistanceReference.Anchor;
            p.m_GateAnchor = "ref";
            p.SetAnchor("ref", anchor.transform);
            results.Add(Gate(p, "g", PlayArgs.Default));
            p.m_GateAnchor = "";
            results.Add(Gate(p, "g", PlayArgs.Default));
            p.m_GateReference = DistanceReference.PlayPosition;
            results.Add(Gate(p, "g", PlayArgs.Default.WithPosition(new Vector3(0f, 0f, 8f))));
            results.Add(Gate(p, "g", PlayArgs.Default));
            Check(string.Join(" ", results) == "0.2 1 0.2 1", "Anchor · no anchor name · PlayPosition · no position: " + string.Join(" ", results));
            results.Clear();
            p.m_GateReference = DistanceReference.MainCamera;
            p.transform.position = new Vector3(0f, 0f, 50f);
            foreach (float range in new[] { 0f, -1f, float.NaN })
            {
                p.m_GateDistance = range;
                results.Add(Gate(p, "g", PlayArgs.Default));
            }
            p.m_GateDistance = 10f;
            Object.DestroyImmediate(camera);
            results.Add(Gate(p, "g", PlayArgs.Default));
            Check(string.Join(" ", results) == "1 1 1 1", "no gate for 0 · −1 · NaN · no camera passes: " + string.Join(" ", results));

            // the anchor's name, found at each Play: a GameObject anchor set after the prepare, then destroyed
            FeedbackPlayer q = Player(s, Act("ga", 1f));
            q.m_UseDistanceGate = true;
            q.m_GateDistance = 10f;
            q.m_UseGateFalloff = true;
            q.m_GateReference = DistanceReference.Anchor;
            q.m_GateAnchor = "ref";
            q.EnsureRuntime();
            GameObject target = Made("anchor-go");
            target.transform.position = new Vector3(0f, 0f, 8f);
            q.SetAnchor("ref", target);
            string set = Gate(q, "ga", PlayArgs.Default);
            Object.DestroyImmediate(target);
            string gone = Gate(q, "ga", PlayArgs.Default);
            Check(set == "0.2" && gone == "1", "a GameObject anchor set after the prepare · then destroyed: " + set + " · " + gone);
            // a prefab's copy takes the name with it; its anchor is set on the copy
            GameObject template = Made("template");
            FeedbackPlayer original = template.AddComponent<FeedbackPlayer>();
            original.m_Items.Add(Act("gc", 1f));
            original.m_UseDistanceGate = true;
            original.m_GateDistance = 10f;
            original.m_UseGateFalloff = true;
            original.m_GateReference = DistanceReference.Anchor;
            original.m_GateAnchor = "ref";
            GameObject copied = Object.Instantiate(template);
            m_Made.Add(copied);
            FeedbackPlayer copy = copied.GetComponent<FeedbackPlayer>();
            m_Players.Add(copy);
            copy.Bind(s);
            GameObject copyAnchor = Made("copy-anchor");
            copyAnchor.transform.position = new Vector3(0f, 0f, 6f);
            copy.SetAnchor("ref", copyAnchor);
            string fromCopy = Gate(copy, "gc", PlayArgs.Default);
            Check(fromCopy == "0.4", "the copy's anchor: " + fromCopy);
            End();
        }

        // ---- the clock

        [Test]
        public void BE08_Clock()
        {
            FeedbackScheduler t = Scheduler(ContextKind.Test);
            t.Tick(new FrameTime(0.1f, 0.25f));
            FeedbackScheduler v = Scheduler(ContextKind.Preview);
            v.Tick(new FrameTime(0.1f, 0.25f));
            Check(Near(t.Context.Now, 0.25) && Near(v.Context.Now, 0.25), "the sum of the unscaled dt: " + t.Context.Now + " · " + v.Context.Now);
            FeedbackScheduler run = Scheduler(ContextKind.Runtime);
            Check(Math.Abs(run.Context.Now - Time.unscaledTimeAsDouble) < 0.5, "the runtime's clock is the engine's");
            FeedbackScheduler s = Scheduler();
            FeedbackPlayer p = Player(s, Act("c", 0.1f));
            p.m_Cooldown = 0.5f;
            bool a = p.Play() != default;
            s.Tick(0.3f);
            bool b = p.Play() != default;
            s.Tick(0.3f);
            bool c = p.Play() != default;
            Check(a && !b && c, "cooldown 0.5 at +0 · +0.3 · +0.6: " + a + " · " + b + " · " + c);
            End();
        }

        // ---- t = 0

        [Test]
        public void BE09_TimeZero()
        {
            FeedbackScheduler s = Scheduler();
            FeedbackPlayer p = Player(s, Act("i", 0f), Act("a", 0.05f), Act("b", 0.5f));
            p.EnsureRuntime();
            s.RunRevert();
            PlayerTestBinding.Log.Clear();
            p.Play();
            string atZero = TakeLog();
            Check(atZero == "i.Execute#0 a.Begin#0 b.Begin#0 a.Evaluate(0) b.Evaluate(0)", "t = 0 in the Play: " + atZero);
            s.RunUpdate(new FrameTime(0.1f, 0.1f));
            s.RunLateApply();
            string same = TakeLog();
            Check(same == "", "the frame's update ran the play: " + same);
            s.Tick(0.1f);
            string next = TakeLog();
            Check(next == "a.Evaluate(1) a.End:Completed a.Settle b.Evaluate(0.2)", "the next frame: " + next);
            FeedbackPlayer q = Player(s, Act("q", 1f));
            q.Play();
            s.Tick(0.1f);
            Check(Near(B("q").LastTime.T, 0.1), "a Play between frames goes on the next tick: " + Fmt(B("q").LastTime.T));
            End();
        }

        // ---- the events' order and count

        [Test]
        public void BE10_EventOrder()
        {
            FeedbackScheduler s = Scheduler();
            FeedbackPlayer p = Player(s, new LoopStartItem(), Act("l", 0.1f), LoopEnd(2));
            Watch(p);
            p.Play();
            for (int k = 0; k < 10 && p.IsPlaying; k++)
                s.Tick(0.05f);
            Check(Events() == "|Played1 |Looped1 |Looped1 |Completed1", "a Loop block twice: " + Events());
            PlayerTestBinding.Log.Clear();
            m_HandleNo.Clear();
            PlayerTestAction rep = Act("rep", 0.05f);
            rep.m_Timing.m_RepeatCount = 2;
            FeedbackPlayer r = Player(s, rep);
            Watch(r);
            r.Play();
            for (int k = 0; k < 10 && r.IsPlaying; k++)
                s.Tick(0.05f);
            Check(Events() == "|Played1 |Completed1", "an item's repeats: " + Events());
            PlayerTestBinding.Log.Clear();
            m_HandleNo.Clear();
            FeedbackScheduler own = Scheduler();
            FeedbackPlayer q = Player(own, Act("x", 0f), Act("y", 0f));
            bool playingInside = true;
            q.Completed += (pl, h) => playingInside = pl.IsPlaying;
            Watch(q);
            PlaybackHandle hq = q.Play();
            string log = TakeLog();
            Check(log == "x.Execute#0 y.Execute#0 |Played1 |Completed1", "an instant-only list: " + log);
            Check(hq != default && !playingInside && No(hq) == "1", "a valid handle · not playing in Completed");
            Check(!q.Stop(hq) && q.GetDuration(hq) == 0f && own.ActiveCount == 0, "the ended play's handle · no slot left: " + q.GetDuration(hq) + " · " + own.ActiveCount);
            m_HandleNo.Clear();
            FeedbackScheduler s2 = Scheduler();
            FeedbackPlayer w = Player(s2, Act("st", 0f), Act("long", 1f));
            Watch(w);
            PlayerTestBinding.Hook = (b, what, rec) =>
            {
                if (what == "Execute" && b.Item.m_Label == "st")
                    w.Stop();
            };
            PlaybackHandle hw = w.Play();
            PlayerTestBinding.Hook = null;
            Check(Events() == "|Stopped1 |Played1" && hw != default, "its own t = 0 stops it: " + Events());
            End();
        }

        // ---- the table and its isolation

        [Test]
        public void BE11_Reg13_EventTable()
        {
            FeedbackScheduler s = Scheduler();
            FeedbackPlayer p = Player(s, new LoopStartItem(), Act("l", 0.1f), LoopEnd(2));
            var order = new List<string>();
            p.Played += (pl, h) => order.Add("Played:C");
            p.Completed += (pl, h) => order.Add("Completed:C");
            p.Stopped += (pl, h) => order.Add("Stopped:C");
            p.Looped += (pl, h) => order.Add("Looped:C");
            p.Paused += pl => order.Add("Paused:C");
            p.Resumed += pl => order.Add("Resumed:C");
            foreach (PlayerEventKind kind in Enum.GetValues(typeof(PlayerEventKind)))
            {
                PlayerEventKind k = kind;
                var e1 = new UnityEvent();
                e1.AddListener(() => order.Add(k + ":U1"));
                var e2 = new UnityEvent();
                e2.AddListener(() => order.Add(k + ":U2"));
                p.m_UnityEvents.Add(new PlayerUnityEvent { m_Kind = k, m_Event = e1 });
                p.m_UnityEvents.Add(new PlayerUnityEvent { m_Kind = k, m_Event = e2 });
            }
            m_Diagnostics = (pl, kind, h) =>
            {
                if (pl == p)
                    order.Add(kind + ":D");
            };
            FeedbackDiagnostics.PlayerEvent += m_Diagnostics;
            p.Play();
            p.Pause();
            p.Resume();
            for (int k = 0; k < 10 && p.IsPlaying; k++)
                s.Tick(0.05f);
            p.Play();
            p.Stop();
            string expected = string.Join(" ", new[] { "Played", "Paused", "Resumed", "Looped", "Looped", "Completed", "Played", "Stopped" }
                .Select(k => k + ":C " + k + ":U1 " + k + ":U2 " + k + ":D"));
            Check(string.Join(" ", order) == expected, "the three stages: " + string.Join(" ", order));
            // a throwing subscriber and a throwing entry: logged, the rest of the table and the play go on
            FeedbackPlayer t = Player(s, Act("t", 1f));
            var after = new List<string>();
            t.Played += (pl, h) => throw new InvalidOperationException("test subscriber");
            var u1 = new UnityEvent();
            u1.AddListener(() => throw new InvalidOperationException("test entry"));
            var u2 = new UnityEvent();
            u2.AddListener(() => after.Add("U2"));
            t.m_UnityEvents.Add(new PlayerUnityEvent { m_Kind = PlayerEventKind.Played, m_Event = u1 });
            t.m_UnityEvents.Add(new PlayerUnityEvent { m_Kind = PlayerEventKind.Played, m_Event = u2 });
            FeedbackDiagnostics.PlayerEvent -= m_Diagnostics;
            m_Diagnostics = (pl, kind, h) =>
            {
                if (pl == t)
                    after.Add(kind + ":D");
            };
            FeedbackDiagnostics.PlayerEvent += m_Diagnostics;
            LogAssert.Expect(LogType.Exception, "InvalidOperationException: test subscriber");
            LogAssert.Expect(LogType.Exception, "InvalidOperationException: test entry");
            PlaybackHandle ht = t.Play();
            Check(ht != default && t.IsPlaying && string.Join(" ", after) == "U2 Played:D", "after the throwing stages: " + string.Join(" ", after));
            // a binding that throws at t = 0: no handle, no event, one log — the next Play stands
            FeedbackScheduler s3 = Scheduler();
            FeedbackPlayer x = Player(s3, Act("x", 1f));
            Watch(x);
            PlayerTestBinding.Log.Clear();
            PlayerTestBinding.ThrowsIn.Add("x.Begin");
            LogAssert.Expect(LogType.Exception, "InvalidOperationException: test x.Begin");
            PlaybackHandle hx = x.Play();
            PlayerTestBinding.ThrowsIn.Clear();
            Check(hx == default && Events() == "" && !x.IsPlaying, "a t = 0 exception: " + Events());
            Check(x.Play() != default, "the next Play stands");
            // a binding that throws in a tick: that play alone ends, no event, the rest End(Aborted)
            FeedbackScheduler s4 = Scheduler();
            FeedbackPlayer y = Player(s4, Act("y1", 1f), Act("y2", 1f));
            Watch(y);
            y.Play();
            PlayerTestBinding.Log.Clear();
            PlayerTestBinding.ThrowsIn.Add("y1.Evaluate");
            LogAssert.Expect(LogType.Exception, "InvalidOperationException: test y1.Evaluate");
            s4.Tick(0.1f);
            PlayerTestBinding.ThrowsIn.Clear();
            string log = TakeLog();
            Check(!y.IsPlaying && !log.Contains("|") && log.Contains("y2.End:Aborted") && log.Contains("y1.End:Aborted"), "a tick exception: " + log);
            End();
        }

        // ---- the re-entry table

        [Test]
        public void BE12_Reentry()
        {
            FeedbackScheduler s = Scheduler();
            FeedbackPlayer p = Player(s, Act("ri", 0f), Act("ra", 1f));
            var nested = new List<PlaybackHandle>();
            // a limit counted before the call: a nested Play the guard lets in calls these again
            int tries = 0;
            PlayerTestBinding.Hook = (b, what, rec) =>
            {
                if (what == "Execute" && b.Item.m_Label == "ri" && tries++ < 12)
                    nested.Add(p.Play());
            };
            p.Played += (pl, h) =>
            {
                if (tries++ < 12)
                    nested.Add(pl.Play());
            };
            p.Stopped += (pl, h) =>
            {
                if (tries++ < 12)
                    nested.Add(pl.Play());
            };
            s_Warnings.Clear();
            p.Play();
            p.Play();
            PlayerTestBinding.Hook = null;
            Check(nested.Count == 5 && nested.All(h => h == default), "a Play inside its own Play: " + nested.Count + " · " + nested.Count(h => h != default) + " taken");
            Check(s_Warnings.Count == 1, "one warning: " + s_Warnings.Count);
            FeedbackPlayer q = Player(s, Act("q", 1f));
            bool? rebuilt = null;
            q.Played += (pl, h) =>
            {
                if (rebuilt == null)
                    rebuilt = pl.Rebuild();
            };
            q.Play();
            Check(rebuilt == false, "Rebuild inside its own Play: " + rebuilt);
            FeedbackScheduler s2 = Scheduler();
            FeedbackPlayer r = Player(s2, Act("rr", 1f));
            r.Play();
            r.Stopped += (pl, h) => pl.ReleaseRuntime();
            PlaybackHandle h2 = r.Play();
            Check(h2 == default && s2.ActiveCount == 0, "a release in the policy's Stopped: " + (h2 != default) + " · slots " + s2.ActiveCount);
            FeedbackScheduler s3 = Scheduler();
            FeedbackPlayer t = Player(s3, Act("ta", 1f), Act("i1", 0f, 0.2f), Act("j1", 0f));
            PlayerTestBinding.Hook = (b, what, rec) =>
            {
                if (what == "Execute" && b.Item.m_Label == "i1")
                    t.Stop();
            };
            t.Play();
            PlayerTestBinding.Log.Clear();
            s3.Tick(0.3f);
            PlayerTestBinding.Hook = null;
            string stopped = TakeLog();
            Check(stopped.Contains("i1.Execute") && !stopped.Contains("j1.Execute") && !t.IsPlaying, "a Stop in a tick's instant: " + stopped);
            FeedbackScheduler s4 = Scheduler();
            FeedbackPlayer u = Player(s4, Act("ua", 1f), Act("ui", 0f, 0.2f));
            int once = 0;
            PlayerTestBinding.Hook = (b, what, rec) =>
            {
                if (what == "Execute" && b.Item.m_Label == "ui" && once++ == 0)
                    u.Play();
            };
            u.Play();
            s4.Tick(0.3f);
            PlayerTestBinding.Hook = null;
            float inPass = B("ua").LastTime.T;
            s4.Tick(0.1f);
            Check(Near(inPass, 0) && Near(B("ua").LastTime.T, 0.1), "a Play in a tick's instant goes on next tick: " + Fmt(inPass) + " · " + Fmt(B("ua").LastTime.T));
            foreach (string act in new[] { "rebuild", "release", "shutdown" })
            {
                FeedbackScheduler sx = Scheduler();
                FeedbackPlayer v = Player(sx, Act("va", 1f), Act("vi", 0f, 0.2f));
                Watch(v);
                PlayerTestBinding.Hook = (b, what, rec) =>
                {
                    if (what != "Execute" || b.Item.m_Label != "vi")
                        return;
                    if (act == "rebuild")
                        v.Rebuild();
                    else if (act == "release")
                        v.ReleaseRuntime();
                    else
                        sx.Shutdown();
                };
                v.Play();
                PlayerTestBinding.Log.Clear();
                m_HandleNo.Clear();
                sx.Tick(0.3f);
                PlayerTestBinding.Hook = null;
                string events = Events();
                string expected = act == "rebuild" ? "|Stopped1" : "";
                Check(!v.IsPlaying && events == expected, act + " in a tick's instant: " + events);
            }
            foreach (PlayPolicy policy in new[] { PlayPolicy.Ignore, PlayPolicy.Restart, PlayPolicy.Overlap })
            {
                FeedbackScheduler sx = Scheduler();
                FeedbackPlayer c = Player(sx, Act("cc", 0.05f));
                c.m_Policy = policy;
                PlaybackHandle again = default;
                bool done = false;
                c.Completed += (pl, h) =>
                {
                    if (done)
                        return;
                    done = true;
                    again = pl.Play();
                };
                c.Play();
                s_Warnings.Clear();
                sx.Tick(0.1f);
                Check(again != default && c.IsPlaying && s_Warnings.Count == 0, policy + " — a Play in Completed: " + (again != default) + " · warnings " + s_Warnings.Count);
            }

            // the scheduler closed in the policy's Stopped: no handle, no record left
            FeedbackScheduler s5 = Scheduler();
            FeedbackPlayer w = Player(s5, Act("cs", 1f));
            w.Play();
            w.Stopped += (pl, h) => s5.Shutdown();
            PlaybackHandle hw = w.Play();
            bool records = Enumerable.Range(0, w.Runtime.RecordCount).Any(i => w.Runtime.Record(i).Active);
            Check(hw == default && !w.IsPlaying && !records, "the scheduler closed in the policy's Stopped: " + (hw != default) + " · " + w.IsPlaying + " · " + records);
            // Rebuild inside a Rebuild's Stopped stands — one more runtime, made and let go
            FeedbackScheduler s6 = Scheduler();
            FeedbackPlayer n = Player(s6, Act("nr", 1f));
            n.Play();
            int inner = 0;
            n.Stopped += (pl, h) =>
            {
                if (inner++ == 0)
                    pl.Rebuild();
            };
            bool outer = n.Rebuild();
            Check(outer && s6.ReservedCount == 1 && !n.IsPlaying, "Rebuild inside a Rebuild's Stopped: " + outer + " · reserved " + s6.ReservedCount);
            // a Play inside a tick's event: the old pass does not evaluate the new play
            FeedbackScheduler s7 = Scheduler();
            FeedbackPlayer o = Player(s7, Act("ub", 1f), Act("uj", 0f, 0.2f));
            int first = 0;
            PlayerTestBinding.Hook = (b, what, rec) =>
            {
                if (what == "Execute" && b.Item.m_Label == "uj" && first++ == 0)
                    o.Play();
            };
            o.Play();
            PlayerTestBinding.Log.Clear();
            s7.Tick(0.3f);
            PlayerTestBinding.Hook = null;
            int evaluations = PlayerTestBinding.Log.Count(l => l.StartsWith("ub.Evaluate", StringComparison.Ordinal));
            Check(evaluations == 1, "the old pass evaluates the new play after it ended: " + string.Join(" ", PlayerTestBinding.Log));
            End();
        }

        // ---- Stop

        [Test]
        public void BE13_Reg14_Stop()
        {
            FeedbackScheduler s = Scheduler();
            FeedbackPlayer p = Player(s, Act("a", 0.5f), Act("e", float.PositiveInfinity), Act("b", 0.1f), Act("i", 0f, 0.05f), Act("c", 0.3f, 1f), Act("j", 0f, 1f));
            Watch(p);
            string Phase(StopMode mode)
            {
                p.Play();
                s.Tick(0.2f);
                PlayerTestBinding.Log.Clear();
                m_HandleNo.Clear();
                p.Stop(mode);
                return TakeLog();
            }
            string hold = Phase(StopMode.Hold);
            Check(hold == "e.End:Hold a.End:Hold a.Settle e.Settle |Stopped1", "Hold: " + hold);
            string revert = Phase(StopMode.Revert);
            Check(revert == "i.End:Revert b.End:Revert e.End:Revert a.End:Revert a.Settle e.Settle |Stopped1", "Revert: " + revert);
            string complete = Phase(StopMode.Complete);
            Check(complete == "a.Evaluate(1) e.End:Complete a.End:Complete a.Settle e.Settle |Stopped1", "Complete: " + complete);
            FeedbackPlayer other = Player(s, Act("o", 1f));
            PlaybackHandle ho = other.Play();
            PlaybackHandle mine = p.Play();
            // Stop(default) would be Stop(StopMode) — the invalid handle is spelled out
            Check(!p.Stop(ho) && !p.Stop(default(PlaybackHandle)), "another player's handle · the invalid handle");
            p.Stop(mine);
            Check(!p.Stop(mine), "an ended handle");
            p.Play();
            s_Warnings.Clear();
            p.Stop((StopMode)9);
            Check(p.IsPlaying && s_Warnings.Count == 1, "an undefined mode: " + p.IsPlaying + " · " + s_Warnings.Count);
            p.Stop();
            FeedbackScheduler s2 = Scheduler();
            FeedbackPlayer q = Player(s2, Act("qa", 1f), Act("qb", 0.1f));
            q.m_Policy = PlayPolicy.Overlap;
            q.m_MaxOverlap = 2;
            PlayerTestBinding.LogRecords = true;
            q.Play();
            s2.Tick(0.2f);
            PlaybackHandle q2 = q.Play();
            s2.Tick(0.15f);
            PlayerTestBinding.Log.Clear();
            q.Stop(q2, StopMode.Revert);
            string second = TakeLog();
            PlayerTestBinding.LogRecords = false;
            Check(second == "qb.End:Revert@1 qa.End:Revert@1 qa.Settle@1", "the second play's Revert: " + second);
            FeedbackScheduler s3 = Scheduler();
            FeedbackPlayer w = Player(s3, Act("wa", 1f), Act("wb", 1f));
            w.Play();
            PlayerTestBinding.ThrowsIn.Add("wa.End");
            LogAssert.Expect(LogType.Exception, "InvalidOperationException: test wa.End");
            bool threw = false;
            try
            {
                w.Stop();
            }
            catch (Exception)
            {
                threw = true;
            }
            PlayerTestBinding.ThrowsIn.Clear();
            Check(!threw && !w.IsPlaying && s3.ActiveCount == 0, "a throwing End in a Stop: " + threw + " · " + w.IsPlaying + " · " + s3.ActiveCount);

            // the release: End(Hold), Settle, no event
            FeedbackScheduler s4 = Scheduler();
            FeedbackPlayer r = Player(s4, Act("ra", 1f));
            Watch(r);
            r.Play();
            s4.Tick(0.1f);
            PlayerTestBinding.Log.Clear();
            r.ReleaseRuntime();
            string released = TakeLog();
            Check(released == "ra.End:Hold ra.Settle", "the release: " + released);
            // Stop() ends the plays that ran when it was called — not the one its Stopped starts
            FeedbackScheduler s5 = Scheduler();
            FeedbackPlayer v = Player(s5, Act("va", 1f));
            v.m_Policy = PlayPolicy.Overlap;
            v.m_MaxOverlap = 2;
            bool started = false;
            v.Stopped += (pl, h) =>
            {
                if (started)
                    return;
                started = true;
                pl.Play();
            };
            v.Play();
            v.Stop();
            Check(v.IsPlaying, "Stop() stopped the play its Stopped started");
            // an ended play's handle has no length — a timed list
            FeedbackScheduler s6 = Scheduler();
            FeedbackPlayer f = Player(s6, Act("fa", 0.1f));
            PlaybackHandle hf = f.Play();
            s6.Tick(0.25f);
            Check(!f.IsPlaying && f.GetDuration(hf) == 0f, "an ended play's handle has no length: " + f.GetDuration(hf));
            End();
        }

        // ---- the pause

        [Test]
        public void BE14_Pause()
        {
            FeedbackScheduler s = Scheduler();
            FeedbackPlayer p = Player(s, Act("pa", 1f));
            p.m_Policy = PlayPolicy.Overlap;
            p.m_MaxOverlap = 2;
            Watch(p);
            p.Pause();
            Check(Events() == "", "a Pause with no play: " + Events());
            PlayerTestBinding.LogRecords = true;
            p.Play();
            s.Tick(0.125f);
            PlayerTestBinding.Log.Clear();
            p.Pause();
            p.Pause();
            Check(Events() == "|Paused", "two Pauses: " + Events());
            s.Tick(0.125f);
            p.Play();
            s.Tick(0.125f);
            string paused = TakeLog();
            Check(!paused.Contains("@0") && paused.Contains("pa.Evaluate(0.125)@1"), "the paused play stands · the later Play runs: " + paused);
            p.Resume();
            p.Resume();
            s.Tick(0.125f);
            string resumed = TakeLog();
            PlayerTestBinding.LogRecords = false;
            Check(resumed.StartsWith("|Resumed ", StringComparison.Ordinal) && resumed.Contains("pa.Evaluate(0.25)@0") && resumed.Split(' ').Count(x => x == "|Resumed") == 1,
                "Resume: " + resumed);
            // a Pause inside an event does not cut the pass
            FeedbackScheduler s2 = Scheduler();
            FeedbackPlayer q = Player(s2, Act("qa", 1f), Act("qi", 0f, 0.2f), Act("qj", 0f));
            PlayerTestBinding.Hook = (b, what, rec) =>
            {
                if (what == "Execute" && b.Item.m_Label == "qi")
                    q.Pause();
            };
            q.Play();
            PlayerTestBinding.Log.Clear();
            s2.Tick(0.3f);
            PlayerTestBinding.Hook = null;
            string pass = TakeLog();
            s2.Tick(0.1f);
            string next = TakeLog();
            Check(pass.Contains("qi.Execute") && pass.Contains("qj.Execute") && next == "", "the pass goes on after a Pause in an event: " + pass + " / " + next);
            End();
        }

        // ---- the pass and Settle

        [Test]
        public void BE15_PassAndSettle()
        {
            FeedbackScheduler s = Scheduler();
            PlayerTestAction r = Act("r", 0.02f);
            r.m_Timing.m_RepeatCount = 4;
            FeedbackPlayer p = Player(s, r);
            PlayerTestBinding.Log.Clear();
            p.Play();
            var begins = new List<string>(PlayerTestBinding.Log.Where(l => l.StartsWith("r.Begin", StringComparison.Ordinal)));
            var ends = new List<int>();
            var settles = new List<int>();
            for (int k = 0; k < 3; k++)
            {
                PlayerTestBinding.Log.Clear();
                s.Tick(0.05f);
                begins.AddRange(PlayerTestBinding.Log.Where(l => l.StartsWith("r.Begin", StringComparison.Ordinal)));
                ends.Add(PlayerTestBinding.Log.Count(l => l.StartsWith("r.End", StringComparison.Ordinal)));
                settles.Add(PlayerTestBinding.Log.Count(l => l == "r.Settle"));
            }
            Check(string.Join(",", ends) == "2,3,0" && string.Join(",", settles) == "1,1,0", "End per occurrence · Settle per pass: " + string.Join(",", ends) + " / " + string.Join(",", settles));
            Check(string.Join(" ", begins) == "r.Begin#0 r.Begin#1 r.Begin#2 r.Begin#3 r.Begin#4", "the occurrences: " + string.Join(" ", begins));
            FeedbackScheduler s2 = Scheduler();
            FeedbackPlayer q = Player(s2, Act("ga", 0.3f), Act("gb", 0.3f));
            Watch(q);
            q.Play();
            s2.Tick(0.1f);
            PlayerTestBinding.Log.Clear();
            PlayerTestBinding.Dead.Add("ga");
            s2.Tick(0.1f);
            string gone = TakeLog();
            PlayerTestBinding.Dead.Clear();
            Check(gone.Contains("ga.End:Aborted") && !gone.Contains("ga.Evaluate") && gone.Contains("gb.Evaluate"), "a gone target: " + gone);
            s2.Tick(0.05f);
            bool before = Events().Contains("|Completed");
            s2.Tick(0.05f);
            Check(!before && Events().Contains("|Completed"), "the play ends at its own time");
            // a target gone before the start is not called
            FeedbackScheduler s3 = Scheduler();
            FeedbackPlayer d = Player(s3, Act("da", 0.2f), Act("db", 0.2f));
            Watch(d);
            PlayerTestBinding.Log.Clear();
            PlayerTestBinding.Dead.Add("da");
            d.Play();
            s3.Tick(0.25f);
            PlayerTestBinding.Dead.Clear();
            string log = TakeLog();
            Check(!log.Contains("da.") && log.Contains("db.End:Completed") && log.Contains("|Completed"), "a target gone before the start: " + log);
            End();
        }

        // ---- the warnings

        [Test]
        public void BE16_Warnings()
        {
            GameObject unbound = Made("unbound");
            FeedbackPlayer p = unbound.AddComponent<FeedbackPlayer>();
            p.m_Items.Add(Act("u", 1f));
            s_Warnings.Clear();
            p.Play();
            p.Play();
            int twoPlays = s_Warnings.Count;
            p.Stop();
            p.Pause();
            p.Resume();
            Check(twoPlays == 1 && s_Warnings.Count == 1, "edit mode without a binding: " + twoPlays + " · " + s_Warnings.Count);
            FeedbackScheduler s = Scheduler();
            FeedbackPlayer q = Player(s, Act("pq", 1f));
            PlaybackHandle inner = default;
            int calls = 0;
            PlayerTestBinding.OnPrepare = b =>
            {
                if (calls++ == 0)
                    inner = q.Play();
            };
            s_Warnings.Clear();
            q.EnsureRuntime();
            PlayerTestBinding.OnPrepare = null;
            Check(inner == default && s_Warnings.Count == 1, "a Play inside its own prepare: taken " + (inner != default) + " · warnings " + s_Warnings.Count);
            // a destroyed player is quiet (a closed scheduler's quiet is PlayerPlaybackPlayTests' — edit mode drops a closed
            // binding and warns)
            FeedbackPlayer d = Player(Scheduler(), Act("pd", 1f));
            Object.DestroyImmediate(d.gameObject);
            s_Warnings.Clear();
            bool destroyed = d.Play() == default;
            Check(destroyed && s_Warnings.Count == 0, "a destroyed player (quiet): " + destroyed + " · " + s_Warnings.Count);
            FeedbackPlayer a = Player(Scheduler(), Act("pa", 1f));
            s_Warnings.Clear();
            bool nan = a.Play(PlayArgs.Default.WithIntensity(float.NaN)) == default;
            bool negative = a.Play(PlayArgs.Default.WithIntensity(-1f)) == default;
            bool infinite = a.Play(PlayArgs.Default.WithIntensity(float.PositiveInfinity)) == default;
            Check(nan && negative && infinite && s_Warnings.Count == 1, "the intensity argument: " + nan + " · " + negative + " · " + infinite + " · " + s_Warnings.Count);
            // the position argument — on a player without a gate too
            FeedbackPlayer w = Player(Scheduler(), Act("pp", 1f));
            s_Warnings.Clear();
            bool nanAt = w.Play(PlayArgs.Default.WithPosition(new Vector3(float.NaN, 0f, 0f))) == default;
            bool infiniteAt = w.Play(PlayArgs.Default.WithPosition(new Vector3(0f, float.PositiveInfinity, 0f))) == default;
            bool finiteAt = w.Play(PlayArgs.Default.WithPosition(new Vector3(1f, 2f, 3f))) != default;
            Check(nanAt && infiniteAt && finiteAt && s_Warnings.Count == 1, "the position argument: " + nanAt + " · " + infiniteAt + " · " + finiteAt + " · " + s_Warnings.Count);
            // an undefined StopMode warns before the prepare
            GameObject unprepared = Made("unprepared");
            FeedbackPlayer u = unprepared.AddComponent<FeedbackPlayer>();
            s_Warnings.Clear();
            u.Stop((StopMode)7);
            Check(s_Warnings.Count == 1 && u.Runtime == null, "an undefined StopMode before the prepare: " + s_Warnings.Count);
            End();
        }

        // ---- the allocations — the first run of five fresh fixtures

        static FeedbackPlayer s_AllocPlayer;
        static FeedbackScheduler s_AllocScheduler;
        static int s_AllocCount;

        static readonly TestDelegate s_Cycle = () =>
        {
            FeedbackPlayer p = s_AllocPlayer;
            FeedbackScheduler s = s_AllocScheduler;
            PlaybackHandle h = p.Play();
            s.Tick(0.05f);
            s.Tick(0.05f);
            p.Pause();
            s.Tick(0.05f);
            p.Resume();
            s.Tick(0.05f);
            p.Stop(h, StopMode.Revert);
            s.Tick(0.05f);
            p.Play(PlayArgs.Default.WithSeed(7u).WithIntensity(0.5f));
            s.Tick(1f);
            s.Tick(1f);
        };

        static readonly TestDelegate s_GateCycle = () =>
        {
            FeedbackPlayer p = s_AllocPlayer;
            FeedbackScheduler s = s_AllocScheduler;
            p.m_GateReference = DistanceReference.MainCamera;
            p.Play();
            s.Tick(0.05f);
            p.m_GateReference = DistanceReference.PlayPosition;
            p.Play(PlayArgs.Default.WithPosition(new Vector3(0f, 0f, 1f)));
            s.Tick(0.05f);
            p.m_GateReference = DistanceReference.Anchor;
            p.Play(PlayArgs.Default.WithPosition(new Vector3(0f, 0f, 1f)));
            s.Tick(0.05f);
        };

        static readonly TestDelegate s_Allocating = () => s_AllocCount += new int[4].Length;

        static void Count(FeedbackPlayer p, PlaybackHandle h) => s_AllocCount++;

        static void CountPlain(FeedbackPlayer p) => s_AllocCount++;

        void CycleFixture(PlayPolicy policy)
        {
            FeedbackScheduler s = Scheduler();
            PlayerTestAction random = Act("b", 0.1f, 0f, StartRule.AfterPrevious);
            random.m_Random.m_RandomizeDuration = true;
            random.m_Random.m_RandomizeOutput = true;
            random.m_Conditions.m_Chance = 50f;
            FeedbackPlayer p = Player(s, new LoopStartItem(), Act("a", 0.1f), random, Act("i", 0f, 0f, StartRule.AfterPrevious), LoopEnd(2));
            p.m_Policy = policy;
            p.m_RandomizeDuration = true;
            p.m_Chance = 99f;
            p.Played += Count;
            p.Completed += Count;
            p.Stopped += Count;
            p.Looped += Count;
            p.Paused += CountPlain;
            p.Resumed += CountPlain;
            p.EnsureRuntime();
            s_AllocPlayer = p;
            s_AllocScheduler = s;
        }

        // a fresh player whose anchor is a new object no script has read
        void GateFixture()
        {
            FeedbackScheduler s = Scheduler();
            FeedbackPlayer p = Player(s, Act("g", 0.3f));
            p.m_Policy = PlayPolicy.Overlap;
            p.m_MaxOverlap = 2;
            p.m_UseDistanceGate = true;
            p.m_GateDistance = 100f;
            p.m_UseGateFalloff = true;
            p.m_GateAnchor = "ref";
            p.SetAnchor("ref", Made("fresh-anchor"));
            p.EnsureRuntime();
            s_AllocPlayer = p;
            s_AllocScheduler = s;
        }

        static bool Allocated(TestDelegate run) => UIs.AllocatingGCMemory().ApplyTo(run).IsSuccess;

        // the first run of five fresh fixtures — the warm-up is another fixture with the same delegate
        static int FirstRuns(Action fixture, TestDelegate cycle)
        {
            fixture();
            cycle();
            int first = 0;
            for (int n = 0; n < 5; n++)
            {
                fixture();
                if (Allocated(cycle))
                    first++;
            }
            return first;
        }

        [Test]
        public void BE17_Allocation()
        {
            GameObject camera = OwnMainCamera();
            camera.transform.position = new Vector3(0f, 0f, -4f);
            PlayerTestBinding.Logging = false;
            int control = 0;
            for (int n = 0; n < 5; n++)
            {
                if (Allocated(s_Allocating))
                    control++;
            }
            int restart = FirstRuns(() => CycleFixture(PlayPolicy.Restart), s_Cycle);
            int overlap = FirstRuns(() => CycleFixture(PlayPolicy.Overlap), s_Cycle);
            int gate = FirstRuns(GateFixture, s_GateCycle);
            PlayerTestBinding.Logging = true;
            Check(control == 5, "the positive control: " + control);
            Check(restart < 5 && overlap < 5, "the cycle allocates on its first run: Restart " + restart + "/5 · Overlap " + overlap + "/5");
            Check(gate < 5, "the gate allocates on a fresh player's first run: " + gate + "/5");
            End();
        }

        // ---- the goldens and the surface

        static FieldInfo[] SavedFields(Type type) => DataModelTraitsTests.SavedFieldInfos(type).ToArray();

        static string Values(Type type) => string.Join(" ", Enum.GetValues(type).Cast<object>().Select(v => v + "=" + Convert.ToInt32(v)));

        static string Keys(AnimationCurve curve) => curve == null ? "null" : string.Join(" ", curve.keys.Select(k => "(" + Fmt(k.time) + "," + Fmt(k.value) + ")"));

        [Test]
        public void BE18_GoldenAndSurface()
        {
            Check(SavedFields(typeof(FeedbackPlayer)).Length == 29, "FeedbackPlayer's saved fields: " + SavedFields(typeof(FeedbackPlayer)).Length);
            Check(string.Join(" ", SavedFields(typeof(PlayerUnityEvent)).Select(f => f.Name)) == "m_Kind m_Event", "PlayerUnityEvent's saved fields");
            string names = string.Join(" ", SavedFields(typeof(FeedbackPlayer)).Select(f => f.Name));
            Check(names.Contains("m_GateReference m_GateAnchor m_GateDistance"), "the anchor's name field: " + names);
            Check(Values(typeof(PlayerAutoPlay)) == "None=0 OnStart=1 OnEnable=2", Values(typeof(PlayerAutoPlay)));
            Check(Values(typeof(PlayerTimeMode)) == "Scaled=0 Unscaled=1", Values(typeof(PlayerTimeMode)));
            Check(Values(typeof(PlayerEventKind)) == "Played=0 Completed=1 Stopped=2 Paused=3 Resumed=4 Looped=5", Values(typeof(PlayerEventKind)));
            Check(Values(typeof(DistanceReference)) == "MainCamera=0 Anchor=1 PlayPosition=2 Global=3", Values(typeof(DistanceReference)));
            Check(Values(typeof(StopMode)) == "Hold=0 Revert=1 Complete=2", Values(typeof(StopMode)));
            Type[] six = { typeof(PlayArgs), typeof(StopMode), typeof(PlayerAutoPlay), typeof(PlayerTimeMode), typeof(DistanceReference), typeof(PlayerEventKind) };
            Check(six.All(t => t.IsPublic), "the six public types");
            PlayArgs none = default;
            Check(none.Intensity == 1f && !none.HasSeed && !none.HasPosition, "default(PlayArgs) is the default play");
            GameObject go = Made("defaults");
            FeedbackPlayer p = go.AddComponent<FeedbackPlayer>();
            Check(p.m_AutoPlay == PlayerAutoPlay.None && p.m_TimeMode == PlayerTimeMode.Scaled && p.m_Speed == 1f && !p.m_RandomizeDuration
                  && p.m_DurationRange == new Vector2(0.5f, 2f) && p.m_Cooldown == 0f && p.m_Chance == 100f && p.m_Intensity == 1f
                  && !p.m_RemapIntensity && Keys(p.m_IntensityRemap) == "(0,0) (1,1)" && !p.m_UseDistanceGate
                  && p.m_GateReference == DistanceReference.MainCamera && p.m_GateAnchor == "" && p.m_GateDistance == 5f
                  && !p.m_UseGateFalloff && Keys(p.m_GateFalloff) == "(0,1) (1,0)" && p.m_UnityEvents != null && p.m_UnityEvents.Count == 0,
                "a new component's defaults: '" + p.m_GateAnchor + "' · " + p.m_GateDistance);
            Check(!typeof(FeedbackDiagnostics).IsPublic && !typeof(BeginContext).IsPublic && !typeof(PlayerUnityEvent).IsPublic, "FeedbackDiagnostics · BeginContext · PlayerUnityEvent are internal");
            End();
        }
    }
}
