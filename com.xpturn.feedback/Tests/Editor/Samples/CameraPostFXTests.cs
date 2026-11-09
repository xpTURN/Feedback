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
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using xpTURN.Feedback.Samples;
using static xpTURN.Feedback.Tests.Editor.Samples.SampleKitTestSupport;
using Object = UnityEngine.Object;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace xpTURN.Feedback.Tests.Editor.Samples
{
    // the bundle's cards · S5 Camera Shake Lab · S5 Cinemachine Shake Lab · S10 PostFX Lab · the pad alone. The bundle's types,
    // Cinemachine's and the volumes are reached by name; a play-mode iterator gets its objects again after the play entry and
    // captures no local in a lambda (the reload empties them)
    public sealed class CameraPostFXTests
    {
        const BindingFlags All = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        static readonly Dictionary<FeedbackPlayer, int> s_Plays = new Dictionary<FeedbackPlayer, int>();
        static readonly string[] s_Titles = { "Before / After", "Combat Hit Lab", "Camera Shake Lab", "Cinemachine Shake Lab", "Spring Playground", "PostFX Lab" };

        static string Scene(string name)
        {
            string path = AssetDatabase.FindAssets("t:Scene").Select(AssetDatabase.GUIDToAssetPath).FirstOrDefault(p => Path.GetFileNameWithoutExtension(p) == name);
            if (path == null)
                Assert.Ignore("no " + name + " in this project");
            return path;
        }

        static void Load(string name) => EditorSceneManager.LoadSceneInPlayMode(Scene(name), new LoadSceneParameters(LoadSceneMode.Single));

        static bool Cinemachine() => Type.GetType("Unity.Cinemachine.CinemachineBrain, Unity.Cinemachine") != null;

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

        static object Call(object o, string method, params object[] args) => o.GetType().GetMethod(method, All).Invoke(o, args);

        static T P<T>(object o, string property) => (T)o.GetType().GetProperty(property, All).GetValue(o);

        static Transform Obj(string name) => Resources.FindObjectsOfTypeAll<Transform>().First(t => t.name == name && t.gameObject.scene.isLoaded);

        static int Listeners() => Resources.FindObjectsOfTypeAll<AudioListener>().Count(l => l.isActiveAndEnabled && l.gameObject.scene.isLoaded);

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

        // ---- volumes by name (the samples' test assembly doesn't reference the render pipeline)

        static readonly Type s_Volume = Type.GetType("UnityEngine.Rendering.Volume, Unity.RenderPipelines.Core.Runtime");

        static Behaviour[] Volumes() => Resources.FindObjectsOfTypeAll(s_Volume).Cast<Behaviour>().Where(v => v.gameObject.scene.isLoaded).ToArray();

        static float Priority(Behaviour v) => (float)s_Volume.GetField("priority").GetValue(v);

        static float Weight(Behaviour v) => (float)s_Volume.GetField("weight").GetValue(v);

        static Object Profile(Behaviour v) => (Object)s_Volume.GetField("sharedProfile").GetValue(v);

        static Behaviour[] Overlays() => Volumes().Where(v => Priority(v) >= 1000f).ToArray();

        static float MaxWeight(Behaviour[] vs) => vs.Length == 0 ? 0f : vs.Max(v => v != null && v.enabled ? Weight(v) : 0f);

        static Behaviour[] Lit(Behaviour[] vs) => vs.Where(v => v != null && v.enabled && Weight(v) > 0.001f).ToArray();

        // the volume's profile holds a component of that type (the overlay lends each item a volume of its own effect)
        static Behaviour[] Of(Behaviour[] vs, string component) => vs.Where(v => Profile(v) is ScriptableObject p && ((IEnumerable)p.GetType().GetField("components").GetValue(p)).Cast<object>().Any(c => c.GetType().Name == component)).ToArray();

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

        static IEnumerator Tap(Gamepad pad, GamepadButton button)
        {
            InputSystem.QueueStateEvent(pad, new GamepadState().WithButton(button));
            yield return Frames(3);
            InputSystem.QueueStateEvent(pad, new GamepadState());
            yield return Frames(3);
        }

        // ---- the bundle's cards

        // a volume profile's components by type name (null for an entry saved without its sub-asset)
        static string Kinds(string path)
        {
            var profile = AssetDatabase.LoadAssetAtPath<ScriptableObject>(path);
            if (profile == null)
                return "no profile";
            var names = new List<string>();
            foreach (object c in (IEnumerable)profile.GetType().GetField("components").GetValue(profile))
                names.Add(c is Object o && o != null ? o.GetType().Name : "null");
            return string.Join("+", names);
        }

        [Test]
        public void XQ01_BundleAndCards()
        {
            SampleCatalog catalog = Catalogs().FirstOrDefault(c => c.Bundle == "Game Feel Labs");
            if (catalog == null)
                Assert.Ignore("no Game Feel Labs bundle in this project");
            string[] titles = catalog.Cards.Select(c => c != null ? c.Title : "null").ToArray();
            PackageInfo package = PackageInfo.FindForAssetPath("Packages/com.xpturn.feedback");
            string json = File.ReadAllText(Path.Combine(package.resolvedPath, "package.json"));
            int at = json.IndexOf("\"displayName\": \"Game Feel Labs\"", StringComparison.Ordinal);
            string entry = at < 0 ? "" : json.Substring(at, Math.Min(400, json.Length - at));
            string[] asmdefs = AssetDatabase.FindAssets("t:AssemblyDefinitionAsset xpTURN.Feedback.Samples.GameFeelLabs").Select(AssetDatabase.GUIDToAssetPath).ToArray();
            string cm = asmdefs.Where(p => Path.GetFileNameWithoutExtension(p) == "xpTURN.Feedback.Samples.GameFeelLabs.CinemachineShake").Select(File.ReadAllText).FirstOrDefault() ?? "";
            int cmOthers = asmdefs.Where(p => !p.EndsWith("CinemachineShake.asmdef", StringComparison.Ordinal)).Count(p => File.ReadAllText(p).Contains("cinemachine"));
            bool cmRule = cm.Contains("\"name\": \"com.unity.cinemachine\", \"expression\": \"3.0.0\", \"define\": \"XPTURN_FEEDBACK_SAMPLES_CINEMACHINE\"") && cm.Contains("\"XPTURN_FEEDBACK_SAMPLES_CINEMACHINE\"]") && cm.Contains("\"Unity.Cinemachine\"");
            Assert.That(titles, Is.EqualTo(s_Titles), "the catalog's cards in their order");
            Assert.That((entry.Contains("S5"), entry.Contains("S10")), Is.EqualTo((true, true)), "the samples entry names S5 and S10: " + entry);
            Assert.That((cmRule, cmOthers), Is.EqualTo((true, 0)), "only the Cinemachine lab's assembly asks for Cinemachine 3 or later (no upper bound)");
            // S10's glow: an emissive (HDR) source for the bloom — URP recomputes the keyword from the GI flags on import (AnyEmissive),
            // so the flags must say emissive too or the keyword goes again
            var glow = AssetDatabase.LoadAssetAtPath<Material>(BundleRoot(catalog) + "/Art/Bright.mat");
            bool emissive = glow != null && glow.IsKeywordEnabled("_EMISSION") && (glow.globalIlluminationFlags & MaterialGlobalIlluminationFlags.AnyEmissive) != 0;
            Assert.That((emissive, glow != null && glow.GetColor("_EmissionColor").maxColorComponent > 1f), Is.EqualTo((true, true)), "the glow material emits above 1 (Bloom's source)");
            // S10's volume profiles hold their components (sub-assets — a profile saved without them is empty: Volume Weight · Blend show nothing)
            Assert.That((Kinds(BundleRoot(catalog) + "/PostFX/PostFXVolume.asset"), Kinds(BundleRoot(catalog) + "/PostFX/Night.asset"), Kinds(BundleRoot(catalog) + "/PostFX/Danger.asset")),
                Is.EqualTo(("Vignette+Tonemapping", "WhiteBalance+ColorAdjustments", "Vignette+ColorAdjustments")), "the scene · Night · Danger profiles");
            string gallery = AssetDatabase.FindAssets("t:Scene FeedbackGallery").Select(AssetDatabase.GUIDToAssetPath).FirstOrDefault();
            if (gallery == null)
                return; // Game Feel Labs without Basics — the gallery's half has nothing to look at
            string text = string.Join(" ", File.ReadAllLines(gallery).Select(l => l.Trim()));
            Assert.That(text, Does.Contain("see " + s_Titles[3] + " in Game Feel Labs (needs Cinemachine 3 or later)"), "the gallery's Cinemachine tile points to the card");
        }

        // ---- S5 Camera Shake Lab

        static Vector3 Rest(string camera) => Obj(camera).position;

        static IEnumerator Window(Transform a, Vector3 ra, Transform b, Vector3 rb, float seconds, float[] max)
        {
            max[0] = max[1] = 0f;
            float until = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < until)
            {
                yield return null;
                max[0] = Mathf.Max(max[0], (a.position - ra).magnitude);
                max[1] = Mathf.Max(max[1], (b.position - rb).magnitude);
            }
        }

        [UnityTest]
        public IEnumerator XQ02_SplitChannels()
        {
            yield return new EnterPlayMode();
            Load("CameraShakeLab");
            yield return Frames(20);
            (int e0, int w0) = Counts();
            Component lab = Lab("CameraShakeLab");
            Transform s1 = Obj("Screen 1"), s2 = Obj("Screen 2");
            Vector3 r1 = s1.position, r2 = s2.position;
            var m = new float[2];
            Call(lab, "Shake", 1);
            yield return Window(s1, r1, s2, r2, 0.4f, m);
            float[] shake2 = { m[0], m[1] };
            Call(lab, "Shake", 0);
            yield return Window(s1, r1, s2, r2, 0.4f, m);
            float[] shake1 = { m[0], m[1] };
            Call(lab, "Preset", 0);
            yield return Window(s1, r1, s2, r2, 0.4f, m);
            float[] light = { m[0], m[1] };
            string main = Camera.main != null ? Camera.main.name : "none";
            int listeners = Listeners();
            (int e1, int w1) = Counts();
            yield return new ExitPlayMode();
            Assert.That((shake2[0], shake2[1] > 0.05f), Is.EqualTo((0f, true)), "channel 2 moves screen 2 only");
            Assert.That((shake1[0] > 0.05f, shake1[1]), Is.EqualTo((true, 0f)), "channel 1 moves screen 1 only");
            Assert.That((light[0] > 0.05f, light[1]), Is.EqualTo((true, 0f)), "a preset (the main camera) moves screen 1 only");
            Assert.That((main, listeners), Is.EqualTo(("Screen 1", 1)), "one main camera · one listener");
            Assert.That((e1 - e0, w1 - w0), Is.EqualTo((0, 0)), "Console: " + string.Join(" / ", RowsFrom(0).Take(3)));
        }

        // the screen never rests over a window of quarter seconds (each quarter moves) — the window is the test's, not the lab's
        static IEnumerator Restless(Transform t, Vector3 rest, float seconds, bool[] ok)
        {
            ok[0] = true;
            int quarters = Mathf.RoundToInt(seconds / 0.25f);
            for (int q = 0; q < quarters; q++)
            {
                float max = 0f, until = Time.realtimeSinceStartup + 0.25f;
                while (Time.realtimeSinceStartup < until)
                {
                    yield return null;
                    max = Mathf.Max(max, (t.position - rest).magnitude);
                }
                ok[0] &= max > 0.01f;
            }
        }

        [UnityTest]
        public IEnumerator XQ03_Endless()
        {
            yield return new EnterPlayMode();
            Load("CameraShakeLab");
            yield return Frames(20);
            (int e0, int w0) = Counts();
            Component lab = Lab("CameraShakeLab");
            Transform s1 = Obj("Screen 1"), s2 = Obj("Screen 2");
            Vector3 r1 = s1.position, r2 = s2.position;
            FeedbackPlayer e1p = F<FeedbackPlayer>(lab, "m_Endless1");
            var ok = new bool[1];
            var m = new float[2];
            Count(e1p);
            Call(lab, "Toggle", 0);
            yield return Restless(s1, r1, 1.5f, ok);
            bool restless = ok[0] && Plays(e1p) == 1;
            Call(lab, "Preset", 2);
            yield return Real(0.3f);
            yield return Restless(s1, r1, 0.5f, ok);
            bool afterHeavy = ok[0] && e1p.IsPlaying;
            Call(lab, "Toggle", 1);
            yield return Real(0.3f);
            Count(e1p);
            Call(lab, "Toggle", 1);
            yield return null;
            float s2Next = (s2.position - r2).magnitude;
            yield return Restless(s1, r1, 0.5f, ok);
            bool oneStillGoes = ok[0] && Plays(e1p) == 0;
            SampleState.SetAfter(false);
            yield return Window(s1, r1, s2, r2, 0.3f, m);
            float before = m[0];
            SampleState.SetAfter(true);
            yield return Restless(s1, r1, 0.5f, ok);
            bool back = ok[0];
            Call(lab, "Toggle", 0);
            yield return null;
            float s1Off = (s1.position - r1).magnitude;
            SampleState.SetAfter(false);
            Call(lab, "Toggle", 0);
            yield return Window(s1, r1, s2, r2, 0.3f, m);
            bool waits = m[0] == 0f && !e1p.IsPlaying;
            SampleState.SetAfter(true);
            yield return Real(0.1f);
            yield return Restless(s1, r1, 0.5f, ok);
            bool startsAtAfter = ok[0];
            Call(lab, "Toggle", 0);
            (int e1, int w1) = Counts();
            SampleState.Restore();
            yield return new ExitPlayMode();
            Assert.That(restless, Is.True, "an endless shake never rests — one play");
            Assert.That(afterHeavy, Is.True, "a preset on top adds to it and the endless goes on after it");
            Assert.That((s2Next, oneStillGoes), Is.EqualTo((0f, true)), "stopping screen 2's endless is at once and leaves screen 1's (not played again)");
            Assert.That((before, back), Is.EqualTo((0f, true)), "Before holds it at 0, After brings it back");
            Assert.That((s1Off, waits, startsAtAfter), Is.EqualTo((0f, true, true)), "switched on in Before, it waits for After");
            Assert.That((e1 - e0, w1 - w0), Is.EqualTo((0, 0)), "Console: " + string.Join(" / ", RowsFrom(0).Take(3)));
        }

        // ---- S5 Cinemachine Shake Lab

        static Transform Vcam(Component lab, int i) => ((Component)F<Array>(lab, "m_Vcams").GetValue(i)).transform;

        [UnityTest]
        public IEnumerator XQ04_CinemachineSwitch()
        {
            if (!Cinemachine())
                Assert.Ignore("no Cinemachine 3 in this project");
            yield return new EnterPlayMode();
            Load("CinemachineShakeLab");
            yield return Frames(20);
            (int e0, int w0) = Counts();
            Component lab = Lab("CinemachineShakeLab");
            Transform cm = F<GameObject>(lab, "m_CmCamera").transform, plain = F<GameObject>(lab, "m_PlainCamera").transform;
            Transform near = Vcam(lab, 0);
            Vector3 rest = plain.position;
            var m = new float[2];
            string main0 = Camera.main != null ? Camera.main.name : "none";
            Call(lab, "Preset", 1);
            yield return Window(cm, near.position, plain, rest, 0.4f, m);
            float onCm = m[0];
            Call(lab, "Switch");
            string main1 = Camera.main != null ? Camera.main.name : "none";
            bool cmOff = !cm.gameObject.activeInHierarchy;
            int listeners = Listeners();
            Call(lab, "Preset", 1);
            yield return Window(near, near.position, plain, rest, 0.4f, m);
            float onPlain = m[1];
            Call(lab, "Toggle");
            yield return Real(0.2f);
            yield return Window(near, near.position, plain, rest, 0.3f, m);
            float endlessPlain = m[1];
            Call(lab, "Switch");
            yield return Real(0.1f);
            yield return Window(cm, near.position, plain, rest, 0.3f, m);
            float endlessCm = m[0];
            Call(lab, "Toggle");
            (int e1, int w1) = Counts();
            yield return new ExitPlayMode();
            Assert.That((main0, main1, cmOff, listeners), Is.EqualTo(("CM Camera", "Plain Camera", true, 1)), "the tag moves · one listener");
            Assert.That((onCm > 0.05f, onPlain > 0.05f), Is.EqualTo((true, true)), "the same preset shakes the Cinemachine camera and the plain one");
            Assert.That((endlessPlain > 0.01f, endlessCm > 0.01f), Is.EqualTo((true, true)), "the endless follows each switch");
            Assert.That((e1 - e0, w1 - w0), Is.EqualTo((0, 0)), "Console: " + string.Join(" / ", RowsFrom(0).Take(3)));
        }

        static IEnumerator Lens(Camera c, float seconds, float[] r)
        {
            r[0] = float.MaxValue;
            r[1] = float.MinValue;
            float until = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < until)
            {
                yield return null;
                float v = c.orthographic ? c.orthographicSize : c.fieldOfView;
                r[0] = Mathf.Min(r[0], v);
                r[1] = Mathf.Max(r[1], v);
            }
        }

        static bool Usable(Component lab, int i) => (bool)Call(lab, "Usable", i);

        [UnityTest]
        public IEnumerator XQ05_CinemachineItems()
        {
            if (!Cinemachine())
                Assert.Ignore("no Cinemachine 3 in this project");
            yield return new EnterPlayMode();
            Load("CinemachineShakeLab");
            yield return Frames(20);
            (int e0, int w0) = Counts();
            Component lab = Lab("CinemachineShakeLab");
            Transform cm = F<GameObject>(lab, "m_CmCamera").transform;
            Camera view = cm.GetComponent<Camera>();
            Transform near = Vcam(lab, 0), wide = Vcam(lab, 1);
            var m = new float[2];
            var r = new float[2];
            Call(lab, "PlayItem", 0);
            yield return Window(cm, near.position, cm, near.position, 0.6f, m);
            float impulse = m[0];
            yield return Real(0.6f);
            Call(lab, "PlayItem", 0);
            yield return Real(0.1f);
            Call(lab, "PlayItem", 1);
            yield return Frames(2);
            float cleared = (cm.position - near.position).magnitude;
            bool perspectiveGate = Usable(lab, 2) && !Usable(lab, 4);
            Call(lab, "PlayItem", 2);
            yield return Lens(view, 0.7f, r);
            float fov = r[1] - r[0];
            Call(lab, "PlayItem", 5);
            yield return Window(cm, near.position, cm, near.position, 0.8f, m);
            float noise = m[0];
            Call(lab, "PlayItem", 9);
            yield return Real(1.5f);
            bool ortho = view.orthographic;
            bool orthoGate = !Usable(lab, 2) && Usable(lab, 4);
            Call(lab, "PlayItem", 4);
            yield return Lens(view, 0.7f, r);
            float size = r[1] - r[0];
            Call(lab, "PlayItem", 8);
            yield return Real(1.5f);
            float atWide = (cm.position - wide.position).magnitude;
            bool backToPerspective = !view.orthographic;
            Call(lab, "Switch");
            bool plainGate = NoneUsable(lab);
            (int e1, int w1) = Counts();
            yield return new ExitPlayMode();
            Assert.That((impulse > 0.1f, cleared < 0.001f), Is.EqualTo((true, true)), $"an impulse moves the camera, a clear stops it ({impulse} · {cleared})");
            Assert.That((perspectiveGate, fov > 5f), Is.EqualTo((true, true)), "in perspective: the field of view, not the size");
            Assert.That(noise > 0.05f, Is.True, "the noise amplitude shows");
            Assert.That((ortho, orthoGate, size > 1f), Is.EqualTo((true, true, true)), "Go Top: orthographic · the size, not the field of view");
            Assert.That((atWide < 0.5f, backToPerspective), Is.EqualTo((true, true)), "Go Wide: the wide camera · perspective again");
            Assert.That(plainGate, Is.True, "on the plain camera no Cinemachine item plays");
            Assert.That((e1 - e0, w1 - w0), Is.EqualTo((0, 0)), "Console: " + string.Join(" / ", RowsFrom(0).Take(3)));
        }

        // ---- S10 PostFX Lab

        // every overlay volume lit at some frame of the window (a punch or a bell passes 0 — one frame is not enough)
        static IEnumerator Union(float seconds, List<Behaviour> into, Behaviour[] except)
        {
            into.Clear();
            float until = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < until)
            {
                yield return null;
                foreach (Behaviour v in Lit(Overlays()))
                    if (!into.Contains(v) && Array.IndexOf(except, v) < 0)
                        into.Add(v);
            }
        }

        static bool NoneUsable(Component lab)
        {
            for (int i = 0; i < 10; i++)
                if (Usable(lab, i))
                    return false;
            return true;
        }

        [UnityTest]
        public IEnumerator XQ06_PostFXCombos()
        {
            yield return new EnterPlayMode();
            Load("PostFXLab");
            yield return Frames(20);
            (int e0, int w0) = Counts();
            Component lab = Lab("PostFXLab");
            float scale0 = Time.timeScale;
            var union = new List<Behaviour>();
            Call(lab, "Hit");
            yield return Union(0.3f, union, new Behaviour[0]);
            Behaviour[] hit = union.ToArray();
            yield return Real(0.6f);
            float hitAfter = MaxWeight(hit);
            Call(lab, "Toggle");
            yield return Union(0.5f, union, new Behaviour[0]);
            Behaviour[] pulse = union.ToArray();
            int flips = 0;
            bool on = false, zeroBetween = false;
            float until = Time.realtimeSinceStartup + 3f;
            while (Time.realtimeSinceStartup < until)
            {
                yield return null;
                bool now = MaxWeight(pulse) > 0.001f;
                if (now != on)
                    flips++;
                if (!now && flips > 0)
                    zeroBetween = true;
                on = now;
            }
            Call(lab, "Hit");
            yield return Union(0.3f, union, pulse);
            Behaviour[] hit2 = union.ToArray();
            float hitTop = hit2.Length == 0 ? 0f : hit2.Max(Priority);
            float pulseTop = pulse.Length == 0 ? float.MaxValue : pulse.Max(Priority);
            yield return Real(0.5f);
            Call(lab, "Toggle");
            FeedbackPlayer lowP = F<FeedbackPlayer>(lab, "m_LowHealth");
            float pulseOff = 0f;
            until = Time.realtimeSinceStartup + 1.2f;
            while (Time.realtimeSinceStartup < until)
            {
                yield return null;
                pulseOff = Mathf.Max(pulseOff, MaxWeight(pulse));
            }
            bool lowStopped = !lowP.IsPlaying;
            Call(lab, "SlowMo");
            float t0 = Time.realtimeSinceStartup, minScale = 9f, peak = 0f, peakAt = 0f;
            Behaviour[] desat = new Behaviour[0];
            while (Time.realtimeSinceStartup - t0 < 2.5f)
            {
                yield return null;
                minScale = Mathf.Min(minScale, Time.timeScale);
                if (desat.Length == 0)
                    desat = Of(Lit(Overlays()), "ColorAdjustments");
                float w = MaxWeight(desat);
                if (w > peak)
                {
                    peak = w;
                    peakAt = Time.realtimeSinceStartup - t0;
                }
            }
            float scaleBack = Time.timeScale;
            SampleState.SetAfter(false);
            Call(lab, "Toggle");
            yield return Real(0.3f);
            float waiting = MaxWeight(pulse);
            SampleState.SetAfter(true);
            float seen = 0f;
            until = Time.realtimeSinceStartup + 1.2f;
            while (Time.realtimeSinceStartup < until)
            {
                yield return null;
                seen = Mathf.Max(seen, MaxWeight(pulse));
            }
            Call(lab, "SlowMo");
            yield return Real(0.6f);
            float midScale = Time.timeScale;
            Load("CameraShakeLab");
            yield return Frames(3);
            float leftScale = Time.timeScale;
            float leftWeight = MaxWeight(Overlays());
            (int e1, int w1) = Counts();
            SampleState.Restore();
            yield return new ExitPlayMode();
            Assert.That((hit.Length, hitAfter), Is.EqualTo((3, 0f)), "the hit: three overlay volumes, back to 0");
            Assert.That((flips >= 6, zeroBetween), Is.EqualTo((true, true)), $"the low-health pulse beats with rests ({flips} flips)");
            Assert.That(hitTop, Is.GreaterThan(pulseTop), "the hit (Start) over the pulse (Awake)");
            Assert.That((pulseOff, lowStopped), Is.EqualTo((0f, true)), "switched off, the pulse is gone at once (a window over one period)");
            Assert.That((Mathf.Abs(minScale - scale0 * 0.2f) < 0.01f, scaleBack), Is.EqualTo((true, scale0)), "the slow-mo goes to 0.2 of the time scale and back");
            Assert.That(peakAt, Is.InRange(0.6f, 1.1f), "the desaturation peaks on the unscaled clock (0.85 s)");
            Assert.That((waiting, seen > 0.001f), Is.EqualTo((0f, true)), "switched on in Before, the pulse starts at After");
            Assert.That((midScale < 0.5f, leftScale, leftWeight), Is.EqualTo((true, scale0, 0f)), "leaving in the middle reverts the time and the overlay");
            Assert.That((e1 - e0, w1 - w0), Is.EqualTo((0, 0)), "Console: " + string.Join(" / ", RowsFrom(0).Take(3)));
        }

        static string Category(FeedbackPlayer p)
        {
            var items = (IList)typeof(FeedbackPlayer).GetField("m_Items", All).GetValue(p);
            var names = new List<string>();
            foreach (object item in items)
                names.Add(item.GetType().GetProperty("EffectiveCategory", All).GetValue(item).ToString());
            return string.Join(",", names);
        }

        [UnityTest]
        public IEnumerator XQ07_PostFXEffects()
        {
            yield return new EnterPlayMode();
            Load("PostFXLab");
            yield return Frames(20);
            (int e0, int w0) = Counts();
            Component lab = Lab("PostFXLab");
            FeedbackPlayer[] effects = F<FeedbackPlayer[]>(lab, "m_Effects");
            Behaviour night = Obj("Night Volume").GetComponent(s_Volume) as Behaviour;
            var dark = new List<string>();
            var stuck = new List<string>();
            var flash = new List<string>();
            for (int i = 0; i < effects.Length; i++)
            {
                string name = effects[i].name;
                if (Category(effects[i]).Contains("Flash"))
                    flash.Add(name);
                Call(lab, "PlayEffect", i);
                float seen = 0f;
                float until = Time.realtimeSinceStartup + 0.3f;
                while (Time.realtimeSinceStartup < until)
                {
                    yield return null;
                    seen = Mathf.Max(seen, name == "Volume Weight" ? Weight(night) : MaxWeight(Overlays()));
                }
                if (seen <= 0.001f)
                    dark.Add(name);
                yield return Real(3f);
                if (MaxWeight(Overlays()) > 0.001f || Weight(night) > 0.001f)
                    stuck.Add(name);
            }
            bool blend = Volumes().Any(v => Profile(v) != null && Profile(v).name == "Danger" && Priority(v) >= 1000f);
            (int e1, int w1) = Counts();
            yield return new ExitPlayMode();
            Assert.That(effects.Length, Is.EqualTo(16), "one button per PostFX item");
            Assert.That(dark, Is.Empty, "every effect shows");
            Assert.That(stuck, Is.Empty, "every effect goes back to 0");
            Assert.That(flash, Is.Empty, "no effect in the Flash category");
            Assert.That(blend, Is.True, "Volume Blend lends its own volume the Danger profile");
            Assert.That((e1 - e0, w1 - w0), Is.EqualTo((0, 0)), "Console: " + string.Join(" / ", RowsFrom(0).Take(3)));
        }

        // ---- the pad alone

        // a pointer down · up · click on the lab's button of that name (the effect players carry the same names — the button is the one
        // with a Button)
        static string s_Press = "";

        static GameObject PressButton(string name)
        {
            UnityEngine.UI.Button[] all = Resources.FindObjectsOfTypeAll<UnityEngine.UI.Button>();
            UnityEngine.UI.Button b = all.FirstOrDefault(x => x.name == name && x.gameObject.scene.isLoaded);
            UnityEngine.UI.Button n = all.FirstOrDefault(x => x.name == name);
            s_Press = $"buttons {all.Length} · named {all.Count(x => x.name == name)} · scene '{(n != null ? n.gameObject.scene.name : "-")}' loaded {(n != null && n.gameObject.scene.isLoaded)} · found {b != null} · event system {EventSystem.current != null}";
            if (b == null || EventSystem.current == null)
                return null;
            var data = new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left };
            ExecuteEvents.Execute(b.gameObject, data, ExecuteEvents.pointerDownHandler);
            ExecuteEvents.Execute(b.gameObject, data, ExecuteEvents.pointerUpHandler);
            ExecuteEvents.Execute(b.gameObject, data, ExecuteEvents.pointerClickHandler);
            return b.gameObject;
        }

        [UnityTest]
        public IEnumerator XQ08_PadOnly()
        {
            yield return new EnterPlayMode();
            Load("PostFXLab");
            var was = Focus();
            var pad = InputSystem.AddDevice<Gamepad>();
            yield return Frames(20);
            (int e0, int w0) = Counts();
            Component lab = Lab("PostFXLab");
            FeedbackPlayer hitP = F<FeedbackPlayer>(lab, "m_Hit"), slowP = F<FeedbackPlayer>(lab, "m_Slow");
            Count(hitP, slowP);
            yield return Tap(pad, GamepadButton.DpadRight);
            yield return Tap(pad, GamepadButton.DpadRight);
            int picked = P<int>(lab, "Selected");
            yield return Tap(pad, GamepadButton.DpadLeft);
            int back = P<int>(lab, "Selected");
            yield return Tap(pad, GamepadButton.DpadDown);
            yield return Real(0.1f);
            float played = MaxWeight(Overlays());
            bool pressed = PressButton("Bloom") != null;
            yield return Frames(2);
            bool clearAfterPress = EventSystem.current != null && EventSystem.current.currentSelectedGameObject == null;
            yield return Tap(pad, GamepadButton.South);
            yield return Tap(pad, GamepadButton.West);
            yield return Tap(pad, GamepadButton.North);
            bool wants = P<bool>(lab, "Wants");
            yield return Tap(pad, GamepadButton.DpadUp);
            bool stopped = !P<bool>(lab, "Wants");
            bool clear = EventSystem.current != null && EventSystem.current.currentSelectedGameObject == null;
            int hits = Plays(hitP), slows = Plays(slowP);
            (int e1, int w1) = Counts();
            InputSystem.RemoveDevice(pad);
            Unfocus(was);
            yield return new ExitPlayMode();
            Assert.That((picked, back), Is.EqualTo((2, 1)), "D-pad right · left pick the effect");
            Assert.That(played, Is.GreaterThan(0.001f), "D-pad down plays the picked effect");
            Assert.That((hits, slows, wants, stopped), Is.EqualTo((1, 1, true, true)), "South · West · North · D-pad up");
            // read inside the play: after the exit a destroyed object compares equal to null (a vacuous pass)
            Assert.That(clear, Is.True, "no widget takes the pad's selection");
            Assert.That((pressed, clearAfterPress), Is.EqualTo((true, true)), "a pointer press on a button leaves no selection (South isn't also a Submit) — " + s_Press);
            Assert.That((e1 - e0, w1 - w0), Is.EqualTo((0, 0)), "Console: " + string.Join(" / ", RowsFrom(0).Take(3)));
        }
    }
}
