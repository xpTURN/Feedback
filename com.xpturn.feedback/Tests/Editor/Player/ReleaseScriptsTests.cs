using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Build.Player;
using UnityEditor.Compilation;
using UnityEngine;
using UnityEngine.TestTools;

namespace xpTURN.Feedback.Tests.Editor
{
    // the player scripts of a non-development build compile. The editor and the development players compile with DEBUG, so
    // a declaration under #if DEBUG that other code uses without it shows only here. Our assemblies are the ones the editor
    // knows for the player — fewer in a project without the optional packages
    public sealed class ReleaseScriptsTests
    {
        [Test]
        public void CK00_ReleasePlayerScriptsCompile()
        {
            List<string> want = CompilationPipeline.GetAssemblies(AssembliesType.PlayerWithoutTestAssemblies)
                .Select(a => a.name + ".dll").Where(n => n.StartsWith("xpTURN.")).OrderBy(n => n).ToList();
            var errors = new List<string>();
            Application.LogCallback grab = (condition, stack, type) =>
            {
                if (type == LogType.Error || type == LogType.Exception)
                    errors.Add(condition);
            };
            // the compiler's errors are the result here, not a failure of their own
            LogAssert.ignoreFailingMessages = true;
            Application.logMessageReceived += grab;
            ScriptCompilationResult result;
            try
            {
                result = PlayerBuildInterface.CompilePlayerScripts(new ScriptCompilationSettings
                {
                    group = BuildTargetGroup.Standalone,
                    target = BuildTarget.StandaloneOSX,
                    options = ScriptCompilationOptions.None
                }, Path.GetFullPath("Temp/xpTURN.Feedback.ReleaseScripts"));
            }
            finally
            {
                Application.logMessageReceived -= grab;
            }
            Assert.That(errors, Is.Empty, "the release player scripts");
            Assert.That(result.assemblies.Where(n => n.StartsWith("xpTURN.")).OrderBy(n => n).ToList(), Is.EqualTo(want), "our player assemblies");
        }
    }
}
