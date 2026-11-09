using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Events;
using UnityEditor.UnityLinker;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using xpTURN.Feedback.Editor;
using xpTURN.Feedback.Tests.Extension;
using Object = UnityEngine.Object;

namespace xpTURN.Feedback.Tests.Editor
{
    // the linker processor without a build: what one player gives, the link.xml, where it reads, the wiring. A row checks
    // everything and judges at the end. The scene processing is called as an internal function — a BuildReport cannot be
    // made. The saved set of a full build is keyed by build target; the rows use a key of their own and remove its file
    public sealed class UnityEventLinkerTests
    {
        readonly List<string> m_Fail = new List<string>();
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
            m_Fail.Clear();
        }

        void Check(bool ok, string what)
        {
            if (!ok)
                m_Fail.Add(what);
        }

        void End() => Assert.That(m_Fail, Is.Empty);

        internal sealed class Nested
        {
        }

        static UnityEventItem Calling(UnityEngine.Events.UnityAction call)
        {
            var item = new UnityEventItem();
            UnityEventTools.AddVoidPersistentListener(item.m_Event, call);
            return item;
        }

        static string Names(IEnumerable<string> set) => string.Join(" + ", set);

        // a call's method renamed — the processor keeps names, the call is not invoked
        static void Rename(FeedbackPlayer player, string method)
        {
            var so = new SerializedObject(player);
            so.FindProperty("m_Items.Array.data[0].m_Event.m_PersistentCalls.m_Calls.Array.data[0].m_MethodName").stringValue = method;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // ---- what one player gives — engine · script · a static argument · no call · no event · twice; a call whose
        // target is gone keeps its written type · a name that does not resolve is written as is
        [Test]
        public void GE2_Collect()
        {
            var go = new GameObject("ge2");
            m_Made.Add(go);
            new GameObject("child").transform.SetParent(go.transform, false);
            var player = go.AddComponent<FeedbackPlayer>();
            var gone = new GameObject("gone");
            Light light = gone.AddComponent<Light>();
            player.m_Items.Add(Calling(go.transform.DetachChildren));
            var stat = new UnityEventItem();
            UnityEventTools.AddStringPersistentListener(stat.m_Event, go.SendMessage, "x");
            player.m_Items.Add(stat);
            player.m_Items.Add(Calling(player.Signal));
            player.m_Items.Add(new UnityEventItem());
            player.m_Items.Add(new PositionItem());
            player.m_Items.Add(Calling(go.transform.DetachChildren));
            player.m_Items.Add(Calling(light.Reset));
            player.m_Items.Add(Calling(go.transform.DetachChildren));
            Object.DestroyImmediate(gone);
            var so = new SerializedObject(player);
            so.FindProperty("m_Items.Array.data[7].m_Event.m_PersistentCalls.m_Calls.Array.data[0].m_TargetAssemblyTypeName").stringValue = "Some.Missing, Missing";
            so.FindProperty("m_Items.Array.data[7].m_Event.m_PersistentCalls.m_Calls.Array.data[0].m_Target").objectReferenceValue = null;
            so.FindProperty("m_Items.Array.data[7].m_Event.m_PersistentCalls.m_Calls.Array.data[0].m_MethodName").stringValue = "Foo";
            so.ApplyModifiedPropertiesWithoutUndo();
            var set = new SortedSet<string>(StringComparer.Ordinal);
            UnityEventLinkerProcessor.Collect(new SerializedObject(player), set);
            string want = "Missing|Some.Missing|Foo + UnityEngine.CoreModule|UnityEngine.GameObject|SendMessage + UnityEngine.CoreModule|UnityEngine.Light|Reset + UnityEngine.CoreModule|UnityEngine.Transform|DetachChildren + xpTURN.Feedback|xpTURN.Feedback.FeedbackPlayer|Signal";
            Check(Names(set) == want, "the set — " + Names(set));
            // the written name of an engine type is the facade; the set has the defining assembly
            string written = new SerializedObject(player).FindProperty("m_Items.Array.data[0].m_Event.m_PersistentCalls.m_Calls.Array.data[0].m_TargetAssemblyTypeName").stringValue;
            Check(written == "UnityEngine.Transform, UnityEngine", "the written name — " + written);
            End();
        }

        // ---- the walk — every UnityEvent under an item: UnityEventItem · a Float Event's dynamic float and float static
        // listeners · a list of two · a nested class's UnityEvent<float> · a string holding the name is not entered
        [Test]
        public void GE7_TheWalk()
        {
            var go = new GameObject("ge7");
            m_Made.Add(go);
            var player = go.AddComponent<FeedbackPlayer>();
            Light light = go.AddComponent<Light>();
            var listener = go.AddComponent<FeedbackTestListener>();
            var plain = new UnityEventItem();
            UnityEventTools.AddVoidPersistentListener(plain.m_Event, go.transform.DetachChildren);
            player.m_Items.Add(plain);
            var floatEvent = new CustomFloatEventItem();
            UnityEventTools.AddPersistentListener(floatEvent.m_Event, (UnityEngine.Events.UnityAction<float>)Delegate.CreateDelegate(typeof(UnityEngine.Events.UnityAction<float>), light, typeof(Light).GetProperty("shadowNearPlane").GetSetMethod()));
            UnityEventTools.AddFloatPersistentListener(floatEvent.m_Event, (UnityEngine.Events.UnityAction<float>)Delegate.CreateDelegate(typeof(UnityEngine.Events.UnityAction<float>), light, typeof(Light).GetProperty("bounceIntensity").GetSetMethod()), 0.5f);
            player.m_Items.Add(floatEvent);
            var shapes = new LinkerShapesTestItem();
            var e1 = new UnityEngine.Events.UnityEvent();
            UnityEventTools.AddBoolPersistentListener(e1, go.SetActive, true);
            shapes.m_Events.Add(e1);
            var e2 = new UnityEngine.Events.UnityEvent();
            UnityEventTools.AddVoidPersistentListener(e2, light.Reset);
            shapes.m_Events.Add(e2);
            UnityEventTools.AddPersistentListener(shapes.m_Inner.m_OnValue, listener.Take);
            player.m_Items.Add(shapes);
            var set = new SortedSet<string>(StringComparer.Ordinal);
            UnityEventLinkerProcessor.Collect(new SerializedObject(player), set);
            string want = "UnityEngine.CoreModule|UnityEngine.GameObject|SetActive + UnityEngine.CoreModule|UnityEngine.Light|Reset + UnityEngine.CoreModule|UnityEngine.Light|set_bounceIntensity"
                          + " + UnityEngine.CoreModule|UnityEngine.Light|set_shadowNearPlane + UnityEngine.CoreModule|UnityEngine.Transform|DetachChildren + xpTURN.Feedback.Tests|xpTURN.Feedback.Tests.FeedbackTestListener|Take";
            Check(Names(set) == want, "the set — " + Names(set));
            End();
        }

        // ---- the walk takes a Random Unity Event's entries — each entry's listeners
        [Test]
        public void GE8_TheRandomUnityEventWalk()
        {
            var go = new GameObject("ge8");
            m_Made.Add(go);
            var player = go.AddComponent<FeedbackPlayer>();
            var camera = go.AddComponent<Camera>();
            var rue = new RandomUnityEventItem();
            var first = new WeightedUnityEvent();
            UnityEventTools.AddVoidPersistentListener(first.m_Event, go.transform.DetachChildren);
            rue.m_Entries.Add(first);
            var second = new WeightedUnityEvent();
            UnityEventTools.AddVoidPersistentListener(second.m_Event, camera.ResetAspect);
            rue.m_Entries.Add(second);
            player.m_Items.Add(rue);
            var set = new SortedSet<string>(StringComparer.Ordinal);
            UnityEventLinkerProcessor.Collect(new SerializedObject(player), set);
            string want = "UnityEngine.CoreModule|UnityEngine.Camera|ResetAspect + UnityEngine.CoreModule|UnityEngine.Transform|DetachChildren";
            Check(Names(set) == want, "the set — " + Names(set));
            End();
        }

        // ---- the link.xml — grouped and sorted · escaped · nested types with '/' · empty · the build's file
        [Test]
        public void GE3_LinkXml()
        {
            Check(UnityEventLinkerProcessor.LinkXml(new string[0]) == "<linker>\n</linker>\n", "empty");
            UnityEventLinkerProcessor.TryName(typeof(Nested).AssemblyQualifiedName, null, out string asm, out string type);
            var set = new SortedSet<string>(StringComparer.Ordinal)
            {
                "B|N.T|Run",
                "A|N.T|Go",
                "A|N.T|Go",
                "A|N.S|Stop",
                "A|N.T|a<b&\"c\"",
                asm + "|" + type + "|Nest",
            };
            string want = "<linker>\n"
                          + "  <assembly fullname=\"A\">\n"
                          + "    <type fullname=\"N.S\">\n      <method name=\"Stop\"/>\n    </type>\n"
                          + "    <type fullname=\"N.T\">\n      <method name=\"Go\"/>\n      <method name=\"a&lt;b&amp;&quot;c&quot;\"/>\n    </type>\n"
                          + "  </assembly>\n"
                          + "  <assembly fullname=\"B\">\n"
                          + "    <type fullname=\"N.T\">\n      <method name=\"Run\"/>\n    </type>\n"
                          + "  </assembly>\n"
                          + "  <assembly fullname=\"xpTURN.Feedback.Tests.Editor\">\n"
                          + "    <type fullname=\"xpTURN.Feedback.Tests.Editor.UnityEventLinkerTests/Nested\">\n      <method name=\"Nest\"/>\n    </type>\n"
                          + "  </assembly>\n"
                          + "</linker>\n";
            string xml = UnityEventLinkerProcessor.LinkXml(set);
            Check(xml == want, "the xml — " + xml.Replace("\n", "⏎"));
            // the build's end: the file and its full path — a scene's player gives the set a call (an empty set would not
            // tell an empty file from the set's)
            var go = new GameObject("ge3");
            m_Made.Add(go);
            go.AddComponent<FeedbackPlayer>().m_Items.Add(Calling(go.transform.DetachChildren));
            string key = "GE3-" + Guid.NewGuid().ToString("N");
            try
            {
                UnityEventLinkerProcessor.Begin(false, key);
                UnityEventLinkerProcessor.ProcessScene(SceneManager.GetActiveScene());
                string path = UnityEventLinkerProcessor.End();
                Check(UnityEventLinkerProcessor.Methods.Count > 0, "the scene gave a call");
                Check(path != null && Path.IsPathRooted(path) && path.Replace('\\', '/').EndsWith("Temp/xpTURN.Feedback/UnityEventItem.link.xml", StringComparison.Ordinal), "the path — " + path);
                Check(path != null && File.Exists(path) && File.ReadAllText(path) == UnityEventLinkerProcessor.LinkXml(UnityEventLinkerProcessor.Methods, UnityEventLinkerProcessor.ExtensionTypes()), "the file is the set's xml and the user's types");
            }
            finally
            {
                File.Delete(UnityEventLinkerProcessor.SavedPath(key));
            }
            End();
        }

        // ---- where it reads — Resources prefabs and what they use (not an unused prefab) · the scene's players,
        // inactive and disabled ones too · a prefab variant's override · a nested prefab · a scene instance's override · play
        // mode reads nothing · a new build starts empty · a scripts-only build merges the full build's set of its target,
        // twice, and warns with no full build of the target
        const string Root = "Assets/FeedbackGE4";

        static GameObject Prefab(string path, Func<GameObject, UnityEventItem> make)
        {
            var go = new GameObject(Path.GetFileNameWithoutExtension(path));
            new GameObject("child").transform.SetParent(go.transform, false);
            if (make != null)
                go.AddComponent<FeedbackPlayer>().m_Items.Add(make(go));
            GameObject asset = PrefabUtility.SaveAsPrefabAsset(go, path);
            Object.DestroyImmediate(go);
            return asset;
        }

        static bool Has(string method) => UnityEventLinkerProcessor.Methods.Any(e => e.EndsWith("|" + method, StringComparison.Ordinal));

        // what the processor reads of one prefab asset — its players with those of nested prefabs and inactive objects
        static string Read(string path)
        {
            var set = new SortedSet<string>(StringComparer.Ordinal);
            foreach (FeedbackPlayer p in AssetDatabase.LoadAssetAtPath<GameObject>(path).GetComponentsInChildren<FeedbackPlayer>(true))
                UnityEventLinkerProcessor.Collect(new SerializedObject(p), set);
            return string.Join(" + ", set.Select(e => e.Substring(e.LastIndexOf('|') + 1)));
        }

        [Test]
        public void GE4_WhereItReads()
        {
            AssetDatabase.DeleteAsset(Root);
            AssetDatabase.CreateFolder("Assets", "FeedbackGE4");
            AssetDatabase.CreateFolder(Root, "Resources");
            AssetDatabase.CreateFolder(Root, "Other");
            string key = "GE4-" + Guid.NewGuid().ToString("N");
            string other = "GE4-" + Guid.NewGuid().ToString("N");
            try
            {
                Prefab(Root + "/Resources/R1.prefab", go => Calling(go.transform.DetachChildren));
                GameObject used = Prefab(Root + "/Other/D1.prefab", go => Calling(go.transform.SetAsFirstSibling));
                var holder = new GameObject("R2");
                holder.AddComponent<LinkerHolder>().m_Target = used;
                PrefabUtility.SaveAsPrefabAsset(holder, Root + "/Resources/R2.prefab");
                Object.DestroyImmediate(holder);
                Prefab(Root + "/Other/U1.prefab", go => Calling(go.transform.SetAsLastSibling));
                // a variant whose call is overridden · a prefab holding the base as a nested child
                GameObject baseAsset = Prefab(Root + "/Other/Base.prefab", go => Calling(go.transform.DetachChildren));
                var variant = (GameObject)PrefabUtility.InstantiatePrefab(baseAsset);
                Rename(variant.GetComponent<FeedbackPlayer>(), "OverriddenInVariant");
                PrefabUtility.SaveAsPrefabAsset(variant, Root + "/Other/Variant.prefab");
                Object.DestroyImmediate(variant);
                var nest = new GameObject("Nest");
                ((GameObject)PrefabUtility.InstantiatePrefab(baseAsset)).transform.SetParent(nest.transform, false);
                PrefabUtility.SaveAsPrefabAsset(nest, Root + "/Other/Nest.prefab");
                Object.DestroyImmediate(nest);
                Check(PrefabUtility.GetPrefabAssetType(AssetDatabase.LoadAssetAtPath<GameObject>(Root + "/Other/Variant.prefab")) == PrefabAssetType.Variant, "the variant is a variant");
                Check(Read(Root + "/Other/Variant.prefab") == "OverriddenInVariant", "a variant's override — " + Read(Root + "/Other/Variant.prefab"));
                Check(Read(Root + "/Other/Nest.prefab") == "DetachChildren", "a nested prefab — " + Read(Root + "/Other/Nest.prefab"));
                // a build: Resources and what they use — not the unused prefab
                UnityEventLinkerProcessor.Begin(false, key);
                Check(Has("DetachChildren"), "a Resources prefab");
                Check(Has("SetAsFirstSibling"), "what a Resources prefab uses");
                Check(!Has("SetAsLastSibling"), "an unused prefab is out");
                // the scene: players on objects of the open scene — an instance with an override, an inactive object, a
                // disabled player
                var sceneObject = new GameObject("ge4 scene");
                m_Made.Add(sceneObject);
                var scenePlayer = sceneObject.AddComponent<FeedbackPlayer>();
                scenePlayer.m_Items.Add(Calling(scenePlayer.Signal));
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(baseAsset);
                m_Made.Add(instance);
                Rename(instance.GetComponent<FeedbackPlayer>(), "OverriddenInScene");
                var inactive = new GameObject("ge4 inactive");
                m_Made.Add(inactive);
                inactive.AddComponent<FeedbackPlayer>().m_Items.Add(Calling(inactive.transform.DetachChildren));
                Rename(inactive.GetComponent<FeedbackPlayer>(), "OnAnInactiveObject");
                inactive.SetActive(false);
                var disabled = new GameObject("ge4 disabled");
                m_Made.Add(disabled);
                FeedbackPlayer off = disabled.AddComponent<FeedbackPlayer>();
                off.m_Items.Add(Calling(disabled.transform.DetachChildren));
                Rename(off, "OnADisabledPlayer");
                off.enabled = false;
                var processor = new UnityEventLinkerProcessor();
                processor.OnProcessScene(SceneManager.GetActiveScene(), null);
                Check(!Has("Signal"), "entering play mode reads nothing");
                UnityEventLinkerProcessor.ProcessScene(SceneManager.GetActiveScene());
                Check(Has("Signal"), "the scene's player");
                Check(Has("OverriddenInScene"), "a scene instance's override");
                Check(Has("OnAnInactiveObject"), "an inactive object");
                Check(Has("OnADisabledPlayer"), "a disabled player");
                UnityEventLinkerProcessor.End();
                // the next build starts empty
                foreach (GameObject g in new[] { sceneObject, instance, inactive, disabled })
                    Object.DestroyImmediate(g);
                UnityEventLinkerProcessor.Begin(false, other);
                Check(!Has("Signal"), "a new build starts from nothing");
                UnityEventLinkerProcessor.End();
                File.Delete(UnityEventLinkerProcessor.SavedPath(other));
                // scripts-only for the full build's target: the saved set without a scene — twice
                UnityEventLinkerProcessor.Begin(true, key);
                Check(Has("Signal") && Has("OverriddenInScene"), "scripts-only merges the full build's set");
                UnityEventLinkerProcessor.End();
                UnityEventLinkerProcessor.Begin(true, key);
                Check(Has("Signal"), "a second scripts-only build still has it");
                UnityEventLinkerProcessor.End();
                // scripts-only for a target with no full build: a warning
                LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("scripts-only build with no full build"));
                UnityEventLinkerProcessor.Begin(true, other);
                Check(!Has("Signal"), "no full build of the target — nothing saved to merge");
            }
            finally
            {
                AssetDatabase.DeleteAsset(Root);
                File.Delete(UnityEventLinkerProcessor.SavedPath(key));
                File.Delete(UnityEventLinkerProcessor.SavedPath(other));
            }
            End();
        }

        // ---- the wiring the engine needs
        [Test]
        public void GE5_Wiring()
        {
            Type t = typeof(UnityEventLinkerProcessor);
            Check(typeof(IPreprocessBuildWithReport).IsAssignableFrom(t) && typeof(IProcessSceneWithReport).IsAssignableFrom(t) && typeof(IUnityLinkerProcessor).IsAssignableFrom(t), "three interfaces");
            Check(t.GetConstructor(Type.EmptyTypes) != null && !t.IsPublic, "a parameterless constructor · internal");
            Check(new UnityEventLinkerProcessor().callbackOrder == 0, "order 0");
            Check(TypeCache.GetTypesDerivedFrom<IUnityLinkerProcessor>().Contains(t) && TypeCache.GetTypesDerivedFrom<IProcessSceneWithReport>().Contains(t), "the type cache finds it");
            End();
        }

        // the build's link.xml keeps the user's feedback types whole (an asset bundle's would be stripped from Medium) — not
        // the bases. With the processor, out of the negative control's package
        [Test]
        public void GE9_TheUsersFeedbackTypes()
        {
            string xml = UnityEventLinkerProcessor.LinkXml(new string[0], UnityEventLinkerProcessor.ExtensionTypes());
            foreach (Type t in new[] { typeof(ProbeOneShot), typeof(ProbeValue), typeof(ProbeTimed) })
                Assert.That(xml, Does.Contain("<type fullname=\"" + t.FullName + "\" preserve=\"all\"/>"));
            Assert.That(xml, Does.Not.Contain("\"xpTURN.Feedback.CustomActionItem\""), "not the abstract bases");
            Assert.That(UnityEventLinkerProcessor.LinkXml(new string[0]), Is.EqualTo("<linker>\n</linker>\n"), "the methods alone");
            const string key = "GE9Probe";
            UnityEventLinkerProcessor.Begin(false, key);
            string path = UnityEventLinkerProcessor.End();
            string written = File.ReadAllText(path);
            File.Delete(UnityEventLinkerProcessor.SavedPath(key));
            Assert.That(written, Does.Contain("<type fullname=\"" + typeof(ProbeOneShot).FullName + "\" preserve=\"all\"/>"), "the build's file");
        }
    }
}
