using System;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace xpTURN.Feedback.Tests.Editor
{
    // an EditMode test that enters Play mode in a non-batch editor: the game loop stops while the editor is out of focus (the
    // test framework turns Run In Background on for PlayMode runs only). Written in Play mode — the engine puts it back at the
    // exit, so the project's setting stays. A Play outside a test run is left alone
    [InitializeOnLoad]
    internal static class TestRunInBackground
    {
        // the framework's own answer (internal) — a cancelled run reports no run, and no callback says it ended
        internal static readonly Func<bool> RunActive = FindRunActive();

        static TestRunInBackground()
        {
            if (!Application.isBatchMode)
                EditorApplication.playModeStateChanged += OnPlayMode;
        }

        static Func<bool> FindRunActive()
        {
            MethodInfo method = typeof(TestRunnerApi).GetMethod("IsRunActive", BindingFlags.Static | BindingFlags.NonPublic);
            return method != null ? (Func<bool>)Delegate.CreateDelegate(typeof(Func<bool>), method) : null;
        }

        static void OnPlayMode(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.EnteredPlayMode && RunActive != null && RunActive() && !Application.runInBackground)
                Application.runInBackground = true;
        }
    }

    internal sealed class TestRunInBackgroundTests
    {
        [Test]
        public void TR01_TheFrameworkSaysARunIsActive()
            => Assert.That(TestRunInBackground.RunActive != null && TestRunInBackground.RunActive(), Is.True, "the test framework's internal IsRunActive");
    }
}
