using System;
using System.Globalization;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace xpTURN.Feedback.Editor
{
    // a RenderSettings value a preview writes — the active scene's at the first write (a prefab stage's preview writes the
    // main scene's). A save goes back to it and on again; the end goes back. The value before is written down in SessionState
    // until the end: a reload whose hook was missed finds it and the recovery puts it back
    internal enum RenderSettingsKind
    {
        FogColor,
        FogDensity,
        AmbientColor,
        AmbientIntensity,
        Skybox,
    }

    // the skybox's row keeps the reference: a save during the preview writes the original, not the hidden copy ({fileID: 0})
    internal sealed class RenderSettingsState : IPreviewSaveState
    {
        readonly RenderSettingsKind m_Kind;
        readonly Scene m_Scene;
        readonly Color m_Color;
        readonly float m_Float;
        readonly Material m_Skybox;
        Color m_KeptColor;
        float m_KeptFloat;
        Material m_KeptSkybox;

        internal RenderSettingsState(RenderSettingsKind kind)
        {
            m_Kind = kind;
            m_Scene = SceneManager.GetActiveScene();
            switch (kind)
            {
                case RenderSettingsKind.FogColor: m_Color = RenderSettings.fogColor; break;
                case RenderSettingsKind.FogDensity: m_Float = RenderSettings.fogDensity; break;
                case RenderSettingsKind.AmbientColor: m_Color = RenderSettings.ambientLight; break;
                case RenderSettingsKind.AmbientIntensity: m_Float = RenderSettings.ambientIntensity; break;
                default: m_Skybox = RenderSettings.skybox; break;
            }
            SessionState.SetString(PreviewRenderSettings.Key(kind), m_Scene.path + "\n" + Text());
        }

        internal RenderSettingsKind Kind => m_Kind;

        public Scene Scene => m_Scene;

        // colors as four round-trip floats, the skybox as its GlobalObjectId (a built-in and an asset both resolve after a reload)
        string Text()
        {
            switch (m_Kind)
            {
                case RenderSettingsKind.FogColor:
                case RenderSettingsKind.AmbientColor:
                    return string.Join(";", F(m_Color.r), F(m_Color.g), F(m_Color.b), F(m_Color.a));
                case RenderSettingsKind.FogDensity:
                case RenderSettingsKind.AmbientIntensity:
                    return F(m_Float);
                default:
                    return m_Skybox != null ? GlobalObjectId.GetGlobalObjectIdSlow(m_Skybox).ToString() : "";
            }
        }

        static string F(float f) => f.ToString("R", CultureInfo.InvariantCulture);

        public void Revert()
        {
            PreviewRenderSettings.InScene(m_Scene, () =>
            {
                switch (m_Kind)
                {
                    case RenderSettingsKind.FogColor: m_KeptColor = RenderSettings.fogColor; RenderSettings.fogColor = m_Color; break;
                    case RenderSettingsKind.FogDensity: m_KeptFloat = RenderSettings.fogDensity; RenderSettings.fogDensity = m_Float; break;
                    case RenderSettingsKind.AmbientColor: m_KeptColor = RenderSettings.ambientLight; RenderSettings.ambientLight = m_Color; break;
                    case RenderSettingsKind.AmbientIntensity: m_KeptFloat = RenderSettings.ambientIntensity; RenderSettings.ambientIntensity = m_Float; break;
                    default: m_KeptSkybox = RenderSettings.skybox; RenderSettings.skybox = m_Skybox; break;
                }
            });
        }

        public void Reapply()
        {
            PreviewRenderSettings.InScene(m_Scene, () =>
            {
                switch (m_Kind)
                {
                    case RenderSettingsKind.FogColor: RenderSettings.fogColor = m_KeptColor; break;
                    case RenderSettingsKind.FogDensity: RenderSettings.fogDensity = m_KeptFloat; break;
                    case RenderSettingsKind.AmbientColor: RenderSettings.ambientLight = m_KeptColor; break;
                    case RenderSettingsKind.AmbientIntensity: RenderSettings.ambientIntensity = m_KeptFloat; break;
                    default:
                        if (m_KeptSkybox != null)
                            RenderSettings.skybox = m_KeptSkybox;
                        break;
                }
            });
        }
    }

    internal static class PreviewRenderSettings
    {
        const string Prefix = "xpTURN.Feedback.Preview.RenderSettings.";

        // the PostFX overlay's profiles and components — Core RP's types by name (null without it)
        static readonly Type[] s_PostFXTypes = { Type.GetType(PostFXNames.VolumeComponent), Type.GetType(PostFXNames.VolumeProfile) };

        internal static string Key(RenderSettingsKind kind) => Prefix + kind;

        // the slot-type table's maker: one state a kind for a session
        internal static void Add(RenderSettingsKind kind)
        {
            for (int i = 0; i < PreviewSaveState.Count; i++)
            {
                if (PreviewSaveState.At(i) is RenderSettingsState s && s.Kind == kind)
                    return;
            }
            PreviewSaveState.Add(new RenderSettingsState(kind));
        }

        // the session wrote RenderSettings
        internal static bool Touched()
        {
            for (int i = 0; i < PreviewSaveState.Count; i++)
            {
                if (PreviewSaveState.At(i) is RenderSettingsState)
                    return true;
            }
            return false;
        }

        // a write in a scene that is loaded but not active: made active for it and back (a preview scene · a closed one is
        // skipped — SetActiveScene throws for them)
        internal static void InScene(Scene scene, Action write)
        {
            if (!scene.IsValid() || !scene.isLoaded)
                return;
            Scene active = SceneManager.GetActiveScene();
            bool swap = active != scene;
            if (swap && !TryActivate(scene))
                return;
            try
            {
                write();
            }
            finally
            {
                if (swap)
                    TryActivate(active);
            }
        }

        static bool TryActivate(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded)
                return false;
            try
            {
                return SceneManager.SetActiveScene(scene);
            }
            catch (ArgumentException)
            {
                return false;
            }
        }

        // the end, after the states went back — the next load has nothing to put back
        internal static void Ended()
        {
            foreach (RenderSettingsKind kind in (RenderSettingsKind[])Enum.GetValues(typeof(RenderSettingsKind)))
                SessionState.EraseString(Key(kind));
        }

        // the recovery: what a missed end left — its scene loaded, the value back in it (else the record waits for a later
        // load); then our hidden materials go
        internal static void Recover()
        {
            foreach (RenderSettingsKind kind in (RenderSettingsKind[])Enum.GetValues(typeof(RenderSettingsKind)))
            {
                string record = SessionState.GetString(Key(kind), "");
                if (record.Length == 0)
                    continue;
                int cut = record.IndexOf('\n');
                string path = cut >= 0 ? record.Substring(0, cut) : "";
                Scene scene = default;
                for (int i = 0; i < SceneManager.sceneCount; i++)
                {
                    Scene at = SceneManager.GetSceneAt(i);
                    if (at.path == path)
                        scene = at;
                }
                if (!scene.IsValid() || !scene.isLoaded)
                    continue;
                string value = cut >= 0 ? record.Substring(cut + 1) : "";
                InScene(scene, () => Put(kind, value));
                SessionState.EraseString(Key(kind));
            }
            foreach (Material m in Resources.FindObjectsOfTypeAll<Material>())
            {
                if (m != null && m.hideFlags == HideFlags.HideAndDontSave && m.name.StartsWith(PreviewRecovery.TemporaryPrefix, StringComparison.Ordinal))
                    Object.DestroyImmediate(m);
            }
            // the screen overlay's hole texture of a missed reload (a prefixed ScriptableObject stays)
            foreach (Texture2D t in Resources.FindObjectsOfTypeAll<Texture2D>())
            {
                if (t != null && t.hideFlags == HideFlags.HideAndDontSave && t.name.StartsWith(PreviewRecovery.TemporaryPrefix, StringComparison.Ordinal))
                    Object.DestroyImmediate(t);
            }
            // the PostFX overlay's of a missed reload — by their types (a profile leaves its components · a prefixed
            // ScriptableObject stays)
            foreach (Type type in s_PostFXTypes)
            {
                if (type == null)
                    continue;
                foreach (Object o in Resources.FindObjectsOfTypeAll(type))
                {
                    if (o != null && o.hideFlags == HideFlags.HideAndDontSave && o.name.StartsWith(PreviewRecovery.TemporaryPrefix, StringComparison.Ordinal))
                        Object.DestroyImmediate(o);
                }
            }
        }

        static void Put(RenderSettingsKind kind, string value)
        {
            switch (kind)
            {
                case RenderSettingsKind.FogColor: RenderSettings.fogColor = ColorOf(value); break;
                case RenderSettingsKind.AmbientColor: RenderSettings.ambientLight = ColorOf(value); break;
                case RenderSettingsKind.FogDensity: RenderSettings.fogDensity = float.Parse(value, CultureInfo.InvariantCulture); break;
                case RenderSettingsKind.AmbientIntensity: RenderSettings.ambientIntensity = float.Parse(value, CultureInfo.InvariantCulture); break;
                default:
                    if (value.Length == 0)
                        RenderSettings.skybox = null;
                    else if (GlobalObjectId.TryParse(value, out GlobalObjectId id))
                        RenderSettings.skybox = GlobalObjectId.GlobalObjectIdentifierToObjectSlow(id) as Material;
                    break;
            }
        }

        static Color ColorOf(string value)
        {
            string[] p = value.Split(';');
            return new Color(float.Parse(p[0], CultureInfo.InvariantCulture), float.Parse(p[1], CultureInfo.InvariantCulture), float.Parse(p[2], CultureInfo.InvariantCulture), float.Parse(p[3], CultureInfo.InvariantCulture));
        }
    }
}
