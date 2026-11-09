using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.PackageManager.UI;
using UnityEngine;
using xpTURN.Feedback.Samples.Editor;

namespace SamplesCheck
{
    // tools/check-samples.sh's driver: every sample of the package imported over a previous import —
    // the user's path — and a player of 'Build Samples' for the tour (SAMPLES_KIND): Mono development or release, or an IL2CPP
    // arm64 development one
    public static class SamplesDriver
    {
        const string Record = "SamplesCheck.txt";

        static void Line(string s)
        {
            Debug.Log("SAMPLES " + s);
            File.AppendAllText(Record, s + "\n");
        }

        public static void Import()
        {
            int imported = 0;
            foreach (Sample sample in Sample.FindByPackage("com.xpturn.feedback", null))
            {
                bool ok = sample.Import(Sample.ImportOptions.OverridePreviousImports);
                Line($"import {sample.displayName} ok={ok}");
                if (ok)
                    imported++;
            }
            AssetDatabase.Refresh();
            // the two settings files saved in this editor's format — the tool's byte comparison starts from them (another
            // version's clone holds the dev project's format until its first save)
            foreach (UnityEngine.Object settings in AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset"))
                EditorUtility.SetDirty(settings);
            EditorBuildSettings.scenes = EditorBuildSettings.scenes;
            AssetDatabase.SaveAssets();
            Line($"DONE import {imported}");
            EditorApplication.Exit(imported > 0 ? 0 : 1);
        }

        public static void Player()
        {
            string kind = Environment.GetEnvironmentVariable("SAMPLES_KIND");
            bool il2cpp = kind == "il2cpp";
            SampleBuild.Apply();
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, il2cpp ? ScriptingImplementation.IL2CPP : ScriptingImplementation.Mono2x);
            if (il2cpp)
                EditorUserBuildSettings.SetPlatformSettings("Standalone", "OSXUniversal", "Architecture", "ARM64");
            PlayerSettings.runInBackground = true;
            var options = new BuildPlayerOptions
            {
                scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray(),
                locationPathName = Environment.GetEnvironmentVariable("SAMPLES_OUT"),
                target = BuildTarget.StandaloneOSX,
                options = kind == "release" ? BuildOptions.None : BuildOptions.Development,
            };
            BuildReport report = BuildPipeline.BuildPlayer(options);
            Line($"player {kind} result={report.summary.result} scenes={options.scenes.Length} preloaded={PlayerSettings.GetPreloadedAssets().Length}");
            SampleBuild.Restore();
            EditorApplication.Exit(report.summary.result == BuildResult.Succeeded ? 0 : 1);
        }
    }
}
