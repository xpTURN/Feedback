using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;
using UIs = UnityEngine.TestTools.Constraints.Is;
using xpTURN.Feedback.Tests.Extension;

namespace xpTURN.Feedback.Tests
{
    // the first cycles over the registry's combos, the composite lists, the double swap, the deserialized player's tick, the
    // linker's regression, the linked players and an observed cycle. Allocation is the first-run rule — the first run of five
    // fresh fixtures, red only when all five allocate, a positive control 5/5 — and a count of the warnings (every log but
    // Log): a development build's warning allocates and a skipped item is a hollow green. A red row carries the warnings' text
    [PrebuildSetup("xpTURN.Feedback.Tests.Editor.FeedbackTestAssets")]
    [PostBuildCleanup("xpTURN.Feedback.Tests.Editor.FeedbackTestAssets")]
    public sealed class PlayerGateTests
    {
        sealed class Fixture
        {
            public FeedbackScheduler Scheduler;
            public FeedbackPlayer Player;
        }

        sealed class Listener : IPlaybackListener
        {
            public int Ends;

            public void OnPlaybackEnded(PlaybackHandle handle, PlaybackEnd end) => Ends++;
        }

        // judged in an IL2CPP player only — a static field, not a const (a const false makes 6000.6 warn CS0162)
#if ENABLE_IL2CPP && !UNITY_EDITOR
        static readonly bool k_Judge = true;
#else
        static readonly bool k_Judge = false;
#endif

        static Fixture s_Fixture;
        static FeedbackPlayer s_Other;
        static int s_Heard;
        static int s_Logs;
        static readonly List<string> s_LogTexts = new List<string>();
        static readonly Listener s_Listener = new Listener();
        static Material s_Material;
        Material m_Sky;
        readonly List<Object> m_Made = new List<Object>();
        readonly List<FeedbackScheduler> m_Schedulers = new List<FeedbackScheduler>();
        readonly List<FeedbackPlayer> m_Players = new List<FeedbackPlayer>();
        readonly List<string> m_Fail = new List<string>();

        static void OnLog(string condition, string stack, LogType type)
        {
            if (type == LogType.Log)
                return;
            s_Logs++;
            if (s_LogTexts.Count < 8)
                s_LogTexts.Add(condition);
        }

        [SetUp]
        public void SetUp()
        {
            if (s_Material == null)
                s_Material = Resources.Load<Material>("FeedbackTestMaterial");
            m_Sky = PlayerSkybox.Keep();
            s_Logs = 0;
            s_LogTexts.Clear();
            Application.logMessageReceived += OnLog;
        }

        [TearDown]
        public void TearDown()
        {
            Application.logMessageReceived -= OnLog;
            foreach (FeedbackPlayer p in m_Players)
            {
                if (p != null)
                    p.ReleaseRuntime();
            }
            foreach (Object o in m_Made)
            {
                if (o != null)
                    Object.Destroy(o);
            }
            foreach (FeedbackScheduler s in m_Schedulers)
            {
                if (!s.IsShutdown)
                    s.Shutdown();
            }
            m_Players.Clear();
            m_Made.Clear();
            m_Schedulers.Clear();
            m_Fail.Clear();
            RenderSettings.skybox = m_Sky;
            // a Time Scale row writes the engine's scale — each fixture's context took the one before's as its base
            Time.timeScale = 1f;
            s_Fixture = null;
            s_Other = null;
        }

        void Check(bool ok, string what)
        {
            if (!ok)
                m_Fail.Add(what);
        }

        // the warnings counted — the expected number — then the row's verdict
        void End(int warnings = 0)
        {
            if (s_Logs != warnings)
                m_Fail.Add("warnings " + s_Logs + " (want " + warnings + ") — " + string.Join(" / ", s_LogTexts.Distinct()));
            Assert.That(m_Fail, Is.Empty);
        }

        static string Where => Application.isEditor ? "editor" : "player";

        static void Heard(FeedbackPlayer p, PlaybackHandle h) => s_Heard++;

        static void Tick(int n)
        {
            for (int i = 0; i < n; i++)
                s_Fixture.Scheduler.Tick(0.05f);
        }

        static TargetRef Self => new TargetRef { m_Mode = TargetRefMode.Self, m_Path = "", m_Anchor = "" };

        static TargetRef Grand() => new TargetRef { m_Mode = TargetRefMode.Child, m_ChildLookup = ChildLookup.Path, m_Path = "c/g", m_Anchor = "" };

        // the cube's second material has a Vector property (Sprites/Default _Flip — Standard and URP Lit have none); the swap's
        // material is a third. The material items name the properties both pipelines' defaults have, a write mode by the variant
        static Material s_Second;
        static Material s_SwapIn;

        static void Materials(GameObject leaf)
        {
            if (s_Second == null)
                s_Second = new Material(Shader.Find("Sprites/Default")) { name = "G1-second" };
            if (s_SwapIn == null)
                s_SwapIn = new Material(Shader.Find("Sprites/Default")) { name = "G1-swap" };
            var r = leaf.GetComponent<Renderer>();
            r.sharedMaterials = new[] { r.sharedMaterial, s_Second };
        }

        // the leaf's kind — a renderer is one a GameObject
        internal enum Leaf
        {
            LightCamera,
            Cube,
            Sprite,
            Line,
            Trail,
            Module,     // an optional module's leaf (ModuleFixtures)
            Custom,     // the user target (FeedbackTestTarget)
            Rect,       // a RectTransform
        }

        static Sprite s_SpriteA;
        static Sprite s_SpriteB;

        static Sprite MakeSprite(string name, Color color)
        {
            var t = new Texture2D(4, 4, TextureFormat.RGBA32, false) { name = name };
            var px = new Color[16];
            for (int i = 0; i < px.Length; i++)
                px[i] = color;
            t.SetPixels(px);
            t.Apply();
            Sprite s = Sprite.Create(t, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 4f);
            s.name = name;
            return s;
        }

        static GameObject MakeLeaf(Leaf kind, Type item)
        {
            switch (kind)
            {
                case Leaf.Sprite:
                {
                    if (s_SpriteA == null)
                        s_SpriteA = MakeSprite("G1-spriteA", Color.white);
                    if (s_SpriteB == null)
                        s_SpriteB = MakeSprite("G1-spriteB", Color.gray);
                    var go = new GameObject();
                    go.AddComponent<SpriteRenderer>().sprite = s_SpriteA;
                    return go;
                }
                case Leaf.Line:
                {
                    var go = new GameObject();
                    var line = go.AddComponent<LineRenderer>();
                    line.positionCount = 2;
                    line.SetPosition(1, Vector3.right);
                    return go;
                }
                case Leaf.Trail:
                {
                    var go = new GameObject();
                    go.AddComponent<TrailRenderer>();
                    return go;
                }
                case Leaf.Cube:
                    return GameObject.CreatePrimitive(PrimitiveType.Cube);
                case Leaf.Custom:
                {
                    var go = new GameObject();
                    go.AddComponent<FeedbackTestTarget>();
                    return go;
                }
                case Leaf.Rect:
                    return new GameObject("G1-rect", typeof(RectTransform));
                case Leaf.Module:
                {
                    var go = new GameObject();
                    ModuleFixtures.Leaf(go, item);
                    return go;
                }
                default:
                    return new GameObject();
            }
        }

        // the leaf's light · a template made once (a pool of 8 — the cycles' plays take a few) · a layer
        static GameObject s_SpawnTemplate;

        static void GameObjectFields(TargetItem item)
        {
            switch (item)
            {
                case ComponentEnabledItem e:
                    e.m_ComponentType = "UnityEngine.Light";
                    break;
                case SpawnItem s:
                    if (s_SpawnTemplate == null)
                        s_SpawnTemplate = new GameObject("G1-spawn");
                    s.m_Prefab = s_SpawnTemplate;
                    s.m_PoolSize = 8;
                    break;
                case SetLayerItem l:
                    l.m_Layer = 4;
                    break;
            }
        }

        static void SpriteFields(TargetItem item, int variant)
        {
            switch (item)
            {
                case SpriteSwapItem w: w.m_Sprite = s_SpriteB; break;
                case SpriteSheetItem h: h.m_Frames = new List<Sprite> { s_SpriteA, s_SpriteB }; break;
                case SpriteFlipItem f: f.m_Axis = variant == 1 ? SpriteAxis.Y : SpriteAxis.X; break;
                case LineColorItem c: c.m_End = variant == 1 ? LineEnd.End : LineEnd.Start; break;
            }
        }

        static void MaterialFields(TargetItem item, int variant)
        {
            MaterialWriteMode mode = variant == 1 ? MaterialWriteMode.Instance : MaterialWriteMode.PropertyBlock;
            switch (item)
            {
                case MaterialFloatItem f:
                    f.m_PropertyName = "_Cutoff";
                    f.m_MaterialIndex = 0;
                    f.m_WriteMode = mode;
                    break;
                case MaterialVectorItem v:
                    v.m_PropertyName = "_Flip";
                    v.m_MaterialIndex = 1;
                    v.m_WriteMode = mode;
                    break;
                case TextureOffsetItem o:
                    o.m_MaterialIndex = 0;
                    o.m_WriteMode = mode;
                    break;
                case TextureScaleItem c:
                    c.m_MaterialIndex = 0;
                    c.m_WriteMode = mode;
                    break;
                case EmissionItem e:
                    e.m_MaterialIndex = 0;
                    e.m_WriteMode = mode;
                    break;
                case MaterialSwapItem w:
                    w.m_Material = s_SwapIn;
                    break;
                // the shader globals' own names · the skybox is Procedural (Default-Skybox or the test skybox) — no _Rotation: a
                // Range of it
                case ShaderGlobalFloatItem g:
                    g.m_PropertyName = "_FeedbackG1GlobalF";
                    break;
                case ShaderGlobalColorItem g:
                    g.m_PropertyName = "_FeedbackG1GlobalC";
                    break;
                case ShaderGlobalVectorItem g:
                    g.m_PropertyName = "_FeedbackG1GlobalV";
                    break;
                case SkyboxRotationItem r:
                    r.m_PropertyName = "_AtmosphereThickness";
                    break;
            }
        }

        // the second references are the owner (Self) — the leaf c/g is under it, no cycle. The variant read by Match (Track) and
        // Set Parent (the local values stay)
        static void SecondRefs(TargetItem item)
        {
            switch (item)
            {
                case LookAtItem look: look.m_LookTarget = Self; break;
                case RotateAroundItem around: around.m_Pivot = Self; break;
                case MatchTransformItem match: match.m_Destination = Self; match.m_Track = true; break;
                case SetParentItem set: set.m_NewParent = Self; set.m_WorldPositionStays = false; break;
            }
        }

        FeedbackPlayer Bound(GameObject owner, FeedbackScheduler s, params FeedbackItem[] items) => Bound(owner, s, null, items);

        FeedbackPlayer Bound(GameObject owner, FeedbackScheduler s, FeedbackPreset preset, params FeedbackItem[] items)
        {
            var player = owner.AddComponent<FeedbackPlayer>();
            m_Players.Add(player);
            player.m_Preset = preset;
            player.m_Items.AddRange(items);
            player.Bind(s);
            player.Played += Heard;
            player.Completed += Heard;
            player.Stopped += Heard;
            player.EnsureRuntime();
            return player;
        }

        FeedbackScheduler NewScheduler()
        {
            var s = new FeedbackScheduler(ContextKind.Test);
            m_Schedulers.Add(s);
            return s;
        }

        // a fresh copy of a template — its grandchild has no C# Transform yet; the cube has the prepared material. A preset: the
        // player links it instead of an item of its own (GP14_Linked)
        Fixture Fresh(Func<FeedbackItem> make, Leaf kind, FeedbackPreset preset = null, Type itemType = null)
        {
            FeedbackScheduler s = NewScheduler();
            var template = new GameObject("template");
            m_Made.Add(template);
            var mid = new GameObject("c");
            mid.transform.SetParent(template.transform, false);
            mid.transform.localRotation = Quaternion.Euler(0f, 0f, 30f);
            bool cube = kind == Leaf.Cube;
            GameObject leaf = MakeLeaf(kind, itemType);
            if (cube && s_Material != null)
                leaf.GetComponent<Renderer>().sharedMaterial = s_Material;
            if (cube)
                Materials(leaf);
            leaf.name = "g";
            leaf.transform.SetParent(mid.transform, false);
            if (kind == Leaf.LightCamera)
            {
                leaf.AddComponent<Light>();
                leaf.AddComponent<Camera>();
            }
            GameObject owner = Object.Instantiate(template);
            m_Made.Add(owner);
            return new Fixture { Scheduler = s, Player = preset != null ? Bound(owner, s, preset) : Bound(owner, s, make()) };
        }

        // the first run of five fresh fixtures — hits of five (a warm-up on another fixture first)
        static int FirstRuns(Func<Fixture> fresh, TestDelegate run)
        {
            s_Fixture = fresh();
            run();
            int hits = 0;
            for (int i = 0; i < 5; i++)
            {
                s_Fixture = fresh();
                if (UIs.AllocatingGCMemory().ApplyTo(run).IsSuccess)
                    hits++;
            }
            s_Fixture = null;
            return hits;
        }

        void Positive()
        {
            int hits = 0;
            for (int i = 0; i < 5; i++)
            {
                if (UIs.AllocatingGCMemory().ApplyTo(new TestDelegate(() => s_Heard += new int[4].Length)).IsSuccess)
                    hits++;
            }
            Check(hits == 5, "the positive control " + hits + "/5");
        }

        static readonly Type[] s_Motions = typeof(FeedbackMotion).Assembly.GetTypes().Where(t => t.IsSubclassOf(typeof(FeedbackMotion)) && !t.IsAbstract).ToArray();

        // the fixture table: the target item types of the package — a type not in it is red, not skipped
        static readonly Dictionary<Type, Leaf> s_FixtureOf = new Dictionary<Type, Leaf>
        {
            { typeof(PositionItem), Leaf.LightCamera }, { typeof(RotationItem), Leaf.LightCamera }, { typeof(ScaleItem), Leaf.LightCamera },
            { typeof(LightIntensityItem), Leaf.LightCamera }, { typeof(SetActiveItem), Leaf.LightCamera }, { typeof(MaterialColorItem), Leaf.Cube },
            { typeof(CameraShakeItem), Leaf.LightCamera },
            // the leaf carries a light and a camera
            { typeof(LightRangeItem), Leaf.LightCamera }, { typeof(LightColorItem), Leaf.LightCamera }, { typeof(LightSpotAngleItem), Leaf.LightCamera }, { typeof(LightShadowStrengthItem), Leaf.LightCamera },
            { typeof(CameraFieldOfViewItem), Leaf.LightCamera }, { typeof(CameraOrthographicSizeItem), Leaf.LightCamera }, { typeof(CameraZoomItem), Leaf.LightCamera },
            { typeof(CameraClippingPlanesItem), Leaf.LightCamera }, { typeof(CameraBackgroundColorItem), Leaf.LightCamera },
            // the transform items
            { typeof(SquashAndStretchItem), Leaf.LightCamera }, { typeof(LookAtItem), Leaf.LightCamera }, { typeof(RotateAroundItem), Leaf.LightCamera },
            { typeof(MatchTransformItem), Leaf.LightCamera }, { typeof(SetParentItem), Leaf.LightCamera },
            // the material items
            { typeof(MaterialFloatItem), Leaf.Cube }, { typeof(MaterialVectorItem), Leaf.Cube }, { typeof(TextureOffsetItem), Leaf.Cube }, { typeof(TextureScaleItem), Leaf.Cube },
            { typeof(EmissionItem), Leaf.Cube }, { typeof(MaterialSwapItem), Leaf.Cube }, { typeof(RendererVisibilityItem), Leaf.Cube },
            // the render globals: no target — the leaf does not matter
            { typeof(ShaderGlobalFloatItem), Leaf.LightCamera }, { typeof(ShaderGlobalColorItem), Leaf.LightCamera }, { typeof(ShaderGlobalVectorItem), Leaf.LightCamera },
            { typeof(FogColorItem), Leaf.LightCamera }, { typeof(FogDensityItem), Leaf.LightCamera }, { typeof(SkyboxExposureItem), Leaf.LightCamera }, { typeof(SkyboxTintItem), Leaf.LightCamera },
            { typeof(SkyboxRotationItem), Leaf.LightCamera }, { typeof(AmbientColorItem), Leaf.LightCamera }, { typeof(AmbientIntensityItem), Leaf.LightCamera },
            // a sprite · a line · a trail leaf
            { typeof(SpriteColorItem), Leaf.Sprite }, { typeof(SpriteAlphaItem), Leaf.Sprite }, { typeof(SpriteSwapItem), Leaf.Sprite },
            { typeof(SpriteFlipItem), Leaf.Sprite }, { typeof(SpriteSheetItem), Leaf.Sprite },
            { typeof(LineWidthItem), Leaf.Line }, { typeof(LineColorItem), Leaf.Line },
            { typeof(TrailTimeItem), Leaf.Trail }, { typeof(TrailWidthItem), Leaf.Trail }, { typeof(TrailEmittingItem), Leaf.Trail }, { typeof(TrailClearItem), Leaf.Trail },
            // the leaf's light turned · a spawn's template at the leaf · the leaf despawned (destroyed — a play) · its layer
            { typeof(ComponentEnabledItem), Leaf.LightCamera }, { typeof(SpawnItem), Leaf.LightCamera }, { typeof(DespawnItem), Leaf.LightCamera },
            { typeof(SetLayerItem), Leaf.LightCamera },
            // the time scale and the float event have no target · the custom values the user target leaf
            { typeof(TimeScaleItem), Leaf.LightCamera }, { typeof(CustomFloatEventItem), Leaf.LightCamera },
            { typeof(CustomFloatItem), Leaf.Custom }, { typeof(CustomVector3Item), Leaf.Custom }, { typeof(CustomColorItem), Leaf.Custom },
            // a RectTransform leaf
            { typeof(RectSizeDeltaItem), Leaf.Rect }, { typeof(RectPivotItem), Leaf.Rect }, { typeof(RectAnchorsItem), Leaf.Rect }, { typeof(RectOffsetsItem), Leaf.Rect },
        };

        static bool OfThePackage(Type t)
        {
            string assembly = t.Assembly.GetName().Name;
            return assembly.StartsWith("xpTURN.Feedback", StringComparison.Ordinal) && !assembly.StartsWith("xpTURN.Feedback.Tests", StringComparison.Ordinal);
        }

        List<(string, Func<FeedbackItem>, Leaf, Type)> Combos()
        {
            var list = new List<(string, Func<FeedbackItem>, Leaf, Type)>();
            foreach (Type itemType in FeedbackImplementations.RegisteredTypes.Where(t => t.IsSubclassOf(typeof(TargetItem)) && OfThePackage(t)).OrderBy(t => t.Name))
            {
                if (!s_FixtureOf.TryGetValue(itemType, out Leaf cube))
                {
                    if (!ModuleFixtures.Has(itemType))
                    {
                        m_Fail.Add("no fixture for " + itemType.Name);
                        continue;
                    }
                    cube = Leaf.Module;
                }
                if (ModuleFixtures.SkipInEditor(itemType))
                    continue;
                var probe = (TargetItem)Activator.CreateInstance(itemType);
                foreach (Type motionType in s_Motions)
                {
                    var motion = (FeedbackMotion)Activator.CreateInstance(motionType);
                    if (!probe.Traits.Supports(motion.Kind))
                        continue;
                    Type it = itemType, mt = motionType;
                    list.Add((it.Name + " " + mt.Name, () =>
                    {
                        var item = (TargetItem)Activator.CreateInstance(it);
                        item.m_Motion = (FeedbackMotion)Activator.CreateInstance(mt);
                        item.m_Target = Grand();
                        item.m_Timing.m_Duration = item.m_Motion is SetMotion ? 0f : 0.3f;
                        SecondRefs(item);
                        MaterialFields(item, 1);
                        SpriteFields(item, 1);
                        GameObjectFields(item);
                        ModuleFixtures.Fields(item);
                        return item;
                    }, cube, it));
                }
            }
            return list;
        }

        // ---- the cycles
        static readonly TestDelegate s_SeekCycle = () =>
        {
            Fixture f = s_Fixture;
            f.Player.Play();
            Tick(3);
            f.Player.Pause();
            f.Player.Seek(0.1f);
            f.Player.Resume();
            Tick(1);
            f.Player.Skip();
            Tick(1);
            f.Player.Play();
            Tick(2);
            f.Player.Seek(0.05f);
            Tick(1);
            f.Player.Stop(StopMode.Revert);
            Tick(1);
            f.Player.Play(PlayArgs.Default.WithDirection(PlayerDirection.Backward));
            Tick(2);
            f.Player.Stop(StopMode.Hold);
            Tick(1);
        };

        static readonly TestDelegate s_ListenerCycle = () =>
        {
            Fixture f = s_Fixture;
            f.Player.Play(PlayArgs.Default.WithListener(s_Listener));
            Tick(3);
            f.Player.Skip();
            PlaybackHandle h = f.Player.Play();
            f.Player.SetListener(h, s_Listener);
            Tick(2);
            f.Player.Stop(StopMode.Complete);
            Tick(1);
        };

        // ---- the registry's combos × the cycles (0 seek · skip · backward · 1 listener) — every combo prepared, none
        // allocating, the listener heard at least once a run (the ends differ by combo)
        [UnityTest]
        public IEnumerator GP09_Cycles([Values(0, 1)] int cycle)
        {
            yield return null;
            TestDelegate run = cycle == 0 ? s_SeekCycle : s_ListenerCycle;
            var red = new List<string>();
            int combos = 0, unprepared = 0;
            int ends = s_Listener.Ends;
            foreach ((string name, Func<FeedbackItem> make, Leaf cube, Type itemType) in Combos())
            {
                combos++;
                Fixture probe = Fresh(make, cube, null, itemType);
                if (probe.Player.Runtime == null || probe.Player.Runtime.Binding(0) == null)
                    unprepared++;
                if (FirstRuns(() => Fresh(make, cube, null, itemType), run) == 5)
                    red.Add(name);
            }
            Check(combos > 0 && unprepared == 0, "prepared — " + unprepared + " of " + combos + " not");
            Check(red.Count == 0, "allocating " + string.Join(" | ", red));
            if (cycle == 1)
                Check(s_Listener.Ends - ends >= combos * 6, "the listener heard the ends — " + (s_Listener.Ends - ends) + " for " + combos + " combos");
            Positive();
            Debug.Log("GATE " + Where + " cycle " + cycle + " combos=" + combos + " red=" + red.Count + " ends=" + (s_Listener.Ends - ends));
            End();
        }

        // ---- the composite lists — no path allocates; the signalled play ends, the infinite block plays on until stopped, a
        // signal inside a tick ends the waiting player
        Fixture Composite()
        {
            FeedbackScheduler s = NewScheduler();
            var owner = new GameObject("composite");
            m_Made.Add(owner);
            var punch = new PositionItem { m_Motion = new PunchMotion(), m_Target = Self };
            punch.m_Timing.m_Duration = 0.2f;
            var shake = new PositionItem { m_Motion = new ShakeMotion(), m_Target = Self };
            shake.m_Timing.m_Duration = 0.1f;
            shake.m_Timing.m_Clock = ItemClock.Unscaled;
            var background = new PositionItem { m_Motion = new OscillateMotion(), m_Target = Self };
            background.m_Timing.m_Duration = 0.5f;
            background.m_Timing.m_Background = true;
            var hitStop = new HitStopItem { m_FreezeDuration = 0.05f };
            var wait = new WaitItem();
            wait.m_Timing.m_Duration = 0.1f;
            var signal = new WaitForSignalItem { m_Timeout = 1f };
            var loopStart = new LoopStartItem();
            var loopEnd = new LoopEndItem { m_LoopCount = 2 };
            var hold = new HoldItem();
            var evt = new UnityEventItem();
            foreach (FeedbackItem f in new FeedbackItem[] { loopStart, shake, loopEnd, wait, signal, hitStop, hold, evt })
                f.m_Timing.m_Rule = StartRule.AfterPrevious;
            return new Fixture { Scheduler = s, Player = Bound(owner, s, background, punch, loopStart, shake, loopEnd, wait, signal, hitStop, hold, evt) };
        }

        static void SignalOther(FeedbackPlayer p, PlaybackHandle h) => s_Other.Signal();

        Fixture ZeroPeriod(int kind)
        {
            FeedbackScheduler s = NewScheduler();
            PositionItem Punch(float duration, ItemClock clock = ItemClock.Player)
            {
                var item = new PositionItem { m_Motion = new PunchMotion(), m_Target = Self };
                item.m_Timing.m_Duration = duration;
                item.m_Timing.m_Clock = clock;
                return item;
            }
            T After<T>(T item) where T : FeedbackItem
            {
                item.m_Timing.m_Rule = StartRule.AfterPrevious;
                return item;
            }
            GameObject Owner(string name)
            {
                var go = new GameObject(name);
                m_Made.Add(go);
                return go;
            }
            switch (kind)
            {
                case 0:
                case 2:
                    return new Fixture { Scheduler = s, Player = Bound(Owner("zero"), s, Punch(0.2f), After(new LoopStartItem()), After(new UnityEventItem()), After(new LoopEndItem { m_LoopCount = 3 }), After(Punch(0.2f))) };
                case 1:
                    return new Fixture { Scheduler = s, Player = Bound(Owner("forever"), s, Punch(0.2f), After(new LoopStartItem()), After(new UnityEventItem()), After(new LoopEndItem { m_LoopCount = 0 })) };
                case 3:
                    return new Fixture { Scheduler = s, Player = Bound(Owner("hold"), s, Punch(0.3f, ItemClock.Unscaled), Punch(0.1f), After(new HoldItem()), After(Punch(0.1f))) };
                default:
                    FeedbackPlayer waiting = Bound(Owner("waiting"), s, new WaitForSignalItem { m_Timeout = 2f }, After(Punch(0.1f)));
                    FeedbackPlayer done = Bound(Owner("done"), s, Punch(0.3f));
                    done.Completed += SignalOther;
                    s_Other = waiting;
                    return new Fixture { Scheduler = s, Player = done };
            }
        }

        static readonly (string, int, TestDelegate)[] s_Paths =
        {
            ("forward with a signal", -1, () => { s_Fixture.Player.Play(); Tick(14); s_Fixture.Player.Signal(); Tick(30); }),
            ("forward, the wait times out", -1, () => { s_Fixture.Player.Play(); Tick(50); }),
            ("forward, skip", -1, () => { s_Fixture.Player.Play(); Tick(3); s_Fixture.Player.Skip(); Tick(1); }),
            ("backward", -1, () => { s_Fixture.Player.Play(PlayArgs.Default.WithDirection(PlayerDirection.Backward)); Tick(40); }),
            ("seek and stop", -1, () => { s_Fixture.Player.Play(); Tick(2); s_Fixture.Player.Seek(0.1f); Tick(1); s_Fixture.Player.Stop(StopMode.Revert); }),
            ("finite period-0 block", 0, () => { s_Fixture.Player.Play(); Tick(12); }),
            ("infinite period-0 block", 1, () => { s_Fixture.Player.Play(); Tick(8); s_Fixture.Player.Stop(StopMode.Hold); Tick(1); }),
            ("backward across a period-0 block", 2, () => { s_Fixture.Player.Play(PlayArgs.Default.WithDirection(PlayerDirection.Backward)); Tick(12); }),
            ("hold over two clocks", 3, () => { s_Fixture.Player.Play(); Tick(12); }),
            ("signal inside a tick", 4, () => { s_Other.Play(); s_Fixture.Player.Play(); Tick(12); }),
        };

        [UnityTest]
        public IEnumerator GP10_Composite([Values(0, 1, 2, 3, 4, 5, 6, 7, 8, 9)] int path)
        {
            yield return null;
            (string name, int kind, TestDelegate run) = s_Paths[path];
            Func<Fixture> fresh = kind < 0 ? (Func<Fixture>)Composite : () => ZeroPeriod(kind);
            int hits = FirstRuns(fresh, run);
            Check(hits < 5, name + " allocates 5/5");
            // the outcome once more, outside the constraint
            s_Fixture = fresh();
            if (path == 0)
            {
                run();
                Check(!s_Fixture.Player.IsPlaying, "the signalled play ends");
            }
            else if (path == 6)
            {
                s_Fixture.Player.Play();
                Tick(8);
                Check(s_Fixture.Player.IsPlaying, "the infinite block plays on");
            }
            else if (path == 9)
            {
                run();
                Check(!s_Other.IsPlaying, "the signal inside a tick ends the waiting player");
            }
            s_Fixture = null;
            Positive();
            Debug.Log("GATE " + Where + " composite " + name + " " + hits + "/5");
            End();
        }

        // ---- the double swap — the first run grows (by design), the next is 0; the growth's two development warnings (the
        // scheduler's cells · the layer's ledger) come once a fresh fixture — seven fixtures, 14
        // ---- the extension bases — a user's types are outside the registry's combos (hand rows): an instant, a timed act and an
        // effect from an assembly without InternalsVisibleTo, through the two cycles — none allocating, the user's calls reached
        Fixture Extension()
        {
            FeedbackScheduler s = NewScheduler();
            var owner = new GameObject("extension");
            m_Made.Add(owner);
            var instant = new GateInstant();
            instant.m_Timing.m_Delay = 0.05f;
            var effect = new GateEffect { m_Motion = new PunchMotion(), m_Target = Self };
            effect.m_Timing.m_Duration = 0.3f;
            return new Fixture { Scheduler = s, Player = Bound(owner, s, instant, new GateTimed(), effect) };
        }

        [UnityTest]
        public IEnumerator GP17_Extension([Values(0, 1)] int cycle)
        {
            yield return null;
            TestDelegate run = cycle == 0 ? s_SeekCycle : s_ListenerCycle;
            GateCounts.Clear();
            int hits = FirstRuns(Extension, run);
            Check(hits < 5, "the extension items allocate 5/5");
            Check(GateCounts.Fires > 0 && GateCounts.Updates > 0 && GateCounts.Applies > 0, "the user's calls — fire " + GateCounts.Fires + " · update " + GateCounts.Updates + " · apply " + GateCounts.Applies);
            Positive();
            Debug.Log("GATE " + Where + " extension cycle " + cycle + " hits=" + hits);
            End();
        }

        [UnityTest]
        public IEnumerator GP11_DoubleSwap()
        {
            yield return null;
            TestDelegate twice = () => { s_Fixture.Player.Play(); s_Fixture.Player.Play(); s_Fixture.Player.Play(); Tick(2); };
            Func<Fixture> fresh = () => Fresh(() => new PositionItem { m_Motion = new PunchMotion(), m_Target = Grand(), m_Timing = new ItemTiming { m_Duration = 0.3f } }, Leaf.LightCamera);
            int first = FirstRuns(fresh, twice);
            s_Fixture = fresh();
            twice();
            bool again = UIs.AllocatingGCMemory().ApplyTo(twice).IsSuccess;
            s_Fixture = null;
            Check(first == 5, "the first run grows " + first + "/5");
            Check(!again, "the next run is 0");
            Debug.Log("GATE " + Where + " double swap first " + first + "/5 again " + again);
            End(Debug.isDebugBuild ? 14 : 0);
        }

        // ---- players of 100 items (Punch, endless): the code-built one and its Instantiate copy (the serializer's path — new
        // item instances) on schedulers of their own, alternated five times; the median of copy / built per tick within 1.2 in
        // an IL2CPP player. 100 items — one item's tick is mostly the scheduler's fixed share and that dilutes a lost call
        [UnityTest]
        public IEnumerator GP12_Deserialized()
        {
            yield return null;
            const int items = 100;
            const int ticks = 5000;
            var built = new GameObject("built");
            m_Made.Add(built);
            var pb = built.AddComponent<FeedbackPlayer>();
            m_Players.Add(pb);
            for (int i = 0; i < items; i++)
            {
                var item = new PositionItem { m_Motion = new PunchMotion(), m_Target = Self };
                item.m_Timing.m_Infinite = true;
                pb.m_Items.Add(item);
            }
            GameObject copy = Object.Instantiate(built);
            m_Made.Add(copy);
            var pc = copy.GetComponent<FeedbackPlayer>();
            m_Players.Add(pc);
            int shared = 0;
            for (int i = 0; i < items; i++)
            {
                if (ReferenceEquals(pc.m_Items[i], pb.m_Items[i]))
                    shared++;
            }
            Check(pc.m_Items.Count == items && shared == 0, "the copy's items are new — " + shared + " shared of " + pc.m_Items.Count);
            FeedbackScheduler s = NewScheduler();
            FeedbackScheduler s2 = NewScheduler();
            pb.Bind(s);
            pc.Bind(s2);
            pb.EnsureRuntime();
            pc.EnsureRuntime();
            pb.Play();
            pc.Play();
            var ratios = new List<double>();
            var sw = new Stopwatch();
            for (int i = 0; i < 500; i++)
            {
                s.Tick(0.016f);
                s2.Tick(0.016f);
            }
            for (int round = 0; round < 5; round++)
            {
                sw.Restart();
                for (int i = 0; i < ticks; i++)
                    s.Tick(0.016f);
                double a = sw.Elapsed.TotalMilliseconds;
                sw.Restart();
                for (int i = 0; i < ticks; i++)
                    s2.Tick(0.016f);
                ratios.Add(sw.Elapsed.TotalMilliseconds / a);
            }
            ratios.Sort();
            Debug.Log("GATE " + Where + " deserialized items=" + items + " median copy/built=" + ratios[2].ToString("0.###", CultureInfo.InvariantCulture));
            if (k_Judge)
                Check(ratios[2] <= 1.2, "copy / built " + ratios[2].ToString("0.###", CultureInfo.InvariantCulture));
            End();
        }

        // ---- linked players — the first runs of five fresh fixtures linked to one
        // preset (a position tween and a color flash on the grandchild), the seek and the listener cycles: the link is read at
        // the prepare only
        [UnityTest]
        public IEnumerator GP14_Linked()
        {
            yield return null;
            Positive();
            var preset = ScriptableObject.CreateInstance<FeedbackPreset>();
            m_Made.Add(preset);
            var tween = new PositionItem { m_Motion = new TweenMotion(), m_Target = Grand() };
            tween.m_Timing.m_Duration = 0.3f;
            var flash = new MaterialColorItem { m_Motion = new FlashMotion(), m_Target = Grand() };
            flash.m_Timing.m_Duration = 0.3f;
            preset.m_Items.Add(tween);
            preset.m_Items.Add(flash);
            Func<Fixture> linked = () =>
            {
                Fixture f = Fresh(() => null, Leaf.Cube, preset);
                Check(f.Player.Runtime != null && f.Player.Runtime.ItemCount == 2, "the linked list prepared");
                return f;
            };
            int seek = FirstRuns(linked, s_SeekCycle);
            int listener = FirstRuns(linked, s_ListenerCycle);
            Check(seek < 5, "the seek cycle allocates 5/5");
            Check(listener < 5, "the listener cycle allocates 5/5");
            Debug.Log("GATE " + Where + " linked first runs seek " + seek + "/5 listener " + listener + "/5");
            End();
        }

        // ---- an observed cycle — ring handlers on the diagnostics' events and ends (the debugger's
        // shape) and the missed signals of both ways in the seek cycle: the handlers and the counts add nothing to the run. A
        // fixture's first missed Signal warns when it is made (outside the run) — one warning per fixture
        struct SeenEvent
        {
            public FeedbackPlayer Player;
            public PlayerEventKind Kind;
            public PlaybackHandle Handle;
            public int Frame;
        }

        static readonly SeenEvent[] s_Ring = new SeenEvent[200];
        static int s_RingHead;
        static int s_RingTotal;
        static readonly Action<FeedbackPlayer, PlayerEventKind, PlaybackHandle> s_OnEvent = (p, k, h) =>
        {
            s_Ring[s_RingHead] = new SeenEvent { Player = p, Kind = k, Handle = h, Frame = Time.frameCount };
            s_RingHead = (s_RingHead + 1) % s_Ring.Length;
            s_RingTotal++;
        };

        static readonly PlaybackEnd[] s_EndRing = new PlaybackEnd[200];
        static int s_EndHead;
        static int s_EndTotal;
        static readonly Action<FeedbackPlayer, PlaybackHandle, PlaybackEnd, bool> s_OnEnd = (p, h, e, q) =>
        {
            s_EndRing[s_EndHead] = e;
            s_EndHead = (s_EndHead + 1) % s_EndRing.Length;
            s_EndTotal++;
        };

        static readonly TestDelegate s_ObservedCycle = () =>
        {
            s_SeekCycle();
            s_Fixture.Player.Signal();
            s_Fixture.Player.SignalQuietly();
        };

        [UnityTest]
        public IEnumerator GP15_Observed()
        {
            yield return null;
            Positive();
            FeedbackDiagnostics.PlayerEvent += s_OnEvent;
            FeedbackDiagnostics.PlaybackEnded += s_OnEnd;
            try
            {
                int total = s_RingTotal;
                int ends = s_EndTotal;
                var made = new List<FeedbackPlayer>();
                Func<Fixture> fresh = () =>
                {
                    Fixture f = Fresh(() =>
                    {
                        var item = new PositionItem { m_Motion = new TweenMotion(), m_Target = Grand() };
                        item.m_Timing.m_Duration = 0.3f;
                        return item;
                    }, Leaf.LightCamera);
                    f.Player.Signal();
                    made.Add(f.Player);
                    return f;
                };
                int hits = FirstRuns(fresh, s_ObservedCycle);
                Check(hits < 5, "the observed cycle allocates 5/5");
                Check(s_RingTotal > total, "the handler heard nothing");
                Check(s_EndTotal > ends, "the end handler heard nothing");
                Check(made.Count == 6 && made.All(x => x.MissedSignals == 2 && x.MissedQuietSignals == 1), "the counts " + string.Join(",", made.Select(x => x.MissedSignals + "/" + x.MissedQuietSignals)));
                Debug.Log("GATE " + Where + " observed first runs " + hits + "/5 · heard " + (s_RingTotal - total) + " · ends " + (s_EndTotal - ends));
            }
            finally
            {
                FeedbackDiagnostics.PlayerEvent -= s_OnEvent;
                FeedbackDiagnostics.PlaybackEnded -= s_OnEnd;
            }
            End(6);
        }

        // ---- a pooled spawn's cycle — a play takes one, its lifetime gives it back:
        // warmed, the repeats allocate nothing (a fixture — its pool reused) · a despawn of a live one gives it back the same
        static readonly TestDelegate s_SpawnCycle = () =>
        {
            Fixture f = s_Fixture;
            f.Player.Play();
            Tick(4);
        };

        [UnityTest]
        public IEnumerator GP16_SpawnLifetimeCycle()
        {
            yield return null;
            Positive();
            s_Fixture = Fresh(() =>
            {
                var item = new SpawnItem { m_Motion = new SetMotion(), m_Target = Grand(), m_PoolSize = 2, m_Lifetime = 0.1f };
                GameObjectFields(item);
                item.m_PoolSize = 2;
                return item;
            }, Leaf.LightCamera);
            PoolService pools = s_Fixture.Scheduler.Context.PoolsIfMade;
            for (int i = 0; i < 3; i++)
                s_SpawnCycle();
            int hits = 0;
            for (int i = 0; i < 5; i++)
            {
                if (UIs.AllocatingGCMemory().ApplyTo(s_SpawnCycle).IsSuccess)
                    hits++;
            }
            Check(hits == 0, "the spawn → lifetime → give-back cycle allocates " + hits + "/5");
            Check(pools != null && pools.LiveCount == 0 && pools.GrownOf(s_SpawnTemplate) == 0, "given back, never grown");
            s_Fixture.Player.Play();
            Tick(1);
            GameObject live = pools.LiveAt(0);
            bool back = UIs.AllocatingGCMemory().ApplyTo(new TestDelegate(() => pools.TryReturn(live))).IsSuccess;
            Check(!back && pools.LiveCount == 0, "a despawn's give-back allocates " + back);
            s_Fixture = null;
            Debug.Log("GATE " + Where + " spawn cycle " + hits + "/5");
            End();
        }

        // ---- the linker's regression — engine methods nothing else in the build calls, one on each path: a Resources prefab
        // (Transform.DetachChildren — its child leaves), a prefab a Resources prefab names (Light.Reset — the intensity 7 goes
        // back to the default) and the package's scene on the build list (Camera.Reset — the field of view 30 goes back to 60).
        // A Float Event's dynamic float listener in a Resources prefab (Light.set_shadowNearPlane — 0.2 goes to 3). Without the
        // processor they vanish from Minimal, with no error
        [UnityTest]
        public IEnumerator GP13_Linker()
        {
            yield return null;
            var prefab = Resources.Load<GameObject>("FeedbackLinkerProbe");
            Check(prefab != null, "the Resources prefab");
            if (prefab != null)
            {
                GameObject go = Object.Instantiate(prefab);
                m_Made.Add(go);
                int before = go.transform.childCount;
                go.GetComponent<FeedbackPlayer>().Play();
                yield return null;
                yield return null;
                Check(before == 1 && go.transform.childCount == 0, "Resources — children " + before + " → " + go.transform.childCount);
            }
            var holder = Resources.Load<GameObject>("FeedbackLinkerHolder");
            Check(holder != null, "the holder");
            if (holder != null)
            {
                GameObject held = Object.Instantiate(holder.GetComponent<LinkerHolder>().m_Target);
                m_Made.Add(held);
                var light = held.GetComponent<Light>();
                float before = light.intensity;
                held.GetComponent<FeedbackPlayer>().Play();
                yield return null;
                yield return null;
                Check(before == 7f && light.intensity != 7f, "what a Resources prefab names — intensity " + before + " → " + light.intensity);
            }
            var nearPrefab = Resources.Load<GameObject>("FeedbackLinkerFloat");
            Check(nearPrefab != null, "the Float Event's prefab");
            if (nearPrefab != null)
            {
                GameObject near = Object.Instantiate(nearPrefab);
                m_Made.Add(near);
                var light = near.GetComponent<Light>();
                float before = light.shadowNearPlane;
                near.GetComponent<FeedbackPlayer>().Play();
                yield return null;
                yield return null;
                Check(before == 0.2f && light.shadowNearPlane == 3f, "a Float Event — near plane " + before + " → " + light.shadowNearPlane);
            }
            // a Random Unity Event's listener — the aspect a script set goes back to the screen's
            var randomPrefab = Resources.Load<GameObject>("FeedbackLinkerRandom");
            Check(randomPrefab != null, "the Random Unity Event's prefab");
            if (randomPrefab != null)
            {
                GameObject random = Object.Instantiate(randomPrefab);
                m_Made.Add(random);
                var camera = random.GetComponent<Camera>();
                camera.aspect = 5f;
                float before = camera.aspect;
                random.GetComponent<FeedbackPlayer>().Play();
                yield return null;
                yield return null;
                Check(before == 5f && camera.aspect != 5f, "a Random Unity Event — aspect " + before + " → " + camera.aspect);
            }
            Check(SceneUtility.GetBuildIndexByScenePath(FeedbackLinkerSceneName.Path) >= 0, "the scene on the build list");
            if (SceneUtility.GetBuildIndexByScenePath(FeedbackLinkerSceneName.Path) >= 0)
            {
                yield return SceneManager.LoadSceneAsync(FeedbackLinkerSceneName.Name, LoadSceneMode.Additive);
                Scene scene = SceneManager.GetSceneByName(FeedbackLinkerSceneName.Name);
                GameObject root = scene.GetRootGameObjects().FirstOrDefault(r => r.name == "packaged linker");
                Camera camera = root != null ? root.GetComponentInChildren<Camera>(true) : null;
                Check(root != null && camera != null, "the scene's player and camera");
                if (camera != null)
                {
                    float before = camera.fieldOfView;
                    root.GetComponent<FeedbackPlayer>().Play();
                    yield return null;
                    yield return null;
                    Check(before == 30f && camera.fieldOfView == 60f, "the scene — field of view " + before + " → " + camera.fieldOfView);
                }
                yield return SceneManager.UnloadSceneAsync(scene);
            }
            Debug.Log("GATE " + Where + " linker " + (m_Fail.Count == 0 ? "kept" : "lost"));
            End();
        }
    }

    // the skybox items need the scene's skybox. Each fixture has a scheduler of its own — a second skybox copy takes the first
    // copy as its original, and a teardown closing them in the order made leaves a destroyed copy in (null for the next test):
    // a test keeps the skybox and puts it back, and puts the test skybox in when the scene has none (Resources — a player
    // build has it)
    static class PlayerSkybox
    {
        internal const string Name = "FeedbackTestSkybox";

        internal static Material Keep()
        {
            Material kept = RenderSettings.skybox;
            if (kept == null)
                RenderSettings.skybox = Resources.Load<Material>(Name);
            return kept;
        }
    }

    static class FeedbackLinkerSceneName
    {
        internal const string Name = "FeedbackLinkerScene";
        internal const string Path = "Packages/com.xpturn.feedback/Tests/Runtime/Player/Scenes/FeedbackLinkerScene.unity";
    }
}
