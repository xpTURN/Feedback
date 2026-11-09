using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using TMPro;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;
using Perf = xpTURN.Feedback.Tests.PlayerPerformanceTests;

namespace xpTURN.Feedback.Tests
{
    // real frames on a canvas text. The truth is TMP's own record of a character (characterInfo — the glyph's corners and its
    // line) — the slot writes the mesh arrays only. Read after a frame: the canvas update (TMP's rebuild · our OnPreRenderText)
    // ran at that frame's end
    [PrebuildSetup("xpTURN.Feedback.Tests.Editor.FeedbackTestAssets")]
    [PostBuildCleanup("xpTURN.Feedback.Tests.Editor.FeedbackTestAssets")]
    public sealed class TMPCharacterPlayTests
    {
        const float Eps = 1e-3f;
        readonly List<FeedbackPlayer> m_Players = new List<FeedbackPlayer>();
        readonly List<Object> m_Made = new List<Object>();

        [SetUp]
        public void SetUp()
        {
            if (TMP_Settings.instance == null || TMP_Settings.defaultFontAsset == null)
                Assert.Ignore("TMP Essential Resources are not in the project");
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            foreach (FeedbackPlayer p in m_Players)
                if (p != null)
                    p.Stop(StopMode.Revert);
            foreach (Object o in m_Made)
                if (o != null)
                    Object.Destroy(o);
            m_Players.Clear();
            m_Made.Clear();
            FeedbackCategories.SetEnabled(FeedbackCategory.Motion, true);
            yield return null;
            Time.captureDeltaTime = 0f;
        }

        TextMeshProUGUI Text(string text)
        {
            var cgo = new GameObject("Canvas", typeof(RectTransform));
            m_Made.Add(cgo);
            cgo.AddComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var go = new GameObject("tmp", typeof(RectTransform));
            go.transform.SetParent(cgo.transform, false);
            ((RectTransform)go.transform).sizeDelta = new Vector2(600f, 80f);
            var t = go.AddComponent<TextMeshProUGUI>();
            t.text = text;
            return t;
        }

        FeedbackPlayer Player(GameObject go, TargetItem item, Object target, FeedbackMotion motion, float duration)
        {
            item.m_Target = new TargetRef { m_Mode = TargetRefMode.Direct, m_Object = target, m_Path = "", m_Anchor = "" };
            item.m_Motion = motion;
            item.m_Timing.m_Duration = duration;
            var p = go.AddComponent<FeedbackPlayer>();
            p.m_Items.Add(item);
            m_Players.Add(p);
            return p;
        }

        static TweenMotion TweenTo(Vector4 to)
        {
            TweenParams p = TweenParams.Default;
            p.m_To = to;
            return new TweenMotion { m_Params = p };
        }

        // a slow sine a quarter turn in — 0.99 ~ 1 of its amplitude for the first half second (the sine's own value, not the slot's)
        static OscillateMotion Crest(Vector4 a) => new OscillateMotion { m_Params = new OscillateParams { m_Amplitude = a, m_Frequency = 0.05f, m_Phase = 0.25f } };

        static bool AtCrest(float shift, float amplitude) => shift >= 0.985f * amplitude && shift <= 1.0005f * amplitude;

        static float Size(in TMP_CharacterInfo c) => c.ascender - c.descender;

        // the uploaded bottom-left corner minus TMP's own — characterInfo's order, a reverse sort mirrored
        static Vector3 Shift(TMP_Text t, int i)
        {
            TMP_CharacterInfo c = t.textInfo.characterInfo[i];
            Vector3[] v = MeshOf(t, c.materialReferenceIndex).vertices;
            int at = c.vertexIndex;
            if (t.geometrySortingOrder == VertexSortingOrder.Reverse)
                at = t.textInfo.meshInfo[c.materialReferenceIndex].vertexCount - 4 - at;
            return v[at] - c.vertex_BL.position;
        }

        static Mesh MeshOf(TMP_Text t, int material)
        {
            if (material == 0)
                return t.mesh;
            TMP_SubMeshUI[] subs = t.GetComponentsInChildren<TMP_SubMeshUI>(true);
            Material wanted = t.textInfo.meshInfo[material].material;
            foreach (TMP_SubMeshUI s in subs)
                if (s.sharedMaterial == wanted)
                    return s.mesh;
            return subs.Length > 0 ? subs[0].mesh : null;
        }

        // ---- a text change in the same frame keeps the effect — a running offset (a square wave — ± its amplitude) and a kept
        // one (a Tween's end)

        [UnityTest]
        public IEnumerator CW03_ChangeFramesKeepTheEffect([Values(false, true)] bool kept)
        {
            Time.captureDeltaTime = 1f / 60f;
            TextMeshProUGUI t = Text("Hello 0");
            yield return null;
            FeedbackPlayer p = kept
                ? Player(t.gameObject, new TMPCharacterOffsetItem { m_Stagger = 0f }, t, TweenTo(new Vector4(0f, 0.5f, 0f, 0f)), 0.05f)
                : Player(t.gameObject, new TMPCharacterOffsetItem { m_Stagger = 0f }, t, Crest(new Vector4(0f, 0.5f, 0f, 0f)), 10f);
            p.Play();
            for (int f = 0; f < 6; f++)
                yield return null;
            int changed = 0, changedOk = 0, quiet = 0, quietOk = 0;
            for (int f = 0; f < 20; f++)
            {
                bool change = (f & 1) == 0;
                // the first letter changes too (a descender's 'g' and a capital) — a stale copy would put the old glyph's quad there
                if (change)
                    t.SetText((f & 2) == 0 ? "gamma {0}" : "Hello {0}", f + 1);
                yield return null;
                TMP_CharacterInfo c = t.textInfo.characterInfo[0];
                Vector3 s0 = Shift(t, 0);
                bool ok = Mathf.Abs(s0.x) < Eps && (kept ? Mathf.Abs(s0.y - 0.5f * Size(c)) < Eps * Size(c) : AtCrest(s0.y, 0.5f * Size(c)));
                if (change)
                {
                    changed++;
                    changedOk += ok ? 1 : 0;
                }
                else
                {
                    quiet++;
                    quietOk += ok ? 1 : 0;
                }
            }
            Assert.That((changedOk, quietOk), Is.EqualTo((changed, quiet)), "the effect on change frames · quiet frames");
        }

        // ---- the end gives the original — a revert stop, a released player, a kept value reverted

        [UnityTest]
        public IEnumerator CW05_TheEndGivesTheOriginal([Values(0, 1, 2)] int way)
        {
            Time.captureDeltaTime = 1f / 60f;
            TextMeshProUGUI t = Text("Hello World");
            yield return null;
            FeedbackPlayer p = way == 2
                ? Player(t.gameObject, new TMPCharacterOffsetItem(), t, TweenTo(new Vector4(0f, 1f, 0f, 0f)), 0.05f)
                : Player(t.gameObject, new TMPCharacterOffsetItem(), t, Crest(new Vector4(0f, 0.5f, 0f, 0f)), 10f);
            if (way == 2)
            {
                // a punch on its position keeps the play running — a Revert stop has a play to stop and reaches the Tween that ended earlier
                var keeper = new PositionItem { m_Target = new TargetRef { m_Mode = TargetRefMode.Self, m_Path = "", m_Anchor = "" }, m_Motion = new PunchMotion { m_Params = new PunchParams { m_Amplitude = new Vector4(0.5f, 0.5f, 0.5f, 0f), m_Vibrato = 2f, m_Decay = 1f } } };
                keeper.m_Timing.m_Duration = 3f;
                p.m_Items.Add(keeper);
            }
            p.Play();
            for (int f = 0; f < 10; f++)
                yield return null;
            float moved = Mathf.Abs(Shift(t, 0).y);
            if (way == 1)
                p.ReleaseRuntime();
            else
                p.Stop(StopMode.Revert);
            yield return null;
            yield return null;
            int back = 0;
            for (int i = 0; i < t.textInfo.characterCount; i++)
                if (!t.textInfo.characterInfo[i].isVisible || Shift(t, i) == Vector3.zero)
                    back++;
            t.SetText("Hello Again");
            yield return null;
            Assert.That((moved > 1f, back, Shift(t, 0)), Is.EqualTo((true, t.textInfo.characterCount, Vector3.zero)), "moved · every character back bit for bit · no effect after a rebuild");
        }

        // ---- the channels — an offset in the character's size · a scale about its centre · an alpha multiplied (the rich text's kept)

        [UnityTest]
        public IEnumerator CW06_TheChannels()
        {
            Time.captureDeltaTime = 1f / 60f;
            TextMeshProUGUI a = Text("AB");
            TextMeshProUGUI b = Text("AB");
            TextMeshProUGUI c = Text("<alpha=#80>AB");
            yield return null;
            Player(a.gameObject, new TMPCharacterOffsetItem(), a, TweenTo(new Vector4(1f, 0f, 0f, 0f)), 0.05f).Play();
            Player(b.gameObject, new TMPCharacterScaleItem(), b, TweenTo(new Vector4(0.5f, 0f, 0f, 0f)), 0.05f).Play();
            Player(c.gameObject, new TMPCharacterAlphaItem(), c, TweenTo(new Vector4(0.5f, 0f, 0f, 0f)), 0.05f).Play();
            for (int f = 0; f < 10; f++)
                yield return null;
            TMP_CharacterInfo ca = a.textInfo.characterInfo[0], cb = b.textInfo.characterInfo[0], cc = c.textInfo.characterInfo[0];
            Vector3[] vb = b.mesh.vertices;
            float width = vb[cb.vertexIndex + 2].x - vb[cb.vertexIndex].x, width0 = cb.vertex_TR.position.x - cb.vertex_BL.position.x;
            Vector3 centre = (vb[cb.vertexIndex] + vb[cb.vertexIndex + 2]) * 0.5f, centre0 = (cb.vertex_BL.position + cb.vertex_TR.position) * 0.5f;
            byte alpha = c.mesh.colors32[cc.vertexIndex].a;
            Assert.That(Mathf.Abs(Shift(a, 0).x - Size(ca)) < Eps * Size(ca), Is.True, "the offset in the character's size");
            Assert.That((Mathf.Abs(width - 0.5f * width0) < Eps, (centre - centre0).magnitude < Eps), Is.EqualTo((true, true)), "half the width about the centre");
            Assert.That(alpha, Is.EqualTo((byte)64), "128 × 0.5 — the rich text's alpha kept");
        }

        // ---- one shared slot — three items on one text apply once each (an offset not doubled)

        [UnityTest]
        public IEnumerator CW07_OneSlotComposes()
        {
            Time.captureDeltaTime = 1f / 60f;
            TextMeshProUGUI t = Text("AB");
            yield return null;
            Player(t.gameObject, new TMPCharacterOffsetItem(), t, TweenTo(new Vector4(0f, 1f, 0f, 0f)), 0.05f).Play();
            Player(t.gameObject, new TMPCharacterScaleItem(), t, TweenTo(new Vector4(0.5f, 0f, 0f, 0f)), 0.05f).Play();
            Player(t.gameObject, new TMPCharacterAlphaItem(), t, TweenTo(new Vector4(0.5f, 0f, 0f, 0f)), 0.05f).Play();
            for (int f = 0; f < 10; f++)
                yield return null;
            TMP_CharacterInfo c = t.textInfo.characterInfo[0];
            Vector3[] v = t.mesh.vertices;
            Vector3 centre = (v[c.vertexIndex] + v[c.vertexIndex + 2]) * 0.5f, centre0 = (c.vertex_BL.position + c.vertex_TR.position) * 0.5f;
            float width = v[c.vertexIndex + 2].x - v[c.vertexIndex].x, width0 = c.vertex_TR.position.x - c.vertex_BL.position.x;
            Assert.That((Mathf.Abs(centre.y - centre0.y - Size(c)) < Eps * Size(c), Mathf.Abs(width - 0.5f * width0) < Eps, t.mesh.colors32[c.vertexIndex].a),
                Is.EqualTo((true, true, (byte)128)), "moved once · half the width · half the alpha");
        }

        // ---- the stagger — the first character ahead of the last mid-way, all at the end (Backward the other way)

        [UnityTest]
        public IEnumerator CW08_TheStagger([Values(false, true)] bool backward)
        {
            Time.captureDeltaTime = 1f / 60f;
            TextMeshProUGUI t = Text("ABCDE");
            yield return null;
            var item = new TMPCharacterOffsetItem { m_Stagger = 0.5f, m_Order = backward ? TMPCharacterOrder.Backward : TMPCharacterOrder.Forward };
            FeedbackPlayer p = Player(t.gameObject, item, t, TweenTo(new Vector4(0f, 1f, 0f, 0f)), 1f);
            p.Play();
            for (int f = 0; f < 30; f++)
                yield return null;
            float size = Size(t.textInfo.characterInfo[0]);
            float first = Shift(t, 0).y / size, last = Shift(t, 4).y / size;
            for (int f = 0; f < 40; f++)
                yield return null;
            float firstEnd = Shift(t, 0).y / size, lastEnd = Shift(t, 4).y / size;
            float ahead = backward ? last : first, behind = backward ? first : last;
            Assert.That((ahead > 0.8f, behind < 0.2f), Is.EqualTo((true, true)), $"mid-way {first:0.00} · {last:0.00}");
            Assert.That((Mathf.Abs(firstEnd - 1f) < Eps, Mathf.Abs(lastEnd - 1f) < Eps), Is.EqualTo((true, true)), "every character at the end inside the item");
        }

        // ---- tweens one after another on a channel — a fade out kept (a text change too), then a fade in brings it back

        [UnityTest]
        public IEnumerator CW09_TweensInTurn()
        {
            Time.captureDeltaTime = 1f / 60f;
            TextMeshProUGUI t = Text("Hello");
            yield return null;
            FeedbackPlayer outP = Player(t.gameObject, new TMPCharacterAlphaItem { m_Stagger = 0f }, t, TweenTo(Vector4.zero), 0.05f);
            outP.Play();
            for (int f = 0; f < 10; f++)
                yield return null;
            byte faded = t.mesh.colors32[t.textInfo.characterInfo[0].vertexIndex].a;
            t.SetText("Hello again");
            yield return null;
            byte fadedAfterChange = t.mesh.colors32[t.textInfo.characterInfo[0].vertexIndex].a;
            FeedbackPlayer inP = Player(t.gameObject, new TMPCharacterAlphaItem { m_Stagger = 0f }, t, TweenTo(Vector4.one), 0.05f);
            inP.Play();
            for (int f = 0; f < 10; f++)
                yield return null;
            byte back = t.mesh.colors32[t.textInfo.characterInfo[0].vertexIndex].a;
            Assert.That((faded, fadedAfterChange, back), Is.EqualTo(((byte)0, (byte)0, (byte)255)), "kept 0 · through a rebuild · the next tween brings it back");
        }

        // ---- the gate — a category off leaves the item out of new plays (the framework); turned off in the middle of a play the
        // running offset and Persist records stop writing at once (the text back to its kept value) · the alpha (no category) goes
        // on

        [UnityTest]
        public IEnumerator CW10_TheGate()
        {
            Time.captureDeltaTime = 1f / 60f;
            TextMeshProUGUI a = Text("AB");
            TextMeshProUGUI b = Text("AB");
            TextMeshProUGUI c = Text("AB");
            yield return null;
            Player(a.gameObject, new TMPCharacterOffsetItem(), a, Crest(new Vector4(0f, 0.5f, 0f, 0f)), 10f).Play();
            Player(b.gameObject, new TMPCharacterScaleItem(), b, TweenTo(new Vector4(0.5f, 0f, 0f, 0f)), 2f).Play();
            Player(c.gameObject, new TMPCharacterAlphaItem(), c, TweenTo(new Vector4(0.5f, 0f, 0f, 0f)), 0.05f).Play();
            for (int f = 0; f < 20; f++)
                yield return null;
            TMP_CharacterInfo cb = b.textInfo.characterInfo[0];
            float width0 = cb.vertex_TR.position.x - cb.vertex_BL.position.x;
            float Width()
            {
                Vector3[] vb = b.mesh.vertices;
                return vb[cb.vertexIndex + 2].x - vb[cb.vertexIndex].x;
            }
            bool movedBefore = Shift(a, 0) != Vector3.zero && Width() < width0 - Eps;
            FeedbackCategories.SetEnabled(FeedbackCategory.Motion, false);
            yield return null;
            yield return null;
            Assert.That((movedBefore, Shift(a, 0), Mathf.Abs(Width() - width0) < Eps, c.mesh.colors32[c.textInfo.characterInfo[0].vertexIndex].a),
                Is.EqualTo((true, Vector3.zero, true, (byte)128)), "moved · off: no offset · the Persist scale back · the alpha written");
        }

        // ---- the category's scale multiplies an offset

        [UnityTest]
        public IEnumerator CW10b_TheScale()
        {
            Time.captureDeltaTime = 1f / 60f;
            TextMeshProUGUI a = Text("AB");
            yield return null;
            FeedbackCategories.SetScale(FeedbackCategory.Motion, 0.5f);
            try
            {
                Player(a.gameObject, new TMPCharacterOffsetItem { m_Stagger = 0f }, a, Crest(new Vector4(0f, 0.5f, 0f, 0f)), 10f).Play();
                for (int f = 0; f < 10; f++)
                    yield return null;
                TMP_CharacterInfo c = a.textInfo.characterInfo[0];
                Assert.That(AtCrest(Shift(a, 0).y, 0.25f * Size(c)), Is.True, $"half of the crest — {Shift(a, 0).y / Size(c):0.0000}");
            }
            finally
            {
                FeedbackCategories.SetScale(FeedbackCategory.Motion, 1f);
            }
        }

        // ---- a space's vertexIndex is 0 (the first character's) — with a stagger each character has its own value, a space
        // written would put its share on the first character · a hidden character stays size 0

        [UnityTest]
        public IEnumerator CW11_SpacesAndHidden()
        {
            Time.captureDeltaTime = 1f / 60f;
            TextMeshProUGUI t = Text("A B C");
            t.maxVisibleCharacters = 3;
            yield return null;
            Player(t.gameObject, new TMPCharacterOffsetItem { m_Stagger = 0.5f }, t, TweenTo(new Vector4(0f, 1f, 0f, 0f)), 1f).Play();
            // past the item's half: the first character's window is done (T ≥ 1 − s), the space's (share 0.25) is not
            for (int f = 0; f < 38; f++)
                yield return null;
            TMP_CharacterInfo c0 = t.textInfo.characterInfo[0], c4 = t.textInfo.characterInfo[4];
            Vector3[] v = t.mesh.vertices;
            Assert.That((Mathf.Abs(Shift(t, 0).y - Size(c0)) < Eps * Size(c0), c4.isVisible, v[c4.vertexIndex] == v[c4.vertexIndex + 2]),
                Is.EqualTo((true, false, true)), $"the first at its own end {Shift(t, 0).y / Size(c0):0.000} · the hidden 'C' still size 0");
        }

        // ---- a fallback font's character (a sub-mesh) and a 3D text

        [UnityTest]
        public IEnumerator CW12_FallbackAnd3D()
        {
            Time.captureDeltaTime = 1f / 60f;
            TextMeshProUGUI t = Text("AΩ");
            var go = new GameObject("tmp3d");
            m_Made.Add(go);
            var t3 = go.AddComponent<TextMeshPro>();
            t3.text = "AB";
            yield return null;
            Player(t.gameObject, new TMPCharacterOffsetItem { m_Stagger = 0f }, t, TweenTo(new Vector4(0f, 1f, 0f, 0f)), 0.05f).Play();
            Player(go, new TMPCharacterOffsetItem { m_Stagger = 0f }, t3, TweenTo(new Vector4(0f, 1f, 0f, 0f)), 0.05f).Play();
            for (int f = 0; f < 10; f++)
                yield return null;
            TMP_CharacterInfo omega = t.textInfo.characterInfo[1], b3 = t3.textInfo.characterInfo[1];
            Assert.That(omega.materialReferenceIndex, Is.EqualTo(1), "Ω on the fallback's sub-mesh");
            Assert.That((Mathf.Abs(Shift(t, 1).y - Size(omega)) < Eps * Size(omega), Mathf.Abs(Shift(t3, 1).y - Size(b3)) < Eps * Mathf.Abs(Size(b3))), Is.EqualTo((true, true)), "the sub-mesh's · the 3D text's");
        }

        // ---- a reverse sort — each character's own quad moves (the LateApply mirrors), a change frame too

        [UnityTest]
        public IEnumerator CW14_ReverseSort()
        {
            Time.captureDeltaTime = 1f / 60f;
            TextMeshProUGUI t = Text("ABCDE");
            t.geometrySortingOrder = VertexSortingOrder.Reverse;
            yield return null;
            Player(t.gameObject, new TMPCharacterOffsetItem { m_Stagger = 0f }, t, TweenTo(new Vector4(0f, 0.5f, 0f, 0f)), 0.05f).Play();
            for (int f = 0; f < 6; f++)
                yield return null;
            int ok = 0, frames = 0;
            // before any rebuild: the first activation's copy came from TMP's sorted arrays
            for (int i = 0; i < t.textInfo.characterCount; i++)
            {
                frames++;
                TMP_CharacterInfo c = t.textInfo.characterInfo[i];
                Vector3 s = Shift(t, i);
                if (!c.isVisible || (Mathf.Abs(s.y - 0.5f * Size(c)) < Eps * Size(c) && Mathf.Abs(s.x) < Eps))
                    ok++;
            }
            for (int f = 0; f < 10; f++)
            {
                if ((f & 1) == 0)
                    t.SetText("ABCD{0}", f);
                yield return null;
                for (int i = 0; i < t.textInfo.characterCount; i++)
                {
                    frames++;
                    TMP_CharacterInfo c = t.textInfo.characterInfo[i];
                    Vector3 s = Shift(t, i);
                    if (!c.isVisible || (Mathf.Abs(s.y - 0.5f * Size(c)) < Eps * Size(c) && Mathf.Abs(s.x) < Eps))
                        ok++;
                }
            }
            Assert.That(ok, Is.EqualTo(frames), "each character's own quad, quiet and change frames");
        }

        // the text's OnPreRenderText handlers (TMP's field-like event — its backing field)
        static int Handlers(TMP_Text t)
        {
            System.Reflection.FieldInfo f = t.GetType().GetField("OnPreRenderText", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            var d = f?.GetValue(t) as Delegate;
            return d == null ? 0 : d.GetInvocationList().Length;
        }

        // ---- the slot's life — two players compose on one slot · released, a rebuild leaves the original (no handler left)

        [UnityTest]
        public IEnumerator CW20_TheSlotsLife()
        {
            Time.captureDeltaTime = 1f / 60f;
            TextMeshProUGUI t = Text("AB");
            yield return null;
            int handlers0 = Handlers(t);
            FeedbackPlayer wave = Player(t.gameObject, new TMPCharacterOffsetItem { m_Stagger = 0f }, t, Crest(new Vector4(0f, 0.25f, 0f, 0f)), 10f);
            FeedbackPlayer up = Player(t.gameObject, new TMPCharacterOffsetItem { m_Stagger = 0f }, t, TweenTo(new Vector4(0f, 0.5f, 0f, 0f)), 0.05f);
            wave.Play();
            up.Play();
            for (int f = 0; f < 10; f++)
                yield return null;
            int handlersPlaying = Handlers(t);
            TMP_CharacterInfo c = t.textInfo.characterInfo[0];
            float shift = Shift(t, 0).y / Size(c);
            bool composed = shift >= 0.5f + 0.985f * 0.25f && shift <= 0.5f + 1.0005f * 0.25f;
            wave.ReleaseRuntime();
            up.ReleaseRuntime();
            yield return null;
            yield return null;
            t.SetText("AC");
            yield return null;
            Assert.That((composed, Shift(t, 0)), Is.EqualTo((true, Vector3.zero)), $"composed {shift:0.000} · released — the rebuild is the original");
            Assert.That((handlersPlaying - handlers0, Handlers(t) - handlers0), Is.EqualTo((1, 0)), "one handler for the two players · none after the release");
        }
        static OscillateMotion Wave(Vector4 a, float hz) => new OscillateMotion { m_Params = new OscillateParams { m_Amplitude = a, m_Frequency = hz } };

        // ---- the frame allocation in real frames — the three channels running on a canvas text allocate in no more frames past
        // the run's median idle frame than the idle window does (three to spare) · a quiet text and one rebuilt every frame (a
        // spacing change — TMP's rebuild without a string) · the writes in the window · the positive control

        [UnityTest]
        public IEnumerator CW16_FrameAllocation([Values(false, true)] bool rebuild)
        {
            LogAssert.ignoreFailingMessages = true;
            Time.captureDeltaTime = 1f / 60f;
            TextMeshProUGUI t = Text("G1 characters");
            var players = new List<FeedbackPlayer>
            {
                Player(t.gameObject, new TMPCharacterOffsetItem(), t, Wave(new Vector4(0f, 0.2f, 0f, 0f), 2f), 5f),
                Player(t.gameObject, new TMPCharacterScaleItem(), t, Wave(new Vector4(0.2f, 0f, 0f, 0f), 3f), 5f),
                Player(t.gameObject, new TMPCharacterAlphaItem(), t, Wave(new Vector4(0.3f, 0f, 0f, 0f), 1f), 5f),
            };
            using ProfilerRecorder rec = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame");
            yield return null;
            foreach (FeedbackPlayer p in players)
            {
                p.EnsureRuntime();
                p.Play();
            }
            for (int f = 0; f < 20; f++)
            {
                if (rebuild)
                    t.characterSpacing = (f & 1) == 0 ? 1f : 0f;
                yield return null;
            }
            foreach (FeedbackPlayer p in players)
                p.Stop(StopMode.Revert);
            for (int f = 0; f < 5; f++)
                yield return null;
            var idle = new List<long>();
            for (int f = 0; f < 31; f++)
            {
                if (rebuild)
                    t.characterSpacing = (f & 1) == 0 ? 1f : 0f;
                yield return null;
                idle.Add(rec.LastValue);
            }
            idle.Sort();
            long baseline = idle[15];
            int idleAllocating = 0;
            foreach (long v in idle)
                if (v - baseline >= 64)
                    idleAllocating++;
            foreach (FeedbackPlayer p in players)
                p.Play();
            int allocating = 0, moved = 0;
            long over = 0;
            for (int f = 0; f < 120; f++)
            {
                if (rebuild)
                    t.characterSpacing = (f & 1) == 0 ? 1f : 0f;
                yield return null;
                long d = rec.LastValue - baseline;
                if (f > 2 && d >= 64)
                {
                    allocating++;
                    over += d;
                }
                TMP_CharacterInfo c = t.textInfo.characterInfo[0];
                Vector3 v0 = t.textInfo.meshInfo[0].vertices[c.vertexIndex];
                if (v0 != c.vertex_BL.position)
                    moved++;
            }
            int positive = 0;
            for (int f = 0; f < 5; f++)
            {
                _ = new byte[8192];
                yield return null;
                if (rec.LastValue - baseline >= 4000)
                    positive++;
            }
            Debug.Log($"PROBE CW16 rebuild {rebuild} · baseline {baseline} · idle allocating frames {idleAllocating}/31 · allocating frames {allocating}/117 ({over} B) · positive {positive}/5 · moved {moved}/120");
            Assert.That(moved, Is.GreaterThan(100), "the characters moved in the window — the positive");
            Assert.That(positive, Is.GreaterThanOrEqualTo(4), "the positive control");
            Assert.That(allocating, Is.LessThanOrEqualTo(idleAllocating + 3), "the characters allocate nothing once warm");
        }

        static long s_Baseline = -1;

        static IEnumerator Baseline()
        {
            if (s_Baseline >= 0)
                yield break;
            yield return Perf.Measure("(x) no play", 1, 0);
            long first = Perf.s_Last.Alloc;
            yield return Perf.Measure("(x) no play again", 1, 0);
            s_Baseline = Math.Min(first, Perf.s_Last.Alloc);
        }

        static readonly string[] s_Costs = { "(x) tmp character offset", "(x) tmp character scale", "(x) tmp character alpha" };
        static readonly double[] s_Budgets = { 3750, 3750, 3750 };

        // ---- the cost records — a text of three characters per player · per play ns within its budget and the window's
        // allocation against the baseline in an IL2CPP player

        [UnityTest]
        public IEnumerator CW17_Costs([Values(0, 1, 2)] int kind, [Values(100, 1000)] int n)
        {
            FeedbackScheduler saved = FeedbackScheduler.Default;
            var test = new FeedbackScheduler(ContextKind.Test);
            FeedbackScheduler.Default = test;
            Time.captureDeltaTime = 1f / 60f;
            try
            {
                yield return null;
                yield return Baseline();
                var cgo = new GameObject("Canvas", typeof(RectTransform));
                m_Made.Add(cgo);
                cgo.AddComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
                var players = new List<FeedbackPlayer>(n);
                TextMeshProUGUI first = null;
                for (int i = 0; i < n; i++)
                {
                    var go = new GameObject("tmp", typeof(RectTransform));
                    go.transform.SetParent(cgo.transform, false);
                    ((RectTransform)go.transform).sizeDelta = new Vector2(40f, 20f);
                    var text = go.AddComponent<TextMeshProUGUI>();
                    text.text = "G11";
                    first = first ?? text;
                    TargetItem item = kind == 0 ? new TMPCharacterOffsetItem { m_Motion = Wave(new Vector4(0f, 0.2f, 0f, 0f), 10f) }
                        : kind == 1 ? new TMPCharacterScaleItem { m_Motion = Wave(new Vector4(0.2f, 0f, 0f, 0f), 10f) }
                        : (TargetItem)new TMPCharacterAlphaItem { m_Motion = Wave(new Vector4(0.3f, 0f, 0f, 0f), 10f) };
                    item.m_Target = new TargetRef { m_Mode = TargetRefMode.Direct, m_Object = text, m_Path = "", m_Anchor = "" };
                    item.m_Timing.m_Duration = 20f;
                    var p = go.AddComponent<FeedbackPlayer>();
                    p.m_Items.Add(item);
                    p.EnsureRuntime();
                    players.Add(p);
                }
                int prepared = 0, accepted = 0;
                TextMeshProUGUI at = first;
                float Read() => kind == 2 ? at.textInfo.meshInfo[0].colors32[0].a : at.textInfo.meshInfo[0].vertices[0].y + at.textInfo.meshInfo[0].vertices[2].x;
                float start = Read(), moved = 0f;
                void Sample() => moved = Mathf.Max(moved, Mathf.Abs(Read() - start));
                Canvas.willRenderCanvases += Sample;
                foreach (FeedbackPlayer p in players)
                {
                    if (p.Runtime != null && p.Runtime.Binding(0) != null)
                        prepared++;
                    if (p.Play() != default)
                        accepted++;
                }
                try
                {
                    yield return Perf.Settled(s_Costs[kind] + " " + n, n, 0, s_Baseline, s_Budgets[kind]);
                }
                finally
                {
                    Canvas.willRenderCanvases -= Sample;
                }
                Perf.Window w = Perf.s_Last;
                Assert.That(moved, Is.GreaterThan(0.005f), "the plays write the characters — the positive");
                Assert.That(prepared + " · " + accepted, Is.EqualTo(n + " · " + n), "prepared · accepted");
                Assert.That(w.PerPlay, Is.GreaterThan(0), "per play ns (written down)");
                Perf.RowBudget(s_Costs[kind] + " " + n, s_Budgets[kind]);
                if (Perf.k_Judge)
                    Assert.That(Math.Abs(w.Alloc - s_Baseline), Is.LessThanOrEqualTo(Perf.Equal), "the window " + w.Alloc + " vs " + s_Baseline + " B");
                foreach (FeedbackPlayer p in players)
                    p.Stop();
            }
            finally
            {
                FeedbackScheduler.Default = saved;
                if (!test.IsShutdown)
                    test.Shutdown();
            }
        }
        // ---- a Wiggle gives each character its own seed — the characters move apart

        [UnityTest]
        public IEnumerator CW21_WiggleOwnSeeds()
        {
            Time.captureDeltaTime = 1f / 60f;
            TextMeshProUGUI t = Text("ABCDE");
            yield return null;
            var wiggle = new WiggleMotion { m_Params = new WiggleParams { m_Amplitude = new Vector4(0.1f, 0.1f, 0f, 0f), m_Frequency = 3f, m_Octaves = 2 } };
            // a seed of its own: the players made before (the run's order) do not choose it
            Player(t.gameObject, new TMPCharacterOffsetItem { m_Stagger = 0f }, t, wiggle, 10f).Play(PlayArgs.Default.WithSeed(1u));
            for (int f = 0; f < 15; f++)
                yield return null;
            var seen = new HashSet<int>();
            float most = 0f;
            for (int i = 0; i < 5; i++)
            {
                float y = Shift(t, i).y / Size(t.textInfo.characterInfo[i]);
                seen.Add(Mathf.RoundToInt(y * 10000f));
                most = Mathf.Max(most, Mathf.Abs(y));
            }
            Assert.That((seen.Count >= 4, most > 0.001f, most <= 0.2f), Is.EqualTo((true, true, true)), $"apart {seen.Count} · the most {most:0.0000}");
        }

        // ---- Restore gives the first value back — a kept fade out

        [UnityTest]
        public IEnumerator CW22_Restore()
        {
            Time.captureDeltaTime = 1f / 60f;
            TextMeshProUGUI t = Text("Hello");
            yield return null;
            FeedbackPlayer p = Player(t.gameObject, new TMPCharacterAlphaItem { m_Stagger = 0f }, t, TweenTo(Vector4.zero), 0.05f);
            p.Play();
            for (int f = 0; f < 10; f++)
                yield return null;
            byte faded = t.mesh.colors32[t.textInfo.characterInfo[0].vertexIndex].a;
            p.Restore();
            yield return null;
            yield return null;
            Assert.That((faded, t.mesh.colors32[t.textInfo.characterInfo[0].vertexIndex].a), Is.EqualTo(((byte)0, (byte)255)), "faded · restored");
        }

        // ---- a backward play — the window mirrors on its own (the last character returns first), the end is the start

        [UnityTest]
        public IEnumerator CW23_Backward()
        {
            Time.captureDeltaTime = 1f / 60f;
            TextMeshProUGUI t = Text("ABCDE");
            yield return null;
            FeedbackPlayer p = Player(t.gameObject, new TMPCharacterOffsetItem { m_Stagger = 0.5f }, t, TweenTo(new Vector4(0f, 1f, 0f, 0f)), 1f);
            p.Play(PlayArgs.Default.WithDirection(PlayerDirection.Backward));
            for (int f = 0; f < 30; f++)
                yield return null;
            float size = Size(t.textInfo.characterInfo[0]);
            float first = Shift(t, 0).y / size, last = Shift(t, 4).y / size;
            for (int f = 0; f < 40; f++)
                yield return null;
            Assert.That((first > 0.8f, last < 0.2f), Is.EqualTo((true, true)), $"mid-way back {first:0.00} · {last:0.00}");
            Assert.That((Shift(t, 0), Shift(t, 4)), Is.EqualTo((Vector3.zero, Vector3.zero)), "the backward end is the start");
        }

        // ---- a Hold stop keeps the item's value where it stopped — one value for every character, through a rebuild

        [UnityTest]
        public IEnumerator CW24_Hold()
        {
            Time.captureDeltaTime = 1f / 60f;
            TextMeshProUGUI t = Text("ABCDE");
            yield return null;
            FeedbackPlayer p = Player(t.gameObject, new TMPCharacterOffsetItem { m_Stagger = 0.5f }, t, TweenTo(new Vector4(0f, 1f, 0f, 0f)), 1f);
            p.Play();
            for (int f = 0; f < 30; f++)
                yield return null;
            p.Stop(StopMode.Hold);
            yield return null;
            t.SetText("ABCDF");
            yield return null;
            float size = Size(t.textInfo.characterInfo[0]);
            float a = Shift(t, 0).y / size, e = Shift(t, 4).y / size;
            Assert.That((Mathf.Abs(a - e) < Eps, a > 0.05f, a < 0.95f), Is.EqualTo((true, true, true)), $"one value {a:0.000} · {e:0.000}");
        }

        // ---- the latest begun Persist record on a channel wins — a fade in begun later over a running fade out

        [UnityTest]
        public IEnumerator CW25_TheLatestWins()
        {
            Time.captureDeltaTime = 1f / 60f;
            TextMeshProUGUI t = Text("Hello");
            yield return null;
            Player(t.gameObject, new TMPCharacterAlphaItem { m_Stagger = 0f }, t, TweenTo(Vector4.zero), 2f).Play();
            for (int f = 0; f < 20; f++)
                yield return null;
            byte fading = t.mesh.colors32[t.textInfo.characterInfo[0].vertexIndex].a;
            Player(t.gameObject, new TMPCharacterAlphaItem { m_Stagger = 0f }, t, TweenTo(Vector4.one), 2f).Play();
            for (int f = 0; f < 10; f++)
                yield return null;
            Assert.That((fading < 255, t.mesh.colors32[t.textInfo.characterInfo[0].vertexIndex].a), Is.EqualTo((true, (byte)255)), "the later tween (to 1 from the kept 1) wins over the running fade");
        }
        // ---- a quiet text rebuilt (a text change with nothing running), then a new play — the copy is the new mesh, not the old

        [UnityTest]
        public IEnumerator CW26_AQuietRebuildThenAPlay()
        {
            Time.captureDeltaTime = 1f / 60f;
            TextMeshProUGUI t = Text("Hello World");
            yield return null;
            FeedbackPlayer p = Player(t.gameObject, new TMPCharacterOffsetItem { m_Stagger = 0f }, t, Crest(new Vector4(0f, 0.5f, 0f, 0f)), 10f);
            p.Play();
            for (int f = 0; f < 5; f++)
                yield return null;
            p.Stop(StopMode.Revert);
            yield return null;
            t.SetText("Wide Words");
            yield return null;
            yield return null;
            p.Play();
            for (int f = 0; f < 5; f++)
                yield return null;
            TMP_CharacterInfo c = t.textInfo.characterInfo[0];
            Vector3 s = Shift(t, 0);
            Assert.That((AtCrest(s.y, 0.5f * Size(c)), Mathf.Abs(s.x) < Eps), Is.EqualTo((true, true)), $"the new text's own corner — {s}");
        }

        // ---- a staggered Oscillate starts and ends at the rest — a late character waits for its turn at the press and the lag
        // closes over the item's last stagger, so no character steps at the press or at the end (two whole turns: the wave's own
        // end is at the rest). The wave's own pace is 0.1 a frame, 0.16 catching up

        [UnityTest]
        public IEnumerator CW27_AStaggeredOscillateStartsAndEndsAtRest()
        {
            Time.captureDeltaTime = 1f / 60f;
            TextMeshProUGUI t = Text("ABCDE");
            yield return null;
            var wave = new OscillateMotion { m_Params = new OscillateParams { m_Amplitude = new Vector4(0f, 0.5f, 0f, 0f), m_Frequency = 2f } };
            FeedbackPlayer p = Player(t.gameObject, new TMPCharacterOffsetItem { m_Stagger = 0.5f }, t, wave, 1f);
            float size = Size(t.textInfo.characterInfo[0]);
            var last = new float[5];
            var reach = new float[5];
            float step = 0f;
            p.Play();
            for (int f = 0; f < 80; f++)
            {
                yield return null;
                for (int i = 0; i < 5; i++)
                {
                    float y = Shift(t, i).y / size;
                    step = Mathf.Max(step, Mathf.Abs(y - last[i]));
                    reach[i] = Mathf.Max(reach[i], Mathf.Abs(y));
                    last[i] = y;
                }
            }
            float least = Mathf.Min(Mathf.Min(reach[0], reach[1]), Mathf.Min(Mathf.Min(reach[2], reach[3]), reach[4]));
            Assert.That((step < 0.2f, least > 0.3f, Mathf.Abs(last[2]) < Eps), Is.EqualTo((true, true, true)),
                $"the largest step {step:0.000} · every character moved {least:0.00} · the rest after the end {last[2]:0.000}");
        }
    }
}
