using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Threading;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace xpTURN.Feedback.Tests.Editor
{
    // EditMode — a player's Intensity setter and the Variant keys: a preset's alias, the player's list alias, the player's
    // key and a play's key. A player is bound to a test scheduler that the row ticks directly; the test items log their calls
    // by label. A row checks everything and judges at the end
    public sealed class PlayerSetterTests
    {
        static readonly List<string> s_Warnings = new List<string>();
        readonly List<string> m_Fail = new List<string>();
        readonly List<Object> m_Made = new List<Object>();
        readonly List<FeedbackPlayer> m_Players = new List<FeedbackPlayer>();
        readonly List<FeedbackScheduler> m_Schedulers = new List<FeedbackScheduler>();

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
            PlayerTestBinding.Reset();
            PlayerTestBinding.Register();
            PlayerRuntime.ResetSession();
            Application.logMessageReceived += OnLog;
        }

        [TearDown]
        public void TearDown()
        {
            Application.logMessageReceived -= OnLog;
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
            foreach (FeedbackScheduler scheduler in m_Schedulers)
            {
                if (!scheduler.IsShutdown)
                    scheduler.Shutdown();
            }
            m_Schedulers.Clear();
            PlayerTestBinding.Reset();
        }

        void Check(bool ok, string what)
        {
            if (!ok)
                m_Fail.Add(what);
        }

        void End() => Assert.That(m_Fail, Is.Empty);

        // ---- fixtures

        FeedbackScheduler Scheduler()
        {
            var scheduler = new FeedbackScheduler(ContextKind.Test);
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

        FeedbackPreset Preset(string alias, params FeedbackItem[] items)
        {
            var preset = ScriptableObject.CreateInstance<FeedbackPreset>();
            preset.name = alias;
            preset.m_Alias = alias;
            preset.m_Items.AddRange(items);
            m_Made.Add(preset);
            return preset;
        }

        GameObject Made(string name)
        {
            var go = new GameObject(name);
            m_Made.Add(go);
            return go;
        }

        static PlayerTestAction Act(string label, float length) => new PlayerTestAction { m_Label = label, m_Length = length };

        static PlayerTestAction After(PlayerTestAction action)
        {
            action.m_Timing.m_Rule = StartRule.AfterPrevious;
            return action;
        }

        static TargetRef Anchored(string name) => new TargetRef { m_Mode = TargetRefMode.Anchor, m_Path = "", m_Anchor = name };

        static PositionItem To(TargetRef target, Vector3 to, float seconds)
        {
            var item = new PositionItem { m_Target = target, m_Motion = new TweenMotion { m_Params = new TweenParams { m_Mode = TweenMode.To, m_To = to, m_Ease = default, m_Loops = 1 } } };
            item.m_Timing.m_Duration = seconds;
            return item;
        }

        static PlayArgs Of(FeedbackPreset preset) => PlayArgs.Default.WithPreset(preset);

        static PlayerTestBinding B(string label) => PlayerTestBinding.Made.LastOrDefault(b => b.Item.m_Label == label);

        static string Began()
        {
            string text = string.Join(" ", PlayerTestBinding.Log.Where(l => l.Contains(".Begin")).Select(l => l.Substring(0, l.IndexOf('.'))));
            PlayerTestBinding.Log.Clear();
            return text;
        }

        static bool Near(double a, double b) => Math.Abs(a - b) < 1e-5;

        static string Fmt(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);

        // a player whose list is "base" (alias normal) with the Variants light and heavy — each a single action
        FeedbackPlayer Keyed(FeedbackScheduler s, out FeedbackPreset light, out FeedbackPreset heavy, float baseLength = 0.5f, float heavyLength = 0.5f)
        {
            light = Preset("light", Act("light", 0.5f));
            heavy = Preset("heavy", Act("heavy", heavyLength));
            FeedbackPlayer p = Player(s, Act("base", baseLength));
            p.m_ListAlias = "normal";
            p.m_Variants.Add(light);
            p.m_Variants.Add(heavy);
            return p;
        }

        // ---- T1 · T2 — the Intensity setter

        [Test]
        public void PV01_TheIntensitySetterReachesTheNextPlays()
        {
            FeedbackScheduler s = Scheduler();
            FeedbackPlayer p = Player(s, Act("a", 0.1f), After(Act("b", 0.1f)));
            bool reads = Near(p.Intensity, 1);
            p.Play();
            float first = B("a").LastBegin.Intensity;
            p.Intensity = 2f;
            s.Tick(0.15f);
            float running = B("b").LastBegin.Intensity;
            s.Tick(1f);
            p.Play();
            float next = B("a").LastBegin.Intensity;
            s.Tick(1f);
            Check(reads && Near(p.Intensity, 2) && Near(new SerializedObject(p).FindProperty("m_Intensity").floatValue, 2), "the getter and the inspector's field");
            Check(Near(first, 1) && Near(running, 1), "the play that runs keeps its own: " + Fmt(first) + " · " + Fmt(running));
            Check(Near(next, 2), "the next play takes it: " + Fmt(next));
            p.m_RemapIntensity = true;
            p.m_IntensityRemap = AnimationCurve.Constant(0f, 10f, 0.25f);
            p.Play();
            Check(Near(B("a").LastBegin.Intensity, 0.5), "after the remap: " + Fmt(B("a").LastBegin.Intensity));
            End();
        }

        [Test]
        public void PV02_AnIntensityOutOfRangeIsIgnored()
        {
            FeedbackScheduler s = Scheduler();
            FeedbackPlayer p = Player(s, Act("a", 0.1f));
            p.Intensity = -1f;
            p.Intensity = float.NaN;
            p.Intensity = float.PositiveInfinity;
            Check(Near(p.Intensity, 1), "unchanged: " + p.Intensity);
            Check(s_Warnings.Count(w => w.StartsWith(FeedbackPlayer.ArgumentWarning, StringComparison.Ordinal) && w.Contains("Intensity")) == 1, "one warning: " + string.Join(" | ", s_Warnings));
            p.Intensity = 0f;
            p.Play();
            Check(Near(p.Intensity, 0) && Near(B("a").LastBegin.Intensity, 0), "0 is taken: " + Fmt(B("a").LastBegin.Intensity));
            End();
        }

        // ---- T4 · T5 · T6 · T10 — the aliases and the player's key

        [Test]
        public void PV03_TryGetVariantFindsALists()
        {
            FeedbackScheduler s = Scheduler();
            FeedbackPlayer p = Keyed(s, out FeedbackPreset light, out FeedbackPreset heavy);
            FeedbackPreset blank = Preset("", Act("blank", 0.1f));
            p.m_Variants.Add(blank);
            p.m_Variants.Add(null);
            p.m_Variants.Add(light);
            bool heavyFound = p.TryGetVariant("heavy", out FeedbackPreset h);
            bool normalFound = p.TryGetVariant("normal", out FeedbackPreset n);
            Check(heavyFound && h == heavy, "a Variant's alias, before the prepare");
            Check(normalFound && n == null, "the list alias: the player's own list");
            Check(!p.TryGetVariant("Heavy", out _) && !p.TryGetVariant("", out _) && !p.TryGetVariant(null, out _) && !p.TryGetVariant("missing", out _), "the exact text — no empty key, none missing");
            FeedbackPreset linked = Preset("linked", Act("linked", 0.1f));
            FeedbackPlayer q = Player(s);
            q.m_Preset = linked;
            q.m_ListAlias = "own";
            q.m_Variants.Add(linked);
            bool viaLinked = q.TryGetVariant("linked", out FeedbackPreset l);
            Check(viaLinked && l == null && !q.TryGetVariant("own", out _), "a linked player's list takes its preset's alias");
            End();
        }

        [Test]
        public void PV04_ThePlayersKeyPicksThePlaysThatNameNoPreset()
        {
            FeedbackScheduler s = Scheduler();
            FeedbackPlayer p = Keyed(s, out FeedbackPreset light, out FeedbackPreset heavy);
            p.m_VariantKey = "heavy";
            p.Play();
            string key = Began();
            p.Play(Of(light));
            string named = Began();
            p.Play(PlayArgs.Default.WithPlayerList());
            string list = Began();
            p.Play(Of(null));
            string none = Began();
            Check(key == "heavy" && named == "light" && list == "base" && none == "heavy", "key · a named preset wins · the player's list · none: " + key + " " + named + " " + list + " " + none);
            p.m_VariantKey = "missing";
            p.Play();
            string missing = Began();
            p.m_VariantKey = "normal";
            p.Play();
            string normal = Began();
            Check(missing == "base" && normal == "base", "a key no list has · the list alias: " + missing + " " + normal);
            Check(!s_Warnings.Any(), "no warning: " + string.Join(" | ", s_Warnings));
            p.m_VariantKey = "heavy";
            p.m_Variants.Remove(heavy);
            p.Rebuild();
            p.Play();
            string gone = Began();
            p.m_Variants.Add(heavy);
            p.Rebuild();
            p.Play();
            string back = Began();
            heavy.m_Alias = "big";
            p.m_VariantKey = "big";
            p.Play();
            string renamed = Began();
            Check(gone == "base" && back == "heavy" && renamed == "heavy", "the key stays through Rebuild · an alias edit takes the next play: " + gone + " " + back + " " + renamed);
            PlayArgs a = PlayArgs.Default.WithPlayerList().WithPreset(light);
            PlayArgs b = Of(light).WithPlayerList();
            Check(!a.HasPlayerList && a.HasPreset && b.HasPlayerList && !b.HasPreset && b.Preset == null && !Of(null).HasPlayerList, "the later mark takes the other away");
            Check(PlayArgs.Default.WithVariant("x").VariantKey == "x" && PlayArgs.Default.WithVariant(null).VariantKey == "" && PlayArgs.Default.WithVariant("").VariantKey == ""
                  && PlayArgs.Default.VariantKey == "" && PlayArgs.Default.WithVariant("x").WithIntensity(2f).VariantKey == "x", "the play's key");
            End();
        }

        [Test]
        public void PV05_TheKeyIsSetInPlayModeOnly()
        {
            FeedbackPlayer p = Keyed(Scheduler(), out _, out _);
            p.VariantKey = "heavy";
            p.VariantKey = "light";
            Check(p.VariantKey == "" && p.m_VariantKey == null, "ignored: '" + p.VariantKey + "'");
            Check(s_Warnings.Count(w => w == FeedbackPlayer.VariantKeyWarning) == 1, "one warning: " + string.Join(" | ", s_Warnings));
            End();
        }

        [Test]
        public void PV06_EveryPlayThatNamesNoPresetFollowsTheKey()
        {
            // the public entries: Play · PlayAsync · PlayFromEvent without a preset argument — a new one has to be sorted here
            MethodInfo[] entries = typeof(FeedbackPlayer).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
                .Where(m => m.Name.StartsWith("Play", StringComparison.Ordinal) && !m.GetParameters().Any(x => x.ParameterType == typeof(FeedbackPreset)))
                .OrderBy(m => m.ToString(), StringComparer.Ordinal).ToArray();
            Check(entries.Length == 9, "nine entries: " + string.Join(" · ", entries.Select(m => m.ToString())));
            foreach (MethodInfo m in entries)
            {
                FeedbackScheduler s = Scheduler();
                FeedbackPlayer p = Keyed(s, out _, out _);
                p.m_VariantKey = "heavy";
                PlayerTestBinding.Log.Clear();
                object[] args = m.GetParameters().Select(x =>
                {
                    Type t = x.ParameterType.IsByRef ? x.ParameterType.GetElementType() : x.ParameterType;
                    return t == typeof(float) ? 1f : t == typeof(bool) ? (object)true : Activator.CreateInstance(t);
                }).ToArray();
                m.Invoke(p, args);
                string began = Began();
                Check(began == "heavy", m + " played '" + began + "'");
                p.Stop();
            }
            // a channel's play and a Play Player item's
            {
                FeedbackScheduler s = Scheduler();
                FeedbackPlayer p = Keyed(s, out _, out _);
                p.m_VariantKey = "heavy";
                var r = p.gameObject.AddComponent<FeedbackReceiver>();
                r.m_Channel = ChannelRef.Of(7);
                r.Bind(s);
                PlayerTestBinding.Log.Clear();
                s.Context.Channels.Send(FeedbackCommand.Play(ChannelRef.Of(7)), false, default);
                string began = Began();
                Check(began == "heavy", "a channel's play: '" + began + "'");
                FeedbackPlayer child = Keyed(s, out _, out _);
                child.m_VariantKey = "heavy";
                FeedbackPlayer parent = Player(s, new PlayPlayerItem { m_Player = child });
                PlayerTestBinding.Log.Clear();
                parent.Play();
                s.Tick(0.01f);
                string nested = Began();
                Check(nested == "heavy", "a Play Player item's: '" + nested + "'");
            }
            End();
        }

        [Test]
        public void PV07_TheFirstFrameIsTheList()
        {
            FeedbackScheduler s = Scheduler();
            GameObject hit = Made("hit");
            FeedbackPreset heavy = Preset("heavy", To(Anchored("hit"), new Vector3(0f, 3f, 0f), 0.5f));
            FeedbackPlayer p = Player(s, To(Anchored("hit"), new Vector3(1f, 0f, 0f), 0.5f));
            p.SetAnchor("hit", hit);
            p.m_Variants.Add(heavy);
            p.m_ApplyInitialState = true;
            p.m_Direction = PlayerDirection.Backward;
            p.m_VariantKey = "heavy";
            p.EnsureRuntime();
            Check((hit.transform.localPosition - new Vector3(1f, 0f, 0f)).sqrMagnitude < 1e-6f, "the list's first frame: " + hit.transform.localPosition);
            End();
        }

        [Test]
        public void PV08_TotalDurationIsTheKeysList()
        {
            FeedbackScheduler s = Scheduler();
            FeedbackPlayer p = Keyed(s, out _, out _, 0.5f, 2f);
            float none = p.TotalDuration;
            p.m_VariantKey = "heavy";
            float keyed = p.TotalDuration;
            p.m_VariantKey = "missing";
            float missing = p.TotalDuration;
            Check(Near(none, 0.5) && Near(keyed, 2) && Near(missing, 0.5), "list · key · missing: " + Fmt(none) + " " + Fmt(keyed) + " " + Fmt(missing));
            End();
        }

        [Test]
        public void PV09_TheFirstListOfAnAliasPlays()
        {
            FeedbackScheduler s = Scheduler();
            FeedbackPreset same = Preset("x", Act("variant", 0.1f));
            FeedbackPlayer p = Player(s, Act("base", 0.1f));
            p.m_ListAlias = "x";
            p.m_Variants.Add(same);
            p.Play(PlayArgs.Default.WithVariant("x"));
            string own = Began();
            FeedbackPreset first = Preset("y", Act("first", 0.1f));
            FeedbackPreset second = Preset("y", Act("second", 0.1f));
            FeedbackPlayer q = Player(s, Act("base2", 0.1f));
            q.m_Variants.Add(first);
            q.m_Variants.Add(second);
            q.Play(PlayArgs.Default.WithVariant("y"));
            string slots = Began();
            Check(own == "base" && slots == "first", "the own list before the Variants · slot order: " + own + " " + slots);
            End();
        }

        // ---- T24 — the order Select picks by, and the drive decisions on the same arguments

        [Test]
        public void PV10_SelectsOrder()
        {
            FeedbackScheduler s = Scheduler();
            FeedbackPlayer p = Keyed(s, out FeedbackPreset light, out FeedbackPreset heavy);
            FeedbackPreset stranger = Preset("stranger", Act("stranger", 0.1f));
            var rows = new (string name, PlayArgs args, string key, string plays)[]
            {
                ("default", PlayArgs.Default, null, "base"),
                ("the player's key", PlayArgs.Default, "heavy", "heavy"),
                ("a named preset over the player's key", Of(light), "heavy", "light"),
                ("the player's list over the player's key", PlayArgs.Default.WithPlayerList(), "heavy", "base"),
                ("the play's key", PlayArgs.Default.WithVariant("heavy"), null, "heavy"),
                ("the play's key over a named preset", Of(light).WithVariant("heavy"), null, "heavy"),
                ("a key no list has: the named preset", Of(light).WithVariant("missing"), null, "light"),
                ("the play's key on the own list over the player's key", PlayArgs.Default.WithVariant("normal"), "heavy", "base"),
                ("a play's key no list has: the player's key", PlayArgs.Default.WithVariant("missing"), "heavy", "heavy"),
                ("the play's key over the player's list", PlayArgs.Default.WithPlayerList().WithVariant("light"), null, "light"),
                ("the play's key over a stranger", Of(stranger).WithVariant("heavy"), null, "heavy"),
                ("a stranger", Of(stranger), null, ""),
            };
            foreach ((string name, PlayArgs args, string key, string plays) in rows)
            {
                p.m_VariantKey = key;
                bool lists = p.Lists(in args);
                bool drives = p.CanDrive(in args);
                PlaybackHandle h = p.Play(in args);
                string began = Began();
                Check(began == plays, name + ": played '" + began + "', expected '" + plays + "'");
                Check(lists == (plays != "") && drives == (plays != "") && (h != default) == (plays != ""), name + ": Lists " + lists + " · CanDrive " + drives + " · handle " + (h != default));
                p.Stop();
            }
            Check(s_Warnings.Count(w => w == FeedbackPlayer.VariantWarning) == 1, "the stranger's refusal warns once: " + string.Join(" | ", s_Warnings));
            End();
        }

        // ---- T25 — a channel's key reaches each receiver's own list

        [Test]
        public void PV11_AChannelCommandsKey()
        {
            FeedbackScheduler s = Scheduler();
            FeedbackPlayer a = Keyed(s, out _, out _);
            FeedbackPlayer b = Player(s, Act("plain", 0.5f));
            FeedbackPreset other = Preset("heavy", Act("otherHeavy", 0.5f));
            FeedbackPlayer c = Player(s, Act("cBase", 0.5f));
            c.m_Variants.Add(other);
            foreach (FeedbackPlayer p in new[] { a, b, c })
            {
                var r = p.gameObject.AddComponent<FeedbackReceiver>();
                r.m_Channel = ChannelRef.Of(9);
                r.Bind(s);
            }
            PlayerTestBinding.Log.Clear();
            int reached = s.Context.Channels.Send(FeedbackCommand.Play(ChannelRef.Of(9), PlayArgs.Default.WithVariant("heavy")), false, default);
            string began = string.Join(" ", Began().Split(' ').OrderBy(x => x, StringComparer.Ordinal));
            Check(reached == 3 && began == "heavy otherHeavy plain", "each its own heavy, the plain one its list: " + reached + " · " + began);
            End();
        }
    }
}
