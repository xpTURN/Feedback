using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using xpTURN.Feedback.Editor;
using F = xpTURN.Feedback.Tests.Editor.ValidationFixtures;
using Object = UnityEngine.Object;

namespace xpTURN.Feedback.Tests.Editor
{
    // the MotionDriver host · E · F · G (transforms) · G (bodies) and the pivot · the pivot and prefabs
    internal sealed class ValidationSceneTests
    {
        // a spring item whose binding cannot follow — the test assembly's own
        internal sealed class SpringNoFollowItem : TargetItem
        {
            static readonly TargetTraits s_Traits = new TargetTraits(ValueKind.Float, MotionMask.Spring, FeedbackCategory.None, false, ApplyMode.Offset);

            internal override TargetTraits Traits => s_Traits;
        }

        sealed class NoFollowBinding : ItemBinding
        {
            internal override bool Prepare(in PrepareContext context) => true;

            internal override void Begin(int record, in BeginContext context)
            {
            }

            internal override void Evaluate(int record, in MotionTime time)
            {
            }

            internal override void End(int record, EndReason reason)
            {
            }
        }

        F m_Fx;

        [SetUp]
        public void SetUp() => m_Fx = new F();

        [TearDown]
        public void TearDown() => m_Fx.Clean();

        GameObject Go(string name, Transform parent = null)
        {
            var g = new GameObject(name);
            if (parent != null)
                g.transform.SetParent(parent, false);
            else
                m_Fx.Made.Add(g);
            return g;
        }

        GameObject Cube(string name)
        {
            GameObject c = GameObject.CreatePrimitive(PrimitiveType.Cube);
            c.name = name;
            m_Fx.Made.Add(c);
            return c;
        }

        static T With<T>(T item, TargetRefMode mode, Object o = null) where T : TargetItem
        {
            item.m_Target.m_Mode = mode;
            item.m_Target.m_Object = o;
            return item;
        }

        static BlinkMotion Blink(float period) => new BlinkMotion { m_Params = new BlinkParams { m_Value = Vector4.zero, m_Period = period, m_Duty = 0.5f, m_StartOn = true } };

        static List<RuleResult> Of(Component host, string id) => F.Of(host, id);

        static bool Hit(Component host, string id, int item = int.MinValue) => Of(host, id).Any(r => item == int.MinValue || r.Item == item);

        static RuleFix FixNamed(RuleResult r, string name) => r.Fixes.Single(f => f.Name == name);

        static float MaxDiff(Matrix4x4 a, Matrix4x4 b)
        {
            float d = 0f;
            for (int i = 0; i < 16; i++)
                d = Mathf.Max(d, Mathf.Abs(a[i] - b[i]));
            return d;
        }

        GameObject Prefab(string name, Action<Transform> build)
        {
            var root = new GameObject(name);
            build(root.transform);
            GameObject asset = PrefabUtility.SaveAsPrefabAsset(root, F.Dir + "/" + name + ".prefab");
            Object.DestroyImmediate(root);
            return asset;
        }

        // a root with T (a rigidbody) and T's child c1
        static void Body(Transform root)
        {
            var t = new GameObject("T");
            t.transform.SetParent(root, false);
            t.AddComponent<Rigidbody>();
            new GameObject("c1").transform.SetParent(t.transform, false);
        }

        // the prefab's own player on T (the player inside the prefab)
        static void BodyWithPlayer(Transform root)
        {
            Body(root);
            root.gameObject.AddComponent<FeedbackPlayer>().m_Items.Add(With(F.Item<PositionItem>(F.Tween), TargetRefMode.Direct, root.Find("T")));
        }

        GameObject Instance(GameObject asset)
        {
            var g = (GameObject)PrefabUtility.InstantiatePrefab(asset);
            m_Fx.Made.Add(g);
            return g;
        }

        // ---- the MotionDriver host ----

        // the driver's one item: the shared rules on it, a candidate written into m_Item, the player's rules not on it
        [Test]
        public void SV01a_TheDriversItem()
        {
            GameObject dgo = Go("SV01a");
            var d = dgo.AddComponent<MotionDriver>();
            d.m_Item = F.Item<LightIntensityItem>(new OscillateMotion());
            Go("lamp", dgo.transform).AddComponent<Light>();
            RuleResult a01 = Of(d, "FB-A01").Single();
            Assert.That(a01.Item, Is.EqualTo(0));
            Assert.That(a01.Host, Is.EqualTo(d));
            FeedbackValidator.Apply(a01, a01.Fixes.Single(f => f.Name == "Use SV01a > lamp (Light)"));
            Assert.That(d.m_Item.m_Target.m_Mode, Is.EqualTo(TargetRefMode.Direct));
            Assert.That(d.m_Item.m_Target.m_Object, Is.EqualTo(dgo.transform.Find("lamp").GetComponent<Light>()));
            Assert.That(Hit(d, "FB-A01"), Is.False);
            d.m_Item = With(new SetActiveItem { m_Motion = Blink(0.2f) }, TargetRefMode.Self);
            string[] playerOnly = { "FB-A04", "FB-A05", "FB-C01", "FB-D01", "FB-E01", "FB-E05" };
            Assert.That(FeedbackValidator.Run(d).Select(r => r.Id).Intersect(playerOnly), Is.Empty, "the player's rules");
            Assert.That(ValidatorScan.OpenSceneHosts(), Does.Contain(d), "the menu's scan");
        }

        // A03's fix on a driver clears the field's reference (the slot path)
        [Test]
        public void SV01b_AnotherScenesReferenceInTheDriver()
        {
            GameObject there = Go("SV01bThere");
            SceneManager.MoveGameObjectToScene(there, m_Fx.Scene("SV01b"));
            var d = Go("SV01b").AddComponent<MotionDriver>();
            d.m_Item = With(F.Item<PositionItem>(new OscillateMotion()), TargetRefMode.Direct, there.transform);
            RuleResult a03 = Of(d, "FB-A03").Single();
            FeedbackValidator.Apply(a03, a03.Fixes.Single());
            Assert.That(d.m_Item.m_Target.m_Object, Is.Null);
            Assert.That(Hit(d, "FB-A03"), Is.False);
        }

        // the driver's inspector: the results panel, its fix, gone after (a frame)
        [UnityTest]
        public IEnumerator SV01c_TheDriversInspector()
        {
            var window = ScriptableObject.CreateInstance<PreviewBarTestWindow>();
            window.Show();
            try
            {
                var d = Go("SV01c").AddComponent<MotionDriver>();
                d.m_Item = F.Item<PositionItem>(new SpringMotion());
                var inspector = InspectorTesting.Inspector(window, d);
                yield return InspectorTesting.Wait(0.2);
                var view = inspector.Q<ValidationView>();
                Assert.That(view.HostPanel.style.display.value, Is.EqualTo(DisplayStyle.Flex));
                Button fix = view.HostPanel.Query<Button>(className: InspectorStyles.ValidationFix).ToList().Single();
                Assert.That(fix.text, Is.EqualTo("Use Follow"));
                PreviewBarTesting.Submit(fix);
                Assert.That(d.m_Mode, Is.EqualTo(MotionDriverMode.Follow));
                Assert.That(view.HostPanel.style.display.value, Is.EqualTo(DisplayStyle.None), "nothing left");
            }
            finally
            {
                window.Close();
            }
        }

        // ---- E ----

        // E01: its own object · a parent · a child × the disable behaviour · OnEnable · Set on · off · relative
        [Test]
        public void SV02a_TurningOffItsOwnPlayer()
        {
            FeedbackPlayer p = m_Fx.Player("SV02a", With(new SetActiveItem { m_Motion = Blink(0.4f) }, TargetRefMode.Self));
            GameObject kid = Go("kid", p.transform);
            p.m_DisableBehavior = PlayerDisableBehavior.StopHold;
            RuleResult r = Of(p, "FB-E01").Single();
            Assert.That(r.Fixes.Select(f => f.Name), Is.EqualTo(new[] { "Keep playing on disable", "Use SV02a > kid" }));
            p.m_DisableBehavior = PlayerDisableBehavior.Continue;
            Assert.That(Hit(p, "FB-E01"), Is.False, "Continue");
            p.m_AutoPlay = PlayerAutoPlay.OnEnable;
            Assert.That(Of(p, "FB-E01").Single().Message, Does.Contain("OnEnable"), "a blink starts again");
            p.m_AutoPlay = PlayerAutoPlay.None;
            p.m_DisableBehavior = PlayerDisableBehavior.StopHold;
            ((TargetItem)p.m_Items[0]).m_Target.m_Mode = TargetRefMode.Direct;
            ((TargetItem)p.m_Items[0]).m_Target.m_Object = kid;
            Assert.That(Hit(p, "FB-E01"), Is.False, "a child");
            p.m_Items[0] = With(new SetActiveItem { m_Motion = new SetMotion { m_Params = new SetParams { m_Value = Vector4.one } } }, TargetRefMode.Self);
            Assert.That(Hit(p, "FB-E01"), Is.False, "Set on");
            p.m_Items[0] = With(new SetActiveItem { m_Motion = new SetMotion { m_Params = new SetParams { m_Value = Vector4.one, m_Relative = true } } }, TargetRefMode.Self);
            Assert.That(Hit(p, "FB-E01"), Is.True, "a relative Set toggles — it turns the active object off");
            p.m_Items[0] = With(new SetActiveItem { m_Motion = new SetMotion() }, TargetRefMode.Self);
            r = Of(p, "FB-E01").Single();
            FeedbackValidator.Apply(r, FixNamed(r, "Keep playing on disable"));
            Assert.That(p.m_DisableBehavior, Is.EqualTo(PlayerDisableBehavior.Continue));
            Assert.That(Hit(p, "FB-E01"), Is.False);
            GameObject a = Go("SV02aParent");
            FeedbackPlayer q = m_Fx.Player("SV02aChild", With(new SetActiveItem { m_Motion = Blink(0.4f) }, TargetRefMode.Direct, a));
            q.transform.SetParent(a.transform, false);
            q.m_DisableBehavior = PlayerDisableBehavior.StopHold;
            Assert.That(Hit(q, "FB-E01"), Is.True, "a parent");
        }

        [Test]
        public void SV02b_AnAnimatorUnderABlink()
        {
            GameObject t = Go("SV02bT");
            Animator animator = Go("k", t.transform).AddComponent<Animator>();
            FeedbackPlayer p = m_Fx.Player("SV02b", With(new SetActiveItem { m_Motion = Blink(0.4f) }, TargetRefMode.Direct, t));
            RuleResult r = Of(p, "FB-E02").Single();
            FeedbackValidator.Apply(r, r.Fixes.Single());
            Assert.That(animator.keepAnimatorStateOnDisable, Is.True);
            Assert.That(Hit(p, "FB-E02"), Is.False);
        }

        // E03: a parent's controller curve at the target's path · another path · a legacy Animation on the target
        [Test]
        public void SV02c_AnAnimatedActiveState()
        {
            GameObject root = Go("SV02cRoot");
            GameObject target = Go("Tgt", root.transform);
            GameObject other = Go("Other", root.transform);
            var clip = new AnimationClip();
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("Tgt", typeof(GameObject), "m_IsActive"), AnimationCurve.Constant(0, 1, 1));
            AssetDatabase.CreateAsset(clip, F.Dir + "/SV02c.anim");
            root.AddComponent<Animator>().runtimeAnimatorController = AnimatorController.CreateAnimatorControllerAtPathWithClip(F.Dir + "/SV02c.controller", clip);
            Assert.That(Hit(m_Fx.Player("SV02c", With(new SetActiveItem { m_Motion = Blink(0.4f) }, TargetRefMode.Direct, target)), "FB-E03"), Is.True);
            Assert.That(Hit(m_Fx.Player("SV02cQuiet", With(new SetActiveItem { m_Motion = Blink(0.4f) }, TargetRefMode.Direct, other)), "FB-E03"), Is.False, "another path");
            GameObject own = Go("SV02cOwn");
            var self = new AnimationClip { legacy = true };
            AnimationUtility.SetEditorCurve(self, EditorCurveBinding.FloatCurve("", typeof(GameObject), "m_IsActive"), AnimationCurve.Constant(0, 1, 1));
            AssetDatabase.CreateAsset(self, F.Dir + "/SV02cSelf.anim");
            own.AddComponent<Animation>().AddClip(self, "self");
            Assert.That(Hit(m_Fx.Player("SV02cLegacy", With(new SetActiveItem { m_Motion = Blink(0.4f) }, TargetRefMode.Direct, own)), "FB-E03"), Is.True, "a legacy Animation");
        }

        [Test]
        public void SV02d_ALargeBlink()
        {
            GameObject big = Go("SV02dBig");
            for (int i = 0; i < LargeBlinkRule.Threshold; i++)
                new GameObject("c").transform.SetParent(big.transform, false);
            FeedbackPlayer p = m_Fx.Player("SV02d", With(new SetActiveItem { m_Motion = Blink(0.4f) }, TargetRefMode.Direct, big));
            Assert.That(Hit(p, "FB-E04"), Is.True, "1,000");
            Object.DestroyImmediate(big.transform.GetChild(0).gameObject);
            Assert.That(Hit(p, "FB-E04"), Is.False, "999");
        }

        // E05: 5 a second over 3 · 2.5 under · the settings' limit 2 · a motion that does not flash · the add menu's blink
        [Test]
        public void SV02e_OverTheFlashLimit()
        {
            Renderer r = Cube("SV02eCube").GetComponent<Renderer>();
            FeedbackPlayer fast = m_Fx.Player("SV02e", With(new MaterialColorItem { m_Motion = Blink(0.2f) }, TargetRefMode.Direct, r));
            FeedbackPlayer slow = m_Fx.Player("SV02eSlow", With(new MaterialColorItem { m_Motion = Blink(0.4f) }, TargetRefMode.Direct, r));
            FeedbackPlayer move = m_Fx.Player("SV02eMove", With(F.Item<PositionItem>(new OscillateMotion()), TargetRefMode.Direct, r.transform));
            Assert.That(Of(fast, "FB-E05").Single().Message, Is.EqualTo("Flashes 5 times a second — over the flash limit 3"));
            Assert.That(Hit(slow, "FB-E05"), Is.False);
            Assert.That(Hit(move, "FB-E05"), Is.False);
            AssetDatabase.CreateFolder(F.Dir, "Resources");
            var settings = ScriptableObject.CreateInstance<FeedbackSettings>();
            settings.m_FlashLimit = 2;
            AssetDatabase.CreateAsset(settings, F.Dir + "/Resources/FeedbackSettings.asset");
            AssetDatabase.SaveAssets();
            Assert.That(Hit(slow, "FB-E05"), Is.True, "the runtime's limit");
            foreach (Type t in new[] { typeof(SetActiveItem), typeof(MaterialColorItem), typeof(LightIntensityItem) })
                Assert.That(((BlinkMotion)MotionDefaults.Create(t, MotionKind.Blink, out _)).m_Params.m_Period, Is.EqualTo(MotionDefaults.BlinkPeriod), t.Name);
        }

        // E06 · E07 · E08: a driver that does nothing · its fixes · its own object · a fast blink
        [Test]
        public void SV03a_ADriverThatDoesNothing()
        {
            var d = Go("SV03a").AddComponent<MotionDriver>();
            Assert.That(Hit(d, "FB-E06", 0), Is.True, "no item");
            Assert.That(Hit(d, "FB-B02"), Is.False, "not an empty list slot");
            d.m_Item = F.Item<PositionItem>(F.Tween);
            Assert.That(Of(d, "FB-E06").Single().Fixes, Is.Empty, "a Tween fits neither");
            d.m_Item = F.Item<PositionItem>(new OscillateMotion());
            d.m_Mode = MotionDriverMode.Follow;
            RuleResult r = Of(d, "FB-E06").Single();
            FeedbackValidator.Apply(r, FixNamed(r, "Use Loop"));
            Assert.That(d.m_Mode, Is.EqualTo(MotionDriverMode.Loop));
            Assert.That(Hit(d, "FB-E06"), Is.False);
            d.m_Item = With(new SetActiveItem { m_Motion = Blink(0.2f) }, TargetRefMode.Self);
            Assert.That(Hit(d, "FB-E07"), Is.True, "its own object");
            Assert.That(Of(d, "FB-E08").Single().Severity, Is.EqualTo(RuleSeverity.Info));
        }

        // E06: a spring item whose binding cannot follow (the test assembly's)
        [Test]
        public void SV03b_AFollowThatCannotFollow()
        {
            FeedbackImplementations.Register<SpringNoFollowItem>(i => new NoFollowBinding());
            var d = Go("SV03b").AddComponent<MotionDriver>();
            d.m_Item = new SpringNoFollowItem { m_Motion = new SpringMotion() };
            d.m_Mode = MotionDriverMode.Follow;
            Assert.That(Of(d, "FB-E06").Single().Message, Does.StartWith("Follow cannot follow"));
            d.m_Item = F.Item<PositionItem>(new SpringMotion());
            Assert.That(Hit(d, "FB-E06"), Is.False, "a position follows");
        }

        // ---- F ----

        [Test]
        public void SV04a_PersistBlocksAndSprites()
        {
            Renderer r = Cube("SV04a").GetComponent<Renderer>();
            FeedbackPlayer p = m_Fx.Player("SV04a", With(new MaterialColorItem { m_Motion = F.Tween }, TargetRefMode.Direct, r));
            RuleResult f01 = Of(p, "FB-F01").Single();
            FeedbackValidator.Apply(f01, f01.Fixes.Single());
            Assert.That(((MaterialColorItem)p.m_Items[0]).m_WriteMode, Is.EqualTo(MaterialWriteMode.Instance));
            Assert.That(Hit(p, "FB-F01"), Is.False);
            var many = new List<FeedbackItem>();
            for (int i = 0; i < ManyBlockBlinksRule.Count; i++)
            {
                var m = With(new MaterialColorItem { m_Motion = Blink(0.4f) }, TargetRefMode.Direct, Cube("SV04aMany" + i).GetComponent<Renderer>());
                m.m_Timing.m_Duration = 1f;
                many.Add(m);
            }
            FeedbackPlayer pm = m_Fx.Player("SV04aMany", many.ToArray());
            Assert.That(Of(pm, "FB-F02"), Has.Count.EqualTo(8));
            ((TargetItem)pm.m_Items[7]).m_Timing.m_Duration = 0.9f;
            Assert.That(Hit(pm, "FB-F02"), Is.False, "seven long");
            var sprite = Go("SV04aSprite").AddComponent<SpriteRenderer>();
            Assert.That(Hit(m_Fx.Player("SV04aSpriteP", With(new MaterialColorItem { m_Motion = Blink(0.4f) }, TargetRefMode.Direct, sprite)), "FB-F03"), Is.True);
        }

        // F04 · F05: the whole renderer with a differing material · a host's index block · two modes
        [Test]
        public void SV04b_TheWholeRendererAndTwoModes()
        {
            Renderer r = Cube("SV04b").GetComponent<Renderer>();
            Shader color = Shader.Find("Unlit/Color");
            var m0 = new Material(color);
            var m1 = new Material(color);
            m1.SetColor("_Color", Color.red);
            r.sharedMaterials = new[] { m0, m1 };
            var whole = With(new MaterialColorItem { m_Motion = Blink(0.4f) }, TargetRefMode.Direct, r);
            FeedbackPlayer p = m_Fx.Player("SV04b", whole);
            RuleResult f04 = Of(p, "FB-F04").Single();
            Assert.That(f04.Message, Does.StartWith("The materials differ"));
            Assert.That(f04.Fixes.Select(f => f.Name), Is.EqualTo(new[] { "Use index 0", "Use index 1" }));
            m1.SetColor("_Color", m0.GetColor("_Color"));
            Assert.That(Hit(p, "FB-F04"), Is.False, "the same");
            p.m_Items.Add(With(new MaterialColorItem { m_Motion = Blink(0.4f), m_MaterialIndex = 1 }, TargetRefMode.Direct, r));
            f04 = Of(p, "FB-F04").Single();
            Assert.That(f04.Message, Does.StartWith("An index block of item 1"));
            FeedbackValidator.Apply(f04, FixNamed(f04, "Use index 0"));
            Assert.That(whole.m_MaterialIndex, Is.EqualTo(0));
            Assert.That(Hit(p, "FB-F04"), Is.False);
            p.m_Items.Add(With(new MaterialColorItem { m_Motion = Blink(0.4f), m_MaterialIndex = -1, m_WriteMode = MaterialWriteMode.Instance }, TargetRefMode.Direct, r));
            Assert.That(Of(p, "FB-F05").Select(x => x.Item), Is.EqualTo(new[] { 2 }), "−1 overlaps every index");
        }

        // F06: no main color · a name the shader lacks · a declared Vector property is quiet
        [Test]
        public void SV04c_TheColorProperty()
        {
            Renderer r = Cube("SV04c").GetComponent<Renderer>();
            r.sharedMaterials = new[] { new Material(Shader.Find("Unlit/Texture")) };
            FeedbackPlayer p = m_Fx.Player("SV04c", With(new MaterialColorItem { m_Motion = Blink(0.4f) }, TargetRefMode.Direct, r));
            Assert.That(Of(p, "FB-F06").Single().Fixes, Is.Empty, "no color property to pick");
            r.sharedMaterials = new[] { new Material(Shader.Find("Skybox/Procedural")) };
            Assert.That(Of(p, "FB-F06").Single().Fixes.Select(f => f.Name), Is.EqualTo(new[] { "Use _SkyTint", "Use _GroundColor" }));
            r.sharedMaterials = new[] { new Material(Shader.Find("Unlit/Color")) };
            var named = With(new MaterialColorItem { m_Motion = Blink(0.4f), m_PropertyName = "_Nope" }, TargetRefMode.Direct, r);
            FeedbackPlayer pn = m_Fx.Player("SV04cNamed", named);
            RuleResult f06 = Of(pn, "FB-F06").Single();
            FeedbackValidator.Apply(f06, FixNamed(f06, "Use _Color"));
            Assert.That(named.m_PropertyName, Is.EqualTo("_Color"));
            Assert.That(Hit(pn, "FB-F06"), Is.False);
            Shader sprites = Shader.Find("Sprites/Default");
            Assert.That(sprites.GetPropertyType(sprites.FindPropertyIndex("_Flip")), Is.EqualTo(UnityEngine.Rendering.ShaderPropertyType.Vector), "the fixture: a declared Vector property");
            r.sharedMaterials = new[] { new Material(sprites) };
            named.m_PropertyName = "_Flip";
            Assert.That(Hit(pn, "FB-F06"), Is.False, "a Vector property shows a color too");
        }

        // F07: out of range (the fix) · no material · an empty slot for the instances of −1
        [Test]
        public void SV04d_MaterialsThePrepareRefuses()
        {
            Renderer r = Cube("SV04d").GetComponent<Renderer>();
            var range = With(new MaterialColorItem { m_Motion = Blink(0.4f), m_MaterialIndex = 5 }, TargetRefMode.Direct, r);
            FeedbackPlayer p = m_Fx.Player("SV04d", range);
            RuleResult f07 = Of(p, "FB-F07").Single();
            FeedbackValidator.Apply(f07, f07.Fixes.Single());
            Assert.That(range.m_MaterialIndex, Is.EqualTo(-1));
            Assert.That(Hit(p, "FB-F07"), Is.False);
            r.sharedMaterials = new Material[0];
            Assert.That(FeedbackValidator.Run(p).Where(x => x.Id.StartsWith("FB-F", StringComparison.Ordinal)).Select(x => x.Id), Is.EqualTo(new[] { "FB-F07" }), "the other F rules are quiet");
            r.sharedMaterials = new[] { new Material(Shader.Find("Unlit/Color")), null };
            range.m_WriteMode = MaterialWriteMode.Instance;
            Assert.That(Hit(p, "FB-F07"), Is.True, "an empty slot");
        }

        // ---- G (transforms) ----

        // G06: World — a mirror or a non-uniform parent · Local — a mirror only
        [Test]
        public void SV05a_ASkewedParent()
        {
            GameObject parent = Go("SV05aParent");
            GameObject t = Go("t", parent.transform);
            FeedbackPlayer p = m_Fx.Player("SV05a", With(F.Item<RotationItem>(F.Tween), TargetRefMode.Direct, t.transform),
                With(new RotationItem { m_Motion = F.Tween, m_Space = TransformSpace.Local }, TargetRefMode.Direct, t.transform));
            parent.transform.localScale = new Vector3(-1f, 1f, 1f);
            Assert.That(Of(p, "FB-G06").Select(r => r.Item), Is.EqualTo(new[] { 0, 1 }), "a mirror");
            parent.transform.localScale = new Vector3(2f, 1f, 1f);
            Assert.That(Of(p, "FB-G06").Select(r => r.Item), Is.EqualTo(new[] { 0 }), "non-uniform — World only");
            parent.transform.localScale = Vector3.one;
            Assert.That(Hit(p, "FB-G06"), Is.False);
        }

        // G07 · G08: a deep root (10,000 · 9,999) · a driven RectTransform's position and scale, not its rotation
        [Test]
        public void SV05b_DeepRootsAndLayouts()
        {
            GameObject big = Go("SV05bBig");
            for (int i = 0; i < DeepRootRule.Threshold; i++)
                new GameObject("c").transform.SetParent(big.transform, false);
            FeedbackPlayer p = m_Fx.Player("SV05b", With(F.Item<PositionItem>(F.Tween), TargetRefMode.Direct, big.transform));
            Assert.That(Hit(p, "FB-G07"), Is.True);
            Object.DestroyImmediate(big.transform.GetChild(0).gameObject);
            Assert.That(Hit(p, "FB-G07"), Is.False);
            var rt = new GameObject("SV05bRect", typeof(RectTransform));
            m_Fx.Made.Add(rt);
            var tracker = new DrivenRectTransformTracker();
            tracker.Add(rt, (RectTransform)rt.transform, DrivenTransformProperties.AnchoredPosition);
            try
            {
                FeedbackPlayer pr = m_Fx.Player("SV05bRectP", With(F.Item<PositionItem>(F.Tween), TargetRefMode.Direct, rt.transform),
                    With(F.Item<ScaleItem>(F.Tween), TargetRefMode.Direct, rt.transform), With(F.Item<RotationItem>(F.Tween), TargetRefMode.Direct, rt.transform));
                Assert.That(Of(pr, "FB-G08").Select(r => r.Item), Is.EqualTo(new[] { 0, 1 }));
            }
            finally
            {
                tracker.Clear();
            }
        }

        // G09 · G10: a constraint's source (its follower is quiet) · a shaken empty under a drawn parent · a camera shake's camera
        [Test]
        public void SV05c_SourcesAndEmpties()
        {
            GameObject src = Go("SV05cSource");
            GameObject follower = Go("SV05cFollower");
            follower.AddComponent<ParentConstraint>().AddSource(new ConstraintSource { sourceTransform = src.transform, weight = 1f });
            FeedbackPlayer pc = m_Fx.Player("SV05c", With(F.Item<PositionItem>(F.Tween), TargetRefMode.Direct, src.transform), With(F.Item<PositionItem>(F.Tween), TargetRefMode.Direct, follower.transform));
            Assert.That(Of(pc, "FB-G09").Select(r => r.Item), Is.EqualTo(new[] { 0 }));
            GameObject empty = Go("empty", Cube("SV05cDrawn").transform);
            Assert.That(Hit(m_Fx.Player("SV05cEmpty", With(F.Item<PositionItem>(F.Tween), TargetRefMode.Direct, empty.transform)), "FB-G10"), Is.True);
            GameObject cam = Go("SV05cCamera");
            cam.AddComponent<Camera>();
            cam.AddComponent<Rigidbody>();
            RuleResult shake = Of(m_Fx.Player("SV05cShake", With(F.Item<CameraShakeItem>(new ShakeMotion()), TargetRefMode.Direct, cam.GetComponent<Camera>())), "FB-G02").Single();
            Assert.That(shake.Fixes, Is.Empty, "a camera is not pivoted");
        }

        // ---- G (bodies) and the pivot ----

        // G01: an error; the pivot for every item on T — the children under it (their world pose), the items on it, one undo
        [Test]
        public void SV06a_AnInterpolatingBodyAndThePivot()
        {
            GameObject t = Cube("SV06aT");
            t.AddComponent<Rigidbody>().interpolation = RigidbodyInterpolation.Interpolate;
            GameObject visual = Go("visual", t.transform);
            visual.transform.localPosition = new Vector3(0f, 1f, 0f);
            visual.transform.localRotation = Quaternion.Euler(0f, 0f, 30f);
            Matrix4x4 before = visual.transform.localToWorldMatrix;
            var a = With(F.Item<PositionItem>(F.Tween), TargetRefMode.Direct, t.transform);
            var b = With(F.Item<RotationItem>(F.Tween), TargetRefMode.Direct, t.transform);
            FeedbackPlayer p = m_Fx.Player("SV06a", a, b);
            RuleResult g01 = Of(p, "FB-G01").First();
            Assert.That(g01.Severity, Is.EqualTo(RuleSeverity.Error));
            Assert.That(g01.Fixes.Select(f => f.Name), Is.EqualTo(new[] { "Make a visual pivot", "Make a visual pivot for every item on SV06aT" }));
            Undo.ClearAll();
            FeedbackValidator.Apply(g01, g01.Fixes[1]);
            Transform pivot = t.transform.Find(VisualPivot.Name);
            Assert.That(visual.transform.parent, Is.EqualTo(pivot));
            Assert.That(MaxDiff(before, visual.transform.localToWorldMatrix), Is.LessThan(1e-5f));
            Assert.That(a.m_Target.m_Object, Is.EqualTo(pivot));
            Assert.That(b.m_Target.m_Object, Is.EqualTo(pivot));
            Assert.That(Hit(p, "FB-G01"), Is.False);
            Undo.PerformUndo();
            Assert.That(t.transform.Find(VisualPivot.Name), Is.Null);
            Assert.That(visual.transform.parent, Is.EqualTo(t.transform));
            Assert.That(a.m_Target.m_Object, Is.EqualTo(t.transform), "one undo");
        }

        // G02 info · G03 · G04 for a collider in the shaken subtree · G05 → clear the mark → G02
        [Test]
        public void SV06b_BodiesAndColliders()
        {
            GameObject t = Cube("SV06bT");
            var body = t.AddComponent<Rigidbody>();
            FeedbackPlayer p = m_Fx.Player("SV06b", With(F.Item<PositionItem>(F.Tween), TargetRefMode.Direct, t.transform));
            Assert.That(Of(p, "FB-G02").Single().Severity, Is.EqualTo(RuleSeverity.Info));
            Object.DestroyImmediate(body);
            Assert.That(Hit(p, "FB-G03"), Is.True, "a static collider");
            GameObject outer = Go("SV06bOuter");
            outer.AddComponent<Rigidbody>();
            GameObject part = Go("part", outer.transform);
            Go("partCollider", part.transform).AddComponent<BoxCollider>();
            Assert.That(Hit(m_Fx.Player("SV06bPart", With(F.Item<PositionItem>(F.Tween), TargetRefMode.Direct, part.transform)), "FB-G04"), Is.True, "a collider under it");
            GameObject db = Go("SV06bDirect");
            db.AddComponent<Rigidbody>();
            var direct = With(F.Item<PositionItem>(F.Tween), TargetRefMode.Direct, db.transform);
            direct.m_DirectWrite = true;
            FeedbackPlayer pd = m_Fx.Player("SV06bDirectP", direct);
            RuleResult g05 = Of(pd, "FB-G05").Single();
            FeedbackValidator.Apply(g05, FixNamed(g05, "Clear the direct-write mark"));
            Assert.That(direct.m_DirectWrite, Is.False);
            Assert.That(Hit(pd, "FB-G02"), Is.True);
        }

        // the pivot keeps a collider of the body under T; T's own renderer stays and G10 says so
        [Test]
        public void SV06c_TheBodysCollidersStay()
        {
            GameObject t = Cube("SV06cT");
            t.AddComponent<Rigidbody>();
            GameObject hit = Go("hitbox", t.transform);
            hit.AddComponent<BoxCollider>();
            Go("visual", t.transform);
            FeedbackPlayer p = m_Fx.Player("SV06c", With(F.Item<PositionItem>(F.Tween), TargetRefMode.Direct, t.transform));
            RuleResult g02 = Of(p, "FB-G02").Single();
            FeedbackValidator.Apply(g02, FixNamed(g02, "Make a visual pivot"));
            Assert.That(hit.transform.parent, Is.EqualTo(t.transform));
            Assert.That(t.transform.Find(VisualPivot.Name + "/visual"), Is.Not.Null);
            Assert.That(Hit(p, "FB-G10"), Is.True, "T's own renderer stays — G10 says so");
        }

        // a reused pivot the user moved: the children keep their world pose and one undo puts them back
        [Test]
        public void SV06d_AReusedPivot()
        {
            GameObject t = Go("SV06dT");
            t.AddComponent<Rigidbody>();
            GameObject old = Go(VisualPivot.Name, t.transform);
            old.transform.localPosition = new Vector3(1f, 0f, 0f);
            GameObject k = Go("k", t.transform);
            k.transform.localPosition = new Vector3(0f, 2f, 0f);
            Matrix4x4 before = k.transform.localToWorldMatrix;
            FeedbackPlayer p = m_Fx.Player("SV06d", With(F.Item<PositionItem>(F.Tween), TargetRefMode.Direct, t.transform));
            RuleResult g02 = Of(p, "FB-G02").Single();
            Undo.ClearAll();
            FeedbackValidator.Apply(g02, FixNamed(g02, "Make a visual pivot"));
            Assert.That(t.GetComponentsInChildren<Transform>(true).Count(x => x.name == VisualPivot.Name), Is.EqualTo(1), "reused");
            Assert.That(k.transform.parent, Is.EqualTo(old.transform));
            Assert.That(MaxDiff(before, k.transform.localToWorldMatrix), Is.LessThan(1e-5f));
            Undo.PerformUndo();
            Assert.That(k.transform.parent, Is.EqualTo(t.transform));
        }

        // a Child-path item through T gets its new path; an animation under T withholds the pivot
        [Test]
        public void SV06e_PathsUnderT()
        {
            FeedbackPlayer p = m_Fx.Player("SV06e");
            GameObject t = Go("T", p.transform);
            t.AddComponent<Rigidbody>();
            Go("c1", t.transform);
            var child = F.Item<ScaleItem>(F.Tween, TargetRefMode.Child);
            child.m_Target.m_ChildLookup = ChildLookup.Path;
            child.m_Target.m_Path = "T/c1";
            p.m_Items.Add(With(F.Item<PositionItem>(F.Tween), TargetRefMode.Direct, t.transform));
            p.m_Items.Add(child);
            RuleResult g02 = Of(p, "FB-G02").Single();
            FeedbackValidator.Apply(g02, FixNamed(g02, "Make a visual pivot"));
            Assert.That(child.m_Target.m_Path, Is.EqualTo("T/" + VisualPivot.Name + "/c1"));
            Assert.That(Hit(p, "FB-A01"), Is.False);
            GameObject r = Go("SV06eRoot");
            GameObject u = Go("U", r.transform);
            u.AddComponent<Rigidbody>();
            Go("k", u.transform);
            var clip = new AnimationClip();
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("U/k", typeof(Transform), "m_LocalPosition.x"), AnimationCurve.Linear(0, 0, 1, 1));
            AssetDatabase.CreateAsset(clip, F.Dir + "/SV06e.anim");
            r.AddComponent<Animator>().runtimeAnimatorController = AnimatorController.CreateAnimatorControllerAtPathWithClip(F.Dir + "/SV06e.controller", clip);
            Assert.That(Of(m_Fx.Player("SV06eCurves", With(F.Item<PositionItem>(F.Tween), TargetRefMode.Direct, u.transform)), "FB-G02").Single().Fixes, Is.Empty);
        }

        // ---- the pivot and prefabs ----

        // an instance's part: no pivot in place; the fix in its prefab (not undoable) — the player outside is retargeted after
        [Test]
        public void SV07a_AnInstancesPart()
        {
            GameObject plain = Prefab("SV07a", Body);
            Transform it = Instance(plain).transform.Find("T");
            FeedbackPlayer p = m_Fx.Player("SV07a", With(F.Item<PositionItem>(F.Tween), TargetRefMode.Direct, it));
            RuleResult g02 = Of(p, "FB-G02").Single();
            RuleFix fix = g02.Fixes.Single();
            Assert.That(fix.Label, Is.EqualTo("Fix in the prefab — every instance of SV07a changes" + RuleFix.NotUndoable));
            FeedbackValidator.Apply(g02, fix);
            Assert.That(it.Find(VisualPivot.Name + "/c1"), Is.Not.Null, "the instance follows its asset");
            Assert.That(((TargetItem)p.m_Items[0]).m_Target.m_Object, Is.EqualTo(it.Find(VisualPivot.Name)));
            Assert.That(AssetDatabase.LoadAssetAtPath<GameObject>(F.Dir + "/SV07a.prefab").transform.Find("T/" + VisualPivot.Name + "/c1"), Is.Not.Null);
        }

        // nested: the inner asset · a variant: the base (no empty pivot) · a variant's own child: no button
        [Test]
        public void SV07b_NestedAndVariants()
        {
            GameObject inner = Prefab("SV07bInner", Body);
            GameObject outer = Prefab("SV07bOuter", root => ((GameObject)PrefabUtility.InstantiatePrefab(inner)).transform.SetParent(root, false));
            Transform nt = Instance(outer).GetComponentsInChildren<Transform>(true).First(x => x.name == "T");
            FeedbackPlayer pn = m_Fx.Player("SV07bNested", With(F.Item<PositionItem>(F.Tween), TargetRefMode.Direct, nt));
            RuleResult g = Of(pn, "FB-G02").Single();
            Assert.That(g.Fixes.Single().Name, Is.EqualTo("Fix in the prefab — every instance of SV07bInner changes"));
            FeedbackValidator.Apply(g, g.Fixes.Single());
            Assert.That(nt.Find(VisualPivot.Name + "/c1"), Is.Not.Null);
            GameObject baseAsset = Prefab("SV07bBase", Body);
            var vi = (GameObject)PrefabUtility.InstantiatePrefab(baseAsset);
            GameObject variant = PrefabUtility.SaveAsPrefabAsset(vi, F.Dir + "/SV07bVariant.prefab");
            Object.DestroyImmediate(vi);
            Transform vt = Instance(variant).transform.Find("T");
            FeedbackPlayer pv = m_Fx.Player("SV07bVariant", With(F.Item<PositionItem>(F.Tween), TargetRefMode.Direct, vt));
            g = Of(pv, "FB-G02").Single();
            Assert.That(g.Fixes.Single().Name, Is.EqualTo("Fix in the prefab — every instance of SV07bBase changes"), "T's original asset");
            FeedbackValidator.Apply(g, g.Fixes.Single());
            Assert.That(vt.Find(VisualPivot.Name + "/c1"), Is.Not.Null);
            Assert.That(vt.GetComponentsInChildren<Transform>(true).Count(x => x.name == VisualPivot.Name), Is.EqualTo(1), "no empty pivot");
            var vi2 = (GameObject)PrefabUtility.InstantiatePrefab(baseAsset);
            new GameObject("v1").transform.SetParent(vi2.transform.Find("T"), false);
            GameObject variant2 = PrefabUtility.SaveAsPrefabAsset(vi2, F.Dir + "/SV07bVariant2.prefab");
            Object.DestroyImmediate(vi2);
            FeedbackPlayer pv2 = m_Fx.Player("SV07bAdded", With(F.Item<PositionItem>(F.Tween), TargetRefMode.Direct, Instance(variant2).transform.Find("T")));
            Assert.That(Of(pv2, "FB-G02").Single().Fixes, Is.Empty, "a child the variant added");
        }

        // the player inside the prefab: the copy retargets too · a prefab asset's player · an open stage in place
        [Test]
        public void SV07c_PlayersInsideAPrefab()
        {
            GameObject inside = Prefab("SV07cInside", BodyWithPlayer);
            GameObject ii = Instance(inside);
            RuleResult g = Of(ii.GetComponent<FeedbackPlayer>(), "FB-G02").Single();
            FeedbackValidator.Apply(g, g.Fixes.Single());
            Assert.That(((TargetItem)ii.GetComponent<FeedbackPlayer>().m_Items[0]).m_Target.m_Object, Is.EqualTo(ii.transform.Find("T/" + VisualPivot.Name)));
            GameObject asset = Prefab("SV07cAsset", BodyWithPlayer);
            g = Of(asset.GetComponent<FeedbackPlayer>(), "FB-G02").Single();
            Assert.That(g.Fixes.Single().Undoable, Is.False, "an asset's player — its Direct target taken");
            FeedbackValidator.Apply(g, g.Fixes.Single());
            GameObject saved = AssetDatabase.LoadAssetAtPath<GameObject>(F.Dir + "/SV07cAsset.prefab");
            Assert.That(saved.transform.Find("T/" + VisualPivot.Name + "/c1"), Is.Not.Null);
            Assert.That(((TargetItem)saved.GetComponent<FeedbackPlayer>().m_Items[0]).m_Target.m_Object, Is.EqualTo(saved.transform.Find("T/" + VisualPivot.Name)));
            Prefab("SV07cStaged", BodyWithPlayer);
            PrefabStage stage = PrefabStageUtility.OpenPrefab(F.Dir + "/SV07cStaged.prefab");
            Assert.That(Of(stage.prefabContentsRoot.GetComponent<FeedbackPlayer>(), "FB-G02").Single().Fixes.Select(f => f.Name), Does.Contain("Make a visual pivot"), "a stage is edited in place");
            StageUtility.GoToMainStage();
        }
    }
}
