using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using xpTURN.Feedback.Editor;
using Object = UnityEngine.Object;
using F = xpTURN.Feedback.Tests.Editor.ValidationFixtures;

namespace xpTURN.Feedback.Tests.Editor
{
    // the framework · the sweep of every fix
    internal sealed class ValidationFrameworkTests
    {
        F m_Fx;

        [SetUp]
        public void SetUp() => m_Fx = new F();

        [TearDown]
        public void TearDown() => m_Fx.Clean();

        static readonly string[] EditorRules =
        {
            "FB-A01", "FB-A02", "FB-A03", "FB-A04", "FB-A05", "FB-A06", "FB-A07", "FB-A08", "FB-A09", "FB-A10",
            "FB-B01", "FB-B02", "FB-B03", "FB-B04", "FB-B05", "FB-B06", "FB-B07", "FB-B08", "FB-B09",
            "FB-C01", "FB-C02", "FB-C03", "FB-C04", "FB-C05", "FB-C06", "FB-C07",
            "FB-D01", "FB-D02", "FB-D03", "FB-D04", "FB-D05", "FB-D06", "FB-D07", "FB-D08",
            "FB-E01", "FB-E02", "FB-E03", "FB-E04", "FB-E05", "FB-E06", "FB-E07", "FB-E08", "FB-E09", "FB-E10",
            "FB-F01", "FB-F02", "FB-F03", "FB-F04", "FB-F05", "FB-F06", "FB-F07", "FB-F08", "FB-F09", "FB-F11", "FB-F12", "FB-F13", "FB-F14", "FB-F15",
            "FB-G01", "FB-G02", "FB-G03", "FB-G04", "FB-G05", "FB-G06", "FB-G07", "FB-G08", "FB-G09", "FB-G10", "FB-G11", "FB-G12", "FB-G13", "FB-G14",
            "FB-H01", "FB-H02", "FB-H03", "FB-H04", "FB-H05", "FB-H06",
            "FB-I01", "FB-I02", "FB-I03", "FB-I04", "FB-I05", "FB-I06", "FB-I07",
            "FB-J01", "FB-J02", "FB-J03", "FB-J04", "FB-J05", "FB-J06", "FB-J07",
            "FB-K01", "FB-K02", "FB-K03", "FB-K04", "FB-K05", "FB-K06",
            "FB-L01", "FB-L02", "FB-L03", "FB-L04", "FB-L05", "FB-L06", "FB-L07", "FB-L08",
            "FB-M01", "FB-M02", "FB-M03", "FB-M04", "FB-M05",
            "FB-N01", "FB-N02", "FB-N03", "FB-N04", "FB-N05",
            "FB-O01", "FB-O02", "FB-O03", "FB-O04", "FB-O05", "FB-O06", "FB-O07", "FB-O08",
            "FB-P01", "FB-P02", "FB-P03", "FB-P04", "FB-P05", "FB-P06", "FB-P07", "FB-P08", "FB-P09", "FB-P10", "FB-P11", "FB-P12", "FB-P13",
            "FB-Q01", "FB-Q02", "FB-Q03", "FB-Q04",
            "FB-R01", "FB-R02", "FB-R03", "FB-R04",
            "FB-S01",
        };

        [Test]
        public void VT01a_TheRulesAndTheTestAssemblysRule()
        {
            List<string> ids = FeedbackValidator.Rules.Select(r => r.Id).ToList();
            Assert.That(ids.Where(id => !id.StartsWith("FB-Z", StringComparison.Ordinal)), Is.EqualTo(EditorRules), "the editor's rules, in id order");
            Assert.That(ids, Does.Contain("FB-Z99"), "a rule of the test assembly");
            Assert.That(FeedbackValidator.Rules.Where(r => r.GetType().Assembly == typeof(FeedbackRule).Assembly).Select(r => r.Id), Is.EqualTo(EditorRules));
        }

        [Test]
        public void VT01b_ASecondRuleOfAnIdIsLeftOut()
        {
            LogAssert.Expect(LogType.Error, new Regex("two rules share the id FB-Q01"));
            List<FeedbackRule> rules = FeedbackValidator.Collect(new FeedbackRule[] { new ValidationIdRule("FB-Q02"), new ValidationIdRule("FB-Q01"), new ValidationIdRule("FB-Q01") });
            Assert.That(rules.Select(r => r.Id), Is.EqualTo(new[] { "FB-Q01", "FB-Q02" }));
        }

        [Test]
        public void VT01c_TheResultsSlotAndTheHighestSeverity()
        {
            var stop = new HitStopItem { m_FreezeDuration = float.PositiveInfinity };
            stop.m_Timing.m_Delay = -1f;
            FeedbackPlayer p = m_Fx.Player("VT01c", F.Position(), stop);
            p.m_AutoPlay = PlayerAutoPlay.OnEnable;
            List<RuleResult> results = FeedbackValidator.Run(p);
            List<RuleResult> slot1 = results.Where(r => r.Item == 1).ToList();
            Assert.That(slot1.Select(r => r.Severity), Is.EquivalentTo(new[] { RuleSeverity.Warning, RuleSeverity.Error }), "the delay's warning and the freeze's error");
            Assert.That(slot1[0].Severity, Is.EqualTo(RuleSeverity.Warning), "a warning first");
            Assert.That(FeedbackValidator.Highest(results, 1), Is.EqualTo(RuleSeverity.Error));
            Assert.That(FeedbackValidator.Highest(results, 0), Is.Null);
            Assert.That(FeedbackValidator.Highest(results, -1), Is.EqualTo(RuleSeverity.Info), "the player's — D02");
            Assert.That(results.All(r => r.Host == p));
        }

        [Test]
        public void VT01d_AFixIsOneUndoGroupAndTheHostRunsAgain()
        {
            FeedbackPlayer p = m_Fx.Player("VT01d", F.Position(), null);
            Undo.ClearAll();
            RuleResult r = F.Of(p, "FB-B02").Single();
            FeedbackValidator.Apply(r, r.Fixes.Single());
            Assert.That(p.m_Items.Count, Is.EqualTo(1));
            Assert.That(F.Of(p, "FB-B02"), Is.Empty);
            Undo.PerformUndo();
            Assert.That(p.m_Items.Count, Is.EqualTo(2), "one undo");
            Assert.That(F.Of(p, "FB-B02"), Has.Count.EqualTo(1));
        }

        [Test]
        public void VT01e_AThrowingFixIsCaught()
        {
            LogAssert.Expect(LogType.Error, new Regex("the fix 'Throw' of FB-Z99 failed — thrown on purpose"));
            FeedbackPlayer p = m_Fx.Player(ValidationFakeRule.Prefix + "Throw");
            RuleResult r = F.Of(p, "FB-Z99").Single();
            Assert.DoesNotThrow(() => FeedbackValidator.Apply(r, r.Fixes.Single()));
            Assert.That(F.Of(p, "FB-Z99"), Has.Count.EqualTo(1), "the result stays");
        }

        // a project rule never runs in a host's check: an ownerless receiver and two settings would report
        [Test]
        public void VT01f_AHostsCheckHasNoProjectRule()
        {
            var receiver = new GameObject("VT01fReceiver");
            m_Fx.Made.Add(receiver);
            receiver.AddComponent<FeedbackReceiver>();
            FeedbackPlayer p = m_Fx.Player("VT01f", F.Position());
            Assert.That(FeedbackValidator.RunProject().Select(r => r.Id), Does.Contain("FB-A08"), "the project's check reports it");
            Assert.That(FeedbackValidator.Run(p).Where(r => r.Rule.Scope == RuleScope.Project), Is.Empty);
        }

        // the run 0.3 s after an edit (the editor clock)
        [Test]
        public void VT01g_TheRunWaitsForTheDelay()
        {
            Func<double> real = PreviewClock.Source;
            double now = 1000;
            PreviewClock.Source = () => now;
            try
            {
                FeedbackPlayer p = m_Fx.Player("VT01g", F.Position());
                var so = new SerializedObject(p);
                var view = new ValidationView(p, so, new ItemListView(so, ItemListEdits.ItemsPath, p, null), new VisualElement());
                int first = view.Runs;
                Assert.That(first, Is.EqualTo(1), "a run when drawn");
                view.Schedule();
                now += 0.29;
                view.Poll();
                Assert.That(view.Runs, Is.EqualTo(first), "not yet");
                now += 0.02;
                view.Poll();
                Assert.That(view.Runs, Is.EqualTo(first + 1));
                view.Poll();
                Assert.That(view.Runs, Is.EqualTo(first + 1), "once");
            }
            finally
            {
                PreviewClock.Source = real;
            }
        }

        // a rule's words name no stage — the known list is empty
        static readonly string[] KnownStageNames = { };

        [Test]
        public void VT01h_NoStageNameInTheWords()
        {
            var package = UnityEditor.PackageManager.PackageInfo.FindForAssetPath("Packages/com.xpturn.feedback");
            Assert.That(package, Is.Not.Null);
            var literal = new Regex(@"""((?:[^""\\]|\\.)*)""");
            var stage = new Regex(@"\bP\d\b");
            var found = new List<string>();
            int literals = 0;
            foreach (string file in System.IO.Directory.GetFiles(System.IO.Path.Combine(package.resolvedPath, "Editor", "Validation"), "*.cs", System.IO.SearchOption.AllDirectories))
            {
                foreach (Match m in literal.Matches(System.IO.File.ReadAllText(file)))
                {
                    literals++;
                    if (stage.IsMatch(m.Groups[1].Value))
                        found.Add(System.IO.Path.GetFileName(file) + ": " + m.Groups[1].Value.Trim());
                }
            }
            Assert.That(literals, Is.GreaterThan(100), "the words were read");
            Assert.That(found.OrderBy(x => x, StringComparer.Ordinal), Is.EqualTo(KnownStageNames));
        }

        static BlinkMotion Blinking => new BlinkMotion { m_Params = new BlinkParams { m_Value = Vector4.zero, m_Period = 0.4f, m_Duty = 0.5f, m_StartOn = true } };

        // a prefab asset to spawn (the folder goes at the clean)
        static GameObject SpawnPrefab(F fx)
        {
            var source = new GameObject("SwSpawn");
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(source, F.Dir + "/SwSpawn.prefab");
            Object.DestroyImmediate(source);
            return prefab;
        }

        // a looping particle prefab (K03) · a system whose emission is off (K04)
        static GameObject LoopingParticlePrefab(F fx)
        {
            var source = new GameObject("SwLooping");
            ParticleSystem.MainModule main = source.AddComponent<ParticleSystem>().main;
            main.playOnAwake = false;
            main.loop = true;
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(source, F.Dir + "/SwLooping.prefab");
            Object.DestroyImmediate(source);
            return prefab;
        }

        // an animator on the test controller (the folder goes at the clean)
        static Animator TestAnimator(F fx, string name, AnimatorUpdateMode mode)
        {
            var go = new GameObject(name);
            fx.Made.Add(go);
            var a = go.AddComponent<Animator>();
            a.updateMode = mode;
            a.runtimeAnimatorController = FeedbackTestAnimator.Make(F.Dir + "/" + name + ".controller");
            return a;
        }

        // a body
        static Rigidbody Body(F fx, string name)
        {
            var go = new GameObject(name);
            fx.Made.Add(go);
            return go.AddComponent<Rigidbody>();
        }

        static ParticleSystem EmissionOff(F fx, string name)
        {
            var go = new GameObject(name);
            fx.Made.Add(go);
            var ps = go.AddComponent<ParticleSystem>();
            ParticleSystem.EmissionModule e = ps.emission;
            e.enabled = false;
            return ps;
        }

        static GameObject Cube(F fx, string name)
        {
            GameObject c = GameObject.CreatePrimitive(PrimitiveType.Cube);
            c.name = name;
            fx.Made.Add(c);
            return c;
        }

        static T On<T>(T item, Object target) where T : TargetItem
        {
            item.m_Target.m_Mode = TargetRefMode.Direct;
            item.m_Target.m_Object = target;
            return item;
        }

        // a prefab of T (a rigidbody) and its child — the scene rules' pivot fixtures
        static GameObject BodyPrefab(string name, bool player)
        {
            var root = new GameObject(name);
            var t = new GameObject("T");
            t.transform.SetParent(root.transform, false);
            t.AddComponent<Rigidbody>();
            new GameObject("c1").transform.SetParent(t.transform, false);
            if (player)
                root.AddComponent<FeedbackPlayer>().m_Items.Add(On(F.Item<PositionItem>(F.Tween), t.transform));
            GameObject asset = PrefabUtility.SaveAsPrefabAsset(root, F.Dir + "/" + name + ".prefab");
            Object.DestroyImmediate(root);
            return asset;
        }

        // every fix of the rule fixtures: an undoable one is one undo back to the snapshot; the not undoable ones are the list.
        // A reload of the scene is not run (it closes the test's scenes), only its presence is looked at
        internal static readonly (string Rule, Func<F, Object> Make)[] SweepFixtures =
        {
            // a main camera of the fixture's own — without URP's data (its post-processing off) · with it on but a volume mask of
            // nothing
            ("FB-Q01", fx =>
            {
                fx.OwnMainCamera("SwQ01Camera", false);
                return fx.Player("SwQ01", F.Item<PostFXBloomItem>(F.Tween));
            }),
            ("FB-Q02", fx =>
            {
                Camera cam = fx.OwnMainCamera("SwQ02Camera", true, true);
                Type data = Type.GetType(PostFXNames.CameraData);
                if (data != null)
                {
                    var so = new SerializedObject(cam.GetComponent(data));
                    so.FindProperty("m_VolumeLayerMask").intValue = 0;
                    so.ApplyModifiedPropertiesWithoutUndo();
                }
                return fx.Player("SwQ02", F.Item<PostFXBloomItem>(F.Tween));
            }),
            // a main camera Cinemachine drives and its live camera without the shake extension (the package's types by name)
            ("FB-R01", fx =>
            {
                Camera cam = fx.OwnMainCamera("SwR01Camera", false);
                Type brain = Type.GetType(CinemachineNames.Brain);
                if (brain != null)
                {
                    cam.gameObject.AddComponent(brain);
                    var vcam = new GameObject("SwR01Vcam");
                    fx.Made.Add(vcam);
                    vcam.AddComponent(Type.GetType(CinemachineNames.Camera));
                }
                return fx.Player("SwR01", F.Item<CameraShakeItem>(new ShakeMotion(), TargetRefMode.MainCamera));
            }),
            // a preset host · a link to a deleted preset
            ("FB-A09", fx =>
            {
                var preset = ScriptableObject.CreateInstance<FeedbackPreset>();
                preset.m_Items.Add(F.Item<PositionItem>(F.Tween, TargetRefMode.Direct));
                AssetDatabase.CreateAsset(preset, F.Dir + "/SwA09.asset");
                return preset;
            }),
            ("FB-B08", fx =>
            {
                var preset = ScriptableObject.CreateInstance<FeedbackPreset>();
                AssetDatabase.CreateAsset(preset, F.Dir + "/SwB08.asset");
                FeedbackPlayer p = fx.Player("SwB08", F.Position());
                p.m_Preset = preset;
                AssetDatabase.DeleteAsset(F.Dir + "/SwB08.asset");
                return p;
            }),
            ("FB-A01", fx =>
            {
                FeedbackPlayer p = fx.Player("SwA01", F.Item<LightIntensityItem>(F.Tween));
                var child = new GameObject("SwA01Light");
                child.transform.SetParent(p.transform);
                child.AddComponent<Light>();
                return p;
            }),
            ("FB-A02", fx =>
            {
                var src = new GameObject("SwA02Src");
                GameObject asset = PrefabUtility.SaveAsPrefabAsset(src, F.Dir + "/SwA02.prefab");
                Object.DestroyImmediate(src);
                var t = F.Item<PositionItem>(F.Tween, TargetRefMode.Direct);
                t.m_Target.m_Object = asset.transform;
                FeedbackPlayer p = fx.Player("SwA02", t);
                p.m_Anchors.Add(new PlayerAnchor { m_Name = "asset", m_Object = asset.transform });
                return p;
            }),
            ("FB-A03", fx =>
            {
                var there = new GameObject("SwA03There");
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(there, fx.Scene("SwA03"));
                var t = F.Item<PositionItem>(F.Tween, TargetRefMode.Direct);
                t.m_Target.m_Object = there.transform;
                return fx.Player("SwA03", t);
            }),
            ("FB-A05", fx =>
            {
                FeedbackPlayer p = fx.Player("SwA05");
                p.m_Anchors.Add(new PlayerAnchor { m_Name = "y", m_Object = p.transform });
                p.m_UseDistanceGate = true;
                p.m_GateReference = DistanceReference.Anchor;
                p.m_GateAnchor = "x";
                return p;
            }),
            ("FB-A06", fx => fx.Player("SwA06", F.Item<PositionItem>(F.Tween, TargetRefMode.Channel))),
            ("FB-A07", fx =>
            {
                var t = F.Item<CameraShakeItem>(new ShakeMotion(), TargetRefMode.Channel);
                t.m_DirectWrite = true;
                return fx.Player("SwA07", t);
            }),
            ("FB-B01", fx => fx.MissingScenePlayer()),
            ("FB-B01", fx => fx.Instance(fx.MissingPrefab("SwB01"))),
            ("FB-B02", fx => fx.Player("SwB02", F.Position(), null)),
            ("FB-B03", fx =>
            {
                PositionItem a = F.Position();
                return fx.Player("SwB03", a, a);
            }),
            ("FB-B04", fx => fx.ReorderedInstance()),
            ("FB-B04", fx => fx.ReorderedVariant(false)),
            ("FB-B05", fx => fx.OverriddenInstance(true)),
            ("FB-B05", fx => fx.UnusedVariant()),
            ("FB-B06", fx => fx.Player("SwB06", new PositionItem())),
            ("FB-C02", fx =>
            {
                PositionItem t = F.Position();
                t.m_Timing.m_RepeatForever = true;
                FeedbackPlayer p = fx.Player("SwC02", t);
                p.m_Direction = PlayerDirection.Toggle;
                return p;
            }),
            ("FB-C06", fx =>
            {
                PositionItem t = F.Position();
                t.m_PlayDirection = ItemPlayDirection.Backward;
                t.m_Conditions.m_Direction = DirectionCondition.ForwardOnly;
                return fx.Player("SwC06", t);
            }),
            ("FB-C07", fx =>
            {
                SetActiveItem t = F.Item<SetActiveItem>(new SetMotion());
                t.m_PlayDirection = ItemPlayDirection.Backward;
                return fx.Player("SwC07", t);
            }),
            ("FB-C05", fx =>
            {
                PositionItem t = F.Position();
                t.m_Timing.m_Delay = -1f;
                t.m_Random.m_RandomizeDuration = true;
                t.m_Random.m_DurationRange = new Vector2(-1f, 2f);
                FeedbackPlayer p = fx.Player("SwC05", t, new LoopStartItem(), F.Position(), new LoopEndItem { m_LoopCount = -1 }, new HitStopItem { m_FreezeDuration = float.PositiveInfinity });
                p.m_UseDistanceGate = true;
                p.m_GateDistance = 0f;
                return p;
            }),
            ("FB-D01", fx =>
            {
                FeedbackPlayer p = fx.Player("SwD01", F.Position());
                p.m_Initialization = PlayerInitialization.Lazy;
                p.m_ApplyInitialState = true;
                return p;
            }),
            ("FB-D04", fx => fx.Player("SwD04", F.Item<PositionItem>(new SpringMotion()))),
            ("FB-D06", fx =>
            {
                FeedbackPlayer p = fx.Player("SwD06", F.Item<RotationItem>(F.Tween));
                TransformUtils.SetInspectorRotation(p.transform, new Vector3(120f, 0f, 0f));
                return p;
            }),
            ("FB-D07", fx =>
            {
                var stop = new HitStopItem();
                stop.m_Timing.m_Infinite = true;
                return fx.Player("SwD07", stop);
            }),
            // E · F · G
            ("FB-E01", fx =>
            {
                FeedbackPlayer p = fx.Player("SwE01", F.Item<SetActiveItem>(Blinking));
                new GameObject("SwE01Kid").transform.SetParent(p.transform, false);
                p.m_DisableBehavior = PlayerDisableBehavior.StopHold;
                return p;
            }),
            ("FB-E02", fx =>
            {
                var t = new GameObject("SwE02T");
                fx.Made.Add(t);
                var k = new GameObject("k");
                k.transform.SetParent(t.transform, false);
                k.AddComponent<Animator>();
                return fx.Player("SwE02", On(F.Item<SetActiveItem>(Blinking), t));
            }),
            // a Component Enabled turning off its own player · a Direct component moved up
            ("FB-E09", fx =>
            {
                FeedbackPlayer p = fx.Player("SwE09", F.Item<ComponentEnabledItem>(new SetMotion()));
                ((ComponentEnabledItem)p.m_Items[0]).m_ComponentType = typeof(FeedbackPlayer).FullName;
                p.m_DisableBehavior = PlayerDisableBehavior.StopHold;
                return p;
            }),
            ("FB-E10", fx =>
            {
                var g = new GameObject("SwE10T");
                fx.Made.Add(g);
                g.AddComponent<AudioSource>();
                AudioSource second = g.AddComponent<AudioSource>();
                var item = On(new ComponentEnabledItem { m_Motion = new SetMotion(), m_ComponentType = "UnityEngine.AudioSource", m_ComponentIndex = 1 }, second);
                UnityEditorInternal.ComponentUtility.MoveComponentUp(second);
                return fx.Player("SwE10", item);
            }),
            ("FB-E06", fx =>
            {
                var g = new GameObject("SwE06");
                fx.Made.Add(g);
                var d = g.AddComponent<MotionDriver>();
                d.m_Item = F.Item<PositionItem>(new SpringMotion());
                return d;
            }),
            ("FB-E07", fx =>
            {
                var g = new GameObject("SwE07");
                fx.Made.Add(g);
                new GameObject("k").transform.SetParent(g.transform, false);
                var d = g.AddComponent<MotionDriver>();
                d.m_Item = F.Item<SetActiveItem>(Blinking);
                return d;
            }),
            ("FB-F01", fx => fx.Player("SwF01", On(new MaterialColorItem { m_Motion = F.Tween }, Cube(fx, "SwF01Cube").GetComponent<Renderer>()))),
            ("FB-F02", fx =>
            {
                var many = new List<FeedbackItem>();
                for (int i = 0; i < ManyBlockBlinksRule.Count; i++)
                {
                    var m = On(new MaterialColorItem { m_Motion = Blinking }, Cube(fx, "SwF02Cube" + i).GetComponent<Renderer>());
                    m.m_Timing.m_Duration = 1f;
                    many.Add(m);
                }
                return fx.Player("SwF02", many.ToArray());
            }),
            ("FB-F04", fx =>
            {
                Renderer r = Cube(fx, "SwF04Cube").GetComponent<Renderer>();
                Shader color = Shader.Find("Unlit/Color");
                var m1 = new Material(color);
                m1.SetColor("_Color", Color.red);
                r.sharedMaterials = new[] { new Material(color), m1 };
                return fx.Player("SwF04", On(new MaterialColorItem { m_Motion = Blinking }, r));
            }),
            ("FB-F06", fx =>
            {
                Renderer r = Cube(fx, "SwF06Cube").GetComponent<Renderer>();
                r.sharedMaterials = new[] { new Material(Shader.Find("Skybox/Procedural")) };
                return fx.Player("SwF06", On(new MaterialColorItem { m_Motion = Blinking }, r));
            }),
            ("FB-F07", fx => fx.Player("SwF07", On(new MaterialColorItem { m_Motion = Blinking, m_MaterialIndex = 5 }, Cube(fx, "SwF07Cube").GetComponent<Renderer>()))),
            ("FB-F09", fx =>
            {
                Renderer r = Cube(fx, "SwF09Cube").GetComponent<Renderer>();
                return fx.Player("SwF09", On(new MaterialSwapItem { m_Motion = new SetMotion(), m_Material = r.sharedMaterial, m_MaterialIndex = 5 }, r));
            }),
            // a direct Sprite Color beside a Sprite Alpha of the same renderer
            ("FB-F13", fx =>
            {
                var sr = new GameObject("SwF13Sprite").AddComponent<SpriteRenderer>();
                fx.Made.Add(sr.gameObject);
                SpriteColorItem direct = On(new SpriteColorItem { m_Motion = F.Tween }, sr);
                direct.m_DirectWrite = true;
                return fx.Player("SwF13", direct, On(new SpriteAlphaItem { m_Motion = F.Tween }, sr));
            }),
            // a pool of nothing · a spawn without a pool
            ("FB-J02", fx => fx.Player("SwJ02", On(new SpawnItem { m_Motion = new SetMotion(), m_Prefab = SpawnPrefab(fx), m_PoolSize = 0 }, Cube(fx, "SwJ02T").transform))),
            ("FB-J03", fx => fx.Player("SwJ03", On(new SpawnItem { m_Motion = new SetMotion(), m_Prefab = SpawnPrefab(fx), m_Pooled = false }, Cube(fx, "SwJ03T").transform))),
            // a looping spawn without Stop After · a rate on a system whose emission is off
            ("FB-K03", fx => fx.Player("SwK03", On(new ParticleSpawnItem { m_Motion = new SetMotion(), m_Prefab = LoopingParticlePrefab(fx) }, Cube(fx, "SwK03T").transform))),
            ("FB-K04", fx => fx.Player("SwK04", On(new ParticleEmissionRateItem { m_Motion = F.Tween }, EmissionOff(fx, "SwK04System")))),
            // a parameter's name in another case · a Fixed animator's punch
            ("FB-L02", fx => fx.Player("SwL02", On(new AnimatorParameterItem { m_Motion = new SetMotion(), m_Parameter = "on", m_Kind = AnimatorParameterKind.Bool }, TestAnimator(fx, "SwL02", AnimatorUpdateMode.Normal)))),
            ("FB-L05", fx => fx.Player("SwL05", On(new AnimatorSpeedItem { m_Motion = new PunchMotion() }, TestAnimator(fx, "SwL05", AnimatorUpdateMode.Fixed)))),
            // a force of one step
            ("FB-M03", fx => fx.Player("SwM03", On(new RigidbodyForceItem { m_Motion = new SetMotion(), m_Mode = PhysicsForceMode.Force }, Body(fx, "SwM03Body")))),
            // a Time Scale tween stays — the fix is a flash
            ("FB-N03", fx => fx.Player("SwN03", new TimeScaleItem { m_Motion = F.Tween })),
            // a wait on itself · empty entries · a break · a background wait · a preset's player
            ("FB-O01", fx =>
            {
                FeedbackPlayer p = fx.Player("SwO01");
                p.m_Items.Add(new PlayPlayerItem { m_Player = p });
                return p;
            }),
            ("FB-O03", fx =>
            {
                var random = new RandomPlayerItem();
                random.m_Entries.Add(new WeightedPlayer { m_Player = fx.Player("SwO03Target") });
                random.m_Entries.Add(new WeightedPlayer());
                return fx.Player("SwO03", random);
            }),
            ("FB-O06", fx => fx.Player("SwO06", new DebugItem { m_Action = DebugAction.Break })),
            ("FB-O07", fx =>
            {
                var waits = new PlayPlayerItem { m_Player = fx.Player("SwO07Target") };
                waits.m_Timing.m_Background = true;
                return fx.Player("SwO07", waits);
            }),
            ("FB-O08", fx =>
            {
                var preset = ScriptableObject.CreateInstance<FeedbackPreset>();
                preset.m_Items.Add(new PlayPlayerItem { m_Player = fx.Player("SwO08Target") });
                AssetDatabase.CreateAsset(preset, F.Dir + "/SwO08.asset");
                return preset;
            }),
            // a skybox without the property — the fixes name the shader's floats
            ("FB-I04", fx =>
            {
                if (RenderSettings.skybox == null)
                    RenderSettings.skybox = AssetDatabase.GetBuiltinExtraResource<Material>("Default-Skybox.mat");
                return fx.Player("SwI04", new SkyboxExposureItem { m_Motion = F.Tween, m_PropertyName = "_SwI04None" });
            }),
            ("FB-G01", fx =>
            {
                GameObject t = Cube(fx, "SwG01T");
                t.AddComponent<Rigidbody>().interpolation = RigidbodyInterpolation.Interpolate;
                new GameObject("visual").transform.SetParent(t.transform, false);
                return fx.Player("SwG01", On(F.Item<PositionItem>(F.Tween), t.transform), On(F.Item<RotationItem>(F.Tween), t.transform));
            }),
            ("FB-G02", fx =>
            {
                var inst = (GameObject)PrefabUtility.InstantiatePrefab(BodyPrefab("SwG02", false));
                fx.Made.Add(inst);
                return fx.Player("SwG02", On(F.Item<PositionItem>(F.Tween), inst.transform.Find("T")));
            }),
            ("FB-G02", fx => BodyPrefab("SwG02a", true).GetComponent<FeedbackPlayer>()),
            ("FB-G03", fx => fx.Player("SwG03", On(F.Item<PositionItem>(F.Tween), Cube(fx, "SwG03Cube").transform))),
            ("FB-G04", fx =>
            {
                var outer = new GameObject("SwG04Outer");
                fx.Made.Add(outer);
                outer.AddComponent<Rigidbody>();
                var part = new GameObject("part");
                part.transform.SetParent(outer.transform, false);
                var col = new GameObject("collider");
                col.transform.SetParent(part.transform, false);
                col.AddComponent<BoxCollider>();
                new GameObject("visual").transform.SetParent(part.transform, false);
                return fx.Player("SwG04", On(F.Item<PositionItem>(F.Tween), part.transform));
            }),
            ("FB-G05", fx =>
            {
                var b = new GameObject("SwG05Body");
                fx.Made.Add(b);
                b.AddComponent<Rigidbody>();
                PositionItem t = On(F.Item<PositionItem>(F.Tween), b.transform);
                t.m_DirectWrite = true;
                return fx.Player("SwG05", t);
            }),
        };

        // the rules without a fix — with the sweep's, every editor rule (a new rule must join one of the two)
        static readonly string[] Fixless =
        {
            "FB-A04", "FB-A08", "FB-B07", "FB-C01", "FB-C03", "FB-C04", "FB-D02", "FB-D03", "FB-D05", "FB-D08",
            "FB-E03", "FB-E04", "FB-E05", "FB-E08", "FB-F03", "FB-F05", "FB-G06", "FB-G07", "FB-G08", "FB-G09", "FB-G10",
            "FB-H01", "FB-H02", "FB-H03", "FB-H04", "FB-H05", "FB-H06",
            "FB-A10", "FB-G11", "FB-G12", "FB-G13", "FB-G14",
            "FB-F08", "FB-F11",
            "FB-I01", "FB-I02", "FB-I03", "FB-I05", "FB-I06", "FB-I07",
            "FB-F12", "FB-F14", "FB-F15",
            "FB-J01", "FB-J04", "FB-J05", "FB-J06", "FB-J07",
            "FB-K01", "FB-K02", "FB-K05", "FB-K06",
            "FB-L01", "FB-L03", "FB-L04", "FB-L06", "FB-L07", "FB-L08",
            "FB-M01", "FB-M02", "FB-M04", "FB-M05",
            "FB-N01", "FB-N02", "FB-N04", "FB-N05",
            "FB-B09",
            "FB-O02", "FB-O04", "FB-O05",
            "FB-P01", "FB-P02", "FB-P03", "FB-P04", "FB-P05", "FB-P06", "FB-P07", "FB-P08", "FB-P09", "FB-P10", "FB-P11", "FB-P12", "FB-P13",
            "FB-Q03", "FB-Q04",
            "FB-R02", "FB-R03", "FB-R04",
            "FB-S01",
        };

        static readonly string[] NotUndoable =
        {
            "FB-B01 Clear missing entries (scene)", "FB-B01 Clear missing entries (instance)",
            "FB-B04 Apply the list structure to the base (asset)", "FB-B05 Remove unused overrides (asset)",
            "FB-G02 Fix in the prefab — every instance of SwG02 changes (scene)", "FB-G02 Fix in the prefab — every instance of SwG02a changes (asset)",
        };

        [Test]
        public void VT02a_EveryFixIsOneUndoOrSaysItIsNot()
        {
            LogAssert.ignoreFailingMessages = true;
            Assert.That(SweepFixtures.Select(f => f.Rule).Concat(Fixless).Distinct().OrderBy(x => x, StringComparer.Ordinal), Is.EqualTo(EditorRules), "every editor rule is swept or listed fixless");
            var notUndoable = new List<string>();
            var failures = new List<string>();
            int fixes = 0;
            int swept = 0;
            foreach (var (rule, make) in SweepFixtures)
            {
                // the PostFX rules are quiet without URP (the minimal project)
                if (rule.StartsWith("FB-Q", StringComparison.Ordinal) && Type.GetType(PostFXNames.CameraData) == null)
                    continue;
                // the Cinemachine rules are quiet without the package
                if (rule.StartsWith("FB-R", StringComparison.Ordinal) && Type.GetType(CinemachineNames.ShakeExtension) == null)
                    continue;
                swept++;
                int count;
                var probe = new F();
                try
                {
                    count = F.Of(make(probe), rule).SelectMany(r => r.Fixes).Count();
                }
                finally
                {
                    probe.Clean();
                }
                Assert.That(count, Is.GreaterThan(0), rule + " offers a fix");
                for (int f = 0; f < count; f++)
                {
                    var fx = new F();
                    try
                    {
                        Object host = make(fx);
                        var pairs = F.Of(host, rule).SelectMany(r => r.Fixes.Select(x => (r, x))).ToList();
                        (RuleResult result, RuleFix fix) = pairs[f];
                        string place = EditorUtility.IsPersistent(host) ? "asset" : PrefabUtility.IsPartOfPrefabInstance(host) ? "instance" : "scene";
                        string key = rule + " " + fix.Name + " (" + place + ")";
                        Assert.That(fix.Name.StartsWith("Reopen", StringComparison.Ordinal), Is.False, key + " — offered with several scenes open");
                        Undo.ClearAll();
                        List<string> before = F.Snapshot(host);
                        FeedbackValidator.Apply(result, fix);
                        fixes++;
                        if (!fix.Undoable)
                        {
                            notUndoable.Add(key);
                            continue;
                        }
                        Undo.PerformUndo();
                        if (!before.SequenceEqual(F.Snapshot(host)))
                            failures.Add(key);
                    }
                    finally
                    {
                        fx.Clean();
                    }
                }
            }
            Assert.That(failures, Is.Empty, "one undo restores");
            Assert.That(notUndoable, Is.EquivalentTo(NotUndoable));
            Assert.That(fixes, Is.GreaterThan(swept));
        }

        // an undo-recorded scene change re-runs 0.3 s later (the frame after)
        [UnityTest]
        public IEnumerator VT01h_ASceneChangeRunsAgain()
        {
            Func<double> real = PreviewClock.Source;
            double now = 3000;
            PreviewClock.Source = () => now;
            var window = ScriptableObject.CreateInstance<PreviewBarTestWindow>();
            window.Show();
            try
            {
                FeedbackPlayer p = m_Fx.Player("VT01h", F.Item<LightIntensityItem>(F.Tween));
                var inspector = InspectorTesting.Inspector(window, p);
                yield return InspectorTesting.Wait(0.2);
                var view = inspector.Q<ValidationView>();
                Assert.That(view.Results.Select(r => r.Id), Does.Contain("FB-A01"));
                Undo.AddComponent<Light>(p.gameObject);
                yield return null;
                yield return null;
                yield return null;
                Assert.That(view.Results.Select(r => r.Id), Does.Contain("FB-A01"), "not before the delay");
                now += 0.31;
                view.Poll();
                Assert.That(view.Results.Select(r => r.Id), Does.Not.Contain("FB-A01"));
            }
            finally
            {
                PreviewClock.Source = real;
                window.Close();
            }
        }
    }
}
