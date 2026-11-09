using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using xpTURN.Feedback.Editor;
using Object = UnityEngine.Object;
using F = xpTURN.Feedback.Tests.Editor.ValidationFixtures;

namespace xpTURN.Feedback.Tests.Editor
{
    // group H speaks in its cases only. H05's brain is Cinemachine's when the package is in, else the test assembly's stand-in
    // of the same short name (the short name finds the package's type when there is one)
    public sealed class LightCameraRulesTests
    {
        readonly List<Object> m_Made = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (Object o in m_Made)
            {
                if (o != null)
                    Object.DestroyImmediate(o);
            }
            m_Made.Clear();
        }

        Light Lamp(LightType type, LightShadows shadows = LightShadows.Soft)
        {
            var go = new GameObject("Light");
            m_Made.Add(go);
            Light light = go.AddComponent<Light>();
            light.type = type;
            light.shadows = shadows;
            return light;
        }

        Camera Cam(CameraClearFlags clear, bool orthographic = false)
        {
            var go = new GameObject("Camera");
            m_Made.Add(go);
            Camera camera = go.AddComponent<Camera>();
            camera.clearFlags = clear;
            camera.orthographic = orthographic;
            return camera;
        }

        static T On<T>(T item, Object target) where T : TargetItem
        {
            item.m_Target = new TargetRef { m_Mode = TargetRefMode.Direct, m_Object = target, m_Path = "", m_Anchor = "" };
            item.m_Motion = new TweenMotion();
            return item;
        }

        [Test]
        public void LQ11_GroupH()
        {
            Camera driven = Cam(CameraClearFlags.SolidColor);
            Type brain = Type.GetType("Unity.Cinemachine.CinemachineBrain, Unity.Cinemachine") ?? typeof(CinemachineBrain);
            driven.gameObject.AddComponent(brain);
            Camera orthographic = Cam(CameraClearFlags.SolidColor, true);
            var go = new GameObject("Player");
            m_Made.Add(go);
            var host = go.AddComponent<FeedbackPlayer>();
            host.m_Items.AddRange(new FeedbackItem[]
            {
                On(new LightSpotAngleItem(), Lamp(LightType.Point)),                          // 0 H01
                On(new LightSpotAngleItem(), Lamp(LightType.Spot)),                           // 1
                On(new LightShadowStrengthItem(), Lamp(LightType.Point, LightShadows.None)),  // 2 H02
                On(new LightShadowStrengthItem(), Lamp(LightType.Point)),                     // 3
                On(new CameraFieldOfViewItem(), orthographic),                                // 4 H03
                On(new CameraOrthographicSizeItem(), Cam(CameraClearFlags.SolidColor)),       // 5 H03
                On(new CameraZoomItem(), orthographic),                                       // 6 — a zoom reads the projection
                On(new CameraBackgroundColorItem(), Cam(CameraClearFlags.Skybox)),            // 7 H04
                On(new CameraBackgroundColorItem(), Cam(CameraClearFlags.SolidColor)),        // 8
                On(new CameraBackgroundColorItem(), Cam(CameraClearFlags.Depth)),             // 9 — URP clears to the color
                On(new CameraBackgroundColorItem(), Cam(CameraClearFlags.Nothing)),           // 10
                On(new CameraFieldOfViewItem(), driven),                                      // 11 H05
                On(new CameraClippingPlanesItem(), driven),                                   // 12 H05
                On(new CameraBackgroundColorItem(), driven),                                  // 13 — Cinemachine leaves the background
            });
            var want = new Dictionary<string, int[]>
            {
                { "FB-H01", new[] { 0 } }, { "FB-H02", new[] { 2 } }, { "FB-H03", new[] { 4, 5 } }, { "FB-H04", new[] { 7 } }, { "FB-H05", new[] { 11, 12 } },
            };
            foreach (KeyValuePair<string, int[]> w in want)
            {
                List<RuleResult> got = F.Of(host, w.Key);
                Assert.That(got.Select(r => r.Item).OrderBy(i => i), Is.EqualTo(w.Value), w.Key);
                RuleSeverity severity = w.Key == "FB-H05" ? RuleSeverity.Warning : RuleSeverity.Info;
                Assert.That(got.Select(r => r.Severity).Distinct(), Is.EqualTo(new[] { severity }), w.Key);
            }
        }

        // a target that does not resolve says nothing here — group A's
        [Test]
        public void LQ11_SilentWithoutATarget()
        {
            var go = new GameObject("Player");
            m_Made.Add(go);
            var host = go.AddComponent<FeedbackPlayer>();
            host.m_Items.Add(On(new LightSpotAngleItem(), null));
            host.m_Items.Add(On(new CameraBackgroundColorItem(), null));
            foreach (string id in new[] { "FB-H01", "FB-H02", "FB-H03", "FB-H04", "FB-H05" })
                Assert.That(F.Of(host, id), Is.Empty, id);
        }
    }
}
