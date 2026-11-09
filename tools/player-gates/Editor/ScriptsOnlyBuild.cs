using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace PlayerGates
{
    // check-player-gates.sh's scripts-only build: -executeMethod PlayerGates.ScriptsOnlyBuild.Build -soOut <path.app>
    // -soMode full|scripts — a development IL2CPP player of the build scenes and the package's scene, or a scripts-only
    // one (it processes no scene — the linker processor merges the set the full build left for its target)
    public static class ScriptsOnlyBuild
    {
        const string Scene = "Packages/com.xpturn.feedback/Tests/Runtime/Player/Scenes/FeedbackLinkerScene.unity";

        public static void Build()
        {
            string[] args = Environment.GetCommandLineArgs();
            string outPath = args[Array.IndexOf(args, "-soOut") + 1];
            bool scriptsOnly = args[Array.IndexOf(args, "-soMode") + 1] == "scripts";
            EditorUserBuildSettings.SetPlatformSettings("Standalone", "OSXUniversal", "Architecture", "ARM64");
            string[] scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).Append(Scene).ToArray();
            BuildOptions options = BuildOptions.Development | (scriptsOnly ? BuildOptions.BuildScriptsOnly : BuildOptions.None);
            BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions { scenes = scenes, locationPathName = outPath, target = BuildTarget.StandaloneOSX, options = options });
            Debug.Log("GATES " + (scriptsOnly ? "scripts-only" : "full") + " build " + report.summary.result + " seconds=" + (int)report.summary.totalTime.TotalSeconds + " options=" + report.summary.options);
            EditorApplication.Exit(report.summary.result == BuildResult.Succeeded ? 0 : 1);
        }
    }
}
