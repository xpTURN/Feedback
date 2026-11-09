using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace xpTURN.Feedback.Tests.Editor
{
    // the material and renderer targets on a test scheduler ticked by hand (0.05 s). The cubes' materials are the pipeline's own
    // (URP Lit in the development project, Standard in the minimal one) — the properties both have (the main color · _Cutoff ·
    // _EmissionColor · the main texture)
    public sealed class MaterialTargetsTests
    {
        const float Step = 0.05f;
        const string PropsShader = "Shader \"Hidden/MaterialTargetsProps\" { Properties { _F(\"F\", Float) = 1  _R(\"R\", Range(0, 1)) = 0.5  _N(\"N\", Integer) = 3  _V(\"V\", Vector) = (1,2,3,4)  _C(\"C\", Color) = (1,0,0,1) } SubShader { Pass { } } }";

        static readonly int s_Emission = Shader.PropertyToID("_EmissionColor");
        static readonly int s_Cutoff = Shader.PropertyToID("_Cutoff");

        readonly List<Object> m_Made = new List<Object>();
        readonly List<FeedbackScheduler> m_Schedulers = new List<FeedbackScheduler>();
        readonly List<FeedbackPlayer> m_Players = new List<FeedbackPlayer>();
        Shader m_Props;

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
            foreach (Object o in m_Made)
            {
                if (o != null)
                    Object.DestroyImmediate(o);
            }
            m_Made.Clear();
            m_Schedulers.Clear();
            m_Players.Clear();
            m_Props = null;
        }

        static string B(float f) => BitConverter.SingleToInt32Bits(f).ToString("X8");

        static string BC(Color c) => B(c.r) + B(c.g) + B(c.b) + B(c.a);

        static string BV(Vector4 v) => B(v.x) + B(v.y) + B(v.z) + B(v.w);

        static TargetRef Direct(Object o) => new TargetRef { m_Mode = TargetRefMode.Direct, m_Object = o, m_Path = "", m_Anchor = "" };

        static TweenMotion TweenTo(Vector4 to)
        {
            TweenParams p = TweenParams.Default;
            p.m_To = to;
            return new TweenMotion { m_Params = p };
        }

        static PunchMotion PunchBy(float a) => new PunchMotion { m_Params = new PunchParams { m_Amplitude = new Vector4(a, a, a, 0f), m_Vibrato = 1f, m_Decay = 0f } };

        static OscillateMotion Oscillate(float a) => new OscillateMotion { m_Params = new OscillateParams { m_Amplitude = new Vector4(a, 0f, 0f, 0f), m_Frequency = 1f, m_Phase = 0f } };

        static BlinkMotion BlinkOff()
        {
            var blink = new BlinkMotion();
            blink.m_Params.m_Value = Vector4.zero;
            return blink;
        }

        static T On<T>(T item, Object target, FeedbackMotion motion, float duration) where T : TargetItem
        {
            item.m_Target = Direct(target);
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

        FeedbackScheduler Sched()
        {
            var s = new FeedbackScheduler(ContextKind.Test);
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

        // a keeper runs on, so an instant's Revert stop comes while the play lives
        FeedbackItem Keeper() => On(new PositionItem(), Go("Keeper"), PunchBy(0.5f), 1f);

        static void Tick(FeedbackScheduler s, int n)
        {
            for (int i = 0; i < n; i++)
                s.Tick(Step);
        }

        Shader Props()
        {
            if (m_Props == null)
            {
                m_Props = ShaderUtil.CreateShaderAsset(PropsShader);
                m_Made.Add(m_Props);
            }
            return m_Props;
        }

        // copies of the pipeline's default material (the second of another shader when given) — a main color and an emission
        // off their defaults
        MeshRenderer Cube(string name, int materials = 1, Shader second = null, Shader all = null)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            m_Made.Add(go);
            var r = go.GetComponent<MeshRenderer>();
            var mats = new Material[materials];
            for (int i = 0; i < materials; i++)
            {
                mats[i] = i == 1 && second != null ? new Material(second) : all != null ? new Material(all) : new Material(r.sharedMaterial);
                mats[i].name = name + "-m" + i;
                if (MaterialColorBinding.MainColor(mats[i].shader, out int main))
                    mats[i].SetColor(main, new Color(0.8f, 0.3f, 0.2f, 1f));
                if (mats[i].HasColor(s_Emission))
                    mats[i].SetColor(s_Emission, new Color(0.1f, 0.2f, 0.3f, 1f));
                m_Made.Add(mats[i]);
            }
            r.sharedMaterials = mats;
            return r;
        }

        static int Main(Renderer r)
        {
            Assert.That(MaterialColorBinding.MainColor(r.sharedMaterial.shader, out int id), Is.True, "the fixture has a main color");
            return id;
        }

        static MaterialPropertyBlock Block(Renderer r, int index = -1)
        {
            var b = new MaterialPropertyBlock();
            if (index < 0)
                r.GetPropertyBlock(b);
            else
                r.GetPropertyBlock(b, index);
            return b;
        }

        static string Mats(Renderer r) => string.Join(",", r.sharedMaterials.Select(m => ReferenceEquals(m, null) ? "null" : m == null ? "destroyed" : m.name));

        static int Copies() => Resources.FindObjectsOfTypeAll<Material>().Count(m => m != null && m.name.EndsWith("(Feedback)", StringComparison.Ordinal));

        bool Prepared(TargetItem item, Shader shader)
        {
            MeshRenderer r = Cube("Form", 1, null, shader);
            item.m_Target = Direct(r);
            item.m_Motion = TweenTo(Vector4.one);
            return Player(Sched(), item).Runtime.Binding(0) != null;
        }

        // ---- each item makes its own binding (the registry's completeness by name is DataModelReadersTests')

        [Test]
        public void MQ02_EachMakesItsBinding()
        {
            var want = new Dictionary<Type, Type>
            {
                { typeof(MaterialFloatItem), typeof(MaterialFloatBinding) }, { typeof(MaterialVectorItem), typeof(MaterialVectorBinding) },
                { typeof(TextureOffsetItem), typeof(TextureOffsetBinding) }, { typeof(TextureScaleItem), typeof(TextureScaleBinding) },
                { typeof(EmissionItem), typeof(EmissionBinding) }, { typeof(MaterialSwapItem), typeof(MaterialSwapBinding) },
                { typeof(RendererVisibilityItem), typeof(RendererVisibilityBinding) },
            };
            foreach (KeyValuePair<Type, Type> w in want)
                Assert.That(FeedbackImplementations.Create((EffectItem)Activator.CreateInstance(w.Key))?.GetType(), Is.EqualTo(w.Value), w.Key.Name);
        }

        // ---- another's block is kept whole · an index block starts from the whole block — the color and a float

        void BlockRules(bool color)
        {
            FeedbackScheduler s = Sched();
            MeshRenderer r = Cube("Rules");
            int main = Main(r);
            var other = new MaterialPropertyBlock();
            if (color)
                other.SetFloat(s_Cutoff, 0.25f);
            else
                other.SetColor(s_Emission, Color.green);
            r.SetPropertyBlock(other);
            TargetItem item = color ? (TargetItem)new MaterialColorItem() : new MaterialFloatItem { m_PropertyName = "_Cutoff" };
            Player(s, On(item, r, PunchBy(0.3f), 0.5f)).Play();
            Tick(s, 15);
            s.Tick(0f);
            MaterialPropertyBlock b = Block(r);
            if (color)
                Assert.That((b.HasFloat(s_Cutoff) && B(b.GetFloat(s_Cutoff)) == B(0.25f), b.HasColor(main)), Is.EqualTo((true, false)), "the other's block kept, ours gone");
            else
                Assert.That((b.HasColor(s_Emission), b.HasFloat(s_Cutoff)), Is.EqualTo((true, false)), "the other's block kept, ours gone");

            FeedbackScheduler s2 = Sched();
            MeshRenderer r2 = Cube("Index", 2);
            var whole = new MaterialPropertyBlock();
            if (color)
                whole.SetColor(main, Color.blue);
            else
                whole.SetFloat(s_Cutoff, 0.77f);
            r2.SetPropertyBlock(whole);
            TargetItem index = color ? (TargetItem)new MaterialColorItem { m_MaterialIndex = 0 } : new MaterialFloatItem { m_PropertyName = "_Cutoff", m_MaterialIndex = 0 };
            Player(s2, On(index, r2, PunchBy(0.01f), 1f)).Play();
            s2.Tick(Step);
            MaterialPropertyBlock ib = Block(r2, 0);
            float got = color ? ib.GetColor(main).b : ib.GetFloat(s_Cutoff);
            Assert.That(got, Is.EqualTo(color ? 1f : 0.77f).Within(0.05f), "the index block starts from the whole block");
        }

        [Test]
        public void MQ03_TheColorBlockKeepsItsRules() => BlockRules(true);

        [Test]
        public void MQ04_AFloatBlockTakesTheSameRules() => BlockRules(false);

        // ---- one renderer's copies

        void TwoInstances(TargetItem a, TargetItem b, string what)
        {
            foreach (bool aFirst in new[] { true, false })
            {
                FeedbackScheduler s = Sched();
                MeshRenderer r = Cube("Two");
                string originals = Mats(r);
                int before = Copies();
                a.m_Target = Direct(r);
                b.m_Target = Direct(r);
                FeedbackPlayer pa = Player(s, a);
                FeedbackPlayer pb = Player(s, b);
                pa.Play();
                pb.Play();
                Tick(s, 6);
                Assert.That(r.sharedMaterial.name, Does.EndWith("(Feedback)"), what + " — a copy during the effect");
                Assert.That(r.sharedMaterial.name, Does.Not.EndWith("(Feedback) (Feedback)"), what + " — one copy, not a copy of a copy");
                (aFirst ? pa : pb).ReleaseRuntime();
                Tick(s, 2);
                (aFirst ? pb : pa).ReleaseRuntime();
                Tick(s, 2);
                Assert.That((Mats(r), Copies() - before), Is.EqualTo((originals, 0)), what + (aFirst ? " — the first made let go first" : " — the last made let go first"));
            }
        }

        // two instance properties on one renderer — the first made let go first does not leave the renderer's material null
        [Test]
        public void MQ05a_TwoInstancePropertiesGiveTheOriginalsBack()
        {
            foreach (bool direct in new[] { false, true })
            {
                var color = On(new MaterialColorItem { m_WriteMode = MaterialWriteMode.Instance }, null, TweenTo(new Vector4(1f, 0f, 0f, 1f)), 0.2f);
                var emission = On(new MaterialColorItem { m_WriteMode = MaterialWriteMode.Instance, m_PropertyName = "_EmissionColor" }, null, TweenTo(new Vector4(0f, 1f, 0f, 1f)), 0.2f);
                emission.m_DirectWrite = direct;
                TwoInstances(color, emission, direct ? "shared and private" : "shared");
            }
        }

        // the whole renderer's instances over a material of another shader — its read logs no error
        [Test]
        public void MQ05b_AnotherShadersMaterialIsSkipped()
        {
            FeedbackScheduler s = Sched();
            MeshRenderer r = Cube("Mixed", 2, Shader.Find("Sprites/Default"));
            Assert.That(r.sharedMaterials[1].HasColor(s_Emission), Is.False, "the fixture: the second lacks the property");
            Material sprite = r.sharedMaterials[1];
            Player(s, On(new MaterialColorItem { m_WriteMode = MaterialWriteMode.Instance, m_PropertyName = "_EmissionColor" }, r, PunchBy(0.2f), 0.3f)).Play();
            Tick(s, 3);
            Assert.That(r.sharedMaterials[1], Is.SameAs(sprite), "no copy of the material without the property");
            Assert.That(r.sharedMaterials[0].name, Does.EndWith("(Feedback)"));
            Tick(s, 6);
            LogAssert.NoUnexpectedReceived();
        }

        // the property items share the copies too · an index slot copies its index · the game's material put in during the effect
        // stays (only our copy is taken back)
        [Test]
        public void MQ05c_TheCopiesAreTheRenderers()
        {
            foreach (bool direct in new[] { false, true })
            {
                var color = On(new MaterialColorItem { m_WriteMode = MaterialWriteMode.Instance }, null, TweenTo(new Vector4(1f, 0f, 0f, 1f)), 0.2f);
                var cutoff = On(new MaterialFloatItem { m_PropertyName = "_Cutoff", m_WriteMode = MaterialWriteMode.Instance }, null, TweenTo(new Vector4(0.9f, 0f, 0f, 0f)), 0.2f);
                cutoff.m_DirectWrite = direct;
                TwoInstances(color, cutoff, direct ? "color and a private float" : "color and float");
            }

            FeedbackScheduler s = Sched();
            MeshRenderer r = Cube("IndexOnly", 2);
            Material first = r.sharedMaterials[0];
            Player(s, On(new MaterialColorItem { m_WriteMode = MaterialWriteMode.Instance, m_MaterialIndex = 1 }, r, TweenTo(new Vector4(1f, 0f, 0f, 1f)), 0.2f)).Play();
            Tick(s, 3);
            Assert.That((r.sharedMaterials[0] == first, r.sharedMaterials[1].name.EndsWith("(Feedback)", StringComparison.Ordinal)), Is.EqualTo((true, true)), "index 1 only");

            FeedbackScheduler s2 = Sched();
            MeshRenderer gr = Cube("GameMaterial");
            var game = new Material(gr.sharedMaterial) { name = "GameMaterial-game" };
            m_Made.Add(game);
            FeedbackPlayer gp = Player(s2, On(new MaterialColorItem { m_WriteMode = MaterialWriteMode.Instance }, gr, TweenTo(new Vector4(1f, 0f, 0f, 1f)), 0.5f));
            gp.Play();
            Tick(s2, 3);
            gr.sharedMaterial = game;
            Tick(s2, 2);
            gp.ReleaseRuntime();
            Tick(s2, 2);
            Assert.That(gr.sharedMaterial, Is.SameAs(game), "the game's material stays");
        }

        // ---- Material Float · Vector

        void PersistBack(string what, MaterialWriteMode mode, Func<MeshRenderer, FeedbackItem> item, Func<MeshRenderer, string> read)
        {
            foreach (bool midway in new[] { true, false })
            {
                FeedbackScheduler s = Sched();
                MeshRenderer r = Cube("Persist", 1, null, Props());
                string before = read(r);
                FeedbackPlayer p = Player(s, item(r), Keeper());
                p.Play();
                Tick(s, midway ? 8 : 30);
                Assert.That(read(r), Is.Not.EqualTo(before), what + " " + mode + " — moved (the positive)");
                if (midway)
                    p.Stop(StopMode.Revert);
                else
                    p.Restore();
                s.Tick(0f);
                Assert.That(read(r), Is.EqualTo(before), what + " " + mode + (midway ? " — the Revert stop" : " — the Restore"));
            }
        }

        // an instance's value is read from the copy the renderer holds — the copy goes with the slot, so by value
        static string FloatOf(Renderer r, string name, MaterialWriteMode mode)
        {
            if (mode == MaterialWriteMode.Instance)
                return B(r.sharedMaterial.GetFloat(name));
            MaterialPropertyBlock b = Block(r);
            return b.HasFloat(name) ? "block " + B(b.GetFloat(name)) : B(r.sharedMaterial.GetFloat(name));
        }

        static string VectorOf(Renderer r, string name, MaterialWriteMode mode)
        {
            if (mode == MaterialWriteMode.Instance)
                return BV(r.sharedMaterial.GetVector(name));
            MaterialPropertyBlock b = Block(r);
            return b.HasVector(name) ? "block " + BV(b.GetVector(name)) : BV(r.sharedMaterial.GetVector(name));
        }

        [Test]
        public void MQ06a_FloatAndVectorComeBackBitForBit()
        {
            foreach (MaterialWriteMode mode in new[] { MaterialWriteMode.PropertyBlock, MaterialWriteMode.Instance })
            {
                PersistBack("float", mode, r => On(new MaterialFloatItem { m_PropertyName = "_F", m_WriteMode = mode }, r, TweenTo(new Vector4(5f, 0f, 0f, 0f)), 1f), r => FloatOf(r, "_F", mode));
                PersistBack("vector", mode, r => On(new MaterialVectorItem { m_PropertyName = "_V", m_WriteMode = mode }, r, TweenTo(new Vector4(5f, 6f, 7f, 8f)), 1f), r => VectorOf(r, "_V", mode));
                FeedbackScheduler s = Sched();
                MeshRenderer pr = Cube("Punch", 1, null, Props());
                string start = FloatOf(pr, "_F", mode);
                Player(s, On(new MaterialFloatItem { m_PropertyName = "_F", m_WriteMode = mode }, pr, PunchBy(2f), 0.5f)).Play();
                float peak = 0f;
                for (int i = 0; i < 15; i++)
                {
                    s.Tick(Step);
                    peak = Mathf.Max(peak, mode == MaterialWriteMode.Instance ? pr.sharedMaterial.GetFloat("_F") : Block(pr).GetFloat("_F"));
                }
                s.Tick(0f);
                Assert.That(peak, Is.GreaterThan(2.5f), "the punch's peak " + mode);
                Assert.That(FloatOf(pr, "_F", mode), Is.EqualTo(start), "the punch ends at the base " + mode);
            }
        }

        // Float · Range (not clamped) · a Vector take; an empty name · an Integer (SetFloat logs and writes nothing) · a
        // Color for a vector (a block's SetVector skips the linear conversion) are refused with their reasons
        [Test]
        public void MQ06b_TheFormsThePrepareTakes()
        {
            Assert.That(Prepared(new MaterialFloatItem { m_PropertyName = "_F" }, Props()), Is.True, "a Float");
            Assert.That(Prepared(new MaterialFloatItem { m_PropertyName = "_R" }, Props()), Is.True, "a Range");
            Assert.That(Prepared(new MaterialVectorItem { m_PropertyName = "_V" }, Props()), Is.True, "a Vector");
            LogAssert.Expect(LogType.Warning, new Regex("name the float property"));
            Assert.That(Prepared(new MaterialFloatItem(), Props()), Is.False, "an empty name");
            LogAssert.Expect(LogType.Warning, new Regex("the property _N is an Integer, not a float"));
            Assert.That(Prepared(new MaterialFloatItem { m_PropertyName = "_N" }, Props()), Is.False, "an Integer");
            LogAssert.Expect(LogType.Warning, new Regex("the property _C is a Color, not a vector"));
            Assert.That(Prepared(new MaterialVectorItem { m_PropertyName = "_C" }, Props()), Is.False, "a Color for a vector");
            LogAssert.Expect(LogType.Warning, new Regex("the property _V is a Vector, not a float"));
            Assert.That(Prepared(new MaterialFloatItem { m_PropertyName = "_V" }, Props()), Is.False, "a Vector for a float");
            LogAssert.Expect(LogType.Warning, new Regex("the material has no property _Nope"));
            Assert.That(Prepared(new MaterialVectorItem { m_PropertyName = "_Nope" }, Props()), Is.False, "none");

            FeedbackScheduler s = Sched();
            MeshRenderer r = Cube("Range", 1, null, Props());
            Player(s, On(new MaterialFloatItem { m_PropertyName = "_R", m_WriteMode = MaterialWriteMode.Instance }, r, TweenTo(new Vector4(5f, 0f, 0f, 0f)), 0.5f)).Play();
            Tick(s, 15);
            Assert.That(r.sharedMaterial.GetFloat("_R"), Is.EqualTo(5f), "a Range is not clamped");
            LogAssert.NoUnexpectedReceived();
        }

        // ---- a texture's two halves on one _ST slot

        static string ST(Renderer r, MaterialWriteMode mode)
        {
            string texture = TextureST.MainTexture(r.sharedMaterial.shader);
            if (mode == MaterialWriteMode.Instance)
            {
                Vector2 scale = r.sharedMaterial.GetTextureScale(texture);
                Vector2 offset = r.sharedMaterial.GetTextureOffset(texture);
                return new Vector4(scale.x, scale.y, offset.x, offset.y).ToString("F2");
            }
            MaterialPropertyBlock b = Block(r);
            int st = Shader.PropertyToID(texture + "_ST");
            return (b.HasVector(st) ? b.GetVector(st) : r.sharedMaterial.GetVector(st)).ToString("F2");
        }

        [Test]
        public void MQ07a_BothHalvesOnOneTexture()
        {
            foreach (MaterialWriteMode mode in new[] { MaterialWriteMode.PropertyBlock, MaterialWriteMode.Instance })
            {
                FeedbackScheduler s = Sched();
                MeshRenderer r = Cube("Halves");
                Assert.That(ST(r, mode), Is.EqualTo(new Vector4(1f, 1f, 0f, 0f).ToString("F2")), "the fixture's ST");
                Player(s, On(new TextureOffsetItem { m_WriteMode = mode }, r, TweenTo(new Vector4(0.5f, 0.25f, 0f, 0f)), 0.2f),
                    On(new TextureScaleItem { m_WriteMode = mode }, r, TweenTo(new Vector4(2f, 3f, 0f, 0f)), 0.4f)).Play();
                Tick(s, 20);
                Assert.That(ST(r, mode), Is.EqualTo(new Vector4(2f, 3f, 0.5f, 0.25f).ToString("F2")), mode + " — each its half (zw the offset, xy the scale)");
            }
        }

        // two players — a Revert stop of the offset leaves the scale's Persist (the remembered value over the other half now)
        [Test]
        public void MQ07b_ARevertLeavesTheOtherHalf()
        {
            foreach (MaterialWriteMode mode in new[] { MaterialWriteMode.PropertyBlock, MaterialWriteMode.Instance })
            {
                FeedbackScheduler s = Sched();
                MeshRenderer r = Cube("TwoPlayers");
                FeedbackPlayer offset = Player(s, On(new TextureOffsetItem { m_WriteMode = mode }, r, TweenTo(new Vector4(0.5f, 0.25f, 0f, 0f)), 1f));
                FeedbackPlayer scale = Player(s, On(new TextureScaleItem { m_WriteMode = mode }, r, TweenTo(new Vector4(2f, 3f, 0f, 0f)), 0.2f));
                offset.Play();
                scale.Play();
                Tick(s, 8);
                offset.Stop(StopMode.Revert);
                s.Tick(0f);
                Assert.That(ST(r, mode), Is.EqualTo(new Vector4(2f, 3f, 0f, 0f).ToString("F2")), mode + " — the offset's revert");
            }
        }

        // an empty name is the main texture — the shader's [MainTexture], else _MainTex (the engine's mainTexture)
        [Test]
        public void MQ07c_TheMainTexture()
        {
            Assert.That(TextureST.MainTexture(Shader.Find("Standard")), Is.EqualTo("_MainTex"));
            Assert.That(TextureST.MainTexture(Shader.Find("Unlit/Texture")), Is.EqualTo("_MainTex"));
            var flagged = ShaderUtil.CreateShaderAsset("Shader \"Hidden/MaterialTargetsMain\" { Properties { _A(\"A\", 2D) = \"white\" {}  [MainTexture] _B(\"B\", 2D) = \"white\" {}  _MainTex(\"M\", 2D) = \"white\" {} } SubShader { Pass { } } }");
            m_Made.Add(flagged);
            Assert.That(TextureST.MainTexture(flagged), Is.EqualTo("_B"), "the flag first");
            var material = new Material(flagged);
            m_Made.Add(material);
            material.SetTextureOffset("_B", new Vector2(0.25f, 0f));
            Assert.That(material.mainTextureOffset, Is.EqualTo(new Vector2(0.25f, 0f)), "the engine's main texture is the flagged one");
        }

        // URP Lit's main texture is _BaseMap
        [Test, Category(OptionalAssemblyTests.DevProjectCategory)]
        public void MQ07d_URPsMainTexture()
        {
            Assert.That(TextureST.MainTexture(Shader.Find("Universal Render Pipeline/Lit")), Is.EqualTo("_BaseMap"));
        }

        // ---- Emission — the color × the multiplier, the keyword the material's

        static Color EmissionOf(Renderer r, MaterialWriteMode mode, Color start)
            => mode == MaterialWriteMode.Instance ? r.sharedMaterial.GetColor(s_Emission) : (Block(r).HasColor(s_Emission) ? Block(r).GetColor(s_Emission) : start);

        float Peak(MaterialWriteMode mode, FeedbackMotion motion, float k, out bool endBits)
        {
            FeedbackScheduler s = Sched();
            MeshRenderer r = Cube("Peak");
            Color start = r.sharedMaterial.GetColor(s_Emission);
            Player(s, On(new EmissionItem { m_WriteMode = mode, m_Multiplier = k }, r, motion, 0.5f)).Play();
            float peak = float.MinValue;
            for (int i = 0; i < 20; i++)
            {
                s.Tick(Step);
                peak = Mathf.Max(peak, EmissionOf(r, mode, start).r);
            }
            s.Tick(0f);
            endBits = BC(EmissionOf(r, mode, start)) == BC(start);
            return peak;
        }

        [Test]
        public void MQ08a_TheEmissionTimesTheMultiplier()
        {
            foreach (MaterialWriteMode mode in new[] { MaterialWriteMode.PropertyBlock, MaterialWriteMode.Instance })
            {
                FeedbackScheduler s = Sched();
                MeshRenderer r = Cube("Tween");
                bool keyword = r.sharedMaterial.IsKeywordEnabled("_EMISSION");
                Player(s, On(new EmissionItem { m_WriteMode = mode, m_Multiplier = 2f }, r, TweenTo(new Vector4(1f, 0.5f, 0f, 0.25f)), 0.5f)).Play();
                Tick(s, 15);
                Color e = EmissionOf(r, mode, Color.black);
                Assert.That(new[] { e.r, e.g, e.b, e.a }, Is.EqualTo(new[] { 2f, 1f, 0f, 0.25f }).Within(1e-4f), mode + " — the rgb × 2, the alpha the motion's");
                Assert.That(r.sharedMaterial.IsKeywordEnabled("_EMISSION"), Is.EqualTo(keyword), mode + " — the keyword as it was");
                var flash = new FlashMotion();
                flash.m_Params.m_Target = new Vector4(1f, 0f, 0f, 1f);
                Peak(mode, flash, 3f, out bool flashEnd);
                Peak(mode, new BlinkMotion(), 3f, out bool blinkEnd);
                Assert.That((flashEnd, blinkEnd), Is.EqualTo((true, true)), mode + " — the flash and the blink end at the base");
            }
        }

        // an override's target takes the multiplier too — the flash's and the blink's peaks, the oscillation's
        [Test]
        public void MQ08b_TheOverridesTakeTheMultiplier()
        {
            foreach (MaterialWriteMode mode in new[] { MaterialWriteMode.PropertyBlock, MaterialWriteMode.Instance })
            {
                var flash = new FlashMotion();
                flash.m_Params.m_Target = new Vector4(1f, 0f, 0f, 1f);
                Assert.That(Peak(mode, flash, 3f, out _), Is.GreaterThan(2.9f), mode + " flash");
                Assert.That(Peak(mode, new BlinkMotion(), 3f, out _), Is.GreaterThan(2.9f), mode + " blink");
                Assert.That(Peak(mode, Oscillate(1f), 3f, out _), Is.GreaterThan(2.9f), mode + " oscillate");
            }
            LogAssert.Expect(LogType.Warning, new Regex("the multiplier is not above 0"));
            Assert.That(Prepared(new EmissionItem { m_Multiplier = 0f }, null), Is.False, "a multiplier of 0");
        }

        // ---- Material Swap

        [Test]
        public void MQ09a_TheSwapComesBack()
        {
            FeedbackScheduler s = Sched();
            MeshRenderer r = Cube("Swap", 2);
            string originals = Mats(r);
            var swapIn = new Material(r.sharedMaterial) { name = "Swap-in" };
            m_Made.Add(swapIn);
            FeedbackPlayer p = Player(s, On(new MaterialSwapItem { m_Material = swapIn, m_MaterialIndex = 1 }, r, new SetMotion(), 0f), Keeper());
            p.Play();
            Tick(s, 2);
            Assert.That(r.sharedMaterials[1], Is.SameAs(swapIn), "set");
            p.Stop(StopMode.Revert);
            s.Tick(0f);
            Assert.That(Mats(r), Is.EqualTo(originals), "the Revert stop");
            p.Play();
            Tick(s, 30);
            p.Play(PlayArgs.Default.WithDirection(PlayerDirection.Backward));
            Tick(s, 30);
            Assert.That(Mats(r), Is.EqualTo(originals), "backward");
            p.Play();
            Tick(s, 30);
            Assert.That(r.sharedMaterials[1], Is.SameAs(swapIn), "set again");
            p.Restore();
            s.Tick(0f);
            Assert.That(Mats(r), Is.EqualTo(originals), "the Restore");

            FeedbackScheduler s2 = Sched();
            MeshRenderer all = Cube("SwapAll", 3);
            Player(s2, On(new MaterialSwapItem { m_Material = swapIn, m_MaterialIndex = -1 }, all, new SetMotion(), 0f)).Play();
            Tick(s2, 2);
            Assert.That(all.sharedMaterials.All(m => m == swapIn), Is.True, "−1 is every index");
            LogAssert.Expect(LogType.Warning, new Regex("no material to swap in"));
            Assert.That(Player(Sched(), On(new MaterialSwapItem(), Cube("SwapEmpty"), new SetMotion(), 0f)).Runtime.Binding(0), Is.Null, "no material");
            LogAssert.Expect(LogType.Warning, new Regex("material index out of range"));
            Assert.That(Player(Sched(), On(new MaterialSwapItem { m_Material = swapIn, m_MaterialIndex = 3 }, Cube("SwapPast"), new SetMotion(), 0f)).Runtime.Binding(0), Is.Null, "past the materials");
        }

        // with an instance on the same index — a start that is the copies' copy is its original, in every order
        [Test]
        public void MQ09b_TheSwapTakesTheCopysOriginal()
        {
            foreach (string order in new[] { "instance, swap, drop instance, swap back", "swap, instance, drop instance, swap back", "swap, instance, swap back, drop instance", "instance, swap, swap back, drop instance" })
            {
                FeedbackScheduler s = Sched();
                MeshRenderer r = Cube("SwapOrder");
                string originals = Mats(r);
                var swapIn = new Material(r.sharedMaterial) { name = "SwapOrder-in" };
                m_Made.Add(swapIn);
                FeedbackPlayer instance = Player(s, On(new MaterialColorItem { m_WriteMode = MaterialWriteMode.Instance, m_MaterialIndex = 0 }, r, TweenTo(new Vector4(1f, 0f, 0f, 1f)), 0.2f));
                FeedbackPlayer swap = Player(s, On(new MaterialSwapItem { m_Material = swapIn }, r, new SetMotion(), 0f));
                foreach (string step in order.Split(new[] { ", " }, StringSplitOptions.None))
                {
                    switch (step)
                    {
                        case "instance":
                            instance.Play();
                            Tick(s, 6);
                            break;
                        case "swap":
                            swap.Play();
                            Tick(s, 1);
                            break;
                        case "drop instance":
                            instance.ReleaseRuntime();
                            Tick(s, 2);
                            break;
                        default:
                            swap.Restore();
                            s.Tick(0f);
                            break;
                    }
                }
                Assert.That(Mats(r), Is.EqualTo(originals), order);
            }
        }

        // ---- Renderer Visibility — the toggle's rules

        [Test]
        public void MQ10_TheVisibility()
        {
            FeedbackScheduler s = Sched();
            MeshRenderer r = Cube("Visible");
            FeedbackPlayer p = Player(s, On(new RendererVisibilityItem(), r, new SetMotion(), 0f), Keeper());
            p.Play();
            Tick(s, 2);
            Assert.That(r.enabled, Is.False, "Set 0 turns it off");
            p.Stop(StopMode.Revert);
            s.Tick(0f);
            Assert.That(r.enabled, Is.True, "the Revert stop");
            p.Play();
            Tick(s, 30);
            p.Restore();
            s.Tick(0f);
            Assert.That(r.enabled, Is.True, "the Restore");

            FeedbackScheduler s2 = Sched();
            MeshRenderer b = Cube("Blinked");
            Player(s2, On(new RendererVisibilityItem(), b, BlinkOff(), 0.5f)).Play();
            bool off = false;
            for (int i = 0; i < 20; i++)
            {
                s2.Tick(Step);
                off |= !b.enabled;
            }
            s2.Tick(0f);
            Assert.That((off, b.enabled), Is.EqualTo((true, true)), "the blink turned it off and ends on");

            // the game turns it off while the blink shows it — its off is the new before (an off frame's is ours to the slot)
            FeedbackScheduler s3 = Sched();
            MeshRenderer g = Cube("GameOff");
            Player(s3, On(new RendererVisibilityItem(), g, BlinkOff(), 0.6f)).Play();
            int at = -1;
            for (int i = 0; i < 12 && at < 0; i++)
            {
                s3.Tick(Step);
                if (g.enabled && i > 1)
                    at = i;
            }
            Assert.That(at, Is.GreaterThan(0), "a shown frame inside the blink");
            g.enabled = false;
            Tick(s3, 20);
            s3.Tick(0f);
            Assert.That(g.enabled, Is.False, "the game's off stays");
        }
    }
}
