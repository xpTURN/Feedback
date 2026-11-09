using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.LowLevel;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace xpTURN.Feedback.Tests
{
    // the transform rows in real frames: Default is a test scheduler, the capture delta mixes frames with and without
    // FixedUpdate, and TearDown puts Default, the capture delta and the loop back after a red test too. No UnityEditor here
    public class LayerTransformPlayTests
    {
        struct WindowWriter { }

        static Transform s_WindowTarget;
        static int s_WindowFrame;
        static Vector3 s_WindowPosition;
        static Quaternion s_WindowRotation;
        static Vector3 s_WindowReadPosition;
        static Quaternion s_WindowReadRotation;
        static bool s_WindowWrote;
        static FeedbackScheduler s_Saved;
        static FeedbackScheduler s_Test;
        readonly List<Object> m_Made = new List<Object>();

        // the window — after our LateApply, before the next Revert
        static void OnWindow()
        {
            if (s_WindowTarget == null || Time.frameCount != s_WindowFrame)
                return;
            s_WindowTarget.localPosition = s_WindowPosition;
            s_WindowReadPosition = s_WindowTarget.localPosition;
            s_WindowTarget.localRotation = s_WindowRotation;
            s_WindowReadRotation = s_WindowTarget.localRotation;
            s_WindowWrote = true;
        }

        // a loop system at the end of PostLateUpdate (WaitForEndOfFrame never resumes in batch mode)
        static void SetWindow(bool on)
        {
            PlayerLoopSystem loop = PlayerLoop.GetCurrentPlayerLoop();
            for (int i = 0; i < loop.subSystemList.Length; i++)
            {
                if (loop.subSystemList[i].type != typeof(UnityEngine.PlayerLoop.PostLateUpdate))
                    continue;
                var list = new List<PlayerLoopSystem>(loop.subSystemList[i].subSystemList);
                list.RemoveAll(s => s.type == typeof(WindowWriter));
                if (on)
                    list.Add(new PlayerLoopSystem { type = typeof(WindowWriter), updateDelegate = OnWindow });
                loop.subSystemList[i].subSystemList = list.ToArray();
            }
            PlayerLoop.SetPlayerLoop(loop);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            SetWindow(false);
            s_WindowTarget = null;
            s_WindowWrote = false;
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

        Transform T(string name, Transform parent = null)
        {
            var go = new GameObject(name);
            m_Made.Add(go);
            if (parent != null)
                go.transform.SetParent(parent, false);
            return go.transform;
        }

        static bool Bits(float a, float b) => BitConverter.SingleToInt32Bits(a) == BitConverter.SingleToInt32Bits(b);
        static bool Bits(Vector3 a, Vector3 b) => Bits(a.x, b.x) && Bits(a.y, b.y) && Bits(a.z, b.z);
        static bool Bits(Quaternion a, Quaternion b) => Bits(a.x, b.x) && Bits(a.y, b.y) && Bits(a.z, b.z) && Bits(a.w, b.w);

        static LocalPositionSlot Pos(PropertyLayerRegistry r, Transform t) => r.GetOrCreate(t, 0, 0, () => new LocalPositionSlot(t));
        static LocalRotationSlot Rot(PropertyLayerRegistry r, Transform t) => r.GetOrCreate(t, 0, 0, () => new LocalRotationSlot(t));
        static LocalScaleSlot Scl(PropertyLayerRegistry r, Transform t) => r.GetOrCreate(t, 0, 0, () => new LocalScaleSlot(t));

        // the value once written — N(q)
        Quaternion Once(Quaternion q)
        {
            var s = T("once");
            s.localRotation = q;
            return s.localRotation;
        }

        // a rotation (as Euler-made) whose read value does not settle in one write — N(N(q)) ≠ N(q) (not a fixed point)
        Quaternion Unsettled(int seed)
        {
            var s = T("unsettled");
            var rng = new System.Random(seed);
            for (int i = 0; i < 1000000; i++)
            {
                var e = new Vector3((float)(rng.NextDouble() * 360 - 180), (float)(rng.NextDouble() * 360 - 180), (float)(rng.NextDouble() * 360 - 180));
                s.localRotation = Quaternion.Euler(e);
                Quaternion q0 = s.localRotation;
                s.localRotation = q0;
                Quaternion n1 = s.localRotation;
                if (Bits(n1, q0))
                    continue;
                s.localRotation = n1;
                if (!Bits(s.localRotation, n1))
                    return Quaternion.Euler(e);
            }
            throw new InvalidOperationException("no unsettled rotation");
        }

        // the ends after N real frames — position Local · World (rotated · scaled parent) · the camera (Self position and
        // rotation) · rotation Local · World (starts that do not settle) · scale, and a position · rotation override; frames with
        // and without FixedUpdate mixed in
        [UnityTest]
        public IEnumerator TP01_EndsInRealFrames()
        {
            var reg = Swap();
            m_Made.Add(new GameObject(nameof(FrameRecorder), typeof(FrameRecorder)));
            var tLocal = T("local");
            tLocal.localPosition = new Vector3(1, 2, 3);
            var pWorld = T("parentWorld");
            pWorld.localRotation = Quaternion.Euler(17, 33, -8);
            pWorld.localScale = new Vector3(1.3f, 0.7f, 2.1f);
            var tWorld = T("world", pWorld);
            tWorld.localPosition = new Vector3(-1, 0.5f, 2);
            var cam = T("camera");
            cam.localPosition = new Vector3(0, 1, -10);
            cam.localRotation = Unsettled(11);
            var tRotL = T("rotationLocal");
            tRotL.localRotation = Unsettled(12);
            var pRot = T("parentRotation");
            pRot.localRotation = Quaternion.Euler(-20, 50, 10);
            var tRotW = T("rotationWorld", pRot);
            tRotW.localRotation = Unsettled(13);
            var tScale = T("scale");
            tScale.localScale = new Vector3(2, 3, 0.5f);

            Vector3 sLocal = tLocal.localPosition, sWorld = tWorld.localPosition, sCam = cam.localPosition, sScale = tScale.localScale;
            Quaternion nCam = Once(cam.localRotation), nRotL = Once(tRotL.localRotation), nRotW = Once(tRotW.localRotation);

            var pl = Pos(reg, tLocal);
            var pw = Pos(reg, tWorld);
            var pc = Pos(reg, cam);
            var rc = Rot(reg, cam);
            var rl = Rot(reg, tRotL);
            var rw = Rot(reg, tRotW);
            var sc = Scl(reg, tScale);
            pl.Reserve(2);
            pw.Reserve(1);
            pc.Reserve(1);
            rc.Reserve(1);
            rl.Reserve(2);
            rw.Reserve(1);
            sc.Reserve(1);
            var hl = pl.Begin(ContributionKind.Offset);
            var hlo = pl.Begin(ContributionKind.Override);
            var hw = pw.Begin(ContributionKind.Offset, LayerSpace.World);
            var hc = pc.Begin(ContributionKind.Offset, LayerSpace.Self);
            var hcr = rc.Begin(ContributionKind.Offset);
            var hrl = rl.Begin(ContributionKind.Offset);
            var hrlo = rl.Begin(ContributionKind.Override);
            var hrw = rw.Begin(ContributionKind.Offset, LayerSpace.World);
            var hs = sc.Begin(ContributionKind.Offset);
            FrameRecorder.Clear();
            int first = Time.frameCount;
            for (int f = 0; f < 30; f++)
            {
                float s = Mathf.Sin(f * 0.7f);
                pl.SetOffset(hl, new Vector3(0.1f * s, 0, 0));
                pl.SetOverride(hlo, new Vector3(5, 5, 5), 0.5f);
                pw.SetOffset(hw, new Vector3(0, 0.1f * s, 0));
                pc.SetOffset(hc, new Vector3(0.05f * s, 0.05f, 0));
                rc.SetOffset(hcr, Quaternion.Euler(0, 0, 2 * s));
                rl.SetOffset(hrl, Quaternion.Euler(3 * s, 0, 0));
                rl.SetOverride(hrlo, Quaternion.Euler(-40, 70, 15), 0.5f);
                rw.SetOffset(hrw, Quaternion.Euler(0, 2 * s, 1));
                sc.SetOffset(hs, Vector3.one * 0.2f * s);
                yield return null;
            }
            int last = Time.frameCount;
            pl.End(hl);
            pl.End(hlo);
            pw.End(hw);
            pc.End(hc);
            rc.End(hcr);
            rl.End(hrl);
            rl.End(hrlo);
            rw.End(hrw);
            sc.End(hs);
            yield return null;
            yield return null;
            yield return null;

            var fixedFrames = new HashSet<int>();
            for (int i = 0; i < FrameRecorder.Record.Count; i++)
                if (FrameRecorder.Record[i] == "MB.FixedUpdate" && FrameRecorder.Frames[i] > first && FrameRecorder.Frames[i] < last)
                    fixedFrames.Add(FrameRecorder.Frames[i]);
            int span = last - first - 1;
            Assert.That(fixedFrames.Count > 0 && fixedFrames.Count < span, $"frames with FixedUpdate {fixedFrames.Count} of {span}");
            Assert.That(Bits(tLocal.localPosition, sLocal), "position Local");
            Assert.That(Bits(tWorld.localPosition, sWorld), "position World");
            Assert.That(Bits(cam.localPosition, sCam), "camera position");
            Assert.That(Bits(cam.localRotation, nCam), "camera rotation = N(q0)");
            Assert.That(Bits(tRotL.localRotation, nRotL), "rotation Local = N(q0)");
            Assert.That(Bits(tRotW.localRotation, nRotW), "rotation World = N(q0)");
            Assert.That(Bits(tScale.localScale, sScale), "scale");
        }

        // the consumption stage — during the effect FixedUpdate · Update · LateUpdate see the base (Revert is before them,
        // LateApply after)
        [UnityTest]
        public IEnumerator TP02_CallbacksSeeTheBase()
        {
            var reg = Swap();
            var t = T("t");
            t.localPosition = new Vector3(1, 2, 3);
            var writer = t.gameObject.AddComponent<LayerWriter>();
            var pos = Pos(reg, t);
            pos.Reserve(1);
            var h = pos.Begin(ContributionKind.Offset);
            pos.SetOffset(h, new Vector3(0, 0.5f, 0));
            int first = Time.frameCount + 2;
            for (int f = 0; f < 12; f++)
                yield return null;
            int last = Time.frameCount - 1;
            pos.End(h);
            yield return null;
            yield return null;
            int seen = 0, composed = 0;
            foreach (var s in writer.Seen)
            {
                if (s.Frame < first || s.Frame > last)
                    continue;
                seen++;
                if (!Bits(s.Value, new Vector3(1, 2, 3)))
                    composed++;
            }
            Assert.That(seen, Is.GreaterThan(10), "callbacks seen");
            Assert.That(composed, Is.EqualTo(0), "callbacks that saw the composed value");
        }

        // the game's writes in Update and LateUpdate during the effect stay — the position bit-equal, the rotation = N(read)
        // (a game rotation that does not settle)
        [UnityTest]
        public IEnumerator TP03_GameWritesStay()
        {
            var reg = Swap();
            var writers = new LayerWriter[2];
            var positions = new LocalPositionSlot[2];
            var rotations = new LocalRotationSlot[2];
            var hp = new ContributionHandle[2];
            var hr = new ContributionHandle[2];
            Quaternion game = Unsettled(21);
            for (int i = 0; i < 2; i++)
            {
                var t = T("t" + i);
                t.localPosition = new Vector3(1, 2, 3);
                t.localRotation = Quaternion.Euler(10, 20, 30);
                var w = t.gameObject.AddComponent<LayerWriter>();
                w.Mode = i;
                w.Position = new Vector3(7 + i, 8, 9);
                w.Rotation = game;
                writers[i] = w;
                positions[i] = Pos(reg, t);
                rotations[i] = Rot(reg, t);
                positions[i].Reserve(1);
                rotations[i].Reserve(1);
                hp[i] = positions[i].Begin(ContributionKind.Offset);
                hr[i] = rotations[i].Begin(ContributionKind.Offset);
            }
            int at = Time.frameCount + 10;
            writers[0].AtFrame = at;
            writers[1].AtFrame = at;
            for (int f = 0; f < 20; f++)
            {
                for (int i = 0; i < 2; i++)
                {
                    positions[i].SetOffset(hp[i], new Vector3(0.1f * Mathf.Sin(f), 0.2f, 0));
                    rotations[i].SetOffset(hr[i], Quaternion.Euler(3f * Mathf.Sin(f), 1f, 0));
                }
                yield return null;
            }
            for (int i = 0; i < 2; i++)
            {
                positions[i].End(hp[i]);
                rotations[i].End(hr[i]);
            }
            yield return null;
            yield return null;
            yield return null;
            for (int i = 0; i < 2; i++)
            {
                string where = i == 0 ? "Update" : "LateUpdate";
                Assert.That(writers[i].Wrote, $"{where} wrote");
                Assert.That(Bits(writers[i].transform.localPosition, writers[i].ReadPosition), $"{where} position");
                Assert.That(Bits(writers[i].transform.localRotation, Once(writers[i].ReadRotation)), $"{where} rotation = N(read)");
            }
        }

        // a write in the window stays — the position bit-equal, the rotation = N(read)
        [UnityTest]
        public IEnumerator TP04_WindowWriteStays()
        {
            var reg = Swap();
            var t = T("t");
            t.localPosition = new Vector3(1, 2, 3);
            t.localRotation = Quaternion.Euler(10, 20, 30);
            var pos = Pos(reg, t);
            var rot = Rot(reg, t);
            pos.Reserve(1);
            rot.Reserve(1);
            var hp = pos.Begin(ContributionKind.Offset);
            var hr = rot.Begin(ContributionKind.Offset);
            s_WindowTarget = t;
            s_WindowFrame = Time.frameCount + 10;
            s_WindowPosition = new Vector3(7, 8, 9);
            s_WindowRotation = Unsettled(31);
            SetWindow(true);
            try
            {
                for (int f = 0; f < 20; f++)
                {
                    pos.SetOffset(hp, new Vector3(0.1f * Mathf.Sin(f), 0.2f, 0));
                    rot.SetOffset(hr, Quaternion.Euler(3f * Mathf.Sin(f), 1f, 0));
                    yield return null;
                }
                pos.End(hp);
                rot.End(hr);
                yield return null;
                yield return null;
                yield return null;
            }
            finally
            {
                SetWindow(false);
            }
            Assert.That(s_WindowWrote, "the window wrote");
            Assert.That(Bits(t.localPosition, s_WindowReadPosition), "position");
            Assert.That(Bits(t.localRotation, Once(s_WindowReadRotation)), "rotation = N(read)");
        }
    }
}
