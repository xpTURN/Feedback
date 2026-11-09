using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace PlayerGates
{
    // check-player-gates.sh's release probe: -executeMethod PlayerGates.ReleaseBuild.Build -releaseOut <path.app>
    // [-releaseDev] — an IL2CPP macOS arm64 player with the test assemblies (the release probe is in them and runs only
    // with its argument): a non-development one, or a development one for the positive control
    public static class ReleaseBuild
    {
        public static void Build()
        {
            string[] args = Environment.GetCommandLineArgs();
            string outPath = args[Array.IndexOf(args, "-releaseOut") + 1];
            bool development = Array.IndexOf(args, "-releaseDev") >= 0;
            EditorUserBuildSettings.SetPlatformSettings("Standalone", "OSXUniversal", "Architecture", "ARM64");
            var options = new BuildPlayerOptions
            {
                scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray(),
                locationPathName = outPath,
                target = BuildTarget.StandaloneOSX,
                options = BuildOptions.IncludeTestAssemblies | (development ? BuildOptions.Development : BuildOptions.None),
            };
            BuildReport report = BuildPipeline.BuildPlayer(options);
            Debug.Log("GATES release build " + report.summary.result + " seconds=" + (int)report.summary.totalTime.TotalSeconds + " options=" + report.summary.options);
            EditorApplication.Exit(report.summary.result == BuildResult.Succeeded ? 0 : 1);
        }
    }
}
