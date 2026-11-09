using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace xpTURN.Feedback
{
    // no target is read (any target mode — a main camera is not waited for). A shader global's slot is (null, property id); a
    // RenderSettings value's and a skybox's are the active scene's (aux2 the scene) — the binding takes the slot of the scene
    // active at the prepare and, with nothing running, of the scene active at the next Begin
    internal static class RenderGlobalKeys
    {
        // Scene.handle is a SceneHandle in 6000.6 (its int is CS0619) — the hash is the handle in both
        internal static int Scene(Scene scene) => scene.GetHashCode();
    }

    internal sealed class ShaderGlobalFloatBinding : ValueTargetBinding<float, FloatOps>
    {
        readonly ShaderGlobalFloatItem m_Global;

        internal ShaderGlobalFloatBinding(ShaderGlobalFloatItem item) : base(item) => m_Global = item;

        protected override bool ReadsTarget => false;

        protected override bool Acquire(in PrepareContext context, bool direct, out ValueSlot<float, FloatOps> slot)
        {
            slot = null;
            if (string.IsNullOrEmpty(m_Global.m_PropertyName))
                return Refuse("name the shader global");
            int id = Shader.PropertyToID(m_Global.m_PropertyName);
            slot = Layers.GetOrCreate(null, id, 0, () => new ShaderGlobalFloatSlot(id));
            return true;
        }
    }

    internal sealed class ShaderGlobalColorBinding : ValueTargetBinding<Color, ColorOps>
    {
        readonly ShaderGlobalColorItem m_Global;

        internal ShaderGlobalColorBinding(ShaderGlobalColorItem item) : base(item) => m_Global = item;

        protected override bool ReadsTarget => false;

        protected override bool Acquire(in PrepareContext context, bool direct, out ValueSlot<Color, ColorOps> slot)
        {
            slot = null;
            if (string.IsNullOrEmpty(m_Global.m_PropertyName))
                return Refuse("name the shader global");
            int id = Shader.PropertyToID(m_Global.m_PropertyName);
            slot = Layers.GetOrCreate(null, id, 0, () => new ShaderGlobalColorSlot(id));
            return true;
        }
    }

    internal sealed class ShaderGlobalVectorBinding : ValueTargetBinding<Vector4, Vector4Ops>
    {
        readonly ShaderGlobalVectorItem m_Global;

        internal ShaderGlobalVectorBinding(ShaderGlobalVectorItem item) : base(item) => m_Global = item;

        protected override bool ReadsTarget => false;

        protected override bool Acquire(in PrepareContext context, bool direct, out ValueSlot<Vector4, Vector4Ops> slot)
        {
            slot = null;
            if (string.IsNullOrEmpty(m_Global.m_PropertyName))
                return Refuse("name the shader global");
            int id = Shader.PropertyToID(m_Global.m_PropertyName);
            slot = Layers.GetOrCreate(null, id, 0, () => new ShaderGlobalVectorSlot(id));
            return true;
        }
    }

    internal sealed class FogColorBinding : ValueTargetBinding<Color, ColorOps>
    {
        Scene m_Scene;

        internal FogColorBinding(FogColorItem item) : base(item) { }

        protected override bool ReadsTarget => false;
        protected override bool Refollows => true;
        protected override bool SlotStale => SceneManager.GetActiveScene() != m_Scene;

        protected override bool Acquire(in PrepareContext context, bool direct, out ValueSlot<Color, ColorOps> slot)
        {
            Scene scene = SceneManager.GetActiveScene();
            m_Scene = scene;
            slot = Layers.GetOrCreate(null, 0, RenderGlobalKeys.Scene(scene), () => new FogColorSlot(scene));
            return true;
        }
    }

    internal sealed class FogDensityBinding : ValueTargetBinding<float, FloatOps>
    {
        Scene m_Scene;

        internal FogDensityBinding(FogDensityItem item) : base(item) { }

        protected override bool ReadsTarget => false;
        protected override bool Refollows => true;
        protected override bool SlotStale => SceneManager.GetActiveScene() != m_Scene;

        protected override bool Acquire(in PrepareContext context, bool direct, out ValueSlot<float, FloatOps> slot)
        {
            Scene scene = SceneManager.GetActiveScene();
            m_Scene = scene;
            slot = Layers.GetOrCreate(null, 0, RenderGlobalKeys.Scene(scene), () => new FogDensitySlot(scene));
            return true;
        }
    }

    internal sealed class AmbientColorBinding : ValueTargetBinding<Color, ColorOps>
    {
        Scene m_Scene;

        internal AmbientColorBinding(AmbientColorItem item) : base(item) { }

        protected override bool ReadsTarget => false;
        protected override bool Refollows => true;
        protected override bool SlotStale => SceneManager.GetActiveScene() != m_Scene;

        protected override bool Acquire(in PrepareContext context, bool direct, out ValueSlot<Color, ColorOps> slot)
        {
            Scene scene = SceneManager.GetActiveScene();
            m_Scene = scene;
            slot = Layers.GetOrCreate(null, 0, RenderGlobalKeys.Scene(scene), () => new AmbientColorSlot(scene));
            return true;
        }
    }

    internal sealed class AmbientIntensityBinding : ValueTargetBinding<float, FloatOps>
    {
        Scene m_Scene;

        internal AmbientIntensityBinding(AmbientIntensityItem item) : base(item) { }

        protected override bool ReadsTarget => false;
        protected override bool Refollows => true;
        protected override bool SlotStale => SceneManager.GetActiveScene() != m_Scene;

        protected override bool Acquire(in PrepareContext context, bool direct, out ValueSlot<float, FloatOps> slot)
        {
            Scene scene = SceneManager.GetActiveScene();
            m_Scene = scene;
            slot = Layers.GetOrCreate(null, 0, RenderGlobalKeys.Scene(scene), () => new AmbientIntensitySlot(scene));
            return true;
        }
    }

    // the scene's skybox must have the property (empty name = the standard one) — a Float or Range for the exposure and the
    // rotation, a Color for the tint
    internal static class SkyboxPrepare
    {
        internal const string NoSkybox = "the scene has no skybox";

        internal static string TintName(Material sky) => sky.shader != null && sky.shader.FindPropertyIndex("_Tint") >= 0 ? "_Tint" : "_SkyTint";
    }

    internal sealed class SkyboxExposureBinding : ValueTargetBinding<float, FloatOps>
    {
        readonly SkyboxExposureItem m_Sky;
        Scene m_Scene;

        internal SkyboxExposureBinding(SkyboxExposureItem item) : base(item) => m_Sky = item;

        protected override bool ReadsTarget => false;
        protected override bool Refollows => true;
        protected override bool SlotStale => SceneManager.GetActiveScene() != m_Scene;

        protected override bool Acquire(in PrepareContext context, bool direct, out ValueSlot<float, FloatOps> slot)
        {
            slot = null;
            Material sky = RenderSettings.skybox;
            if (sky == null)
                return Refuse(SkyboxPrepare.NoSkybox);
            string name = string.IsNullOrEmpty(m_Sky.m_PropertyName) ? "_Exposure" : m_Sky.m_PropertyName;
            if (!MaterialPrepare.Named(sky, name, ShaderPropertyType.Float, ShaderPropertyType.Range, "float", out int id, out string reason))
                return Refuse(reason);
            Scene scene = SceneManager.GetActiveScene();
            m_Scene = scene;
            slot = Layers.GetOrCreate(null, id, RenderGlobalKeys.Scene(scene), () => new SkyboxFloatSlot(scene, id, true));
            return true;
        }
    }

    internal sealed class SkyboxRotationBinding : ValueTargetBinding<float, FloatOps>
    {
        readonly SkyboxRotationItem m_Sky;
        Scene m_Scene;

        internal SkyboxRotationBinding(SkyboxRotationItem item) : base(item) => m_Sky = item;

        protected override bool ReadsTarget => false;
        protected override bool Refollows => true;
        protected override bool SlotStale => SceneManager.GetActiveScene() != m_Scene;

        protected override bool Acquire(in PrepareContext context, bool direct, out ValueSlot<float, FloatOps> slot)
        {
            slot = null;
            Material sky = RenderSettings.skybox;
            if (sky == null)
                return Refuse(SkyboxPrepare.NoSkybox);
            string name = string.IsNullOrEmpty(m_Sky.m_PropertyName) ? "_Rotation" : m_Sky.m_PropertyName;
            if (!MaterialPrepare.Named(sky, name, ShaderPropertyType.Float, ShaderPropertyType.Range, "float", out int id, out string reason))
                return Refuse(reason);
            Scene scene = SceneManager.GetActiveScene();
            m_Scene = scene;
            slot = Layers.GetOrCreate(null, id, RenderGlobalKeys.Scene(scene), () => new SkyboxFloatSlot(scene, id, false));
            return true;
        }
    }

    internal sealed class SkyboxTintBinding : ValueTargetBinding<Color, ColorOps>
    {
        readonly SkyboxTintItem m_Sky;
        Scene m_Scene;

        internal SkyboxTintBinding(SkyboxTintItem item) : base(item) => m_Sky = item;

        protected override bool ReadsTarget => false;
        protected override bool Refollows => true;
        protected override bool SlotStale => SceneManager.GetActiveScene() != m_Scene;

        protected override bool Acquire(in PrepareContext context, bool direct, out ValueSlot<Color, ColorOps> slot)
        {
            slot = null;
            Material sky = RenderSettings.skybox;
            if (sky == null)
                return Refuse(SkyboxPrepare.NoSkybox);
            string name = string.IsNullOrEmpty(m_Sky.m_PropertyName) ? SkyboxPrepare.TintName(sky) : m_Sky.m_PropertyName;
            if (!MaterialPrepare.Named(sky, name, ShaderPropertyType.Color, ShaderPropertyType.Color, "color", out int id, out string reason))
                return Refuse(reason);
            Scene scene = SceneManager.GetActiveScene();
            m_Scene = scene;
            slot = Layers.GetOrCreate(null, id, RenderGlobalKeys.Scene(scene), () => new SkyboxColorSlot(scene, id));
            return true;
        }
    }
}
