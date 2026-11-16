using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Scripting;
using xpTURN.Feedback.Editor;
using Object = UnityEngine.Object;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace xpTURN.Feedback.Tests.Editor
{
    // the Audio category — its saved value, the gate's arrays and the settings' rows · the editor's colour of every category ·
    // the Audio assembly's registration (the dev project's — DevProject)
    public sealed class AudioBaseTests
    {
        const string AudioAssembly = "xpTURN.Feedback.Audio";

        static FeedbackCategory[] Categories() => (FeedbackCategory[])Enum.GetValues(typeof(FeedbackCategory));

        static CategoryGate Gate(params CategoryDefault[] rows)
        {
            var asset = ScriptableObject.CreateInstance<FeedbackSettings>();
            var s = new FeedbackScheduler(ContextKind.Test);
            try
            {
                asset.m_Categories = rows;
                s.Context.UseSettings(SettingsValues.From(asset));
                return s.Context.Categories;
            }
            finally
            {
                s.Shutdown();
                Object.DestroyImmediate(asset);
            }
        }

        // the value is saved at the enum's end · the gate's arrays reach the last category · a settings row turns Audio off
        // or scales it · an asset from before the category (no Audio row) starts it on at 1
        [Test]
        public void AD01_TheCategoryAndTheSettings()
        {
            Assert.That((int)FeedbackCategory.Audio, Is.EqualTo(9));
            Assert.That((int)Categories().Max() + 1, Is.EqualTo(SettingsValues.CategoryCount), "the gate's arrays end at the last category");
            CategoryGate off = Gate(new CategoryDefault { m_Category = FeedbackCategory.Audio, m_Enabled = false, m_Scale = 0.5f });
            CategoryGate half = Gate(new CategoryDefault { m_Category = FeedbackCategory.Audio, m_Enabled = true, m_Scale = 0.5f });
            CategoryGate old = Gate(new CategoryDefault { m_Category = FeedbackCategory.Camera, m_Enabled = false, m_Scale = 0.25f });
            Assert.That((off.IsEnabled(FeedbackCategory.Audio), off.Scale(FeedbackCategory.Audio)), Is.EqualTo((false, 0f)), "the Audio row off");
            Assert.That((half.IsEnabled(FeedbackCategory.Audio), half.Scale(FeedbackCategory.Audio)), Is.EqualTo((true, 0.5f)), "the Audio row at 0.5");
            Assert.That((old.IsEnabled(FeedbackCategory.Audio), old.Scale(FeedbackCategory.Audio), old.Scale(FeedbackCategory.Camera)), Is.EqualTo((true, 1f, 0f)), "no Audio row");
        }

        // the inspector's strip and the sheet colour every category — one left out draws the None grey
        [Test]
        public void AD02_EveryCategoryHasItsEditorColour()
        {
            PackageInfo package = PackageInfo.FindForAssetPath("Packages/com.xpturn.feedback");
            Assert.That(package, Is.Not.Null, "com.xpturn.feedback is not registered");
            string sheet = File.ReadAllText(Path.Combine(package.resolvedPath, "Editor", "Styles", "xpTURN.Feedback.Inspector.uss"));
            Color grey = InspectorStyles.CategoryColor(FeedbackCategory.None);
            FeedbackCategory[] named = Categories().Where(c => c != FeedbackCategory.None).ToArray();
            Assert.That(named.Where(c => InspectorStyles.CategoryColor(c) == grey), Is.Empty, "the strip's colour");
            Assert.That(named.Select(InspectorStyles.CategoryColor).Distinct().Count(), Is.EqualTo(named.Length), "a colour of its own");
            Assert.That(named.Where(c => !sheet.Contains("." + InspectorStyles.CategoryClass(c) + " {")), Is.Empty, "the sheet's class");
        }

        // the Audio assembly compiles with its module, is always linked and registers the items whose [FeedbackTarget] names it
        [Test, Category(OptionalAssemblyTests.DevProjectCategory)]
        public void AD03_TheAudioAssemblysRegistration()
        {
            System.Reflection.Assembly audio = Type.GetType("xpTURN.Feedback.AudioImplementations, " + AudioAssembly)?.Assembly;
            Assert.That(audio, Is.Not.Null, "the Audio assembly is loaded");
            Assert.That(audio.IsDefined(typeof(AlwaysLinkAssemblyAttribute), false), Is.True, "always linked");
            Assert.That(audio.GetType("xpTURN.Feedback.AudioImplementations"), Is.Not.Null, "its registration");
            Type[] items = typeof(FeedbackItem).Assembly.GetTypes().Where(t => OptionalAssemblyTests.ImplementationOf(t) == AudioAssembly).ToArray();
            Assert.That(items.Where(t => !FeedbackImplementations.RegisteredTypes.Contains(t)).Select(t => t.Name), Is.Empty, "registered");
            Assert.That(items.Length, Is.EqualTo(14), "the Audio items");
        }
    }
}
