using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using xpTURN.Feedback.Editor;
using Object = UnityEngine.Object;

namespace xpTURN.Feedback.Tests.Editor
{
    // the global render targets on a test scheduler ticked by hand (0.05 s), the preview on the test's clock (an update is
    // 1/120 s). The checks are bits, and the copies counted by their name's tail (a copy without our prefix is counted too). The
    // active scene's RenderSettings and the built-in skybox's values are put back after each test (a defect writing the original
    // changes the asset in memory for the run)
    public sealed class RenderGlobalTargetsTests
    {
        const float Step = 0.05f;
        const double UpdateStep = 1.0 / 120.0;

        static double s_Now;

        readonly List<Object> m_Made = new List<Object>();
        readonly List<FeedbackScheduler> m_Schedulers = new List<FeedbackScheduler>();
        readonly List<FeedbackPlayer> m_Players = new List<FeedbackPlayer>();
        readonly List<Scene> m_Scenes = new List<Scene>();
        readonly List<string> m_Assets = new List<string>();
        IPreviewRecordSource m_Source;
        Func<double> m_Clock;
        Scene m_Main;
        bool m_Fog;
        Color m_FogColor;
        float m_Density;
        FogMode m_FogMode;
        AmbientMode m_AmbientMode;
        Color m_Ambient;
        float m_Intensity;
        Material m_Sky;
        float m_SkyExposure;
        Color m_SkyTint;

        static Material BuiltinSky => AssetDatabase.GetBuiltinExtraResource<Material>("Default-Skybox.mat");

        [SetUp]
        public void SetUp()
        {
            m_Source = PreviewRecordPaths.Source;
            m_Clock = PreviewClock.Source;
            PreviewRecordPaths.Source = new PreviewRecordPathTable();
            s_Now = 0;
            PreviewClock.Source = () => s_Now;
            m_Main = SceneManager.GetActiveScene();
            m_Fog = RenderSettings.fog;
            m_FogColor = RenderSettings.fogColor;
            m_Density = RenderSettings.fogDensity;
            m_FogMode = RenderSettings.fogMode;
            m_AmbientMode = RenderSettings.ambientMode;
            m_Ambient = RenderSettings.ambientLight;
            m_Intensity = RenderSettings.ambientIntensity;
            m_Sky = RenderSettings.skybox;
            m_SkyExposure = BuiltinSky.GetFloat("_Exposure");
            m_SkyTint = BuiltinSky.GetColor("_SkyTint");
        }

        [TearDown]
        public void TearDown()
        {
            PreviewSession.Current?.Stop();
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
            PreviewRecovery.Run();
            PreviewRecordPaths.Source = m_Source;
            PreviewClock.Source = m_Clock;
            if (StageUtility.GetCurrentStage() is PrefabStage)
                StageUtility.GoToMainStage();
            if (m_Main.IsValid() && m_Main.isLoaded)
                SceneManager.SetActiveScene(m_Main);
            RenderSettings.fog = m_Fog;
            RenderSettings.fogColor = m_FogColor;
            RenderSettings.fogDensity = m_Density;
            RenderSettings.fogMode = m_FogMode;
            RenderSettings.ambientMode = m_AmbientMode;
            RenderSettings.ambientLight = m_Ambient;
            RenderSettings.ambientIntensity = m_Intensity;
            RenderSettings.skybox = m_Sky;
            BuiltinSky.SetFloat("_Exposure", m_SkyExposure);
            BuiltinSky.SetColor("_SkyTint", m_SkyTint);
            foreach (Object o in m_Made)
            {
                if (o != null)
                    Object.DestroyImmediate(o);
            }
            foreach (Scene s in m_Scenes)
            {
                if (s.IsValid() && s.isLoaded)
                    EditorSceneManager.CloseScene(s, true);
            }
            foreach (string a in m_Assets)
                AssetDatabase.DeleteAsset(a);
            foreach (RenderSettingsKind k in (RenderSettingsKind[])Enum.GetValues(typeof(RenderSettingsKind)))
                SessionState.EraseString(PreviewRenderSettings.Key(k));
            m_Made.Clear();
            m_Schedulers.Clear();
            m_Players.Clear();
            m_Scenes.Clear();
            m_Assets.Clear();
        }

        static string B(float f) => BitConverter.SingleToInt32Bits(f).ToString("X8");

        static string BC(Color c) => B(c.r) + B(c.g) + B(c.b) + B(c.a);

        static string BV(Vector4 v) => B(v.x) + B(v.y) + B(v.z) + B(v.w);

        static TweenMotion TweenTo(Vector4 to)
        {
            TweenParams p = TweenParams.Default;
            p.m_To = to;
            return new TweenMotion { m_Params = p };
        }

        static FlashMotion FlashTo(Vector4 target)
        {
            var flash = new FlashMotion();
            flash.m_Params.m_Target = target;
            return flash;
        }

        static SetMotion SetTo(float v) => new SetMotion { m_Params = new SetParams { m_Value = new Vector4(v, 0f, 0f, 0f) } };

        static PunchMotion PunchBy(float a) => new PunchMotion { m_Params = new PunchParams { m_Amplitude = new Vector4(a, a, a, 0f), m_Vibrato = 1f, m_Decay = 0f } };

        static OscillateMotion Oscillate(float a) => new OscillateMotion { m_Params = new OscillateParams { m_Amplitude = new Vector4(a, 0f, 0f, 0f), m_Frequency = 1f, m_Phase = 0.25f } };

        static T Global<T>(T item, FeedbackMotion motion, float duration, TargetRefMode mode = TargetRefMode.Direct) where T : TargetItem
        {
            item.m_Target = new TargetRef { m_Mode = mode, m_Path = "", m_Anchor = "" };
            item.m_Motion = motion;
            item.m_Timing.m_Duration = duration;
            return item;
        }

        GameObject Go(string name)
        {
            var go = new GameObject(name);
            m_Made.Add(go);
            return go;
        }

        FeedbackScheduler Sched(ContextKind kind = ContextKind.Test)
        {
            var s = new FeedbackScheduler(kind);
            m_Schedulers.Add(s);
            return s;
        }

        FeedbackPlayer Player(FeedbackScheduler s, params FeedbackItem[] items)
        {
            FeedbackPlayer p = Go("Player").AddComponent<FeedbackPlayer>();
            p.m_Items.AddRange(items);
            p.Bind(s);
            p.EnsureRuntime();
            m_Players.Add(p);
            return p;
        }

        static bool Bound(FeedbackPlayer p) => p.Runtime != null && p.Runtime.Binding(0) != null;

        // a keeper runs on, so a Revert stop · a Restore come while the play lives
        FeedbackItem Keeper()
        {
            var keeper = new PositionItem { m_Target = new TargetRef { m_Mode = TargetRefMode.Direct, m_Object = Go("Keeper").transform, m_Path = "", m_Anchor = "" }, m_Motion = PunchBy(0.5f) };
            keeper.m_Timing.m_Duration = 2f;
            return keeper;
        }

        static void Tick(FeedbackScheduler s, int n)
        {
            for (int i = 0; i < n; i++)
                s.Tick(Step);
        }

        static void Pump(int updates)
        {
            for (int i = 0; i < updates; i++)
            {
                s_Now += UpdateStep;
                PreviewSession.Current?.Update();
            }
        }

        Scene OpenScene(string name)
        {
            string path = "Assets/RenderGlobalTargetsTests." + name + ".unity";
            File.WriteAllText(Path.GetFullPath(path), "%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n");
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            Scene s = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
            m_Scenes.Add(s);
            m_Assets.Add(path);
            return s;
        }

        // a previewed player in the scene — a tween on itself keeps the session
        static FeedbackPlayer PreviewPlayer(Scene scene, params FeedbackItem[] items)
        {
            var player = new GameObject("RenderGlobalPlayer").AddComponent<FeedbackPlayer>();
            SceneManager.MoveGameObjectToScene(player.gameObject, scene);
            player.m_Items.AddRange(items);
            player.m_Items.Add(Global(new PositionItem(), TweenTo(new Vector4(0f, 3f, 0f, 0f)), 2f, TargetRefMode.Self));
            return player;
        }

        Material Panoramic()
        {
            var t = new Texture2D(2, 1, TextureFormat.RGBA32, false);
            t.SetPixel(0, 0, Color.red);
            t.SetPixel(1, 0, Color.green);
            t.Apply();
            m_Made.Add(t);
            var m = new Material(Shader.Find("Skybox/Panoramic")) { name = "RenderGlobalPanoramic" };
            m.SetTexture("_MainTex", t);
            m_Made.Add(m);
            return m;
        }

        static int HiddenCopies() => Resources.FindObjectsOfTypeAll<Material>().Count(m => m != null && m.hideFlags == HideFlags.HideAndDontSave && m.name.EndsWith("(Feedback)", StringComparison.Ordinal));

        static int Copies() => Resources.FindObjectsOfTypeAll<Material>().Count(m => m != null && m.name.EndsWith("(Feedback)", StringComparison.Ordinal));

        static bool AnyRecord() => ((RenderSettingsKind[])Enum.GetValues(typeof(RenderSettingsKind))).Any(k => SessionState.GetString(PreviewRenderSettings.Key(k), "").Length > 0);

        // ---- a reload whose hook was missed after an instance material's preview — the recovery (run while the session is
        // forgotten) leaves no hidden copy, the renderer has its original
        [Test]
        public void RQ03_MissedReloadLeavesNoHiddenInstanceCopy()
        {
            Scene a = OpenScene("rq03");
            SceneManager.SetActiveScene(a);
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            SceneManager.MoveGameObjectToScene(cube, a);
            var r = cube.GetComponent<Renderer>();
            Material original = r.sharedMaterial;
            var runtimeCopy = new Material(original) { name = original.name + " (Feedback)" };
            m_Made.Add(runtimeCopy);
            int hidden = HiddenCopies();
            var color = new MaterialColorItem { m_WriteMode = MaterialWriteMode.Instance, m_Motion = TweenTo(new Vector4(1f, 0f, 0f, 1f)) };
            color.m_Target = new TargetRef { m_Mode = TargetRefMode.Direct, m_Object = r, m_Path = "", m_Anchor = "" };
            color.m_Timing.m_Duration = 1f;
            FeedbackPlayer player = PreviewPlayer(a, color);
            Assert.That(EditorSceneManager.SaveScene(a), Is.True);
            Assert.That(PreviewSession.Start(player, default), Is.EqualTo(PreviewRefusal.None));
            Pump(30);
            Assert.That(HiddenCopies() - hidden, Is.EqualTo(1), "the preview's hidden copy");
            PreviewRecovery.Run();
            Assert.That(HiddenCopies() - hidden, Is.Zero, "no hidden copy after the recovery");
            Assert.That(r.sharedMaterial, Is.SameAs(original), "the renderer's original");
            Assert.That(runtimeCopy != null, Is.True, "a copy without our prefix stays");
        }

        // ---- no target — empty Direct · Self · MainCamera (no main camera: not waited for) · Channel write the one fog slot
        [Test]
        public void RQ04a_EveryTargetModeWritesTheOneGlobal()
        {
            var untagged = new List<GameObject>();
            foreach (Camera c in Resources.FindObjectsOfTypeAll<Camera>())
            {
                if (c != null && c.gameObject.scene.IsValid() && c.CompareTag("MainCamera"))
                {
                    c.gameObject.tag = "Untagged";
                    untagged.Add(c.gameObject);
                }
            }
            try
            {
                Assert.That(Camera.main, Is.Null, "no main camera");
                RenderSettings.fogColor = Color.red;
                FeedbackScheduler s = Sched();
                int before = s.Context.Layers.SlotCount;
                var modes = new[] { TargetRefMode.Direct, TargetRefMode.Self, TargetRefMode.MainCamera, TargetRefMode.Channel };
                List<FeedbackPlayer> players = modes.Select(m => Player(s, Global(new FogColorItem(), FlashTo(new Vector4(0f, 1f, 0f, 1f)), 0.5f, m))).ToList();
                Assert.That(players.Select(Bound), Is.All.True, "every mode prepared");
                Assert.That(s.Context.Layers.SlotCount - before, Is.EqualTo(1), "one slot");
                foreach (FeedbackPlayer p in players)
                    p.Play();
                Tick(s, 3);
                Assert.That(BC(RenderSettings.fogColor), Is.Not.EqualTo(BC(Color.red)), "written");
                Tick(s, 15);
                s.Tick(0f);
                Assert.That(BC(RenderSettings.fogColor), Is.EqualTo(BC(Color.red)), "the end's bits");
                FeedbackScheduler alone = Sched();
                Player(alone, Global(new FogColorItem(), FlashTo(new Vector4(0f, 1f, 0f, 1f)), 0.5f, TargetRefMode.MainCamera)).Play();
                Tick(alone, 3);
                Assert.That(BC(RenderSettings.fogColor), Is.Not.EqualTo(BC(Color.red)), "a main camera target with no camera writes alone");
            }
            finally
            {
                foreach (GameObject go in untagged)
                {
                    if (go != null)
                        go.tag = "MainCamera";
                }
            }
        }

        // a shader global's slot by its name and form — A · B · A · A as a color are three
        [Test]
        public void RQ04b_ShaderGlobalSlotsByNameAndForm()
        {
            FeedbackScheduler s = Sched();
            int before = s.Context.Layers.SlotCount;
            Player(s, Global(new ShaderGlobalFloatItem { m_PropertyName = "_RQ04A" }, TweenTo(Vector4.one), 0.1f), Global(new ShaderGlobalFloatItem { m_PropertyName = "_RQ04B" }, TweenTo(Vector4.one), 0.1f),
                Global(new ShaderGlobalFloatItem { m_PropertyName = "_RQ04A" }, TweenTo(Vector4.one), 0.1f), Global(new ShaderGlobalColorItem { m_PropertyName = "_RQ04A" }, TweenTo(Vector4.one), 0.1f));
            Assert.That(s.Context.Layers.SlotCount - before, Is.EqualTo(3));
        }

        // ---- a shader global — a Persist's Revert · Restore bits · a Set · an empty name refused
        [Test]
        public void RQ05a_ShaderGlobalPersistAndSet()
        {
            const string N = "_RQ05G";
            int id = Shader.PropertyToID(N);
            foreach (bool midway in new[] { true, false })
            {
                Shader.SetGlobalFloat(id, 0.25f);
                FeedbackScheduler s = Sched();
                FeedbackPlayer p = Player(s, Global(new ShaderGlobalFloatItem { m_PropertyName = N }, TweenTo(new Vector4(5f, 0f, 0f, 0f)), 1f), Keeper());
                p.Play();
                Tick(s, midway ? 8 : 30);
                if (midway)
                    p.Stop(StopMode.Revert);
                else
                    p.Restore();
                s.Tick(0f);
                Assert.That(B(Shader.GetGlobalFloat(id)), Is.EqualTo(B(0.25f)), midway ? "the Revert's bits" : "the Restore's bits");
            }
            FeedbackScheduler set = Sched();
            Player(set, Global(new ShaderGlobalFloatItem { m_PropertyName = N }, SetTo(3f), 0f)).Play();
            Tick(set, 2);
            Assert.That(Shader.GetGlobalFloat(id), Is.EqualTo(3f), "the Set");
            LogAssert.ignoreFailingMessages = true;
            Assert.That(Bound(Player(Sched(), Global(new ShaderGlobalFloatItem(), TweenTo(Vector4.one), 0.1f))), Is.False, "an empty name refused");
        }

        // the close puts the first value back — a release keeps the Persist (bound · released first — the close's ledger · two
        // slots of one property, the earliest value) · the color and the vector too
        [Test]
        public void RQ05b_ShaderGlobalCloseFirstValue()
        {
            const string N = "_RQ05C";
            int id = Shader.PropertyToID(N);
            Shader.SetGlobalFloat(id, 0.25f);
            FeedbackScheduler bound = Sched();
            Player(bound, Global(new ShaderGlobalFloatItem { m_PropertyName = N }, SetTo(3f), 0f)).Play();
            Tick(bound, 2);
            bound.Shutdown();
            Assert.That(B(Shader.GetGlobalFloat(id)), Is.EqualTo(B(0.25f)), "closed while bound");

            FeedbackScheduler released = Sched();
            FeedbackPlayer p = Player(released, Global(new ShaderGlobalFloatItem { m_PropertyName = N }, SetTo(3f), 0f));
            p.Play();
            Tick(released, 2);
            p.ReleaseRuntime();
            released.Tick(0f);
            Assert.That(Shader.GetGlobalFloat(id), Is.EqualTo(3f), "a release keeps the Persist");
            released.Shutdown();
            Assert.That(B(Shader.GetGlobalFloat(id)), Is.EqualTo(B(0.25f)), "released, then closed");

            FeedbackScheduler two = Sched();
            foreach (float v in new[] { 3f, 5f })
            {
                FeedbackPlayer q = Player(two, Global(new ShaderGlobalFloatItem { m_PropertyName = N }, SetTo(v), 0f));
                q.Play();
                Tick(two, 2);
                q.ReleaseRuntime();
                two.Tick(0f);
            }
            Assert.That(Shader.GetGlobalFloat(id), Is.EqualTo(5f));
            two.Shutdown();
            Assert.That(B(Shader.GetGlobalFloat(id)), Is.EqualTo(B(0.25f)), "two slots of one property — the earliest value");

            int cid = Shader.PropertyToID("_RQ05Color");
            int vid = Shader.PropertyToID("_RQ05Vector");
            var c0 = new Vector4(0.2f, 0.4f, 0.6f, 1f);
            var v0 = new Vector4(1f, 2f, 3f, 4f);
            Shader.SetGlobalVector(cid, c0);
            Shader.SetGlobalVector(vid, v0);
            FeedbackScheduler forms = Sched();
            FeedbackPlayer f = Player(forms, Global(new ShaderGlobalColorItem { m_PropertyName = "_RQ05Color" }, TweenTo(new Vector4(1f, 0f, 0f, 1f)), 0.1f),
                Global(new ShaderGlobalVectorItem { m_PropertyName = "_RQ05Vector" }, TweenTo(new Vector4(9f, 9f, 9f, 9f)), 0.1f));
            f.Play();
            Tick(forms, 4);
            f.ReleaseRuntime();
            forms.Tick(0f);
            Assert.That(BV(Shader.GetGlobalVector(vid)), Is.EqualTo(BV(new Vector4(9f, 9f, 9f, 9f))), "the vector's Persist stays at a release");
            forms.Shutdown();
            Assert.That((BV(Shader.GetGlobalVector(cid)), BV(Shader.GetGlobalVector(vid))), Is.EqualTo((BV(c0), BV(v0))), "the color and the vector — released, then closed");
        }

        // the color is the picker's (linear in a Linear project) and its revert · close write the raw bits it took
        [Test]
        public void RQ05c_ShaderGlobalColorRawBits()
        {
            int cid = Shader.PropertyToID("_RQ05Raw");
            var c0 = new Vector4(0.2f, 0.4f, 0.6f, 1f);
            Shader.SetGlobalVector(cid, c0);
            FeedbackScheduler s = Sched();
            FeedbackPlayer p = Player(s, Global(new ShaderGlobalColorItem { m_PropertyName = "_RQ05Raw" }, TweenTo(new Vector4(0.5f, 0.25f, 0.75f, 1f)), 0.2f), Keeper());
            p.Play();
            Tick(s, 10);
            bool linear = QualitySettings.activeColorSpace == ColorSpace.Linear;
            Assert.That(B(Shader.GetGlobalVector(cid).x), Is.EqualTo(B(linear ? Mathf.GammaToLinearSpace(0.5f) : 0.5f)), "the color written as the picker's");
            p.Stop(StopMode.Revert);
            s.Tick(0f);
            Assert.That(BV(Shader.GetGlobalVector(cid)), Is.EqualTo(BV(c0)), "the Revert's raw bits");
            p.Play();
            Tick(s, 10);
            s.Shutdown();
            Assert.That(BV(Shader.GetGlobalVector(cid)), Is.EqualTo(BV(c0)), "the close's raw bits");
            // an offset's end puts the raw value it took back (the slot's restore — not the base written again)
            FeedbackScheduler o = Sched();
            var curve = new CurveMotion { m_Params = CurveParams.Default };
            curve.m_Params.m_RemapOne = new Vector4(0.3f, 0.3f, 0.3f, 0f);
            Player(o, Global(new ShaderGlobalColorItem { m_PropertyName = "_RQ05Raw" }, curve, 0.2f)).Play();
            Tick(o, 2);
            Assert.That(BV(Shader.GetGlobalVector(cid)), Is.Not.EqualTo(BV(c0)), "the offset wrote");
            Tick(o, 8);
            o.Tick(0f);
            Assert.That(BV(Shader.GetGlobalVector(cid)), Is.EqualTo(BV(c0)), "an offset's end — the raw bits");
        }

        // ---- fog — a Tween's Revert bits (density · color) · a flash's end bits · no density below 0 · the switch and the mode stay
        [Test]
        public void RQ06_Fog()
        {
            RenderSettings.fog = false;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = Color.red;
            RenderSettings.fogDensity = 0.01f;
            FeedbackScheduler s = Sched();
            FeedbackPlayer p = Player(s, Global(new FogDensityItem(), TweenTo(new Vector4(0.2f, 0f, 0f, 0f)), 1f), Global(new FogColorItem(), TweenTo(new Vector4(0f, 0f, 1f, 1f)), 1f), Keeper());
            p.Play();
            Tick(s, 8);
            Assert.That(RenderSettings.fogDensity, Is.GreaterThan(0.01f));
            p.Stop(StopMode.Revert);
            s.Tick(0f);
            Assert.That((B(RenderSettings.fogDensity), BC(RenderSettings.fogColor)), Is.EqualTo((B(0.01f), BC(Color.red))), "the Revert's bits");
            FeedbackScheduler f = Sched();
            Player(f, Global(new FogColorItem(), FlashTo(new Vector4(0f, 1f, 0f, 1f)), 0.4f)).Play();
            Tick(f, 12);
            f.Tick(0f);
            Assert.That(BC(RenderSettings.fogColor), Is.EqualTo(BC(Color.red)), "the flash's end bits");
            FeedbackScheduler g = Sched();
            Player(g, Global(new FogDensityItem(), TweenTo(new Vector4(-1f, 0f, 0f, 0f)), 0.1f)).Play();
            Tick(g, 4);
            Assert.That(RenderSettings.fogDensity, Is.Zero, "never below 0");
            Assert.That((RenderSettings.fog, RenderSettings.fogMode), Is.EqualTo((false, FogMode.ExponentialSquared)), "the switch and the mode");
        }

        // ---- ambient light — Flat's color: the Revert's bits and the probe follows and comes back · the intensity's guard and bits
        [Test]
        public void RQ07_AmbientLight()
        {
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.3f, 0.3f, 0.3f, 1f);
            RenderSettings.ambientIntensity = 1f;
            SphericalHarmonicsL2 sh0 = RenderSettings.ambientProbe;
            FeedbackScheduler s = Sched();
            FeedbackPlayer p = Player(s, Global(new AmbientColorItem(), TweenTo(new Vector4(1f, 0f, 0f, 1f)), 1f), Keeper());
            p.Play();
            Tick(s, 8);
            Assert.That(RenderSettings.ambientProbe == sh0, Is.False, "the probe follows");
            p.Stop(StopMode.Revert);
            s.Tick(0f);
            Assert.That(B(RenderSettings.ambientLight.r), Is.EqualTo(B(0.3f)), "the Revert's bits");
            Assert.That(RenderSettings.ambientProbe == sh0, Is.True, "the probe back");
            FeedbackScheduler g = Sched();
            Player(g, Global(new AmbientIntensityItem(), TweenTo(new Vector4(-2f, 0f, 0f, 0f)), 0.1f)).Play();
            Tick(g, 4);
            Assert.That(RenderSettings.ambientIntensity, Is.Zero, "never below 0");
            RenderSettings.ambientIntensity = 1f;
            FeedbackScheduler r = Sched();
            FeedbackPlayer q = Player(r, Global(new AmbientIntensityItem(), TweenTo(new Vector4(3f, 0f, 0f, 0f)), 1f), Keeper());
            q.Play();
            Tick(r, 8);
            q.Stop(StopMode.Revert);
            r.Tick(0f);
            Assert.That(B(RenderSettings.ambientIntensity), Is.EqualTo(B(1f)), "the intensity's Revert bits");
        }

        // ---- the skybox — a copy is the scene's skybox (the original's bits stay) · a release puts the original back with no
        // copy left · a skybox the game set stays · a close while awake · Procedural's tint is _SkyTint and it has no rotation
        [Test]
        public void RQ08a_SkyboxCopy()
        {
            Material sky = BuiltinSky;
            RenderSettings.skybox = sky;
            float e0 = sky.GetFloat("_Exposure");
            Color t0 = sky.GetColor("_SkyTint");
            int copies = Copies();
            FeedbackScheduler s = Sched();
            FeedbackPlayer p = Player(s, Global(new SkyboxExposureItem(), TweenTo(new Vector4(3f, 0f, 0f, 0f)), 0.2f), Global(new SkyboxTintItem(), TweenTo(new Vector4(1f, 0f, 0f, 1f)), 0.2f));
            p.Play();
            Tick(s, 6);
            Material copy = RenderSettings.skybox;
            Assert.That(copy != sky && copy != null, Is.True, "a copy in");
            Assert.That((copy.GetFloat("_Exposure"), BC(copy.GetColor("_SkyTint"))), Is.EqualTo((3f, BC(new Color(1f, 0f, 0f, 1f)))), "the copy's values — the tint is _SkyTint");
            Assert.That((B(sky.GetFloat("_Exposure")), BC(sky.GetColor("_SkyTint"))), Is.EqualTo((B(e0), BC(t0))), "the original's bits");
            p.ReleaseRuntime();
            s.Tick(0f);
            Assert.That((RenderSettings.skybox == sky, Copies() - copies), Is.EqualTo((true, 0)), "a release — the original, no copy");

            FeedbackScheduler g = Sched();
            FeedbackPlayer q = Player(g, Global(new SkyboxExposureItem(), TweenTo(new Vector4(3f, 0f, 0f, 0f)), 0.2f));
            q.Play();
            Tick(g, 3);
            Material game = Panoramic();
            RenderSettings.skybox = game;
            Tick(g, 3);
            q.ReleaseRuntime();
            g.Tick(0f);
            Assert.That((RenderSettings.skybox == game, Copies() - copies), Is.EqualTo((true, 0)), "the game's skybox stays");

            RenderSettings.skybox = sky;
            FeedbackScheduler c = Sched();
            Player(c, Global(new SkyboxExposureItem(), TweenTo(new Vector4(3f, 0f, 0f, 0f)), 0.2f)).Play();
            Tick(c, 3);
            c.Shutdown();
            Assert.That((RenderSettings.skybox == sky, Copies() - copies), Is.EqualTo((true, 0)), "a close while awake");
        }

        // the rotation of a Panoramic copy · refused: Procedural's rotation · no skybox
        [Test]
        public void RQ08b_SkyboxRotationAndRefusals()
        {
            LogAssert.ignoreFailingMessages = true;
            RenderSettings.skybox = BuiltinSky;
            Assert.That(Bound(Player(Sched(), Global(new SkyboxRotationItem(), TweenTo(new Vector4(90f, 0f, 0f, 0f)), 0.2f))), Is.False, "Procedural has no _Rotation");
            Material pano = Panoramic();
            RenderSettings.skybox = pano;
            FeedbackScheduler s = Sched();
            Player(s, Global(new SkyboxRotationItem(), TweenTo(new Vector4(180f, 0f, 0f, 0f)), 0.2f)).Play();
            Tick(s, 6);
            Assert.That((RenderSettings.skybox.GetFloat("_Rotation"), pano.GetFloat("_Rotation")), Is.EqualTo((180f, 0f)), "the copy turned · the original not");
            RenderSettings.skybox = null;
            Assert.That(Bound(Player(Sched(), Global(new SkyboxExposureItem(), TweenTo(Vector4.one), 0.2f))), Is.False, "no skybox");
        }

        // ---- the active scene at run time — a flash in A (red · density 0.01), B (blue · 0.02) made active midway, the end in B:
        // B's bits stay during and after, back in A its original (the asleep writes' books). A new play with B active takes B's slot,
        // A's Persist stays
        [Test]
        public void RQ09a_ActiveSceneAtRunTime()
        {
            Scene a = OpenScene("rq09a");
            Scene b = OpenScene("rq09b");
            SceneManager.SetActiveScene(b);
            RenderSettings.fogColor = Color.blue;
            RenderSettings.fogDensity = 0.02f;
            SceneManager.SetActiveScene(a);
            RenderSettings.fogColor = Color.red;
            RenderSettings.fogDensity = 0.01f;
            FeedbackScheduler s = Sched();
            Player(s, Global(new FogColorItem(), FlashTo(new Vector4(0f, 1f, 0f, 1f)), 0.5f), Global(new FogDensityItem(), FlashTo(new Vector4(0.1f, 0f, 0f, 0f)), 0.5f)).Play();
            Tick(s, 3);
            SceneManager.SetActiveScene(b);
            Tick(s, 3);
            Assert.That((BC(RenderSettings.fogColor), B(RenderSettings.fogDensity)), Is.EqualTo((BC(Color.blue), B(0.02f))), "B during");
            Tick(s, 15);
            s.Tick(0f);
            Assert.That((BC(RenderSettings.fogColor), B(RenderSettings.fogDensity)), Is.EqualTo((BC(Color.blue), B(0.02f))), "B after");
            SceneManager.SetActiveScene(a);
            s.Tick(0f);
            Tick(s, 2);
            Assert.That((BC(RenderSettings.fogColor), B(RenderSettings.fogDensity)), Is.EqualTo((BC(Color.red), B(0.01f))), "A back — its original");
            var yellow = new Color(1f, 1f, 0f, 1f);
            FeedbackPlayer q = Player(s, Global(new FogColorItem(), TweenTo(new Vector4(1f, 1f, 0f, 1f)), 0.1f));
            q.Play();
            Tick(s, 4);
            SceneManager.SetActiveScene(b);
            q.Play();
            Tick(s, 4);
            Assert.That(BC(RenderSettings.fogColor), Is.EqualTo(BC(yellow)), "a new play with B active — B's slot");
            SceneManager.SetActiveScene(a);
            Assert.That(BC(RenderSettings.fogColor), Is.EqualTo(BC(yellow)), "A's Persist stays");
        }

        // the skybox copy across scenes — an oscillation in A, the end with B active: B's skybox untouched, back in A the copy's
        // exposure is the original's (the copy is A's own — written asleep too)
        [Test]
        public void RQ09b_SkyboxAcrossScenes()
        {
            Scene a = OpenScene("rq09sa");
            Scene b = OpenScene("rq09sb");
            Material sky = BuiltinSky;
            SceneManager.SetActiveScene(b);
            RenderSettings.skybox = sky;
            SceneManager.SetActiveScene(a);
            RenderSettings.skybox = sky;
            float e0 = sky.GetFloat("_Exposure");
            FeedbackScheduler s = Sched();
            Player(s, Global(new SkyboxExposureItem(), Oscillate(2f), 0.5f)).Play();
            Tick(s, 3);
            Material copy = RenderSettings.skybox;
            SceneManager.SetActiveScene(b);
            Tick(s, 3);
            Assert.That(RenderSettings.skybox, Is.SameAs(sky), "B's skybox");
            Tick(s, 15);
            s.Tick(0f);
            SceneManager.SetActiveScene(a);
            s.Tick(0f);
            Tick(s, 2);
            Assert.That(RenderSettings.skybox, Is.SameAs(copy), "A's copy");
            Assert.That(B(copy.GetFloat("_Exposure")), Is.EqualTo(B(e0)), "the copy's exposure back");
        }

        // ---- the preview: a session that wrote RenderSettings ends when the active scene changes — B's · A's bits, A's skybox
        // the original, no hidden copy left (the close destroys a copy asleep)
        [Test]
        public void RQ09c_PreviewEndsWhenTheActiveSceneChanges()
        {
            Scene a = OpenScene("rq09pa");
            Scene b = OpenScene("rq09pb");
            SceneManager.SetActiveScene(b);
            RenderSettings.fogColor = Color.blue;
            SceneManager.SetActiveScene(a);
            RenderSettings.fogColor = Color.red;
            Material sky = BuiltinSky;
            RenderSettings.skybox = sky;
            int hidden = HiddenCopies();
            FeedbackPlayer player = PreviewPlayer(a, Global(new FogColorItem(), TweenTo(new Vector4(0f, 1f, 0f, 1f)), 1f), Global(new SkyboxExposureItem(), TweenTo(new Vector4(3f, 0f, 0f, 0f)), 1f));
            Assert.That(EditorSceneManager.SaveScene(a), Is.True);
            Assert.That(PreviewSession.Start(player, default), Is.EqualTo(PreviewRefusal.None));
            Pump(30);
            SceneManager.SetActiveScene(b);
            Assert.That((PreviewSession.Current == null, PreviewSession.LastEnd), Is.EqualTo((true, PreviewEndReason.ActiveSceneChanged)), "ended");
            Assert.That(BC(RenderSettings.fogColor), Is.EqualTo(BC(Color.blue)), "B's bits");
            SceneManager.SetActiveScene(a);
            Assert.That(BC(RenderSettings.fogColor), Is.EqualTo(BC(Color.red)), "A's bits");
            Assert.That((RenderSettings.skybox == sky, HiddenCopies() - hidden), Is.EqualTo((true, 0)), "A's skybox · no hidden copy");
        }

        // a session that wrote a shader global only goes on — the global is not the scene's
        [Test]
        public void RQ09d_AShaderGlobalSessionGoesOn()
        {
            Scene a = OpenScene("rq09ga");
            Scene b = OpenScene("rq09gb");
            SceneManager.SetActiveScene(a);
            FeedbackPlayer player = PreviewPlayer(a, Global(new ShaderGlobalFloatItem { m_PropertyName = "_RQ09Global" }, TweenTo(new Vector4(3f, 0f, 0f, 0f)), 1f));
            Assert.That(EditorSceneManager.SaveScene(a), Is.True);
            Assert.That(PreviewSession.Start(player, default), Is.EqualTo(PreviewRefusal.None));
            Pump(10);
            SceneManager.SetActiveScene(b);
            Assert.That(PreviewSession.Current, Is.Not.Null);
        }

        // a prefab stage's preview writes the main scene (the active one) and its end puts it back
        [Test]
        public void RQ09e_PrefabStagePreviewWritesTheMainScene()
        {
            Scene a = OpenScene("rq09e");
            SceneManager.SetActiveScene(a);
            RenderSettings.fogColor = Color.red;
            Assert.That(EditorSceneManager.SaveScene(a), Is.True);
            const string prefabPath = "Assets/RenderGlobalTargetsTests.rq09e.prefab";
            var root = new GameObject("RQ09e");
            root.AddComponent<FeedbackPlayer>().m_Items.Add(Global(new FogColorItem(), TweenTo(new Vector4(1f, 0f, 1f, 1f)), 1f));
            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            Object.DestroyImmediate(root);
            m_Assets.Add(prefabPath);
            PrefabStage stage = PrefabStageUtility.OpenPrefab(prefabPath);
            Assert.That(PreviewSession.Start(stage.prefabContentsRoot.GetComponent<FeedbackPlayer>(), default), Is.EqualTo(PreviewRefusal.None));
            Pump(30);
            Assert.That(SceneManager.GetActiveScene(), Is.EqualTo(a), "the main scene is active");
            Assert.That(BC(RenderSettings.fogColor), Is.Not.EqualTo(BC(Color.red)), "the main scene's fog written");
            Assert.That(PreviewSaveState.Count > 0 && PreviewSaveState.At(0).Scene == a, Is.True, "the state's scene");
            PreviewSession.Current.Stop();
            Assert.That(BC(RenderSettings.fogColor), Is.EqualTo(BC(Color.red)), "the end puts it back");
        }

        // ---- a save during the preview writes the values before (the five kinds — the skybox's reference, not {fileID: 0}),
        // the preview's after it, the originals at the end, the scene clean
        [Test]
        public void RQ10_SaveDuringThePreview()
        {
            Scene a = OpenScene("rq10");
            SceneManager.SetActiveScene(a);
            RenderSettings.fogColor = Color.red;
            RenderSettings.fogDensity = 0.01f;
            RenderSettings.ambientLight = new Color(0.3f, 0.3f, 0.3f, 1f);
            RenderSettings.ambientIntensity = 1f;
            Material sky = BuiltinSky;
            RenderSettings.skybox = sky;
            FeedbackPlayer player = PreviewPlayer(a, Global(new FogColorItem(), TweenTo(new Vector4(0f, 1f, 0f, 1f)), 1f), Global(new FogDensityItem(), TweenTo(new Vector4(0.2f, 0f, 0f, 0f)), 1f),
                Global(new AmbientColorItem(), TweenTo(new Vector4(1f, 0f, 0f, 1f)), 1f), Global(new AmbientIntensityItem(), TweenTo(new Vector4(3f, 0f, 0f, 0f)), 1f),
                Global(new SkyboxExposureItem(), TweenTo(new Vector4(3f, 0f, 0f, 0f)), 1f));
            Assert.That(EditorSceneManager.SaveScene(a), Is.True);
            string before = File.ReadAllText(a.path);
            Assert.That(PreviewSession.Start(player, default), Is.EqualTo(PreviewRefusal.None));
            Pump(30);
            Color during = RenderSettings.fogColor;
            Assert.That(EditorSceneManager.SaveScene(a), Is.True);
            Assert.That(File.ReadAllText(a.path), Is.EqualTo(before), "the file is the values before");
            Assert.That((BC(RenderSettings.fogColor), RenderSettings.skybox != sky), Is.EqualTo((BC(during), true)), "the preview's after the save");
            PreviewSession.Current.Stop();
            Assert.That((BC(RenderSettings.fogColor), B(RenderSettings.fogDensity), B(RenderSettings.ambientLight.r), B(RenderSettings.ambientIntensity), RenderSettings.skybox == sky),
                Is.EqualTo((BC(Color.red), B(0.01f), B(0.3f), B(1f), true)), "the end");
            Assert.That(a.isDirty, Is.False, "clean");
        }

        // ---- a reload whose hook was missed (the recovery while the session is forgotten — the true reload is in
        // PreviewReloadTests): SessionState's values put the five back, no hidden copy, no record left · a normal end erases the
        // records · a record of a scene not loaded waits
        [Test]
        public void RQ11a_MissedReloadPutsRenderSettingsBack()
        {
            Scene a = OpenScene("rq11");
            SceneManager.SetActiveScene(a);
            RenderSettings.fogColor = Color.red;
            RenderSettings.fogDensity = 0.01f;
            RenderSettings.ambientLight = new Color(0.3f, 0.3f, 0.3f, 1f);
            RenderSettings.ambientIntensity = 1f;
            Material sky = BuiltinSky;
            RenderSettings.skybox = sky;
            int hidden = HiddenCopies();
            FeedbackPlayer player = PreviewPlayer(a, Global(new FogColorItem(), TweenTo(new Vector4(0f, 1f, 0f, 1f)), 1f), Global(new FogDensityItem(), TweenTo(new Vector4(0.2f, 0f, 0f, 0f)), 1f),
                Global(new AmbientColorItem(), TweenTo(new Vector4(1f, 0f, 0f, 1f)), 1f), Global(new AmbientIntensityItem(), TweenTo(new Vector4(3f, 0f, 0f, 0f)), 1f),
                Global(new SkyboxExposureItem(), TweenTo(new Vector4(3f, 0f, 0f, 0f)), 1f));
            Assert.That(EditorSceneManager.SaveScene(a), Is.True);
            Assert.That(PreviewSession.Start(player, default), Is.EqualTo(PreviewRefusal.None));
            Pump(30);
            Assert.That(AnyRecord(), Is.True, "written down");
            PreviewRecovery.Run();
            Assert.That((BC(RenderSettings.fogColor), B(RenderSettings.fogDensity), B(RenderSettings.ambientLight.r), B(RenderSettings.ambientIntensity), RenderSettings.skybox == sky),
                Is.EqualTo((BC(Color.red), B(0.01f), B(0.3f), B(1f), true)), "put back");
            Assert.That((HiddenCopies() - hidden, AnyRecord()), Is.EqualTo((0, false)), "no hidden copy · no record");
            try
            {
                PreviewSession.Current?.Stop();
            }
            catch (Exception)
            {
                // the forgotten session's driver is gone
            }
            Assert.That(PreviewSession.Start(PreviewPlayer(a, Global(new FogColorItem(), TweenTo(new Vector4(0f, 1f, 0f, 1f)), 1f)), default), Is.EqualTo(PreviewRefusal.None));
            Pump(10);
            Assert.That(AnyRecord(), Is.True);
            PreviewSession.Current.Stop();
            Assert.That(AnyRecord(), Is.False, "a normal end erases the records");
            string key = PreviewRenderSettings.Key(RenderSettingsKind.FogColor);
            SessionState.SetString(key, "Assets/RenderGlobalTargetsTests.notLoaded.unity\n1;0;0;1");
            PreviewRecovery.Run();
            Assert.That(SessionState.GetString(key, ""), Is.Not.Empty, "a record of a scene not loaded waits");
        }

        // ---- the record source knows the global slot types (no rows: not recorded) · a session makes one state for each kind · a
        // start and stop's round trip (the fog · the ambient light · the skybox · a shader global)
        [Test]
        public void RQ13a_TheSourceKnowsTheSlotTypes()
        {
            foreach (Type t in new[] { typeof(ShaderGlobalFloatSlot), typeof(ShaderGlobalColorSlot), typeof(ShaderGlobalVectorSlot), typeof(FogColorSlot), typeof(FogDensitySlot),
                typeof(AmbientColorSlot), typeof(AmbientIntensitySlot), typeof(SkyboxFloatSlot), typeof(SkyboxColorSlot), typeof(SkyboxCopySlot) })
                Assert.That(PreviewRecordPaths.Source.Knows(t), Is.True, t.Name);
        }

        [Test]
        public void RQ13b_OneStateForEachKind()
        {
            Scene a = OpenScene("rq13b");
            SceneManager.SetActiveScene(a);
            RenderSettings.skybox = BuiltinSky;
            Assert.That(EditorSceneManager.SaveScene(a), Is.True);
            var made = new List<RenderSettingsKind>();
            foreach (FeedbackItem item in new FeedbackItem[]
            {
                Global(new FogColorItem(), TweenTo(new Vector4(0f, 1f, 0f, 1f)), 1f), Global(new FogDensityItem(), TweenTo(new Vector4(0.2f, 0f, 0f, 0f)), 1f),
                Global(new AmbientColorItem(), TweenTo(new Vector4(1f, 0f, 0f, 1f)), 1f), Global(new AmbientIntensityItem(), TweenTo(new Vector4(3f, 0f, 0f, 0f)), 1f),
                Global(new SkyboxExposureItem(), TweenTo(new Vector4(3f, 0f, 0f, 0f)), 1f),
            })
            {
                FeedbackPlayer one = PreviewPlayer(a, item);
                Assert.That(PreviewSession.Start(one, default), Is.EqualTo(PreviewRefusal.None));
                Pump(10);
                for (int i = 0; i < PreviewSaveState.Count; i++)
                {
                    if (PreviewSaveState.At(i) is RenderSettingsState s)
                        made.Add(s.Kind);
                }
                PreviewSession.Current.Stop();
                Object.DestroyImmediate(one.gameObject);
            }
            Assert.That(made, Is.EqualTo(new[] { RenderSettingsKind.FogColor, RenderSettingsKind.FogDensity, RenderSettingsKind.AmbientColor, RenderSettingsKind.AmbientIntensity, RenderSettingsKind.Skybox }));
        }

        [Test]
        public void RQ13c_RoundTrip()
        {
            Scene a = OpenScene("rq13c");
            SceneManager.SetActiveScene(a);
            RenderSettings.fogColor = Color.red;
            RenderSettings.ambientLight = new Color(0.3f, 0.3f, 0.3f, 1f);
            RenderSettings.ambientIntensity = 1f;
            Material sky = BuiltinSky;
            RenderSettings.skybox = sky;
            int gid = Shader.PropertyToID("_RQ13Global");
            Shader.SetGlobalFloat(gid, 0.25f);
            FeedbackPlayer player = PreviewPlayer(a, Global(new FogColorItem(), TweenTo(new Vector4(0f, 1f, 0f, 1f)), 1f), Global(new AmbientColorItem(), TweenTo(new Vector4(1f, 0f, 0f, 1f)), 1f),
                Global(new AmbientIntensityItem(), TweenTo(new Vector4(3f, 0f, 0f, 0f)), 1f), Global(new SkyboxExposureItem(), TweenTo(new Vector4(3f, 0f, 0f, 0f)), 1f),
                Global(new ShaderGlobalFloatItem { m_PropertyName = "_RQ13Global" }, TweenTo(new Vector4(5f, 0f, 0f, 0f)), 1f));
            Assert.That(EditorSceneManager.SaveScene(a), Is.True);
            Assert.That(PreviewSession.Start(player, default), Is.EqualTo(PreviewRefusal.None));
            Pump(60);
            Assert.That(Shader.GetGlobalFloat(gid), Is.Not.EqualTo(0.25f));
            PreviewSession.Current.Stop();
            Assert.That((BC(RenderSettings.fogColor), B(RenderSettings.ambientLight.r), B(RenderSettings.ambientIntensity), RenderSettings.skybox == sky, B(Shader.GetGlobalFloat(gid))),
                Is.EqualTo((BC(Color.red), B(0.3f), B(1f), true, B(0.25f))));
        }

        // the debugger's Target and Slot cells of a global slot (no target) — a destroyed target stays '(destroyed)'
        [Test]
        public void RQ13d_TheDebuggerNamesAGlobalSlot()
        {
            FeedbackScheduler s = Sched();
            Player(s, Global(new ShaderGlobalFloatItem { m_PropertyName = "_RQ13Debugger" }, TweenTo(Vector4.one), 0.1f));
            Assert.That(s.Context.Layers.TryGetSlot(0, out LayerSlotInfo global) && global.SlotType == typeof(ShaderGlobalFloatSlot), Is.True);
            Assert.That((DebuggerTable.TargetText(in global), DebuggerTable.SlotText(in global)), Is.EqualTo(("(global)", "ShaderGlobalFloatSlot")));
        }

        // ---- the inspector — a global item's target and direct-write fields are not drawn, its header says Global, an added
        // item keeps its empty Direct, the add menu's defaults
        [Test]
        public void RQ17a_TheTargetAndDirectWriteAreHidden()
        {
            foreach (FeedbackItem item in new FeedbackItem[] { new FogColorItem(), new ShaderGlobalColorItem(), new SkyboxRotationItem(), new AmbientIntensityItem() })
            {
                Assert.That((ItemRules.Visible(item, "m_Target"), ItemRules.Visible(item, "m_DirectWrite")), Is.EqualTo((false, false)), item.GetType().Name);
                Assert.That(ItemWords.Summary(item), Does.EndWith(" · Global"));
            }
            Assert.That((ItemRules.Visible(new LightColorItem(), "m_Target"), ItemRules.Visible(new LightColorItem(), "m_DirectWrite")), Is.EqualTo((true, true)), "a target item");
            CatalogLeaf leaf = FeedbackCatalog.All.First(l => l.Type == typeof(FogColorItem) && l.Motion == (int)MotionKind.Tween);
            var added = (TargetItem)ItemFactory.Create(in leaf);
            Assert.That(added.m_Target.m_Mode, Is.EqualTo(TargetRefMode.Direct), "not made Self");
        }

        [Test]
        public void RQ17b_TheAddMenusDefaults()
        {
            float To(Type t) => ((TweenMotion)MotionDefaults.Create(t, MotionKind.Tween, out _)).m_Params.m_To.x;
            Assert.That((To(typeof(FogDensityItem)), To(typeof(SkyboxExposureItem)), To(typeof(SkyboxRotationItem)), To(typeof(AmbientIntensityItem))),
                Is.EqualTo((MotionDefaults.FogDensityTo, MotionDefaults.ExposureTo, MotionDefaults.SkyRotationTo, MotionDefaults.AmbientIntensityTo)));
            foreach (Type t in new[] { typeof(FogColorItem), typeof(SkyboxTintItem), typeof(AmbientColorItem) })
                Assert.That(((TweenMotion)MotionDefaults.Create(t, MotionKind.Tween, out _)).m_Params.m_To, Is.EqualTo(MotionDefaults.RenderColorTo), t.Name);
            Assert.That(((FlashMotion)MotionDefaults.Create(typeof(FogColorItem), MotionKind.Flash, out _)).m_Params.m_Target, Is.EqualTo(MotionDefaults.RenderColorTo));
            Assert.That(((FlashMotion)MotionDefaults.Create(typeof(FogDensityItem), MotionKind.Flash, out _)).m_Params.m_Target.x, Is.EqualTo(MotionDefaults.FogDensityFlash));
            Assert.That(((OscillateMotion)MotionDefaults.Create(typeof(SkyboxExposureItem), MotionKind.Oscillate, out _)).m_Params.m_Amplitude.x, Is.EqualTo(MotionDefaults.ExposureSize));
            Assert.That(((OscillateMotion)MotionDefaults.Create(typeof(SkyboxRotationItem), MotionKind.Oscillate, out _)).m_Params.m_Amplitude.x, Is.EqualTo(MotionDefaults.SkyRotationSize));
            Assert.That(((TweenMotion)MotionDefaults.Create(typeof(ShaderGlobalFloatItem), MotionKind.Tween, out _)).m_Params.m_To, Is.EqualTo(Vector4.one), "a shader global keeps the motion's own");
        }

        // ---- the play's end — a Runtime context's close puts the shader global's first value back (bound · released first)
        [Test]
        public void RQ18_TheRuntimeContextsClose()
        {
            const string N = "_RQ18Global";
            int id = Shader.PropertyToID(N);
            foreach (bool release in new[] { false, true })
            {
                Shader.SetGlobalFloat(id, 0.25f);
                FeedbackScheduler s = Sched(ContextKind.Runtime);
                FeedbackPlayer p = Player(s, Global(new ShaderGlobalFloatItem { m_PropertyName = N }, TweenTo(new Vector4(7f, 0f, 0f, 0f)), 0.2f));
                p.Play();
                Tick(s, 6);
                Assert.That(Shader.GetGlobalFloat(id), Is.EqualTo(7f));
                if (release)
                {
                    p.ReleaseRuntime();
                    s.Tick(0f);
                }
                s.Shutdown();
                Assert.That(B(Shader.GetGlobalFloat(id)), Is.EqualTo(B(0.25f)), release ? "released, then closed" : "closed while bound");
            }
        }
    }
}
