using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using xpTURN.Feedback.Editor;
using Object = UnityEngine.Object;
using F = xpTURN.Feedback.Tests.Editor.ValidationFixtures;

namespace xpTURN.Feedback.Tests.Editor
{
    // group F over the material property items (Material Color · Float · Vector · Texture Offset · Scale · Emission) · F08 ·
    // F09 · F11 in their cases only · E05 takes the visibility's and the emission's blinks. The shaders are the built-in ones
    // (the minimal project has no URP)
    public sealed class MaterialRulesTests
    {
        const string PropsShader = "Shader \"Hidden/MaterialRulesProps\" { Properties { _F(\"F\", Float) = 1  _R(\"R\", Range(0, 1)) = 0.5  _N(\"N\", Integer) = 3  _V(\"V\", Vector) = (1,2,3,4)  _C(\"C\", Color) = (1,0,0,1) } SubShader { Pass { } } }";

        readonly List<Object> m_Made = new List<Object>();
        Shader m_Props;

        [TearDown]
        public void TearDown()
        {
            foreach (Object o in m_Made)
            {
                if (o != null)
                    Object.DestroyImmediate(o);
            }
            m_Made.Clear();
            m_Props = null;
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

        Material Mat(Shader shader)
        {
            var m = new Material(shader);
            m_Made.Add(m);
            return m;
        }

        Material Standard() => Mat(Shader.Find("Standard"));

        Renderer Cube(params Material[] materials)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            m_Made.Add(go);
            var r = go.GetComponent<Renderer>();
            r.sharedMaterials = materials;
            return r;
        }

        FeedbackPlayer Host(params FeedbackItem[] items)
        {
            var go = new GameObject("Player");
            m_Made.Add(go);
            var host = go.AddComponent<FeedbackPlayer>();
            host.m_Items.AddRange(items);
            return host;
        }

        static T On<T>(T item, Object target, FeedbackMotion motion = null) where T : TargetItem
        {
            item.m_Target = new TargetRef { m_Mode = TargetRefMode.Direct, m_Object = target, m_Path = "", m_Anchor = "" };
            item.m_Motion = motion ?? new TweenMotion { m_Params = TweenParams.Default };
            return item;
        }

        static BlinkMotion Blink(float period) => new BlinkMotion { m_Params = new BlinkParams { m_Value = Vector4.zero, m_Period = period, m_Duty = 0.5f, m_StartOn = true } };

        static TweenMotion Tween(TweenMode mode, float from, float to)
        {
            TweenParams p = TweenParams.Default;
            p.m_Mode = mode;
            p.m_From = new Vector4(from, 0f, 0f, 0f);
            p.m_To = new Vector4(to, 0f, 0f, 0f);
            return new TweenMotion { m_Params = p };
        }

        static void Want(FeedbackPlayer host, string id, int[] items, RuleSeverity severity)
        {
            List<RuleResult> got = F.Of(host, id);
            Assert.That(got.Select(r => r.Item).OrderBy(i => i), Is.EqualTo(items), id);
            if (items.Length > 0)
                Assert.That(got.Select(r => r.Severity).Distinct(), Is.EqualTo(new[] { severity }), id);
        }

        static string Message(FeedbackPlayer host, string id, int item) => F.Of(host, id).Single(r => r.Item == item).Message;

        // F01 · F02 · F03 · F05 · F07 over the material property items — the sprite's rule stays the color's
        [Test]
        public void MQ15a_TheWidenedRules()
        {
            var sprite = new GameObject("Sprite").AddComponent<SpriteRenderer>();
            m_Made.Add(sprite.gameObject);
            Renderer shared = Cube(Standard());
            var blinks = new List<FeedbackItem>();
            for (int i = 0; i < ManyBlockBlinksRule.Count; i++)
            {
                EmissionItem e = On(new EmissionItem(), Cube(Standard()), Blink(0.4f));
                e.m_Timing.m_Duration = 1f;
                blinks.Add(e);
            }
            FeedbackPlayer host = Host(new FeedbackItem[]
            {
                On(new MaterialFloatItem { m_PropertyName = "_Cutoff" }, Cube(Standard())),                                                 // 0 F01 — a block's Persist
                On(new MaterialFloatItem { m_PropertyName = "_Cutoff", m_WriteMode = MaterialWriteMode.Instance }, Cube(Standard())),        // 1
                On(new MaterialFloatItem { m_PropertyName = "_Cutoff" }, sprite, new PunchMotion()),                                         // 2 — no F03
                On(new MaterialColorItem(), sprite, new PunchMotion()),                                                                       // 3 F03
                On(new EmissionItem(), shared, new PunchMotion()),                                                                            // 4
                On(new MaterialColorItem { m_PropertyName = "_EmissionColor", m_WriteMode = MaterialWriteMode.Instance }, shared, new PunchMotion()), // 5 F05
                On(new MaterialFloatItem { m_PropertyName = "_Cutoff", m_MaterialIndex = 5 }, Cube(Standard()), new PunchMotion()),          // 6 F07
            }.Concat(blinks).ToArray());
            Want(host, "FB-F01", new[] { 0 }, RuleSeverity.Info);
            Want(host, "FB-F02", Enumerable.Range(7, ManyBlockBlinksRule.Count).ToArray(), RuleSeverity.Info);
            Want(host, "FB-F03", new[] { 3 }, RuleSeverity.Warning);
            Want(host, "FB-F05", new[] { 5 }, RuleSeverity.Warning);
            Want(host, "FB-F07", new[] { 6 }, RuleSeverity.Warning);
            Assert.That(F.Of(host, "FB-F01").Single().Fixes.Select(f => f.Name), Is.EqualTo(new[] { "Write to material instances" }));
        }

        // F04: the values differ for a float · a material without the property (instances skip it, a block does
        // not show there) · the instances' whole and an index of one property share a copy
        [Test]
        public void MQ15b_TheWholeRenderer()
        {
            Material low = Standard();
            low.SetFloat("_Cutoff", 0.2f);
            Material sprite = Mat(Shader.Find("Sprites/Default"));
            Renderer both = Cube(Standard(), Standard());
            FeedbackPlayer host = Host(
                On(new MaterialFloatItem { m_PropertyName = "_Cutoff" }, Cube(Standard(), low), new PunchMotion()),                          // 0
                On(new EmissionItem { m_WriteMode = MaterialWriteMode.Instance }, Cube(Standard(), sprite), new PunchMotion()),              // 1
                On(new EmissionItem(), Cube(Standard(), sprite), new PunchMotion()),                                                          // 2
                On(new MaterialColorItem { m_WriteMode = MaterialWriteMode.Instance }, both, new PunchMotion()),                             // 3
                On(new MaterialColorItem { m_WriteMode = MaterialWriteMode.Instance, m_MaterialIndex = 0 }, both, new PunchMotion()),        // 4 — an index
                On(new MaterialFloatItem { m_PropertyName = "_Cutoff" }, Cube(Standard(), Standard()), new PunchMotion()));                  // 5 — the same
            Want(host, "FB-F04", new[] { 0, 1, 2, 3 }, RuleSeverity.Warning);
            Assert.That(new[] { 0, 1, 2, 3 }.Select(i => Message(host, "FB-F04", i)), Is.EqualTo(new[]
            {
                "The materials differ — the whole renderer starts from material 0",
                "Material 1 has no such property — its instance is skipped",
                "Material 1 has no such property — the block does not show there",
                "Item 4 writes the property at an index — it shares a copy with the whole renderer",
            }));
            Assert.That(F.Of(host, "FB-F04").First().Fixes.Select(f => f.Name), Is.EqualTo(new[] { "Use index 0", "Use index 1" }));
        }

        // F06: of the item's type — the fixes are the shader's properties of that type
        [Test]
        public void MQ15c_TheProperty()
        {
            FeedbackPlayer host = Host(
                On(new MaterialFloatItem(), Cube(Standard())),                                                // 0 an empty name
                On(new MaterialFloatItem { m_PropertyName = "_N" }, Cube(Mat(Props()))),                      // 1 an Integer
                On(new MaterialVectorItem { m_PropertyName = "_C" }, Cube(Mat(Props()))),                     // 2 a Color
                On(new TextureOffsetItem { m_PropertyName = "_MainTex_ST" }, Cube(Standard())),               // 3 the _ST
                On(new EmissionItem(), Cube(Mat(Shader.Find("Unlit/Color")))),                               // 4 no emission
                On(new MaterialFloatItem { m_PropertyName = "_Cutoff" }, Cube(Standard())),                   // 5
                On(new MaterialFloatItem { m_PropertyName = "_R" }, Cube(Mat(Props()))),                      // 6 a Range
                On(new MaterialVectorItem { m_PropertyName = "_V" }, Cube(Mat(Props()))),                     // 7
                On(new TextureScaleItem(), Cube(Standard())),                                                 // 8 the main texture
                On(new EmissionItem(), Cube(Standard())),                                                     // 9
                On(new EmissionItem { m_PropertyName = "_F" }, Cube(Mat(Props()))));                          // 10 a Float
            Want(host, "FB-F06", new[] { 0, 1, 2, 3, 4, 10 }, RuleSeverity.Warning);
            Assert.That(new[] { 0, 1, 2, 3, 4, 10 }.Select(i => Message(host, "FB-F06", i)), Is.EqualTo(new[]
            {
                "Name a float property — the item is skipped",
                "_N is an Integer, not a float — the item is skipped",
                "_C is a Color, not a vector — the item is skipped",
                "_MainTex_ST is the scale and offset of _MainTex — name the texture",
                "The shader has no property _EmissionColor — the item is skipped",
                "_F is a Float, not a color — the item is skipped",
            }));
            List<RuleResult> f06 = F.Of(host, "FB-F06");
            Assert.That(f06.Single(r => r.Item == 1).Fixes.Select(f => f.Name), Is.EqualTo(new[] { "Use _F", "Use _R" }), "Float and Range");
            Assert.That(f06.Single(r => r.Item == 2).Fixes.Select(f => f.Name), Is.EqualTo(new[] { "Use _V" }), "Vector");
            Assert.That(f06.Single(r => r.Item == 10).Fixes.Select(f => f.Name), Is.EqualTo(new[] { "Use _C" }), "Color");
            Assert.That(f06.Single(r => r.Item == 3).Fixes.First().Name, Is.EqualTo("Use _MainTex"), "the texture first");
            Assert.That(f06.Single(r => r.Item == 0).Fixes.Count, Is.InRange(1, MaterialTargets.MaxFixes));
        }

        // F08 · F09 · F11
        [Test]
        public void MQ15d_TheNewRules()
        {
            Material lit = Standard();
            lit.EnableKeyword("_EMISSION");
            Material swapIn = Standard();
            Renderer bare = Cube();
            FeedbackPlayer host = Host(
                On(new EmissionItem(), Cube(Standard()), new PunchMotion()),                                                   // 0 F08 — the keyword off
                On(new EmissionItem { m_WriteMode = MaterialWriteMode.Instance }, Cube(lit, Standard()), new PunchMotion()),  // 1 F08 — material 1
                On(new EmissionItem { m_MaterialIndex = 0 }, Cube(lit, Standard()), new PunchMotion()),                       // 2 — index 0 is on
                On(new EmissionItem(), Cube(Mat(Shader.Find("Unlit/Color"))), new PunchMotion()),                             // 3 — no keyword (F06's)
                On(new MaterialSwapItem(), Cube(Standard()), new SetMotion()),                                                 // 4 F09 — no material
                On(new MaterialSwapItem { m_Material = swapIn, m_MaterialIndex = 5 }, Cube(Standard()), new SetMotion()),     // 5 F09 — out of range
                On(new MaterialSwapItem { m_Material = swapIn }, bare, new SetMotion()),                                       // 6 F09 — no material on it
                On(new MaterialSwapItem { m_Material = swapIn, m_MaterialIndex = -1 }, Cube(Standard()), new SetMotion()),    // 7
                On(new MaterialFloatItem { m_PropertyName = "_R" }, Cube(Mat(Props())), Tween(TweenMode.To, 0f, 5f)),         // 8 F11
                On(new MaterialFloatItem { m_PropertyName = "_R" }, Cube(Mat(Props())), Tween(TweenMode.FromTo, -1f, 0.5f)),  // 9 F11 — the from
                On(new MaterialFloatItem { m_PropertyName = "_R" }, Cube(Mat(Props())), Tween(TweenMode.To, 0f, 0.5f)),       // 10
                On(new MaterialFloatItem { m_PropertyName = "_R" }, Cube(Mat(Props())), Tween(TweenMode.By, 0f, 5f)),         // 11 — relative
                On(new MaterialFloatItem { m_PropertyName = "_F" }, Cube(Mat(Props())), Tween(TweenMode.To, 0f, 5f)));        // 12 — a Float
            Want(host, "FB-F08", new[] { 0, 1 }, RuleSeverity.Warning);
            Want(host, "FB-F09", new[] { 4, 5, 6 }, RuleSeverity.Warning);
            Want(host, "FB-F11", new[] { 8, 9 }, RuleSeverity.Info);
            Assert.That(new[] { Message(host, "FB-F08", 0), Message(host, "FB-F08", 1) }, Is.EqualTo(new[]
            {
                "Material 0 has its emission off (_EMISSION) — nothing shows; turn it on in the material",
                "Material 1 has its emission off (_EMISSION) — nothing shows; turn it on in the material",
            }));
            Assert.That(new[] { 4, 5, 6 }.Select(i => Message(host, "FB-F09", i)), Is.EqualTo(new[]
            {
                "No material to swap in — the item is skipped",
                "Material index 5 is out of range (1) — the item is skipped",
                "The renderer has no material — the item is skipped",
            }));
            Assert.That(new[] { 8, 9 }.Select(i => Message(host, "FB-F11", i)), Is.EqualTo(new[]
            {
                "Tween value 5 is outside _R's range (0 ~ 1) — the shader takes it as it is",
                "Tween value -1 is outside _R's range (0 ~ 1) — the shader takes it as it is",
            }));
            List<RuleResult> f09 = F.Of(host, "FB-F09");
            Assert.That(f09.Select(r => r.Fixes.Count), Is.EqualTo(new[] { 0, 1, 0 }));
            RuleResult range = f09.Single(r => r.Item == 5);
            FeedbackValidator.Apply(range, range.Fixes.Single());
            Assert.That(((MaterialSwapItem)host.m_Items[5]).m_MaterialIndex, Is.EqualTo(-1));
            Want(host, "FB-F09", new[] { 4, 6 }, RuleSeverity.Warning);
        }

        // the flash limit takes the visibility's and the emission's fast blinks (both flash)
        [Test]
        public void MQ17_TheFlashLimitTakesTheNewBlinks()
        {
            FeedbackPlayer host = Host(
                On(new RendererVisibilityItem(), Cube(Standard()), Blink(0.1f)),        // 0
                On(new EmissionItem(), Cube(Standard()), Blink(0.1f)),                  // 1
                On(new RendererVisibilityItem(), Cube(Standard()), Blink(0.4f)));       // 2 — 2.5 a second
            Want(host, "FB-E05", new[] { 0, 1 }, RuleSeverity.Warning);
        }
    }
}
