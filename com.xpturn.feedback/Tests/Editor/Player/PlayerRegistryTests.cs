using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace xpTURN.Feedback.Tests.Editor
{
    // the registry: every concrete effect item of Core is registered and makes its binding, an unregistered one makes none;
    // with the domain reload off the registry stays across three play entries and a real player prepares and plays each time
    // (nothing clears it on SubsystemRegistration). The settings go back in the tear-down (SessionState crosses the reloads);
    // a killed run leaves the settings file as it was
    public sealed class PlayerRegistryTests
    {
        const string SavedKey = "xpTURN.Feedback.Tests.PlayerRegistryTests.Saved";
        const string OptionsKey = "xpTURN.Feedback.Tests.PlayerRegistryTests.Options";
        const string EnabledKey = "xpTURN.Feedback.Tests.PlayerRegistryTests.Enabled";

        static List<Type> EffectItems()
            => typeof(FeedbackPlayer).Assembly.GetTypes().Where(t => !t.IsAbstract && typeof(EffectItem).IsAssignableFrom(t)).OrderBy(t => t.Name).ToList();

        [Serializable]
        sealed class Unregistered : ActionItem
        {
            internal override FeedbackCategory DeclaredCategory => FeedbackCategory.None;
            internal override float NaturalDuration => 0f;
        }

        [Test]
        public void FE02_EveryEffectItemIsRegistered()
        {
            var fails = new List<string>();
            List<Type> effects = EffectItems();
            IReadOnlyCollection<Type> registered = FeedbackImplementations.RegisteredTypes;
            // registered exactly when its implementing assembly compiles, and by that assembly (the registration wrapper's
            // factory — Core's are Core's)
            var factories = (System.Collections.IDictionary)typeof(FeedbackImplementations).GetField("s_Factories", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static).GetValue(null);
            foreach (Type t in effects)
            {
                bool expected = OptionalAssemblyTests.Implemented(t);
                string implementation = OptionalAssemblyTests.ImplementationOf(t);
                string want = implementation.Length > 0 ? implementation : typeof(FeedbackPlayer).Assembly.GetName().Name;
                if (registered.Contains(t) != expected)
                    fails.Add(t.Name + (expected ? " is not registered" : " is registered without " + implementation));
                else if (!expected)
                    continue;
                else if (FeedbackImplementations.Create((EffectItem)Activator.CreateInstance(t)) == null)
                    fails.Add(t.Name + " makes no binding");
                else if (Registrant((Delegate)factories[t]) != want)
                    fails.Add(t.Name + " registered by " + Registrant((Delegate)factories[t]));
            }
            // other tests register their own test items (no way to take one off) — of Core's types only its effect items
            foreach (Type t in registered.Where(t => t.Assembly == typeof(FeedbackPlayer).Assembly && !effects.Contains(t)))
                fails.Add(t.Name + " is registered and is not a Core effect item");
            if (effects.Count < 9)
                fails.Add("only " + effects.Count + " effect items");
            if (FeedbackImplementations.Create(new Unregistered()) != null)
                fails.Add("an unregistered item made a binding");
            Assert.That(fails, Is.Empty);
        }

        // the assembly of the factory a registration wrapped
        internal static string Registrant(Delegate wrapper)
        {
            System.Reflection.FieldInfo inner = wrapper.Target.GetType().GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)
                .First(f => typeof(Delegate).IsAssignableFrom(f.FieldType));
            return ((Delegate)inner.GetValue(wrapper.Target)).Method.DeclaringType.Assembly.GetName().Name;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (EditorApplication.isPlaying)
                yield return new ExitPlayMode();
            if (SessionState.GetBool(SavedKey, false))
            {
                EditorSettings.enterPlayModeOptions = (EnterPlayModeOptions)SessionState.GetInt(OptionsKey, 0);
                EditorSettings.enterPlayModeOptionsEnabled = SessionState.GetBool(EnabledKey, false);
                SessionState.EraseBool(SavedKey);
                SessionState.EraseInt(OptionsKey);
                SessionState.EraseBool(EnabledKey);
            }
        }

        [UnityTest]
        public IEnumerator FE03_RegistryAcrossThreeEntries()
        {
            SessionState.SetBool(SavedKey, true);
            SessionState.SetInt(OptionsKey, (int)EditorSettings.enterPlayModeOptions);
            SessionState.SetBool(EnabledKey, EditorSettings.enterPlayModeOptionsEnabled);
            EditorSettings.enterPlayModeOptionsEnabled = true;
            EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload;
            var fails = new List<string>();
            List<Type> effects = EffectItems().Where(OptionalAssemblyTests.Implemented).ToList();
            // the set as the run has it (other tests' test items too) — each entry keeps it
            var before = new HashSet<Type>(FeedbackImplementations.RegisteredTypes);
            for (int entry = 0; entry < 3; entry++)
            {
                yield return new EnterPlayMode(false);
                yield return null;
                int made = effects.Count(t => FeedbackImplementations.Create((EffectItem)Activator.CreateInstance(t)) != null);
                if (!before.SetEquals(FeedbackImplementations.RegisteredTypes) || made != effects.Count)
                    fails.Add("entry " + entry + ": registered " + FeedbackImplementations.RegisteredTypes.Count + " (before " + before.Count + ") · made " + made + "/" + effects.Count);
                var go = new GameObject("FE03");
                var player = go.AddComponent<FeedbackPlayer>();
                var item = new PositionItem { m_Motion = new PunchMotion { m_Params = new PunchParams { m_Amplitude = Vector4.one, m_Vibrato = 5f, m_Decay = 1f } }, m_Target = new TargetRef { m_Mode = TargetRefMode.Self, m_Path = "", m_Anchor = "" } };
                item.m_Timing.m_Duration = 0.5f;
                player.m_Items.Add(item);
                PlaybackHandle h = player.Play();
                yield return null;
                if (h == default(PlaybackHandle) || !player.IsPlaying)
                    fails.Add("entry " + entry + ": the player did not play");
                Object.Destroy(go);
                yield return new ExitPlayMode();
            }
            Assert.That(fails, Is.Empty);
        }
    }
}
