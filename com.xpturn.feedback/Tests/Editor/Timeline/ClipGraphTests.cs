using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.Timeline;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.Timeline;
using UnityEngine.UIElements;
using xpTURN.Feedback.Editor;
using Object = UnityEngine.Object;

namespace xpTURN.Feedback.Tests.Editor
{
    // a window for a clip's Inspector in a batch editor
    internal sealed class ClipGraphTestWindow : EditorWindow
    {
    }

    // a clip's Graph: a driven clip's line against the Timeline window's preview of the clip, bit for bit (clip-in, speed,
    // loop, the list's end, a Persist, the driven mark), Triggered and Auto, the builds after an edit, what cannot be drawn,
    // the head and the scrub, Play mode (a director's options, a player not prepared yet), the cost, the start values under a
    // preview, the Triggered words, and inside Timeline's clip Inspector. The window's state is driven through its internal
    // members; values are read in the call that turns the preview off
    public sealed class ClipGraphTests
    {
        const BindingFlags All = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        const string ScenePath = "Assets/ClipGraphTests.unity";
        const string TimelinePath = "Assets/ClipGraphTests.playable";
        const string VariantPath = "Assets/ClipGraphTestsVariant.asset";
        const string OptionsKey = "xpTURN.Feedback.ClipGraphTests.";

        TimelineEditorWindow m_Window;
        object m_State;
        ClipGraphTestWindow m_Inspector;
        readonly List<UnityEditor.Editor> m_Editors = new List<UnityEditor.Editor>();

        [TearDown]
        public void TearDown()
        {
            if (m_State != null && m_Window != null && Preview)
                Preview = false;
            if (m_Window != null)
                m_Window.Close();
            m_Window = null;
            m_State = null;
            if (m_Inspector != null)
                m_Inspector.Close();
            foreach (UnityEditor.Editor e in m_Editors)
            {
                if (e != null)
                    Object.DestroyImmediate(e);
            }
            m_Editors.Clear();
            PreviewSession.Current?.End(PreviewEndReason.Stopped);
            if (!AnimationMode.InAnimationMode())
                PreviewSession.SettleTrackRebuild();
            PreviewHooks.ReplayPending();
            RestoreOptions();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            AssetDatabase.DeleteAsset(ScenePath);
            AssetDatabase.DeleteAsset(TimelinePath);
            AssetDatabase.DeleteAsset(VariantPath);
        }

        // ---- the play mode options, put back after

        static void SetOptions(EnterPlayModeOptions options)
        {
            if (!SessionState.GetBool(OptionsKey + "Saved", false))
            {
                SessionState.SetBool(OptionsKey + "Saved", true);
                SessionState.SetInt(OptionsKey + "Options", (int)EditorSettings.enterPlayModeOptions);
                SessionState.SetBool(OptionsKey + "Enabled", EditorSettings.enterPlayModeOptionsEnabled);
            }
            EditorSettings.enterPlayModeOptionsEnabled = true;
            EditorSettings.enterPlayModeOptions = options;
        }

        static void RestoreOptions()
        {
            if (!SessionState.GetBool(OptionsKey + "Saved", false))
                return;
            EditorSettings.enterPlayModeOptions = (EnterPlayModeOptions)SessionState.GetInt(OptionsKey + "Options", 0);
            EditorSettings.enterPlayModeOptionsEnabled = SessionState.GetBool(OptionsKey + "Enabled", false);
            SessionState.EraseBool(OptionsKey + "Saved");
        }

        static IEnumerator Wait(double seconds)
        {
            double end = EditorApplication.timeSinceStartup + seconds;
            while (EditorApplication.timeSinceStartup < end)
                yield return null;
        }

        // ---- the Timeline window

        void Open(PlayableDirector director)
        {
            m_Window = TimelineEditor.GetOrCreateWindow();
            m_Window.SetTimeline(director);
            PropertyInfo state = m_Window.GetType().GetProperty("state", All);
            Assert.That(state, Is.Not.Null, "TimelineWindow.state");
            m_State = state.GetValue(m_Window);
            foreach (string member in new[] { "previewMode", "editSequence" })
                Assert.That(m_State.GetType().GetProperty(member), Is.Not.Null, "WindowState." + member);
            Assert.That(m_State.GetType().GetMethod("EvaluateImmediate", All, null, Type.EmptyTypes, null), Is.Not.Null, "WindowState.EvaluateImmediate()");
        }

        bool Preview
        {
            get => (bool)m_State.GetType().GetProperty("previewMode").GetValue(m_State);
            set => m_State.GetType().GetProperty("previewMode").SetValue(m_State, value);
        }

        double WindowTime
        {
            get
            {
                object sequence = m_State.GetType().GetProperty("editSequence").GetValue(m_State);
                return (double)sequence.GetType().GetProperty("time").GetValue(sequence);
            }
        }

        void Scrub(double t)
        {
            object sequence = m_State.GetType().GetProperty("editSequence").GetValue(m_State);
            sequence.GetType().GetProperty("time").SetValue(sequence, t);
            m_State.GetType().GetMethod("EvaluateImmediate", All, null, Type.EmptyTypes, null).Invoke(m_State, null);
        }

        // ---- the clip's Inspector

        ClipGraphTestWindow Host(float width)
        {
            if (m_Inspector == null)
            {
                m_Inspector = ScriptableObject.CreateInstance<ClipGraphTestWindow>();
                m_Inspector.Show();
            }
            m_Inspector.position = new Rect(40f, 40f, width, 700f);
            m_Inspector.rootVisualElement.Clear();
            return m_Inspector;
        }

        UnityEditor.Editor Draw(Object[] targets, float width)
        {
            ClipGraphTestWindow host = Host(width);
            UnityEditor.Editor editor = UnityEditor.Editor.CreateEditor(targets);
            m_Editors.Add(editor);
            VisualElement root = editor.CreateInspectorGUI();
            host.rootVisualElement.Add(root);
            root.Bind(editor.serializedObject);
            return editor;
        }

        ClipGraphField Inspect(TimelineClip clip, float width = 220f) => ((FeedbackClipInspector)Draw(new Object[] { clip.asset }, width)).Graph;

        static IEnumerator OpenGraph(ClipGraphField field)
        {
            field.Switch.value = true;
            yield return Wait(0.4);
        }

        // ---- the scene

        static Scene Fresh()
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Assert.That(EditorSceneManager.SaveScene(scene, ScenePath), Is.True);
            return scene;
        }

        static TargetRef Self => new TargetRef { m_Mode = TargetRefMode.Self, m_Path = "", m_Anchor = "" };

        static PositionItem Pos(FeedbackMotion m, float duration, float delay = 0f, int repeats = 0, float gap = 0f)
        {
            var item = new PositionItem { m_Target = Self, m_Motion = m };
            item.m_Timing.m_Duration = duration;
            item.m_Timing.m_Delay = delay;
            item.m_Timing.m_RepeatCount = repeats;
            item.m_Timing.m_RepeatGap = gap;
            return item;
        }

        static PunchMotion Punch(Vector4 a) => new PunchMotion { m_Params = new PunchParams { m_Amplitude = a, m_Vibrato = 4f, m_Decay = 1f } };

        static ShakeMotion Shake(Vector4 a)
        {
            ShakeParams p = ShakeParams.Default;
            p.m_Amplitude = a;
            p.m_Frequency = 12f;
            return new ShakeMotion { m_Params = p };
        }

        static TweenMotion Tween(Vector4 to)
        {
            TweenParams t = TweenParams.Default;
            t.m_To = to;
            t.m_Ease = Ease.Of(EaseType.Linear);
            return new TweenMotion { m_Params = t };
        }

        // a punch repeated with a gap, a shake (Chance 40) after a delay, a Persist tween after a delay
        static List<FeedbackItem> Items(float chance = 40f)
        {
            PositionItem shake = Pos(Shake(new Vector4(0.2f, 0.2f, 0.2f, 0f)), 0.6f, 0.2f);
            shake.m_Conditions.m_Chance = chance;
            return new List<FeedbackItem>
            {
                Pos(Punch(new Vector4(0.3f, 0.2f, 0.1f, 0f)), 0.4f, 0f, 2, 0.1f),
                shake,
                Pos(Tween(new Vector4(0.5f, 0f, 0f, 0f)), 0.5f, 0.6f),
            };
        }

        static FeedbackPlayer Box(string name, List<FeedbackItem> items)
        {
            var go = new GameObject(name);
            go.transform.localPosition = new Vector3(1f, 2f, 3f);
            var p = go.AddComponent<FeedbackPlayer>();
            p.m_Items.AddRange(items);
            return p;
        }

        static PlayableDirector Director(out TimelineAsset asset, string assetPath = null)
        {
            var d = new GameObject("Director").AddComponent<PlayableDirector>();
            d.playOnAwake = false;
            asset = ScriptableObject.CreateInstance<TimelineAsset>();
            if (assetPath != null)
                AssetDatabase.CreateAsset(asset, assetPath);
            d.playableAsset = asset;
            return d;
        }

        static TimelineClip Track(PlayableDirector d, TimelineAsset asset, FeedbackPlayer player, double start, double duration, FeedbackClipTiming timing = FeedbackClipTiming.Driven)
        {
            FeedbackTrack track = asset.CreateTrack<FeedbackTrack>(null, player != null ? player.name : "Unbound");
            TimelineClip clip = track.CreateClip<FeedbackClip>();
            clip.start = start;
            clip.duration = duration;
            var feedback = (FeedbackClip)clip.asset;
            feedback.m_Timing = timing;
            feedback.m_Seed = 777u;
            feedback.m_Intensity = 0.8f;
            if (player != null)
                d.SetGenericBinding(track, player);
            return clip;
        }

        static FeedbackClip Asset(TimelineClip clip) => (FeedbackClip)clip.asset;

        // the preview's values at the model's ticks, in its order — the line's ticks against them: the count of components
        // that differ and the ticks that moved
        int Differ(SlotGraphModel m, TimelineClip clip, Transform t, out int moved, out float worst)
        {
            Vector4 start = t.localPosition;
            double dt = m.Span / m.TickCount;
            int differ = 0;
            moved = 0;
            worst = 0f;
            for (int n = 0; n < m.TickCount; n++)
            {
                Scrub(clip.start + n * dt);
                Vector4 a = (Vector4)t.localPosition - start;
                for (int c = 0; c < 3; c++)
                {
                    float g = m.Ticks[n * 4 + c];
                    if (BitConverter.SingleToInt32Bits(g) != BitConverter.SingleToInt32Bits(a[c]))
                    {
                        differ++;
                        worst = Math.Max(worst, Math.Abs(g - a[c]));
                    }
                }
                if (a.sqrMagnitude > 1e-8f)
                    moved++;
            }
            return differ;
        }

        // T8 — a driven clip's line is the preview's values, bit for bit: clip-in and speed (0.5 · 1.5 · 2), a loop wrapped
        // (its boundary crossed), the list's end held, a repeated punch, a Persist tween
        [UnityTest]
        public IEnumerator CL01_ADrivenClipIsItsPreview()
        {
            foreach (var (clipIn, scale, loop) in new[] { (0.25, 1.5, true), (0.0, 1.0, false), (0.6, 0.5, true), (0.0, 2.0, false) })
            {
                Fresh();
                FeedbackPlayer box = Box("Box", Items());
                PlayableDirector d = Director(out TimelineAsset asset);
                TimelineClip clip = Track(d, asset, box, 0.5, 3.0);
                clip.clipIn = clipIn;
                clip.timeScale = scale;
                Asset(clip).m_Loop = loop;
                Open(d);
                ClipGraphField field = Inspect(clip);
                yield return OpenGraph(field);
                SlotGraphModel m = field.Model;
                string row = "clip-in " + clipIn + " · speed " + scale + " · loop " + loop;
                Assert.That(m, Is.Not.Null, row);
                Assert.That(field.Triggered, Is.False, row);
                Assert.That(m.Refused, Is.Null, row);
                Preview = true;
                int differ = Differ(m, clip, box.transform, out int moved, out float worst);
                Preview = false;
                Assert.That(differ, Is.EqualTo(0), row + " — worst " + worst);
                Assert.That(moved, Is.GreaterThan(m.TickCount / 4), row + " — the line moves");
                if (loop)
                {
                    double wrap = (TimelinePlayerInfo.Of(box, null).Model.Total - clipIn) / scale;
                    Assert.That(SpanAfter(m, wrap, 0.3 / scale), Is.GreaterThan(1e-3f), row + " — the punch again after the loop's wrap at " + wrap);
                }
                m_Window.Close();
                m_Window = null;
                m_State = null;
                yield return null;
            }
        }

        // the x line's span over a clip time window
        static float SpanAfter(SlotGraphModel m, double from, double length)
        {
            float lo = float.MaxValue, hi = float.MinValue;
            double dt = m.Span / m.TickCount;
            for (int n = 0; n < m.TickCount; n++)
            {
                double x = n * dt;
                if (x < from + 0.02 || x > from + length)
                    continue;
                lo = Math.Min(lo, m.Ticks[n * 4]);
                hi = Math.Max(hi, m.Ticks[n * 4]);
            }
            return hi - lo;
        }

        // T8 — the driven mark: at a seed whose Chance roll drops the shake in a free play (its line is the list's without the
        // shake), the clip's line keeps it, as the preview does
        [UnityTest]
        public IEnumerator CL01b_TheDrivenMark()
        {
            Fresh();
            FeedbackPlayer box = Box("Box", Items());
            PlayableDirector d = Director(out TimelineAsset asset);
            TimelineClip clip = Track(d, asset, box, 0.5, 3.0);
            Open(d);
            ClipGraphField field = Inspect(clip);
            yield return OpenGraph(field);
            // the items' seeds key on their managed reference ids, given when the window first reads the player (an editor
            // session's own values): the seed is looked for once the window shows the clip, on the field's own ticks
            int columns = field.Model.Columns;
            List<FeedbackItem> never = Items(0f);
            uint seed = 0u;
            for (uint s = 1u; s < 300u && seed == 0u; s++)
            {
                Asset(clip).m_Seed = s;
                PlayArgs driven = FeedbackClipBehaviour.DrivenArgs(PlayArgs.Default.WithIntensity(0.8f), Asset(clip));
                PlayArgs free = PlayArgs.Default.WithIntensity(0.8f).WithSeed(s).WithDirection(PlayerDirection.Forward);
                SlotGraphModel.ClipSeek seek = (x, low, high) => FeedbackClipBehaviour.Target(x, false, low, high);
                SlotGraphModel a = SlotGraphModel.BuildClip(box, box.m_Items, box, new GraphInputs(0.8f, s, true, false, false), in driven, 3.0, seek, false, columns, null, null);
                SlotGraphModel b = SlotGraphModel.BuildClip(box, box.m_Items, box, new GraphInputs(0.8f, s, false, false, false), in free, 3.0, seek, false, columns, null, null);
                SlotGraphModel n = SlotGraphModel.BuildClip(box, never, box, new GraphInputs(0.8f, s, false, false, false), in free, 3.0, seek, false, columns, null, null);
                if (b.Ticks.SequenceEqual(n.Ticks) && !a.Ticks.SequenceEqual(n.Ticks))
                    seed = s;
            }
            Assert.That(seed, Is.Not.EqualTo(0u), "a seed whose free play drops the shake");
            UnityEngine.Debug.Log("CL01b seed " + seed + " · " + columns + " columns");
            Asset(clip).m_Seed = seed;
            field.Build();
            Assert.That(field.Model.Columns, Is.EqualTo(columns));
            Preview = true;
            int differ = Differ(field.Model, clip, box.transform, out _, out float worst);
            Preview = false;
            Assert.That(differ, Is.EqualTo(0), "seed " + seed + " — worst " + worst);
        }

        // T9 — Triggered and Auto: a list that cannot be scrubbed and a simulated target play as Triggered (the clip's
        // background's judgement), at the sample seed on the player's clock; a loop plays again after each end
        [UnityTest]
        public IEnumerator CL02_TriggeredAndAuto()
        {
            Fresh();
            PositionItem endless = Pos(Shake(new Vector4(0.1f, 0f, 0f, 0f)), 0.5f);
            endless.m_Timing.m_Infinite = true;
            FeedbackPlayer open = Box("Open", new List<FeedbackItem> { endless });
            FeedbackPlayer punch = Box("Punch", new List<FeedbackItem> { Pos(Punch(new Vector4(0.4f, 0f, 0f, 0f)), 0.5f) });
            var simulatedGo = new GameObject("Simulated");
            simulatedGo.AddComponent<ParticleSystem>();
            FeedbackPlayer simulated = simulatedGo.AddComponent<FeedbackPlayer>();
            simulated.m_Items.Add(new ParticleControlItem { m_Target = Self });
            simulated.m_Items.Add(Pos(Punch(new Vector4(0.4f, 0f, 0f, 0f)), 0.5f));
            PlayableDirector d = Director(out TimelineAsset asset);
            TimelineClip auto = Track(d, asset, open, 0.0, 2.0, FeedbackClipTiming.Auto);
            TimelineClip once = Track(d, asset, punch, 0.0, 2.0, FeedbackClipTiming.Triggered);
            TimelineClip sim = Track(d, asset, simulated, 0.0, 2.0, FeedbackClipTiming.Driven);
            Open(d);
            ClipGraphField field = Inspect(auto);
            yield return OpenGraph(field);
            Assert.That(field.Triggered, Is.True, "a list that cannot be scrubbed");
            Assert.That(field.Triggered, Is.EqualTo(TimelinePlayerInfo.PlaysTriggered(Asset(auto), TimelinePlayerInfo.Of(open, null))), "the background's judgement");
            Assert.That(field.Model.Inputs.Seed, Is.EqualTo(GraphInputs.SampleSeed));
            Assert.That(field.Notes.text, Does.Contain(ClipGraphField.NotScrubbedText));
            Assert.That(field.Notes.text, Does.Contain(ClipGraphField.TriggeredText));
            field = Inspect(sim);
            yield return OpenGraph(field);
            Assert.That(field.Triggered, Is.True, "a simulated target in the preview");
            Assert.That(field.Notes.text, Does.Contain(ClipGraphField.SimulatedText));
            field = Inspect(once);
            yield return OpenGraph(field);
            Assert.That(field.Triggered, Is.True);
            SlotGraphModel single = field.Model;
            int lastMoving = LastMoving(single);
            Assert.That((lastMoving + 1) * single.Span / single.TickCount, Is.EqualTo(0.5).Within(0.02), "one play of 0.5 s");
            punch.m_Speed = 2f;
            field.Build();
            lastMoving = LastMoving(field.Model);
            Assert.That((lastMoving + 1) * field.Model.Span / field.Model.TickCount, Is.EqualTo(0.25).Within(0.02), "the player's clock (×2)");
            punch.m_Speed = 1f;
            Asset(once).m_Loop = true;
            field.Build();
            lastMoving = LastMoving(field.Model);
            Assert.That((lastMoving + 1) * field.Model.Span / field.Model.TickCount, Is.GreaterThan(1.5), "played again after each end");
        }

        static int LastMoving(SlotGraphModel m)
        {
            int last = -1;
            for (int n = 0; n < m.TickCount; n++)
            {
                if (Math.Abs(m.Ticks[n * 4]) > 1e-5f)
                    last = n;
            }
            return last;
        }

        // T10 — built again at the next frame after an edit of the clip asset, of its TimelineClip (through Timeline's own
        // clip Inspector object) and of the player; nothing while off
        [UnityTest]
        public IEnumerator CL03_BuiltAgainAfterAnEdit()
        {
            Fresh();
            FeedbackPlayer box = Box("Box", Items());
            PlayableDirector d = Director(out TimelineAsset asset);
            TimelineClip clip = Track(d, asset, box, 0.5, 3.0);
            Open(d);
            ClipGraphField field = Inspect(clip);
            yield return OpenGraph(field);
            yield return Wait(0.2);
            int builds = field.Builds;
            Assert.That(builds, Is.GreaterThan(0), "built once open (and again for the settled width)");
            var so = new SerializedObject(clip.asset);
            so.FindProperty("m_Seed").uintValue = 12345u;
            so.ApplyModifiedProperties();
            yield return Wait(0.3);
            Assert.That(field.Builds, Is.EqualTo(builds + 1), "the clip asset's seed");
            TimelineEditor.selectedClip = clip;
            Object selected = Selection.activeObject;
            Assert.That(selected, Is.Not.Null);
            var clipSo = new SerializedObject(selected);
            SerializedProperty clipIn = clipSo.FindProperty("m_Clip.m_ClipIn");
            Assert.That(clipIn, Is.Not.Null, "EditorClip m_Clip.m_ClipIn");
            clipIn.doubleValue = 0.3;
            clipSo.ApplyModifiedProperties();
            Assert.That(clip.clipIn, Is.EqualTo(0.3).Within(1e-9));
            yield return Wait(0.4);
            Assert.That(field.Builds, Is.EqualTo(builds + 2), "the TimelineClip's clip-in");
            var player = new SerializedObject(box);
            player.FindProperty("m_DurationMultiplier").floatValue = 2f;
            player.ApplyModifiedProperties();
            yield return Wait(0.4);
            Assert.That(field.Builds, Is.EqualTo(builds + 3), "the player");
            field.Switch.value = false;
            yield return null;
            player.FindProperty("m_DurationMultiplier").floatValue = 1f;
            player.ApplyModifiedProperties();
            so.FindProperty("m_Seed").uintValue = 54321u;
            so.ApplyModifiedProperties();
            yield return Wait(0.4);
            Assert.That(field.Builds, Is.EqualTo(builds + 3), "off: nothing");
        }

        // T11 — no player (an unbound track, the window shows another timeline), a Preset the player does not list: words,
        // nothing built; two clips: no Graph
        [UnityTest]
        public IEnumerator CL04_WhatCannotBeDrawn()
        {
            Fresh();
            FeedbackPlayer box = Box("Box", Items());
            var unlisted = ScriptableObject.CreateInstance<FeedbackPreset>();
            unlisted.m_Items.Add(Pos(Punch(Vector4.one * 0.1f), 0.3f));
            AssetDatabase.CreateAsset(unlisted, VariantPath);
            PlayableDirector d = Director(out TimelineAsset asset);
            TimelineClip unbound = Track(d, asset, null, 0.0, 1.0);
            TimelineClip other = Track(d, asset, box, 0.0, 1.0);
            Asset(other).m_Preset = unlisted;
            TimelineClip plain = Track(d, asset, box, 1.0, 1.0);
            Open(d);
            ClipGraphField field = Inspect(unbound);
            yield return OpenGraph(field);
            Assert.That(field.Builds, Is.EqualTo(0));
            Assert.That(field.Notes.text, Is.EqualTo(ClipGraphField.NoPlayerText));
            Assert.That(field.Header.resolvedStyle.display, Is.EqualTo(DisplayStyle.None), "the note only");
            Assert.That(field.View.resolvedStyle.display, Is.EqualTo(DisplayStyle.None));
            field = Inspect(other);
            yield return OpenGraph(field);
            Assert.That(field.Builds, Is.EqualTo(0));
            Assert.That(field.Notes.text, Is.EqualTo(ClipGraphField.UnlistedText));
            PlayableDirector d2 = Director(out TimelineAsset asset2);
            Track(d2, asset2, box, 0.0, 1.0);
            m_Window.SetTimeline(d2);
            field = Inspect(plain);
            yield return OpenGraph(field);
            Assert.That(field.Builds, Is.EqualTo(0), "the window shows another timeline");
            Assert.That(field.Notes.text, Is.EqualTo(ClipGraphField.NoPlayerText));
            var two = (FeedbackClipInspector)Draw(new[] { plain.asset, other.asset }, 220f);
            Assert.That(two.Graph, Is.Null, "two clips");
        }

        // T12 — the head is the window's time inside the clip; a press moves the window's time while its preview holds the
        // player — the same call, the next frame and 0.3 s on; with the preview off nothing moves and the words say why
        [UnityTest]
        public IEnumerator CL05_TheHeadAndTheScrub()
        {
            Fresh();
            FeedbackPlayer box = Box("Box", Items());
            PlayableDirector d = Director(out TimelineAsset asset);
            TimelineClip clip = Track(d, asset, box, 0.5, 2.0);
            Open(d);
            ClipGraphField field = Inspect(clip);
            yield return OpenGraph(field);
            Preview = true;
            Scrub(1.0);
            yield return Wait(0.3);
            Assert.That(field.View.Heads.Count, Is.EqualTo(1));
            Assert.That(field.View.Heads[0].Seconds, Is.EqualTo(0.5).Within(1e-9));
            Scrub(0.2);
            yield return Wait(0.3);
            Assert.That(field.View.Heads.Count, Is.EqualTo(0), "before the clip");
            field.Scrub(1.2);
            Vector3 there = box.transform.localPosition;
            Assert.That(WindowTime, Is.EqualTo(1.7).Within(1e-9), "the same call");
            Assert.That(d.time, Is.EqualTo(1.7).Within(1e-9));
            Assert.That(field.Notes.text, Does.Not.Contain(ClipGraphField.ScrubText));
            yield return null;
            Assert.That(WindowTime, Is.EqualTo(1.7).Within(1e-9), "the next frame");
            yield return Wait(0.3);
            Assert.That(WindowTime, Is.EqualTo(1.7).Within(1e-9), "0.3 s on");
            Assert.That(box.transform.localPosition, Is.EqualTo(there), "the preview's values at that time");
            Scrub(1.7);
            Assert.That(box.transform.localPosition, Is.EqualTo(there), "the window's own evaluation at 1.7");
            Preview = false;
            double before = WindowTime;
            field.Scrub(0.4);
            Assert.That(WindowTime, Is.EqualTo(before), "the preview off: the window's time stays");
            Assert.That(field.Notes.text, Does.Contain(ClipGraphField.ScrubText));
        }

        // T13 — Play mode: a director's Hit Key row (its intensity, its Variant key's list) is in the clip's Graph; a Lazy
        // player not prepared yet is drawn with the clip's Preset and stays unprepared
        [UnityTest]
        public IEnumerator CL06_PlayMode()
        {
            SetOptions(EnterPlayModeOptions.DisableDomainReload);
            Scene scene = Fresh();
            var variant = ScriptableObject.CreateInstance<FeedbackPreset>();
            variant.m_Alias = "alt";
            variant.m_Items.Add(Pos(Punch(new Vector4(0.9f, 0f, 0f, 0f)), 0.6f));
            AssetDatabase.CreateAsset(variant, VariantPath);
            FeedbackPlayer box = Box("Box", new List<FeedbackItem> { Pos(Punch(new Vector4(0.1f, 0f, 0f, 0f)), 0.3f) });
            box.m_Variants.Add(variant);
            FeedbackPlayer lazy = Box("Lazy", new List<FeedbackItem> { Pos(Punch(new Vector4(0.2f, 0f, 0f, 0f)), 0.3f) });
            lazy.m_Initialization = PlayerInitialization.Lazy;
            PlayableDirector d = Director(out TimelineAsset asset, TimelinePath);
            d.gameObject.AddComponent<FeedbackTimelineOptions>().SetHit("hit", 0.5f, "alt");
            TimelineClip boxClip = Track(d, asset, box, 0.0, 1.0);
            Asset(boxClip).m_HitKey = "hit";
            TimelineClip lazyClip = Track(d, asset, lazy, 0.0, 1.0);
            FeedbackClip boxAsset = Asset(boxClip);
            FeedbackClip lazyAsset = Asset(lazyClip);
            Assert.That(EditorSceneManager.SaveScene(scene), Is.True);
            yield return new EnterPlayMode();
            var director = GameObject.Find("Director").GetComponent<PlayableDirector>();
            Open(director);
            yield return null;
            TimelineClip inPlay = TimelinePlayerInfo.ClipOf(boxAsset);
            Assert.That(inPlay, Is.Not.Null);
            ClipGraphField field = Inspect(inPlay);
            yield return OpenGraph(field);
            Assert.That(field.Owner, Is.SameAs(variant), "the Hit Key row's Variant key picks the list");
            Assert.That(field.Model.Inputs.Intensity, Is.EqualTo(0.8f * PlayerRuntime.Factor(0.5f)), "the row's intensity");
            FeedbackPlayer lazyInPlay = GameObject.Find("Lazy").GetComponent<FeedbackPlayer>();
            Assert.That(lazyInPlay.Runtime, Is.Null);
            field = Inspect(TimelinePlayerInfo.ClipOf(lazyAsset));
            yield return OpenGraph(field);
            Assert.That(field.Notes.text, Does.Contain(ClipGraphField.NotPreparedText));
            Assert.That(field.Model, Is.Not.Null);
            Assert.That(field.Model.Refused, Is.Null);
            Assert.That(lazyInPlay.Runtime, Is.Null, "not prepared by the Graph");
            m_Window.Close();
            m_Window = null;
            m_State = null;
            yield return new ExitPlayMode();
        }

        // T17 — the cost: a hundred items on a 400 px Graph built in under a second; the sum only — no line alone
        [UnityTest]
        public IEnumerator CL07_TheCost()
        {
            Fresh();
            var items = new List<FeedbackItem>();
            for (int i = 0; i < 100; i++)
                items.Add(i % 3 == 0 ? Pos(Punch(new Vector4(0.1f, 0.1f, 0f, 0f)), 0.3f + 0.01f * i, 0.02f * i, 1, 0.05f)
                    : i % 3 == 1 ? Pos(Shake(new Vector4(0.05f, 0f, 0.05f, 0f)), 0.5f, 0.015f * i) : Pos(Punch(new Vector4(0f, 0.2f, 0f, 0f)), 0.2f, 0.01f * i));
            FeedbackPlayer box = Box("Box", items);
            PlayableDirector d = Director(out TimelineAsset asset);
            TimelineClip clip = Track(d, asset, box, 0.0, 9.0);
            Asset(clip).m_Loop = true;
            Open(d);
            ClipGraphField field = Inspect(clip, 460f);
            yield return OpenGraph(field);
            Assert.That(field.Model.Columns, Is.GreaterThanOrEqualTo(380));
            var sw = Stopwatch.StartNew();
            field.Build();
            sw.Stop();
            UnityEngine.Debug.Log("CL07 " + field.Model.Columns + " columns · 100 items: " + sw.ElapsedMilliseconds + " ms");
            Assert.That(sw.ElapsedMilliseconds, Is.LessThan(1000));
            Assert.That(field.View.Faint.Count, Is.EqualTo(0), "no line alone");
        }

        // T18 — under the Timeline window's preview, holding the list in its middle (a punch's offset written, a Persist tween
        // not started), the clip's Graph and the player's Inspector Graph start from the session's base values — the values
        // before the preview: the same lines as with the preview off
        [UnityTest]
        public IEnumerator CL08_TheStartValuesUnderAPreview()
        {
            Fresh();
            FeedbackPlayer box = Box("Box", new List<FeedbackItem> { Pos(Punch(new Vector4(0f, 0.3f, 0f, 0f)), 0.5f), Pos(Tween(new Vector4(4f, 0f, 0f, 0f)), 0.5f, 1f) });
            PlayableDirector d = Director(out TimelineAsset asset);
            TimelineClip clip = Track(d, asset, box, 0.0, 2.0);
            Open(d);
            Preview = true;
            Scrub(0.17);
            Assert.That(box.transform.localPosition, Is.Not.EqualTo(new Vector3(1f, 2f, 3f)), "the preview moved it");
            Assert.That(PreviewSession.Current != null && PreviewSession.Current.HoldsTrackPlayer(box), Is.True);
            ClipGraphField field = Inspect(clip);
            field.Switch.value = true;
            field.Build();
            float[] clipUnder = field.Model.Ticks;
            Assert.That(field.Model.Seeded, Is.True);
            Assert.That(field.Notes.text, Does.Contain("the preview's start values"));
            var playerEditor = (FeedbackPlayerEditor)Draw(new Object[] { box }, 420f);
            playerEditor.SlotGraph.Switch.value = true;
            playerEditor.SlotGraph.Build();
            float[] playerUnder = playerEditor.SlotGraph.Model.Ticks;
            Assert.That(playerEditor.SlotGraph.Model.Seeded, Is.True, "the player's Inspector Graph too");
            Preview = false;
            Assert.That(box.transform.localPosition, Is.EqualTo(new Vector3(1f, 2f, 3f)), "the preview off");
            playerEditor.SlotGraph.Build();
            float[] playerOff = playerEditor.SlotGraph.Model.Ticks;
            Assert.That(playerUnder, Is.EqualTo(playerOff), "the player's Graph: the values before the preview");
            ClipGraphField again = Inspect(clip);
            again.Switch.value = true;
            again.Build();
            Assert.That(clipUnder, Is.EqualTo(again.Model.Ticks), "the clip's Graph: the values before the preview");
            yield return null;
        }

        // T19 — a Triggered clip's words: a player that plays backward or turns, an overlapping clip of the same player; the line
        // drawn forward
        [UnityTest]
        public IEnumerator CL09_TriggeredWords()
        {
            Fresh();
            FeedbackPlayer box = Box("Box", new List<FeedbackItem> { Pos(Punch(new Vector4(0.4f, 0f, 0f, 0f)), 0.5f) });
            box.m_Direction = PlayerDirection.Backward;
            box.m_Policy = PlayPolicy.Restart;
            PlayableDirector d = Director(out TimelineAsset asset);
            TimelineClip clip = Track(d, asset, box, 0.0, 1.0, FeedbackClipTiming.Triggered);
            TimelineClip later = clip.GetParentTrack().CreateClip<FeedbackClip>();
            later.start = 0.5;
            later.duration = 1.0;
            Asset(later).m_Timing = FeedbackClipTiming.Triggered;
            Open(d);
            ClipGraphField field = Inspect(clip);
            yield return OpenGraph(field);
            Assert.That(field.Notes.text, Does.Contain(ClipGraphField.BackwardText));
            Assert.That(field.Notes.text, Does.Contain(TimelinePlayerInfo.RestartText));
            float first = 0f;
            for (int n = 0; n < SlotGraphModel.PerColumn; n++)
                first = Math.Max(first, Math.Abs(field.Model.Ticks[n * 4]));
            Assert.That(first, Is.GreaterThan(1e-4f), "forward: the punch at the clip's start");
            box.m_Direction = PlayerDirection.Toggle;
            field.Build();
            Assert.That(field.Notes.text, Does.Contain(ClipGraphField.ToggleText));
        }

        // Timeline's own clip Inspector holds the clip's editor: the Graph is in it and builds
        [UnityTest]
        public IEnumerator CL10_InTimelinesClipInspector()
        {
            Fresh();
            FeedbackPlayer box = Box("Box", Items());
            PlayableDirector d = Director(out TimelineAsset asset);
            TimelineClip clip = Track(d, asset, box, 0.5, 3.0);
            Open(d);
            TimelineEditor.selectedClip = clip;
            Object selected = Selection.activeObject;
            Assert.That(selected, Is.Not.Null);
            ClipGraphTestWindow host = Host(420f);
            var inspector = new InspectorElement(selected);
            host.rootVisualElement.Add(inspector);
            yield return Wait(0.4);
            var field = inspector.Q<ClipGraphField>();
            Assert.That(field, Is.Not.Null, "inside the clip Inspector");
            field.Switch.value = true;
            yield return Wait(0.4);
            Assert.That(field.Model, Is.Not.Null);
            Assert.That(field.Model.Refused, Is.Null);
            Assert.That(field.Player, Is.SameAs(box));
        }

        // the clip's Graph Preview box: its title row and switch, off at first and idle — the title row only; on, built and
        // drawn; the switch kept per clip asset for the session; an edit while off builds nothing, on again builds for it
        [UnityTest]
        public IEnumerator CL11_TheBox()
        {
            Fresh();
            FeedbackPlayer box = Box("Box", Items());
            PlayableDirector d = Director(out TimelineAsset asset);
            TimelineClip clip = Track(d, asset, box, 0.5, 3.0);
            Open(d);
            string key = ClipGraphField.SwitchPrefix + clip.asset.GetEntityId();
            SessionState.EraseBool(key);
            try
            {
                ClipGraphField field = Inspect(clip);
                yield return Wait(0.4);
                Assert.That(field.ClassListContains(InspectorStyles.GraphBox), Is.True, "a box");
                Assert.That(field.Q<Foldout>(), Is.Null, "no fold");
                Assert.That(field.Q<Label>(className: InspectorStyles.GraphTitle).text, Is.EqualTo("Graph Preview"));
                Assert.That(field.Switch.tooltip, Is.EqualTo(ClipGraphField.SwitchTooltip));
                Assert.That(field.Switch.value, Is.False, "off at first");
                Assert.That(field.Body.resolvedStyle.display, Is.EqualTo(DisplayStyle.None), "the title row only");
                Assert.That(field.Builds, Is.Zero, "idle while off");
                field.Switch.value = true;
                yield return Wait(0.4);
                Assert.That(field.Builds, Is.GreaterThan(0), "on: built");
                Assert.That(field.Model.Sum, Is.Not.Null);
                Assert.That(field.View.resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex), "drawn");
                Assert.That(SessionState.GetBool(key, false), Is.True, "kept per clip asset");
                int builds = field.Builds;
                field.Switch.value = false;
                var so = new SerializedObject(clip.asset);
                so.FindProperty("m_Seed").uintValue = 4242u;
                so.ApplyModifiedProperties();
                yield return Wait(0.3);
                Assert.That(field.Builds, Is.EqualTo(builds), "off: an edit builds nothing");
                field.Switch.value = true;
                yield return Wait(0.4);
                Assert.That(field.Builds, Is.GreaterThan(builds), "on again: built for the edit made while off");
                ClipGraphField again = Inspect(clip);
                yield return Wait(0.4);
                Assert.That(again.Switch.value, Is.True, "built again: still on");
            }
            finally
            {
                SessionState.EraseBool(key);
            }
        }
    }
}
