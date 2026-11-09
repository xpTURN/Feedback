using System;
using System.Collections;
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
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using xpTURN.Feedback.Editor;
using Object = UnityEngine.Object;

namespace xpTURN.Feedback.Tests.Editor
{
    // the PostFX items in edit mode: a test scheduler ticked by hand (1/60 s). The stack's values after a render of the test's
    // own RT camera (post-processing on · the Default mask) — the expected values from the base read there (the pipeline
    // asset's profile: vignette 0.2 · bloom 0.25)
    public sealed class PostFXOverlayTargetsTests
    {
        const float Step = 1f / 60f;
        const string ScenePath = "Assets/PostFXOverlayTargetsTests.unity";

        readonly List<Object> m_Made = new List<Object>();
        readonly List<FeedbackScheduler> m_Schedulers = new List<FeedbackScheduler>();
        readonly List<FeedbackPlayer> m_Players = new List<FeedbackPlayer>();
        readonly List<Action> m_Restore = new List<Action>();
        Camera m_Cam;

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
            if (m_Cam != null)
                m_Cam.targetTexture = null;
            for (int i = m_Made.Count - 1; i >= 0; i--)
            {
                if (m_Made[i] != null)
                    Object.DestroyImmediate(m_Made[i]);
            }
            for (int i = m_Restore.Count - 1; i >= 0; i--)
                m_Restore[i]();
            m_Made.Clear();
            m_Schedulers.Clear();
            m_Players.Clear();
            m_Restore.Clear();
            m_Cam = null;
        }

        FeedbackScheduler Sched(ContextKind kind = ContextKind.Test)
        {
            var s = new FeedbackScheduler(kind);
            m_Schedulers.Add(s);
            return s;
        }

        FeedbackPlayer Player(FeedbackScheduler s, params FeedbackItem[] items) => Player(s, PlayPolicy.Restart, items);

        FeedbackPlayer Player(FeedbackScheduler s, PlayPolicy policy, params FeedbackItem[] items)
        {
            var go = new GameObject("Player");
            m_Made.Add(go);
            var p = go.AddComponent<FeedbackPlayer>();
            p.m_Policy = policy;
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

        // the test's RT camera — its first render brings the volume manager up
        Camera Cam()
        {
            if (m_Cam != null)
                return m_Cam;
            var go = new GameObject("UO camera");
            m_Made.Add(go);
            m_Cam = go.AddComponent<Camera>();
            m_Cam.clearFlags = CameraClearFlags.SolidColor;
            m_Cam.backgroundColor = new Color(0.5f, 0.5f, 0.5f, 1f);
            var rt = new RenderTexture(32, 32, 24, RenderTextureFormat.ARGBHalf);
            m_Made.Add(rt);
            m_Cam.targetTexture = rt;
            UniversalAdditionalCameraData d = m_Cam.GetUniversalAdditionalCameraData();
            d.renderPostProcessing = true;
            d.volumeLayerMask = 1;
            m_Cam.Render();
            return m_Cam;
        }

        static T Stack<T>() where T : VolumeComponent => VolumeManager.instance.stack.GetComponent<T>();

        float Render<T>(Func<T, float> read) where T : VolumeComponent
        {
            Cam().Render();
            return read(Stack<T>());
        }

        // the centre pixel of the test camera's render
        Color Pixel()
        {
            Camera cam = Cam();
            cam.Render();
            var read = new Texture2D(32, 32, TextureFormat.RGBAFloat, false);
            m_Made.Add(read);
            RenderTexture.active = cam.targetTexture;
            read.ReadPixels(new Rect(0, 0, 32, 32), 0, 0);
            read.Apply();
            RenderTexture.active = null;
            return read.GetPixel(16, 16);
        }

        static TweenMotion TweenTo(float to)
        {
            TweenParams p = TweenParams.Default;
            p.m_To = new Vector4(to, to, to, to);
            return new TweenMotion { m_Params = p };
        }

        static FlashMotion Flash(float attack, float hold, float release) => new FlashMotion { m_Params = new FlashParams { m_Target = Vector4.one, m_Attack = attack, m_Hold = hold, m_Release = release } };

        // a shape held at v — its weight toward the item's fields is v
        static CurveMotion Held(float v) => new CurveMotion { m_Params = new CurveParams { m_Curve = AnimationCurve.Constant(0f, 1f, v) } };

        static T With<T>(T item, FeedbackMotion motion, float duration) where T : TargetItem
        {
            item.m_Motion = motion;
            item.m_Timing.m_Duration = duration;
            return item;
        }

        static string Prefix(ContextKind kind) => kind == ContextKind.Preview ? PropertyLayerRegistry.PreviewPrefix : PropertyLayerRegistry.RuntimePrefix;

        // prefixed GameObjects · profiles · components (a profile leaves its components)
        static int Prefixed(string prefix)
        {
            Type comp = Type.GetType(PostFXNames.VolumeComponent), prof = Type.GetType(PostFXNames.VolumeProfile);
            return Resources.FindObjectsOfTypeAll<GameObject>().Count(g => g != null && g.name.StartsWith(prefix, StringComparison.Ordinal))
                   + Resources.FindObjectsOfTypeAll(comp).Count(o => o != null && o.name.StartsWith(prefix, StringComparison.Ordinal))
                   + Resources.FindObjectsOfTypeAll(prof).Count(o => o != null && o.name.StartsWith(prefix, StringComparison.Ordinal));
        }

        // a global user volume under the overlay — its own profile with a vignette
        Volume UserVolume(float intensity, Color color, float weight = 1f)
        {
            var go = new GameObject("user volume");
            m_Made.Add(go);
            var v = go.AddComponent<Volume>();
            v.isGlobal = true;
            v.weight = weight;
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            m_Made.Add(profile);
            Vignette vignette = profile.Add<Vignette>(false);
            m_Made.Add(vignette);
            vignette.intensity.overrideState = true;
            vignette.intensity.value = intensity;
            vignette.color.overrideState = true;
            vignette.color.value = color;
            v.sharedProfile = profile;
            return v;
        }

        // the main camera the test owns — the scene's own untagged until the tear-down · URP's data as given
        Camera OwnMainCamera(bool data, bool postProcessing, int mask = 1)
        {
            foreach (Camera other in Camera.allCameras)
            {
                if (!other.CompareTag("MainCamera"))
                    continue;
                Camera c = other;
                c.tag = "Untagged";
                m_Restore.Add(() =>
                {
                    if (c != null)
                        c.tag = "MainCamera";
                });
            }
            var go = new GameObject("UO main camera");
            m_Made.Add(go);
            go.tag = "MainCamera";
            Camera cam = go.AddComponent<Camera>();
            if (data)
            {
                UniversalAdditionalCameraData d = go.AddComponent<UniversalAdditionalCameraData>();
                d.renderPostProcessing = postProcessing;
                d.volumeLayerMask = mask;
            }
            return cam;
        }

        // ---- made at the preparation (the first Of) · one per context · Shutdown leaves nothing — the components too · closed
        // refuses · a closed context's service closes at once

        [Test]
        public void UO03a_MadeAtThePreparationGoneAtShutdown()
        {
            foreach (ContextKind kind in new[] { ContextKind.Test, ContextKind.Preview })
            {
                FeedbackScheduler s = Sched(kind);
                string prefix = Prefix(kind);
                int before = Prefixed(prefix);
                FeedbackPlayer p = Player(s, With(new PostFXBloomItem(), TweenTo(1f), 0.1f), With(new PostFXVignetteItem(), TweenTo(1f), 0.1f));
                PostFXOverlayService svc = PostFXOverlayService.Of(s.Context);
                Assert.That((svc.RentedCount, svc.Made), Is.EqualTo((2, 2)), kind + ": a volume per item, rented at the preparation");
                Assert.That(PostFXOverlayService.Of(s.Context), Is.SameAs(svc), kind + ": one per context");
                Assert.That(Prefixed(prefix), Is.EqualTo(before + 1 + 2 * 3), kind + ": the root · two volumes, profiles and components");
                p.ReleaseRuntime();
                s.Shutdown();
                Assert.That(svc.Root == null, Is.True, kind + ": Shutdown destroys the root");
                Assert.That(svc.IsClosed && svc.Refuses(), Is.True, kind + ": closed — it refuses");
                Assert.That(Prefixed(prefix), Is.EqualTo(before), kind + ": nothing prefixed left — the components too");
            }
        }

        [Test]
        public void UO03b_AClosedContextsServiceClosesAtOnce()
        {
            FeedbackScheduler s = Sched();
            int before = Prefixed(PropertyLayerRegistry.RuntimePrefix);
            s.Shutdown();
            PostFXOverlayService svc = PostFXOverlayService.Of(s.Context);
            Assert.That(svc.IsClosed && svc.Root == null, Is.True, "made and closed at once");
            Assert.That(Prefixed(PropertyLayerRegistry.RuntimePrefix), Is.EqualTo(before), "nothing left");
        }

        // ---- names · flags by kind (a component's after Add) · global · off at rest · the layer and the priority — the settings'
        // (a test context's UseSettings · a preview's defaults) and the preparation's order

        [Test]
        public void UO04a_NamesFlagsLayerAndPriority()
        {
            foreach (ContextKind kind in new[] { ContextKind.Test, ContextKind.Preview })
            {
                FeedbackScheduler s = Sched(kind);
                string prefix = Prefix(kind);
                var asset = ScriptableObject.CreateInstance<FeedbackSettings>();
                m_Made.Add(asset);
                asset.m_PostFXLayer = 4;
                asset.m_PostFXPriority = 500f;
                if (kind == ContextKind.Test)
                    s.Context.UseSettings(SettingsValues.From(asset));
                Player(s, With(new PostFXBloomItem(), TweenTo(1f), 0.1f), With(new PostFXVignetteItem(), TweenTo(1f), 0.1f));
                PostFXOverlayService svc = PostFXOverlayService.Of(s.Context);
                Volume a = svc.RentedAt(0), b = svc.RentedAt(1);
                VolumeComponent c = a.sharedProfile.components[0];
                HideFlags hidden = kind == ContextKind.Preview ? HideFlags.HideAndDontSave : HideFlags.None;
                int layer = kind == ContextKind.Test ? 4 : 0;
                float priority = kind == ContextKind.Test ? 500f : 1000f;
                Assert.That(new[] { svc.Root.name, a.name, a.sharedProfile.name, c.name }.All(n => n.StartsWith(prefix, StringComparison.Ordinal)), Is.True, kind + ": prefixed");
                Assert.That((svc.Root.hideFlags, a.gameObject.hideFlags, a.sharedProfile.hideFlags), Is.EqualTo((hidden, hidden, hidden)), kind + ": the objects' flags");
                Assert.That(c.hideFlags, Is.EqualTo(kind == ContextKind.Preview ? HideFlags.HideAndDontSave : HideFlags.HideInHierarchy | HideFlags.HideInInspector), kind + ": the component's — Add's, or the preview's over them");
                Assert.That((svc.Root.layer, a.gameObject.layer, a.isGlobal), Is.EqualTo((layer, layer, true)), kind + ": the layer · global");
                Assert.That((a.priority, b.priority), Is.EqualTo((priority, priority + PostFXOverlayService.PriorityStep)), kind + ": the priority — the preparation's order");
                Assert.That((a.enabled, a.weight, svc.OnCount), Is.EqualTo((false, 0f, 0)), kind + ": off at rest");
            }
        }

        // ---- in play the root is in DontDestroyOnLoad (visible); after the exit nothing prefixed — the play's exit destroys the
        // volumes, profiles and components made in it

        [UnityTest]
        public IEnumerator UO05a_ThePlayExitLeavesNothing()
        {
            yield return new EnterPlayMode();
            var go = new GameObject("UO05 player");
            var p = go.AddComponent<FeedbackPlayer>();
            p.m_Items.Add(With(new PostFXBloomItem(), TweenTo(1f), 0.1f));
            p.EnsureRuntime();
            PostFXOverlayService svc = PostFXOverlayService.Of(FeedbackScheduler.Default.Context);
            string scene = svc.Root.scene.name;
            HideFlags flags = svc.Root.hideFlags;
            yield return null;
            yield return new ExitPlayMode();
            int left = Prefixed(PropertyLayerRegistry.RuntimePrefix);
            Assert.That((scene, flags), Is.EqualTo(("DontDestroyOnLoad", HideFlags.None)), "in play");
            Assert.That(left, Is.Zero, "after the exit — nothing prefixed");
        }

        // ---- the weight model on the stack — a Tween's way and end (a Revert during the play gives the base back · the end
        // stays: a Revert after it reaches nothing) · a weight past 1 kept to 1 · a Flash's rise and fall · a Punch's negative
        // half off · the category's scale · the same item's overlapping plays chain (1 − Π(1 − wᵢ))

        [Test]
        public void UO06a_TheWeightModel()
        {
            FeedbackScheduler s = Sched();
            float b = Render<Bloom>(c => c.intensity.value);
            FeedbackPlayer tween = Player(s, With(new PostFXBloomItem(), TweenTo(1f), 1f));
            PostFXOverlayService svc = PostFXOverlayService.Of(s.Context);
            Volume v = svc.RentedAt(0);
            tween.Play();
            Tick(s, 6);
            float wMid = v.weight, sMid = Render<Bloom>(c => c.intensity.value);
            tween.Stop(StopMode.Revert);
            Tick(s, 2);
            float wRevert = v.weight, sRevert = Render<Bloom>(c => c.intensity.value);
            Assert.That(wMid, Is.GreaterThan(0f).And.LessThan(1f), "mid-play");
            Assert.That(sMid, Is.EqualTo(Mathf.Lerp(b, 3f, wMid)).Within(1e-4f), "between — the stack lerps by the weight");
            Assert.That(new[] { wRevert, sRevert }, Is.EqualTo(new[] { 0f, b }).Within(1e-4f), "a Revert during the play gives the base back");

            FeedbackPlayer ended = Player(s, With(new PostFXBloomItem(), TweenTo(1f), 0.25f));
            Volume ev = svc.RentedAt(1);
            ended.Play();
            Tick(s, 20);
            float wEnd = ev.weight, sEnd = Render<Bloom>(c => c.intensity.value);
            ended.Stop(StopMode.Revert);
            Tick(s, 2);
            Assert.That(new[] { wEnd, sEnd }, Is.EqualTo(new[] { 1f, 3f }).Within(1e-4f), "a Tween's end is the target");
            Assert.That(ev.weight, Is.EqualTo(1f), "a Revert after the play's end reaches nothing — the end stays");

            FeedbackPlayer over = Player(s, With(new PostFXFilmGrainItem(), TweenTo(2f), 0.05f));
            over.Play();
            Tick(s, 6);
            Assert.That(svc.RentedAt(2).weight, Is.EqualTo(1f), "a weight past 1 is kept to 1");

            FeedbackPlayer flash = Player(s, With(new PostFXVignetteItem(), Flash(0.25f, 0.5f, 0.25f), 0.4f));
            Volume fv = svc.RentedAt(3);
            flash.Play();
            float peak = 0f, last = -1f;
            for (int i = 0; i < 30; i++)
            {
                Tick(s, 1);
                peak = Mathf.Max(peak, fv.weight);
                last = fv.weight;
            }
            Assert.That(peak, Is.EqualTo(1f).Within(1e-4f), "a Flash rises to 1");
            Assert.That((last, fv.enabled), Is.EqualTo((0f, false)), "and falls to 0 (off)");

            FeedbackPlayer punch = Player(s, With(new PostFXChromaticAberrationItem(), new PunchMotion { m_Params = new PunchParams { m_Amplitude = Vector4.one, m_Vibrato = 2f, m_Decay = 0f } }, 1f));
            Volume pv = svc.RentedAt(4);
            punch.Play();
            int on = 0, off = 0;
            float max = 0f;
            for (int i = 0; i < 60; i++)
            {
                Tick(s, 1);
                if (pv.enabled)
                    on++;
                else
                    off++;
                max = Mathf.Max(max, pv.weight);
            }
            Assert.That(on > 0 && max <= 1f, Is.True, "the punch's positive half weighs toward the fields");
            Assert.That(off, Is.GreaterThanOrEqualTo(20), "about half of the punch is off — the negative half is dropped, not turned");

            s.Context.Categories.SetScale(FeedbackCategory.PostFX, 0.5f);
            FeedbackPlayer scaled = Player(s, With(new PostFXChromaticAberrationItem(), Held(1f), 1f));
            scaled.Play();
            Tick(s, 6);
            Assert.That(svc.RentedAt(5).weight, Is.EqualTo(0.5f).Within(1e-4f), "the category's scale");
            s.Context.Categories.SetScale(FeedbackCategory.PostFX, 1f);

            FeedbackPlayer twice = Player(s, PlayPolicy.Overlap, With(new PostFXColorAdjustmentsItem(), Held(0.5f), 1f));
            Volume tv = svc.RentedAt(6);
            twice.Play();
            Tick(s, 3);
            float one = tv.weight;
            twice.Play();
            Tick(s, 3);
            Assert.That((one, tv.weight, svc.RentedCount), Is.EqualTo((0.5f, 0.75f, 7)), "two plays of an item — one volume, 1 − (1 − 0.5)²");
        }

        // ---- the pixels — a Color Adjustments filter of black darkens the render by its weight and gives it back at 0 · the
        // user's volume under it and its asset unchanged

        [Test]
        public void UO07a_ThePixels()
        {
            FeedbackScheduler s = Sched();
            Volume user = UserVolume(0.4f, Color.blue);
            Color baseColor = Pixel();
            var item = With(new PostFXColorAdjustmentsItem(), TweenTo(1f), 1f);
            item.m_PostExposure = new PostFXFloat(false, 0f);
            item.m_ColorFilter = new PostFXColor(true, Color.black);
            FeedbackPlayer p = Player(s, item);
            Volume v = PostFXOverlayService.Of(s.Context).RentedAt(0);
            p.Play();
            Tick(s, 30);
            float w = v.weight;
            Color mid = Pixel();
            Tick(s, 40);
            Color covered = Pixel();
            p.ReleaseRuntime();
            Color back = Pixel();
            var vignette = (Vignette)user.sharedProfile.components[0];
            Assert.That(baseColor.g, Is.GreaterThan(0.1f), "the render shows the clear color — the positive");
            Assert.That(covered.g, Is.LessThan(0.02f), "a filter of black at weight 1");
            Assert.That(mid.g, Is.GreaterThan(covered.g + 0.02f).And.LessThan(baseColor.g - 0.02f), "between at weight " + w);
            Assert.That(back.g, Is.EqualTo(baseColor.g).Within(1e-3f), "the base again when the player goes (its end with it)");
            Assert.That((vignette.intensity.value, vignette.color.value), Is.EqualTo((0.4f, Color.blue)), "the user's asset unchanged");
        }

        // ---- a field not chosen passes the volumes under it (the user's color — not the item's default) · a target past its
        // range kept (.value) · an edit takes at the next start

        [Test]
        public void UO08a_OnlyTheChosenFields()
        {
            FeedbackScheduler s = Sched();
            UserVolume(0.3f, Color.blue);
            Render<Vignette>(c => c.intensity.value);
            Assert.That(Stack<Vignette>().color.value, Is.EqualTo(Color.blue), "the user's color is the base");
            var item = With(new PostFXVignetteItem(), TweenTo(1f), 0.05f);
            item.m_IntensityTarget = new PostFXFloat(true, 2f);
            FeedbackPlayer p = Player(s, item);
            p.Play();
            Tick(s, 6);
            float kept = Render<Vignette>(c => c.intensity.value);
            Color notChosen = Stack<Vignette>().color.value;
            item.m_Color = new PostFXColor(true, Color.red);
            p.Play();
            Tick(s, 6);
            Render<Vignette>(c => c.intensity.value);
            Color chosen = Stack<Vignette>().color.value;
            Assert.That(kept, Is.EqualTo(1f).Within(1e-4f), "2 is kept to the range (.value)");
            Assert.That(notChosen, Is.EqualTo(Color.blue), "a field not chosen passes");
            Assert.That(new[] { chosen.r, chosen.g, chosen.b }, Is.EqualTo(new[] { 1f, 0f, 0f }).Within(1e-4f), "an edit takes at the next start");
        }

        // ---- the order — the preparation's: the later one is above · A rests (off) while B holds and comes back on, still
        // under B (unique priorities — the same priority would put the last enabled above) · two items on one parameter chain

        [Test]
        public void UO09a_TheOrder()
        {
            FeedbackScheduler s = Sched();
            Render<Vignette>(c => c.intensity.value);
            var ia = With(new PostFXVignetteItem(), Flash(0.1f, 0.8f, 0.1f), 1f);
            ia.m_IntensityTarget = new PostFXFloat(true, 0.8f);
            var ib = With(new PostFXVignetteItem(), Flash(0.05f, 0.9f, 0.05f), 3f);
            ib.m_IntensityTarget = new PostFXFloat(true, 0.3f);
            FeedbackPlayer a = Player(s, ia);
            FeedbackPlayer bp = Player(s, ib);
            Volume va = PostFXOverlayService.Of(s.Context).RentedAt(0);
            a.Play();
            bp.Play();
            Tick(s, 30);
            float both = Render<Vignette>(c => c.intensity.value);
            a.Stop(StopMode.Revert);
            Tick(s, 2);
            bool rested = !va.enabled;
            float onlyB = Render<Vignette>(c => c.intensity.value);
            a.Play();
            Tick(s, 30);
            float again = Render<Vignette>(c => c.intensity.value);
            Assert.That((rested, va.enabled), Is.EqualTo((true, true)), "A rested off and came back on");
            Assert.That(va.weight, Is.EqualTo(1f).Within(1e-4f), "at full weight");
            Assert.That(new[] { both, onlyB, again }, Is.All.EqualTo(0.3f).Within(1e-4f), "the later preparation is above — a rest and a replay keep it");
            s.Shutdown();

            FeedbackScheduler s2 = Sched();
            float bc = Render<ChromaticAberration>(c => c.intensity.value);
            Player(s2, With(new PostFXChromaticAberrationItem(), Held(0.5f), 1f)).Play();
            Player(s2, With(new PostFXChromaticAberrationItem(), Held(0.5f), 1f)).Play();
            Tick(s2, 3);
            Assert.That(Render<ChromaticAberration>(c => c.intensity.value), Is.EqualTo(bc + (1f - bc) * 0.75f).Within(1e-4f), "two items on a parameter chain — 0.5 · 0.5 → 0.75 of the way");
        }

        // ---- on and off — off at 0 · a player let go returns its volume clean (weight 0 · overrides off) · the end a Persist left
        // goes with the player · the next rent of the type takes the resting volume

        [Test]
        public void UO10a_OnOffAndTheReturn()
        {
            FeedbackScheduler s = Sched();
            float b = Render<Vignette>(c => c.intensity.value);
            FeedbackPlayer p = Player(s, With(new PostFXVignetteItem(), TweenTo(1f), 0.05f));
            PostFXOverlayService svc = PostFXOverlayService.Of(s.Context);
            Volume v = svc.RentedAt(0);
            p.Play();
            Tick(s, 6);
            float stays = Render<Vignette>(c => c.intensity.value);
            bool onAtEnd = v.enabled;
            p.ReleaseRuntime();
            float released = Render<Vignette>(c => c.intensity.value);
            bool clean = !v.enabled && v.weight == 0f && !v.sharedProfile.components[0].parameters.Any(x => x.overrideState);
            Assert.That(stays, Is.EqualTo(0.45f).Within(1e-4f), "a Tween's end stays");
            Assert.That(onAtEnd, Is.True, "on");
            Assert.That(released, Is.EqualTo(b).Within(1e-4f), "the player let go — its end goes with it");
            Assert.That((clean, svc.RentedCount, svc.OnCount), Is.EqualTo((true, 0, 0)), "returned clean · nothing rented · nothing on");
            Player(s, With(new PostFXVignetteItem(), TweenTo(1f), 0.05f));
            Assert.That((svc.RentedAt(0), svc.Made), Is.EqualTo((v, 1)), "the next rent takes the resting volume");
        }

        // ---- each item's fields reach its component — every field chosen with a value of its own, by the parameter of the
        // field's name; none chosen leaves every override off · Depth of Field's mode · Vignette's rounded

        static readonly Type[] s_ComponentItems =
        {
            typeof(PostFXBloomItem), typeof(PostFXChromaticAberrationItem), typeof(PostFXColorAdjustmentsItem), typeof(PostFXDepthOfFieldItem),
            typeof(PostFXFilmGrainItem), typeof(PostFXLensDistortionItem), typeof(PostFXMotionBlurItem), typeof(PostFXPaniniProjectionItem),
            typeof(PostFXVignetteItem), typeof(PostFXWhiteBalanceItem), typeof(PostFXChannelMixerItem), typeof(PostFXSplitToningItem),
            typeof(PostFXLiftGammaGainItem), typeof(PostFXShadowsMidtonesHighlightsItem),
        };

        static readonly Type[] s_FieldTypes = { typeof(PostFXFloat), typeof(PostFXColor), typeof(PostFXVector2), typeof(PostFXVector4) };

        // a value inside every parameter's range (Depth of Field's aperture and focal length start at 1)
        static object Chosen(FieldInfo f, bool on)
        {
            if (f.FieldType == typeof(PostFXFloat))
                return new PostFXFloat(on, f.Name == "m_Aperture" ? 2f : f.Name == "m_FocalLength" ? 50f : 0.15f);
            if (f.FieldType == typeof(PostFXColor))
                return new PostFXColor(on, new Color(0.25f, 0.5f, 0.75f, 1f));
            if (f.FieldType == typeof(PostFXVector2))
                return new PostFXVector2(on, new Vector2(0.3f, 0.6f));
            return new PostFXVector4(on, new Vector4(0.9f, 0.8f, 0.7f, 0.1f));
        }

        // m_IntensityTarget → intensity · m_PostExposure → postExposure
        static string ParameterOf(string field)
        {
            string n = field == "m_IntensityTarget" ? "Intensity" : field.Substring(2);
            return char.ToLowerInvariant(n[0]) + n.Substring(1);
        }

        static object ValueOf(object chosen)
        {
            switch (chosen)
            {
                case PostFXFloat f: return f.m_Value;
                case PostFXColor c: return c.m_Value;
                case PostFXVector2 v2: return v2.m_Value;
                default: return ((PostFXVector4)chosen).m_Value;
            }
        }

        [Test]
        public void UO12a_EachItemsFieldsReachItsComponent()
        {
            FeedbackScheduler s = Sched();
            var wrong = new List<string>();
            int checkedFields = 0;
            foreach (Type type in s_ComponentItems)
            {
                foreach (bool on in new[] { true, false })
                {
                    var item = (TargetItem)Activator.CreateInstance(type);
                    FieldInfo[] fields = type.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly).Where(f => s_FieldTypes.Contains(f.FieldType)).ToArray();
                    foreach (FieldInfo f in fields)
                        f.SetValue(item, Chosen(f, on));
                    if (item is PostFXDepthOfFieldItem dof)
                        dof.m_OverrideMode = on;
                    if (item is PostFXVignetteItem vig)
                        vig.m_OverrideRounded = on;
                    FeedbackPlayer p = Player(s, With(item, TweenTo(1f), 0.05f));
                    Volume v = ((PostFXBinding)p.Runtime.Binding(0)).Volume;
                    p.Play();
                    Tick(s, 2);
                    VolumeComponent c = v.sharedProfile.components[0];
                    foreach (FieldInfo f in fields)
                    {
                        FieldInfo pf = c.GetType().GetField(ParameterOf(f.Name), BindingFlags.Public | BindingFlags.Instance);
                        if (pf == null)
                        {
                            wrong.Add(type.Name + "." + f.Name + " has no parameter");
                            continue;
                        }
                        var parameter = (VolumeParameter)pf.GetValue(c);
                        object value = parameter.GetType().GetProperty("value").GetValue(parameter);
                        object want = ValueOf(f.GetValue(item));
                        checkedFields++;
                        if (parameter.overrideState != on || on && !Equals(value, want))
                            wrong.Add(type.Name + "." + f.Name + (on ? " → " + value + " (want " + want + ")" : " left on"));
                    }
                    p.ReleaseRuntime();
                }
            }
            Assert.That(checkedFields, Is.EqualTo(2 * 52), "every field, chosen and not");
            Assert.That(wrong, Is.Empty);
        }

        [Test]
        public void UO12b_TheFieldsNotInterpolated()
        {
            FeedbackScheduler s = Sched();
            Render<DepthOfField>(c => 0f);
            DepthOfFieldMode baseMode = Stack<DepthOfField>().mode.value;
            var dof = With(new PostFXDepthOfFieldItem(), TweenTo(1f), 1f);
            dof.m_Mode = PostFXDepthOfFieldMode.Bokeh;
            var vig = With(new PostFXVignetteItem(), TweenTo(1f), 1f);
            vig.m_OverrideRounded = true;
            vig.m_Rounded = true;
            FeedbackPlayer p = Player(s, dof, vig);
            p.Play();
            Tick(s, 6);
            Render<DepthOfField>(c => 0f);
            (DepthOfFieldMode, bool) during = (Stack<DepthOfField>().mode.value, Stack<Vignette>().rounded.value);
            p.Stop(StopMode.Revert);
            Tick(s, 2);
            Render<DepthOfField>(c => 0f);
            (DepthOfFieldMode, bool) after = (Stack<DepthOfField>().mode.value, Stack<Vignette>().rounded.value);
            Assert.That(baseMode, Is.EqualTo(DepthOfFieldMode.Off), "the base is off");
            Assert.That(during, Is.EqualTo((DepthOfFieldMode.Bokeh, true)), "taken at a weight above 0");
            Assert.That(after, Is.EqualTo((DepthOfFieldMode.Off, false)), "back at 0");
        }

        // ---- Volume Blend — the user's asset on the item's volume (sharedProfile — no copy) · the stack blends it by the
        // weight · the asset unchanged · let go with the player · another ScriptableObject refused (FB-Q03)

        [Test]
        public void UO13a_VolumeBlend()
        {
            FeedbackScheduler s = Sched();
            float b = Render<Vignette>(c => c.intensity.value);
            var asset = ScriptableObject.CreateInstance<VolumeProfile>();
            m_Made.Add(asset);
            Vignette av = asset.Add<Vignette>(false);
            m_Made.Add(av);
            av.intensity.overrideState = true;
            av.intensity.value = 0.66f;
            FeedbackPlayer p = Player(s, With(new PostFXVolumeBlendItem { m_Profile = asset }, TweenTo(0.5f), 0.05f));
            Volume v = PostFXOverlayService.Of(s.Context).RentedAt(0);
            p.Play();
            Tick(s, 6);
            float mid = Render<Vignette>(c => c.intensity.value);
            bool copied = v.HasInstantiatedProfile();
            Assert.That(v.sharedProfile, Is.SameAs(asset), "the asset itself");
            p.ReleaseRuntime();
            Assert.That(mid, Is.EqualTo(Mathf.Lerp(b, 0.66f, 0.5f)).Within(1e-4f), "blended by the weight");
            Assert.That((av.intensity.value, copied, v.sharedProfile == null), Is.EqualTo((0.66f, false, true)), "the asset unchanged · no copy · let go");
            var other = ScriptableObject.CreateInstance<FeedbackSettings>();
            m_Made.Add(other);
            LogAssert.ignoreFailingMessages = true;
            FeedbackPlayer q = Player(s, With(new PostFXVolumeBlendItem { m_Profile = other }, TweenTo(1f), 0.05f));
            FeedbackPlayer none = Player(s, With(new PostFXVolumeBlendItem(), TweenTo(1f), 0.05f));
            Assert.That((q.Runtime.Binding(0), none.Runtime.Binding(0)), Is.EqualTo(((ItemBinding)null, (ItemBinding)null)), "another ScriptableObject · none — refused");
        }

        // ---- Volume Weight — the user volume's weight moves and a Revert gives it back · its record row (the preview's
        // AnimationMode)

        [Test]
        public void UO14a_VolumeWeight()
        {
            FeedbackScheduler s = Sched();
            float b = Render<Vignette>(c => c.intensity.value);
            Volume user = UserVolume(0.7f, Color.black, 0f);
            var item = With(new PostFXVolumeWeightItem(), TweenTo(1f), 0.5f);
            item.m_Target = new TargetRef { m_Mode = TargetRefMode.Direct, m_Object = user, m_Path = "", m_Anchor = "" };
            FeedbackPlayer p = Player(s, item);
            p.Play();
            Tick(s, 6);
            float w = user.weight, on = Render<Vignette>(c => c.intensity.value);
            p.Stop(StopMode.Revert);
            Tick(s, 2);
            float back = Render<Vignette>(c => c.intensity.value);
            Assert.That(w, Is.GreaterThan(0f).And.LessThan(1f), "mid-play");
            Assert.That(new[] { on, user.weight, back }, Is.EqualTo(new[] { Mathf.Lerp(b, 0.7f, w), 0f, b }).Within(1e-4f), "the weight blends the user's volume · the Revert gives 0 back");
            Assert.That(PostFXOverlayService.Of(s.Context).RentedCount, Is.Zero, "no overlay volume");
            Assert.That(typeof(PostFXVolumeWeightSlot).GetCustomAttribute<RecordPathsAttribute>(), Is.Not.Null, "its record row");
        }

        // ---- the category — PostFX off holds a play · a Bloom Flash is the Flash category (the flash limit holds it, PostFX
        // off does not)

        [Test]
        public void UO15a_TheCategory()
        {
            var shown = new List<string>();
            foreach (string gate in new[] { "open", "postfx off", "limit 0" })
            {
                FeedbackScheduler s = Sched();
                if (gate == "postfx off")
                    s.Context.Categories.SetEnabled(FeedbackCategory.PostFX, false);
                else if (gate == "limit 0")
                    s.Context.Categories.SetFlashLimit(0);
                FeedbackPlayer p = Player(s, With(new PostFXBloomItem(), new FlashMotion { m_Params = FlashParams.Default }, 0.3f), With(new PostFXVignetteItem(), TweenTo(1f), 0.05f));
                PostFXOverlayService svc = PostFXOverlayService.Of(s.Context);
                p.Play();
                Tick(s, 6);
                shown.Add($"{gate}: bloom {svc.RentedAt(0).enabled} vignette {svc.RentedAt(1).enabled}");
            }
            Assert.That(shown, Is.EqualTo(new[] { "open: bloom True vignette True", "postfx off: bloom True vignette False", "limit 0: bloom False vignette True" }));
            Assert.That((new PostFXBloomItem { m_Motion = new FlashMotion() }.EffectiveCategory, new PostFXBloomItem { m_Motion = new TweenMotion() }.EffectiveCategory),
                Is.EqualTo((FeedbackCategory.Flash, FeedbackCategory.PostFX)));
        }

        // ---- the settings — the layer 0 ~ 31 · the priority finite (else the defaults) · UseSettings reaches the service

        [Test]
        public void UO16a_TheSettings()
        {
            var asset = ScriptableObject.CreateInstance<FeedbackSettings>();
            m_Made.Add(asset);
            var layers = new List<int>();
            foreach (int l in new[] { 0, 31, -1, 32 })
            {
                asset.m_PostFXLayer = l;
                layers.Add(SettingsValues.From(asset).PostFXLayer);
            }
            var priorities = new List<float>();
            foreach (float p in new[] { -5f, 2000f, float.NaN, float.PositiveInfinity, float.NegativeInfinity })
            {
                asset.m_PostFXPriority = p;
                priorities.Add(SettingsValues.From(asset).PostFXPriority);
            }
            Assert.That(layers, Is.EqualTo(new[] { 0, 31, 0, 0 }));
            Assert.That(priorities, Is.EqualTo(new[] { -5f, 2000f, 1000f, 1000f, 1000f }));
            Assert.That((SettingsValues.From(null).PostFXLayer, SettingsValues.From(null).PostFXPriority), Is.EqualTo((0, 1000f)), "the defaults");
            FeedbackScheduler s = Sched();
            asset.m_PostFXLayer = 5;
            asset.m_PostFXPriority = 50f;
            s.Context.UseSettings(SettingsValues.From(asset));
            Player(s, With(new PostFXBloomItem(), TweenTo(1f), 0.1f));
            Volume v = PostFXOverlayService.Of(s.Context).RentedAt(0);
            Assert.That((v.gameObject.layer, v.priority), Is.EqualTo((5, 50f)), "the settings reach the volumes");
        }

        // ---- the debugger reads the extensions made, never makes one — the overlay's name and rows

        [Test]
        public void UO17a_TheDebugger()
        {
            FeedbackScheduler s = Sched();
            var reader = new DebuggerReader();
            int services = s.Context.ServiceCount;
            reader.Read(s);
            Assert.That((reader.ExtensionNames.Count, s.Context.ServiceCount), Is.EqualTo((0, services)), "none made — none read, the read makes none");
            Player(s, With(new PostFXBloomItem(), TweenTo(1f), 0.1f));
            reader.Read(s);
            Assert.That(reader.ExtensionNames, Is.EqualTo(new[] { "PostFX overlay" }));
            Assert.That(reader.ExtensionRows[0].Select(r => r.Label + " " + r.Value),
                Is.EqualTo(new[] { "priority 1000", "layer 0", "volumes 1", "rented 1", "on 0", "initialized " + (VolumeManager.instance.isInitialized ? 1 : 0) }));
        }

        // ---- the preview — it moves on the hidden overlay · a user volume's weight comes back (its record) · the scene clean ·
        // nothing hidden left · the notices (the Scene view — a seam · the main camera · the effects the Game view or play draws) ·
        // a missed reload's recovery finds the root, the profiles and the components

        [Test]
        public void UO18a_ThePreviewAndItsNotices()
        {
            Func<double> clock = PreviewClock.Source;
            IPreviewRecordSource source = PreviewRecordPaths.Source;
            Func<bool> sceneView = PreviewNotices.SceneViewShowsPostFX;
            Scene active = SceneManager.GetActiveScene();
            double now = 0;
            File.WriteAllText(Path.GetFullPath(ScenePath), "%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n");
            AssetDatabase.ImportAsset(ScenePath, ImportAssetOptions.ForceUpdate);
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            try
            {
                PreviewClock.Source = () => now;
                PreviewRecordPaths.Source = new PreviewRecordPathTable();
                PreviewNotices.SceneViewShowsPostFX = () => false;
                OwnMainCamera(true, false);
                SceneManager.SetActiveScene(scene);
                var go = new GameObject("UO18 player");
                var volume = new GameObject("UO18 volume").AddComponent<Volume>();
                volume.isGlobal = true;
                volume.weight = 0f;
                var player = go.AddComponent<FeedbackPlayer>();
                var weight = With(new PostFXVolumeWeightItem(), TweenTo(1f), 1f);
                weight.m_Target = new TargetRef { m_Mode = TargetRefMode.Direct, m_Object = volume, m_Path = "", m_Anchor = "" };
                player.m_Items.Add(With(new PostFXBloomItem(), TweenTo(1f), 1f));
                player.m_Items.Add(With(new PostFXDepthOfFieldItem(), TweenTo(1f), 1f));
                player.m_Items.Add(With(new PostFXMotionBlurItem(), TweenTo(1f), 1f));
                player.m_Items.Add(weight);
                Assert.That(EditorSceneManager.SaveScene(scene), Is.True);
                Assert.That(PreviewSession.Start(player, default), Is.EqualTo(PreviewRefusal.None));
                for (int i = 0; i < 60; i++)
                {
                    now += 1.0 / 120.0;
                    PreviewSession.Current?.Update();
                }
                PostFXOverlayService svc = PostFXOverlayService.Of(PreviewSession.Current.Scheduler.Context);
                bool moved = svc.RentedAt(0).enabled && svc.RentedAt(0).weight > 0f && volume.weight > 0f;
                bool hidden = svc.Root.hideFlags == HideFlags.HideAndDontSave;
                var notices = new List<PreviewNotice>();
                PreviewNotices.Collect(PreviewSession.Current, notices);
                PreviewSession.Current.Stop();
                Assert.That((moved, hidden), Is.EqualTo((true, true)), "the overlay and the user volume moved — on the hidden overlay");
                Assert.That(notices.Where(n => n.Kind == PreviewNoticeKind.PostFXSceneView), Is.Not.Empty, "the Scene view's toggle");
                Assert.That(notices.Where(n => n.Kind == PreviewNoticeKind.PostFXCamera).Select(n => n.Text), Has.Some.Contains("post-processing is off"), "the main camera's post-processing");
                Assert.That(notices.Where(n => n.Kind == PreviewNoticeKind.PostFXGameView).Select(n => n.Text).Single(),
                    Is.EqualTo("Depth of Field, Panini Projection and Lens Distortion show in the Game view. Motion Blur draws in play only."), "the effects the Game view or play draws");
                Assert.That(volume.weight, Is.Zero, "the user volume's weight back");
                Assert.That(Prefixed(PropertyLayerRegistry.PreviewPrefix), Is.Zero, "nothing hidden left");
                Assert.That(scene.isDirty, Is.False, "the scene stays clean");
            }
            finally
            {
                PreviewSession.Current?.Stop();
                PreviewClock.Source = clock;
                PreviewRecordPaths.Source = source;
                PreviewNotices.SceneViewShowsPostFX = sceneView;
                if (active.IsValid())
                    SceneManager.SetActiveScene(active);
                if (scene.IsValid())
                    EditorSceneManager.CloseScene(scene, true);
                AssetDatabase.DeleteAsset(ScenePath);
            }
        }

        [Test]
        public void UO18b_AMissedReloadsRecovery()
        {
            FeedbackScheduler s = Sched(ContextKind.Preview);
            Player(s, With(new PostFXBloomItem(), TweenTo(1f), 0.1f), With(new PostFXVignetteItem(), TweenTo(1f), 0.1f));
            PostFXOverlayService svc = PostFXOverlayService.Of(s.Context);
            int made = Prefixed(PropertyLayerRegistry.PreviewPrefix);
            PreviewRecovery.Run();
            Assert.That(made, Is.EqualTo(7), "the root · two volumes, profiles and components");
            Assert.That(svc.Root == null, Is.True, "the root found by its prefix");
            Assert.That(Prefixed(PropertyLayerRegistry.PreviewPrefix), Is.Zero, "the profiles and the components by their types");
        }

        // ---- FB-Q01 ~ FB-Q04 — quiet without their items · the main camera's post-processing (its fix adds URP's data and
        // turns it on — one undo) · its volume mask (the fix adds the layer — one undo) · a blend's profile · a depth of field's mode

        [Test]
        public void UO19a_TheRules()
        {
            var go = new GameObject("UO19 host");
            m_Made.Add(go);
            var host = go.AddComponent<FeedbackPlayer>();
            Camera cam = OwnMainCamera(false, false);
            Assert.That(FeedbackValidator.Run(host).Where(r => r.Id.StartsWith("FB-Q", StringComparison.Ordinal)), Is.Empty, "quiet without their items");
            var other = ScriptableObject.CreateInstance<FeedbackSettings>();
            m_Made.Add(other);
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            m_Made.Add(profile);
            var noMode = With(new PostFXDepthOfFieldItem(), TweenTo(1f), 0.1f);
            noMode.m_OverrideMode = false;
            host.m_Items.AddRange(new FeedbackItem[]
            {
                With(new PostFXBloomItem(), TweenTo(1f), 0.1f),                                 // 0 Q01 · Q02
                With(new PostFXVolumeBlendItem(), TweenTo(1f), 0.1f),                           // 1 Q03 (none)
                With(new PostFXVolumeBlendItem { m_Profile = other }, TweenTo(1f), 0.1f),       // 2 Q03
                With(new PostFXVolumeBlendItem { m_Profile = profile }, TweenTo(1f), 0.1f),     // 3
                noMode,                                                                         // 4 Q04
                With(new PostFXDepthOfFieldItem(), TweenTo(1f), 0.1f),                          // 5
            });
            List<RuleResult> got = FeedbackValidator.Run(host).Where(r => r.Id.StartsWith("FB-Q", StringComparison.Ordinal)).ToList();
            Assert.That(got.Select(r => r.Id + "@" + r.Item + ":" + r.Severity).OrderBy(x => x, StringComparer.Ordinal),
                Is.EqualTo(new[] { "FB-Q01@0:Warning", "FB-Q03@1:Warning", "FB-Q03@2:Warning", "FB-Q04@4:Info" }), "no URP data — post-processing off (FB-Q02 waits for the data)");

            RuleResult q01 = got.First(r => r.Id == "FB-Q01");
            Undo.ClearAll();
            FeedbackValidator.Apply(q01, q01.Fixes.Single());
            bool fixedOn = cam.TryGetComponent(out UniversalAdditionalCameraData data) && data.renderPostProcessing;
            bool quiet = FeedbackValidator.Run(host).All(r => r.Id != "FB-Q01");
            Undo.PerformUndo();
            Assert.That((fixedOn, quiet, cam.TryGetComponent(out UniversalAdditionalCameraData _)), Is.EqualTo((true, true, false)), "FB-Q01's fix adds the data on — one undo takes it away");

            data = cam.gameObject.AddComponent<UniversalAdditionalCameraData>();
            data.renderPostProcessing = true;
            data.volumeLayerMask = 0;
            RuleResult q02 = FeedbackValidator.Run(host).Single(r => r.Id == "FB-Q02");
            Assert.That((q02.Item, q02.Severity), Is.EqualTo((0, RuleSeverity.Warning)), "the layer out of the mask");
            Undo.ClearAll();
            FeedbackValidator.Apply(q02, q02.Fixes.Single());
            int added = data.volumeLayerMask.value;
            Undo.PerformUndo();
            Assert.That((added, data.volumeLayerMask.value), Is.EqualTo((1, 0)), "FB-Q02's fix adds layer 0 — one undo takes it out");
        }

        // ---- the add menu's defaults (Tween to 1 · a Flash of 0.3 s to 1) · the shape-only notice beside a size field ·
        // the keep (the structs · the enum · the bools · a profile asset's reference)

        [Test]
        public void UO20a_DefaultsTheNoticeAndKeep()
        {
            var tween = (TweenMotion)MotionDefaults.Create(typeof(PostFXBloomItem), MotionKind.Tween, out float tweenDuration);
            var flash = (FlashMotion)MotionDefaults.Create(typeof(PostFXVignetteItem), MotionKind.Flash, out float flashDuration);
            MotionDefaults.Create(typeof(PostFXVolumeWeightItem), MotionKind.Flash, out float weightFlash);
            Assert.That((tween.m_Params.m_To.x, tweenDuration), Is.EqualTo((1f, -1f)), "a Tween to 1");
            Assert.That((flash.m_Params.m_Target.x, flashDuration, weightFlash), Is.EqualTo((1f, MotionDefaults.PostFXFlashDuration, MotionDefaults.PostFXFlashDuration)), "a Flash of 0.3 s to 1");

            var go = new GameObject("UO20 host");
            m_Made.Add(go);
            var host = go.AddComponent<FeedbackPlayer>();
            host.m_Items.Add(new PostFXBloomItem { m_Motion = MotionDefaults.Create(typeof(PostFXBloomItem), MotionKind.Punch, out _) });
            host.m_Items.Add(new PostFXBloomItem { m_Motion = MotionDefaults.Create(typeof(PostFXBloomItem), MotionKind.Curve, out _) });
            host.m_Items.Add(new PostFXBloomItem { m_Motion = MotionDefaults.Create(typeof(PostFXBloomItem), MotionKind.Tween, out _) });
            host.m_Items.Add(new LightIntensityItem { m_Motion = MotionDefaults.Create(typeof(LightIntensityItem), MotionKind.Punch, out _) });
            var so = new SerializedObject(host);
            var notices = new List<int>();
            for (int i = 0; i < 4; i++)
            {
                var body = new ItemBody();
                body.Build(so.FindProperty("m_Items").GetArrayElementAtIndex(i), "UO20", host);
                notices.Add(body.Query<HelpBox>().ToList().Count(h => h.text == ItemBody.ShapeOnlyNotice));
            }
            Assert.That(notices, Is.EqualTo(new[] { 1, 1, 0, 0 }), "a Punch's amplitude · a Curve's remap — once; a Tween · a light's Punch none");

            const string profilePath = "Assets/UO20Profile.asset";
            var asset = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(asset, profilePath);
            try
            {
                var keep = new GameObject("UO20 keep");
                m_Made.Add(keep);
                FeedbackPlayer k = keep.AddComponent<FeedbackPlayer>();
                var bloom = new PostFXBloomItem { m_IntensityTarget = new PostFXFloat(false, 7f), m_Tint = new PostFXColor(true, Color.green) };
                var dof = new PostFXDepthOfFieldItem { m_OverrideMode = false, m_Mode = PostFXDepthOfFieldMode.Bokeh, m_Aperture = new PostFXFloat(true, 9f) };
                var vig = new PostFXVignetteItem { m_OverrideRounded = true, m_Rounded = true, m_Center = new PostFXVector2(true, new Vector2(0.2f, 0.8f)) };
                var lgg = new PostFXLiftGammaGainItem { m_Lift = new PostFXVector4(true, new Vector4(0.1f, 0.2f, 0.3f, 0.4f)) };
                var blend = new PostFXVolumeBlendItem { m_Profile = asset };
                k.m_Items.AddRange(new FeedbackItem[] { bloom, dof, vig, lgg, blend });
                var into = new GameObject("UO20 into");
                m_Made.Add(into);
                FeedbackPlayer kept = into.AddComponent<FeedbackPlayer>();
                PlayModeRecord.Apply(kept, PlayModeRecord.Capture(k, false, out int unknown), out PlayModeRecord.Stats stats);
                Assert.That((unknown, stats.Dropped), Is.EqualTo((0, 0)));
                var kb = (PostFXBloomItem)kept.m_Items[0];
                var kd = (PostFXDepthOfFieldItem)kept.m_Items[1];
                var kv = (PostFXVignetteItem)kept.m_Items[2];
                var kl = (PostFXLiftGammaGainItem)kept.m_Items[3];
                Assert.That((kb.m_IntensityTarget.m_Override, kb.m_IntensityTarget.m_Value, kb.m_Tint.m_Override, kb.m_Tint.m_Value), Is.EqualTo((false, 7f, true, Color.green)), "the structs");
                Assert.That((kd.m_OverrideMode, kd.m_Mode, kd.m_Aperture.m_Value), Is.EqualTo((false, PostFXDepthOfFieldMode.Bokeh, 9f)), "the enum · the bool");
                Assert.That((kv.m_OverrideRounded, kv.m_Rounded, kv.m_Center.m_Value, kl.m_Lift.m_Value), Is.EqualTo((true, true, new Vector2(0.2f, 0.8f), new Vector4(0.1f, 0.2f, 0.3f, 0.4f))), "the vectors");
                Assert.That(((PostFXVolumeBlendItem)kept.m_Items[4]).m_Profile, Is.SameAs(asset), "the profile asset's reference");
            }
            finally
            {
                AssetDatabase.DeleteAsset(profilePath);
            }
        }

        // ---- a Revert with nothing playing changes nothing · a retrigger mid-play does not write over the base (the end is the
        // user's value) · a play's intensity weighs every field alike (the weight)

        [Test]
        public void UO23a_KnownDefectsDoNotCarryOver()
        {
            FeedbackScheduler s = Sched();
            UserVolume(0.3f, Color.blue);
            float b = Render<Vignette>(c => c.intensity.value);
            Color bc = Stack<Vignette>().color.value;
            var item = With(new PostFXVignetteItem(), Flash(0.25f, 0.5f, 0.25f), 0.4f);
            item.m_Color = new PostFXColor(true, Color.red);
            FeedbackPlayer p = Player(s, item);
            p.Stop(StopMode.Revert);
            Tick(s, 2);
            Assert.That((Render<Vignette>(c => c.intensity.value), Stack<Vignette>().color.value), Is.EqualTo((b, bc)), "a Revert with nothing playing changes nothing");
            p.Play();
            Tick(s, 10);
            p.Play();
            Tick(s, 40);
            Assert.That((Render<Vignette>(c => c.intensity.value), Stack<Vignette>().color.value), Is.EqualTo((b, bc)), "a retrigger mid-play ends at the user's values");
            p.Play(PlayArgs.Default.WithIntensity(0.5f));
            Tick(s, 12);
            float half = Render<Vignette>(c => c.intensity.value);
            Color halfColor = Stack<Vignette>().color.value;
            Assert.That(half, Is.EqualTo(Mathf.Lerp(b, 0.45f, 0.5f)).Within(1e-3f), "the intensity halfway");
            Assert.That(new[] { halfColor.r, halfColor.b }, Is.EqualTo(new[] { Mathf.Lerp(bc.r, 1f, 0.5f), Mathf.Lerp(bc.b, 0f, 0.5f) }).Within(1e-3f), "the color halfway too — the play's intensity is the weight's");
        }

        // ---- a context closed while an overlay is on — a Via Scripting camera's stack loses it (Shutdown updates it once)

        [Test]
        public void UO27a_ClosedWhileOn()
        {
            Camera cam = Cam();
            UniversalAdditionalCameraData data = cam.GetUniversalAdditionalCameraData();
            cam.SetVolumeFrameworkUpdateMode(VolumeFrameworkUpdateMode.ViaScripting);
            cam.UpdateVolumeStack(data);
            float Read() => data.volumeStack.GetComponent<Vignette>().intensity.value;
            float b = Read();
            FeedbackScheduler s = Sched();
            Player(s, With(new PostFXVignetteItem(), Flash(0.25f, 0.5f, 0.25f), 1f)).Play();
            Tick(s, 30);
            float on = Read();
            s.Shutdown();
            Assert.That(on, Is.GreaterThan(b + 0.1f), "seen while on");
            Assert.That(Read(), Is.EqualTo(b).Within(1e-4f), "lost after the close");
        }
    }
}
