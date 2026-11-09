using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.LowLevel;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;
using UIs = UnityEngine.TestTools.Constraints.Is;

namespace xpTURN.Feedback.Tests
{
    // PlayMode — the cross check through the bindings, a game's write, the first rows. Frames are fixed with
    // captureDeltaTime. The effect's values are seen at PostLateUpdate's end (a loop system — a coroutine sees the reverted
    // ones). NP02 and NP04's material part are the development project's (URP — the DevProject category). A row checks
    // everything and judges at the end; TearDown puts Default, the capture delta and the loop back. No Math names · no
    // UnityEditor here
    [PrebuildSetup("xpTURN.Feedback.Tests.Editor.FeedbackTestAssets")]
    [PostBuildCleanup("xpTURN.Feedback.Tests.Editor.FeedbackTestAssets")]
    public sealed class PlayerBindingsPlayTests
    {
        struct BindingsWindow { }

        static Action s_Window;
        static FeedbackScheduler s_Saved;
        static FeedbackScheduler s_Test;
        static readonly int s_BaseColor = Shader.PropertyToID("_BaseColor");
        readonly List<Object> m_Made = new List<Object>();
        readonly List<FeedbackPlayer> m_Players = new List<FeedbackPlayer>();
        readonly List<FeedbackScheduler> m_Schedulers = new List<FeedbackScheduler>();
        readonly List<Camera> m_Silenced = new List<Camera>();
        readonly List<string> m_Fails = new List<string>();

        void Check(bool ok, string what)
        {
            if (!ok)
                m_Fails.Add(what);
        }

        void Done()
        {
            if (m_Fails.Count > 0)
                Assert.Fail(string.Join(" · ", m_Fails));
        }

        static string F(float v) => v.ToString("0.#####", CultureInfo.InvariantCulture);
        static bool Bits(float a, float b) => BitConverter.SingleToInt32Bits(a) == BitConverter.SingleToInt32Bits(b);
        static bool Bits(Vector3 a, Vector3 b) => Bits(a.x, b.x) && Bits(a.y, b.y) && Bits(a.z, b.z);
        static bool Bits(Quaternion a, Quaternion b) => Bits(a.x, b.x) && Bits(a.y, b.y) && Bits(a.z, b.z) && Bits(a.w, b.w);
        static bool Bits(Color a, Color b) => Bits(a.r, b.r) && Bits(a.g, b.g) && Bits(a.b, b.b) && Bits(a.a, b.a);

        static void OnWindow() => s_Window?.Invoke();

        static void SetWindow(bool on)
        {
            PlayerLoopSystem loop = PlayerLoop.GetCurrentPlayerLoop();
            for (int i = 0; i < loop.subSystemList.Length; i++)
            {
                if (loop.subSystemList[i].type != typeof(UnityEngine.PlayerLoop.PostLateUpdate))
                    continue;
                var list = new List<PlayerLoopSystem>(loop.subSystemList[i].subSystemList);
                list.RemoveAll(s => s.type == typeof(BindingsWindow));
                if (on)
                    list.Add(new PlayerLoopSystem { type = typeof(BindingsWindow), updateDelegate = OnWindow });
                loop.subSystemList[i].subSystemList = list.ToArray();
            }
            PlayerLoop.SetPlayerLoop(loop);
        }

        Material m_Sky;

        [SetUp]
        public void SetUp()
        {
            m_Fails.Clear();
            m_Sky = PlayerSkybox.Keep();
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            SetWindow(false);
            s_Window = null;
            foreach (FeedbackPlayer p in m_Players)
            {
                if (p != null)
                    p.ReleaseRuntime();
            }
            m_Players.Clear();
            if (s_Test != null)
            {
                FeedbackScheduler.Default = s_Saved;
                s_Test.Shutdown();
                s_Test = null;
                s_Saved = null;
            }
            foreach (FeedbackScheduler s in m_Schedulers)
            {
                if (!s.IsShutdown)
                    s.Shutdown();
            }
            m_Schedulers.Clear();
            RenderSettings.skybox = m_Sky;
            Time.captureDeltaTime = 0f;
            // a Time Scale row writes the engine's scale — each fixture's context took the one before's as its base
            Time.timeScale = 1f;
            foreach (Camera camera in m_Silenced)
            {
                if (camera != null)
                    camera.enabled = true;
            }
            m_Silenced.Clear();
            foreach (Object o in m_Made)
            {
                if (o != null)
                    Object.Destroy(o);
            }
            m_Made.Clear();
            yield return null;
        }

        static void Swap()
        {
            Time.captureDeltaTime = 1f / 90f;
            s_Saved = FeedbackScheduler.Default;
            s_Test = new FeedbackScheduler(ContextKind.Test);
            FeedbackScheduler.Default = s_Test;
        }

        GameObject Go(string name, Transform parent = null)
        {
            var go = new GameObject(name);
            m_Made.Add(go);
            if (parent != null)
                go.transform.SetParent(parent, false);
            return go;
        }

        FeedbackPlayer PlayerOn(GameObject owner, params FeedbackItem[] items)
        {
            var player = owner.AddComponent<FeedbackPlayer>();
            m_Players.Add(player);
            player.m_Initialization = PlayerInitialization.Lazy;
            player.m_Items.AddRange(items);
            return player;
        }

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

        static TargetRef Ref(TargetRefMode mode) => new TargetRef { m_Mode = mode, m_Path = "", m_Anchor = "" };
        static TargetRef Direct(Object o) => new TargetRef { m_Mode = TargetRefMode.Direct, m_Object = o, m_Path = "", m_Anchor = "" };
        static TargetRef Grand() => new TargetRef { m_Mode = TargetRefMode.Child, m_ChildLookup = ChildLookup.Path, m_Path = "c/g", m_Anchor = "" };

        static T Item<T>(FeedbackMotion motion, TargetRef target, float duration) where T : TargetItem, new()
        {
            var item = new T { m_Motion = motion, m_Target = target };
            item.m_Timing.m_Duration = duration;
            return item;
        }

        static PunchMotion Punch(Vector4 a) => new PunchMotion { m_Params = new PunchParams { m_Amplitude = a, m_Vibrato = 5f, m_Decay = 1f } };
        static ShakeMotion Shake(Vector4 a) => new ShakeMotion { m_Params = new ShakeParams { m_Amplitude = a, m_Frequency = 10f } };
        static OscillateMotion Oscillate(Vector4 a) => new OscillateMotion { m_Params = new OscillateParams { m_Amplitude = a, m_Frequency = 2f, m_Phase = 0.25f } };
        static FlashMotion Flash(Vector4 target) => new FlashMotion { m_Params = new FlashParams { m_Target = target, m_Attack = 0.1f, m_Hold = 0.2f, m_Release = 0.1f } };
        static BlinkMotion Blink(Vector4 v) => new BlinkMotion { m_Params = new BlinkParams { m_Value = v, m_Period = 0.1f, m_Duty = 0.5f, m_StartOn = true } };

        // ---- the cross check through the bindings

        [UnityTest]
        public IEnumerator NP01_CrossCheck() => CrossCheck(ItemPlayDirection.FollowPlayer);

        // every item Backward — each is its own reverse play in a forward play
        [UnityTest]
        public IEnumerator NP01b_TurnedCrossCheck() => CrossCheck(ItemPlayDirection.Backward);

        IEnumerator CrossCheck(ItemPlayDirection way)
        {
            Swap();
            GameObject parent = Go("parent");
            parent.transform.localRotation = Quaternion.Euler(0f, 0f, 37f);
            parent.transform.localScale = new Vector3(1.5f, 2f, 0.7f);
            GameObject pl = Go("pl");
            pl.transform.localPosition = new Vector3(0.3f, -1.7f, 2.9f);
            Vector3 plBase = pl.transform.localPosition;
            GameObject pw = Go("pw", parent.transform);
            pw.transform.localPosition = new Vector3(1.1f, 0.4f, -0.6f);
            Vector3 pwBase = pw.transform.localPosition;
            var world = Item<PositionItem>(Oscillate(new Vector4(0.5f, 0.5f, 0f, 0f)), Direct(pw.transform), 0.4f);
            world.m_Space = PositionSpace.World;
            GameObject once = Go("once");
            GameObject rl = Go("rl");
            rl.transform.localRotation = Quaternion.Euler(13f, 47f, -71f);
            Quaternion rlBase = rl.transform.localRotation;
            once.transform.localRotation = rlBase;
            Quaternion rlN = once.transform.localRotation;
            GameObject rw = Go("rw", parent.transform);
            rw.transform.localRotation = Quaternion.Euler(-21f, 5f, 63f);
            Quaternion rwBase = rw.transform.localRotation;
            once.transform.localRotation = rwBase;
            Quaternion rwN = once.transform.localRotation;
            var rotWorld = Item<RotationItem>(Shake(new Vector4(20f, 20f, 20f, 0f)), Direct(rw.transform), 0.4f);
            rotWorld.m_Space = TransformSpace.World;
            GameObject sc = Go("sc");
            sc.transform.localScale = new Vector3(1.3f, 0.7f, 2.1f);
            Vector3 scBase = sc.transform.localScale;
            GameObject lg = Go("lg");
            Light light = lg.AddComponent<Light>();
            light.intensity = 0.2f;
            GameObject blinked = Go("blinked");
            FeedbackPlayer player = PlayerOn(Go("player"),
                Item<PositionItem>(Oscillate(new Vector4(1f, 2f, 3f, 0f)), Direct(pl.transform), 0.4f),
                world,
                Item<RotationItem>(Shake(new Vector4(30f, 30f, 30f, 0f)), Direct(rl.transform), 0.4f),
                rotWorld,
                Item<ScaleItem>(Oscillate(new Vector4(0.5f, 0.5f, 0.5f, 0f)), Direct(sc.transform), 0.4f),
                Item<LightIntensityItem>(Shake(new Vector4(2f, 0f, 0f, 0f)), Direct(light), 0.4f),
                Item<LightIntensityItem>(Flash(new Vector4(4f, 0f, 0f, 0f)), Direct(light), 0.4f),
                Item<SetActiveItem>(Blink(Vector4.zero), Direct(blinked), 0.4f));
            yield return null;
            bool plSeen = false, pwSeen = false, rlSeen = false, rwSeen = false, scSeen = false, lightSeen = false, blinkSeen = false;
            s_Window = () =>
            {
                plSeen |= pl.transform.localPosition != plBase;
                pwSeen |= pw.transform.localPosition != pwBase;
                rlSeen |= Quaternion.Angle(rl.transform.localRotation, rlBase) > 0.1f;
                rwSeen |= Quaternion.Angle(rw.transform.localRotation, rwBase) > 0.1f;
                scSeen |= sc.transform.localScale != scBase;
                lightSeen |= light.intensity != 0.2f;
                blinkSeen |= !blinked.activeSelf;
            };
            foreach (FeedbackItem it in player.m_Items)
                ((EffectItem)it).m_PlayDirection = way;
            SetWindow(true);
            player.Play();
            for (int i = 0; i < 40; i++)
                yield return null;
            SetWindow(false);
            s_Window = null;
            for (int i = 0; i < 10; i++)
                yield return null;
            Check(plSeen && pwSeen && rlSeen && rwSeen && scSeen && lightSeen && blinkSeen, "not seen in the window — " + plSeen + " " + pwSeen + " " + rlSeen + " " + rwSeen + " " + scSeen + " " + lightSeen + " " + blinkSeen);
            Check(Bits(pl.transform.localPosition, plBase), "position local end");
            Check(Bits(pw.transform.localPosition, pwBase), "position world end");
            Check(Bits(rl.transform.localRotation, rlN), "rotation local end ≠ N(q0)");
            Check(Bits(rw.transform.localRotation, rwN), "rotation world end ≠ N(q0)");
            Check(Bits(sc.transform.localScale, scBase), "scale end");
            Check(Bits(light.intensity, 0.2f), "light end " + F(light.intensity));
            Check(blinked.activeSelf, "the blink did not end on");
            Done();
        }

        // ---- the cross check — materials, the development project's URP

        [UnityTest, Category("DevProject")]
        public IEnumerator NP02_MaterialCrossCheck()
        {
            Swap();
            var c0 = new Color(0.8f, 0.3f, 0.2f, 1f);
            MeshRenderer Cube(string name)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.name = name;
                m_Made.Add(go);
                var r = go.GetComponent<MeshRenderer>();
                // the prepared material — a URP player's primitive has the error shader
                var prepared = Resources.Load<Material>("FeedbackTestMaterial");
                var mat = new Material(prepared != null ? prepared : r.sharedMaterial) { name = name + "-m" };
                mat.SetColor(s_BaseColor, c0);
                m_Made.Add(mat);
                r.sharedMaterial = mat;
                return r;
            }
            MeshRenderer a = Cube("mpb");
            Color shown = a.sharedMaterial.GetColor(s_BaseColor);
            MeshRenderer b = Cube("instance");
            var inst = Item<MaterialColorItem>(Oscillate(new Vector4(0.2f, 0.1f, 0.05f, 0f)), Direct(b), 0.4f);
            inst.m_WriteMode = MaterialWriteMode.Instance;
            FeedbackPlayer player = PlayerOn(Go("player"),
                Item<MaterialColorItem>(Oscillate(new Vector4(0.2f, 0.1f, 0.05f, 0f)), Direct(a), 0.4f),
                Item<MaterialColorItem>(Flash(new Vector4(1f, 1f, 1f, 1f)), Direct(a), 0.4f),
                inst);
            yield return null;
            player.Play();
            Material copy = b.sharedMaterial;
            var reread = new Material(copy);
            m_Made.Add(reread);
            reread.SetColor(s_BaseColor, shown);
            Color nc0 = reread.GetColor(s_BaseColor);
            bool block = false, changed = false;
            s_Window = () =>
            {
                block |= a.HasPropertyBlock();
                changed |= !Bits(b.sharedMaterial.GetColor(s_BaseColor), nc0);
            };
            SetWindow(true);
            for (int i = 0; i < 40; i++)
                yield return null;
            SetWindow(false);
            s_Window = null;
            for (int i = 0; i < 10; i++)
                yield return null;
            Check(block, "MPB — no block in the window");
            Check(changed, "instance — no change in the window");
            Check(!a.HasPropertyBlock(), "MPB — a block after");
            Check(Bits(a.sharedMaterial.GetColor(s_BaseColor), shown), "MPB — the material changed");
            Check(Bits(b.sharedMaterial.GetColor(s_BaseColor), nc0), "instance — the end is not N(c0)");
            Done();
        }

        // ---- a game's write during the effect stays after it

        [UnityTest]
        public IEnumerator NP03_GameWrite()
        {
            Swap();
            GameObject t = Go("t");
            t.transform.localPosition = new Vector3(1f, 1f, 1f);
            GameObject r = Go("r");
            r.transform.localRotation = Quaternion.Euler(10f, 20f, 30f);
            Quaternion rBase = r.transform.localRotation;
            GameObject lg = Go("lg");
            Light light = lg.AddComponent<Light>();
            light.intensity = 1f;
            FeedbackPlayer player = PlayerOn(Go("player"),
                Item<PositionItem>(Oscillate(new Vector4(1f, 0f, 0f, 0f)), Direct(t.transform), 0.4f),
                Item<RotationItem>(Oscillate(new Vector4(20f, 0f, 0f, 0f)), Direct(r.transform), 0.4f),
                Item<LightIntensityItem>(Oscillate(new Vector4(0.5f, 0f, 0f, 0f)), Direct(light), 0.4f));
            yield return null;
            player.Play();
            int target = Time.frameCount + 10;
            var written = new Vector3(5f, 6f, 7f);
            Quaternion writtenRotation = Quaternion.Euler(-40f, 15f, 70f);
            Quaternion readBack = default;
            bool seen = false;
            s_Window = () =>
            {
                if (Time.frameCount != target)
                    return;
                seen = t.transform.localPosition != new Vector3(1f, 1f, 1f) && Quaternion.Angle(r.transform.localRotation, rBase) > 0.1f && light.intensity != 1f;
                t.transform.localPosition = written;
                r.transform.localRotation = writtenRotation;
                readBack = r.transform.localRotation;
                light.intensity = 3f;
            };
            SetWindow(true);
            for (int i = 0; i < 60; i++)
                yield return null;
            SetWindow(false);
            s_Window = null;
            GameObject once = Go("once");
            once.transform.localRotation = readBack;
            Check(seen, "the effect not seen in the window before the write");
            Check(Bits(t.transform.localPosition, written), "position after " + t.transform.localPosition);
            Check(Bits(r.transform.localRotation, once.transform.localRotation), "rotation after ≠ N(written)");
            Check(light.intensity == 3f, "light after " + F(light.intensity));
            Done();
        }

        // ---- the first rows — five fresh fixtures, their first run. A test scheduler ticked in the delegate; the combos are the
        // package's registered target types (a test type another test registered is not one) × the motions they take
        // (reflection) × the space · interpolation · write mode branches

        sealed class Fixture
        {
            public FeedbackScheduler Scheduler;
            public FeedbackPlayer Player;
        }

        static Fixture s_Fixture;
        static int s_Heard;
        static readonly TestDelegate s_Cycle = () =>
        {
            Fixture f = s_Fixture;
            f.Player.Play();
            for (int i = 0; i < 6; i++)
                f.Scheduler.Tick(0.05f);
            f.Player.Pause();
            f.Scheduler.Tick(0.05f);
            f.Player.Resume();
            f.Scheduler.Tick(0.05f);
            f.Player.Stop(StopMode.Revert);
            f.Player.Play();
            for (int i = 0; i < 12; i++)
                f.Scheduler.Tick(0.05f);
            f.Player.Stop(StopMode.Complete);
            f.Scheduler.Tick(0.05f);
        };

        static void Heard(FeedbackPlayer p, PlaybackHandle h) => s_Heard++;

        static void Noop()
        {
        }

        static void NoopFloat(float v)
        {
        }

        // a fresh copy of a template — its grandchild has no C# Transform yet (the first read is the fixture's)
        Fixture Fresh(Func<GameObject, FeedbackItem> make, Leaf kind, Type itemType = null)
        {
            var s = new FeedbackScheduler(ContextKind.Test);
            m_Schedulers.Add(s);
            var template = new GameObject("template");
            m_Made.Add(template);
            var mid = new GameObject("c");
            mid.transform.SetParent(template.transform, false);
            mid.transform.localRotation = Quaternion.Euler(0f, 0f, 30f);
            bool cube = kind == Leaf.Cube;
            GameObject leaf = MakeLeaf(kind, itemType);
            if (cube)
            {
                var prepared = Resources.Load<Material>("FeedbackTestMaterial");
                if (prepared != null)
                    leaf.GetComponent<Renderer>().sharedMaterial = prepared;
                Materials(leaf);
            }
            leaf.name = "g";
            leaf.transform.SetParent(mid.transform, false);
            if (kind == Leaf.LightCamera)
            {
                leaf.AddComponent<Light>();
                leaf.AddComponent<Camera>();
            }
            GameObject owner = Object.Instantiate(template);
            m_Made.Add(owner);
            var player = owner.AddComponent<FeedbackPlayer>();
            m_Players.Add(player);
            FeedbackItem item = make(owner);
            player.m_Items.Add(item);
            player.Bind(s);
            player.Played += Heard;
            player.Completed += Heard;
            player.Stopped += Heard;
            player.EnsureRuntime();
            return new Fixture { Scheduler = s, Player = player };
        }

        // red when the first run of all five fresh fixtures allocates — the warm-up is another fixture with the same delegate
        string Judge(string name, Func<GameObject, FeedbackItem> make, Leaf cube, Type itemType = null)
        {
            s_Fixture = Fresh(make, cube, itemType);
            s_Cycle();
            int hits = 0, prepared = 0;
            for (int i = 0; i < 5; i++)
            {
                s_Fixture = Fresh(make, cube, itemType);
                if (s_Fixture.Player.Runtime != null && s_Fixture.Player.Runtime.Binding(0) != null)
                    prepared++;
                if (UIs.AllocatingGCMemory().ApplyTo(s_Cycle).IsSuccess)
                    hits++;
            }
            s_Fixture = null;
            if (hits == 5)
                return name + " allocated 5/5";
            if (prepared < 5)
                return name + " prepared " + prepared + "/5";
            return null;
        }

        // the motion axis: every concrete motion type by reflection (no kind → instance factory)
        static readonly Type[] s_Motions = typeof(FeedbackMotion).Assembly.GetTypes().Where(t => t.IsSubclassOf(typeof(FeedbackMotion)) && !t.IsAbstract).ToArray();

        // the fixture table: a registered target type not here is red
        static readonly Dictionary<Type, Leaf> s_FixtureOf = new Dictionary<Type, Leaf>
        {
            { typeof(PositionItem), Leaf.LightCamera }, { typeof(RotationItem), Leaf.LightCamera }, { typeof(ScaleItem), Leaf.LightCamera },
            { typeof(LightIntensityItem), Leaf.LightCamera }, { typeof(SetActiveItem), Leaf.LightCamera }, { typeof(MaterialColorItem), Leaf.Cube },
            // the camera shake on the grandchild's transform (the hit stop is no target item)
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

        IEnumerable<(string, Func<GameObject, FeedbackItem>, Leaf, Type)> Combos(bool materials)
        {
            foreach (Type itemType in FeedbackImplementations.RegisteredTypes.Where(t => t.IsSubclassOf(typeof(TargetItem)) && OfThePackage(t)).OrderBy(t => t.Name))
            {
                if (!s_FixtureOf.TryGetValue(itemType, out Leaf cube))
                {
                    if (!ModuleFixtures.Has(itemType))
                    {
                        m_Fails.Add("no fixture for " + itemType.Name);
                        continue;
                    }
                    cube = Leaf.Module;
                }
                if ((cube == Leaf.Cube) != materials || ModuleFixtures.SkipInEditor(itemType))
                    continue;
                var probe = (TargetItem)Activator.CreateInstance(itemType);
                foreach (Type motionType in s_Motions)
                {
                    var motion = (FeedbackMotion)Activator.CreateInstance(motionType);
                    if (!probe.Traits.Supports(motion.Kind))
                        continue;
                    Type it = itemType, mt = motionType;
                    var springs = motion is SpringMotion ? new[] { SpringMode.Bump, SpringMode.MoveTo } : new[] { SpringMode.Bump };
                    foreach (SpringMode mode in springs)
                    {
                        foreach (int variant in Variants(itemType, motion))
                        {
                            SpringMode sm = mode;
                            int v = variant;
                            string name = it.Name + " " + mt.Name + (motion is SpringMotion ? " " + sm : "") + " v" + v;
                            yield return (name, owner =>
                            {
                                var item = (TargetItem)Activator.CreateInstance(it);
                                var made = (FeedbackMotion)Activator.CreateInstance(mt);
                                if (made is SpringMotion spring)
                                    spring.m_Params.m_Mode = sm;
                                item.m_Motion = made;
                                item.m_Target = Grand();
                                item.m_Timing.m_Duration = made is SetMotion ? 0f : 0.3f;
                                switch (item)
                                {
                                    case PositionItem p: p.m_Space = v == 1 ? PositionSpace.World : v == 2 ? PositionSpace.Anchored : PositionSpace.Local; break;
                                    case RotationItem r: r.m_Space = v == 1 ? TransformSpace.World : TransformSpace.Local; r.m_Interpolation = v == 2 ? RotationInterpolation.Shortest : RotationInterpolation.Euler; break;
                                    case MaterialColorItem c: c.m_WriteMode = v == 1 ? MaterialWriteMode.Instance : MaterialWriteMode.PropertyBlock; break;
                                    // the second references are the owner (Self) — the leaf c/g is under it, no cycle
                                    case LookAtItem l: l.m_LookTarget = Ref(TargetRefMode.Self); l.m_Mode = v == 1 ? LookAtMode.Direction : LookAtMode.Transform; break;
                                    case RotateAroundItem a: a.m_Pivot = Ref(TargetRefMode.Self); break;
                                    case MatchTransformItem m: m.m_Destination = Ref(TargetRefMode.Self); m.m_Track = v == 1; break;
                                    case SetParentItem s: s.m_NewParent = Ref(TargetRefMode.Self); s.m_WorldPositionStays = v == 0; break;
                                }
                                MaterialFields(item, v);
                                SpriteFields(item, v);
                                GameObjectFields(item);
                                ModuleFixtures.Fields(item);
                                return item;
                            }, cube, it);
                        }
                    }
                }
            }
        }

        static IEnumerable<int> Variants(Type itemType, FeedbackMotion motion)
        {
            if (itemType == typeof(PositionItem))
                return new[] { 0, 1, 2 };
            if (itemType == typeof(RotationItem))
                return motion is TweenMotion ? new[] { 0, 1, 2 } : new[] { 0, 1 };
            if (itemType == typeof(MaterialColorItem))
                return new[] { 0, 1 };
            if (itemType == typeof(LookAtItem) || itemType == typeof(MatchTransformItem) || itemType == typeof(SetParentItem))
                return new[] { 0, 1 };
            // the write mode
            if (typeof(IMaterialPropertyItem).IsAssignableFrom(itemType))
                return new[] { 0, 1 };
            // the axis · the end
            if (itemType == typeof(SpriteFlipItem) || itemType == typeof(LineColorItem))
                return new[] { 0, 1 };
            return new[] { 0 };
        }

        [UnityTest]
        public IEnumerator NP04_FirstRows()
        {
            yield return null;
            int combos = 0;
            foreach ((string name, Func<GameObject, FeedbackItem> make, Leaf cube, Type itemType) in Combos(false).ToList())
            {
                combos++;
                string bad = Judge(name, make, cube, itemType);
                if (bad != null)
                    m_Fails.Add(bad);
            }
            // the particles' seven where their module compiles · the animators' eight (a speed's spring twice) · the 3D bodies'
            // three, the 2D bodies' two · Core's thirty-one (Time Scale 3 · Float Value 10 · Vector3 8 · Color 5 · Float Event 5)
            Check(combos == 181 + (FeedbackImplementations.RegisteredTypes.Contains(typeof(ParticleControlItem)) ? 7 : 0)
                + (FeedbackImplementations.RegisteredTypes.Contains(typeof(AnimatorSpeedItem)) ? 8 : 0)
                + (FeedbackImplementations.RegisteredTypes.Contains(typeof(RigidbodyForceItem)) ? 3 : 0)
                + (FeedbackImplementations.RegisteredTypes.Contains(typeof(Rigidbody2DForceItem)) ? 2 : 0)
                + (FeedbackImplementations.RegisteredTypes.Contains(typeof(Light2DIntensityItem)) ? 10 : 0) + 15
                + (FeedbackImplementations.RegisteredTypes.Contains(typeof(GraphicColorItem)) ? 33 : 0) + (FeedbackImplementations.RegisteredTypes.Contains(typeof(VideoPlayerControlItem)) ? 1 : 0)
                + (FeedbackImplementations.RegisteredTypes.Contains(typeof(TMPFontSizeItem)) ? (ModuleFixtures.SkipInEditor(typeof(TMPCountToItem)) ? 64 : 65) : 0)
                + (FeedbackImplementations.RegisteredTypes.Contains(typeof(ScreenFlashItem)) ? 4 : 0)
                + (FeedbackImplementations.RegisteredTypes.Contains(typeof(PostFXBloomItem)) ? 45 : 0)
                + (FeedbackImplementations.RegisteredTypes.Contains(typeof(CinemachineLensFieldOfViewItem)) ? 19 : 0)
                + (FeedbackImplementations.RegisteredTypes.Contains(typeof(GamepadRumbleItem)) ? 3 : 0), "combos " + combos);
            // UnityEvent: an empty event · a listener's second invoke
            string ue = Judge("UnityEvent empty", o => new UnityEventItem(), Leaf.LightCamera);
            if (ue != null)
                m_Fails.Add(ue);
            ue = Judge("UnityEvent listener (second invoke)", o =>
            {
                var item = new UnityEventItem();
                item.m_Event.AddListener(Noop);
                // measured at its second invoke — the first builds the invoke list
                item.m_Event.Invoke();
                return item;
            }, Leaf.LightCamera);
            if (ue != null)
                m_Fails.Add(ue);
            // a Float Event listener — measured at its second invoke too
            ue = Judge("Float Event listener (second invoke)", o =>
            {
                var item = new CustomFloatEventItem { m_Motion = Punch(Vector4.one) };
                item.m_Timing.m_Duration = 0.3f;
                item.m_Event.AddListener(NoopFloat);
                item.m_Event.Invoke(0f);
                return item;
            }, Leaf.LightCamera);
            if (ue != null)
                m_Fails.Add(ue);
            // positive control
            int positive = 0;
            for (int i = 0; i < 5; i++)
            {
                if (UIs.AllocatingGCMemory().ApplyTo(new TestDelegate(() => s_Heard += new int[4].Length)).IsSuccess)
                    positive++;
            }
            Check(positive == 5, "positive control " + positive + "/5");
            // the main camera's variant: the first play after a change takes the slot — the play after is 0
            foreach (Camera other in Camera.allCameras)
            {
                if (!other.CompareTag("MainCamera"))
                    continue;
                other.enabled = false;
                m_Silenced.Add(other);
            }
            GameObject camA = Go("camA");
            camA.tag = "MainCamera";
            camA.AddComponent<Camera>();
            s_Fixture = Fresh(o => Item<PositionItem>(Punch(Vector4.one), Ref(TargetRefMode.MainCamera), 0.3f), Leaf.LightCamera);
            s_Cycle();
            camA.tag = "Untagged";
            GameObject camB = Go("camB");
            camB.tag = "MainCamera";
            camB.AddComponent<Camera>();
            _ = camB.transform;
            bool first = UIs.AllocatingGCMemory().ApplyTo(s_Cycle).IsSuccess;
            bool second = UIs.AllocatingGCMemory().ApplyTo(s_Cycle).IsSuccess;
            s_Fixture = null;
            Check(first && !second, "camera change — first " + first + " · then " + second);
            Done();
        }

        [UnityTest, Category("DevProject")]
        public IEnumerator NP04_FirstRowsMaterials()
        {
            yield return null;
            int combos = 0;
            foreach ((string name, Func<GameObject, FeedbackItem> make, Leaf cube, Type itemType) in Combos(true).ToList())
            {
                combos++;
                string bad = Judge(name, make, cube, itemType);
                if (bad != null)
                    m_Fails.Add(bad);
            }
            Check(combos == 61, "material combos " + combos);
            Done();
        }

        // every combo turned — Backward in a forward play (the cursor's way) · Forward items in a backward play (the backward
        // sample's way) — the first runs allocate nothing
        static Func<GameObject, FeedbackItem> Turned(Func<GameObject, FeedbackItem> make, ItemPlayDirection way) => o =>
        {
            var item = (EffectItem)make(o);
            item.m_PlayDirection = way;
            return item;
        };

        // Judge with backward players: each fresh fixture's player plays backward
        string JudgeBackward(string name, Func<GameObject, FeedbackItem> make, Leaf cube)
        {
            s_Fixture = Fresh(make, cube);
            s_Fixture.Player.m_Direction = PlayerDirection.Backward;
            s_Cycle();
            int hits = 0;
            for (int i = 0; i < 5; i++)
            {
                s_Fixture = Fresh(make, cube);
                s_Fixture.Player.m_Direction = PlayerDirection.Backward;
                if (UIs.AllocatingGCMemory().ApplyTo(s_Cycle).IsSuccess)
                    hits++;
            }
            s_Fixture = null;
            return hits == 5 ? name + " allocated 5/5" : null;
        }

        [UnityTest]
        public IEnumerator NP04_TurnedRows()
        {
            yield return null;
            int combos = 0;
            foreach ((string name, Func<GameObject, FeedbackItem> make, Leaf cube, Type itemType) in Combos(false).ToList())
            {
                combos++;
                string bad = Judge(name + " Backward", Turned(make, ItemPlayDirection.Backward), cube, itemType);
                if (bad != null)
                    m_Fails.Add(bad);
            }
            Check(combos >= 181, "combos " + combos);
            foreach ((string name, FeedbackMotion motion) in new (string, FeedbackMotion)[] { ("Punch", Punch(Vector4.one)), ("Shake", Shake(Vector4.one)), ("Oscillate", Oscillate(Vector4.one)), ("Flash", Flash(Vector4.one)) })
            {
                FeedbackMotion m = motion;
                string bad = JudgeBackward("Position " + name + " Forward in a backward play", Turned(o => name == "Flash" ? Item<LightIntensityItem>(m, Grand(), 0.3f) : (FeedbackItem)Item<PositionItem>(m, Grand(), 0.3f), ItemPlayDirection.Forward), Leaf.LightCamera);
                if (bad != null)
                    m_Fails.Add(bad);
            }
            Done();
        }

        [UnityTest, Category("DevProject")]
        public IEnumerator NP04_TurnedRowsMaterials()
        {
            yield return null;
            int combos = 0;
            foreach ((string name, Func<GameObject, FeedbackItem> make, Leaf cube, Type itemType) in Combos(true).ToList())
            {
                combos++;
                string bad = Judge(name + " Backward", Turned(make, ItemPlayDirection.Backward), cube, itemType);
                if (bad != null)
                    m_Fails.Add(bad);
            }
            Check(combos == 61, "material combos " + combos);
            Done();
        }
    }
}
