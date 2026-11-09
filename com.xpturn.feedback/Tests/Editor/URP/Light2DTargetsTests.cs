using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using xpTURN.Feedback.Editor;
using Object = UnityEngine.Object;

namespace xpTURN.Feedback.Tests.Editor
{
    // the 2D light items and the render: a test scheduler ticked by hand (0.05 s); this assembly names Light2D (URP's package
    // — the main test assemblies can't)
    public sealed class Light2DTargetsTests
    {
        const float Step = 0.05f;
        const string ScenePath = "Assets/Light2DTargetsTests.unity";

        readonly List<Object> m_Made = new List<Object>();
        readonly List<FeedbackScheduler> m_Schedulers = new List<FeedbackScheduler>();
        readonly List<FeedbackPlayer> m_Players = new List<FeedbackPlayer>();

        [TearDown]
        public void TearDown()
        {
            foreach (FeedbackPlayer p in m_Players)
            {
                if (p != null)
                    p.ReleaseRuntime();
            }
            foreach (FeedbackScheduler s in m_Schedulers)
            {
                if (!s.IsShutdown)
                    s.Shutdown();
            }
            for (int i = m_Made.Count - 1; i >= 0; i--)
            {
                if (m_Made[i] != null)
                    Object.DestroyImmediate(m_Made[i]);
            }
            m_Made.Clear();
            m_Schedulers.Clear();
            m_Players.Clear();
        }

        static string B(float f) => BitConverter.SingleToInt32Bits(f).ToString("X8");

        static string Bits(Light2D l) => B(l.intensity) + "/" + B(l.color.r) + B(l.color.g) + B(l.color.b) + B(l.color.a) + "/" + B(l.pointLightOuterRadius) + "/" + B(l.pointLightInnerRadius) + "/" + B(l.falloffIntensity);

        static string RadiusBits(Light2D l) => B(l.pointLightOuterRadius) + "/" + B(l.pointLightInnerRadius);

        static string RadiusValues(Light2D l) => l.pointLightOuterRadius + "/" + l.pointLightInnerRadius;

        static TargetRef Direct(Object o) => new TargetRef { m_Mode = TargetRefMode.Direct, m_Object = o, m_Path = "", m_Anchor = "" };

        static TweenMotion TweenTo(Vector4 to)
        {
            TweenParams p = TweenParams.Default;
            p.m_To = to;
            return new TweenMotion { m_Params = p };
        }

        static OscillateMotion Swing(float amplitude) => new OscillateMotion { m_Params = new OscillateParams { m_Amplitude = new Vector4(amplitude, amplitude, amplitude, amplitude), m_Frequency = 2f } };

        static FlashMotion FlashTo(Vector4 target) => new FlashMotion { m_Params = new FlashParams { m_Target = target, m_Attack = 0.25f, m_Hold = 0.5f, m_Release = 0.25f } };

        static T On<T>(T item, Object target, FeedbackMotion motion, float duration) where T : TargetItem
        {
            item.m_Target = Direct(target);
            item.m_Motion = motion;
            item.m_Timing.m_Duration = duration;
            return item;
        }

        FeedbackScheduler Sched()
        {
            var s = new FeedbackScheduler(ContextKind.Test);
            m_Schedulers.Add(s);
            return s;
        }

        FeedbackPlayer Player(FeedbackScheduler s, params FeedbackItem[] items)
        {
            var go = new GameObject("Player");
            m_Made.Add(go);
            var p = go.AddComponent<FeedbackPlayer>();
            p.m_Items.AddRange(items);
            p.Bind(s);
            p.EnsureRuntime();
            m_Players.Add(p);
            return p;
        }

        static void Tick(FeedbackScheduler s, int n)
        {
            for (int i = 0; i < n; i++)
                s.Tick(Step);
        }

        Light2D Lamp(Light2D.LightType type = Light2D.LightType.Point)
        {
            var go = new GameObject("Light2D");
            m_Made.Add(go);
            var light = go.AddComponent<Light2D>();
            if (type == Light2D.LightType.Freeform)
            {
                var path = new[] { new Vector3(-0.5f, -0.5f), new Vector3(0.5f, -0.5f), new Vector3(0.5f, 0.5f), new Vector3(-0.5f, 0.5f) };
                MethodInfo set = typeof(Light2D).GetMethod("SetShapePath", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                set.Invoke(light, new object[] { path });
            }
            light.lightType = type;
            light.pointLightOuterRadius = 2f;
            light.pointLightInnerRadius = 0.5f;
            light.intensity = 1.25f;
            return light;
        }

        // a contribution's end: the base bit for bit — and, where the light clamps, a frame at the clamp (the positive)
        string EndsAtTheBase(string name, Func<Light2D, FeedbackItem> item, Func<Light2D, bool> clamped = null)
        {
            FeedbackScheduler s = Sched();
            Light2D l = Lamp();
            string before = Bits(l);
            Player(s, item(l)).Play();
            bool reached = false;
            for (int i = 0; i < 30; i++)
            {
                s.Tick(Step);
                if (clamped != null && clamped(l))
                    reached = true;
            }
            s.Tick(0f);
            if (clamped != null && !reached)
                return name + " never reached the clamp";
            return Bits(l) == before ? null : name + " " + before + " → " + Bits(l);
        }

        // a Persist play (Tween): a Revert stop in the middle and a Restore after the end both give the start back — the light
        // moved before the stop (the positive)
        IEnumerable<string> PersistBack(string name, Func<TargetItem> item, Vector4 to, bool direct = false)
        {
            foreach (bool midway in new[] { true, false })
            {
                FeedbackScheduler s = Sched();
                Light2D l = Lamp();
                string before = Bits(l);
                TargetItem it = On(item(), l, TweenTo(to), 1f);
                it.m_DirectWrite = direct;
                FeedbackPlayer p = Player(s, it);
                p.Play();
                Tick(s, midway ? 16 : 30);
                if (Bits(l) == before)
                    yield return name + (midway ? " — never moved before the Revert stop" : " — never moved before the Restore");
                if (midway)
                    p.Stop(StopMode.Revert);
                else
                    p.Restore();
                s.Tick(0f);
                if (Bits(l) != before)
                    yield return name + (midway ? " — a Revert stop " : " — a Restore ") + before + " → " + Bits(l);
            }
        }

        // ---- each item makes its own binding · contributions end at the base · Persist comes back · a motion it does not take

        [Test]
        public void LD01_EachMakesItsBinding()
        {
            var want = new Dictionary<TargetItem, Type>
            {
                { new Light2DIntensityItem(), typeof(Light2DIntensityBinding) }, { new Light2DColorItem(), typeof(Light2DColorBinding) },
                { new Light2DRadiusItem(), typeof(Light2DRadiusBinding) }, { new Light2DFalloffItem(), typeof(Light2DFalloffBinding) },
            };
            foreach (KeyValuePair<TargetItem, Type> w in want)
                Assert.That(FeedbackImplementations.Create(w.Key)?.GetType(), Is.EqualTo(w.Value), w.Key.GetType().Name);
        }

        [Test]
        public void LD02_ContributionsEndAtTheBase()
        {
            var fails = new List<string>
            {
                EndsAtTheBase("intensity oscillate", l => On(new Light2DIntensityItem(), l, Swing(1f), 1f)),
                EndsAtTheBase("intensity flash", l => On(new Light2DIntensityItem(), l, FlashTo(new Vector4(4f, 0f, 0f, 0f)), 1f)),
                EndsAtTheBase("color flash", l => On(new Light2DColorItem(), l, FlashTo(new Vector4(2f, 0f, -0.5f, 1f)), 1f)),
                EndsAtTheBase("radius oscillate 1.75 (the inner follows)", l => On(new Light2DRadiusItem(), l, Swing(1.75f), 1f), l => l.pointLightInnerRadius < 0.5f),
                EndsAtTheBase("falloff oscillate 1 (the light clamps)", l => On(new Light2DFalloffItem(), l, Swing(1f), 1f), l => l.falloffIntensity == 0f || l.falloffIntensity == 1f),
            };
            {
                // a game write between the revert and the apply stays after the end
                FeedbackScheduler s = Sched();
                Light2D l = Lamp();
                Player(s, On(new Light2DIntensityItem(), l, Swing(1f), 1f)).Play();
                Tick(s, 3);
                s.RunRevert();
                l.intensity = 3.125f;
                s.RunUpdate(new FrameTime(Step, Step));
                s.RunLateApply();
                Tick(s, 30);
                s.Tick(0f);
                if (B(l.intensity) != B(3.125f))
                    fails.Add("the game's intensity 3.125 became " + l.intensity);
            }
            Assert.That(fails.Where(f => f != null), Is.Empty);
        }

        [Test]
        public void LD03_PersistComesBack()
        {
            var fails = new List<string>();
            fails.AddRange(PersistBack("intensity → 3", () => new Light2DIntensityItem(), new Vector4(3f, 0f, 0f, 0f)));
            fails.AddRange(PersistBack("color → HDR", () => new Light2DColorItem(), new Vector4(2f, 0.1f, -0.5f, 1f)));
            fails.AddRange(PersistBack("radius → 0.2 (below the inner)", () => new Light2DRadiusItem(), new Vector4(0.2f, 0f, 0f, 0f)));
            fails.AddRange(PersistBack("radius → 0.2, a direct write", () => new Light2DRadiusItem(), new Vector4(0.2f, 0f, 0f, 0f), true));
            fails.AddRange(PersistBack("falloff → 2 (clamped)", () => new Light2DFalloffItem(), new Vector4(2f, 0f, 0f, 0f)));
            Assert.That(fails, Is.Empty);
        }

        [Test]
        public void LD03b_AMotionItDoesNotTake()
        {
            FeedbackScheduler s = Sched();
            Light2D l = Lamp();
            FeedbackPlayer p = Player(s, On(new Light2DRadiusItem(), l, new PunchMotion(), 1f));
            Assert.That(p.Runtime.Binding(0), Is.Null, "a punch on the radius is not prepared");
        }

        // ---- the stage: an offset contribution shows after LateApply, not after the Update stage

        [Test]
        public void LD02c_TheStage()
        {
            var fails = new List<string>();
            foreach (Func<Light2D, TargetItem> make in new Func<Light2D, TargetItem>[]
            {
                l => On(new Light2DIntensityItem(), l, Swing(1f), 1f),
                l => On(new Light2DColorItem(), l, FlashTo(new Vector4(2f, 0f, 0f, 1f)), 1f),
                l => On(new Light2DRadiusItem(), l, Swing(1f), 1f),
                l => On(new Light2DFalloffItem(), l, Swing(0.4f), 1f),
            })
            {
                FeedbackScheduler s = Sched();
                Light2D l = Lamp();
                TargetItem item = make(l);
                Player(s, item).Play();
                Tick(s, 3);
                string start = Bits(l);
                s.RunRevert();
                string reverted = Bits(l);
                s.RunUpdate(new FrameTime(Step, Step));
                string updated = Bits(l);
                s.RunLateApply();
                string applied = Bits(l);
                if (updated != reverted || applied == reverted)
                    fails.Add(item.GetType().Name + " — revert " + reverted + " · update " + updated + " · late apply " + applied + " (start " + start + ")");
            }
            Assert.That(fails, Is.Empty, "the four write in LateApply");
        }

        // ---- the rows: every serialized path a slot's write changes is in its rows — the inner radius the radius pushes too
        // (the derived row · PX02e's form)

        static Dictionary<string, string> Serialized(Object o)
        {
            var values = new Dictionary<string, string>();
            SerializedProperty it = new SerializedObject(o).GetIterator();
            while (it.Next(true))
            {
                switch (it.propertyType)
                {
                    case SerializedPropertyType.Float: values[it.propertyPath] = it.floatValue.ToString("R"); break;
                    case SerializedPropertyType.Integer: values[it.propertyPath] = it.intValue.ToString(); break;
                    case SerializedPropertyType.Boolean: values[it.propertyPath] = it.boolValue.ToString(); break;
                    case SerializedPropertyType.Color: values[it.propertyPath] = it.colorValue.ToString("R"); break;
                }
            }
            return values;
        }

        [Test]
        public void LD05b_TheRowsCoverTheWrites()
        {
            var fails = new List<string>();
            var s = Sched();
            foreach ((Type slot, Func<Light2D, LayerSlot> make, Action<LayerSlot> write) in new (Type, Func<Light2D, LayerSlot>, Action<LayerSlot>)[]
            {
                (typeof(Light2DIntensitySlot), l => s.Context.Layers.GetOrCreate(l, 0, 0, () => new Light2DIntensitySlot(l)), x => ((Light2DIntensitySlot)x).WriteValue(3f)),
                (typeof(Light2DColorSlot), l => s.Context.Layers.GetOrCreate(l, 0, 0, () => new Light2DColorSlot(l)), x => ((Light2DColorSlot)x).WriteValue(new Color(2f, 0.1f, -0.5f, 0.5f))),
                (typeof(Light2DRadiusSlot), l => s.Context.Layers.GetOrCreate(l, 0, 0, () => new Light2DRadiusSlot(l)), x => ((Light2DRadiusSlot)x).WriteValue(0.25f)),
                (typeof(Light2DFalloffSlot), l => s.Context.Layers.GetOrCreate(l, 0, 0, () => new Light2DFalloffSlot(l)), x => ((Light2DFalloffSlot)x).WriteValue(0.9f)),
            })
            {
                Light2D l = Lamp();
                Dictionary<string, string> before = Serialized(l);
                write(make(l));
                Dictionary<string, string> after = Serialized(l);
                string[] rows = slot.GetCustomAttribute<RecordPathsAttribute>().Paths;
                List<string> changed = after.Where(kv => !before.TryGetValue(kv.Key, out string v) || v != kv.Value).Select(kv => kv.Key).ToList();
                if (changed.Count == 0)
                    fails.Add(slot.Name + " changed nothing — the positive");
                foreach (string path in changed.Where(c => !rows.Any(r => r == c || r.StartsWith(c + ".", StringComparison.Ordinal) || c.StartsWith(r + ".", StringComparison.Ordinal))))
                    fails.Add(slot.Name + " wrote " + path + " outside its rows");
            }
            Assert.That(fails, Is.Empty);
        }

        // ---- the inner radius follows the outer down, opens again with it, and keeps a game's write

        [Test]
        public void LD04_TheRadiusPushesTheInner()
        {
            var fails = new List<string>();
            string start = B(2f) + "/" + B(0.5f);
            FeedbackScheduler s = Sched();
            Light2D l = Lamp();
            FeedbackPlayer down = Player(s, On(new Light2DRadiusItem(), l, TweenTo(new Vector4(0.25f, 0f, 0f, 0f)), 0.5f));
            FeedbackPlayer up = Player(s, On(new Light2DRadiusItem(), l, TweenTo(new Vector4(2f, 0f, 0f, 0f)), 0.5f));
            down.Play();
            Tick(s, 15);
            if (RadiusBits(l) != B(0.25f) + "/" + B(0.25f))
                fails.Add("down to 0.25 → " + RadiusValues(l));
            up.Play();
            Tick(s, 15);
            if (RadiusBits(l) != start)
                fails.Add("up to 2 → " + RadiusValues(l));
            down.Play();
            Tick(s, 5);
            l.pointLightInnerRadius = 0.125f;
            Tick(s, 10);
            if (RadiusBits(l) != B(0.25f) + "/" + B(0.125f))
                fails.Add("down with the game's inner 0.125 → " + RadiusValues(l));
            up.Play();
            Tick(s, 15);
            if (RadiusBits(l) != B(2f) + "/" + B(0.125f))
                fails.Add("up after the game's inner → " + RadiusValues(l));
            Assert.That(fails, Is.Empty);
        }

        // ---- the preview — a session plays the four and its stop gives every value back, bit for bit

        [Test]
        public void LD05_ThePreview()
        {
            Func<double> clock = PreviewClock.Source;
            IPreviewRecordSource source = PreviewRecordPaths.Source;
            Scene active = SceneManager.GetActiveScene();
            double now = 0;
            File.WriteAllText(Path.GetFullPath(ScenePath), "%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n");
            AssetDatabase.ImportAsset(ScenePath, ImportAssetOptions.ForceUpdate);
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            try
            {
                PreviewClock.Source = () => now;
                PreviewRecordPaths.Source = new PreviewRecordPathTable();
                SceneManager.SetActiveScene(scene);
                var go = new GameObject("Light2D");
                var light = go.AddComponent<Light2D>();
                light.pointLightOuterRadius = 2f;
                light.pointLightInnerRadius = 0.5f;
                var player = go.AddComponent<FeedbackPlayer>();
                foreach (TargetItem item in new TargetItem[] { new Light2DIntensityItem(), new Light2DColorItem(), new Light2DRadiusItem(), new Light2DFalloffItem() })
                {
                    item.m_Target.m_Mode = TargetRefMode.Self;
                    item.m_Timing.m_Duration = 1f;
                    player.m_Items.Add(item);
                }
                ((TargetItem)player.m_Items[0]).m_Motion = TweenTo(new Vector4(3f, 0f, 0f, 0f));
                ((TargetItem)player.m_Items[1]).m_Motion = TweenTo(new Vector4(2f, 0.1f, -0.5f, 1f));
                ((TargetItem)player.m_Items[2]).m_Motion = TweenTo(new Vector4(0.2f, 0f, 0f, 0f));
                ((TargetItem)player.m_Items[3]).m_Motion = TweenTo(new Vector4(0.9f, 0f, 0f, 0f));
                Assert.That(EditorSceneManager.SaveScene(scene), Is.True);
                string before = Bits(light);
                Assert.That(PreviewSession.Start(player, default), Is.EqualTo(PreviewRefusal.None));
                for (int i = 0; i < 60; i++)
                {
                    now += 1.0 / 120.0;
                    PreviewSession.Current?.Update();
                }
                string during = Bits(light);
                PreviewSession.Current.Stop();
                Assert.That(during, Is.Not.EqualTo(before), "the preview moved the light — the positive");
                Assert.That(Bits(light), Is.EqualTo(before), "the stop gives the four back");
                Assert.That(scene.isDirty, Is.False, "the scene stays clean");
            }
            finally
            {
                PreviewSession.Current?.Stop();
                PreviewClock.Source = clock;
                PreviewRecordPaths.Source = source;
                if (active.IsValid())
                    SceneManager.SetActiveScene(active);
                if (scene.IsValid())
                    EditorSceneManager.CloseScene(scene, true);
                AssetDatabase.DeleteAsset(ScenePath);
            }
        }

        // ---- FB-H06 — a radius on a light that is not a point light, a radius or a falloff on a global light

        [Test]
        public void LD06_TheRule()
        {
            var go = new GameObject("Player");
            m_Made.Add(go);
            var host = go.AddComponent<FeedbackPlayer>();
            Light2D point = Lamp(Light2D.LightType.Point), free = Lamp(Light2D.LightType.Freeform), global = Lamp(Light2D.LightType.Global);
            host.m_Items.AddRange(new FeedbackItem[]
            {
                On(new Light2DRadiusItem(), point, TweenTo(Vector4.one), 1f),          // 0
                On(new Light2DRadiusItem(), free, TweenTo(Vector4.one), 1f),           // 1 H06
                On(new Light2DRadiusItem(), global, TweenTo(Vector4.one), 1f),         // 2 H06
                On(new Light2DFalloffItem(), free, TweenTo(Vector4.one), 1f),          // 3
                On(new Light2DFalloffItem(), global, TweenTo(Vector4.one), 1f),        // 4 H06
                On(new Light2DIntensityItem(), global, TweenTo(Vector4.one), 1f),      // 5
            });
            List<RuleResult> got = FeedbackValidator.Run(host).Where(r => r.Id == "FB-H06").ToList();
            Assert.That(got.Select(r => r.Item).OrderBy(i => i), Is.EqualTo(new[] { 1, 2, 4 }));
            Assert.That(got.Select(r => r.Severity).Distinct(), Is.EqualTo(new[] { RuleSeverity.Info }));
        }

        // ---- the add menu's motions fitted to a 2D light

        [Test]
        public void LD07_TheDefaults()
        {
            var tween = (TweenMotion)MotionDefaults.Create(typeof(Light2DIntensityItem), MotionKind.Tween, out _);
            var radius = (TweenMotion)MotionDefaults.Create(typeof(Light2DRadiusItem), MotionKind.Tween, out _);
            var falloff = (OscillateMotion)MotionDefaults.Create(typeof(Light2DFalloffItem), MotionKind.Oscillate, out _);
            var flash = (FlashMotion)MotionDefaults.Create(typeof(Light2DColorItem), MotionKind.Flash, out _);
            var blink = (BlinkMotion)MotionDefaults.Create(typeof(Light2DIntensityItem), MotionKind.Blink, out _);
            Assert.That((tween.m_Params.m_To.x, radius.m_Params.m_To.x, falloff.m_Params.m_Amplitude.x, flash.m_Params.m_Target, blink.m_Params.m_Period),
                Is.EqualTo((2f, 3f, 0.25f, new Vector4(1f, 0f, 0f, 1f), 0.4f)));
            Assert.That(TargetChecks.Requires(typeof(Light2DIntensityItem)), Is.EqualTo(typeof(Light2D)), "the name resolves to the package's type");
        }

        // ---- the render: a 2D renderer made here — a radius tween below the inner keeps the light (the push)

        [Test]
        public void LD08_TheRender()
        {
            RenderPipelineAsset savedQuality = QualitySettings.renderPipeline;
            RenderPipelineAsset savedDefault = GraphicsSettings.defaultRenderPipeline;
            var made = new List<Object>();
            try
            {
                var data = ScriptableObject.CreateInstance<Renderer2DData>();
                made.Add(data);
                UniversalRenderPipelineAsset asset = UniversalRenderPipelineAsset.Create(data);
                made.Add(asset);
                QualitySettings.renderPipeline = asset;
                GraphicsSettings.defaultRenderPipeline = asset;
                var rt = new RenderTexture(64, 64, 24, RenderTextureFormat.ARGBFloat);
                made.Add(rt);
                var camGo = new GameObject("cam");
                made.Add(camGo);
                var cam = camGo.AddComponent<Camera>();
                cam.orthographic = true;
                cam.orthographicSize = 2f;
                cam.transform.position = new Vector3(0f, 0f, -10f);
                cam.targetTexture = rt;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = Color.black;
                var tex = new Texture2D(4, 4, TextureFormat.RGBA32, false);
                made.Add(tex);
                tex.SetPixels(Enumerable.Repeat(Color.white, 16).ToArray());
                tex.Apply();
                Sprite sprite = Sprite.Create(tex, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 1f);
                made.Add(sprite);
                var spriteGo = new GameObject("sprite");
                made.Add(spriteGo);
                var sr = spriteGo.AddComponent<SpriteRenderer>();
                sr.sprite = sprite;
                var mat = new Material(Shader.Find("Universal Render Pipeline/2D/Sprite-Lit-Default"));
                made.Add(mat);
                sr.sharedMaterial = mat;
                Light2D light = Lamp();
                light.pointLightOuterRadius = 1f;
                light.pointLightInnerRadius = 0.5f;
                var read = new Texture2D(64, 64, TextureFormat.RGBAFloat, false);
                made.Add(read);
                float Centre()
                {
                    typeof(Light2D).GetMethod("UpdateMesh", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(light, new object[] { true });
                    typeof(Light2D).GetMethod("UpdateBoundingSphere", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(light, null);
                    cam.Render();
                    RenderTexture.active = rt;
                    read.ReadPixels(new Rect(0, 0, 64, 64), 0, 0);
                    read.Apply();
                    RenderTexture.active = null;
                    return read.GetPixel(32, 32).r;
                }
                float start = Centre();
                FeedbackScheduler s = Sched();
                Player(s, On(new Light2DRadiusItem(), light, TweenTo(new Vector4(0.25f, 0f, 0f, 0f)), 0.5f)).Play();
                Tick(s, 15);
                float pushed = Centre();
                light.pointLightInnerRadius = 0.5f;
                float unpushed = Centre();
                Assert.That(start, Is.GreaterThan(0.5f), "the 2D renderer lights the sprite — the positive");
                Assert.That(pushed, Is.GreaterThan(0.5f), "a radius below the inner keeps the light (the inner follows)");
                Assert.That(unpushed, Is.LessThan(0.01f), "an inner past the outer puts the light out — the control");
            }
            finally
            {
                QualitySettings.renderPipeline = savedQuality;
                GraphicsSettings.defaultRenderPipeline = savedDefault;
                for (int i = made.Count - 1; i >= 0; i--)
                {
                    if (made[i] != null)
                        Object.DestroyImmediate(made[i]);
                }
            }
        }

        // ---- a preset saved from the player holds its own light as Self, and a player the preset is copied into moves its own
        // light · the copy's four round trip the play mode's record (it names a scene object by its global id — one of an
        // untitled scene has none, so the round trip is of Self targets)

        [Test]
        public void LD11_ThePresetAndTheKeep()
        {
            const string presetPath = "Assets/LD11Preset.asset";
            Light2D light = Lamp();
            FeedbackPlayer a = light.gameObject.AddComponent<FeedbackPlayer>();
            a.m_Items.AddRange(new FeedbackItem[]
            {
                On(new Light2DIntensityItem(), light, Swing(0.5f), 1f),
                On(new Light2DColorItem(), light, FlashTo(new Vector4(1f, 0f, 0f, 1f)), 1f),
                On(new Light2DRadiusItem(), light, TweenTo(new Vector4(0.25f, 0f, 0f, 0f)), 0.5f),
                On(new Light2DFalloffItem(), light, Swing(0.25f), 1f),
            });
            try
            {
                FeedbackPreset preset = PresetOps.SaveAs(a, presetPath, out int cleared);
                Assert.That(cleared, Is.Zero);
                Assert.That(SerializationUtility.HasManagedReferencesWithMissingTypes(preset), Is.False, "no missing type");
                Assert.That(preset.m_Items.Cast<TargetItem>().Select(i => (i.GetType(), i.m_Target.m_Mode)),
                    Is.EqualTo(a.m_Items.Select(i => (i.GetType(), TargetRefMode.Self))), "the four, Self");
                Light2D other = Lamp();
                FeedbackPlayer c = other.gameObject.AddComponent<FeedbackPlayer>();
                Assert.That(PresetOps.CopyInto(c, preset), Is.Not.Negative);
                FeedbackScheduler s = Sched();
                c.Bind(s);
                c.EnsureRuntime();
                m_Players.Add(c);
                string mine = Bits(light), theirs = Bits(other);
                c.Play();
                Tick(s, 5);
                Assert.That(Bits(other), Is.Not.EqualTo(theirs), "the copy moves its own light");
                Assert.That(Bits(light), Is.EqualTo(mine), "and not the preset's source");

                var into = new GameObject("LD11Into");
                m_Made.Add(into);
                FeedbackPlayer b = into.AddComponent<FeedbackPlayer>();
                PlayModeRecord.Apply(b, PlayModeRecord.Capture(c, false, out int unknown), out PlayModeRecord.Stats stats);
                Assert.That((unknown, stats.Dropped), Is.EqualTo((0, 0)));
                Assert.That(b.m_Items.Cast<TargetItem>().Select(i => (i.GetType(), i.m_Target.m_Mode)), Is.EqualTo(a.m_Items.Select(i => (i.GetType(), TargetRefMode.Self))), "the types");
                Assert.That((((OscillateMotion)((TargetItem)b.m_Items[0]).m_Motion).m_Params.m_Amplitude.x, ((FlashMotion)((TargetItem)b.m_Items[1]).m_Motion).m_Params.m_Target,
                    ((TweenMotion)((TargetItem)b.m_Items[2]).m_Motion).m_Params.m_To.x, ((TargetItem)b.m_Items[2]).m_Timing.m_Duration),
                    Is.EqualTo((0.5f, new Vector4(1f, 0f, 0f, 1f), 0.25f, 0.5f)), "the motions");
            }
            finally
            {
                AssetDatabase.DeleteAsset(presetPath);
            }
        }
    }
}
