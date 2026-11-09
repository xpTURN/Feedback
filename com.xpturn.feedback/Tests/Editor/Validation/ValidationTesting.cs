using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using xpTURN.Feedback.Editor;
using Object = UnityEngine.Object;

namespace xpTURN.Feedback.Tests.Editor
{
    // a rule from the test assembly — TypeCache brings it in. It reports on a host whose name starts with Z99; its fix
    // throws
    internal sealed class ValidationFakeRule : FeedbackRule
    {
        internal const string Prefix = "Z99";

        internal override string Id => "FB-Z99";
        internal override RuleSeverity Severity => RuleSeverity.Info;

        internal override void Check(RuleContext c, List<RuleResult> into)
        {
            if (!c.Host.name.StartsWith(Prefix, StringComparison.Ordinal))
                return;
            RuleResult r = Result(c, -1, "the test assembly's rule");
            r.Fixes.Add(new RuleFix("Throw", () => throw new InvalidOperationException("thrown on purpose")));
            into.Add(r);
        }
    }

    // a rule whose id is set — two of one id for the collection's check. TypeCache brings the default one in too
    internal sealed class ValidationIdRule : FeedbackRule
    {
        readonly string m_Id;

        public ValidationIdRule() : this("FB-Z98")
        {
        }

        internal ValidationIdRule(string id) => m_Id = id;

        internal override string Id => m_Id;
        internal override RuleSeverity Severity => RuleSeverity.Info;
    }

    // the fixtures of the validation's tests — a folder of assets and scenes removed after each test
    internal sealed class ValidationFixtures
    {
        internal const string Dir = "Assets/ValidationTests";

        internal readonly List<Object> Made = new List<Object>();
        // undone at the clean, the last first — a fixture's change to the scene's own objects
        internal readonly List<Action> Restore = new List<Action>();

        internal ValidationFixtures()
        {
            if (!AssetDatabase.IsValidFolder(Dir))
                AssetDatabase.CreateFolder("Assets", "ValidationTests");
        }

        internal FeedbackPlayer Player(string name, params FeedbackItem[] items)
        {
            var go = new GameObject(name);
            Made.Add(go);
            var p = go.AddComponent<FeedbackPlayer>();
            p.m_Items.AddRange(items);
            return p;
        }

        internal static T Item<T>(FeedbackMotion motion, TargetRefMode mode = TargetRefMode.Self) where T : TargetItem, new()
        {
            var t = new T { m_Motion = motion };
            t.m_Target.m_Mode = mode;
            return t;
        }

        internal static TweenMotion Tween => new TweenMotion { m_Params = new TweenParams { m_To = Vector4.one, m_Loops = 1 } };

        internal static PositionItem Position() => Item<PositionItem>(Tween);

        internal Scene Scene(string name)
        {
            string path = Dir + "/" + name + ".unity";
            File.WriteAllText(Path.GetFullPath(path), "%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n");
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            return EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
        }

        // a scene's player whose first slot's type does not exist (InspectorTesting's fixture)
        internal FeedbackPlayer MissingScenePlayer()
        {
            string path = Dir + "/Missing.unity";
            InspectorTesting.MissingScene(path);
            Scene s = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
            return s.GetRootGameObjects().First(g => g.name == "Missing").GetComponent<FeedbackPlayer>();
        }

        // a prefab asset whose player's first slot's type does not exist
        internal GameObject MissingPrefab(string name)
        {
            var src = new GameObject(name);
            src.AddComponent<FeedbackPlayer>().m_Items.Add(new WaitItem());
            string path = Dir + "/" + name + ".prefab";
            PrefabUtility.SaveAsPrefabAsset(src, path);
            Object.DestroyImmediate(src);
            Rename(path);
            return AssetDatabase.LoadAssetAtPath<GameObject>(path);
        }

        internal static void Rename(string path)
        {
            string full = Path.GetFullPath(path);
            File.WriteAllText(full, File.ReadAllText(full).Replace("class: WaitItem", "class: NoSuchItemType"));
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        }

        internal GameObject BasePrefab(string name, params string[] labels)
        {
            var src = new GameObject(name);
            var p = src.AddComponent<FeedbackPlayer>();
            foreach (string l in labels)
                p.m_Items.Add(new WaitItem { m_Label = l });
            GameObject asset = PrefabUtility.SaveAsPrefabAsset(src, Dir + "/" + name + ".prefab");
            Object.DestroyImmediate(src);
            return asset;
        }

        internal FeedbackPlayer Instance(GameObject asset)
        {
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(asset);
            Made.Add(inst);
            return inst.GetComponent<FeedbackPlayer>();
        }

        // an instance of [A, B, C] with C moved first
        internal FeedbackPlayer ReorderedInstance()
        {
            FeedbackPlayer ip = Instance(BasePrefab("Ordered", "A", "B", "C"));
            var so = new SerializedObject(ip);
            so.FindProperty("m_Items").MoveArrayElement(2, 0);
            so.ApplyModifiedPropertiesWithoutUndo();
            return ip;
        }

        // a variant of [A, B, C] with C moved first; the base then inserts D at 0
        internal FeedbackPlayer ReorderedVariant(bool insertD)
        {
            GameObject baseAsset = BasePrefab("VariantBase", "A", "B", "C");
            string variantPath = Dir + "/Variant.prefab";
            var vi = (GameObject)PrefabUtility.InstantiatePrefab(baseAsset);
            PrefabUtility.SaveAsPrefabAsset(vi, variantPath);
            Object.DestroyImmediate(vi);
            GameObject contents = PrefabUtility.LoadPrefabContents(variantPath);
            var cso = new SerializedObject(contents.GetComponent<FeedbackPlayer>());
            cso.FindProperty("m_Items").MoveArrayElement(2, 0);
            cso.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(contents, variantPath);
            PrefabUtility.UnloadPrefabContents(contents);
            if (insertD)
            {
                string basePath = AssetDatabase.GetAssetPath(baseAsset);
                GameObject bc = PrefabUtility.LoadPrefabContents(basePath);
                var bso = new SerializedObject(bc.GetComponent<FeedbackPlayer>());
                SerializedProperty list = bso.FindProperty("m_Items");
                list.InsertArrayElementAtIndex(0);
                list.GetArrayElementAtIndex(0).managedReferenceValue = new WaitItem { m_Label = "D" };
                bso.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(bc, basePath);
                PrefabUtility.UnloadPrefabContents(bc);
            }
            return AssetDatabase.LoadAssetAtPath<GameObject>(variantPath).GetComponent<FeedbackPlayer>();
        }

        // an instance with an old managed-reference path (unused) or an item field's override (used)
        internal FeedbackPlayer OverriddenInstance(bool unused)
        {
            GameObject baseAsset = BasePrefab("Overridden", "A");
            FeedbackPlayer ip = Instance(baseAsset);
            if (unused)
            {
                long rid = UnityEngine.Serialization.ManagedReferenceUtility.GetManagedReferenceIdForObject(ip, ip.m_Items[0]);
                List<PropertyModification> mods = PrefabUtility.GetPropertyModifications(ip.gameObject).ToList();
                mods.Add(new PropertyModification { target = baseAsset.GetComponent<FeedbackPlayer>(), propertyPath = "managedReferences[" + rid + "].m_OldLabel", value = "x" });
                PrefabUtility.SetPropertyModifications(ip.gameObject, mods.ToArray());
            }
            else
            {
                var so = new SerializedObject(ip);
                so.FindProperty("m_Items.Array.data[0].m_Label").stringValue = "valid";
                so.ApplyModifiedPropertiesWithoutUndo();
            }
            return ip;
        }

        // a variant asset whose player carries an old managed-reference path
        internal FeedbackPlayer UnusedVariant()
        {
            GameObject baseAsset = BasePrefab("UnusedBase", "A");
            var vi = (GameObject)PrefabUtility.InstantiatePrefab(baseAsset);
            var vip = vi.GetComponent<FeedbackPlayer>();
            long rid = UnityEngine.Serialization.ManagedReferenceUtility.GetManagedReferenceIdForObject(vip, vip.m_Items[0]);
            List<PropertyModification> mods = PrefabUtility.GetPropertyModifications(vi).ToList();
            mods.Add(new PropertyModification { target = baseAsset.GetComponent<FeedbackPlayer>(), propertyPath = "managedReferences[" + rid + "].m_OldLabel", value = "x" });
            PrefabUtility.SetPropertyModifications(vi, mods.ToArray());
            string path = Dir + "/UnusedVariant.prefab";
            PrefabUtility.SaveAsPrefabAsset(vi, path);
            Object.DestroyImmediate(vi);
            return AssetDatabase.LoadAssetAtPath<GameObject>(path).GetComponent<FeedbackPlayer>();
        }

        internal void Clean()
        {
            for (int i = Restore.Count - 1; i >= 0; i--)
                Restore[i]();
            Restore.Clear();
            foreach (Object o in Made)
                if (o != null)
                    Object.DestroyImmediate(o);
            Made.Clear();
            StageUtility.GoToMainStage();
            for (int i = SceneManager.sceneCount - 1; i >= 0; i--)
            {
                Scene s = SceneManager.GetSceneAt(i);
                if (s.path.StartsWith(Dir, StringComparison.Ordinal) && SceneManager.sceneCount > 1)
                    EditorSceneManager.CloseScene(s, true);
            }
            AssetDatabase.DeleteAsset(Dir);
            Undo.ClearAll();
        }

        // the main camera the fixture owns — the scene's own (the batch test scene has one) untagged until the clean · URP's
        // camera data by name when asked and there (post-processing as given)
        internal Camera OwnMainCamera(string name, bool cameraData, bool postProcessing = false)
        {
            foreach (Camera other in Camera.allCameras)
            {
                if (!other.CompareTag("MainCamera"))
                    continue;
                Camera c = other;
                c.tag = "Untagged";
                Restore.Add(() =>
                {
                    if (c != null)
                        c.tag = "MainCamera";
                });
            }
            var go = new GameObject(name);
            Made.Add(go);
            go.tag = "MainCamera";
            Camera cam = go.AddComponent<Camera>();
            Type type = Type.GetType(PostFXNames.CameraData);
            if (cameraData && type != null)
            {
                Component data = go.AddComponent(type);
                var so = new SerializedObject(data);
                so.FindProperty("m_RenderPostProcessing").boolValue = postProcessing;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
            return cam;
        }

        internal static List<RuleResult> Of(Object host, string id) => FeedbackValidator.Run(host).Where(r => r.Id == id).ToList();

        // every property, entering managed references: path = value (floats as bits, objects by entity id)
        internal static List<string> Snapshot(Object target)
        {
            var lines = new List<string>();
            SerializedProperty p = new SerializedObject(target).GetIterator();
            while (p.Next(true))
            {
                string v;
                switch (p.propertyType)
                {
                    case SerializedPropertyType.ObjectReference: v = p.objectReferenceValue != null ? p.objectReferenceValue.GetEntityId().ToString() : "null"; break;
                    case SerializedPropertyType.ManagedReference: v = p.managedReferenceId + " " + p.managedReferenceFullTypename; break;
                    case SerializedPropertyType.Float: v = BitConverter.SingleToInt32Bits(p.floatValue).ToString(CultureInfo.InvariantCulture); break;
                    case SerializedPropertyType.Integer:
                    case SerializedPropertyType.Enum: v = p.longValue.ToString(CultureInfo.InvariantCulture); break;
                    case SerializedPropertyType.Boolean: v = p.boolValue.ToString(); break;
                    case SerializedPropertyType.String: v = p.stringValue; break;
                    default: v = p.propertyType.ToString(); break;
                }
                lines.Add(p.propertyPath + " = " + v);
            }
            return lines;
        }
    }
}
