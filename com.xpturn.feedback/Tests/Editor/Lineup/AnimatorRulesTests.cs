using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using xpTURN.Feedback.Editor;
using Object = UnityEngine.Object;
using F = xpTURN.Feedback.Tests.Editor.ValidationFixtures;

namespace xpTURN.Feedback.Tests.Editor
{
    // L01 ~ L08 speak in their cases only — their severities, their fixes. The rules read the controllers through the editor's
    // types (named with the module off); the controllers are FeedbackTestAnimator's layout
    public sealed class AnimatorRulesTests
    {
        F m_Fx;
        readonly List<Object> m_Made = new List<Object>();

        [SetUp]
        public void SetUp() => m_Fx = new F();

        [TearDown]
        public void TearDown()
        {
            m_Fx.Clean();
            foreach (Object o in m_Made)
            {
                if (o != null)
                    Object.DestroyImmediate(o);
            }
            m_Made.Clear();
        }

        Animator Make(string name, RuntimeAnimatorController controller)
        {
            var go = new GameObject(name);
            m_Made.Add(go);
            var a = go.AddComponent<Animator>();
            a.runtimeAnimatorController = controller;
            return a;
        }

        static AnimatorController Controller(string name) => FeedbackTestAnimator.Make(F.Dir + "/" + name + ".controller");

        static TweenMotion TweenTo(float v, TweenMode mode = TweenMode.To)
        {
            var m = new TweenMotion();
            m.m_Params.m_Mode = mode;
            m.m_Params.m_To = new Vector4(v, 0, 0, 0);
            return m;
        }

        static T On<T>(T item, Object target, FeedbackMotion motion = null) where T : TargetItem
        {
            item.m_Target = new TargetRef { m_Mode = TargetRefMode.Direct, m_Object = target, m_Path = "", m_Anchor = "" };
            item.m_Motion = motion ?? new SetMotion();
            return item;
        }

        static void Want(Component host, string id, int[] items, RuleSeverity severity)
        {
            List<RuleResult> results = F.Of(host, id);
            Assert.That(results.Select(r => r.Item).OrderBy(i => i), Is.EqualTo(items), id);
            Assert.That(results.All(r => r.Severity == severity), Is.True, id + " " + severity);
        }

        static string Message(Component host, string id, int item) => F.Of(host, id).Single(r => r.Item == item).Message;

        // L01 · L02 · L03 · L04: no controller · a parameter missing, of another kind or driven by a curve (one of the right kind
        // that differs in case is offered) · a state missing on its layer · a layer missing, the base layer's weight
        [Test]
        public void AQ17a_TheNameRules()
        {
            AnimatorController c = Controller("AQ17a");
            var curve = new AnimationClip { name = "Curve" };
            AnimationUtility.SetEditorCurve(curve, EditorCurveBinding.FloatCurve("", typeof(Animator), "Speed"), AnimationCurve.Constant(0f, 1f, 0.7f));
            AssetDatabase.AddObjectToAsset(curve, c);
            c.layers[1].stateMachine.states.First(s => s.state.name == "Up").state.motion = curve;
            Animator a = Make("AQ17a", c);
            Animator none = Make("AQ17aNone", null);
            FeedbackPlayer p = m_Fx.Player("AQ17a",
                On(new AnimatorSpeedItem(), none, TweenTo(2f)),
                On(new AnimatorParameterItem { m_Parameter = "on", m_Kind = AnimatorParameterKind.Bool }, a),
                On(new AnimatorParameterItem { m_Parameter = "On", m_Kind = AnimatorParameterKind.Int }, a),
                On(new AnimatorFloatItem { m_Parameter = "Speed" }, a, TweenTo(1f)),
                On(new AnimatorParameterItem { m_Parameter = "Go", m_Kind = AnimatorParameterKind.Trigger }, a),
                On(new AnimatorParameterItem { m_Parameter = "Nope", m_Kind = AnimatorParameterKind.Float }, a),
                On(new AnimatorStateItem { m_State = "Up", m_Layer = "Base Layer" }, a),
                On(new AnimatorStateItem { m_State = "Up" }, a),
                On(new AnimatorStateItem { m_State = "Upper.Up", m_Layer = "Upper" }, a),
                On(new AnimatorLayerWeightItem { m_Layer = "Nope" }, a, TweenTo(1f)),
                On(new AnimatorLayerWeightItem { m_Layer = "Base Layer" }, a, TweenTo(1f)),
                On(new AnimatorLayerWeightItem { m_Layer = "Upper" }, a, TweenTo(1f)),
                On(new AnimatorStateItem { m_State = "Idle", m_Layer = "Nope" }, a));
            Want(p, "FB-L01", new[] { 0 }, RuleSeverity.Warning);
            Want(p, "FB-L02", new[] { 1, 2, 3, 5 }, RuleSeverity.Warning);
            Assert.That((Message(p, "FB-L02", 2), Message(p, "FB-L02", 3)), Is.EqualTo(("'On' is a Bool parameter — the item's is Int — the item is skipped", "'Speed' is driven by a clip's curve — a write is ignored")));
            Want(p, "FB-L03", new[] { 6 }, RuleSeverity.Warning);
            Want(p, "FB-L04", new[] { 9, 10, 12 }, RuleSeverity.Warning);
            Assert.That(Message(p, "FB-L04", 10), Does.Contain("base layer"));
            Assert.That(F.Of(p, "FB-L02").Where(r => r.Item == 5).Single().Fixes, Is.Empty, "no name to offer");
            RuleResult l02 = F.Of(p, "FB-L02").Single(r => r.Item == 1);
            FeedbackValidator.Apply(l02, l02.Fixes.Single(f => f.Name == "Use 'On'"));
            Assert.That(((AnimatorParameterItem)p.m_Items[1]).m_Parameter, Is.EqualTo("On"));
            Want(p, "FB-L02", new[] { 2, 3, 5 }, RuleSeverity.Warning);
        }

        // L05 · L06 · L07 · L08: a Fixed animator's offset speed without the direct write (its fix — a Tween, a Spring's move are
        // Persist) · culling completely · a weight out of 0 ~ 1, a speed below 0 (a By tween is relative — not judged) · the
        // controller's behaviours
        [Test]
        public void AQ17b_TheAnimatorRules()
        {
            AnimatorController c = Controller("AQ17b");
            Animator a = Make("AQ17b", c);
            a.updateMode = AnimatorUpdateMode.Fixed;
            a.cullingMode = AnimatorCullingMode.CullCompletely;
            Animator normal = Make("AQ17bNormal", c);
            var move = new SpringMotion();
            move.m_Params.m_Mode = SpringMode.MoveTo;
            AnimatorSpeedItem direct = On(new AnimatorSpeedItem(), a, new PunchMotion());
            direct.m_DirectWrite = true;
            FeedbackPlayer p = m_Fx.Player("AQ17b",
                On(new AnimatorSpeedItem(), a, new PunchMotion()),
                On(new AnimatorSpeedItem(), a, new SpringMotion()),
                On(new AnimatorSpeedItem(), a, move),
                On(new AnimatorSpeedItem(), a, TweenTo(-1f)),
                direct,
                On(new AnimatorSpeedItem(), normal, new PunchMotion()),
                On(new AnimatorLayerWeightItem { m_Layer = "Upper" }, normal, TweenTo(1.5f)),
                On(new AnimatorLayerWeightItem { m_Layer = "Upper" }, normal, TweenTo(-0.5f, TweenMode.By)),
                On(new AnimatorSpeedItem(), normal, TweenTo(-5f, TweenMode.By)),
                On(new AnimatorLayerWeightItem { m_Layer = "Upper" }, normal, TweenTo(0.5f)));
            Want(p, "FB-L05", new[] { 0, 1 }, RuleSeverity.Warning);
            Want(p, "FB-L06", new[] { 0, 1, 2, 3, 4 }, RuleSeverity.Info);
            Want(p, "FB-L07", new[] { 3, 6 }, RuleSeverity.Info);
            Want(p, "FB-L08", new int[0], RuleSeverity.Info);
            RuleResult l05 = F.Of(p, "FB-L05").Single(r => r.Item == 0);
            FeedbackValidator.Apply(l05, l05.Fixes.Single(f => f.Name == "Write directly"));
            Assert.That(((AnimatorSpeedItem)p.m_Items[0]).m_DirectWrite, Is.True);
            Want(p, "FB-L05", new[] { 1 }, RuleSeverity.Warning);
        }

        // L08: the controller's behaviours (on a state, on a sub-machine) — the dev project's (the behaviour is the Animation tests')
        [Test, Category(OptionalAssemblyTests.DevProjectCategory)]
        public void AQ17c_TheBehavioursRule()
        {
            AnimatorController c = Controller("AQ17c");
            AnimatorController d = Controller("AQ17cMachine");
            Animator a = Make("AQ17c", c);
            Animator b = Make("AQ17cMachine", d);
            FeedbackPlayer p = m_Fx.Player("AQ17c", On(new AnimatorSpeedItem(), a, TweenTo(2f)), On(new AnimatorSpeedItem(), b, TweenTo(2f)));
            Want(p, "FB-L08", new int[0], RuleSeverity.Info);
            Type behaviour = Type.GetType(AnimatorTargetsTests.BehaviourName, true);
            c.layers[0].stateMachine.states.First(s => s.state.name == "Idle").state.AddStateMachineBehaviour(behaviour);
            d.layers[1].stateMachine.AddStateMachine("Inner").AddStateMachineBehaviour(behaviour);
            Want(p, "FB-L08", new[] { 0, 1 }, RuleSeverity.Info);
        }
    }
}
