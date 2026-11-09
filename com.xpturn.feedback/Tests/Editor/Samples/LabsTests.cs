using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using xpTURN.Feedback.Samples;
using static xpTURN.Feedback.Tests.Editor.Samples.SampleKitTestSupport;

namespace xpTURN.Feedback.Tests.Editor.Samples
{
    // the Sample map's Option1 ~ 4 · the layer gate · S3 · S4 · S6 · the cards' keys. The bundle's types are reached by name; a
    // play-mode iterator captures no local in a lambda (the reload)
    public sealed class LabsTests
    {
        const BindingFlags All = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        static readonly Dictionary<FeedbackPlayer, int> s_Plays = new Dictionary<FeedbackPlayer, int>();

        static string Scene(string name)
        {
            string path = AssetDatabase.FindAssets("t:Scene").Select(AssetDatabase.GUIDToAssetPath).FirstOrDefault(p => System.IO.Path.GetFileNameWithoutExtension(p) == name);
            if (path == null)
                Assert.Ignore("no Game Feel Labs bundle in this project");
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

        static Component Lab(string type) => Resources.FindObjectsOfTypeAll<MonoBehaviour>().First(m => m != null && m.GetType().Name == type && m.gameObject.scene.isLoaded);

        static T F<T>(object o, string field) => (T)o.GetType().GetField(field, All).GetValue(o);

        static void OnPlayed(FeedbackPlayer p, PlaybackHandle h) => s_Plays[p] = Plays(p) + 1;

        static int Plays(FeedbackPlayer p) => p != null && s_Plays.TryGetValue(p, out int n) ? n : 0;

        static void Count(params FeedbackPlayer[] players)
        {
            s_Plays.Clear();
            foreach (FeedbackPlayer p in players)
                if (p != null)
                {
                    p.Played -= OnPlayed;
                    p.Played += OnPlayed;
                }
        }

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

        // ---- the Sample map's Option1 ~ 4 · the layer gate

        [Test]
        public void LQ01_OptionActions()
        {
            var asset = AssetDatabase.LoadAssetAtPath<InputActionAsset>(ActionsPath);
            string[] keys = { "<Keyboard>/z", "<Keyboard>/x", "<Keyboard>/c", "<Keyboard>/v" };
            string[] pads = { "<Gamepad>/dpad/left", "<Gamepad>/dpad/right", "<Gamepad>/dpad/down", "<Gamepad>/dpad/up" };
            var problems = new List<string>();
            for (int i = 0; i < 4; i++)
            {
                InputAction a = asset.FindAction("Sample/Option" + (i + 1));
                string[] paths = a == null ? new string[0] : a.bindings.Select(b => b.path).ToArray();
                if (a == null || a.type != InputActionType.Button || !paths.Contains(keys[i]) || !paths.Contains(pads[i]) || paths.Length != 2)
                    problems.Add("Option" + (i + 1) + ": " + string.Join(",", paths));
            }
            Assert.That(problems, Is.Empty);
        }

        [Test]
        public void LQ02_TheLayerGate()
        {
            SampleState.Restore();
            bool allOn = Enumerable.Range(0, SampleLayers.Count).All(SampleLayers.IsOn);
            SampleLayers.Set(1, false);
            bool oneOff = !SampleLayers.IsOn(1) && !SampleLayers.Enabled(1) && SampleLayers.IsOn(0);
            SampleState.SetAfter(false);
            bool beforeOff = !SampleLayers.IsOn(0) && SampleLayers.Enabled(0);
            SampleLayers.Set(99, false);
            bool outside = !SampleLayers.IsOn(99) && !SampleLayers.Enabled(-1);
            SampleState.Restore();
            bool restored = SampleLayers.IsOn(1) && SampleState.After;
            Assert.That((allOn, oneOff, beforeOff, outside, restored), Is.EqualTo((true, true, true, true, true)));
        }

        // ---- S3

        [UnityTest]
        public IEnumerator LQ03_BeforeAfterLayers()
        {
            yield return new EnterPlayMode();
            EditorSceneManager.LoadSceneInPlayMode(Scene("BeforeAfter"), new LoadSceneParameters(LoadSceneMode.Single));
            var was = Focus();
            var kb = InputSystem.AddDevice<Keyboard>();
            yield return Frames(20);
            (int e0, int w0) = Counts();
            Component lab = Lab("BeforeAfterLab");
            FeedbackPlayer[] layer = { F<FeedbackPlayer>(lab, "m_JumpSquash"), F<FeedbackPlayer>(lab, "m_LandSquash"), F<FeedbackPlayer>(lab, "m_JumpDust"), F<FeedbackPlayer>(lab, "m_LandDust"), F<FeedbackPlayer>(lab, "m_LandShake"), F<FeedbackPlayer>(lab, "m_TrailOn") };
            Transform before = F<Transform>(lab, "m_BeforeBody"), after = F<Transform>(lab, "m_AfterBody");
            Vector3 ob = F<Vector3>(lab, "m_BeforeOrigin"), oa = F<Vector3>(lab, "m_AfterOrigin");
            int beforePlayers = before.GetComponentsInChildren<FeedbackPlayer>(true).Length;
            var trail = F<FeedbackPlayer>(lab, "m_TrailOn").GetComponent<TrailRenderer>();
            float maxDiff = 0f;
            int[] all = new int[6], dustOff = new int[6], tabOff = new int[6];
            bool trailEndedOff = true;
            // a jump in place (no platform above the start): every layer once
            for (int round = 0; round < 3; round++)
            {
                if (round == 1)
                    lab.GetType().GetMethod("Toggle").Invoke(lab, new object[] { 1 });
                if (round == 2)
                {
                    lab.GetType().GetMethod("Toggle").Invoke(lab, new object[] { 1 });
                    SampleState.SetAfter(false);
                }
                Count(layer);
                InputSystem.QueueStateEvent(kb, new KeyboardState(Key.Space));
                yield return Frames(3);
                InputSystem.QueueStateEvent(kb, new KeyboardState());
                float until = Time.realtimeSinceStartup + 2f;
                while (Time.realtimeSinceStartup < until)
                {
                    maxDiff = Mathf.Max(maxDiff, ((before.position - ob) - (after.position - oa)).magnitude);
                    yield return null;
                }
                InputSystem.QueueStateEvent(kb, new KeyboardState());
                yield return Frames(3);
                int[] dst = round == 0 ? all : round == 1 ? dustOff : tabOff;
                for (int i = 0; i < 6; i++)
                    dst[i] = Plays(layer[i]);
                trailEndedOff &= !trail.emitting;
            }
            SampleState.SetAfter(true);
            // the trail layer turned off in the air: its 'off' plays at once (a finished Set would stay)
            InputSystem.QueueStateEvent(kb, new KeyboardState(Key.Space));
            yield return Frames(3);
            InputSystem.QueueStateEvent(kb, new KeyboardState());
            yield return Real(0.1f);
            bool onInAir = trail.emitting;
            lab.GetType().GetMethod("Toggle").Invoke(lab, new object[] { 3 });
            yield return Frames(2);
            bool offInAir = !trail.emitting;
            yield return Real(1.5f);
            lab.GetType().GetMethod("Toggle").Invoke(lab, new object[] { 3 });
            // Tab (Before) in the air: the gate stops the next plays, the landing still ends the trail
            InputSystem.QueueStateEvent(kb, new KeyboardState(Key.Space));
            yield return Frames(3);
            InputSystem.QueueStateEvent(kb, new KeyboardState());
            yield return Real(0.1f);
            bool onBeforeTab = trail.emitting;
            SampleState.SetAfter(false);
            yield return Real(1.5f);
            bool offAfterLanding = !trail.emitting;
            SampleState.SetAfter(true);
            (int e1, int w1) = Counts();
            InputSystem.RemoveDevice(kb);
            Unfocus(was);
            yield return new ExitPlayMode();
            Assert.That((onInAir, offInAir), Is.EqualTo((true, true)), "the trail layer off in the air ends the trail");
            Assert.That((onBeforeTab, offAfterLanding), Is.EqualTo((true, true)), "Tab in the air — the landing ends the trail");
            Assert.That(maxDiff, Is.EqualTo(0f), "the two bodies, track-local");
            Assert.That(beforePlayers, Is.EqualTo(0), "the Before side has no player");
            Assert.That(all, Is.EqualTo(new[] { 1, 1, 1, 1, 1, 1 }), "all layers: jump squash · land squash · jump dust · land dust · shake · trail on");
            Assert.That(dustOff, Is.EqualTo(new[] { 1, 1, 0, 0, 1, 1 }), "dust off");
            Assert.That(tabOff, Is.EqualTo(new[] { 0, 0, 0, 0, 0, 0 }), "Tab: every layer off, dust and trail too");
            Assert.That(trailEndedOff, Is.True, "the trail off after each landing");
            Assert.That((e1 - e0, w1 - w0), Is.EqualTo((0, 0)), "Console: " + string.Join(" / ", RowsFrom(0).Take(3)));
        }

        [UnityTest]
        public IEnumerator LQ04_BeforeAfterCameras()
        {
            yield return new EnterPlayMode();
            EditorSceneManager.LoadSceneInPlayMode(Scene("BeforeAfter"), new LoadSceneParameters(LoadSceneMode.Single));
            yield return Frames(20);
            Component lab = Lab("BeforeAfterLab");
            Transform bcam = F<Transform>(lab, "m_BeforeRig").GetChild(0), acam = F<Transform>(lab, "m_AfterRig").GetChild(0);
            Vector3 b0 = bcam.localPosition, a0 = acam.localPosition;
            F<FeedbackPlayer>(lab, "m_LandShake").Play();
            float bMax = 0f, aMax = 0f;
            float until = Time.realtimeSinceStartup + 0.4f;
            while (Time.realtimeSinceStartup < until)
            {
                bMax = Mathf.Max(bMax, (bcam.localPosition - b0).magnitude);
                aMax = Mathf.Max(aMax, (acam.localPosition - a0).magnitude);
                yield return null;
            }
            string main = Camera.main != null ? Camera.main.name : "none";
            int listeners = Resources.FindObjectsOfTypeAll<AudioListener>().Count(l => l.isActiveAndEnabled && l.gameObject.scene.isLoaded);
            string acamName = acam.name;
            yield return new ExitPlayMode();
            Assert.That((main, listeners), Is.EqualTo((acamName, 1)), "the After camera is the main one · one listener");
            Assert.That(bMax, Is.EqualTo(0f), "the Before camera does not shake");
            Assert.That(aMax, Is.GreaterThan(0f), "the After camera shakes");
        }

        // ---- S4

        static int Damage(int tier) => tier == 2 ? 120 : 37;

        static List<string> NumberTexts()
        {
            Type tmp = Type.GetType("TMPro.TMP_Text, Unity.TextMeshPro");
            var list = new List<string>();
            if (tmp == null)
                return list;
            PropertyInfo text = tmp.GetProperty("text");
            foreach (UnityEngine.Object o in Resources.FindObjectsOfTypeAll(tmp))
            {
                var c = (Component)o;
                if (c != null && c.gameObject.activeInHierarchy && c.gameObject.scene.isLoaded)
                    list.Add((string)text.GetValue(c));
            }
            return list;
        }

        // the font size of the shown number with that text (0 if none)
        static float NumberSize(string s)
        {
            Type tmp = Type.GetType("TMPro.TMP_Text, Unity.TextMeshPro");
            if (tmp == null)
                return 0f;
            foreach (UnityEngine.Object o in Resources.FindObjectsOfTypeAll(tmp))
            {
                var c = (Component)o;
                if (c != null && c.gameObject.activeInHierarchy && c.gameObject.scene.isLoaded && (string)tmp.GetProperty("text").GetValue(c) == s)
                    return (float)tmp.GetProperty("fontSize").GetValue(c);
            }
            return 0f;
        }

        [UnityTest]
        public IEnumerator LQ05_CombatHit()
        {
            yield return new EnterPlayMode();
            Type labType = Type.GetType("xpTURN.Feedback.Samples.GameFeelLabs.CombatHitLab, xpTURN.Feedback.Samples.GameFeelLabs.CombatHit");
            if (labType == null)
                Assert.Ignore("no Game Feel Labs bundle in this project");
            labType.GetField("s_DamageOf", All).SetValue(null, (Func<int, int>)Damage);
            EditorSceneManager.LoadSceneInPlayMode(Scene("CombatHit"), new LoadSceneParameters(LoadSceneMode.Single));
            yield return Frames(20);
            (int e0, int w0) = Counts();
            Component lab = Lab("CombatHitLab");
            FeedbackPlayer[] hits = { F<FeedbackPlayer>(lab, "m_Light"), F<FeedbackPlayer>(lab, "m_Heavy"), F<FeedbackPlayer>(lab, "m_Critical") };
            Count(hits);
            Transform body = hits[2].transform;
            MethodInfo attack = lab.GetType().GetMethod("Attack");
            attack.Invoke(lab, new object[] { 0 });
            yield return Real(0.2f);
            List<string> afterLight = NumberTexts();
            float lightSize = NumberSize(Damage(0).ToString());
            yield return Real(1.2f);
            float z0 = body.position.z, firstMove = 0f;
            attack.Invoke(lab, new object[] { 2 });
            float until = Time.realtimeSinceStartup + 0.6f;
            while (Time.realtimeSinceStartup < until)
            {
                // the punch swings both ways — its first clear move tells the side
                if (firstMove == 0f && Mathf.Abs(body.position.z - z0) > 0.05f)
                    firstMove = body.position.z - z0;
                yield return null;
            }
            List<string> afterCritical = NumberTexts();
            float criticalSize = NumberSize(Damage(2).ToString());
            attack.Invoke(lab, new object[] { 1 });
            yield return Real(0.3f);
            int[] plays = { Plays(hits[0]), Plays(hits[1]), Plays(hits[2]) };
            yield return Real(1.5f);
            (int e1, int w1) = Counts();
            labType.GetField("s_DamageOf", All).SetValue(null, null);
            yield return new ExitPlayMode();
            Assert.That(plays, Is.EqualTo(new[] { 1, 1, 1 }), "a tier plays its preset");
            // the expectation from the seam's damage — the number reads intensity × 100
            Assert.That(afterLight, Is.EquivalentTo(new[] { Damage(0).ToString() }), "the light hit's number");
            Assert.That(afterCritical, Has.Member(Damage(2).ToString()), "the critical's number");
            Assert.That(criticalSize, Is.GreaterThan(lightSize * 1.2f), "the critical's number is the bigger style");
            Assert.That(firstMove, Is.GreaterThan(0f), "the critical first pushes the dummy away from the camera (+z)");
            Assert.That((e1 - e0, w1 - w0), Is.EqualTo((0, 0)), "Console: " + string.Join(" / ", RowsFrom(0).Take(3)));
        }

        [UnityTest]
        public IEnumerator LQ06_CombatHitWithoutTmp()
        {
            yield return new EnterPlayMode();
            Func<bool> probe = SampleTmp.s_Probe;
            SampleTmp.s_Probe = () => false;
            SampleTmp.Reset();
            EditorSceneManager.LoadSceneInPlayMode(Scene("CombatHit"), new LoadSceneParameters(LoadSceneMode.Single));
            yield return Frames(20);
            (int e0, int w0) = Counts();
            Component lab = Lab("CombatHitLab");
            FeedbackPlayer light = F<FeedbackPlayer>(lab, "m_Light");
            Count(light);
            lab.GetType().GetMethod("Attack").Invoke(lab, new object[] { 0 });
            yield return Real(0.5f);
            int plays = Plays(light);
            bool numbers = F<GameObject>(lab, "m_Numbers") != null;
            bool notice = F<GameObject>(lab, "m_Notice") != null;
            (int e1, int w1) = Counts();
            SampleTmp.s_Probe = probe;
            SampleTmp.Reset();
            yield return new ExitPlayMode();
            Assert.That((plays, numbers, notice), Is.EqualTo((1, false, true)), "the hit plays · no numbers · a notice that stays");
            Assert.That((e1 - e0, w1 - w0), Is.EqualTo((0, 0)), "Console: " + string.Join(" / ", RowsFrom(0).Take(3)));
        }

        // ---- S6

        [UnityTest]
        public IEnumerator LQ07_SpringsFollow()
        {
            yield return new EnterPlayMode();
            EditorSceneManager.LoadSceneInPlayMode(Scene("SpringPlayground"), new LoadSceneParameters(LoadSceneMode.Single));
            yield return Frames(20);
            (int e0, int w0) = Counts();
            Component lab = Lab("SpringPlayground");
            Transform cube = F<Transform>(lab, "m_Cube");
            Transform driver = F<MotionDriver>(lab, "m_Driver").transform;
            float lift = F<float>(lab, "m_CubeLift"), dlift = F<float>(lab, "m_DriverLift");
            MethodInfo moveTo = lab.GetType().GetMethod("MoveTo");
            // the true values: springs the test steps itself with the frame's dt (public structs — the exact solution)
            var cubeTruth = new SpringVector3(cube.position, 6f, 0.4f);
            var driverTruth = new SpringVector3(driver.position, 6f, 0.4f);
            Vector2Int[] cells = { new Vector2Int(2, 0), new Vector2Int(2, 2), new Vector2Int(-3, 1), new Vector2Int(0, -2) };
            float cubeErr = 0f, driverErr = 0f;
            for (int f = 0; f < 160; f++)
            {
                if (f % 40 == 0)
                {
                    Vector2Int c = cells[f / 40];
                    moveTo.Invoke(lab, new object[] { c });
                    cubeTruth.MoveTo(new Vector3(c.x, lift, c.y));
                    driverTruth.MoveTo(new Vector3(c.x, dlift, c.y));
                }
                yield return null;
                cubeTruth.Step(Time.deltaTime);
                driverTruth.Step(Time.deltaTime);
                cubeErr = Mathf.Max(cubeErr, (cube.position - cubeTruth.Value).magnitude);
                driverErr = Mathf.Max(driverErr, (driver.position - driverTruth.Value).magnitude);
            }
            lab.GetType().GetMethod("SetParameters").Invoke(lab, new object[] { 2, 9 });
            float freq = F<SpringVector3>(lab, "m_Spring3").Frequency, damp = F<SpringVector3>(lab, "m_Spring3").Damping;
            float freq1 = F<SpringFloat>(lab, "m_Spring1").Frequency, damp2 = F<SpringVector2>(lab, "m_Spring2").Damping;
            (int e1, int w1) = Counts();
            yield return new ExitPlayMode();
            Assert.That((cubeErr, driverErr), Is.EqualTo((0f, 0f)), "the code spring and the MotionDriver follow the true springs");
            // the slider table: frequency 1 + 0.5 i · damping 0.05 (i + 1)
            Assert.That((freq, freq1, damp, damp2), Is.EqualTo((2f, 2f, 0.5f, 0.5f)).Within(1e-5f), "a slider sets every code spring");
            Assert.That((e1 - e0, w1 - w0), Is.EqualTo((0, 0)), "Console: " + string.Join(" / ", RowsFrom(0).Take(3)));
        }

        [UnityTest]
        public IEnumerator LQ08_SpringMotions()
        {
            yield return new EnterPlayMode();
            EditorSceneManager.LoadSceneInPlayMode(Scene("SpringPlayground"), new LoadSceneParameters(LoadSceneMode.Single));
            var was = Focus();
            var kb = InputSystem.AddDevice<Keyboard>();
            yield return Frames(20);
            (int e0, int w0) = Counts();
            Component lab = Lab("SpringPlayground");
            FeedbackPlayer bump = F<FeedbackPlayer>(lab, "m_Bump");
            Transform bcube = bump.transform, mcube = F<FeedbackPlayer>(lab, "m_MoveUp").transform;
            Vector3 s0 = bcube.localScale, last = s0;
            float peak = 0f, jump = 0f;
            for (int i = 0; i < 3; i++)
            {
                bump.Play();
                float next = Time.realtimeSinceStartup + 0.1f;
                while (Time.realtimeSinceStartup < next)
                {
                    peak = Mathf.Max(peak, (bcube.localScale - s0).magnitude);
                    // a restart snaps the scale back — overlapped bumps add smoothly: the change over the frame's time stays a
                    // spring's speed (the bump's 4 · √3 and what is left), a snap is far faster
                    if (Time.deltaTime > 0f)
                        jump = Mathf.Max(jump, (bcube.localScale - last).magnitude / Time.deltaTime);
                    last = bcube.localScale;
                    yield return null;
                }
            }
            yield return Real(1.5f);
            float rest = (bcube.localScale - s0).magnitude;
            float y0 = mcube.position.y;
            InputSystem.QueueStateEvent(kb, new KeyboardState(Key.Digit3));
            yield return Frames(3);
            InputSystem.QueueStateEvent(kb, new KeyboardState());
            yield return Real(0.25f);
            InputSystem.QueueStateEvent(kb, new KeyboardState(Key.Digit3));
            yield return Frames(3);
            InputSystem.QueueStateEvent(kb, new KeyboardState());
            float upPeak = 0f;
            float until = Time.realtimeSinceStartup + 2f;
            while (Time.realtimeSinceStartup < until)
            {
                upPeak = Mathf.Max(upPeak, mcube.position.y - y0);
                yield return null;
            }
            float back = mcube.position.y - y0;
            (int e1, int w1) = Counts();
            InputSystem.RemoveDevice(kb);
            Unfocus(was);
            yield return new ExitPlayMode();
            Assert.That(peak, Is.GreaterThan(0.02f), "the bumps move the scale");
            Assert.That(jump, Is.LessThan(20f), "repeated bumps never snap (Overlap) — the scale's fastest speed");
            Assert.That(rest, Is.LessThan(1e-3f), "the scale settles back");
            Assert.That(upPeak, Is.GreaterThan(0.2f), "MoveTo goes up");
            Assert.That(Mathf.Abs(back), Is.LessThan(1e-3f), "the second MoveTo brings it back — one write at a time");
            Assert.That((e1 - e0, w1 - w0), Is.EqualTo((0, 0)), "Console: " + string.Join(" / ", RowsFrom(0).Take(3)));
        }

        // ---- a pointer's press only is refused over the UI · only a pointer drags (S6)

        static Vector2 ScreenOf(RectTransform rt)
        {
            var corners = new Vector3[4];
            rt.GetWorldCorners(corners);
            return RectTransformUtility.WorldToScreenPoint(null, (corners[0] + corners[2]) * 0.5f);
        }

        [UnityTest]
        public IEnumerator LQ12_PointerOverUi()
        {
            yield return new EnterPlayMode();
            EditorSceneManager.LoadSceneInPlayMode(Scene("BeforeAfter"), new LoadSceneParameters(LoadSceneMode.Single));
            var was = Focus();
            var kb = InputSystem.AddDevice<Keyboard>();
            var mouse = InputSystem.AddDevice<Mouse>();
            yield return Frames(20);
            (int e0, int w0) = Counts();
            Component lab = Lab("BeforeAfterLab");
            Transform after = F<Transform>(lab, "m_AfterBody");
            float y0 = after.position.y;
            var toggle = (RectTransform)Resources.FindObjectsOfTypeAll<RectTransform>().First(r => r.name == "Camera" && r.gameObject.scene.isLoaded);
            Vector2 over = ScreenOf(toggle);
            InputSystem.QueueStateEvent(mouse, new MouseState { position = over });
            yield return Frames(5);
            bool overUi = SampleInput.PointerOverUI;
            InputSystem.QueueStateEvent(kb, new KeyboardState(Key.Space));
            yield return Frames(3);
            InputSystem.QueueStateEvent(kb, new KeyboardState());
            float top = y0;
            float until = Time.realtimeSinceStartup + 0.4f;
            while (Time.realtimeSinceStartup < until)
            {
                top = Mathf.Max(top, after.position.y);
                yield return null;
            }
            yield return Real(1.2f);
            bool layerBefore = SampleLayers.Enabled(2);
            float y1 = after.position.y, clickTop = y1;
            InputSystem.QueueStateEvent(mouse, new MouseState { position = over }.WithButton(MouseButton.Left));
            yield return Frames(3);
            InputSystem.QueueStateEvent(mouse, new MouseState { position = over });
            until = Time.realtimeSinceStartup + 0.4f;
            while (Time.realtimeSinceStartup < until)
            {
                clickTop = Mathf.Max(clickTop, after.position.y);
                yield return null;
            }
            bool layerAfter = SampleLayers.Enabled(2);
            SampleLayers.Set(2, true);
            (int e1, int w1) = Counts();
            InputSystem.RemoveDevice(kb);
            InputSystem.RemoveDevice(mouse);
            Unfocus(was);
            yield return new ExitPlayMode();
            Assert.That(overUi, Is.True, "the pointer is over the camera layer's button");
            Assert.That(top - y0, Is.GreaterThan(0.5f), "Space jumps with the pointer over the UI");
            Assert.That(clickTop - y1, Is.LessThan(0.01f), "a click on the button does not jump");
            Assert.That((layerBefore, layerAfter), Is.EqualTo((true, false)), "the click toggles the layer");
            Assert.That((e1 - e0, w1 - w0), Is.EqualTo((0, 0)), "Console: " + string.Join(" / ", RowsFrom(0).Take(3)));
        }

        [UnityTest]
        public IEnumerator LQ13_OnlyAPointerDrags()
        {
            yield return new EnterPlayMode();
            EditorSceneManager.LoadSceneInPlayMode(Scene("SpringPlayground"), new LoadSceneParameters(LoadSceneMode.Single));
            var was = Focus();
            var kb = InputSystem.AddDevice<Keyboard>();
            var mouse = InputSystem.AddDevice<Mouse>();
            yield return Frames(20);
            (int e0, int w0) = Counts();
            Component lab = Lab("SpringPlayground");
            PropertyInfo goal = lab.GetType().GetProperty("Goal");
            Vector2 at = Camera.main.WorldToScreenPoint(new Vector3(2f, 0f, 1f));
            InputSystem.QueueStateEvent(mouse, new MouseState { position = at });
            yield return Frames(5);
            InputSystem.QueueStateEvent(kb, new KeyboardState(Key.Space));
            yield return Frames(10);
            InputSystem.QueueStateEvent(kb, new KeyboardState());
            yield return Frames(3);
            var bySpace = (Vector3)goal.GetValue(lab);
            InputSystem.QueueStateEvent(mouse, new MouseState { position = at }.WithButton(MouseButton.Left));
            yield return Frames(10);
            InputSystem.QueueStateEvent(mouse, new MouseState { position = at });
            yield return Frames(3);
            var byMouse = (Vector3)goal.GetValue(lab);
            (int e1, int w1) = Counts();
            InputSystem.RemoveDevice(kb);
            InputSystem.RemoveDevice(mouse);
            Unfocus(was);
            yield return new ExitPlayMode();
            Assert.That(bySpace.magnitude, Is.LessThan(1e-4f), "Space does not drag the target");
            Assert.That((byMouse - new Vector3(2f, 0f, 1f)).magnitude, Is.LessThan(0.05f), "the mouse drags it to the floor point");
            Assert.That((e1 - e0, w1 - w0), Is.EqualTo((0, 0)), "Console: " + string.Join(" / ", RowsFrom(0).Take(3)));
        }

        // ---- the cards' keys are bound

        static readonly Dictionary<string, string> s_KeyPaths = new Dictionary<string, string>
        {
            { "Space", "<Keyboard>/space" }, { "1", "<Keyboard>/1" }, { "2", "<Keyboard>/2" }, { "3", "<Keyboard>/3" },
            { "Z", "<Keyboard>/z" }, { "X", "<Keyboard>/x" }, { "C", "<Keyboard>/c" }, { "V", "<Keyboard>/v" },
            { "A", "<Keyboard>/a" }, { "D", "<Keyboard>/d" }, { "←", "<Keyboard>/leftArrow" }, { "→", "<Keyboard>/rightArrow" },
        };

        [Test]
        public void LQ09_CardKeysAreBound()
        {
            var asset = AssetDatabase.LoadAssetAtPath<InputActionAsset>(ActionsPath);
            var bound = new HashSet<string>(asset.FindActionMap("Sample", true).bindings.Select(b => b.path));
            var problems = new List<string>();
            SampleCard[] cards = Catalogs().Where(c => c.Bundle == "Game Feel Labs").SelectMany(c => c.Cards).Where(c => c != null).ToArray();
            if (cards.Length == 0)
                Assert.Ignore("no Game Feel Labs bundle in this project");
            foreach (SampleCard card in cards)
                foreach (string token in (card.Controls ?? "").Split(new[] { '·', '—', ',', '/', ' ' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    string k = token.Trim();
                    string path = s_KeyPaths.TryGetValue(k, out string known) ? known : k.Length == 1 && char.IsLetterOrDigit(k[0]) ? "<Keyboard>/" + char.ToLowerInvariant(k[0]) : null;
                    if (path != null && !bound.Contains(path))
                        problems.Add(card.Title + ": " + k);
                }
            Assert.That(problems, Is.Empty);
        }
    }
}
