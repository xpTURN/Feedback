using System;
using UnityEngine;
using UnityEngine.SceneManagement;
#if UNITY_EDITOR
using UnityEditor.SceneManagement;
#endif

namespace xpTURN.Feedback
{
    // a RenderSettings slot's scene — alive while loaded, awake while active. Asleep a write goes on the books only (the slot
    // reads what it wrote) and is written when the scene is active again: the scene change is heard while something is
    // booked, not asked every frame
    internal sealed class SceneGate
    {
        readonly Scene m_Scene;
        Action m_Flush;
        bool m_Booked;

        internal SceneGate(Scene scene)
        {
            m_Scene = scene;
        }

        internal Scene Scene => m_Scene;

        internal bool Loaded => m_Scene.IsValid() && m_Scene.isLoaded;

        internal bool Awake => SceneManager.GetActiveScene() == m_Scene;

        internal void Sleeping(Action flush)
        {
            if (m_Booked)
                return;
            m_Booked = true;
            m_Flush = flush;
            SceneManager.activeSceneChanged += Changed;
#if UNITY_EDITOR
            EditorSceneManager.activeSceneChangedInEditMode += Changed;
#endif
        }

        void Changed(Scene from, Scene to)
        {
            if (to != m_Scene)
                return;
            Release();
            m_Flush?.Invoke();
        }

        internal void Release()
        {
            if (!m_Booked)
                return;
            m_Booked = false;
            SceneManager.activeSceneChanged -= Changed;
#if UNITY_EDITOR
            EditorSceneManager.activeSceneChangedInEditMode -= Changed;
#endif
        }
    }

    [NotRecorded("RenderSettings is not recordable — the preview's save state puts it back")]
    internal sealed class FogColorSlot : ValueSlot<Color, ColorOps>
    {
        readonly SceneGate m_Gate;
        Color m_Seen;

        internal FogColorSlot(Scene scene) : base(ConsumptionStage.LateApply, Composition.Additive, false) => m_Gate = new SceneGate(scene);

        internal override bool IsAlive => m_Gate.Loaded;
        protected override Color Read() => m_Gate.Awake ? (m_Seen = RenderSettings.fogColor) : m_Seen;

        protected override void Write(in Color value)
        {
            m_Seen = value;
            if (m_Gate.Awake)
                RenderSettings.fogColor = value;
            else
                m_Gate.Sleeping(Flush);
        }

        void Flush()
        {
            if (m_Gate.Awake)
                RenderSettings.fogColor = m_Seen;
        }

        internal override void Dropped() => m_Gate.Release();
    }

    // never below 0 — the engine takes a negative · NaN density
    [NotRecorded("RenderSettings is not recordable — the preview's save state puts it back")]
    internal sealed class FogDensitySlot : ValueSlot<float, FloatOps>
    {
        readonly SceneGate m_Gate;
        float m_Seen;

        internal FogDensitySlot(Scene scene) : base(ConsumptionStage.LateApply, Composition.Additive, false) => m_Gate = new SceneGate(scene);

        internal override bool IsAlive => m_Gate.Loaded;
        protected override float Read() => m_Gate.Awake ? (m_Seen = RenderSettings.fogDensity) : m_Seen;

        protected override void Write(in float value)
        {
            m_Seen = value;
            if (m_Gate.Awake)
                Flush();
            else
                m_Gate.Sleeping(Flush);
        }

        void Flush()
        {
            if (m_Gate.Awake)
                RenderSettings.fogDensity = m_Seen >= 0f ? m_Seen : 0f;
        }

        internal override void Dropped() => m_Gate.Release();
    }

    [NotRecorded("RenderSettings is not recordable — the preview's save state puts it back")]
    internal sealed class AmbientColorSlot : ValueSlot<Color, ColorOps>
    {
        readonly SceneGate m_Gate;
        Color m_Seen;

        internal AmbientColorSlot(Scene scene) : base(ConsumptionStage.LateApply, Composition.Additive, false) => m_Gate = new SceneGate(scene);

        internal override bool IsAlive => m_Gate.Loaded;
        protected override Color Read() => m_Gate.Awake ? (m_Seen = RenderSettings.ambientLight) : m_Seen;

        protected override void Write(in Color value)
        {
            m_Seen = value;
            if (m_Gate.Awake)
                RenderSettings.ambientLight = value;
            else
                m_Gate.Sleeping(Flush);
        }

        void Flush()
        {
            if (m_Gate.Awake)
                RenderSettings.ambientLight = m_Seen;
        }

        internal override void Dropped() => m_Gate.Release();
    }

    // never below 0
    [NotRecorded("RenderSettings is not recordable — the preview's save state puts it back")]
    internal sealed class AmbientIntensitySlot : ValueSlot<float, FloatOps>
    {
        readonly SceneGate m_Gate;
        float m_Seen;

        internal AmbientIntensitySlot(Scene scene) : base(ConsumptionStage.LateApply, Composition.Additive, false) => m_Gate = new SceneGate(scene);

        internal override bool IsAlive => m_Gate.Loaded;
        protected override float Read() => m_Gate.Awake ? (m_Seen = RenderSettings.ambientIntensity) : m_Seen;

        protected override void Write(in float value)
        {
            m_Seen = value;
            if (m_Gate.Awake)
                Flush();
            else
                m_Gate.Sleeping(Flush);
        }

        void Flush()
        {
            if (m_Gate.Awake)
                RenderSettings.ambientIntensity = m_Seen >= 0f ? m_Seen : 0f;
        }

        internal override void Dropped() => m_Gate.Release();
    }

    // a shader global — the value before the first write comes back when the registry closes (the quit · a reload during
    // play · a preview's end), not at a release (a runtime Persist stays): a slot released first hands it to the close
    [NotRecorded("a shader global is not serialized — the slot puts its first value back at the close")]
    internal sealed class ShaderGlobalFloatSlot : ValueSlot<float, FloatOps>
    {
        readonly int m_Id;
        float m_First;
        bool m_HasFirst;

        internal ShaderGlobalFloatSlot(int id) : base(ConsumptionStage.LateApply, Composition.Additive, false) => m_Id = id;

        internal override bool IsAlive => true;
        protected override float Read() => Shader.GetGlobalFloat(m_Id);

        protected override void Write(in float value)
        {
            if (!m_HasFirst)
            {
                m_First = Shader.GetGlobalFloat(m_Id);
                m_HasFirst = true;
            }
            Shader.SetGlobalFloat(m_Id, value);
        }

        internal override void Dropped()
        {
            if (!m_HasFirst || Registry == null)
                return;
            if (Registry.IsClosed)
            {
                Shader.SetGlobalFloat(m_Id, m_First);
                return;
            }
            int id = m_Id;
            float first = m_First;
            Registry.OnClose(() => Shader.SetGlobalFloat(id, first));
        }
    }

    // the picker's color — linear in a Linear project (as a material's Color property), read back as gamma. The round trip is
    // not bit exact: a revert writes the raw value it took, a Persist back to the first value its first raw
    [NotRecorded("a shader global is not serialized — the slot puts its first value back at the close")]
    internal sealed class ShaderGlobalColorSlot : ValueSlot<Color, ColorOps>
    {
        readonly int m_Id;
        Vector4 m_First;
        Color m_FirstSeen;
        bool m_HasFirst;
        Vector4 m_Snapshot;

        internal ShaderGlobalColorSlot(int id) : base(ConsumptionStage.LateApply, Composition.Additive, true) => m_Id = id;

        static bool Linear => QualitySettings.activeColorSpace == ColorSpace.Linear;

        internal override bool IsAlive => true;

        protected override Color Read()
        {
            Color c = Shader.GetGlobalColor(m_Id);
            return Linear ? c.gamma : c;
        }

        protected override void Write(in Color value)
        {
            if (!m_HasFirst)
            {
                m_First = Shader.GetGlobalVector(m_Id);
                m_FirstSeen = Read();
                m_HasFirst = true;
            }
            Shader.SetGlobalColor(m_Id, Linear ? value.linear : value);
        }

        protected override void BeforeWrite() => m_Snapshot = Shader.GetGlobalVector(m_Id);

        protected override void Restore(in Color baseValue) => Shader.SetGlobalVector(m_Id, m_Snapshot);

        protected override void WriteDirect(in Color value)
        {
            if (m_HasFirst && default(ColorOps).BitEquals(value, m_FirstSeen))
                Shader.SetGlobalVector(m_Id, m_First);
            else
                Write(value);
        }

        internal override void Dropped()
        {
            if (!m_HasFirst || Registry == null)
                return;
            if (Registry.IsClosed)
            {
                Shader.SetGlobalVector(m_Id, m_First);
                return;
            }
            int id = m_Id;
            Vector4 first = m_First;
            Registry.OnClose(() => Shader.SetGlobalVector(id, first));
        }
    }

    [NotRecorded("a shader global is not serialized — the slot puts its first value back at the close")]
    internal sealed class ShaderGlobalVectorSlot : ValueSlot<Vector4, Vector4Ops>
    {
        readonly int m_Id;
        Vector4 m_First;
        bool m_HasFirst;

        internal ShaderGlobalVectorSlot(int id) : base(ConsumptionStage.LateApply, Composition.Additive, false) => m_Id = id;

        internal override bool IsAlive => true;
        protected override Vector4 Read() => Shader.GetGlobalVector(m_Id);

        protected override void Write(in Vector4 value)
        {
            if (!m_HasFirst)
            {
                m_First = Shader.GetGlobalVector(m_Id);
                m_HasFirst = true;
            }
            Shader.SetGlobalVector(m_Id, value);
        }

        internal override void Dropped()
        {
            if (!m_HasFirst || Registry == null)
                return;
            if (Registry.IsClosed)
            {
                Shader.SetGlobalVector(m_Id, m_First);
                return;
            }
            int id = m_Id;
            Vector4 first = m_First;
            Registry.OnClose(() => Shader.SetGlobalVector(id, first));
        }
    }

    // one scene's skybox copy — made and put in at the attach (the original asset is not written; a preview's copy is hidden
    // and carries the recovery's prefix). At the drop the original goes back while the scene is active and the copy is still
    // its skybox (another's skybox stays); an asleep scene keeps the copy at a release, the close destroys it (the preview's
    // save state · the play's end put the original back)
    [NotRecorded("RenderSettings.skybox is not recordable — the preview's save state puts the original back")]
    internal sealed class SkyboxCopySlot : LayerSlot
    {
        readonly SceneGate m_Gate;
        Material m_Original;
        Material m_Copy;
        bool m_Freed;

        internal SkyboxCopySlot(Scene scene) : base(ConsumptionStage.LateApply) => m_Gate = new SceneGate(scene);

        internal Material Copy => m_Copy;
        internal Material Original => m_Original;
        internal bool Usable => !m_Freed && m_Copy != null && m_Gate.Loaded;

        internal override bool IsAlive => m_Gate.Loaded;
        internal override bool Pending => false;
        internal override int LiveContributions => 0;
        internal override int Reserved => 0;
        internal override Vector4 BaseVector => Vector4.zero;
        internal override void Revert() { }
        internal override void Apply() { }
        internal override void Close() { }
        internal override void EndAll() { }
        internal override void Unreserve(int count) { }

        internal override void Attached()
        {
            RecordOnce();
            m_Original = RenderSettings.skybox;
            if (m_Original == null)
                return;
            bool preview = Registry != null && Registry.IsPreview;
            m_Copy = new Material(m_Original) { name = (preview ? PropertyLayerRegistry.PreviewPrefix : "") + m_Original.name + " (Feedback)" };
            if (preview)
                m_Copy.hideFlags = HideFlags.HideAndDontSave;
            RenderSettings.skybox = m_Copy;
        }

        internal override void Dropped()
        {
            if (m_Freed)
                return;
            m_Freed = true;
            if (m_Copy == null)
                return;
            bool awake = m_Gate.Awake;
            if (awake && RenderSettings.skybox == m_Copy)
                RenderSettings.skybox = m_Original;
            if (awake || !m_Gate.Loaded || (Registry != null && Registry.IsClosed))
            {
                if (Application.isPlaying)
                    UnityEngine.Object.Destroy(m_Copy);
                else
                    UnityEngine.Object.DestroyImmediate(m_Copy);
            }
        }
    }

    // a property of the scene's copy — written asleep too (the copy is that scene's own: no other scene sees it)
    [NotRecorded("the skybox copy is not saved — the preview's save state puts the original back")]
    internal sealed class SkyboxFloatSlot : ValueSlot<float, FloatOps>
    {
        readonly Scene m_Scene;
        readonly int m_Id;
        readonly bool m_AtLeastZero;
        SkyboxCopySlot m_Copies;
        bool m_Let;

        internal SkyboxFloatSlot(Scene scene, int id, bool atLeastZero) : base(ConsumptionStage.LateApply, Composition.Additive, false)
        {
            m_Scene = scene;
            m_Id = id;
            m_AtLeastZero = atLeastZero;
        }

        internal SkyboxCopySlot Copies => m_Copies;

        internal override void Attached()
        {
            Scene scene = m_Scene;
            m_Copies = Registry.GetOrCreate(null, 0, RenderGlobalKeys.Scene(scene), () => new SkyboxCopySlot(scene));
        }

        internal override bool IsAlive => m_Copies != null && m_Copies.Usable;
        protected override float Read() => m_Copies.Copy.GetFloat(m_Id);

        protected override void Write(in float value) => m_Copies.Copy.SetFloat(m_Id, m_AtLeastZero && !(value >= 0f) ? 0f : value);

        internal override void Dropped()
        {
            if (m_Let)
                return;
            m_Let = true;
            if (m_Copies != null && Registry != null && !Registry.IsClosed)
                Registry.Release(m_Copies, 0);
        }
    }

    [NotRecorded("the skybox copy is not saved — the preview's save state puts the original back")]
    internal sealed class SkyboxColorSlot : ValueSlot<Color, ColorOps>
    {
        readonly Scene m_Scene;
        readonly int m_Id;
        SkyboxCopySlot m_Copies;
        bool m_Let;

        internal SkyboxColorSlot(Scene scene, int id) : base(ConsumptionStage.LateApply, Composition.Additive, true)
        {
            m_Scene = scene;
            m_Id = id;
        }

        internal SkyboxCopySlot Copies => m_Copies;

        internal override void Attached()
        {
            Scene scene = m_Scene;
            m_Copies = Registry.GetOrCreate(null, 0, RenderGlobalKeys.Scene(scene), () => new SkyboxCopySlot(scene));
        }

        internal override bool IsAlive => m_Copies != null && m_Copies.Usable;
        protected override Color Read() => m_Copies.Copy.GetColor(m_Id);

        protected override void Write(in Color value) => m_Copies.Copy.SetColor(m_Id, value);

        internal override void Dropped()
        {
            if (m_Let)
                return;
            m_Let = true;
            if (m_Copies != null && Registry != null && !Registry.IsClosed)
                Registry.Release(m_Copies, 0);
        }
    }
}
