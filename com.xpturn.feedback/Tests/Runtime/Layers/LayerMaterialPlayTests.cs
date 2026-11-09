using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.LowLevel;
using UnityEngine.Profiling;
using UnityEngine.Rendering;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace xpTURN.Feedback.Tests
{
    // the material row · the render path in real frames: Default is a test scheduler, the capture delta mixes frames with
    // and without FixedUpdate, and TearDown puts Default, the capture delta and the player loop back after a red test too.
    // The path is the graphics group — it draws in the window (a loop system at the end of PostLateUpdate — a coroutine's
    // camera.Render() comes before LateApply) and needs a graphics device (the Graphics category · ignored without one). No
    // UnityEditor here
    public class LayerMaterialPlayTests
    {
        struct DrawWindow { }

        static readonly int s_Base = Shader.PropertyToID("_BaseColor");
        static readonly int s_Smooth = Shader.PropertyToID("_Smoothness");
        static readonly Color s_C0 = new Color(0.8f, 0.3f, 0.2f, 1f);
        static readonly Color s_C1 = new Color(0.1f, 0.6f, 0.9f, 1f);
        static readonly MaterialPropertyBlock s_Probe = new MaterialPropertyBlock();
        static FeedbackScheduler s_Saved;
        static FeedbackScheduler s_Test;
        static Action s_Window;
        readonly List<Object> m_Made = new List<Object>();

        static void OnWindow() => s_Window?.Invoke();

        static void SetWindow(bool on)
        {
            PlayerLoopSystem loop = PlayerLoop.GetCurrentPlayerLoop();
            for (int i = 0; i < loop.subSystemList.Length; i++)
            {
                if (loop.subSystemList[i].type != typeof(UnityEngine.PlayerLoop.PostLateUpdate))
                    continue;
                var list = new List<PlayerLoopSystem>(loop.subSystemList[i].subSystemList);
                list.RemoveAll(s => s.type == typeof(DrawWindow));
                if (on)
                    list.Add(new PlayerLoopSystem { type = typeof(DrawWindow), updateDelegate = OnWindow });
                loop.subSystemList[i].subSystemList = list.ToArray();
            }
            PlayerLoop.SetPlayerLoop(loop);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            SetWindow(false);
            s_Window = null;
            if (s_Test != null)
            {
                FeedbackScheduler.Default = s_Saved;
                s_Test.Shutdown();
                s_Test = null;
                s_Saved = null;
            }
            Time.captureDeltaTime = 0f;
            foreach (Object o in m_Made)
                if (o != null)
                    Object.Destroy(o);
            m_Made.Clear();
            FrameRecorder.Clear();
            yield return null;
        }

        PropertyLayerRegistry Swap()
        {
            Time.captureDeltaTime = 1f / 90f;
            s_Saved = FeedbackScheduler.Default;
            s_Test = new FeedbackScheduler(ContextKind.Test);
            FeedbackScheduler.Default = s_Test;
            return s_Test.Context.Layers;
        }

        // a default cube with a material copy per color (URP Lit)
        MeshRenderer Cube(string name, params Color[] colors)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            m_Made.Add(go);
            var r = go.GetComponent<MeshRenderer>();
            var materials = new Material[colors.Length];
            for (int i = 0; i < colors.Length; i++)
            {
                materials[i] = new Material(r.sharedMaterial) { name = $"{name}-m{i}" };
                materials[i].SetColor(s_Base, colors[i]);
                m_Made.Add(materials[i]);
            }
            r.sharedMaterials = materials;
            return r;
        }

        static bool B(float a, float b) => BitConverter.SingleToInt32Bits(a) == BitConverter.SingleToInt32Bits(b);

        static bool B(Color a, Color b) => B(a.r, b.r) && B(a.g, b.g) && B(a.b, b.b) && B(a.a, b.a);

        static bool IndexHasColor(Renderer r, int index)
        {
            r.GetPropertyBlock(s_Probe, index);
            return s_Probe.HasColor(s_Base);
        }

        // the material row: three renderers — MPB whole · MPB index 1 · instance −1 over two colors — a sine offset and a
        // flash (override) over 30 frames with and without FixedUpdate: LateUpdate sees the base (LateApply is after it — an
        // instance copy shows its first color or that color written once, after the first restore); after the end no block
        // and each copy's color written once
        [UnityTest]
        public IEnumerator MP01_MaterialInRealFrames()
        {
            var layers = Swap();
            m_Made.Add(new GameObject(nameof(FrameRecorder), typeof(FrameRecorder)));
            var watcher = new GameObject(nameof(LayerMaterialWatcher), typeof(LayerMaterialWatcher)).GetComponent<LayerMaterialWatcher>();
            m_Made.Add(watcher.gameObject);
            var a = Cube("mp1a", s_C0);
            var b = Cube("mp1b", s_C0, s_C1);
            var c = Cube("mp1c", s_C0, s_C1);
            var sa = layers.GetOrCreate(a, s_Base, -1, () => new MaterialBlockColorSlot(a, s_Base, -1));
            var sb = layers.GetOrCreate(b, s_Base, 1, () => new MaterialBlockColorSlot(b, s_Base, 1));
            var sc = layers.GetOrCreate(c, s_Base, -1, () => new MaterialInstanceColorSlot(c, s_Base, -1));
            Color c0 = c.sharedMaterials[0].GetColor(s_Base), c1 = c.sharedMaterials[1].GetColor(s_Base);
            var scratch = new Material(c.sharedMaterials[0]);
            m_Made.Add(scratch);
            scratch.SetColor(s_Base, c0);
            Color o0 = scratch.GetColor(s_Base);
            scratch.SetColor(s_Base, c1);
            Color o1 = scratch.GetColor(s_Base);
            sa.Reserve(2);
            sb.Reserve(2);
            sc.Reserve(2);
            var ha = sa.Begin(ContributionKind.Offset);
            var hb = sb.Begin(ContributionKind.Offset);
            var hc = sc.Begin(ContributionKind.Offset);
            var fa = sa.Begin(ContributionKind.Override);
            var fb = sb.Begin(ContributionKind.Override);
            var fc = sc.Begin(ContributionKind.Override);
            int seen = 0, whole = 0, index = 0, instance = 0;
            watcher.OnLateUpdate = () =>
            {
                seen++;
                if (a.HasPropertyBlock())
                    whole++;
                if (IndexHasColor(b, 1))
                    index++;
                Color x0 = c.sharedMaterials[0].GetColor(s_Base), x1 = c.sharedMaterials[1].GetColor(s_Base);
                if (!(B(x0, c0) || B(x0, o0)) || !(B(x1, c1) || B(x1, o1)))
                    instance++;
            };
            FrameRecorder.Clear();
            int first = Time.frameCount;
            var flash = new Color(1f, 1f, 1f, 1f);
            for (int f = 0; f < 30; f++)
            {
                var o = new Color(0.2f * Mathf.Sin(f * 0.7f), 0.05f, 0f, 0f);
                float w = f % 4 < 2 ? 1f : 0f;
                sa.SetOffset(ha, o);
                sb.SetOffset(hb, o);
                sc.SetOffset(hc, o);
                sa.SetOverride(fa, flash, w);
                sb.SetOverride(fb, flash, w);
                sc.SetOverride(fc, flash, w);
                yield return null;
            }
            int last = Time.frameCount;
            watcher.OnLateUpdate = null;
            sa.End(ha);
            sb.End(hb);
            sc.End(hc);
            sa.End(fa);
            sb.End(fb);
            sc.End(fc);
            yield return null;
            yield return null;
            yield return null;

            var fixedFrames = new HashSet<int>();
            for (int i = 0; i < FrameRecorder.Record.Count; i++)
                if (FrameRecorder.Record[i] == "MB.FixedUpdate" && FrameRecorder.Frames[i] > first && FrameRecorder.Frames[i] < last)
                    fixedFrames.Add(FrameRecorder.Frames[i]);
            int span = last - first - 1;
            Assert.That(fixedFrames.Count > 0 && fixedFrames.Count < span, $"frames with FixedUpdate {fixedFrames.Count} of {span}");
            Assert.That(seen, Is.GreaterThanOrEqualTo(29), "LateUpdates seen");
            Assert.That(whole + index + instance, Is.EqualTo(0), $"LateUpdates that saw the composed value — whole {whole} · index {index} · instance {instance}");
            Assert.That(a.HasPropertyBlock(), Is.False, "whole — no block after the end");
            Assert.That(b.HasPropertyBlock(), Is.False, "index — no block after the end");
            Assert.That(B(c.sharedMaterials[0].GetColor(s_Base), o0) && B(c.sharedMaterials[1].GetColor(s_Base), o1), $"instance — each copy's color written once ({c.sharedMaterials[0].GetColor(s_Base)} · {c.sharedMaterials[1].GetColor(s_Base)})");
        }

        // the render path (the graphics group): by the draw markers — a renderer without a block: standard during the
        // effect, SRP Batcher after the end · one with another's index block only: SRP Batcher after the end and the other
        // taking its block off · an instance: SRP Batcher during the effect too
        [UnityTest]
        [Category("Graphics")]
        public IEnumerator MP02_PathInTheWindow()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                Assert.Ignore("no graphics device");
            var layers = Swap();
            foreach (Camera other in Camera.allCameras)
                other.enabled = false;
            var camObject = new GameObject("mp2 camera");
            m_Made.Add(camObject);
            var cam = camObject.AddComponent<Camera>();
            cam.transform.position = new Vector3(0, 0, -10);
            cam.orthographic = true;
            cam.orthographicSize = 2f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
            var rt = new RenderTexture(32, 32, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            m_Made.Add(rt);
            cam.targetTexture = rt;
            cam.enabled = false;
            Recorder srp = Recorder.Get("RenderLoop.DrawSRPBatcher");
            Recorder std = Recorder.Get("RenderLoop.Draw");
            srp.enabled = true;
            std.enabled = true;
            bool draw = false;
            s_Window = () =>
            {
                if (draw)
                    cam.Render();
            };
            SetWindow(true);
            yield return null;
            yield return null;
            var a = Cube("mp2a", s_C0);
            var d = Cube("mp2d", s_C0, s_C1);
            var c = Cube("mp2c", s_C0);
            var cubes = new[] { a, d, c };
            foreach (MeshRenderer r in cubes)
                r.enabled = false;
            int srpCount = 0, stdCount = 0;
            IEnumerator Draw(MeshRenderer visible)
            {
                foreach (MeshRenderer r in cubes)
                    r.enabled = ReferenceEquals(r, visible);
                draw = true;
                for (int i = 0; i < 3; i++)
                    yield return null;
                srpCount = srp.sampleBlockCount;
                stdCount = std.sampleBlockCount;
                draw = false;
            }

            var sa = layers.GetOrCreate(a, s_Base, -1, () => new MaterialBlockColorSlot(a, s_Base, -1));
            sa.Reserve(1);
            var ha = sa.Begin(ContributionKind.Offset);
            sa.SetOffset(ha, new Color(0.1f, 0f, 0f, 0f));
            yield return Draw(a);
            Assert.That(srpCount == 0 && stdCount > 0, $"no block before — standard during the effect (srp {srpCount} · std {stdCount})");
            sa.End(ha);
            yield return null;
            yield return null;
            yield return Draw(a);
            Assert.That(srpCount > 0 && stdCount == 0, $"no block before — SRP Batcher after the end (srp {srpCount} · std {stdCount})");

            var p = new MaterialPropertyBlock();
            p.SetFloat(s_Smooth, 0.2f);
            d.SetPropertyBlock(p, 1);
            var sd = layers.GetOrCreate(d, s_Base, -1, () => new MaterialBlockColorSlot(d, s_Base, -1));
            sd.Reserve(1);
            var hd = sd.Begin(ContributionKind.Offset);
            sd.SetOffset(hd, new Color(0.1f, 0f, 0f, 0f));
            yield return null;
            yield return null;
            sd.End(hd);
            yield return null;
            yield return null;
            d.SetPropertyBlock(null, 1);
            yield return Draw(d);
            Assert.That(srpCount > 0 && stdCount == 0, $"another's index block only — SRP Batcher after the end and its removal (srp {srpCount} · std {stdCount})");

            var sc = layers.GetOrCreate(c, s_Base, -1, () => new MaterialInstanceColorSlot(c, s_Base, -1));
            sc.Reserve(1);
            var hc = sc.Begin(ContributionKind.Offset);
            sc.SetOffset(hc, new Color(0.1f, 0f, 0f, 0f));
            yield return Draw(c);
            Assert.That(srpCount > 0 && stdCount == 0, $"instance — SRP Batcher during the effect (srp {srpCount} · std {stdCount})");
            sc.End(hc);
        }
    }
}
