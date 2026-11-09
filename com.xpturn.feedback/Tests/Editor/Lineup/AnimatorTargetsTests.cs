using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using xpTURN.Feedback.Editor;
using Object = UnityEngine.Object;

namespace xpTURN.Feedback.Tests.Editor
{
    // the values, the parameters, the trigger, the state, the refusals, the simulation, the preview, a missed end, the inspector,
    // the keep and the saved form (direct Tick on test contexts · the preview on the test's clock — two versions). The controllers
    // are the test's (FeedbackTestAnimator's layout); the Animation assembly's types are not named: a test that plays an animator
    // item is the dev project's. The traps: a pose is moved after the bind (the end's Rebind writes the last bind's defaults) · a
    // stage is judged by an offset (a Persist write is in Update anyway) · the preview ticks at 60 Hz and its first tick's step
    // is 0 (that tick is judged on a delayed item against a reference animator)
    public sealed class AnimatorTargetsTests
    {
        const float Step = 0.05f;
        const string Folder = "Assets/AnimatorTargetsTests";
        // the runtime test assembly's behaviour (a controller finds one by its MonoScript — not an editor assembly's)
        internal const string BehaviourName = "xpTURN.Feedback.Tests.AnimationTestBehaviour, xpTURN.Feedback.Tests.Animation";
        static readonly PlayArgs Back = PlayArgs.Default.WithDirection(PlayerDirection.Backward);
        static readonly int IdleHash = Animator.StringToHash("Idle"), OtherHash = Animator.StringToHash("Other"), UpHash = Animator.StringToHash("Up");
        readonly List<Object> m_Made = new List<Object>();
        readonly List<FeedbackScheduler> m_S = new List<FeedbackScheduler>();
        readonly List<FeedbackPlayer> m_P = new List<FeedbackPlayer>();
        readonly List<string> m_Logs = new List<string>();
        static double s_Now;
        Func<double> m_Clock;
        AnimatorController m_Ctrl;

        [SetUp]
        public void SetUp()
        {
            m_Clock = PreviewClock.Source;
            PreviewClock.Source = () => s_Now;
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Application.logMessageReceived += OnLog;
        }

        [TearDown]
        public void TearDown()
        {
            Application.logMessageReceived -= OnLog;
            PreviewSession.Current?.Stop();
            foreach (FeedbackPlayer p in m_P)
                if (p != null) p.ReleaseRuntime();
            foreach (FeedbackScheduler s in m_S)
                if (!s.IsShutdown) s.Shutdown();
            foreach (Object o in m_Made)
                if (o != null) Object.DestroyImmediate(o);
            m_Made.Clear(); m_S.Clear(); m_P.Clear(); m_Logs.Clear();
            m_Ctrl = null;
            AssetDatabase.DeleteAsset(Folder);
            PreviewClock.Source = m_Clock;
        }

        void OnLog(string message, string stack, LogType type) => m_Logs.Add(message);

        int Logged(string part) => m_Logs.Count(m => m.Contains(part));

        AnimatorController Controller(string name, AnimationClip first = null, bool rootZ = true)
        {
            if (!AssetDatabase.IsValidFolder(Folder))
                AssetDatabase.CreateFolder("Assets", "AnimatorTargetsTests");
            return FeedbackTestAnimator.Make(Folder + "/" + name + ".controller", first, rootZ);
        }

        AnimatorController Ctrl => m_Ctrl != null ? m_Ctrl : m_Ctrl = Controller("P");

        static AnimatorState State(AnimatorController c, string name) => c.layers[0].stateMachine.states.First(s => s.state.name == name).state;

        Animator Make(string name, RuntimeAnimatorController ctrl = null)
        {
            var go = new GameObject(name);
            m_Made.Add(go);
            new GameObject("Child").transform.SetParent(go.transform, false);
            go.transform.localPosition = new Vector3(7f, 0f, 0f);
            go.transform.GetChild(0).localPosition = new Vector3(0f, 5f, 0f);
            var an = go.AddComponent<Animator>();
            an.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            an.runtimeAnimatorController = ctrl ?? Ctrl;
            return an;
        }

        // posed after the bind — the bind's defaults are stale: only the record before the Rebind brings the pose back
        Animator Moved(string name, RuntimeAnimatorController ctrl = null)
        {
            Animator an = Make(name, ctrl);
            an.Rebind();
            an.transform.localPosition = new Vector3(9f, 0f, 0f);
            an.transform.GetChild(0).localPosition = new Vector3(2f, 6f, 4f);
            return an;
        }

        static void Pump(int n) { for (int i = 0; i < n; i++) { s_Now += 1.0 / 120.0; PreviewSession.Current?.Update(); } }
        static void Tick(FeedbackScheduler s, int n) { for (int i = 0; i < n; i++) s.Tick(Step); }
        FeedbackScheduler Sched() { var s = new FeedbackScheduler(ContextKind.Test); m_S.Add(s); return s; }

        FeedbackPlayer Player(FeedbackScheduler s, params FeedbackItem[] items)
        {
            var go = new GameObject("Player");
            m_Made.Add(go);
            FeedbackPlayer p = go.AddComponent<FeedbackPlayer>();
            p.m_Items.AddRange(items);
            if (s != null)
            {
                p.Bind(s);
                p.EnsureRuntime();
            }
            m_P.Add(p);
            return p;
        }

        static T With<T>(T item, FeedbackMotion motion, float duration, Object target) where T : TargetItem
        {
            item.m_Target = new TargetRef { m_Mode = TargetRefMode.Direct, m_Object = target, m_Path = "", m_Anchor = "" };
            item.m_Motion = motion;
            item.m_Timing.m_Duration = duration;
            return item;
        }

        static SetMotion Set() => new SetMotion { m_Params = new SetParams { m_Value = new Vector4(1f, 0, 0, 0) } };

        static TweenMotion TweenTo(float v)
        {
            var m = new TweenMotion();
            m.m_Params.m_Mode = TweenMode.To;
            m.m_Params.m_To = new Vector4(v, 0, 0, 0);
            return m;
        }

        static PunchMotion Punch(float amplitude) => new PunchMotion { m_Params = new PunchParams { m_Amplitude = new Vector4(amplitude, 0, 0, 0), m_Vibrato = 0.25f, m_Decay = 0f } };

        static PositionItem Keeper(Component target, float d)
        {
            var k = new PositionItem { m_Target = new TargetRef { m_Mode = TargetRefMode.Direct, m_Object = target.transform, m_Path = "", m_Anchor = "" }, m_Motion = new PunchMotion { m_Params = new PunchParams { m_Amplitude = new Vector4(0.01f, 0, 0, 0), m_Vibrato = 1f, m_Decay = 0f } } };
            k.m_Timing.m_Duration = d;
            return k;
        }

        static AnimatorParameterItem Param(Animator a, string name, AnimatorParameterKind kind)
        {
            AnimatorParameterItem item = With(new AnimatorParameterItem(), Set(), 0f, a);
            item.m_Parameter = name;
            item.m_Kind = kind;
            return item;
        }

        static int ShortHash(Animator a, int layer = 0) => a.GetCurrentAnimatorStateInfo(layer).shortNameHash;
        static float ClipTime(Animator a) => a.GetCurrentAnimatorStateInfo(0).normalizedTime * 10f;
        static int Bits(float f) => BitConverter.ToInt32(BitConverter.GetBytes(f), 0);

        static Dictionary<string, string> SnapOf(Object o)
        {
            var d = new Dictionary<string, string>();
            SerializedProperty it = new SerializedObject(o).GetIterator();
            while (it.Next(true))
            {
                switch (it.propertyType)
                {
                    case SerializedPropertyType.Float: d[it.propertyPath] = it.floatValue.ToString("R"); break;
                    case SerializedPropertyType.Integer: d[it.propertyPath] = it.longValue.ToString(); break;
                    case SerializedPropertyType.Boolean: d[it.propertyPath] = it.boolValue.ToString(); break;
                    case SerializedPropertyType.Enum: d[it.propertyPath] = it.intValue.ToString(); break;
                }
            }
            return d;
        }

        static bool Same(Dictionary<string, string> a, Dictionary<string, string> b) => a.Count == b.Count && a.All(kv => b.TryGetValue(kv.Key, out string v) && v == kv.Value);

        // ---- the requires name resolves — in the minimal project too (the editor references the module)
        [Test]
        public void AQ02_TheRequiresName()
        {
            foreach (Type t in new[] { typeof(AnimatorParameterItem), typeof(AnimatorFloatItem), typeof(AnimatorSpeedItem), typeof(AnimatorLayerWeightItem), typeof(AnimatorStateItem) })
                Assert.That(TargetChecks.Requires(t)?.FullName, Is.EqualTo("UnityEngine.Animator"), t.Name);
        }

        // ---- the values — as written: Speed (Tween · Punch · Spring) · Layer Weight · Float · the base's bits (Revert · Restore) ·
        // a game's write beside an offset · the engine's clamp read back (a weight is not clamped · a negative speed is 0)
        [Test, Category(OptionalAssemblyTests.DevProjectCategory)]
        public void AQ04a_TheValues()
        {
            FeedbackScheduler s = Sched();
            Animator a = Make("V");
            FeedbackPlayer p = Player(s, With(new AnimatorSpeedItem(), TweenTo(2f), 0.2f, a), Keeper(a, 2f));
            p.Play(); Tick(s, 6);
            Assert.That(a.speed, Is.EqualTo(2f).Within(1e-4f), "the speed's tween");
            p.Stop(StopMode.Revert); s.Tick(0f);
            Assert.That(Bits(a.speed), Is.EqualTo(Bits(1f)), "the base's bits");
            var spring = new SpringMotion();
            spring.m_Params.m_Value = Vector4.one * MotionDefaults.Bump;
            FeedbackPlayer pp = Player(s, With(new AnimatorSpeedItem(), Punch(0.5f), 1f, a), With(new AnimatorSpeedItem(), spring, 1f, a), Keeper(a, 2f));
            pp.Play(); Tick(s, 3);
            s.RunRevert();
            a.speed = 3f;
            s.RunUpdate(new FrameTime(Step, Step));
            float written = a.speed;
            s.RunLateApply();
            pp.Stop(StopMode.Revert); s.Tick(0f);
            Assert.That((written != 3f, Bits(a.speed)), Is.EqualTo((true, Bits(3f))), "the offsets over a game's write · the Revert leaves the game's");
            a.speed = 1f;
            FeedbackPlayer pw = Player(s, With(new AnimatorLayerWeightItem { m_Layer = "Upper" }, TweenTo(1f), 0.2f, a), With(new AnimatorFloatItem { m_Parameter = "Speed" }, TweenTo(0.8f), 0.2f, a), Keeper(a, 2f));
            pw.Play(); Tick(s, 6);
            Assert.That(a.GetLayerWeight(1), Is.EqualTo(1f).Within(1e-4f), "the weight");
            Assert.That(a.GetFloat("Speed"), Is.EqualTo(0.8f).Within(1e-4f), "the float");
            pw.Stop(StopMode.Revert); s.Tick(0f);
            Assert.That((Bits(a.GetLayerWeight(1)), Bits(a.GetFloat("Speed"))), Is.EqualTo((Bits(0f), Bits(0f))), "the bases' bits");
            FeedbackPlayer pc = Player(s, With(new AnimatorLayerWeightItem { m_Layer = "Upper" }, TweenTo(1.5f), 0.1f, a), With(new AnimatorSpeedItem(), TweenTo(-1f), 0.1f, a), Keeper(a, 2f));
            pc.Play(); Tick(s, 4);
            Assert.That((a.GetLayerWeight(1), a.speed), Is.EqualTo((1.5f, 0f)), "a weight as written · a negative speed is 0");
            pc.Stop(StopMode.Revert); s.Tick(0f);
            Assert.That((Bits(a.GetLayerWeight(1)), Bits(a.speed)), Is.EqualTo((Bits(0f), Bits(1f))), "the bases' bits after the clamp");
        }

        // a Float Parameter's Set and a Float's Tween share the parameter's slot — the tween's Revert goes back to the Set ·
        // the Set's Revert to the start
        [Test, Category(OptionalAssemblyTests.DevProjectCategory)]
        public void AQ04b_ASetAndATweenShareTheFloat()
        {
            FeedbackScheduler s = Sched();
            Animator a = Make("V2");
            AnimatorParameterItem set = Param(a, "Speed", AnimatorParameterKind.Float);
            set.m_Float = 0.5f;
            FeedbackPlayer pf = Player(s, set, Keeper(a, 2f));
            pf.Play(); s.Tick(0f);
            FeedbackPlayer pt = Player(s, With(new AnimatorFloatItem { m_Parameter = "Speed" }, TweenTo(1f), 0.1f, a), Keeper(a, 2f));
            pt.Play(); Tick(s, 4);
            pt.Stop(StopMode.Revert); s.Tick(0f);
            Assert.That(a.GetFloat("Speed"), Is.EqualTo(0.5f).Within(1e-4f), "the tween's Revert — the Set's value");
            pf.Stop(StopMode.Revert); s.Tick(0f);
            Assert.That(a.GetFloat("Speed"), Is.EqualTo(0f), "the Set's Revert — the start");
        }

        // ---- a Parameter's Set — as written: Bool · Int · Float · Trigger · Revert (a keeper) · Restore · backward
        [Test, Category(OptionalAssemblyTests.DevProjectCategory)]
        public void AQ06a_TheParameterSet()
        {
            FeedbackScheduler s = Sched();
            Animator a = Make("P");
            FeedbackPlayer pb = Player(s, Param(a, "On", AnimatorParameterKind.Bool), Keeper(a, 2f));
            pb.Play(); s.Tick(0f);
            bool on = a.GetBool("On");
            pb.Stop(StopMode.Revert); s.Tick(0f);
            Assert.That((on, a.GetBool("On")), Is.EqualTo((true, false)), "a bool · its Revert");
            AnimatorParameterItem i3 = Param(a, "Count", AnimatorParameterKind.Int);
            i3.m_Int = 3;
            FeedbackPlayer pi = Player(s, i3, Keeper(a, 2f));
            pi.Play(); s.Tick(0f);
            int three = a.GetInteger("Count");
            pi.Stop(StopMode.Revert); s.Tick(0f);
            Assert.That((three, a.GetInteger("Count")), Is.EqualTo((3, 0)), "an int · its Revert");
            AnimatorParameterItem f = Param(a, "Speed", AnimatorParameterKind.Float);
            f.m_Float = 0.25f;
            FeedbackPlayer pf = Player(s, f);
            pf.Play(); s.Tick(0f);
            float quarter = a.GetFloat("Speed");
            pf.Restore(); s.Tick(0f);
            Assert.That((quarter, a.GetFloat("Speed")), Is.EqualTo((0.25f, 0f)), "a float · Restore");
            FeedbackPlayer pg = Player(s, Param(a, "Go", AnimatorParameterKind.Trigger));
            pg.Play(); s.Tick(0f);
            Assert.That(a.GetBool("Go"), Is.True, "a trigger set");
            a.ResetTrigger("Go");
            FeedbackPlayer ph = Player(s, Param(a, "On", AnimatorParameterKind.Bool));
            ph.Play(); s.Tick(0f);
            ph.Play(in Back); s.Tick(0f);
            Assert.That(a.GetBool("On"), Is.False, "backward — the origin");
        }

        // the random pick is the play's seed's (the same seed the same value · an int's range takes both ends) · the
        // increment adds to the value now (two plays 2)
        [Test, Category(OptionalAssemblyTests.DevProjectCategory)]
        public void AQ06b_TheRandomAndTheIncrement()
        {
            FeedbackScheduler s = Sched();
            Animator a = Make("P2");
            AnimatorParameterItem rnd = Param(a, "Count", AnimatorParameterKind.Int);
            rnd.m_Pick = AnimatorValuePick.Random;
            rnd.m_Min = 0; rnd.m_Max = 5;
            FeedbackPlayer pr = Player(s, rnd);
            var seen = new List<int>();
            for (int k = 0; k < 6; k++) { pr.Play(PlayArgs.Default.WithSeed((uint)(k % 3 + 1))); s.Tick(0f); seen.Add(a.GetInteger("Count")); }
            Assert.That((seen[0] == seen[3], seen[1] == seen[4], seen[2] == seen[5], seen.All(v => v >= 0 && v <= 5)), Is.EqualTo((true, true, true, true)), string.Join(" ", seen));
            a.SetInteger("Count", 0);
            AnimatorParameterItem inc = Param(a, "Count", AnimatorParameterKind.Int);
            inc.m_Pick = AnimatorValuePick.Increment;
            FeedbackPlayer pn = Player(s, inc, Keeper(a, 2f));
            pn.Play(); s.Tick(0f);
            pn.Stop(StopMode.Hold); s.Tick(0f);
            pn.Play(); s.Tick(0f);
            Assert.That(a.GetInteger("Count"), Is.EqualTo(2), "two plays add two");
            pn.Stop(StopMode.Revert); s.Tick(0f);
            Assert.That(a.GetInteger("Count"), Is.EqualTo(1), "the Revert — the play's start");
        }

        // ---- a trigger's Revert clears it while still set — as written
        [Test, Category(OptionalAssemblyTests.DevProjectCategory)]
        public void AQ07a_TheTriggerRevert()
        {
            FeedbackScheduler s = Sched();
            Animator a = Make("T");
            a.Update(0f);
            FeedbackPlayer pg = Player(s, Param(a, "Go", AnimatorParameterKind.Trigger), Keeper(a, 2f));
            pg.Play(); s.Tick(0f);
            pg.Stop(StopMode.Revert); s.Tick(0f);
            Assert.That(a.GetBool("Go"), Is.False);
        }

        // consumed (the transition taken), the Revert changes nothing
        [Test, Category(OptionalAssemblyTests.DevProjectCategory)]
        public void AQ07b_AConsumedTrigger()
        {
            FeedbackScheduler s = Sched();
            Animator a = Make("T2");
            a.Update(0f);
            FeedbackPlayer pg = Player(s, Param(a, "Go", AnimatorParameterKind.Trigger), Keeper(a, 2f));
            pg.Play(); s.Tick(0f);
            a.Update(0.1f);
            pg.Stop(StopMode.Revert); s.Tick(0f);
            Assert.That((ShortHash(a) == OtherHash, a.GetBool("Go")), Is.EqualTo((true, false)));
        }

        // ---- Play State — as written: a transition of 0 plays · a cross fade is fading · the Revert puts the start back
        [Test, Category(OptionalAssemblyTests.DevProjectCategory)]
        public void AQ08a_PlayState()
        {
            FeedbackScheduler s = Sched();
            Animator a = Make("S");
            a.Update(0.3f);
            FeedbackPlayer p = Player(s, With(new AnimatorStateItem { m_State = "Other", m_Transition = 0f }, Set(), 0f, a), Keeper(a, 2f));
            p.Play(); s.Tick(0f);
            a.Update(0f);
            bool other = ShortHash(a) == OtherHash;
            p.Stop(StopMode.Revert); s.Tick(0f);
            a.Update(0f);
            Assert.That((other, ShortHash(a) == IdleHash), Is.EqualTo((true, true)), "Play · its Revert");
            FeedbackPlayer pc = Player(s, With(new AnimatorStateItem { m_State = "Other", m_Transition = 0.25f }, Set(), 0f, a), Keeper(a, 2f));
            pc.Play(); s.Tick(0f);
            a.Update(0.1f);
            bool fading = a.IsInTransition(0);
            pc.Stop(StopMode.Revert); s.Tick(0f);
            a.Update(0f);
            Assert.That((fading, a.IsInTransition(0), ShortHash(a) == IdleHash), Is.EqualTo((true, false, true)), "a cross fade · its Revert cuts it");
            FeedbackPlayer pr = Player(s, With(new AnimatorStateItem { m_State = "Other", m_Transition = 0f }, Set(), 0f, a));
            pr.Play(); s.Tick(0f);
            pr.Restore(); s.Tick(0f);
            a.Update(0f);
            Assert.That(ShortHash(a) == IdleHash, Is.True, "Restore");
        }

        // the start in seconds · a fixed-time transition · no layer named finds the layer with the state
        [Test, Category(OptionalAssemblyTests.DevProjectCategory)]
        public void AQ08b_TheStartTheFixedFadeTheLayer()
        {
            FeedbackScheduler s = Sched();
            Animator a = Make("S2");
            a.Update(0.3f);
            FeedbackPlayer p = Player(s, With(new AnimatorStateItem { m_State = "Other", m_Transition = 0f, m_Offset = 0.25f }, Set(), 0f, a), Keeper(a, 2f));
            p.Play(); s.Tick(0f);
            a.Update(0f);
            Assert.That(a.GetCurrentAnimatorStateInfo(0).normalizedTime, Is.EqualTo(0.25f).Within(0.01f), "0.25 s of a 1 s clip");
            p.Stop(StopMode.Revert); s.Tick(0f);
            // from Idle (a 10 s clip) — a normalized 0.25 would fade for 2.5 s
            a.Update(0f);
            FeedbackPlayer pc = Player(s, With(new AnimatorStateItem { m_State = "Other", m_Transition = 0.25f }, Set(), 0f, a), Keeper(a, 2f));
            pc.Play(); s.Tick(0f);
            a.Update(0.3f);
            Assert.That((a.IsInTransition(0), ShortHash(a) == OtherHash), Is.EqualTo((false, true)), "done after its 0.25 s");
            pc.Stop(StopMode.Revert); s.Tick(0f);
            FeedbackPlayer pu = Player(s, With(new AnimatorStateItem { m_State = "Up", m_Transition = 0f }, Set(), 0f, a));
            pu.Play(); s.Tick(0f);
            a.Update(0f);
            Assert.That((pu.Runtime.Binding(0) != null, ShortHash(a, 1) == UpHash), Is.EqualTo((true, true)), "the second layer's state");
        }

        // ---- the prepare refuses — as written: no controller · no parameter · another kind · no layer · no state; the engine's
        // warning for a missing parameter is not left
        [Test, Category(OptionalAssemblyTests.DevProjectCategory)]
        public void AQ09a_TheRefusals()
        {
            FeedbackScheduler s = Sched();
            Animator a = Make("R");
            Animator none = Make("Rnone");
            none.runtimeAnimatorController = null;
            FeedbackItem[] items =
            {
                Param(a, "Nope", AnimatorParameterKind.Float),
                Param(a, "Speed", AnimatorParameterKind.Bool),
                With(new AnimatorFloatItem { m_Parameter = "Nope" }, TweenTo(1f), 0.1f, a),
                With(new AnimatorLayerWeightItem { m_Layer = "Nope" }, TweenTo(1f), 0.1f, a),
                With(new AnimatorStateItem { m_State = "Nope" }, Set(), 0f, a),
                With(new AnimatorSpeedItem(), TweenTo(2f), 0.1f, none),
            };
            var refused = new List<int>();
            for (int i = 0; i < items.Length; i++)
            {
                FeedbackPlayer p = Player(s, items[i]);
                if (p.Runtime.Binding(0) == null)
                    refused.Add(i);
                p.Play(); Tick(s, 2);
            }
            Assert.That(refused, Is.EqualTo(new[] { 0, 1, 2, 3, 4, 5 }));
            Assert.That(Logged("does not exist"), Is.Zero, "no engine warning");
        }

        // an inactive animator — a Play State waits for its first run · a Layer Weight is refused (its key is the layer's
        // number)
        [Test, Category(OptionalAssemblyTests.DevProjectCategory)]
        public void AQ09b_TheStateWaitsTheWeightRefuses()
        {
            FeedbackScheduler s = Sched();
            Animator off = Make("Roff");
            off.gameObject.SetActive(false);
            FeedbackPlayer po = Player(s, With(new AnimatorStateItem { m_State = "Other", m_Transition = 0f }, Set(), 0f, off));
            FeedbackPlayer pw = Player(s, With(new AnimatorLayerWeightItem { m_Layer = "Upper" }, TweenTo(1f), 0.1f, off));
            off.gameObject.SetActive(true);
            po.Play(); s.Tick(0f);
            off.Update(0f);
            Assert.That((po.Runtime.Binding(0) != null, ShortHash(off) == OtherHash), Is.EqualTo((true, true)), "the state waited");
            Assert.That((pw.Runtime.Binding(0), Logged("the Animator is not initialized")), Is.EqualTo(((ItemBinding)null, 1)), "the weight refused — its reason in the prepare's warning");
        }

        static readonly string[] Inactive = { "never active", "turned off", "loaded off" };

        // an animator whose object is inactive — never active (not initialized) · turned off after it ran · off in a scene
        // that is opened
        Animator Off(string how)
        {
            if (how == "turned off")
            {
                Animator a = Make("Off");
                a.gameObject.SetActive(false);
                return a;
            }
            if (how == "loaded off")
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var go = new GameObject("Off");
            m_Made.Add(go);
            go.SetActive(false);
            var an = go.AddComponent<Animator>();
            an.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            an.runtimeAnimatorController = Ctrl;
            if (how == "never active")
                return an;
            string path = Folder + "/Off.unity";
            EditorSceneManager.SaveScene(SceneManager.GetActiveScene(), path);
            EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            return Resources.FindObjectsOfTypeAll<Animator>().First(x => x.name == "Off" && x.gameObject.scene.IsValid() && !EditorUtility.IsPersistent(x));
        }

        // an inactive animator's parameters — Bool · Int · Float · Trigger · a Float Parameter's tween prepare with no exception
        // and no warning, play quietly while it is off and write once it is on
        [Test, Category(OptionalAssemblyTests.DevProjectCategory)]
        public void AQ09c_AnInactiveAnimatorsParameters()
        {
            var items = new (string what, Func<Animator, FeedbackItem> item, Func<Animator, bool> written)[]
            {
                ("a bool", a => Param(a, "On", AnimatorParameterKind.Bool), a => a.GetBool("On")),
                ("an int", a => { AnimatorParameterItem i = Param(a, "Count", AnimatorParameterKind.Int); i.m_Int = 3; return i; }, a => a.GetInteger("Count") == 3),
                ("a float", a => { AnimatorParameterItem i = Param(a, "Speed", AnimatorParameterKind.Float); i.m_Float = 0.25f; return i; }, a => Mathf.Abs(a.GetFloat("Speed") - 0.25f) < 1e-4f),
                ("a trigger", a => Param(a, "Go", AnimatorParameterKind.Trigger), a => a.GetBool("Go")),
                ("a float parameter", a => With(new AnimatorFloatItem { m_Parameter = "Speed" }, TweenTo(0.8f), 0.1f, a), a => Mathf.Abs(a.GetFloat("Speed") - 0.8f) < 1e-4f),
            };
            foreach (string how in Inactive)
            {
                foreach ((string what, Func<Animator, FeedbackItem> item, Func<Animator, bool> written) in items)
                {
                    Animator off = Off(how);
                    FeedbackScheduler s = Sched();
                    m_Logs.Clear();
                    FeedbackPlayer p = Player(s, item(off));
                    string at = how + " · " + what;
                    Assert.That(p.Runtime.Binding(0), Is.Not.Null, at + " — prepared");
                    Assert.That(m_Logs, Is.Empty, at + " — no exception, no warning at the prepare");
                    p.Play(); Tick(s, 3);
                    Assert.That(m_Logs, Is.Empty, at + " — quiet while it is off");
                    off.gameObject.SetActive(true);
                    p.Play(); Tick(s, 3);
                    Assert.That((written(off), m_Logs.Count), Is.EqualTo((true, 0)), at + " — written once it is on " + string.Join(" | ", m_Logs));
                }
            }
        }

        // an inactive animator whose controller has no parameters lists none, as a player's inactive animator does — the
        // parameter items prepare, their names are judged at the first run once it is on, and nothing is written: no engine
        // warning
        [Test, Category(OptionalAssemblyTests.DevProjectCategory)]
        public void AQ09e_AJudgmentAtTheFirstRun()
        {
            if (!AssetDatabase.IsValidFolder(Folder))
                AssetDatabase.CreateFolder("Assets", "AnimatorTargetsTests");
            AnimatorController none = AnimatorController.CreateAnimatorControllerAtPath(Folder + "/None.controller");
            var go = new GameObject("Bare");
            m_Made.Add(go);
            go.SetActive(false);
            var bare = go.AddComponent<Animator>();
            bare.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            bare.runtimeAnimatorController = none;
            Assert.That((bare.isInitialized, bare.parameters.Length), Is.EqualTo((false, 0)), "it lists none");
            FeedbackScheduler s = Sched();
            AnimatorParameterItem count = Param(bare, "Count", AnimatorParameterKind.Int);
            count.m_Int = 3;
            m_Logs.Clear();
            FeedbackPlayer[] players =
            {
                Player(s, count),
                Player(s, Param(bare, "On", AnimatorParameterKind.Trigger)),
                Player(s, With(new AnimatorFloatItem { m_Parameter = "Speed" }, TweenTo(0.8f), 0.1f, bare)),
            };
            Assert.That(players.Select(p => p.Runtime.Binding(0) != null), Is.All.True, "prepared — the judgment waits");
            go.SetActive(true);
            foreach (FeedbackPlayer p in players)
                p.Play();
            Tick(s, 4);
            Assert.That(m_Logs, Is.Empty, "nothing written — no engine warning");
        }

        // an inactive animator still refuses a name it lacks, a name in another case and another kind — the reason in the
        // prepare's warning, no exception
        [Test, Category(OptionalAssemblyTests.DevProjectCategory)]
        public void AQ09d_AnInactiveAnimatorStillRefuses()
        {
            foreach (string how in Inactive)
            {
                Animator off = Off(how);
                FeedbackScheduler s = Sched();
                m_Logs.Clear();
                FeedbackItem[] items =
                {
                    Param(off, "Nope", AnimatorParameterKind.Float),
                    Param(off, "speed", AnimatorParameterKind.Float),
                    Param(off, "Speed", AnimatorParameterKind.Bool),
                    With(new AnimatorFloatItem { m_Parameter = "Nope" }, TweenTo(1f), 0.1f, off),
                };
                var refused = new List<int>();
                for (int i = 0; i < items.Length; i++)
                    if (Player(s, items[i]).Runtime.Binding(0) == null)
                        refused.Add(i);
                Assert.That(refused, Is.EqualTo(new[] { 0, 1, 2, 3 }), how);
                Assert.That((Logged("no parameter 'Nope'"), Logged("no parameter 'speed'"), Logged("'Speed' is a Float parameter — the item's is Bool")), Is.EqualTo((2, 1, 1)), how + " — the reasons");
                Assert.That(m_Logs.Where(l => l.Contains("Exception") || l.Contains("not playing an AnimatorController")), Is.Empty, how + " — no exception, no engine warning");
            }
        }

        // ---- the simulation's Animator row — as written: driven · the pose, the speed back · serialized · clean
        [Test, Category(OptionalAssemblyTests.DevProjectCategory)]
        public void AQ10a_TheSimulation()
        {
            Animator a = Make("Sim");
            Dictionary<string, string> animator = SnapOf(a), child = SnapOf(a.transform.GetChild(0));
            FeedbackPlayer player = Player(null, With(new AnimatorSpeedItem(), TweenTo(2f), 1f, a));
            s_Now += 1.0;
            Assert.That(PreviewSession.Start(player, default), Is.EqualTo(PreviewRefusal.None));
            Pump(24);
            float y = a.transform.GetChild(0).localPosition.y;
            Assert.That(y > 0.001f && y < 1f, Is.True, "driven — the clip's child " + y);
            PreviewSession.Current?.Stop();
            Assert.That((a.transform.localPosition.x, a.transform.GetChild(0).localPosition.y, a.speed), Is.EqualTo((7f, 5f, 1f)), "the pose · the speed back");
            Assert.That((Same(animator, SnapOf(a)), Same(child, SnapOf(a.transform.GetChild(0))), SceneManager.GetActiveScene().isDirty), Is.EqualTo((true, true, false)), "serialized · clean");
            // an inactive animator is not driven
            Animator off = Make("SimOff");
            off.gameObject.SetActive(false);
            FeedbackPlayer op = Player(null, With(new AnimatorSpeedItem(), TweenTo(1f), 1f, off), With(new AnimatorSpeedItem(), TweenTo(1f), 1f, Make("SimOn")));
            s_Now += 1.0;
            PreviewSession.Start(op, default);
            Pump(12);
            off.gameObject.SetActive(true);
            Assert.That(ClipTime(off), Is.LessThan(0.01f), "an inactive one is not driven");
        }

        // the tick's write goes into that tick's drive (a delayed item — the first tick's step is 0 — against a reference) ·
        // an offset (Punch) is seen by the drive (the Update stage)
        [Test, Category(OptionalAssemblyTests.DevProjectCategory)]
        public void AQ10b_TheTicksWriteAndTheStage()
        {
            Animator q = Make("SimTick");
            Animator rq = Make("SimTickRef");
            AnimatorSpeedItem quick = With(new AnimatorSpeedItem(), TweenTo(4f), 0.001f, q);
            quick.m_Timing.m_Delay = 0.05f;
            FeedbackPlayer qp = Player(null, quick, Param(rq, "Speed", AnimatorParameterKind.Float), Keeper(q, 1f));
            s_Now += 1.0;
            PreviewSession.Start(qp, default);
            float ratio = -1f;
            for (int k = 0; k < 40 && PreviewSession.Current != null; k++)
            {
                float tq = ClipTime(q), tr = ClipTime(rq);
                int ticks = PreviewSession.Current.Ticks;
                Pump(1);
                if (PreviewSession.Current == null || PreviewSession.Current.Ticks == ticks)
                    continue;
                if (q.speed > 3f)
                {
                    float dr = ClipTime(rq) - tr;
                    ratio = dr > 0f ? (ClipTime(q) - tq) / dr : -2f;
                    break;
                }
            }
            Assert.That(ratio, Is.GreaterThan(2.5f), "the tick the speed came in moves the clip 4 × the reference's");
            PreviewSession.Current?.Stop();
            Animator o = Make("SimPunch");
            Animator ro = Make("SimPunchRef");
            FeedbackPlayer op = Player(null, With(new AnimatorSpeedItem(), Punch(2f), 1f, o), Param(ro, "Speed", AnimatorParameterKind.Float));
            s_Now += 1.0;
            PreviewSession.Start(op, default);
            Pump(24);
            float to = ClipTime(o), tro = ClipTime(ro);
            Pump(36);
            float offset = (ClipTime(o) - to) / Mathf.Max(1e-6f, ClipTime(ro) - tro);
            Assert.That(offset, Is.GreaterThan(1.3f), "the punch is in the drive — " + offset);
        }

        // a pose moved after the bind comes back (the record before the Rebind) · a Parameter's Set alone records (the
        // simulation learns the animator)
        [Test, Category(OptionalAssemblyTests.DevProjectCategory)]
        public void AQ10c_ThePoseAfterTheBindAndASetAlone()
        {
            Animator m = Moved("SimMoved");
            FeedbackPlayer mp = Player(null, With(new AnimatorSpeedItem(), TweenTo(2f), 1f, m));
            s_Now += 1.0;
            PreviewSession.Start(mp, default);
            Pump(24);
            PreviewSession.Current?.Stop();
            // the child's clip moves its y only — the animator writes its whole position (x · z the bind's defaults)
            Assert.That((m.transform.localPosition, m.transform.GetChild(0).localPosition), Is.EqualTo((new Vector3(9f, 0f, 0f), new Vector3(2f, 6f, 4f))), "the pose set after the bind");
            Animator f = Make("SimSet");
            AnimatorParameterItem set = Param(f, "Speed", AnimatorParameterKind.Float);
            set.m_Float = 0.5f;
            FeedbackPlayer fp = Player(null, set, Keeper(f, 1f));
            s_Now += 1.0;
            PreviewSession.Start(fp, default);
            Pump(24);
            Assert.That(ClipTime(f), Is.GreaterThan(0.1f), "a Set alone is driven");
        }

        // unscaled under a hit stop · the start at the default state (Rebind) · the events off and back (an event without a
        // receiver logs an error — none in the preview, one after) · the parameters back (the end's Rebind)
        [Test, Category(OptionalAssemblyTests.DevProjectCategory)]
        public void AQ10d_UnscaledTheDefaultTheEventsTheParameters()
        {
            Animator u = Make("SimU");
            u.updateMode = AnimatorUpdateMode.UnscaledTime;
            Animator n = Make("SimN");
            var stop = new HitStopItem { m_FreezeDuration = 1f, m_TimeScale = 0f, m_MinimumTimeScale = 0f };
            FeedbackPlayer hp = Player(null, With(new AnimatorSpeedItem(), TweenTo(1f), 1f, u), With(new AnimatorSpeedItem(), TweenTo(1f), 1f, n), stop);
            s_Now += 1.0;
            PreviewSession.Start(hp, default);
            Pump(24);
            Assert.That((ClipTime(u) > 0.1f, ClipTime(n) < 0.05f), Is.EqualTo((true, true)), "the unscaled one runs in a hit stop · the scaled one stops");
            PreviewSession.Current?.Stop();
            AnimatorController events = Controller("E");
            AnimationUtility.SetAnimationEvents((AnimationClip)State(events, "Idle").motion, new[] { new AnimationEvent { time = 0.05f, functionName = "OnAnimatorTestEvent" } });
            Animator d = Make("SimD", events);
            d.Play(OtherHash, 0, 0f);
            d.Update(0f);
            FeedbackPlayer dp = Player(null, With(new AnimatorSpeedItem(), TweenTo(2f), 1f, d), Param(d, "On", AnimatorParameterKind.Bool));
            s_Now += 1.0;
            PreviewSession.Start(dp, default);
            Pump(12);
            Assert.That(ShortHash(d) == IdleHash, Is.True, "the default state");
            Pump(12);
            PreviewSession.Current?.Stop();
            Assert.That((Logged("OnAnimatorTestEvent"), d.fireEvents, d.GetBool("On")), Is.EqualTo((0, true, false)), "no event · the events back · the parameter back");
            LogAssert.Expect(LogType.Error, new Regex("OnAnimatorTestEvent"));
            d.Update(0.1f);
        }

        // an animator in another scene — its scene is written down (the session ends if it closes)
        [Test, Category(OptionalAssemblyTests.DevProjectCategory)]
        public void AQ10e_AnotherScene()
        {
            if (!AssetDatabase.IsValidFolder(Folder))
                AssetDatabase.CreateFolder("Assets", "AnimatorTargetsTests");
            EditorSceneManager.SaveScene(SceneManager.GetActiveScene(), Folder + "/Main.unity");
            Scene extra = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            Animator e = Make("Extra");
            SceneManager.MoveGameObjectToScene(e.gameObject, extra);
            FeedbackPlayer ep = Player(null, With(new AnimatorSpeedItem(), TweenTo(2f), 1f, e));
            SceneManager.MoveGameObjectToScene(ep.gameObject, SceneManager.GetSceneAt(0));
            s_Now += 1.0;
            PreviewSession.Start(ep, default);
            Pump(6);
            Assert.That(PreviewSession.Current != null && PreviewSession.Current.Touches(extra), Is.True);
            PreviewSession.Current?.Stop();
            Object.DestroyImmediate(e.gameObject);
            EditorSceneManager.CloseScene(extra, true);
        }

        // ---- the preview — as written: no scrub · the end of its plays · the behaviours logged once · with a particle
        // system in one session · a save in the session keeps the file
        [Test, Category(OptionalAssemblyTests.DevProjectCategory)]
        public void AQ11a_ThePreview()
        {
            AnimatorController smb = Controller("B");
            State(smb, "Idle").AddStateMachineBehaviour(Type.GetType(BehaviourName, true));
            Animator a = Make("Pv", smb);
            var ps = new GameObject("PvSystem").AddComponent<ParticleSystem>();
            m_Made.Add(ps.gameObject);
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ParticleSystem.MainModule main = ps.main;
            main.playOnAwake = false;
            main.duration = 100f;
            var control = With(new ParticleControlItem { m_Action = ParticleAction.Play }, Set(), 0f, ps);
            FeedbackPlayer player = Player(null, With(new AnimatorSpeedItem(), TweenTo(2f), 0.3f, a), control);
            string path = Folder + "/Pv.unity";
            EditorSceneManager.SaveScene(SceneManager.GetActiveScene(), path);
            string saved = System.IO.File.ReadAllText(path);
            m_Logs.Clear();
            s_Now += 1.0;
            PreviewSession.Start(player, default);
            Pump(12);
            Assert.That(PreviewSession.Current.ScrubState(out _, out _, out _), Is.EqualTo(SeekRefusal.Simulated), "no scrub");
            Assert.That(PreviewTexts.Of(SeekRefusal.Simulated), Does.Contain("animators"), "the bar names the animators");
            Assert.That((ClipTime(a) > 0f, ps.time > 0f), Is.EqualTo((true, true)), "the animator and the system in one session");
            EditorSceneManager.SaveScene(SceneManager.GetActiveScene(), path);
            Assert.That(System.IO.File.ReadAllText(path), Is.EqualTo(saved), "a save keeps the file");
            PreviewSession.Current.Stop();
            Assert.That(Logged("state machine behaviours of Pv run in the preview"), Is.EqualTo(1), "logged once");
            FeedbackPlayer ap = Player(null, With(new AnimatorSpeedItem(), TweenTo(2f), 0.3f, Make("PvEnd")));
            s_Now += 1.0;
            PreviewSession.Start(ap, default);
            Pump(2);
            Assert.That(PreviewSession.Current.ScrubState(out _, out _, out _), Is.EqualTo(SeekRefusal.Simulated), "an animator alone refuses the scrub");
            int pumps = 2;
            while (PreviewSession.Current != null && pumps < 240) { Pump(1); pumps++; }
            Assert.That((PreviewSession.LastEnd, pumps < 60), Is.EqualTo((PreviewEndReason.AutoEnd, true)), "the end of its plays — " + pumps);
        }

        // root motion (the root's place and turn back — a controller whose clips don't write the root's depth) · a
        // humanoid (every transform recorded — the muscles are no clip bindings)
        [Test, Category(OptionalAssemblyTests.DevProjectCategory)]
        public void AQ11b_RootMotionAndAHumanoid()
        {
            Animator r = Moved("PvRoot", Controller("N", rootZ: false));
            r.applyRootMotion = true;
            r.transform.rotation = Quaternion.Euler(0f, 30f, 0f);
            FeedbackPlayer rp = Player(null, With(new AnimatorSpeedItem(), TweenTo(2f), 1.5f, r));
            s_Now += 1.0;
            PreviewSession.Start(rp, default);
            Pump(60);
            PreviewSession.Current?.Stop();
            Assert.That((r.transform.localPosition.x, r.transform.localPosition.z, Mathf.Abs(r.transform.eulerAngles.y - 30f) < 1e-3f), Is.EqualTo((9f, 0f, true)), "the root back");
            var root = new GameObject("PvHuman");
            m_Made.Add(root);
            var bones = new Dictionary<string, Transform>();
            void Bone(string n, string parent, Vector3 pos) { var g = new GameObject(n); g.transform.SetParent(parent == null ? root.transform : bones[parent], false); g.transform.position = pos; bones[n] = g.transform; }
            Bone("Hips", null, new Vector3(0, 1f, 0)); Bone("Spine", "Hips", new Vector3(0, 1.15f, 0)); Bone("Chest", "Spine", new Vector3(0, 1.3f, 0)); Bone("Neck", "Chest", new Vector3(0, 1.5f, 0)); Bone("Head", "Neck", new Vector3(0, 1.6f, 0));
            foreach (string sd in new[] { "Left", "Right" })
            {
                float x = sd == "Left" ? -1f : 1f;
                Bone(sd + "UpperLeg", "Hips", new Vector3(0.1f * x, 0.95f, 0)); Bone(sd + "LowerLeg", sd + "UpperLeg", new Vector3(0.1f * x, 0.5f, 0)); Bone(sd + "Foot", sd + "LowerLeg", new Vector3(0.1f * x, 0.08f, 0));
                Bone(sd + "Shoulder", "Chest", new Vector3(0.05f * x, 1.45f, 0)); Bone(sd + "UpperArm", sd + "Shoulder", new Vector3(0.2f * x, 1.45f, 0)); Bone(sd + "LowerArm", sd + "UpperArm", new Vector3(0.45f * x, 1.45f, 0)); Bone(sd + "Hand", sd + "LowerArm", new Vector3(0.7f * x, 1.45f, 0));
            }
            HumanBone[] human = bones.Keys.Select(n => (n, HumanTrait.BoneName.FirstOrDefault(h => h.Replace(" ", "") == n))).Where(t => t.Item2 != null).Select(t => new HumanBone { humanName = t.Item2, boneName = t.n, limit = new HumanLimit { useDefaultValues = true } }).ToArray();
            SkeletonBone[] skeleton = new[] { new SkeletonBone { name = root.name, rotation = Quaternion.identity, scale = Vector3.one } }.Concat(bones.Values.Select(t => new SkeletonBone { name = t.name, position = t.localPosition, rotation = t.localRotation, scale = t.localScale })).ToArray();
            Avatar avatar = AvatarBuilder.BuildHumanAvatar(root, new HumanDescription { human = human, skeleton = skeleton, upperArmTwist = 0.5f, lowerArmTwist = 0.5f, upperLegTwist = 0.5f, lowerLegTwist = 0.5f, armStretch = 0.05f, legStretch = 0.05f });
            m_Made.Add(avatar);
            var clip = new AnimationClip { name = "H" };
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("", typeof(Animator), "Spine Front-Back"), AnimationCurve.Linear(0, 0, 1, 1));
            var an = root.AddComponent<Animator>();
            an.avatar = avatar;
            an.runtimeAnimatorController = Controller("H", first: clip);
            an.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            an.Rebind();
            bones["Spine"].localRotation = Quaternion.Euler(10f, 0f, 0f);
            Quaternion spine = bones["Spine"].localRotation;
            FeedbackPlayer hp = Player(null, With(new AnimatorSpeedItem(), TweenTo(2f), 1.5f, an));
            s_Now += 1.0;
            PreviewSession.Start(hp, default);
            Pump(60);
            float moved = Quaternion.Angle(spine, bones["Spine"].localRotation);
            PreviewSession.Current?.Stop();
            Assert.That((moved > 1f, Quaternion.Angle(spine, bones["Spine"].localRotation) < 0.01f), Is.EqualTo((true, true)), "the humanoid's spine moved and came back — " + moved);
        }

        // the preview of a player whose animator is off in its scene (a sample's hidden tab) — it starts and runs with no
        // exception and no warning
        [Test, Category(OptionalAssemblyTests.DevProjectCategory)]
        public void AQ11c_ThePreviewOfAnInactiveAnimator()
        {
            Animator off = Off("loaded off");
            AnimatorParameterItem count = Param(off, "Count", AnimatorParameterKind.Int);
            count.m_Int = 3;
            FeedbackPlayer player = Player(null, count, With(new AnimatorFloatItem { m_Parameter = "Speed" }, TweenTo(0.8f), 0.2f, off));
            m_Logs.Clear();
            s_Now += 1.0;
            Assert.That(PreviewSession.Start(player, default), Is.EqualTo(PreviewRefusal.None));
            Pump(24);
            PreviewSession.Current?.Stop();
            Assert.That(m_Logs, Is.Empty, "no exception, no warning");
        }

        // ---- a missed end — the recovery's pass during the session (the true reload is in PreviewReloadTests) stops the driver:
        // the clip's transforms back (the speed and the events stay the preview's)
        [Test, Category(OptionalAssemblyTests.DevProjectCategory)]
        public void AQ12a_TheRecoveryPutsThePoseBack()
        {
            Animator a = Moved("Recover");
            FeedbackPlayer player = Player(null, With(new AnimatorSpeedItem(), TweenTo(2f), 2f, a));
            s_Now += 1.0;
            PreviewSession.Start(player, default);
            Pump(24);
            Assert.That(a.transform.GetChild(0).localPosition.y, Is.Not.EqualTo(6f), "driven");
            PreviewRecovery.Run();
            Assert.That((a.transform.localPosition, a.transform.GetChild(0).localPosition), Is.EqualTo((new Vector3(9f, 0f, 0f), new Vector3(2f, 6f, 4f))), "the pose back");
            PreviewSession.Current?.Stop();
        }

        // ---- the inspector — no direct write on a Parameter, a Play State · a Parameter's fields by its kind and pick · the
        // names · the add menu's defaults (a speed's tween to half, a punch of half; a weight's and a float's tween to 1)
        [Test]
        public void AQ18a_TheInspectorAndTheDefaults()
        {
            Assert.That((ItemRules.Visible(new AnimatorParameterItem(), "m_DirectWrite"), ItemRules.Visible(new AnimatorStateItem(), "m_DirectWrite")), Is.EqualTo((false, false)), "no direct write on the instants");
            Assert.That(new FeedbackItem[] { new AnimatorFloatItem(), new AnimatorSpeedItem(), new AnimatorLayerWeightItem() }.Select(i => ItemRules.Visible(i, "m_DirectWrite")), Is.All.True, "the values write directly");
            string[] Shown(AnimatorParameterKind kind, AnimatorValuePick pick)
            {
                var item = new AnimatorParameterItem { m_Kind = kind, m_Pick = pick };
                return new[] { "m_Pick", "m_Bool", "m_Int", "m_Float", "m_Min", "m_Max", "m_Step" }.Where(f => ItemRules.Visible(item, f)).ToArray();
            }
            Assert.That(Shown(AnimatorParameterKind.Trigger, AnimatorValuePick.Random), Is.Empty, "a trigger");
            Assert.That(Shown(AnimatorParameterKind.Bool, AnimatorValuePick.Value), Is.EqualTo(new[] { "m_Pick", "m_Bool" }), "a bool's value");
            Assert.That(Shown(AnimatorParameterKind.Bool, AnimatorValuePick.Random), Is.EqualTo(new[] { "m_Pick" }), "a bool's half and half");
            Assert.That(Shown(AnimatorParameterKind.Int, AnimatorValuePick.Value), Is.EqualTo(new[] { "m_Pick", "m_Int" }), "an int's value");
            Assert.That(Shown(AnimatorParameterKind.Int, AnimatorValuePick.Random), Is.EqualTo(new[] { "m_Pick", "m_Min", "m_Max" }), "an int's range");
            Assert.That(Shown(AnimatorParameterKind.Float, AnimatorValuePick.Value), Is.EqualTo(new[] { "m_Pick", "m_Float" }), "a float's value");
            Assert.That(Shown(AnimatorParameterKind.Float, AnimatorValuePick.Increment), Is.EqualTo(new[] { "m_Pick", "m_Step" }), "a float's step");
            Assert.That((ItemRules.Label(new AnimatorStateItem(), "m_Transition"), ItemRules.Label(new AnimatorStateItem(), "m_Offset"), ItemRules.Label(new AnimatorStateItem(), "m_Layer"), ItemRules.Label(new AnimatorLayerWeightItem(), "m_Layer")),
                Is.EqualTo(("Transition (s · 0 = Play)", "Start (s)", "Layer (empty = the first with the state)", (string)null)));
            Assert.That(new AnimatorStateItem().m_Transition, Is.EqualTo(0.1f), "a cross fade of 0.1 s");
            Assert.That(((TweenMotion)MotionDefaults.Create(typeof(AnimatorSpeedItem), MotionKind.Tween, out _)).m_Params.m_To.x, Is.EqualTo(0.5f), "a speed's tween to half");
            Assert.That(((PunchMotion)MotionDefaults.Create(typeof(AnimatorSpeedItem), MotionKind.Punch, out _)).m_Params.m_Amplitude.x, Is.EqualTo(0.5f), "a speed's punch of half");
            Assert.That(((SpringMotion)MotionDefaults.Create(typeof(AnimatorSpeedItem), MotionKind.Spring, out _)).m_Params.m_Value.x, Is.EqualTo(MotionDefaults.Bump), "a speed's bump — the common one (a peak near half)");
            Assert.That(((TweenMotion)MotionDefaults.Create(typeof(AnimatorLayerWeightItem), MotionKind.Tween, out _)).m_Params.m_To.x, Is.EqualTo(1f), "a weight's tween to 1");
            Assert.That(((TweenMotion)MotionDefaults.Create(typeof(AnimatorFloatItem), MotionKind.Tween, out _)).m_Params.m_To.x, Is.EqualTo(1f), "a float's tween to 1");
        }

        // the pick fields read the target's controller — a Parameter's of its kind · a Float Parameter's floats · the named layer's
        // states (every layer's with none) · the layers · none without a controller (the text field stays) · the body puts them in
        [Test, Category(OptionalAssemblyTests.DevProjectCategory)]
        public void AQ18b_ThePickFields()
        {
            Animator a = Make("Pick");
            var param = new AnimatorParameterItem { m_Kind = AnimatorParameterKind.Bool, m_Target = new TargetRef { m_Mode = TargetRefMode.Self, m_Path = "", m_Anchor = "" }, m_Motion = Set() };
            var flt = new AnimatorFloatItem { m_Target = param.m_Target, m_Motion = TweenTo(1f) };
            var state = new AnimatorStateItem { m_Layer = "Upper", m_Target = param.m_Target, m_Motion = Set() };
            var any = new AnimatorStateItem { m_Target = param.m_Target, m_Motion = Set() };
            var weight = new AnimatorLayerWeightItem { m_Target = param.m_Target, m_Motion = TweenTo(1f) };
            FeedbackPlayer host = a.gameObject.AddComponent<FeedbackPlayer>();
            host.m_Items.AddRange(new FeedbackItem[] { param, flt, state, any, weight });
            var so = new SerializedObject(host);
            SerializedProperty items = so.FindProperty("m_Items");
            AnimatorNameField Field(int i, string field, AnimatorNameKind kind) => new AnimatorNameField(items.GetArrayElementAtIndex(i), field, field, host, kind);
            Assert.That(Field(0, "m_Parameter", AnimatorNameKind.Parameter).Candidates(), Is.EqualTo(new[] { "On" }), "a bool's");
            Assert.That(Field(1, "m_Parameter", AnimatorNameKind.Parameter).Candidates(), Is.EqualTo(new[] { "Speed" }), "the floats");
            Assert.That(Field(2, "m_State", AnimatorNameKind.State).Candidates(), Is.EqualTo(new[] { "Down", "Up" }), "the named layer's");
            Assert.That(Field(3, "m_State", AnimatorNameKind.State).Candidates(), Is.EqualTo(new[] { "Idle", "Other", "Down", "Up" }), "every layer's");
            Assert.That(Field(4, "m_Layer", AnimatorNameKind.Layer).Candidates(), Is.EqualTo(new[] { "Base Layer", "Upper" }), "the layers");
            AnimatorNameField pick = Field(0, "m_Parameter", AnimatorNameKind.Parameter);
            pick.Choose("On");
            Assert.That(param.m_Parameter, Is.EqualTo("On"), "a pick writes the name");
            a.runtimeAnimatorController = null;
            Assert.That(Field(0, "m_Parameter", AnimatorNameKind.Parameter).Candidates(), Is.Empty, "none without a controller");
            var body = new ItemBody();
            body.Build(items.GetArrayElementAtIndex(2), "AQ18b", host);
            Assert.That(body.Query<AnimatorNameField>().ToList().Count, Is.EqualTo(2), "a Play State's state and layer");
            Assert.That(ItemBody.FieldsOf(items.GetArrayElementAtIndex(0), param), Does.Contain("m_Bool").And.Not.Contain("m_Int"), "a bool's fields");
        }

        // the body in a panel builds again when a Parameter's kind changes — its fields follow the kind (the binding's tracking)
        [UnityTest]
        public IEnumerator AQ18c_AKindChangeBuildsTheBodyAgain()
        {
            var window = ScriptableObject.CreateInstance<PreviewBarTestWindow>();
            window.position = new Rect(0, 0, 500, 800);
            window.Show();
            try
            {
                var go = new GameObject("AQ18c");
                m_Made.Add(go);
                FeedbackPlayer host = go.AddComponent<FeedbackPlayer>();
                host.m_Items.Add(new AnimatorParameterItem { m_Kind = AnimatorParameterKind.Bool, m_Target = new TargetRef { m_Mode = TargetRefMode.Self, m_Path = "", m_Anchor = "" }, m_Motion = Set() });
                var so = new SerializedObject(host);
                SerializedProperty item = so.FindProperty("m_Items").GetArrayElementAtIndex(0);
                var body = new ItemBody();
                window.rootVisualElement.Add(body);
                body.Build(item, "AQ18c", host);
                yield return InspectorTesting.Wait(0.3);
                bool before = body.Fields.Contains("m_Bool");
                item.FindPropertyRelative("m_Kind").intValue = (int)AnimatorParameterKind.Int;
                so.ApplyModifiedProperties();
                yield return InspectorTesting.Wait(0.5);
                Assert.That((before, body.Fields.Contains("m_Bool"), body.Fields.Contains("m_Int")), Is.EqualTo((true, false, true)));
            }
            finally
            {
                window.Close();
            }
        }

        // ---- the play mode's keep — the enums, the names, the numbers
        [Test]
        public void AQ19_TheKeepRoundTrips()
        {
            var param = new AnimatorParameterItem { m_Parameter = "Count", m_Kind = AnimatorParameterKind.Int, m_Bool = false, m_Int = 4, m_Float = 0.5f, m_Pick = AnimatorValuePick.Increment, m_Min = 1f, m_Max = 9f, m_Step = 2f, m_Motion = Set() };
            var state = new AnimatorStateItem { m_State = "Other", m_Layer = "Upper", m_Transition = 0.3f, m_Offset = 0.2f, m_Motion = Set() };
            var go = new GameObject("AQ19From");
            m_Made.Add(go);
            FeedbackPlayer a = go.AddComponent<FeedbackPlayer>();
            a.m_Items.AddRange(new FeedbackItem[] { param, new AnimatorFloatItem { m_Parameter = "Speed", m_Motion = TweenTo(1f) }, new AnimatorSpeedItem { m_Motion = TweenTo(2f) }, new AnimatorLayerWeightItem { m_Layer = "Upper", m_Motion = TweenTo(1f) }, state });
            var into = new GameObject("AQ19Into");
            m_Made.Add(into);
            FeedbackPlayer b = into.AddComponent<FeedbackPlayer>();
            PlayModeRecord.Apply(b, PlayModeRecord.Capture(a, false, out int unknown), out PlayModeRecord.Stats stats);
            Assert.That((unknown, stats.Dropped), Is.EqualTo((0, 0)));
            var p2 = (AnimatorParameterItem)b.m_Items[0];
            var s2 = (AnimatorStateItem)b.m_Items[4];
            Assert.That((p2.m_Parameter, p2.m_Kind, p2.m_Bool, p2.m_Int, p2.m_Float, p2.m_Pick, p2.m_Min, p2.m_Max, p2.m_Step), Is.EqualTo(("Count", AnimatorParameterKind.Int, false, 4, 0.5f, AnimatorValuePick.Increment, 1f, 9f, 2f)));
            Assert.That((((AnimatorFloatItem)b.m_Items[1]).m_Parameter, ((AnimatorLayerWeightItem)b.m_Items[3]).m_Layer, s2.m_State, s2.m_Layer, s2.m_Transition, s2.m_Offset), Is.EqualTo(("Speed", "Upper", "Other", "Upper", 0.3f, 0.2f)));
        }

        // ---- the data is Core's — a player with the five items saves and loads with no missing type, in the minimal project too
        // (no Animation assembly)
        [Test]
        public void AQ20_TheSavedFormOutlivesTheModule()
        {
            var source = new GameObject("AQ20");
            var player = source.AddComponent<FeedbackPlayer>();
            player.m_Items.AddRange(new FeedbackItem[] { new AnimatorParameterItem { m_Parameter = "Go", m_Motion = Set() }, new AnimatorFloatItem { m_Parameter = "Speed", m_Motion = TweenTo(1f) },
                new AnimatorSpeedItem { m_Motion = TweenTo(2f) }, new AnimatorLayerWeightItem { m_Layer = "Upper", m_Motion = TweenTo(1f) }, new AnimatorStateItem { m_State = "Other", m_Offset = 0.5f, m_Motion = Set() } });
            if (!AssetDatabase.IsValidFolder(Folder))
                AssetDatabase.CreateFolder("Assets", "AnimatorTargetsTests");
            string path = Folder + "/AQ20Player.prefab";
            PrefabUtility.SaveAsPrefabAsset(source, path);
            Object.DestroyImmediate(source);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var loaded = AssetDatabase.LoadAssetAtPath<GameObject>(path).GetComponent<FeedbackPlayer>();
            Assert.That(SerializationUtility.HasManagedReferencesWithMissingTypes(loaded), Is.False, "no missing type");
            Assert.That(loaded.m_Items.Select(i => i.GetType().Name), Is.EqualTo(new[] { "AnimatorParameterItem", "AnimatorFloatItem", "AnimatorSpeedItem", "AnimatorLayerWeightItem", "AnimatorStateItem" }));
            Assert.That((((AnimatorParameterItem)loaded.m_Items[0]).m_Parameter, ((AnimatorLayerWeightItem)loaded.m_Items[3]).m_Layer, ((AnimatorStateItem)loaded.m_Items[4]).m_Offset), Is.EqualTo(("Go", "Upper", 0.5f)));
        }
    }
}
