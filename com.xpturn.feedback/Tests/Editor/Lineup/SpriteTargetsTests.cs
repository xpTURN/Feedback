using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using xpTURN.Feedback.Editor;
using Object = UnityEngine.Object;
using UIs = UnityEngine.TestTools.Constraints.Is;

namespace xpTURN.Feedback.Tests.Editor
{
    // the sprite, line and trail targets on a test scheduler ticked by hand (0.05 s), the preview on the test's clock (an update
    // is 1/120 s). The checks are bits, a backward play on a player of the item alone (a keeper's length comes first backward),
    // m_Size and the hidden mark on an empty renderer, a save's sprites the built-in assets (a runtime sprite is written into the
    // scene)
    public sealed class SpriteTargetsTests
    {
        const float Step = 0.05f;
        const double UpdateStep = 1.0 / 120.0;

        static double s_Now;
        static readonly Color c0 = new Color(0.123456f, 0.6f, 0.987654f, 0.9f);
        static readonly PlayArgs Back = PlayArgs.Default.WithDirection(PlayerDirection.Backward);

        readonly List<Object> m_Made = new List<Object>();
        readonly List<FeedbackScheduler> m_Schedulers = new List<FeedbackScheduler>();
        readonly List<FeedbackPlayer> m_Players = new List<FeedbackPlayer>();
        readonly List<Scene> m_Scenes = new List<Scene>();
        readonly List<string> m_Assets = new List<string>();
        IPreviewRecordSource m_Source;
        Func<double> m_Clock;
        Scene m_Main;
        static object s_Sink;

        [SetUp]
        public void SetUp()
        {
            m_Source = PreviewRecordPaths.Source;
            m_Clock = PreviewClock.Source;
            PreviewRecordPaths.Source = new PreviewRecordPathTable();
            s_Now = 0;
            PreviewClock.Source = () => s_Now;
            m_Main = SceneManager.GetActiveScene();
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
            if (m_Main.IsValid() && m_Main.isLoaded)
                SceneManager.SetActiveScene(m_Main);
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
            SessionState.EraseString(PreviewLineColors.Key);
            m_Made.Clear();
            m_Schedulers.Clear();
            m_Players.Clear();
            m_Scenes.Clear();
            m_Assets.Clear();
        }

        static string B(float f) => BitConverter.SingleToInt32Bits(f).ToString("X8");

        static string BC(Color c) => B(c.r) + B(c.g) + B(c.b) + B(c.a);

        static bool Rgb(Color a, Color b) => B(a.r) == B(b.r) && B(a.g) == B(b.g) && B(a.b) == B(b.b);

        static string Name(Sprite s) => s != null ? s.name : "null";

        static string Seq(List<Sprite> seq) => string.Concat(seq.Select(s => Name(s).Substring(0, 1)));

        static Vector4 V(Color c) => new Vector4(c.r, c.g, c.b, c.a);

        static TweenMotion TweenTo(Vector4 to)
        {
            TweenParams p = TweenParams.Default;
            p.m_To = to;
            return new TweenMotion { m_Params = p };
        }

        static TweenMotion TweenTo(float to) => TweenTo(new Vector4(to, 0f, 0f, 0f));

        static FlashMotion FlashTo(Vector4 target)
        {
            var flash = new FlashMotion();
            flash.m_Params.m_Target = target;
            return flash;
        }

        static BlinkMotion BlinkTo(Vector4 value)
        {
            var blink = new BlinkMotion();
            blink.m_Params.m_Value = value;
            return blink;
        }

        static SpringMotion Spring(SpringMode mode, Vector4 value)
        {
            var spring = new SpringMotion();
            spring.m_Params.m_Mode = mode;
            spring.m_Params.m_Value = value;
            return spring;
        }

        static PunchMotion PunchBy(Vector4 amplitude) => new PunchMotion { m_Params = new PunchParams { m_Amplitude = amplitude, m_Vibrato = 1f, m_Decay = 0f } };

        static SetMotion SetTo(float value, bool relative = false) => new SetMotion { m_Params = new SetParams { m_Value = new Vector4(value, 0f, 0f, 0f), m_Relative = relative } };

        static T With<T>(T item, FeedbackMotion motion, float duration, Object target) where T : TargetItem
        {
            item.m_Target = new TargetRef { m_Mode = TargetRefMode.Direct, m_Object = target, m_Path = "", m_Anchor = "" };
            item.m_Motion = motion;
            item.m_Timing.m_Duration = duration;
            return item;
        }

        // a punch on the player's own position keeps a play running (a Revert stop · a Restore have a play to stop)
        static PositionItem Keeper(float duration = 3f)
        {
            var keeper = new PositionItem { m_Target = new TargetRef { m_Mode = TargetRefMode.Self, m_Path = "", m_Anchor = "" }, m_Motion = PunchBy(new Vector4(0.5f, 0.5f, 0.5f, 0f)) };
            keeper.m_Timing.m_Duration = duration;
            return keeper;
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

        FeedbackPlayer Player(FeedbackScheduler s, params FeedbackItem[] items) => Player(s, false, items);

        FeedbackPlayer Player(FeedbackScheduler s, bool initial, params FeedbackItem[] items)
        {
            FeedbackPlayer p = Go("Player").AddComponent<FeedbackPlayer>();
            p.m_Items.AddRange(items);
            p.m_ApplyInitialState = initial;
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

        static void Pump(int n)
        {
            for (int i = 0; i < n; i++)
            {
                s_Now += UpdateStep;
                PreviewSession.Current?.Update();
            }
        }

        Sprite Sprite(string name, int width = 8, int height = 8)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false) { name = name };
            m_Made.Add(texture);
            Sprite sprite = UnityEngine.Sprite.Create(texture, new Rect(0, 0, width, height), new Vector2(0.5f, 0.5f), 8f);
            sprite.name = name;
            m_Made.Add(sprite);
            return sprite;
        }

        SpriteRenderer Renderer(string name, Sprite sprite = null)
        {
            var renderer = Go(name).AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            return renderer;
        }

        Scene OpenScene(string name)
        {
            string path = "Assets/" + name + ".unity";
            File.WriteAllText(Path.GetFullPath(path), "%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n");
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            Scene scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
            m_Scenes.Add(scene);
            m_Assets.Add(path);
            SceneManager.SetActiveScene(scene);
            return scene;
        }

        static Sprite Builtin(string path) => AssetDatabase.GetBuiltinExtraResource<Sprite>(path);

        // the saved file's line for one component's field
        static string Saved(string file, Object component, string field)
        {
            string id = GlobalObjectId.GetGlobalObjectIdSlow(component).targetObjectId.ToString();
            int at = file.IndexOf("&" + id + "\n", StringComparison.Ordinal);
            if (at < 0)
                return "no block";
            int end = file.IndexOf("--- !u!", at, StringComparison.Ordinal);
            string block = end < 0 ? file.Substring(at) : file.Substring(at, end - at);
            foreach (string line in block.Split('\n'))
            {
                if (line.TrimStart().StartsWith(field + ":", StringComparison.Ordinal))
                    return line.Trim();
            }
            return "no " + field;
        }

        // true only when five runs in a row allocate — 6000.3 counts unrelated allocations now and then
        static bool Allocates(TestDelegate run)
        {
            for (int i = 0; i < 5; i++)
            {
                if (!UIs.AllocatingGCMemory().ApplyTo(run).IsSuccess)
                    return false;
            }
            return true;
        }

        // ---- Sprite Color — a Persist's Revert · Restore bits · Flash · Blink · Spring · Gradient ends · a game write kept ·
        // an HDR and negative color at a flash's hold (not clamped)
        [Test]
        public void SQ03_SpriteColor()
        {
            SpriteRenderer sr = Renderer("SQ03", Sprite("S"));
            sr.color = c0;
            FeedbackScheduler s = Sched();
            FeedbackPlayer p = Player(s, With(new SpriteColorItem(), TweenTo(V(Color.red)), 0.3f, sr), Keeper());
            p.Play();
            Tick(s, 8);
            Assert.That(BC(sr.color), Is.EqualTo(BC(Color.red)), "the tween's end");
            p.Stop(StopMode.Revert);
            s.Tick(0f);
            Assert.That(BC(sr.color), Is.EqualTo(BC(c0)), "Revert");
            p.Play();
            Tick(s, 8);
            p.Stop(StopMode.Hold);
            s.Tick(0f);
            p.Restore();
            s.Tick(0f);
            Assert.That(BC(sr.color), Is.EqualTo(BC(c0)), "Restore");
            foreach (FeedbackMotion motion in new FeedbackMotion[] { FlashTo(V(Color.red)), BlinkTo(V(Color.green)), Spring(SpringMode.Bump, new Vector4(0.3f, 0.3f, 0.3f, 0f)), new GradientMotion() })
            {
                Player(s, With(new SpriteColorItem(), motion, 0.5f, sr)).Play();
                Tick(s, 40);
                s.Tick(0f);
                Assert.That(BC(sr.color), Is.EqualTo(BC(c0)), motion.GetType().Name + "'s end");
            }
            Player(s, With(new SpriteColorItem(), FlashTo(V(Color.red)), 0.5f, sr)).Play();
            Tick(s, 3);
            var game = new Color(0.2f, 0.3f, 0.4f, 1f);
            sr.color = game;
            Tick(s, 30);
            s.Tick(0f);
            Assert.That(BC(sr.color), Is.EqualTo(BC(game)), "the game's write stays");
            sr.color = c0;
            var hdr = new Color(2f, -0.5f, 0.5f, 1.5f);
            Player(s, With(new SpriteColorItem(), FlashTo(V(hdr)), 1f, sr)).Play();
            Tick(s, 5);
            Assert.That(BC(sr.color), Is.EqualTo(BC(hdr)), "an HDR · negative hold");
            Tick(s, 30);
            s.Tick(0f);
            Assert.That(BC(sr.color), Is.EqualTo(BC(c0)), "its end");
        }

        // ---- Sprite Alpha shares the color's slot — the rgb untouched · Flash · Blink holds and ends
        [Test]
        public void SQ04a_AlphaSharesTheColorsSlot()
        {
            SpriteRenderer r = Renderer("SQ04a", Sprite("S"));
            r.color = c0;
            FeedbackScheduler s = Sched();
            FeedbackPlayer pc = Player(s, With(new SpriteColorItem(), TweenTo(V(Color.red)), 0.3f, r), Keeper());
            FeedbackPlayer pa = Player(s, With(new SpriteAlphaItem(), TweenTo(0.25f), 0.3f, r), Keeper());
            Assert.That(((SpriteAlphaBinding)pa.Runtime.Binding(0)).Slot, Is.SameAs(((SpriteColorBinding)pc.Runtime.Binding(0)).Slot), "one slot");
            pa.Play();
            Tick(s, 3);
            Assert.That(Rgb(r.color, c0), Is.True, "the alpha leaves the rgb");
            Tick(s, 6);
            Assert.That((Rgb(r.color, c0), B(r.color.a)), Is.EqualTo((true, B(0.25f))), "the alpha's end");
            pa.Stop(StopMode.Revert);
            s.Tick(0f);
            Assert.That(BC(r.color), Is.EqualTo(BC(c0)), "the alpha's Revert");
            Player(s, With(new SpriteAlphaItem(), FlashTo(new Vector4(0.1f, 0f, 0f, 0f)), 1f, r)).Play();
            Tick(s, 5);
            Assert.That((Rgb(r.color, c0), B(r.color.a)), Is.EqualTo((true, B(0.1f))), "a flash's hold");
            Tick(s, 30);
            s.Tick(0f);
            Assert.That(BC(r.color), Is.EqualTo(BC(c0)), "a flash's end");
            Player(s, With(new SpriteAlphaItem(), BlinkTo(new Vector4(0.3f, 0f, 0f, 0f)), 0.5f, r)).Play();
            s.Tick(0.01f);
            Assert.That((Rgb(r.color, c0), B(r.color.a)), Is.EqualTo((true, B(0.3f))), "a blink on");
            Tick(s, 15);
            s.Tick(0f);
            Assert.That(BC(r.color), Is.EqualTo(BC(c0)), "a blink's end");
            Player(s, With(new SpriteAlphaItem(), Spring(SpringMode.Bump, new Vector4(-0.5f, 0f, 0f, 0f)), 1f, r)).Play();
            Tick(s, 3);
            Assert.That(Rgb(r.color, c0), Is.True, "a bump leaves the rgb");
            Tick(s, 60);
            s.Tick(0f);
            Assert.That(BC(r.color), Is.EqualTo(BC(c0)), "a bump's end");
        }

        // ---- two plays — the alpha's revert leaves the color's rgb; reverts in reverse order give the original (both orders;
        // the color's value carries its alpha)
        [Test]
        public void SQ04b_TwoPlaysRevertInReverseOrder()
        {
            SpriteRenderer r = Renderer("SQ04b", Sprite("S"));
            r.color = c0;
            FeedbackScheduler s = Sched();
            FeedbackPlayer pc = Player(s, With(new SpriteColorItem(), TweenTo(V(Color.red)), 0.3f, r), Keeper());
            FeedbackPlayer pa = Player(s, With(new SpriteAlphaItem(), TweenTo(0.25f), 0.3f, r), Keeper());
            pa.Play();
            Tick(s, 8);
            pc.Play();
            Tick(s, 8);
            pa.Stop(StopMode.Revert);
            s.Tick(0f);
            Assert.That(Rgb(r.color, Color.red), Is.True, "the alpha's revert leaves the color's rgb");
            pc.Stop(StopMode.Revert);
            s.Tick(0f);
            r.color = c0;
            pc.Play();
            Tick(s, 8);
            pa.Play();
            Tick(s, 8);
            pa.Stop(StopMode.Revert);
            s.Tick(0f);
            pc.Stop(StopMode.Revert);
            s.Tick(0f);
            Assert.That(BC(r.color), Is.EqualTo(BC(c0)), "reverse order — color then alpha");
            pa.Play();
            Tick(s, 8);
            pc.Play();
            Tick(s, 8);
            pc.Stop(StopMode.Revert);
            s.Tick(0f);
            pa.Stop(StopMode.Revert);
            s.Tick(0f);
            Assert.That(BC(r.color), Is.EqualTo(BC(c0)), "reverse order — alpha then color");
        }

        // ---- an alpha override takes the alpha only — a color flash's rgb stays through the alpha's hold
        [Test]
        public void SQ04c_AlphaOverrideKeepsTheColors()
        {
            SpriteRenderer both = Renderer("SQ04cBoth", Sprite("S1")), alone = Renderer("SQ04cAlone", Sprite("S2")), bumped = Renderer("SQ04cBump", Sprite("S3")), bump = Renderer("SQ04cBumpAlone", Sprite("S4"));
            foreach (SpriteRenderer r in new[] { both, alone, bumped, bump })
                r.color = c0;
            FeedbackScheduler s = Sched();
            Player(s, With(new SpriteColorItem(), FlashTo(V(Color.red)), 1f, both)).Play();
            Player(s, With(new SpriteAlphaItem(), FlashTo(new Vector4(0.1f, 0f, 0f, 0f)), 1f, both)).Play();
            Player(s, With(new SpriteColorItem(), FlashTo(V(Color.red)), 1f, alone)).Play();
            // another context — the flash limit counts a context's flash-sensitive starts in a second
            FeedbackScheduler sb = Sched();
            Player(sb, With(new SpriteColorItem(), Spring(SpringMode.Bump, new Vector4(30f, 0f, 0f, 0f)), 1f, bumped)).Play();
            Player(sb, With(new SpriteAlphaItem(), FlashTo(new Vector4(0.1f, 0f, 0f, 0f)), 1f, bumped)).Play();
            Player(sb, With(new SpriteColorItem(), Spring(SpringMode.Bump, new Vector4(30f, 0f, 0f, 0f)), 1f, bump)).Play();
            Tick(s, 5);
            Tick(sb, 5);
            Assert.That((Rgb(both.color, alone.color), B(both.color.a)), Is.EqualTo((true, B(0.1f))), "a color flash under an alpha flash");
            Assert.That(B(bump.color.r), Is.Not.EqualTo(B(c0.r)), "the bump moves the red");
            Assert.That((Rgb(bumped.color, bump.color), B(bumped.color.a)), Is.EqualTo((true, B(0.1f))), "a color bump under an alpha flash");
            Tick(s, 40);
            s.Tick(0f);
            Tick(sb, 60);
            sb.Tick(0f);
            Assert.That((BC(both.color), BC(bumped.color)), Is.EqualTo((BC(c0), BC(c0))), "the ends");
        }

        // ---- Sprite Swap — Set · Revert · Restore · backward · an empty sprite refused · the game's sprite between plays ·
        // an empty renderer's first sprite (m_Size; the revert's null clears the hidden mark)
        [Test]
        public void SQ05_SpriteSwap()
        {
            Sprite o = Sprite("O", 24, 16), b = Sprite("B"), g = Sprite("G");
            SpriteRenderer sr = Renderer("SQ05", o);
            FeedbackScheduler s = Sched();
            FeedbackPlayer p = Player(s, With(new SpriteSwapItem { m_Sprite = b }, SetTo(1f), 0f, sr), Keeper());
            p.Play();
            s.Tick(Step);
            Assert.That(sr.sprite, Is.SameAs(b), "Set");
            p.Stop(StopMode.Revert);
            s.Tick(0f);
            Assert.That(sr.sprite, Is.SameAs(o), "Revert");
            p.Play();
            s.Tick(Step);
            p.Stop(StopMode.Hold);
            s.Tick(0f);
            p.Restore();
            s.Tick(0f);
            Assert.That(sr.sprite, Is.SameAs(o), "Restore");
            SpriteRenderer rb = Renderer("SQ05b", o);
            FeedbackScheduler sb = Sched();
            FeedbackPlayer pb = Player(sb, With(new SpriteSwapItem { m_Sprite = b }, SetTo(1f), 0f, rb));
            pb.Play();
            Tick(sb, 2);
            pb.Play(Back);
            Tick(sb, 2);
            Assert.That(rb.sprite, Is.SameAs(o), "backward — the origin");
            FeedbackPlayer none = Player(s, With(new SpriteSwapItem(), SetTo(1f), 0f, sr));
            Assert.That(none.Runtime.Binding(0), Is.Null, "no sprite — refused");
            p.Play();
            s.Tick(Step);
            p.Stop(StopMode.Hold);
            s.Tick(0f);
            sr.sprite = g;
            p.Play();
            s.Tick(Step);
            p.Stop(StopMode.Revert);
            s.Tick(0f);
            Assert.That(sr.sprite, Is.SameAs(g), "the game's sprite — the play's start");
            p.Restore();
            s.Tick(0f);
            Assert.That(sr.sprite, Is.SameAs(o), "Restore — the first");
            SpriteRenderer empty = Renderer("SQ05e");
            empty.drawMode = SpriteDrawMode.Sliced;
            FeedbackPlayer pe = Player(s, With(new SpriteSwapItem { m_Sprite = o }, SetTo(1f), 0f, empty), Keeper());
            pe.Play();
            s.Tick(Step);
            Assert.That(empty.size, Is.EqualTo(new Vector2(3f, 2f)), "the first sprite sizes it");
            pe.Stop(StopMode.Revert);
            s.Tick(0f);
            Assert.That((empty.sprite, SpriteAssignedState.Assigned(empty)), Is.EqualTo(((Sprite)null, false)), "the revert's null clears the mark");
        }

        // ---- Sprite Sheet — the places (rounded down) · four writes · Revert · Restore · refused lists · an empty frame skipped
        [Test]
        public void SQ06a_SheetPlaces()
        {
            Sprite o = Sprite("O"), a = Sprite("A"), b = Sprite("B"), c = Sprite("C"), d = Sprite("D");
            SpriteRenderer sr = Renderer("SQ06a", o);
            FeedbackScheduler s = Sched();
            FeedbackPlayer p = Player(s, With(new SpriteSheetItem { m_Frames = new List<Sprite> { a, b, c, d } }, TweenTo(1f), 1f, sr), Keeper());
            var seq = new List<Sprite>();
            p.Play();
            for (int i = 0; i < 22; i++)
            {
                s.Tick(Step);
                seq.Add(sr.sprite);
            }
            Assert.That(Seq(seq), Is.EqualTo("AAAABBBBBCCCCCDDDDDDDD"), "the places");
            Assert.That(((SpriteSheetSlot)((SpriteSheetBinding)p.Runtime.Binding(0)).Slot).Writes, Is.EqualTo(4), "a write a frame");
            p.Stop(StopMode.Revert);
            s.Tick(0f);
            Assert.That(sr.sprite, Is.SameAs(o), "Revert");
            p.Play();
            Tick(s, 22);
            p.Stop(StopMode.Hold);
            s.Tick(0f);
            p.Restore();
            s.Tick(0f);
            Assert.That(sr.sprite, Is.SameAs(o), "Restore");
            Assert.That(Player(s, With(new SpriteSheetItem(), TweenTo(1f), 1f, sr)).Runtime.Binding(0), Is.Null, "no frame — refused");
            Assert.That(Player(s, With(new SpriteSheetItem { m_Frames = new List<Sprite> { null, null } }, TweenTo(1f), 1f, sr)).Runtime.Binding(0), Is.Null, "empty frames — refused");
            SpriteRenderer rn = Renderer("SQ06aEmpty", o);
            FeedbackScheduler sn = Sched();
            Player(sn, With(new SpriteSheetItem { m_Frames = new List<Sprite> { a, null, c } }, TweenTo(1f), 0.6f, rn)).Play();
            seq.Clear();
            for (int i = 0; i < 13; i++)
            {
                sn.Tick(Step);
                seq.Add(rn.sprite);
            }
            Assert.That(Seq(seq), Is.EqualTo("AAAAAAACCCCCC"), "an empty frame skipped");
        }

        // ---- backward (the item alone — from the last frame, the first at its end) · seek · the initial state
        [Test]
        public void SQ06b_SheetBackwardSeekInitial()
        {
            Sprite o = Sprite("O"), a = Sprite("A"), b = Sprite("B"), c = Sprite("C"), d = Sprite("D");
            var frames = new List<Sprite> { a, b, c, d };
            SpriteRenderer rb = Renderer("SQ06b", o);
            FeedbackScheduler sb = Sched();
            FeedbackPlayer pb = Player(sb, With(new SpriteSheetItem { m_Frames = frames }, TweenTo(1f), 1f, rb));
            pb.Play();
            Tick(sb, 22);
            var seq = new List<Sprite>();
            pb.Play(Back);
            for (int i = 0; i < 22; i++)
            {
                sb.Tick(Step);
                seq.Add(rb.sprite);
            }
            Assert.That(Seq(seq), Is.EqualTo("DDDDDCCCCCBBBBBAAAAAAA"), "backward");
            SpriteRenderer sr = Renderer("SQ06bSeek", o);
            FeedbackScheduler s = Sched();
            FeedbackPlayer p = Player(s, With(new SpriteSheetItem { m_Frames = frames }, TweenTo(1f), 1f, sr), Keeper());
            p.Play();
            s.Tick(Step);
            p.Seek(0.55f);
            s.Tick(0f);
            Assert.That(sr.sprite, Is.SameAs(c), "a seek");
            SpriteRenderer ri = Renderer("SQ06bInitial", o);
            Player(Sched(), true, With(new SpriteSheetItem { m_Frames = frames }, TweenTo(1f), 1f, ri));
            Assert.That(ri.sprite, Is.SameAs(a), "the initial state — the first frame");
        }

        // ---- on one renderer — two sheets (each its own slot) · a Swap and a Sheet reverted in reverse order (both orders) · a
        // second play from the first frame (its revert the last frame) · the game's sprite between plays
        [Test]
        public void SQ06c_SheetsAndSwapOnOneRenderer()
        {
            Sprite o = Sprite("O"), a = Sprite("A"), b = Sprite("B"), c = Sprite("C"), d = Sprite("D"), g = Sprite("G");
            var frames = new List<Sprite> { a, b, c, d };
            SpriteRenderer two = Renderer("SQ06cTwo", o);
            FeedbackScheduler st = Sched();
            FeedbackPlayer p1 = Player(st, With(new SpriteSheetItem { m_Frames = new List<Sprite> { a, b } }, TweenTo(1f), 0.5f, two), Keeper());
            FeedbackPlayer p2 = Player(st, With(new SpriteSheetItem { m_Frames = new List<Sprite> { c, d } }, TweenTo(1f), 0.5f, two), Keeper());
            p1.Play();
            Tick(st, 12);
            p2.Play();
            Tick(st, 12);
            Assert.That(two.sprite, Is.SameAs(d), "the second sheet shows its own frames");
            p2.Stop(StopMode.Revert);
            st.Tick(0f);
            Assert.That(two.sprite, Is.SameAs(b), "the second sheet's revert — the first's last frame");
            p1.Stop(StopMode.Revert);
            st.Tick(0f);
            Assert.That(two.sprite, Is.SameAs(o), "the first sheet's revert");
            foreach (bool swapFirst in new[] { true, false })
            {
                SpriteRenderer r = Renderer("SQ06cMix", o);
                FeedbackScheduler sm = Sched();
                FeedbackPlayer pw = Player(sm, With(new SpriteSwapItem { m_Sprite = g }, SetTo(1f), 0f, r), Keeper());
                FeedbackPlayer ph = Player(sm, With(new SpriteSheetItem { m_Frames = frames }, TweenTo(1f), 0.5f, r), Keeper());
                (swapFirst ? pw : ph).Play();
                Tick(sm, 12);
                (swapFirst ? ph : pw).Play();
                Tick(sm, 12);
                (swapFirst ? ph : pw).Stop(StopMode.Revert);
                sm.Tick(0f);
                (swapFirst ? pw : ph).Stop(StopMode.Revert);
                sm.Tick(0f);
                Assert.That(r.sprite, Is.SameAs(o), (swapFirst ? "Swap then Sheet" : "Sheet then Swap") + " reverted in reverse order");
            }
            SpriteRenderer sr = Renderer("SQ06cPlays", o);
            FeedbackScheduler s = Sched();
            FeedbackPlayer p = Player(s, With(new SpriteSheetItem { m_Frames = frames }, TweenTo(1f), 1f, sr), Keeper());
            p.Play();
            Tick(s, 22);
            p.Stop(StopMode.Hold);
            s.Tick(0f);
            p.Play();
            s.Tick(Step);
            Assert.That(sr.sprite, Is.SameAs(a), "a second play — the first frame");
            p.Stop(StopMode.Revert);
            s.Tick(0f);
            Assert.That(sr.sprite, Is.SameAs(d), "its revert — the play's start");
            p.Restore();
            s.Tick(0f);
            p.Play();
            Tick(s, 22);
            p.Stop(StopMode.Hold);
            s.Tick(0f);
            sr.sprite = g;
            p.Play();
            Tick(s, 5);
            p.Stop(StopMode.Revert);
            s.Tick(0f);
            Assert.That(sr.sprite, Is.SameAs(g), "the game's sprite — the play's start");
            p.Restore();
            s.Tick(0f);
            Assert.That(sr.sprite, Is.SameAs(o), "Restore — the first");
        }

        // ---- Sprite Flip — per axis · Set · Toggle (from off · on) · Revert · Restore · backward
        [Test]
        public void SQ07_SpriteFlip()
        {
            SpriteRenderer sr = Renderer("SQ07", Sprite("F"));
            FeedbackScheduler s = Sched();
            FeedbackPlayer px = Player(s, With(new SpriteFlipItem { m_Axis = SpriteAxis.X }, SetTo(1f), 0f, sr), Keeper());
            FeedbackPlayer py = Player(s, With(new SpriteFlipItem { m_Axis = SpriteAxis.Y }, SetTo(1f, true), 0f, sr), Keeper());
            px.Play();
            s.Tick(Step);
            Assert.That((sr.flipX, sr.flipY), Is.EqualTo((true, false)), "X Set");
            px.Stop(StopMode.Revert);
            s.Tick(0f);
            Assert.That(sr.flipX, Is.False, "X Revert");
            py.Play();
            s.Tick(Step);
            Assert.That(sr.flipY, Is.True, "Y Toggle from off");
            py.Stop(StopMode.Hold);
            s.Tick(0f);
            py.Restore();
            s.Tick(0f);
            Assert.That(sr.flipY, Is.False, "Y Restore");
            sr.flipY = true;
            py.Play();
            s.Tick(Step);
            Assert.That(sr.flipY, Is.False, "Y Toggle from on");
            SpriteRenderer rb = Renderer("SQ07b", Sprite("F2"));
            FeedbackScheduler sb = Sched();
            FeedbackPlayer pb = Player(sb, With(new SpriteFlipItem { m_Axis = SpriteAxis.X }, SetTo(1f), 0f, rb));
            pb.Play();
            Tick(sb, 2);
            pb.Play(Back);
            Tick(sb, 2);
            Assert.That(rb.flipX, Is.False, "backward — the origin");
        }

        // ---- Line width — Tween · Punch ends · nothing below 0 (the engine keeps a negative one) · the curve's shape
        [Test]
        public void SQ08_LineWidth()
        {
            var lr = Go("SQ08").AddComponent<LineRenderer>();
            lr.positionCount = 2;
            lr.widthMultiplier = 0.25f;
            lr.widthCurve = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0.5f));
            string curve = string.Join(";", lr.widthCurve.keys.Select(k => B(k.time) + B(k.value)));
            FeedbackScheduler s = Sched();
            Player(s, With(new LineWidthItem(), TweenTo(3f), 0.3f, lr)).Play();
            Tick(s, 8);
            Assert.That(B(lr.widthMultiplier), Is.EqualTo(B(3f)), "the tween's end");
            lr.widthMultiplier = 0.25f;
            Player(s, With(new LineWidthItem(), PunchBy(new Vector4(-2f, 0f, 0f, 0f)), 0.5f, lr)).Play();
            float least = float.MaxValue;
            for (int i = 0; i < 12; i++)
            {
                s.Tick(Step);
                least = Mathf.Min(least, lr.widthMultiplier);
            }
            Assert.That(least, Is.GreaterThanOrEqualTo(0f), "nothing below 0");
            Assert.That(B(lr.widthMultiplier), Is.EqualTo(B(0.25f)), "the punch's end");
            Assert.That(string.Join(";", lr.widthCurve.keys.Select(k => B(k.time) + B(k.value))), Is.EqualTo(curve), "the curve's shape");
        }

        // ---- Line color — per end · the first and last keys only (8 bits — the values read back) · Tween · Punch ends ·
        // Revert · Restore · a game write during a punch kept
        [Test]
        public void SQ09a_LineColorEndsAndKeys()
        {
            var lr = Go("SQ09a").AddComponent<LineRenderer>();
            lr.positionCount = 2;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(new Color(0.123456f, 0.456789f, 0.789123f, 1f), 0f), new GradientColorKey(Color.green, 0.5f), new GradientColorKey(new Color(0.3333f, 0.6666f, 0.9999f, 1f), 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.3456f, 1f) });
            lr.colorGradient = g;
            Color s0 = lr.startColor, e0 = lr.endColor;
            string middle = lr.colorGradient.colorKeys[1].color.ToString();
            var target = new Color(0.123456f, 0.5f, 0.987654f, 0.333333f);
            var probe = Go("SQ09aProbe").AddComponent<LineRenderer>();
            probe.startColor = target;
            Color stored = probe.startColor;
            FeedbackScheduler s = Sched();
            FeedbackPlayer pc = Player(s, With(new LineColorItem { m_End = LineEnd.Start }, TweenTo(V(target)), 0.3f, lr), Keeper());
            pc.Play();
            Tick(s, 8);
            Assert.That(BC(lr.startColor), Is.EqualTo(BC(stored)), "the tween's end — the value read back");
            Gradient now = lr.colorGradient;
            Assert.That((now.colorKeys.Length, now.alphaKeys.Length, now.colorKeys[1].color.ToString()), Is.EqualTo((3, 2, middle)), "the keys");
            Assert.That(BC(lr.endColor), Is.EqualTo(BC(e0)), "the other end");
            pc.Stop(StopMode.Revert);
            s.Tick(0f);
            Assert.That(BC(lr.startColor), Is.EqualTo(BC(s0)), "Revert");
            pc.Play();
            Tick(s, 8);
            pc.Stop(StopMode.Hold);
            s.Tick(0f);
            pc.Restore();
            s.Tick(0f);
            Assert.That(BC(lr.startColor), Is.EqualTo(BC(s0)), "Restore");
            FeedbackPlayer pp = Player(s, With(new LineColorItem { m_End = LineEnd.End }, PunchBy(new Vector4(0.3f, 0.3f, 0.3f, 0f)), 0.4f, lr));
            pp.Play();
            Tick(s, 14);
            s.Tick(0f);
            Assert.That((BC(lr.endColor), BC(lr.startColor)), Is.EqualTo((BC(e0), BC(s0))), "the punch's end");
            pp.Play();
            Tick(s, 2);
            lr.endColor = new Color(0.2f, 0.2f, 0.2f, 1f);
            Color game = lr.endColor;
            Tick(s, 12);
            s.Tick(0f);
            Assert.That(BC(lr.endColor), Is.EqualTo(BC(game)), "the game's write during a punch stays");
        }

        // ---- no allocation — a running line color tween and punch (the gradient's getter allocates)
        [Test]
        public void SQ09b_LineColorNoAllocation()
        {
            var lr = Go("SQ09b").AddComponent<LineRenderer>();
            lr.positionCount = 2;
            FeedbackScheduler s = Sched();
            Player(s, With(new LineColorItem { m_End = LineEnd.Start }, TweenTo(V(Color.red)), 2f, lr), With(new LineColorItem { m_End = LineEnd.End }, PunchBy(new Vector4(0.3f, 0.3f, 0.3f, 0f)), 2f, lr)).Play();
            Tick(s, 2);
            Assert.That(Allocates(() => s.Tick(Step)), Is.False, "allocates");
            Assert.That(Allocates(() => s_Sink = new byte[16]), Is.True, "positive control");
        }

        // ---- Trail — time · width (nothing below 0) · emitting (Set · Toggle · Revert) · Clear at the Set's tick, no revert
        [Test]
        public void SQ10_Trail()
        {
            var tr = Go("SQ10").AddComponent<TrailRenderer>();
            FeedbackScheduler s = Sched();
            Player(s, With(new TrailTimeItem(), TweenTo(-1f), 0.3f, tr), With(new TrailWidthItem(), TweenTo(-1f), 0.3f, tr)).Play();
            Tick(s, 8);
            Assert.That((tr.time, tr.widthMultiplier), Is.EqualTo((0f, 0f)), "nothing below 0");
            FeedbackPlayer pe = Player(s, With(new TrailEmittingItem(), SetTo(0f), 0f, tr), Keeper());
            pe.Play();
            s.Tick(Step);
            Assert.That(tr.emitting, Is.False, "emitting off");
            pe.Stop(StopMode.Revert);
            s.Tick(0f);
            Assert.That(tr.emitting, Is.True, "Revert");
            Player(s, With(new TrailEmittingItem(), SetTo(1f, true), 0f, tr)).Play();
            s.Tick(Step);
            Assert.That(tr.emitting, Is.False, "Toggle");
            tr.emitting = true;
            tr.AddPosition(Vector3.zero);
            tr.AddPosition(Vector3.one);
            tr.AddPosition(Vector3.up);
            FeedbackPlayer pc = Player(s, With(new TrailClearItem(), SetTo(1f), 0f, tr), Keeper());
            pc.Play();
            s.Tick(Step);
            Assert.That(tr.positionCount, Is.Zero, "Clear");
            tr.AddPosition(Vector3.zero);
            tr.AddPosition(Vector3.one);
            pc.Stop(StopMode.Revert);
            s.Tick(0f);
            Assert.That(tr.positionCount, Is.EqualTo(2), "a Revert clears nothing");
        }

        // ---- the preview — the recorded rows back after the stop · the line color by its save state · a reload whose hook was
        // missed (the recovery while the session is forgotten — the true reload is in PreviewReloadTests) · a normal end erases
        // the record
        [Test]
        public void SQ11a_ThePreviewPutsItBack()
        {
            Scene scene = OpenScene("SpriteTargetsSQ11");
            Sprite ui = Builtin("UI/Skin/UISprite.psd"), bg = Builtin("UI/Skin/Background.psd");
            var sr = new GameObject("sr").AddComponent<SpriteRenderer>();
            sr.sprite = ui;
            sr.color = c0;
            var lr = new GameObject("lr").AddComponent<LineRenderer>();
            var tr = new GameObject("tr").AddComponent<TrailRenderer>();
            var player = new GameObject("player").AddComponent<FeedbackPlayer>();
            Color s0 = lr.startColor;
            player.m_Items.Add(With(new SpriteColorItem(), TweenTo(V(Color.red)), 0.2f, sr));
            player.m_Items.Add(With(new SpriteFlipItem(), SetTo(1f), 0f, sr));
            player.m_Items.Add(With(new SpriteSwapItem { m_Sprite = bg }, SetTo(1f), 0f, sr));
            player.m_Items.Add(With(new LineWidthItem(), TweenTo(3f), 0.2f, lr));
            player.m_Items.Add(With(new LineColorItem(), TweenTo(V(Color.red)), 0.2f, lr));
            player.m_Items.Add(With(new TrailTimeItem(), TweenTo(2f), 0.2f, tr));
            player.m_Items.Add(With(new TrailEmittingItem(), SetTo(0f), 0f, tr));
            player.m_Items.Add(Keeper());
            Assert.That(EditorSceneManager.SaveScene(scene), Is.True);
            Assert.That(PreviewSession.Start(player, default), Is.EqualTo(PreviewRefusal.None));
            Pump(40);
            Assert.That((BC(sr.color), sr.flipX, sr.sprite, BC(lr.startColor), tr.emitting), Is.EqualTo((BC(Color.red), true, bg, BC(Color.red), false)), "during");
            PreviewSession.Current.Stop();
            Assert.That((BC(sr.color), sr.flipX, sr.sprite), Is.EqualTo((BC(c0), false, ui)), "the sprite back");
            Assert.That((lr.widthMultiplier, tr.time, tr.emitting), Is.EqualTo((1f, 5f, true)), "the width · time · emitting back");
            Assert.That(BC(lr.startColor), Is.EqualTo(BC(s0)), "the line color back");
            Assert.That(scene.isDirty, Is.False, "clean");
            PreviewSession.Start(player, default);
            Pump(40);
            Assert.That(SessionState.GetString(PreviewLineColors.Key, ""), Is.Not.Empty, "the record during");
            PreviewRecovery.Run();
            Assert.That(BC(lr.startColor), Is.EqualTo(BC(s0)), "the recovery puts the line color back");
            Assert.That(SessionState.GetString(PreviewLineColors.Key, ""), Is.Empty, "the recovery takes the record");
            PreviewSession.Current?.Stop();
            PreviewSession.Start(player, default);
            Pump(10);
            PreviewSession.Current.Stop();
            Assert.That(SessionState.GetString(PreviewLineColors.Key, ""), Is.Empty, "the end erases the record");
        }

        // ---- a save during the preview writes the values before (the line's keys · the sprite reference · a Sliced m_Size · an
        // empty renderer's first sprite: its m_Size and the hidden mark) · the preview's values after it · the end's file the one
        // before · clean
        [Test]
        public void SQ12_SaveDuringThePreview()
        {
            Scene scene = OpenScene("SpriteTargetsSQ12");
            Sprite ui = Builtin("UI/Skin/UISprite.psd"), bg = Builtin("UI/Skin/Background.psd"), knob = Builtin("UI/Skin/Knob.psd");
            var sr = new GameObject("sr").AddComponent<SpriteRenderer>();
            sr.sprite = ui;
            sr.drawMode = SpriteDrawMode.Sliced;
            sr.size = new Vector2(5f, 5f);
            sr.color = c0;
            var empty = new GameObject("empty").AddComponent<SpriteRenderer>();
            empty.drawMode = SpriteDrawMode.Sliced;
            var lr = new GameObject("lr").AddComponent<LineRenderer>();
            var player = new GameObject("player").AddComponent<FeedbackPlayer>();
            player.m_Items.Add(With(new SpriteColorItem(), TweenTo(V(Color.red)), 0.2f, sr));
            player.m_Items.Add(With(new SpriteSwapItem { m_Sprite = bg }, SetTo(1f), 0f, sr));
            player.m_Items.Add(With(new LineColorItem { m_End = LineEnd.Start }, TweenTo(V(Color.red)), 0.2f, lr));
            player.m_Items.Add(With(new LineColorItem { m_End = LineEnd.End }, TweenTo(V(Color.blue)), 0.2f, lr));
            player.m_Items.Add(With(new SpriteSwapItem { m_Sprite = knob }, SetTo(1f), 0f, empty));
            player.m_Items.Add(Keeper());
            Assert.That(EditorSceneManager.SaveScene(scene), Is.True);
            string before = File.ReadAllText(scene.path);
            Assert.That(PreviewSession.Start(player, default), Is.EqualTo(PreviewRefusal.None));
            Pump(40);
            Assert.That(empty.sprite, Is.SameAs(knob), "the empty renderer's sprite during");
            Assert.That(EditorSceneManager.SaveScene(scene), Is.True);
            string during = File.ReadAllText(scene.path);
            foreach ((Object o, string field) in new (Object, string)[] { (sr, "m_Color"), (sr, "m_Sprite"), (sr, "m_Size"), (lr, "key0"), (lr, "key1"), (empty, "m_Sprite"), (empty, "m_Size"), (empty, "m_WasSpriteAssigned") })
                Assert.That(Saved(during, o, field), Is.EqualTo(Saved(before, o, field)), o.name + " " + field);
            Assert.That((sr.sprite, BC(lr.startColor), empty.sprite), Is.EqualTo((bg, BC(Color.red), knob)), "the preview's values after the save");
            PreviewSession.Current.Stop();
            Assert.That(scene.isDirty, Is.False, "clean");
            Assert.That(EditorSceneManager.SaveScene(scene), Is.True);
            Assert.That(File.ReadAllText(scene.path), Is.EqualTo(before), "the end's file");
        }

        // ---- a user's edit of a line color during the preview (Undo — the session's interception): (a) after its tween — the
        // session ends, the edit stays, the other end goes back · (b) another field's edit — the session goes on, the stop puts
        // both ends back · (e) while the tween runs — the edit stays
        [Test]
        public void SQ13_UserEditOfALineColor()
        {
            Scene scene = OpenScene("SpriteTargetsSQ13");
            var lr = new GameObject("lr").AddComponent<LineRenderer>();
            lr.positionCount = 2;
            var player = new GameObject("player").AddComponent<FeedbackPlayer>();
            player.m_Items.Add(With(new LineColorItem { m_End = LineEnd.Start }, TweenTo(V(Color.red)), 0.2f, lr));
            player.m_Items.Add(With(new LineColorItem { m_End = LineEnd.End }, TweenTo(V(Color.green)), 0.2f, lr));
            player.m_Items.Add(With(new LineWidthItem(), TweenTo(2f), 0.2f, lr));
            player.m_Items.Add(Keeper(10f));
            Assert.That(EditorSceneManager.SaveScene(scene), Is.True);
            Color s0 = lr.startColor, e0 = lr.endColor;
            PreviewSession.Start(player, default);
            Pump(40);
            Undo.RecordObject(lr, "end color");
            lr.endColor = Color.blue;
            Undo.FlushUndoRecordObjects();
            Pump(2);
            Assert.That((PreviewSession.Current, PreviewSession.LastEnd), Is.EqualTo(((PreviewSession)null, PreviewEndReason.UserEdit)), "(a) ends");
            Assert.That((BC(lr.endColor), BC(lr.startColor)), Is.EqualTo((BC(Color.blue), BC(s0))), "(a) the edit stays · the other end back");
            lr.startColor = s0;
            lr.endColor = e0;
            PreviewSession.Start(player, default);
            Pump(40);
            Undo.RecordObject(lr, "loop");
            lr.loop = !lr.loop;
            Undo.FlushUndoRecordObjects();
            Pump(2);
            Assert.That(PreviewSession.Current, Is.Not.Null, "(b) goes on");
            PreviewSession.Current.Stop();
            Assert.That((BC(lr.startColor), BC(lr.endColor)), Is.EqualTo((BC(s0), BC(e0))), "(b) both ends back");
            PreviewSession.Start(player, default);
            Pump(8);
            Undo.RecordObject(lr, "end color while it runs");
            lr.endColor = Color.blue;
            Undo.FlushUndoRecordObjects();
            Pump(4);
            Assert.That(PreviewSession.Current, Is.Null, "(e) ends");
            Assert.That((BC(lr.endColor), BC(lr.startColor)), Is.EqualTo((BC(Color.blue), BC(s0))), "(e) the edit stays · the other end back");
        }

        // ---- the record source knows the nine slot types (the line color not recorded) · a session makes one line color state an
        // end and a hidden mark's state only for a renderer that never had a sprite
        [Test]
        public void SQ15_TheSourceAndTheStates()
        {
            foreach (Type slot in new[] { typeof(SpriteColorSlot), typeof(SpriteSlot), typeof(SpriteSheetSlot), typeof(SpriteFlipSlot), typeof(LineWidthSlot), typeof(LineColorSlot),
                typeof(TrailTimeSlot), typeof(TrailWidthSlot), typeof(TrailEmittingSlot) })
                Assert.That(PreviewRecordPaths.Knows(slot), Is.True, slot.Name);
            Assert.That(((PreviewRecordPathTable)PreviewRecordPaths.Source).RowsOf(typeof(LineColorSlot)), Is.Null, "the line color has no rows");
            OpenScene("SpriteTargetsSQ15");
            var lr = new GameObject("lr").AddComponent<LineRenderer>();
            var assigned = new GameObject("assigned").AddComponent<SpriteRenderer>();
            assigned.sprite = Builtin("UI/Skin/UISprite.psd");
            var empty = new GameObject("empty").AddComponent<SpriteRenderer>();
            var player = new GameObject("player").AddComponent<FeedbackPlayer>();
            player.m_Items.Add(With(new LineColorItem { m_End = LineEnd.Start }, TweenTo(V(Color.red)), 0.2f, lr));
            player.m_Items.Add(With(new LineColorItem { m_End = LineEnd.Start }, PunchBy(new Vector4(0.2f, 0f, 0f, 0f)), 0.2f, lr));
            player.m_Items.Add(With(new LineColorItem { m_End = LineEnd.End }, TweenTo(V(Color.blue)), 0.2f, lr));
            player.m_Items.Add(With(new SpriteSwapItem { m_Sprite = Builtin("UI/Skin/Knob.psd") }, SetTo(1f), 0f, assigned));
            player.m_Items.Add(With(new SpriteSwapItem { m_Sprite = Builtin("UI/Skin/Knob.psd") }, SetTo(1f), 0f, empty));
            player.m_Items.Add(Keeper());
            Assert.That(PreviewSession.Start(player, default), Is.EqualTo(PreviewRefusal.None));
            Pump(20);
            var ends = new List<LineEnd>();
            var marked = new List<SpriteRenderer>();
            for (int i = 0; i < PreviewSaveState.Count; i++)
            {
                if (PreviewSaveState.At(i) is LineColorState line)
                    ends.Add(line.End);
                else if (PreviewSaveState.At(i) is SpriteAssignedState mark)
                    marked.Add(mark.Target);
            }
            Assert.That(ends.OrderBy(e => e), Is.EqualTo(new[] { LineEnd.Start, LineEnd.End }), "a state an end");
            Assert.That(marked, Is.EqualTo(new[] { empty }), "the mark's state for the empty renderer only");
        }

        // ---- the play mode's keep — a sprite reference, a frame list (an empty slot kept), the two enums: the record round trips
        [Test]
        public void SQ19_TheKeepRoundTrips()
        {
            Sprite ui = Builtin("UI/Skin/UISprite.psd"), knob = Builtin("UI/Skin/Knob.psd");
            var swap = new SpriteSwapItem { m_Sprite = knob, m_Motion = SetTo(1f) };
            var sheet = new SpriteSheetItem { m_Frames = new List<Sprite> { ui, null, knob }, m_Motion = TweenTo(1f) };
            var flip = new SpriteFlipItem { m_Axis = SpriteAxis.Y, m_Motion = SetTo(1f, true) };
            var line = new LineColorItem { m_End = LineEnd.End, m_Motion = TweenTo(V(Color.red)) };
            FeedbackPlayer a = Go("SQ19From").AddComponent<FeedbackPlayer>();
            a.m_Items.AddRange(new FeedbackItem[] { swap, sheet, flip, line });
            FeedbackPlayer b = Go("SQ19Into").AddComponent<FeedbackPlayer>();
            PlayModeRecord.Apply(b, PlayModeRecord.Capture(a, false, out int unknown), out PlayModeRecord.Stats stats);
            Assert.That((unknown, stats.Dropped), Is.EqualTo((0, 0)));
            Assert.That(((SpriteSwapItem)b.m_Items[0]).m_Sprite, Is.SameAs(knob), "the sprite");
            Assert.That(((SpriteSheetItem)b.m_Items[1]).m_Frames, Is.EqualTo(new[] { ui, null, knob }), "the frames");
            Assert.That((((SpriteFlipItem)b.m_Items[2]).m_Axis, ((LineColorItem)b.m_Items[3]).m_End), Is.EqualTo((SpriteAxis.Y, LineEnd.End)), "the enums");
        }

        // ---- the inspector — the direct write hidden where nothing reads it, the Set's Relative named Toggle · the add menu's
        // defaults (seen from a new component's own values)
        [Test]
        public void SQ18_TheInspectorAndTheDefaults()
        {
            foreach (FeedbackItem item in new FeedbackItem[] { new SpriteSwapItem(), new SpriteFlipItem(), new SpriteSheetItem(), new TrailEmittingItem(), new TrailClearItem() })
                Assert.That(ItemRules.Visible(item, "m_DirectWrite"), Is.False, item.GetType().Name + " direct write");
            foreach (FeedbackItem item in new FeedbackItem[] { new SpriteColorItem(), new SpriteAlphaItem(), new LineWidthItem(), new LineColorItem(), new TrailTimeItem(), new TrailWidthItem() })
                Assert.That(ItemRules.Visible(item, "m_DirectWrite"), Is.True, item.GetType().Name + " direct write");
            Assert.That((ItemRules.Label(new SpriteFlipItem(), "m_Relative"), ItemRules.Label(new TrailEmittingItem(), "m_Relative")), Is.EqualTo(("Toggle", "Toggle")));
            var red = new Vector4(1f, 0f, 0f, 1f);
            Assert.That(((TweenMotion)MotionDefaults.Create(typeof(SpriteColorItem), MotionKind.Tween, out _)).m_Params.m_To, Is.EqualTo(red), "Sprite Color tween");
            Assert.That(((FlashMotion)MotionDefaults.Create(typeof(SpriteColorItem), MotionKind.Flash, out _)).m_Params.m_Target, Is.EqualTo(red), "Sprite Color flash");
            Assert.That(((BlinkMotion)MotionDefaults.Create(typeof(SpriteColorItem), MotionKind.Blink, out _)).m_Params.m_Value, Is.EqualTo(red), "Sprite Color blink");
            Assert.That(((TweenMotion)MotionDefaults.Create(typeof(SpriteAlphaItem), MotionKind.Tween, out _)).m_Params.m_To, Is.EqualTo(Vector4.zero), "Sprite Alpha tween");
            Assert.That(((FlashMotion)MotionDefaults.Create(typeof(SpriteAlphaItem), MotionKind.Flash, out _)).m_Params.m_Target, Is.EqualTo(Vector4.zero), "Sprite Alpha flash");
            Assert.That(((BlinkMotion)MotionDefaults.Create(typeof(SpriteAlphaItem), MotionKind.Blink, out _)).m_Params.m_Value, Is.EqualTo(Vector4.zero), "Sprite Alpha blink");
            Assert.That(((SpringMotion)MotionDefaults.Create(typeof(SpriteAlphaItem), MotionKind.Spring, out _)).m_Params.m_Value, Is.EqualTo(Vector4.one * -30f), "Sprite Alpha bump dips");
            Assert.That(((SetMotion)MotionDefaults.Create(typeof(SpriteFlipItem), MotionKind.Set, out _)).m_Params.m_Value, Is.EqualTo(Vector4.one), "a flip on");
            Assert.That(((TweenMotion)MotionDefaults.Create(typeof(SpriteSheetItem), MotionKind.Tween, out float sheet)).m_Params.m_To, Is.EqualTo(Vector4.one), "the sheet's whole list");
            Assert.That(sheet, Is.EqualTo(0.5f), "the sheet's half second");
            Assert.That(((TweenMotion)MotionDefaults.Create(typeof(LineWidthItem), MotionKind.Tween, out _)).m_Params.m_To, Is.EqualTo(Vector4.one * 2f), "a line's width doubled");
            Assert.That(((PunchMotion)MotionDefaults.Create(typeof(LineWidthItem), MotionKind.Punch, out _)).m_Params.m_Amplitude, Is.EqualTo(Vector4.one * 0.5f), "a line's half punch");
            Assert.That(((TweenMotion)MotionDefaults.Create(typeof(LineColorItem), MotionKind.Tween, out _)).m_Params.m_To, Is.EqualTo(red), "a line's color");
            Assert.That(((PunchMotion)MotionDefaults.Create(typeof(LineColorItem), MotionKind.Punch, out _)).m_Params.m_Amplitude, Is.EqualTo(new Vector4(0f, -1f, -1f, 0f)), "a line's punch toward red");
            foreach (Type trail in new[] { typeof(TrailTimeItem), typeof(TrailWidthItem) })
            {
                Assert.That(((TweenMotion)MotionDefaults.Create(trail, MotionKind.Tween, out _)).m_Params.m_To, Is.EqualTo(Vector4.one * 2f), trail.Name + " tween");
                Assert.That(((SetMotion)MotionDefaults.Create(trail, MotionKind.Set, out _)).m_Params.m_Value, Is.EqualTo(Vector4.one * 2f), trail.Name + " set");
            }
            Assert.That(((SetMotion)MotionDefaults.Create(typeof(TrailEmittingItem), MotionKind.Set, out _)).m_Params.m_Value, Is.EqualTo(Vector4.zero), "emitting off");
        }
    }
}
