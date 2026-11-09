using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using xpTURN.Feedback.Samples;
using static xpTURN.Feedback.Tests.Editor.Samples.SampleKitTestSupport;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace xpTURN.Feedback.Tests.Editor.Samples
{
    // the bundle · S8's list and table · forward · seek · landing · pause · the timeline's clock · S9's lamps · S11 · S12 · S14's
    // timeline and its play · the hub's columns. The samples' types are reached by name and the players' internal fields by reflection (this assembly is not
    // a friend of the runtime); a play-mode iterator captures no local in a lambda (the reload); Unity objects are read as
    // numbers in play
    public sealed class AdvancedTests
    {
        const BindingFlags All = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        static int s_Looped;
        static int s_Bursts;
        static PlaybackHandle s_Handle;

        // a static test leaves no sample scene open — the next play-mode test would play it (S12's lab writes the kit's state at Start)
        [TearDown]
        public void LeaveTheScene()
        {
            if (!EditorApplication.isPlaying && SceneManager.GetActiveScene().path.Contains("/Advanced/"))
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }

        // a play test leaves the samples' state as it was and the play mode (the kit's next test enters its own)
        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (!EditorApplication.isPlaying)
                yield break;
            Time.captureDeltaTime = 0f;
            SampleState.Restore();
            yield return new ExitPlayMode();
        }

        static string Scene(string name)
        {
            string path = AssetDatabase.FindAssets("t:Scene").Select(AssetDatabase.GUIDToAssetPath).FirstOrDefault(p => Path.GetFileNameWithoutExtension(p) == name && p.Contains("/Advanced/"));
            if (path == null)
                Assert.Ignore("no Advanced bundle in this project");
            return path;
        }

        static IEnumerator Frames(int n)
        {
            for (int i = 0; i < n; i++)
                yield return null;
        }

        static IEnumerator Real(float seconds)
        {
            float until = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < until)
                yield return null;
        }

        static IEnumerator Open(string name)
        {
            EditorSceneManager.LoadSceneInPlayMode(Scene(name), new LoadSceneParameters(LoadSceneMode.Single));
            yield return Frames(20);
        }

        static Component Lab(string type) => Resources.FindObjectsOfTypeAll<MonoBehaviour>().First(m => m != null && m.GetType().Name == type && m.gameObject.scene.isLoaded);

        static T F<T>(object o, string field) => (T)o.GetType().GetField(field, All).GetValue(o);

        static object P(object o, string property) => o.GetType().GetProperty(property, All).GetValue(o);

        static object Call(object o, string method, params object[] args) => o.GetType().GetMethod(method, All).Invoke(o, args);

        static double Pos(FeedbackPlayer p, PlaybackHandle h) => (long)Call(p, "Position", h) / 1e6;

        static List<object> Items(FeedbackPlayer p) => ((IList)F<object>(p, "m_Items")).Cast<object>().ToList();

        static string Label(object item) => (string)item.GetType().GetField("m_Label", All).GetValue(item);

        static object Timing(object item) => item.GetType().GetField("m_Timing", All).GetValue(item);

        static void OnLooped(FeedbackPlayer p, PlaybackHandle h) => s_Looped++;

        static (InputSettings.BackgroundBehavior, InputSettings.EditorInputBehaviorInPlayMode) Focus()
        {
            var was = (InputSystem.settings.backgroundBehavior, InputSystem.settings.editorInputBehaviorInPlayMode);
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            return was;
        }

        static void Unfocus((InputSettings.BackgroundBehavior, InputSettings.EditorInputBehaviorInPlayMode) was)
        {
            InputSystem.settings.backgroundBehavior = was.Item1;
            InputSystem.settings.editorInputBehaviorInPlayMode = was.Item2;
        }

        static void OnPlayed(FeedbackPlayer p, PlaybackHandle h) => s_Handle = h;

        static bool Flashy(object item)
        {
            object motion = item.GetType().GetField("m_Motion", All)?.GetValue(item);
            if (motion == null)
                return false;
            PropertyInfo traitsOf = item.GetType().GetProperty("Traits", All);
            if (traitsOf == null)
                return false;
            object traits = traitsOf.GetValue(item);
            bool sensitive = (bool)traits.GetType().GetField("FlashSensitive", All).GetValue(traits);
            string m = motion.GetType().Name;
            return sensitive && (m == "FlashMotion" || m == "BlinkMotion");
        }

        // ---- the bundle: catalog · samples entry · five asmdefs (S14's needs Timeline too) · the cards' one-letter keys bound in the
        // Sample map

        [Test]
        public void VQ01_TheBundle()
        {
            Scene("TimelineFlow");
            SampleCatalog catalog = Catalogs().FirstOrDefault(c => c.Bundle == "Advanced");
            Assert.That(catalog, Is.Not.Null, "the Advanced catalog");
            string[] scenes = catalog.Cards.Select(c => Path.GetFileNameWithoutExtension(AssetDatabase.GUIDToAssetPath(c.SceneGuid))).ToArray();
            Assert.That(catalog.Order, Is.EqualTo(40));
            Assert.That(scenes, Is.EqualTo(new[] { "TimelineFlow", "ChannelsRange", "StressZeroGC", "Accessibility", "TimelineCutscene" }));
            PackageInfo package = PackageInfo.FindForAssetPath("Packages/com.xpturn.feedback");
            string json = File.ReadAllText(Path.Combine(package.resolvedPath, "package.json"));
            Assert.That(json.Contains("\"displayName\": \"Advanced\"") && json.Contains("\"path\": \"Samples~/Advanced\""), Is.True, "the samples entry");
            var names = new List<string>();
            foreach (string file in Directory.GetFiles(Disk(BundleRoot(catalog)), "*.asmdef", SearchOption.AllDirectories))
            {
                var asmdef = JsonUtility.FromJson<Asmdef>(File.ReadAllText(file));
                names.Add(asmdef.name);
                var want = new List<string> { "XPTURN_FEEDBACK_SAMPLES_INPUTSYSTEM", "XPTURN_FEEDBACK_SAMPLES_UGUI", "XPTURN_FEEDBACK_SAMPLES_URP" };
                if (asmdef.name.EndsWith(".TimelineCutscene", StringComparison.Ordinal))
                    want.Add("XPTURN_FEEDBACK_SAMPLES_TIMELINE");
                Assert.That(asmdef.defineConstraints, Is.EquivalentTo(want), asmdef.name);
            }
            Assert.That(names, Is.EquivalentTo(new[] { "TimelineFlow", "ChannelsRange", "StressZeroGC", "Accessibility", "TimelineCutscene" }.Select(n => "xpTURN.Feedback.Samples.Advanced." + n)));
            var asset = AssetDatabase.LoadAssetAtPath<InputActionAsset>(ActionsPath);
            var bound = new HashSet<string>(asset.FindActionMap("Sample", true).bindings.Select(x => x.path));
            var problems = new List<string>();
            foreach (SampleCard card in catalog.Cards)
                foreach (string token in (card.Controls ?? "").Split(new[] { '·', '—', ',', '/', ' ' }, StringSplitOptions.RemoveEmptyEntries))
                    if (token.Trim().Length == 1 && char.IsLetterOrDigit(token.Trim()[0]) && !bound.Contains("<Keyboard>/" + char.ToLowerInvariant(token.Trim()[0])))
                        problems.Add(card.Title + ": card key " + token);
            Assert.That(problems, Is.Empty);
        }

        [Serializable]
        sealed class Asmdef
        {
            public string name = "";
            public string[] defineConstraints = new string[0];
        }

        static FeedbackPlayer Named(string name) => Resources.FindObjectsOfTypeAll<FeedbackPlayer>().First(p => p.gameObject.scene.isLoaded && p.name == name);

        // ---- S8's lists: the Flight one clock (the player's) · no Time item · no flash-sensitive Flash · a timed wait · no waiting
        // Play Player; the Flame an endless loop block

        [Test]
        public void VQ02_TheFlightIsSeekable()
        {
            EditorSceneManager.OpenScene(Scene("TimelineFlow"), OpenSceneMode.Single);
            FeedbackPlayer flight = Named("Flight Player"), flame = Named("Flame Player");
            var problems = new List<string>();
            foreach (object item in Items(flight))
            {
                string type = item.GetType().Name;
                if ((int)Timing(item).GetType().GetField("m_Clock", All).GetValue(Timing(item)) != 0)
                    problems.Add(Label(item) + ": its own clock");
                if (type == "HitStopItem" || type == "TimeScaleItem" || type == "PlayPlayerItem" || type == "PlayerChainItem")
                    problems.Add(Label(item) + ": " + type);
                if (Flashy(item))
                    problems.Add(Label(item) + ": a flash");
                if (type == "WaitForSignalItem" && !((float)item.GetType().GetField("m_Timeout", All).GetValue(item) > 0f))
                    problems.Add(Label(item) + ": no timeout");
            }
            object loopEnd = Items(flame).FirstOrDefault(i => i.GetType().Name == "LoopEndItem");
            Assert.That(loopEnd, Is.Not.Null, "the Flame's loop");
            Assert.That((int)loopEnd.GetType().GetField("m_LoopCount", All).GetValue(loopEnd), Is.EqualTo(0), "endless");
            Assert.That(problems, Is.Empty);
        }

        // ---- S8's table against the list: a row's start = its item's place in a play · the table's total = TotalDuration

        // a row's item and how far before it the row starts (the wait and the loop have no node of their own)
        static readonly (string row, string item, float before)[] s_RowItems =
        {
            ("Countdown 3 · 2 · 1", "Count 3", 0f), ("Charge", "Charge", 0f), ("Wait for Launch (2 s)", "Lift off", 2f),
            ("Lift off · Flame Play", "Lift off", 0f), ("Rise 12 m", "Rise", 0f), ("Stage loop ×3", "Stage", 0f), ("End · Flame (reverse)", "End", 0f),
        };

        // a row that is one item: its length is that item's (the wait's — its timeout)
        static readonly (string row, string item)[] s_RowLengths = { ("Charge", "Charge"), ("Charge", "Charge light"), ("Rise 12 m", "Rise"), ("Wait for Launch (2 s)", "Wait for Launch") };

        [UnityTest]
        public IEnumerator VQ03_TheTableIsTheList()
        {
            yield return new EnterPlayMode();
            yield return Open("TimelineFlow");
            Component lab = Lab("TimelineFlowLab");
            FeedbackPlayer flight = F<FeedbackPlayer>(lab, "m_Flight");
            Array rows = F<Array>(lab, "m_Rows");
            float total = F<float>(lab, "m_Total");
            bool totalOk = Mathf.Abs(flight.TotalDuration - total) < 0.001f;
            PlaybackHandle h = flight.Play();
            yield return null;
            List<object> items = Items(flight);
            var problems = new List<string>();
            foreach (object row in rows)
            {
                string name = (string)row.GetType().GetField("Name").GetValue(row);
                float start = (float)row.GetType().GetField("Start").GetValue(row);
                (string row, string item, float before) key = s_RowItems.First(r => r.row == name);
                int index = items.FindIndex(i => Label(i) == key.item);
                double place = (long)Call(flight, "ItemPlace", h, index) / 1e6 - key.before;
                if (Math.Abs(place - start) > 0.001)
                    problems.Add($"{name}: table {start} · list {place:0.000}");
            }
            foreach ((string row, string item) pair in s_RowLengths)
            {
                object r = rows.Cast<object>().First(x => (string)x.GetType().GetField("Name").GetValue(x) == pair.row);
                float length = (float)r.GetType().GetField("Duration").GetValue(r);
                object it = items.First(i => Label(i) == pair.item);
                float own = it.GetType().Name == "WaitForSignalItem" ? (float)it.GetType().GetField("m_Timeout", All).GetValue(it) : (float)Timing(it).GetType().GetField("m_Duration", All).GetValue(Timing(it));
                if (Mathf.Abs(own - length) > 0.001f)
                    problems.Add($"{pair.row}: table {length} · {pair.item} {own}");
            }
            flight.Stop(StopMode.Revert);
            yield return Frames(2);
            Assert.That((totalOk, problems.Count == 0 ? "" : string.Join(" | ", problems)), Is.EqualTo((true, "")), $"total {total}");
        }

        // ---- S8 forward: the countdown · a timed-out launch (nominal — the seek moves) · Looped ×3 · an early launch (the seek is
        // refused, the line says why) · Console 0

        [UnityTest]
        public IEnumerator VQ04_LaunchAndSeek()
        {
            yield return new EnterPlayMode();
            yield return Open("TimelineFlow");
            (int e0, int w0) = Counts();
            Component lab = Lab("TimelineFlowLab");
            FeedbackPlayer flight = F<FeedbackPlayer>(lab, "m_Flight"), flame = F<FeedbackPlayer>(lab, "m_Flame");
            Text count = Resources.FindObjectsOfTypeAll<Text>().First(t => t.gameObject.scene.isLoaded && t.name == "Countdown");
            s_Looped = 0;
            flight.Looped -= OnLooped;
            flight.Looped += OnLooped;
            SampleState.SetTimeScale(2f);
            Call(lab, "PlayForward");
            while ((double)P(lab, "Clock") < 1.0)
                yield return null;
            Call(lab, "Launch");
            while ((double)P(lab, "Clock") < 1.5)
                yield return null;
            string at15 = count.text;
            while (!flame.IsPlaying && (double)P(lab, "Clock") < 7.5)
                yield return null;
            double flameAt = (double)P(lab, "Clock");
            Call(lab, "TogglePause");
            Call(lab, "OnSeek", 6.5f);
            yield return null;
            double sought = (double)P(lab, "Clock");
            bool nominal = !(bool)P(lab, "OffNominal");
            Call(lab, "TogglePause");
            while (flight.IsPlaying)
                yield return null;
            int looped = s_Looped;

            Call(lab, "PlayForward");
            while ((double)P(lab, "Clock") < 4.5)
                yield return null;
            Call(lab, "Launch");
            yield return Frames(3);
            bool flameEarly = flame.IsPlaying;
            Call(lab, "TogglePause");
            double before = (double)P(lab, "Clock");
            Call(lab, "OnSeek", 1f);
            yield return null;
            bool refused = (bool)P(lab, "OffNominal") && Math.Abs((double)P(lab, "Clock") - before) < 0.01 && !flight.Seek(1f);
            string line = Resources.FindObjectsOfTypeAll<Text>().First(t => t.gameObject.scene.isLoaded && t.name == "Status").text;
            SampleState.SetTimeScale(1f);
            flight.Looped -= OnLooped;
            (int e1, int w1) = Counts();
            Assert.That((at15, Math.Abs(flameAt - 5.8) < 0.1, Math.Abs(sought - 6.5) < 0.01, nominal, looped),
                Is.EqualTo(("2", true, true, true, 3)), $"countdown at 1.5 s · flame at {flameAt:0.00} · seek → {sought:0.000}");
            Assert.That((flameEarly, refused, line.Contains("seek is refused"), e1 - e0, w1 - w0), Is.EqualTo((true, true, true, 0, 0)), line);
        }

        // ---- S8 Before → After: the Flame lit under Before (its flicker left out) flickers once After comes back

        [UnityTest]
        public IEnumerator VQ04b_TheFlameAfterBefore()
        {
            yield return new EnterPlayMode();
            yield return Open("TimelineFlow");
            Component lab = Lab("TimelineFlowLab");
            FeedbackPlayer flame = F<FeedbackPlayer>(lab, "m_Flame");
            Transform shape = GameObject.Find("Flame").transform;
            SampleState.SetTimeScale(4f);
            SampleState.SetAfter(false);
            Call(lab, "PlayForward");
            while (!flame.IsPlaying && (double)P(lab, "Clock") < 8f)
                yield return null;
            float s0 = shape.localScale.x;
            bool stillUnderBefore = true;
            for (int f = 0; f < 20; f++) { yield return null; stillUnderBefore &= Mathf.Abs(shape.localScale.x - s0) < 1e-4f; }
            SampleState.SetAfter(true);
            float spread = 0f;
            for (int f = 0; f < 60; f++) { yield return null; spread = Mathf.Max(spread, Mathf.Abs(shape.localScale.x - s0)); }
            SampleState.SetTimeScale(1f);
            Assert.That((flame.IsPlaying, stillUnderBefore, spread > 0.02f), Is.EqualTo((true, true, true)), $"the flicker after Before {spread:0.000}");
        }

        // ---- S8 landing: from the top (the flame lit by the reverse start, put out at ignition — hidden again) · from the top
        // after an early launch · mid-flight (the height goes on without a jump) · pause holds · the Flame takes one play (Ignore) and
        // a Skip ends it at once. A fixed 1/60 frame: the play's end comes before the sample's LateUpdate adds that frame to its clock
        // (the batch editor's frames are far shorter)

        [UnityTest]
        public IEnumerator VQ05_LandingAndPause()
        {
            yield return new EnterPlayMode();
            yield return Open("TimelineFlow");
            Component lab = Lab("TimelineFlowLab");
            FeedbackPlayer flight = F<FeedbackPlayer>(lab, "m_Flight"), flame = F<FeedbackPlayer>(lab, "m_Flame");
            Transform rocket = GameObject.Find("Rocket").transform;
            Renderer flameRenderer = GameObject.Find("Flame").GetComponent<Renderer>();
            Time.captureDeltaTime = 1f / 60f;
            SampleState.SetTimeScale(4f);
            Call(lab, "PlayForward");
            while (flight.IsPlaying)
                yield return null;
            float top = rocket.position.y;
            Call(lab, "Land");
            yield return Frames(2);
            bool litBack = flame.IsPlaying;
            while (flight.IsPlaying)
                yield return null;
            float ground = rocket.position.y;
            yield return Frames(2);
            bool out1 = !flame.IsPlaying && !flameRenderer.enabled;

            Call(lab, "PlayForward");
            while ((double)P(lab, "Clock") < 4.5)
                yield return null;
            Call(lab, "Launch");
            while (flight.IsPlaying)
                yield return null;
            Call(lab, "Land");
            yield return Frames(2);
            bool earlyTop = (bool)P(lab, "Backward") && flight.IsPlaying;
            while (flight.IsPlaying)
                yield return null;
            bool earlyGround = rocket.position.y < 0.5f;
            // a Play at the top starts from the pad (a completed play holds the top — a Stop does nothing to it)
            Call(lab, "PlayForward");
            while (flight.IsPlaying)
                yield return null;
            Call(lab, "PlayForward");
            while ((double)P(lab, "Clock") < 7.3)
                yield return null;
            bool padAfterTop = rocket.position.y < 8f;

            Call(lab, "PlayForward");
            while ((double)P(lab, "Clock") < 2.0)
                yield return null;
            Call(lab, "Land");
            yield return Frames(3);
            bool earlyLand = !(bool)P(lab, "Backward") && flight.IsPlaying && rocket.position.y < 0.5f;
            while ((double)P(lab, "Clock") < 7.3)
                yield return null;
            SampleState.SetTimeScale(1f);
            Call(lab, "TogglePause");
            float y0 = rocket.position.y;
            yield return Frames(10);
            bool held = Mathf.Abs(rocket.position.y - y0) < 1e-4f;
            Call(lab, "TogglePause");
            yield return null;
            float yBefore = rocket.position.y;
            Call(lab, "Land");
            float step = 0f, last = yBefore;
            for (int f = 0; f < 10; f++)
            {
                yield return null;
                step = Mathf.Max(step, Mathf.Abs(rocket.position.y - last));
                last = rocket.position.y;
            }
            bool down = last < yBefore;
            // a Play in flight starts from the pad with the flame out (the Restart's Hold would leave the height)
            while (flight.IsPlaying)
                yield return null;
            Call(lab, "PlayForward");
            while ((double)P(lab, "Clock") < 7.3)
                yield return null;
            Call(lab, "PlayForward");
            yield return Frames(2);
            bool fromPad = rocket.position.y < 0.5f && !flame.IsPlaying;
            // a seek before lift-off puts the flame out (the Player Control items do nothing when crossed backward)
            while ((double)P(lab, "Clock") < 7.3)
                yield return null;
            Call(lab, "TogglePause");
            Call(lab, "OnSeek", 4f);
            yield return null;
            bool outBySeek = !flame.IsPlaying && !flameRenderer.enabled;
            Call(lab, "OnSeek", 7f);
            yield return null;
            bool backBySeek = flame.IsPlaying;
            flight.Stop(StopMode.Revert);
            SampleState.SetTimeScale(1f);
            flame.Stop(StopMode.Revert);
            yield return null;
            flame.Play();
            PlaybackHandle second = flame.Play();
            yield return null;
            bool one = second == default && flame.IsPlaying;
            flame.Skip();
            yield return null;
            Assert.That((top > 12f, litBack, ground < 0.5f, out1, held, step < 0.5f, down, one, flame.IsPlaying),
                Is.EqualTo((true, true, true, true, true, true, true, true, false)), $"top {top:0.00} · ground {ground:0.00} · mid-flight step {step:0.000}");
            Assert.That((fromPad, outBySeek, backBySeek, earlyLand, earlyTop, earlyGround, padAfterTop), Is.EqualTo((true, true, true, true, true, true, true)),
                "a Play in flight · a seek before and after lift-off · a Land before it · a Land from the top after an early launch · a Play at the top");
        }

        // ---- the timeline's clock = the play's position (Played's frame on) · at the HUD's time scale 0.5 · after a seek

        [UnityTest]
        public IEnumerator VQ06_TheClockIsThePlay()
        {
            yield return new EnterPlayMode();
            yield return Open("TimelineFlow");
            Component lab = Lab("TimelineFlowLab");
            FeedbackPlayer flight = F<FeedbackPlayer>(lab, "m_Flight");
            flight.Played -= OnPlayed;
            flight.Played += OnPlayed;
            Time.captureDeltaTime = 1f / 30f;
            double worst = 0;
            for (int pass = 0; pass < 2; pass++)
            {
                SampleState.SetTimeScale(pass == 0 ? 1f : 0.5f);
                Call(lab, "PlayForward");
                // frames, not seconds: the fixed frame runs game time ahead of the wall clock
                for (int f = 0; f < 90; f++)
                {
                    yield return null;
                    worst = Math.Max(worst, Math.Abs((double)P(lab, "Clock") - Pos(flight, s_Handle)));
                }
            }
            Call(lab, "TogglePause");
            Call(lab, "OnSeek", 2.5f);
            yield return null;
            double afterSeek = Math.Abs((double)P(lab, "Clock") - Pos(flight, s_Handle));
            // Space — the Play comes inside the sample's Update, before its LateUpdate in the same frame (a button's · a key's way)
            flight.Stop(StopMode.Revert);
            SampleState.SetTimeScale(1f);
            yield return Frames(2);
            var was = Focus();
            var kb = InputSystem.AddDevice<Keyboard>();
            yield return Frames(2);
            InputSystem.QueueStateEvent(kb, new KeyboardState(Key.Space));
            double keyWorst = 0;
            for (int f = 0; f < 30; f++)
            {
                yield return null;
                if (f == 1)
                    InputSystem.QueueStateEvent(kb, new KeyboardState());
                if (flight.IsPlaying)
                    keyWorst = Math.Max(keyWorst, Math.Abs((double)P(lab, "Clock") - Pos(flight, s_Handle)));
            }
            bool keyPlayed = flight.IsPlaying;
            InputSystem.RemoveDevice(kb);
            Unfocus(was);
            flight.Played -= OnPlayed;
            flight.Stop(StopMode.Revert);
            Time.captureDeltaTime = 0f;
            Assert.That((keyPlayed, keyWorst < 0.002), Is.EqualTo((true, true)), $"by the key: worst {keyWorst:0.0000}");
            Assert.That((worst < 0.002, afterSeek < 0.002), Is.EqualTo((true, true)), $"worst {worst:0.0000} · after the seek {afterSeek:0.0000}");
        }

        // ---- S9's lamps: 49 · a receiver each on Red or Blue (a checkerboard · range 0) · the gate PlayPosition 5 m · a linear
        // falloff · no flash-sensitive Flash

        [Test]
        public void VQ07_TheLamps()
        {
            EditorSceneManager.OpenScene(Scene("ChannelsRange"), OpenSceneMode.Single);
            var lamps = Resources.FindObjectsOfTypeAll<FeedbackPlayer>().Where(p => p.gameObject.scene.isLoaded && p.name.StartsWith("Lamp ", StringComparison.Ordinal)).ToList();
            var problems = new List<string>();
            int red = 0;
            foreach (FeedbackPlayer p in lamps)
            {
                var rx = p.GetComponent<FeedbackReceiver>();
                object ch = F<object>(rx, "m_Channel");
                var asset = (UnityEngine.Object)ch.GetType().GetField("m_Asset", All).GetValue(ch);
                if (asset == null || F<float>(rx, "m_Range") != 0f)
                    problems.Add(p.name + ": channel · range");
                else if (asset.name == "Red")
                    red++;
                if (!F<bool>(p, "m_UseDistanceGate") || (int)F<DistanceReference>(p, "m_GateReference") != (int)DistanceReference.PlayPosition || F<float>(p, "m_GateDistance") != 5f || !F<bool>(p, "m_UseGateFalloff"))
                    problems.Add(p.name + ": gate");
                AnimationCurve c = F<AnimationCurve>(p, "m_GateFalloff");
                if (Mathf.Abs(c.Evaluate(0.25f) - 0.75f) > 0.01f || Mathf.Abs(c.Evaluate(0.75f) - 0.25f) > 0.01f)
                    problems.Add(p.name + ": falloff");
                if (Items(p).Any(Flashy))
                    problems.Add(p.name + ": a flash");
                Material m = p.GetComponent<Renderer>().sharedMaterial;
                if (m == null || !m.name.StartsWith("Lamp", StringComparison.Ordinal))
                    problems.Add(p.name + ": material " + (m != null ? m.name : "missing"));
            }
            Assert.That((lamps.Count, red), Is.EqualTo((49, 25)));
            Assert.That(problems, Is.Empty);
        }

        // ---- S9: a Red blast at the centre — 25 received, the 21 red lamps within 5 m pop (nearer more), blue still · Both 49 ·
        // the marker the same · Before sends nothing · four quick blasts all reach (no flash limit) · Console 0

        [UnityTest]
        public IEnumerator VQ08_ChannelsAndRange()
        {
            yield return new EnterPlayMode();
            yield return Open("ChannelsRange");
            (int e0, int w0) = Counts();
            Component lab = Lab("ChannelsRangeLab");
            var lamps = Resources.FindObjectsOfTypeAll<FeedbackPlayer>().Where(p => p.gameObject.scene.isLoaded && p.name.StartsWith("Lamp ", StringComparison.Ordinal)).Select(p => p.transform).ToList();
            Call(lab, "SetFilter", 0);
            Call(lab, "Explode", Vector3.zero);
            int got = (int)P(lab, "LastReceived");
            yield return Frames(4);
            int popped = 0, blue = 0, far = 0;
            var near = new List<(float d, float s)>();
            foreach (Transform t in lamps)
            {
                float s = t.localScale.x - 0.5f, d = new Vector2(t.position.x, t.position.z).magnitude;
                bool isRed = t.GetComponent<FeedbackReceiver>() is FeedbackReceiver rx && ((UnityEngine.Object)F<object>(rx, "m_Channel").GetType().GetField("m_Asset", All).GetValue(F<object>(rx, "m_Channel"))).name == "Red";
                if (Mathf.Abs(s) > 1e-4f)
                {
                    popped++;
                    if (!isRed) blue++;
                    if (d > 5f) far++;
                    near.Add((d, s));
                }
            }
            bool ordered = near.OrderBy(x => x.d).Select(x => x.s).Zip(near.OrderBy(x => x.d).Select(x => x.s).Skip(1), (a, b) => a >= b - 1e-4f).All(x => x);
            float centreS = near.Where(x => x.d < 0.1f).Select(x => x.s).DefaultIfEmpty(0f).First();
            float threeS = near.Where(x => Mathf.Abs(x.d - 3f) < 0.05f).Select(x => x.s).DefaultIfEmpty(1f).First();
            ordered &= centreS > 0f && threeS < 0.7f * centreS;
            yield return Real(1.2f);
            Call(lab, "SetFilter", 2);
            Call(lab, "Explode", Vector3.zero);
            int both = (int)P(lab, "LastReceived");
            yield return Real(1.2f);
            SampleState.SetAfter(false);
            Call(lab, "Explode", Vector3.zero);
            int before = (int)P(lab, "LastReceived");
            yield return Frames(4);
            bool still = lamps.All(t => Mathf.Abs(t.localScale.x - 0.5f) < 1e-4f);
            SampleState.SetAfter(true);
            int reached = 0;
            Transform centre = lamps.OrderBy(t => new Vector2(t.position.x, t.position.z).magnitude).First();
            for (int k = 0; k < 4; k++)
            {
                Call(lab, "Explode", Vector3.zero);
                yield return Frames(3);
                if (Mathf.Abs(centre.localScale.x - 0.5f) > 1e-4f) reached++;
                yield return Real(0.2f);
            }
            (int e1, int w1) = Counts();
            Assert.That((got, popped, blue, far, ordered, both, before, still, reached, e1 - e0, w1 - w0), Is.EqualTo((25, 21, 0, 0, true, 49, 0, true, 4, 0, 0)));
        }

        // ---- S11: opens at 100 · 1,000 built over frames · 0 leaves no player and no play · plays alive ≈ the count · the markers
        // read (a misspelt name reads 0) · the HUD's Select Player label untouched (the players are made at run time)

        [UnityTest]
        public IEnumerator VQ09_TheStress()
        {
            yield return new EnterPlayMode();
            yield return Open("StressZeroGC");
            Component lab = Lab("StressLab");
            yield return Real(1.5f);
            int at100 = (int)P(lab, "Count"), alive100 = (int)P(lab, "Alive");
            long update = ((Unity.Profiling.ProfilerRecorder)F<object>(lab, "m_Update")).LastValue;
            long revert = ((Unity.Profiling.ProfilerRecorder)F<object>(lab, "m_Revert")).LastValue;
            long late = ((Unity.Profiling.ProfilerRecorder)F<object>(lab, "m_Late")).LastValue;
            Text select = SampleHud.Instance != null ? F<Text>(SampleHud.Instance, "m_SelectLabel") : null;
            string label0 = select != null ? select.text : "";
            Call(lab, "SetStep", 2);
            yield return Frames(4);
            int at1000 = (int)P(lab, "Count");
            yield return Real(1.5f);
            int alive1000 = (int)P(lab, "Alive");
            string label1 = select != null ? select.text : "";
            Call(lab, "SetStep", 0);
            yield return Frames(3);
            int alive0 = (int)P(lab, "Alive");
            int left = Resources.FindObjectsOfTypeAll<FeedbackPlayer>().Count(p => p.gameObject.scene.isLoaded && p.gameObject.activeInHierarchy);
            Assert.That((at100, alive100, at1000, alive1000, (int)P(lab, "Count"), alive0, left),
                Is.EqualTo((100, 100, 1000, 1000, 0, 0, 0)), "players · plays alive");
            Assert.That((update > 0, revert > 0, late > 0, label1 == label0), Is.EqualTo((true, true, true, true)), $"markers {revert} · {update} · {late} · label '{label0}' → '{label1}'");
        }

        // ---- S11: the coroutine baseline allocates while on and the players do not — the frame's allocation (the sample's own
        // recorder; the editor adds its own, so the difference of the windows' medians — the editor's spikes move a mean by kilobytes)
        // · the players stop while it is on

        [UnityTest]
        public IEnumerator VQ10_TheCoroutineBaseline()
        {
            yield return new EnterPlayMode();
            yield return Open("StressZeroGC");
            Component lab = Lab("StressLab");
            Call(lab, "SetStep", 2);
            Call(lab, "SetRetrigger", true);
            yield return Real(2.5f);
            var players = new long[120];
            for (int f = 0; f < 120; f++) { yield return null; players[f] = (long)P(lab, "LastGc"); }
            Call(lab, "SetCoroutines", true);
            yield return Real(1.5f);
            var coroutines = new long[120];
            for (int f = 0; f < 120; f++) { yield return null; coroutines[f] = (long)P(lab, "LastGc"); }
            int playing = Resources.FindObjectsOfTypeAll<FeedbackPlayer>().Count(p => p.gameObject.scene.isLoaded && p.IsPlaying);
            Call(lab, "SetCoroutines", false);
            yield return Real(1.5f);
            var after = new long[120];
            for (int f = 0; f < 120; f++) { yield return null; after[f] = (long)P(lab, "LastGc"); }
            long p50 = Median(players), c50 = Median(coroutines), a50 = Median(after);
            Assert.That((c50 - p50 > 800, a50 - p50 < 200, playing), Is.EqualTo((true, true, 0)),
                $"medians: players {p50} · coroutines {c50} · after {a50} B/frame (means {players.Sum() / 120} · {coroutines.Sum() / 120} · {after.Sum() / 120})");
        }

        static long Median(long[] values)
        {
            var sorted = (long[])values.Clone();
            Array.Sort(sorted);
            return sorted[sorted.Length / 2];
        }

        // ---- S11's template: one Position Punch on itself · stops on disable

        [Test]
        public void VQ11_TheTemplate()
        {
            EditorSceneManager.OpenScene(Scene("StressZeroGC"), OpenSceneMode.Single);
            FeedbackPlayer t = Resources.FindObjectsOfTypeAll<FeedbackPlayer>().First(p => p.gameObject.scene.isLoaded && p.name == "Cube");
            List<object> items = Items(t);
            object target = items[0].GetType().GetField("m_Target", All).GetValue(items[0]);
            float duration = (float)Timing(items[0]).GetType().GetField("m_Duration", All).GetValue(Timing(items[0]));
            Assert.That((items.Count, items[0].GetType().Name, items[0].GetType().GetField("m_Motion", All).GetValue(items[0]).GetType().Name,
                    (int)target.GetType().GetField("m_Mode", All).GetValue(target), (int)F<PlayerDisableBehavior>(t, "m_DisableBehavior"), t.gameObject.activeSelf, duration),
                Is.EqualTo((1, "PositionItem", "PunchMotion", (int)TargetRefMode.Self, (int)PlayerDisableBehavior.StopHold, false, 1000f)));
        }

        // ---- S12's menu: each row's value in effect (the kit's composition — FeedbackCategories' public reads) · the panel says so

        [UnityTest]
        public IEnumerator VQ12_TheMenu()
        {
            yield return new EnterPlayMode();
            yield return Open("Accessibility");
            Component lab = Lab("AccessibilityLab");
            Call(lab, "Step", 0, -2);
            Call(lab, "Step", 1, -2);
            Call(lab, "Step", 2, 1);
            float shake = FeedbackCategories.GetScale(FeedbackCategory.Camera);
            bool motionOff = !FeedbackCategories.IsEnabled(FeedbackCategory.Motion);
            int limit = FeedbackCategories.FlashLimit;
            Call(lab, "Step", 2, 1);
            bool flashOff = !FeedbackCategories.IsEnabled(FeedbackCategory.Flash);
            Call(lab, "Step", 3, 1);
            Call(lab, "Step", 4, 1);
            bool timeOff = !FeedbackCategories.IsEnabled(FeedbackCategory.Time), hapticsOff = !FeedbackCategories.IsEnabled(FeedbackCategory.Haptics);
            yield return null;
            string camera = F<Text>(lab, "m_CameraScale").text, flash = F<Text>(lab, "m_FlashLimit").text;
            Assert.That((Mathf.Abs(shake - 0.5f) < 1e-4f, motionOff, limit, flashOff, timeOff, hapticsOff, camera, flash),
                Is.EqualTo((true, true, 1, true, true, true, "0.50", "Off")));
        }

        // ---- S12 live: a running sway halves at Motion 50 % · Hit stop off — a heavy hit leaves the time scale at 1 (on — it
        // stops) · the kit's Restore puts the scene's values back

        [UnityTest]
        public IEnumerator VQ13_LiveAndBack()
        {
            yield return new EnterPlayMode();
            yield return Open("Accessibility");
            Component lab = Lab("AccessibilityLab");
            Transform banner = GameObject.Find("Banner").transform;
            float full = 0f;
            float until = Time.time + 2f;
            while (Time.time < until) { yield return null; full = Mathf.Max(full, Mathf.Abs(Mathf.DeltaAngle(0f, banner.eulerAngles.z))); }
            Call(lab, "Step", 1, -1);
            yield return null;
            float half = 0f;
            until = Time.time + 2f;
            while (Time.time < until) { yield return null; half = Mathf.Max(half, Mathf.Abs(Mathf.DeltaAngle(0f, banner.eulerAngles.z))); }
            float least = 1f;
            Call(lab, "Attack", 1);
            for (int f = 0; f < 30; f++) { yield return null; least = Mathf.Min(least, Time.timeScale); }
            yield return Real(0.5f);
            Call(lab, "Step", 3, 1);
            float leastOff = 1f;
            Call(lab, "Attack", 1);
            for (int f = 0; f < 30; f++) { yield return null; leastOff = Mathf.Min(leastOff, Time.timeScale); }
            SampleState.Restore();
            bool back = FeedbackCategories.IsEnabled(FeedbackCategory.Time) && Mathf.Abs(FeedbackCategories.GetScale(FeedbackCategory.Motion) - 1f) < 1e-4f && FeedbackCategories.FlashLimit == 3;
            Assert.That((Mathf.Abs(half - full * 0.5f) < full * 0.1f, least < 0.1f, leastOff > 0.99f, back), Is.EqualTo((true, true, true, true)),
                $"sway {full:0.0}° → {half:0.0}° · time scale with hit stop {least:0.00} · off {leastOff:0.00}");
        }

        // ---- S14's timeline: three Feedback tracks on their players (Driven · Driven Loop · Triggered) and a Signal track whose
        // receiver calls the burst's PlayFromEvent · the director plays on waking on the game's clock, looping · the card asks
        // for Timeline and opens in the Editor. The Timeline types are reached by name (this assembly does not need Timeline)

        static double D(object o, string property) => (double)P(o, property);

        // the branch without Timeline keeps S14's files — its timeline and its lab are not there
        static void NeedsTimeline()
        {
            if (Type.GetType("UnityEngine.Timeline.TimelineAsset, Unity.Timeline") == null || Type.GetType("xpTURN.Feedback.Samples.Advanced.TimelineCutsceneLab, xpTURN.Feedback.Samples.Advanced.TimelineCutscene") == null)
                Assert.Ignore("no Timeline in this project");
        }

        [Test]
        public void VQ16_TheCutscene()
        {
            string scene = Scene("TimelineCutscene");
            NeedsTimeline();
            EditorSceneManager.OpenScene(scene, OpenSceneMode.Single);
            PlayableDirector director = Resources.FindObjectsOfTypeAll<PlayableDirector>().First(d => d.gameObject.scene.isLoaded);
            var rows = new List<string>();
            foreach (PlayableBinding b in director.playableAsset.outputs)
            {
                UnityEngine.Object track = b.sourceObject;
                UnityEngine.Object bound = director.GetGenericBinding(track);
                var parts = new List<string>();
                foreach (object clip in ((IEnumerable)track.GetType().GetMethod("GetClips").Invoke(track, null)).Cast<object>())
                {
                    object asset = P(clip, "asset");
                    parts.Add($"{D(clip, "start"):0.##}-{D(clip, "start") + D(clip, "duration"):0.##} {F<object>(asset, "m_Timing")}{(F<bool>(asset, "m_Loop") ? " Loop" : "")}");
                }
                foreach (object marker in ((IEnumerable)track.GetType().GetMethod("GetMarkers").Invoke(track, null)).Cast<object>())
                    parts.Add($"@{D(marker, "time"):0.##} {((UnityEngine.Object)P(marker, "asset")).name}");
                rows.Add($"{track.GetType().Name} {track.name} → {(bound != null ? bound.name : "none")}: {string.Join(" · ", parts)}");
            }
            Component receiver = Resources.FindObjectsOfTypeAll<MonoBehaviour>().First(m => m.GetType().Name == "SignalReceiver" && m.gameObject.scene.isLoaded);
            var reaction = (UnityEngine.Events.UnityEventBase)receiver.GetType().GetMethod("GetReactionAtIndex").Invoke(receiver, new object[] { 0 });
            UnityEngine.Object target = reaction.GetPersistentTarget(0);
            SampleCard card = Catalogs().SelectMany(c => c.Cards).First(c => c.SceneName == "TimelineCutscene");
            Assert.That(rows, Is.EqualTo(new[]
            {
                "FeedbackTrack Camera Shake → Shake Player: 1-2.2 Driven",
                "FeedbackTrack Hero → Hero: 0-6 Driven Loop",
                "FeedbackTrack Flash → Flash Player: 3-4 Triggered",
                "SignalTrack Burst → Burst: @4.5 Burst",
            }));
            Assert.That((target is FeedbackPlayer, target.name, reaction.GetPersistentMethodName(0), reaction.GetPersistentEventCount()), Is.EqualTo((true, "Burst", "PlayFromEvent", 1)));
            Assert.That((director.playOnAwake, director.timeUpdateMode, director.extrapolationMode), Is.EqualTo((true, DirectorUpdateMode.GameTime, DirectorWrapMode.Loop)));
            Assert.That((card.RequiresText, card.OpenInEditor, card.Try.Contains("Window > Sequencing > Timeline"), card.RequiredType),
                Is.EqualTo(("Timeline 1.8 or later", true, true, "xpTURN.Feedback.Samples.Advanced.TimelineCutsceneLab, xpTURN.Feedback.Samples.Advanced.TimelineCutscene")));
        }

        static void OnBurst(FeedbackPlayer p, PlaybackHandle h) => s_Bursts++;

        // ---- S14 in play: the Driven shake shows the same camera at the same time after a scrub away and back · the Triggered
        // flash plays from its clip · the Signal plays the burst once a loop · Before starts the cutscene over with the camera
        // and the hero still · the lab's frames allocate nothing (the frame's allocation with the lab on against a window with it
        // off — the director plays in both — and a positive control, a string a frame) · Console 0

        [UnityTest]
        public IEnumerator VQ17_TheCutscenePlays()
        {
            Scene("TimelineCutscene");
            NeedsTimeline();
            yield return new EnterPlayMode();
            Time.captureDeltaTime = 1f / 60f;
            yield return Open("TimelineCutscene");
            (int e0, int w0) = Counts();
            Component lab = Lab("TimelineCutsceneLab");
            PlayableDirector director = F<PlayableDirector>(lab, "m_Director");
            Transform cam = Camera.main.transform;
            Vector3 rest = cam.localPosition;
            float until = Time.realtimeSinceStartup + 30f;
            while (director.time < 1.5 && Time.realtimeSinceStartup < until)
                yield return null;
            Call(lab, "OnSeek", 1.6f);
            yield return null;
            Vector3 at = cam.localPosition;
            Call(lab, "OnSeek", 1.2f);
            yield return null;
            Call(lab, "OnSeek", 1.6f);
            yield return null;
            Vector3 again = cam.localPosition;
            bool paused = (bool)P(lab, "Paused");
            Call(lab, "TogglePause");
            FeedbackPlayer burst = Named("Burst");
            s_Bursts = 0;
            burst.Played += OnBurst;
            bool flashed = false;
            while (director.time < 4.8 && Time.realtimeSinceStartup < until)
            {
                yield return null;
                flashed |= Named("Flash Player").IsPlaying;
            }
            burst.Played -= OnBurst;
            int bursts = s_Bursts;
            SampleState.SetAfter(false);
            yield return Frames(3);
            float restart = (float)director.time;
            while (director.time < 1.6 && Time.realtimeSinceStartup < until)
                yield return null;
            Vector3 before = cam.localPosition;
            Vector3 hero = Named("Hero").transform.localScale;
            SampleState.SetAfter(true);
            yield return Frames(3);
            var gc = Unity.Profiling.ProfilerRecorder.StartNew(Unity.Profiling.ProfilerCategory.Memory, "GC Allocated In Frame");
            yield return Real(0.5f);
            var qb = new long[120];
            var bb = new long[120];
            var cb = new long[120];
            ((Behaviour)lab).enabled = false;
            for (int f = 0; f < 120; f++) { yield return null; qb[f] = gc.LastValue; }
            ((Behaviour)lab).enabled = true;
            for (int f = 0; f < 120; f++) { yield return null; bb[f] = gc.LastValue; }
            string s = "";
            for (int f = 0; f < 120; f++) { s = f.ToString() + s.Length; yield return null; cb[f] = gc.LastValue; }
            gc.Dispose();
            long q50 = Median(qb), b50 = Median(bb), c50 = Median(cb);
            (int e1, int w1) = Counts();
            Assert.That(((at - rest).magnitude > 1e-3f, (again - at).magnitude < 1e-5f, paused), Is.EqualTo((true, true, true)), $"rest {rest:F4} · at 1.6 {at:F4} · again {again:F4}");
            Assert.That((flashed, bursts), Is.EqualTo((true, 1)));
            Assert.That((restart < 0.5f, (before - rest).magnitude < 1e-5f, (hero - Vector3.one).magnitude < 1e-5f), Is.EqualTo((true, true, true)), $"restart {restart:F2} · camera {before:F4} · hero {hero:F4}");
            Assert.That(c50 - q50, Is.GreaterThan(40), $"the positive control (a dead counter) — medians quiet {q50} · control {c50}");
            Assert.That(b50 - q50, Is.LessThan(40), $"the lab's frames against the quiet ones — medians quiet {q50} · lab {b50} · control {c50}");
            Assert.That((e1 - e0, w1 - w0), Is.EqualTo((0, 0)));
        }

        // ---- S12's scene: its name unique in the bundles · the three Hit presets · five rows · Navigation None

        [Test]
        public void VQ14_TheScene()
        {
            string scene = Scene("Accessibility");
            int same = Catalogs().SelectMany(c => c.Cards).Count(c => Path.GetFileNameWithoutExtension(AssetDatabase.GUIDToAssetPath(c.SceneGuid)) == "Accessibility");
            EditorSceneManager.OpenScene(scene, OpenSceneMode.Single);
            Component lab = Resources.FindObjectsOfTypeAll<MonoBehaviour>().First(m => m.GetType().Name == "AccessibilityLab" && m.gameObject.scene.isLoaded);
            string[] presets = new[] { "m_Light", "m_Heavy", "m_Critical" }.Select(f => F<FeedbackPlayer>(lab, f)).Select(p => F<UnityEngine.Object>(p, "m_Preset")?.name ?? "").ToArray();
            int rows = F<Text[]>(lab, "m_Values").Length;
            bool none = Resources.FindObjectsOfTypeAll<Selectable>().Where(s => s.gameObject.scene.isLoaded).All(s => s.navigation.mode == Navigation.Mode.None);
            Assert.That(presets, Is.EqualTo(new[] { "Hit Light", "Hit Heavy", "Hit Critical" }));
            Assert.That((same, rows, none, F<FeedbackPlayer>(lab, "m_Rumble").GetComponent<FeedbackPlayer>() != null), Is.EqualTo((1, 5, true, true)));
        }

        // ---- the hub: every card inside the canvas at 16:9 · 21:9 · 4:3 (CanvasScaler 1920 × 1080 · match 0.5) · no two overlap

        [UnityTest]
        public IEnumerator VQ15_TheHubHoldsTheCards()
        {
            yield return new EnterPlayMode();
            EditorSceneManager.LoadSceneInPlayMode(SampleKitPaths.HubScene, new LoadSceneParameters(LoadSceneMode.Single));
            yield return Frames(20);
            var buttons = F<List<Button>>(SampleHub.Instance, "m_Buttons");
            var rects = buttons.Select(b => (RectTransform)b.transform).Select(r => new Rect(r.anchoredPosition - r.sizeDelta * 0.5f, r.sizeDelta)).ToList();
            var owner = Enumerable.Range(0, rects.Count).ToList();
            // the editor's Open in Editor button sits in its card's row
            for (int k = 0; k < buttons.Count; k++)
                foreach (RectTransform child in buttons[k].transform)
                    if (child.name == "Open in Editor")
                    {
                        var row = (RectTransform)buttons[k].transform;
                        rects.Add(new Rect(row.anchoredPosition + child.anchoredPosition - child.sizeDelta * 0.5f, child.sizeDelta));
                        owner.Add(k);
                    }
            var problems = new List<string>();
            foreach ((float w, float h) in new[] { (1920f, 1080f), (2520f, 1080f), (1440f, 1080f) })
            {
                float s = Mathf.Pow(2f, Mathf.Lerp(Mathf.Log(w / 1920f, 2f), Mathf.Log(h / 1080f, 2f), 0.5f));
                float hw = w / s * 0.5f, hh = h / s * 0.5f;
                // the HUD's panel (top left 560 × 330 at 12 · 12) takes the clicks it covers
                var hud = new Rect(-hw + 12f, hh - 12f - 330f, 560f, 330f);
                for (int i = 0; i < rects.Count; i++)
                {
                    if (rects[i].xMin < -hw || rects[i].xMax > hw || rects[i].yMin < -hh || rects[i].yMax > hh)
                        problems.Add($"{w}×{h}: card {i} {rects[i]}");
                    if (rects[i].Overlaps(hud))
                        problems.Add($"{w}×{h}: card {i} under the HUD");
                }
            }
            for (int i = 0; i < rects.Count; i++)
                for (int j = i + 1; j < rects.Count; j++)
                    if (owner[i] != owner[j] && rects[i].Overlaps(rects[j]))
                        problems.Add($"cards {i} · {j} overlap");
            Assert.That(buttons.Count, Is.GreaterThan(12), "the dev project's cards");
            Assert.That(problems, Is.Empty);
        }
    }
}
