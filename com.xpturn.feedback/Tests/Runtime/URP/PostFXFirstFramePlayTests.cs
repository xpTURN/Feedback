using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace xpTURN.Feedback.Tests
{
    // the Via Scripting row before the first render: the overlay on with no camera (nothing renders — the volume manager stays
    // down), then a Via Scripting camera: the frame it comes, the service's LateApply (before the render) scans it while the
    // manager is down — no error (the guard), and the camera sees the overlay once a render brought it up. Judged only in a run
    // where nothing rendered before it — a lone run; otherwise ignored
    public sealed class PostFXFirstFramePlayTests
    {
        [UnityTest]
        public IEnumerator UO11b_BeforeTheFirstRender()
        {
            if (VolumeManager.instance.isInitialized)
                Assert.Ignore("the volume manager is up — judged only in a run before the first render");
            FeedbackScheduler saved = FeedbackScheduler.Default;
            var test = new FeedbackScheduler(ContextKind.Test);
            FeedbackScheduler.Default = test;
            Time.captureDeltaTime = 1f / 60f;
            var pgo = new GameObject("UO11b player");
            pgo.SetActive(false);
            var p = pgo.AddComponent<FeedbackPlayer>();
            var item = new PostFXVignetteItem { m_Motion = new FlashMotion { m_Params = new FlashParams { m_Target = Vector4.one, m_Attack = 0.1f, m_Hold = 0.8f, m_Release = 0.1f } } };
            item.m_Timing.m_Duration = 1f;
            p.m_Items.Add(item);
            pgo.SetActive(true);
            p.EnsureRuntime();
            PostFXOverlayService svc = PostFXOverlayService.Of(test.Context);
            p.Play();
            for (int f = 0; f < 10; f++)
                yield return null;
            bool on = svc.OnCount > 0;
            bool before = VolumeManager.instance.isInitialized;
            var go = new GameObject("UO11b camera");
            Camera cam = go.AddComponent<Camera>();
            var rt = new RenderTexture(16, 16, 16);
            cam.targetTexture = rt;
            UniversalAdditionalCameraData data = cam.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = true;
            data.volumeLayerMask = 1;
            // Via Scripting without the extension (it updates the stack — URP's error while the manager is down)
            typeof(UniversalAdditionalCameraData).GetField("m_VolumeFrameworkUpdateModeOption", BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(data, VolumeFrameworkUpdateMode.ViaScripting);
            bool scripted = !data.requiresVolumeFrameworkUpdate;
            float max = 0f;
            for (int f = 0; f < 15; f++)
            {
                yield return null;
                if (data.volumeStack != null)
                    max = Mathf.Max(max, data.volumeStack.GetComponent<Vignette>().intensity.value);
            }
            bool after = VolumeManager.instance.isInitialized;
            int updates = svc.ViaScriptingUpdates;
            p.Stop();
            cam.targetTexture = null;
            Object.Destroy(go);
            Object.Destroy(pgo);
            Object.Destroy(rt);
            yield return null;
            FeedbackScheduler.Default = saved;
            if (!test.IsShutdown)
                test.Shutdown();
            Time.captureDeltaTime = 0f;
            Assert.That((scripted, on, before, after), Is.EqualTo((true, true, false, true)), "Via Scripting · on before the camera · the manager down then up");
            Assert.That(updates, Is.GreaterThan(0), "updated once a render brought it up");
            Assert.That(max, Is.GreaterThan(0.3f), "the camera saw the overlay");
        }
    }
}
