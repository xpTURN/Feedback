using System;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace xpTURN.Feedback.Editor
{
    // a reload whose hook was missed leaves our driver in the mode — the lock held and the recorded values on the preview's —
    // and our hidden temporaries alive. After every load and reload, with no state but the names: our driver stopped and
    // destroyed, a HideAndDontSave GameObject with our prefix destroyed. Nothing else is touched — another name, a DontSave or
    // visible object, the runtime's prefix, another tool's driver. The RenderSettings values a preview wrote down in
    // SessionState go back in their scene, then a HideAndDontSave material with our prefix (a preview's copy — an instance, a
    // skybox) is destroyed; the line colors written down the same way go back on their renderers
    internal static class PreviewRecovery
    {
        // the driver's name starts with it too — a ScriptableObject, not found by the GameObject pass
        internal const string TemporaryPrefix = "xpTURN.Feedback.Preview.";

        [InitializeOnLoadMethod]
        static void Recover() => Run();

        internal static void Run()
        {
            foreach (AnimationModeDriver driver in Resources.FindObjectsOfTypeAll<AnimationModeDriver>())
            {
                if (driver == null || driver.name != PreviewDriver.Name)
                    continue;
                if (AnimationMode.InAnimationMode(driver))
                    AnimationMode.StopAnimationMode(driver);
                Object.DestroyImmediate(driver);
            }
            // the RenderSettings a missed end left, then our hidden copies · the line colors
            PreviewRenderSettings.Recover();
            PreviewLineColors.Recover();
            PreviewKeptValues.Recover();
            foreach (GameObject go in Resources.FindObjectsOfTypeAll<GameObject>())
            {
                if (go == null || go.hideFlags != HideFlags.HideAndDontSave || !go.name.StartsWith(TemporaryPrefix, StringComparison.Ordinal))
                    continue;
                Object.DestroyImmediate(go);
            }
        }
    }
}
