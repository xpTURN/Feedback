using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.Timeline;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.TestTools.Constraints;
using UnityEngine.Timeline;
using UnityEngine.UIElements;
using xpTURN.Feedback.Editor;
using Is = UnityEngine.TestTools.Constraints.Is;
using Object = UnityEngine.Object;

namespace xpTURN.Feedback.Tests.Editor
{
    // the Feedback track and clip in the Timeline window, in a batch editor — the window shows the timeline (its director);
    // Timeline's clip making, duplicating and Match Content are internal and reached by reflection, each member checked
    // first (a missing one fails, not passes)
    public sealed class TimelineEditorTests
    {
        const BindingFlags All = BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        const string ScenePath = "Assets/TimelineEditorTests.unity";
        const string TimelinePath = "Assets/TimelineEditorTests.playable";
        const string OtherPath = "Assets/TimelineEditorTests2.playable";
        const string PresetPath = "Assets/TimelineEditorTests.asset";
        const string Duration = "m_Items.Array.data[0].m_Timing.m_Duration";

        static object s_Sink;
        static double s_Length;
        TimelineEditorWindow m_Window;
        object m_State;

        [SetUp]
        public void SetUp() => TimelinePlayerInfo.Clear();

        [TearDown]
        public void TearDown()
        {
            if (m_State != null && Preview)
                Preview = false;
            if (m_Window != null)
                m_Window.Close();
            PreviewSession.Current?.End(PreviewEndReason.Stopped);
            if (!AnimationMode.InAnimationMode())
                PreviewSession.SettleTrackRebuild();
            PreviewHooks.ReplayPending();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            AssetDatabase.DeleteAsset(ScenePath);
            AssetDatabase.DeleteAsset(TimelinePath);
            AssetDatabase.DeleteAsset(OtherPath);
            AssetDatabase.DeleteAsset(PresetPath);
            TimelinePlayerInfo.Clear();
        }

        // ---- Timeline's internals

        static Type T(string name)
        {
            Type type = typeof(TimelineEditor).Assembly.GetType("UnityEditor.Timeline." + name);
            Assert.That(type, Is.Not.Null, name);
            return type;
        }

        static MethodInfo M(Type type, string name, params Type[] args)
        {
            MethodInfo method = type.GetMethod(name, All, null, args, null);
            Assert.That(method, Is.Not.Null, type.Name + "." + name);
            return method;
        }

        // the track menu's 'Add Feedback Clip' (and a drop on the track)
        static TimelineClip Create(TrackAsset track, double time = 0.0)
            => (TimelineClip)M(T("TimelineHelpers"), "CreateClipOnTrack", typeof(Type), typeof(TrackAsset), typeof(double)).Invoke(null, new object[] { typeof(FeedbackClip), track, time });

        static void Match(TimelineClip clip) => M(T("ClipModifier"), "MatchContent", typeof(TimelineClip)).Invoke(null, new object[] { clip });

        static double[] LoopTimes(TimelineClip clip) => (double[])M(T("TimelineHelpers"), "GetLoopTimes", typeof(TimelineClip)).Invoke(null, new object[] { clip });

        // the window's duplicate (Ctrl+D) and paste — onto a track at a time
        static void Duplicate(PlayableDirector director, TimelineClip clip, TrackAsset into, double time)
        {
            Type perTrack = T("ItemsPerTrack");
            object item = M(T("ItemsUtils"), "ToItem", typeof(TimelineClip)).Invoke(null, new object[] { clip });
            Array items = Array.CreateInstance(T("ITimelineItem"), 1);
            items.SetValue(item, 0);
            object group = Activator.CreateInstance(perTrack, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new object[] { into, items }, null);
            MethodInfo duplicate = T("TimelineHelpers").GetMethods(All).FirstOrDefault(m => m.Name == "DuplicateItemsUsingCurrentEditMode" && m.GetParameters().Length == 6 && m.GetParameters()[2].ParameterType == perTrack);
            Assert.That(duplicate, Is.Not.Null, "TimelineHelpers.DuplicateItemsUsingCurrentEditMode");
            duplicate.Invoke(null, new object[] { director, director, group, into, time, "Duplicate" });
        }

        void Open(PlayableDirector director)
        {
            m_Window = TimelineEditor.GetOrCreateWindow();
            m_Window.SetTimeline(director);
            PropertyInfo state = m_Window.GetType().GetProperty("state", All);
            Assert.That(state, Is.Not.Null, "TimelineWindow.state");
            m_State = state.GetValue(m_Window);
            Assert.That(m_State.GetType().GetProperty("previewMode"), Is.Not.Null, "WindowState.previewMode");
            Assert.That(m_State.GetType().GetProperty("editSequence"), Is.Not.Null, "WindowState.editSequence");
            Assert.That((TimelineEditor.inspectedDirector == director, TimelineEditor.inspectedAsset == director.playableAsset), Is.EqualTo((true, true)), "the window shows the director");
        }

        bool Preview
        {
            get => (bool)m_State.GetType().GetProperty("previewMode").GetValue(m_State);
            set => m_State.GetType().GetProperty("previewMode").SetValue(m_State, value);
        }

        void Scrub(double t)
        {
            object sequence = m_State.GetType().GetProperty("editSequence").GetValue(m_State);
            sequence.GetType().GetProperty("time").SetValue(sequence, t);
            M(m_State.GetType(), "EvaluateImmediate").Invoke(m_State, null);
        }

        // ---- the scene

        static TargetRef Self => new TargetRef { m_Mode = TargetRefMode.Self, m_Path = "", m_Anchor = "" };

        static T Timed<T>(T item, float seconds, float delay = 0f) where T : FeedbackItem
        {
            item.m_Timing.m_Duration = seconds;
            item.m_Timing.m_Delay = delay;
            return item;
        }

        static PositionItem Line(float seconds, float delay = 0f)
        {
            TweenParams t = TweenParams.Default;
            t.m_To = new Vector4(1f, 0f, 0f, 0f);
            return Timed(new PositionItem { m_Target = Self, m_Motion = new TweenMotion { m_Params = t } }, seconds, delay);
        }

        static FeedbackPlayer Player(string name, params FeedbackItem[] items)
        {
            var p = new GameObject(name).AddComponent<FeedbackPlayer>();
            p.m_Items.AddRange(items);
            return p;
        }

        static PlayableDirector Director(string name, out TimelineAsset asset, string path)
        {
            var d = new GameObject(name).AddComponent<PlayableDirector>();
            d.playOnAwake = false;
            asset = ScriptableObject.CreateInstance<TimelineAsset>();
            if (path != null)
                AssetDatabase.CreateAsset(asset, path);
            d.playableAsset = asset;
            return d;
        }

        static FeedbackTrack Bind(PlayableDirector d, TimelineAsset asset, string name, FeedbackPlayer player)
        {
            FeedbackTrack track = asset.CreateTrack<FeedbackTrack>(null, name);
            if (player != null)
                d.SetGenericBinding(track, player);
            return track;
        }

        // Box — 0.5 s start delay + 1.25 s × 2 = 3 s · Open — a signal wait without a timeout (endless) · a track with none
        PlayableDirector Scene(out TimelineAsset asset, out FeedbackTrack box, out FeedbackTrack open, out FeedbackTrack none, out FeedbackPlayer player)
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            player = Player("Box", Line(1.25f));
            player.m_DurationMultiplier = 2f;
            player.m_StartDelay = 0.5f;
            FeedbackPlayer endless = Player("Open", Line(0.5f), new WaitForSignalItem());
            PlayableDirector d = Director("Director", out asset, TimelinePath);
            box = Bind(d, asset, "BoxTrack", player);
            open = Bind(d, asset, "OpenTrack", endless);
            none = Bind(d, asset, "NoneTrack", null);
            Assert.That(EditorSceneManager.SaveScene(scene, ScenePath), Is.True);
            return d;
        }

        // a user's edit as the inspector makes it — an undo group of its own
        static void Edit(Object target, string path, Action<SerializedProperty> set)
        {
            Undo.IncrementCurrentGroup();
            var serialized = new SerializedObject(target);
            set(serialized.FindProperty(path));
            serialized.ApplyModifiedProperties();
            Undo.IncrementCurrentGroup();
        }

        static string F(double v) => double.IsInfinity(v) ? "inf" : v.ToString("F3", CultureInfo.InvariantCulture);

        static uint Seed(TimelineClip clip) => ((FeedbackClip)clip.asset).m_Seed;

        static bool Allocates(TestDelegate run)
        {
            for (int i = 0; i < 5; i++)
            {
                if (!Is.AllocatingGCMemory().ApplyTo(run).IsSuccess)
                    return false;
            }
            return true;
        }

        // ---- EV-1 — a new clip

        [UnityTest]
        public IEnumerator TC01_ANewClipTakesItsPlayersListLengthAndName()
        {
            PlayableDirector d = Scene(out TimelineAsset asset, out FeedbackTrack box, out FeedbackTrack open, out FeedbackTrack none, out FeedbackPlayer player);
            Open(d);
            TimelineClip a = Create(box);
            TimelineClip o = Create(open);
            TimelineClip n = Create(none);
            string made = $"{F(a.duration)} {a.displayName} · {F(o.duration)} {o.displayName} · {F(n.duration)} {n.displayName}";
            bool prepared = player.Runtime != null;
            a.displayName = "Hit";
            for (int i = 0; i < 5; i++)
            {
                m_Window.Repaint();
                yield return null;
            }
            TimelineClip api = box.CreateClip<FeedbackClip>();
            var hidden = ScriptableObject.CreateInstance<TimelineAsset>();
            TimelineClip unseen = hidden.CreateTrack<FeedbackTrack>(null, "Hidden").CreateClip<FeedbackClip>();
            Object.DestroyImmediate(hidden);
            Assert.That((made, prepared, a.displayName, F(api.duration), F(unseen.duration)),
                Is.EqualTo(("3.000 Box · 5.000 Open · 5.000 FeedbackClip", false, "Hit", "3.000", "5.000")));
        }

        // ---- EV-2 — Match Content and the loop marks

        [Test]
        public void TC02_MatchContentAndTheLoopMarksTakeTheListsLength()
        {
            PlayableDirector d = Scene(out _, out FeedbackTrack box, out _, out _, out FeedbackPlayer player);
            Open(d);
            TimelineClip a = Create(box);
            a.duration = 9.0;
            Match(a);
            string matched = F(a.duration);
            var feedback = (FeedbackClip)a.asset;
            feedback.m_Loop = true;
            a.duration = 9.0;
            string loops = string.Join(",", LoopTimes(a).Select(F));
            Edit(player, Duration, p => p.floatValue = 2f);
            feedback.m_Loop = false;
            Match(a);
            Assert.That((matched, loops, F(a.duration)), Is.EqualTo(("3.000", "0.000,3.000,6.000", "4.500")));
        }

        // ---- EV-3 — a duplicate's seed

        [Test]
        public void TC03_ADuplicateOrAPasteDrawsANewSeed()
        {
            PlayableDirector d = Scene(out _, out FeedbackTrack box, out _, out _, out _);
            Open(d);
            TimelineClip a = Create(box);
            uint seed = Seed(a);
            Undo.IncrementCurrentGroup();
            Duplicate(d, a, box, 5.0);
            Undo.IncrementCurrentGroup();
            TimelineClip[] clips = box.GetClips().ToArray();
            uint copy = clips.Length == 2 ? Seed(clips.First(c => c != a)) : 0u;
            Undo.PerformUndo();
            TimelineClip kept = box.GetClips().Single();
            var other = ScriptableObject.CreateInstance<TimelineAsset>();
            AssetDatabase.CreateAsset(other, OtherPath);
            FeedbackTrack elsewhere = other.CreateTrack<FeedbackTrack>(null, "Other");
            Duplicate(d, kept, elsewhere, 0.0);
            uint pasted = elsewhere.GetClips().Any() ? Seed(elsewhere.GetClips().First()) : 0u;
            Assert.That((clips.Length, copy != 0u && copy != seed, box.GetClips().Count(), Seed(kept) == seed, pasted != 0u && pasted != seed),
                Is.EqualTo((2, true, 1, true, true)), $"{seed} · {copy} · {pasted}");
        }

        // ---- EV-13 — the asset's clip in the window's timeline

        [Test]
        public void TC13_TheLengthFollowsTheClipsTrackInTheWindowsTimeline()
        {
            PlayableDirector d = Scene(out TimelineAsset asset, out FeedbackTrack box, out _, out _, out _);
            FeedbackTrack longer = Bind(d, asset, "LongTrack", Player("Long", Line(2f)));
            FeedbackPlayer inner = Player("Inner", Line(1.5f));
            PlayableDirector child = Director("Child", out TimelineAsset childAsset, null);
            TimelineClip innerClip = Bind(child, childAsset, "Inner", inner).CreateClip<FeedbackClip>();
            ControlTrack control = asset.CreateTrack<ControlTrack>(null, "Control");
            TimelineClip host = control.CreateClip<ControlPlayableAsset>();
            host.start = 0.0;
            host.duration = 2.0;
            var id = new PropertyName("child");
            ((ControlPlayableAsset)host.asset).sourceGameObject.exposedName = id;
            d.SetReferenceValue(id, child.gameObject);
            Open(d);
            TimelineClip a = Create(box);
            var feedback = (FeedbackClip)a.asset;
            Undo.IncrementCurrentGroup();
            a.MoveToTrack(longer);
            Undo.IncrementCurrentGroup();
            string moved = F(feedback.duration);
            Undo.PerformUndo();
            TimelineClip back = box.GetClips().Single();
            string restored = F(((FeedbackClip)back.asset).duration);
            var other = ScriptableObject.CreateInstance<TimelineAsset>();
            AssetDatabase.CreateAsset(other, OtherPath);
            back.MoveToTrack(other.CreateTrack<FeedbackTrack>(null, "Other"));
            string left = F(feedback.duration);
            var innerAsset = (FeedbackClip)innerClip.asset;
            string outside = F(innerAsset.duration);
            M(m_State.GetType(), "SetCurrentSequence", typeof(TimelineAsset), typeof(PlayableDirector), typeof(TimelineClip)).Invoke(m_State, new object[] { childAsset, child, host });
            string entered = F(innerAsset.duration);
            Assert.That((moved, restored, left, outside, entered), Is.EqualTo(("2.000", "3.000", "inf", "inf", "1.500")));
        }

        // ---- EV-4 — a clip's texts

        [Test]
        public void TC04_AClipSaysWhyItDoesNotPlayAsSet()
        {
            PlayableDirector d = Scene(out TimelineAsset asset, out FeedbackTrack box, out FeedbackTrack open, out FeedbackTrack none, out _);
            var timeScale = Timed(new TimeScaleItem(), 0.5f);
            FeedbackPlayer slow = Player("Slow", Line(1f), timeScale);
            FeedbackPlayer ignore = Player("Ignore", Line(1f));
            ignore.m_Policy = PlayPolicy.Ignore;
            FeedbackPlayer fine = Player("Fine", Line(1f), Timed(new HitStopItem(), 0.1f), Timed(new ParticleControlItem(), 0.5f));
            FeedbackTrack slowTrack = Bind(d, asset, "Slow", slow);
            FeedbackTrack ignoreA = Bind(d, asset, "IgnoreA", ignore);
            FeedbackTrack ignoreB = Bind(d, asset, "IgnoreB", ignore);
            FeedbackTrack fineTrack = Bind(d, asset, "Fine", fine);
            var clips = new List<TimelineClip>
            {
                Clip(box, 0.0, 3.0, FeedbackClipTiming.Auto),
                Clip(box, 2.0, 3.0, FeedbackClipTiming.Auto),
                Clip(box, 6.0, 3.0, FeedbackClipTiming.Auto),
                Clip(open, 0.0, 3.0, FeedbackClipTiming.Driven),
                Clip(slowTrack, 0.0, 1.0, FeedbackClipTiming.Driven),
                Clip(slowTrack, 2.0, 1.0, FeedbackClipTiming.Triggered),
                Clip(ignoreA, 0.0, 1.0, FeedbackClipTiming.Auto),
                Clip(ignoreB, 0.5, 1.0, FeedbackClipTiming.Auto),
                Clip(none, 0.0, 1.0, FeedbackClipTiming.Auto),
                Clip(fineTrack, 0.0, 1.0, FeedbackClipTiming.Auto),
            };
            ((FeedbackClip)clips[9].asset).m_Seed = 7u;
            Open(d);
            var editor = new FeedbackClipEditor();
            List<string> errors = clips.Select(c => editor.GetClipOptions(c).errorText).ToList();
            string tooltip = editor.GetClipOptions(clips[9]).tooltip;
            m_Window.SetTimeline(asset);
            Assert.That(TimelineEditor.inspectedDirector, Is.Null, "the window shows the asset alone");
            ClipDrawOptions unknown = editor.GetClipOptions(clips[0]);
            string noDirector = editor.GetClipOptions(clips[8]).errorText;
            Assert.That(errors, Is.EqualTo(new[]
            {
                TimelinePlayerInfo.RestartText,
                TimelinePlayerInfo.RestartText,
                "",
                TimelinePlayerInfo.NotScrubbedText,
                TimelinePlayerInfo.TimeScaleText,
                "",
                TimelinePlayerInfo.IgnoreText,
                TimelinePlayerInfo.IgnoreText,
                TimelinePlayerInfo.NoPlayerText,
                "",
            }));
            Assert.That(tooltip, Is.EqualTo(string.Join("\n", TimelinePlayerInfo.AutoDrivenText, "List 1 s · 3 items", "Seed 7", TimelinePlayerInfo.SimulatedText, TimelinePlayerInfo.HitStopText)));
            Assert.That((unknown.errorText, unknown.tooltip, noDirector), Is.EqualTo(("", TimelinePlayerInfo.AutoText + "\nSeed " + Seed(clips[0]), "")));
        }

        static TimelineClip Clip(TrackAsset track, double start, double duration, FeedbackClipTiming timing)
        {
            TimelineClip clip = track.CreateClip<FeedbackClip>();
            clip.start = start;
            clip.duration = duration;
            ((FeedbackClip)clip.asset).m_Timing = timing;
            return clip;
        }

        // ---- EV-5 — a track's texts

        [Test]
        public void TC05_ATrackSaysWhatItsPlayersValidationFinds()
        {
            PlayableDirector d = Scene(out TimelineAsset asset, out FeedbackTrack box, out _, out FeedbackTrack none, out FeedbackPlayer player);
            FeedbackPlayer motionless = Player("Motionless", Timed(new PositionItem { m_Target = Self }, 1f));
            FeedbackPlayer off = Player("Off", Timed(new PositionItem { m_Target = Self }, 1f));
            off.gameObject.SetActive(false);
            FeedbackTrack a = Bind(d, asset, "Motionless", motionless);
            FeedbackTrack b = Bind(d, asset, "Off", off);
            Open(d);
            var editor = new FeedbackTrackEditor();
            var timeline = new TrackEditor();
            TrackDrawOptions problem = editor.GetTrackOptions(a, motionless);
            string offText = editor.GetTrackOptions(b, off).errorText;
            string fine = editor.GetTrackOptions(box, player).errorText;
            string unbound = editor.GetTrackOptions(none, null).errorText;
            string timelineOff = timeline.GetTrackOptions(b, off).errorText;
            Texture2D icon = AssetDatabase.LoadAssetAtPath<Texture2D>(FeedbackTrackEditor.IconPath);
            Assert.That((problem.errorText, offText == timelineOff && offText.Length > 0, fine, unbound, icon != null && problem.icon == icon),
                Is.EqualTo(("The player has 1 warning — see its Inspector.", true, "", timeline.GetTrackOptions(none, null).errorText, true)), offText);
        }

        // ---- EV-6 — the player's info: built again on an edit, an Undo, a link and a linked preset's edit; an ask that
        // finds it built allocates nothing

        [Test]
        public void TC06_ThePlayersInfoIsBuiltOnceAnEdit()
        {
            PlayableDirector d = Scene(out _, out FeedbackTrack box, out _, out _, out FeedbackPlayer player);
            Open(d);
            TimelineClip a = Create(box);
            var feedback = (FeedbackClip)a.asset;
            var rects = new List<TimelinePlayerInfo.StripRect>();
            TestDelegate ask = () =>
            {
                s_Length = TimelinePlayerInfo.Length(feedback);
                TimelinePlayerInfo.Texts(a, out _, out _);
                TimelinePlayerInfo.Strip(a, new Rect(0f, 0f, 200f, 20f), 0.0, 3.0, rects);
                s_Sink = TimelinePlayerInfo.Of(player).TrackText;
            };
            ask();
            int builds = TimelinePlayerInfo.Builds;
            string first = F(feedback.duration);
            ask();
            int again = TimelinePlayerInfo.Builds - builds;
            bool allocates = Allocates(ask);
            bool control = Allocates(() => s_Sink = new object());
            Edit(player, Duration, p => p.floatValue = 2f);
            string edited = F(feedback.duration);
            Undo.PerformUndo();
            string undone = F(feedback.duration);
            Undo.PerformRedo();
            string redone = F(feedback.duration);
            var preset = ScriptableObject.CreateInstance<FeedbackPreset>();
            preset.m_Items.Add(Line(0.4f));
            AssetDatabase.CreateAsset(preset, PresetPath);
            Edit(player, "m_Preset", p => p.objectReferenceValue = preset);
            string linked = F(feedback.duration);
            Edit(preset, Duration, p => p.floatValue = 0.9f);
            string presetEdited = F(feedback.duration);
            Assert.That((first, again, allocates, control), Is.EqualTo(("3.000", 0, false, true)));
            Assert.That((edited, undone, redone, linked, presetEdited), Is.EqualTo(("4.500", "3.000", "4.500", "1.300", "2.300")));
        }

        // ---- EV-7 — the bars on a clip's background

        static string Show(List<TimelinePlayerInfo.StripRect> rects)
            => string.Join(" ", rects.Select(r => string.Format(CultureInfo.InvariantCulture, "{0:0.#}-{1:0.#}@{2:0.#}-{3:0.#}a{4:0.###}", r.Rect.xMin, r.Rect.xMax, r.Rect.yMin, r.Rect.yMax, r.Color.a)));

        [Test]
        public void TC07_TheBarsFollowTheClipsLocalTime()
        {
            // two rows: 0 – 1 s · a delay 0 – 0.5 s, then 0.5 – 1 s
            TimelineModel two = TimelineModel.Build(new List<FeedbackItem> { Line(1f), Line(0.5f, 0.5f) }, 1f, 0f);
            var area = new Rect(0f, 0f, 100f, 20f);
            var rects = new List<TimelinePlayerInfo.StripRect>();
            var got = new List<string>();
            TimelinePlayerInfo.Place(two, area, 0.0, 2.0, false, false, rects);
            got.Add(Show(rects));
            TimelinePlayerInfo.Place(two, area, 0.0, 2.0, true, false, rects);
            got.Add(Show(rects));
            TimelinePlayerInfo.Place(two, area, 0.5, 1.5, false, false, rects);
            got.Add(Show(rects));
            TimelinePlayerInfo.Place(two, area, 0.0, 2.0, false, true, rects);
            got.Add(Show(rects));
            var endless = Line(1f);
            endless.m_Timing.m_Infinite = true;
            TimelinePlayerInfo.Place(TimelineModel.Build(new List<FeedbackItem> { endless }, 1f, 0f), area, 0.0, 2.0, true, false, rects);
            got.Add(Show(rects));
            var many = new List<FeedbackItem>();
            for (int i = 0; i < 30; i++)
                many.Add(Line(1f));
            TimelinePlayerInfo.Place(TimelineModel.Build(many, 1f, 0f), area, 0.0, 1.0, false, false, rects);
            got.Add(rects.Count + " " + string.Join(",", rects.Select(r => r.Rect.height.ToString(CultureInfo.InvariantCulture)).Distinct()));
            Assert.That(got, Is.EqualTo(new[]
            {
                "0-50@0-9a1 0-25@14-15a0.5 25-50@10-19a1",
                "0-50@0-9a1 0-25@14-15a0.5 25-50@10-19a1 50-100@0-9a1 50-75@14-15a0.5 75-100@10-19a1",
                "0-50@0-9a1 0-50@10-19a1",
                "0-50@0-9a0.45 0-25@14-15a0.225 25-50@10-19a0.45",
                "0-100@0-19a1",
                "10 2",
            }));
        }

        // ---- EV-8 — the inspector of a player the Timeline window's preview holds

        [Test]
        public void TC08_TheInspectorSaysTheTimelineWindowHoldsThePlayer()
        {
            PlayableDirector d = Scene(out TimelineAsset asset, out FeedbackTrack box, out _, out _, out FeedbackPlayer player);
            FeedbackPlayer other = Player("Other", Line(1f));
            TimelineClip clip = box.CreateClip<FeedbackClip>();
            clip.start = 0.0;
            clip.duration = 3.0;
            Open(d);
            Preview = true;
            Scrub(0.5);
            var held = new PreviewBar(player);
            var free = new PreviewBar(other);
            held.Refresh();
            free.Refresh();
            string on = Shown(held) + " | " + Shown(free);
            Preview = false;
            PreviewSession.Current?.Update();
            held.Refresh();
            Assert.That((on, Shown(held), PreviewSession.Current == null), Is.EqualTo((PreviewTexts.TrackHeld + " | ", "", true)));
        }

        static string Shown(PreviewBar bar) => bar.Notice.style.display == DisplayStyle.Flex ? bar.Notice.text : "";
    }
}
