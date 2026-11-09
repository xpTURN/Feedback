using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// the tour's editor side (tools/tour/record-tour.sh): Temp/PRTour/go opens the hub and enters play mode, rect.txt is the
// Game view's place on screen for the capture's crop, stop leaves play mode
[InitializeOnLoad]
static class PRTourEditor
{
    const string Hub = "Packages/com.xpturn.feedback/SampleKit/Hub/SampleHub.unity";
    static string Dir => Path.Combine(Path.GetDirectoryName(Application.dataPath), "Temp", "PRTour");
    static double s_PlayAt = -1;
    static bool s_Rect;

    static PRTourEditor()
    {
        Directory.CreateDirectory(Dir);
        File.WriteAllText(Path.Combine(Dir, "ready"), DateTime.Now.ToString("HH:mm:ss"));
        EditorApplication.update += Poll;
    }

    static string P(string name) => Path.Combine(Dir, name);

    static void Poll()
    {
        if (File.Exists(P("go")) && !EditorApplication.isPlayingOrWillChangePlaymode)
        {
            File.Delete(P("go"));
            for (int i = 0; i < EditorSceneManager.sceneCount; i++)
            {
                if (EditorSceneManager.GetSceneAt(i).isDirty)
                {
                    File.WriteAllText(P("error"), "unsaved changes in " + EditorSceneManager.GetSceneAt(i).path);
                    return;
                }
            }
            EditorSceneManager.OpenScene(Hub, OpenSceneMode.Single);
            File.WriteAllText(P("active"), "1");
            EditorApplication.EnterPlaymode();
            return;
        }
        if (EditorApplication.isPlaying && File.Exists(P("active")))
        {
            if (s_PlayAt < 0)
                s_PlayAt = EditorApplication.timeSinceStartup;
            if (!s_Rect && EditorApplication.timeSinceStartup - s_PlayAt > 2.0)
            {
                s_Rect = true;
                WriteRect();
            }
        }
        if (File.Exists(P("stop")))
        {
            File.Delete(P("stop"));
            File.Delete(P("active"));
            if (EditorApplication.isPlaying)
                EditorApplication.ExitPlaymode();
        }
    }

    static string R(Rect r) => string.Format(CultureInfo.InvariantCulture, "{0} {1} {2} {3}", r.x, r.y, r.width, r.height);

    static void WriteRect()
    {
        Type t = typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView");
        var b = new StringBuilder();
        b.Append("ppp ").Append(EditorGUIUtility.pixelsPerPoint.ToString(CultureInfo.InvariantCulture)).Append('\n');
        foreach (UnityEngine.Object o in Resources.FindObjectsOfTypeAll(t))
        {
            var w = (EditorWindow)o;
            w.Focus();
            b.Append("window ").Append(R(w.position)).Append(" maximized ").Append(w.maximized).Append('\n');
            for (Type x = t; x != null && x != typeof(EditorWindow); x = x.BaseType)
            {
                foreach (PropertyInfo p in x.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                {
                    if (p.GetIndexParameters().Length != 0 || (p.PropertyType != typeof(Rect) && p.PropertyType != typeof(Vector2)))
                        continue;
                    try
                    {
                        object v = p.GetValue(w);
                        b.Append("prop ").Append(x.Name).Append('.').Append(p.Name).Append(' ').Append(v is Rect r ? R(r) : ((Vector2)v).ToString("F2")).Append('\n');
                    }
                    catch (Exception)
                    {
                    }
                }
            }
        }
        File.WriteAllText(P("rect.txt"), b.ToString());
    }
}
