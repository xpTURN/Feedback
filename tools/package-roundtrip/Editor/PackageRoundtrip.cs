using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using xpTURN.Feedback;

// check-package-roundtrip.sh: an optional package (Cinemachine) out of the project and back, with a player whose item
// points at that package's component — and, for the negative control, a saved type in the package's optional assembly
// (saved types belong in Core — here on purpose, from a package copy). The file path: Make → (the shell takes the package
// out of the manifest) Out → (back) Back. The open path: Open — the Package Manager window's call (Client.AddAndRemove)
// takes the package out and back in one editor session; the steps cross the domain reloads through SessionState. The
// player's fields are internal: reflection. Lines go to <project>/PackageRoundtrip.txt — "OK …" · "BAD …" · "NEGATIVE …" ·
// "DONE <step>"
[InitializeOnLoad]
public static class PackageRoundtrip
{
    const string Folder = "Assets/PackageRoundtrip";
    const string ScenePath = Folder + "/Roundtrip.unity";
    const string PrefabPath = Folder + "/Roundtrip.prefab";
    const string OutPath = "PackageRoundtrip.txt";
    const string PackageName = "com.unity.cinemachine";
    const string StepKey = "PackageRoundtrip.Step";
    const string IdKey = "PackageRoundtrip.Id";
    const string NegativeKey = "PackageRoundtrip.Negative";
    const float FileEdit = 0.25f;
    const float OpenEdit = 0.5f;
    const BindingFlags Any = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    static Request s_Request;

    static Type CameraType => Type.GetType("Unity.Cinemachine.CinemachineCamera, Unity.Cinemachine");
    static Type NegativeType => Type.GetType("xpTURN.Feedback.RoundtripNegativeItem, xpTURN.Feedback.Cinemachine");

    static void Line(string line)
    {
        File.AppendAllText(OutPath, line + "\n");
        Debug.Log("PackageRoundtrip " + line);
    }

    static void Expect(bool ok, string what) => Line((ok ? "OK " : "BAD ") + what);

    static string Arg(string name)
    {
        string[] args = Environment.GetCommandLineArgs();
        int i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : "";
    }

    static System.Collections.IList Items(FeedbackPlayer player) => (System.Collections.IList)typeof(FeedbackPlayer).GetField("m_Items", Any).GetValue(player);

    static FeedbackPlayer Owner(Scene scene)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            if (root.name == "owner")
                return root.GetComponent<FeedbackPlayer>();
        }
        return null;
    }

    static float Delay(FeedbackPlayer player) => new SerializedObject(player).FindProperty("m_StartDelay").floatValue;

    static object NewTargetItem(Component target)
    {
        var item = new PositionItem();
        typeof(TargetItem).GetField("m_Motion", Any).SetValue(item, new PunchMotion());
        object reference = typeof(TargetItem).GetField("m_Target", Any).GetValue(item);
        Type refType = reference.GetType();
        refType.GetField("m_Mode", Any).SetValue(reference, Enum.Parse(refType.GetField("m_Mode", Any).FieldType, "Direct"));
        refType.GetField("m_Object", Any).SetValue(reference, target);
        typeof(TargetItem).GetField("m_Target", Any).SetValue(item, reference);
        return item;
    }

    // the first item's target as it reads now: camera · placeholder (an Object standing for the missing component) · none
    static string Target(FeedbackPlayer player)
    {
        if (player == null || Items(player).Count == 0 || Items(player)[0] == null)
            return "none";
        object reference = typeof(TargetItem).GetField("m_Target", Any).GetValue(Items(player)[0]);
        var o = (UnityEngine.Object)reference.GetType().GetField("m_Object", Any).GetValue(reference);
        if (o == null)
            return "none";
        Type camera = CameraType;
        return camera != null && camera.IsInstanceOfType(o) ? "camera" : "placeholder";
    }

    // the negative control's item (the second): its value · null (the type is missing) · absent (no such item in this run)
    static string NegativeValue(FeedbackPlayer player)
    {
        if (player == null || Items(player).Count < 2)
            return "absent";
        object item = Items(player)[1];
        return item == null ? "null" : Convert.ToString(item.GetType().GetField("m_Value", Any)?.GetValue(item), System.Globalization.CultureInfo.InvariantCulture);
    }

    public static void Make()
    {
        File.Delete(OutPath);
        Type camera = CameraType;
        Expect(camera != null, "make — the package's component type is there");
        if (camera == null)
        {
            Line("DONE make");
            EditorApplication.Exit(1);
            return;
        }
        Directory.CreateDirectory(Folder);
        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        Component component = new GameObject("cm").AddComponent(camera);
        var player = new GameObject("owner").AddComponent<FeedbackPlayer>();
        Items(player).Add(NewTargetItem(component));
        if (NegativeType != null)
        {
            object negative = Activator.CreateInstance(NegativeType);
            NegativeType.GetField("m_Value", Any).SetValue(negative, 3f);
            Items(player).Add(negative);
        }
        EditorSceneManager.SaveScene(scene, ScenePath);
        var prefabOwner = new GameObject("prefab owner");
        var prefabCamera = new GameObject("prefab cm");
        prefabCamera.transform.SetParent(prefabOwner.transform, false);
        Items(prefabOwner.AddComponent<FeedbackPlayer>()).Add(NewTargetItem(prefabCamera.AddComponent(camera)));
        PrefabUtility.SaveAsPrefabAsset(prefabOwner, PrefabPath);
        UnityEngine.Object.DestroyImmediate(prefabOwner);
        AssetDatabase.SaveAssets();
        Expect(Target(player) == "camera", "make — the item points at the camera");
        Line("DONE make negative=" + NegativeValue(player));
        EditorApplication.Exit(0);
    }

    // the file path, the package out: the project opened without it — an edit of another field, saved
    public static void Out()
    {
        Expect(CameraType == null, "out — the package's component type is gone");
        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        FeedbackPlayer player = Owner(scene);
        Expect(Target(player) == "placeholder", "out — the reference stands as a placeholder (" + Target(player) + ")");
        var so = new SerializedObject(player);
        so.FindProperty("m_StartDelay").floatValue = FileEdit;
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorSceneManager.MarkSceneDirty(scene);
        Expect(EditorSceneManager.SaveScene(scene), "out — the edit saved");
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        Expect(prefab != null && Target(prefab.GetComponent<FeedbackPlayer>()) == "placeholder", "out — the prefab's reference stands as a placeholder");
        Line("DONE out negative=" + NegativeValue(player));
        EditorApplication.Exit(0);
    }

    // the file path, the package back
    public static void Back()
    {
        Expect(CameraType != null, "back — the package's component type is there");
        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        FeedbackPlayer player = Owner(scene);
        Expect(Target(player) == "camera", "back — the reference is the camera again (" + Target(player) + ")");
        Expect(Delay(player) == FileEdit, "back — the edit made while out stayed");
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        Expect(prefab != null && Target(prefab.GetComponent<FeedbackPlayer>()) == "camera", "back — the prefab's reference is the camera again");
        Line("DONE back negative=" + NegativeValue(player));
        EditorApplication.Exit(0);
    }

    // the open path: -roundtripVersion <the manifest's value> — the window's call out, an edit saved, the window's call back
    public static void Open()
    {
        File.Delete(OutPath);
        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        FeedbackPlayer player = Owner(scene);
        Expect(Target(player) == "camera", "open — the item points at the camera");
        SessionState.SetString(IdKey, PackageName + "@" + Arg("-roundtripVersion"));
        SessionState.SetBool(NegativeKey, NegativeType != null);
        SessionState.SetInt(StepKey, 1);
        s_Request = Client.AddAndRemove(null, new[] { PackageName });
        EditorApplication.update += Wait;
    }

    static PackageRoundtrip()
    {
        if (SessionState.GetInt(StepKey, 0) != 0)
            EditorApplication.delayCall += Continue;
    }

    static void Wait()
    {
        if (s_Request == null || !s_Request.IsCompleted)
            return;
        EditorApplication.update -= Wait;
        bool ok = s_Request.Status == StatusCode.Success;
        Expect(ok, "open — the window's call " + s_Request.Status + (s_Request.Error != null ? " " + s_Request.Error.message : ""));
        s_Request = null;
        if (!ok)
        {
            // no reload is coming: end now rather than at the shell's outer limit
            SessionState.EraseInt(StepKey);
            SessionState.EraseString(IdKey);
            SessionState.EraseBool(NegativeKey);
            EditorApplication.Exit(1);
        }
    }

    static void Continue()
    {
        int step = SessionState.GetInt(StepKey, 0);
        Scene scene = SceneManager.GetActiveScene();
        if (step == 1)
        {
            if (CameraType != null)
            {
                EditorApplication.delayCall += Continue;    // the reload is still to come
                return;
            }
            FeedbackPlayer player = Owner(scene);
            Expect(Target(player) == "placeholder", "open, out — the reference stands as a placeholder (" + Target(player) + ")");
            var so = new SerializedObject(player);
            so.FindProperty("m_StartDelay").floatValue = OpenEdit;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorSceneManager.MarkSceneDirty(scene);
            Expect(EditorSceneManager.SaveScene(scene), "open, out — the edit saved");
            SessionState.SetInt(StepKey, 2);
            s_Request = Client.AddAndRemove(new[] { SessionState.GetString(IdKey, PackageName) }, null);
            EditorApplication.update += Wait;
        }
        else if (step == 2)
        {
            if (CameraType == null || (SessionState.GetBool(NegativeKey, false) && NegativeType == null))
            {
                EditorApplication.delayCall += Continue;
                return;
            }
            FeedbackPlayer player = Owner(scene);
            Expect(Target(player) == "camera", "open, back — in memory the reference is the camera again");
            Expect(Delay(player) == OpenEdit, "open, back — in memory the edit stayed");
            string memory = NegativeValue(player);
            // what the session keeps in memory can differ from what it saved: read the file again
            FeedbackPlayer filed = Owner(EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single));
            Expect(Target(filed) == "camera", "open, back — in the file the reference is the camera");
            Expect(Delay(filed) == OpenEdit, "open, back — in the file the edit stayed");
            Line("NEGATIVE memory=" + memory + " file=" + NegativeValue(filed));
            SessionState.EraseInt(StepKey);
            SessionState.EraseString(IdKey);
            SessionState.EraseBool(NegativeKey);
            Line("DONE open");
            EditorApplication.Exit(0);
        }
    }
}
