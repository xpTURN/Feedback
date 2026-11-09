using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace xpTURN.Feedback
{
    // the material property bindings' common prepare — the renderer, its materials and the index (MaterialColorBinding's).
    // The renderer's materials are read here (their C# objects made at the prepare)
    internal static class MaterialPrepare
    {
        static readonly List<Material> s_Materials = new List<Material>();

        // the renderer and the material whose shader is asked (index −1: material 0) — false with the reason
        internal static bool Find(TargetItem item, in PrepareContext context, out Renderer renderer, out Material material, out int index, out string reason)
        {
            var property = (IMaterialPropertyItem)item;
            material = null;
            index = property.MaterialIndex < 0 ? -1 : property.MaterialIndex;
            renderer = TargetResolver.Find<Renderer>(in item.m_Target, in context, out TargetFailure failure);
            if (renderer == null)
            {
                reason = TargetResolver.Describe(failure);
                return false;
            }
            renderer.GetSharedMaterials(s_Materials);
            if (s_Materials.Count == 0)
            {
                reason = "the renderer has no material";
                return false;
            }
            if (index >= s_Materials.Count)
            {
                // the reused list holds nothing past the prepare — a refusal too
                s_Materials.Clear();
                reason = "material index out of range";
                return false;
            }
            material = s_Materials[Math.Max(index, 0)];
            bool holes = false;
            for (int i = 0; i < s_Materials.Count; i++)
                holes |= s_Materials[i] == null;
            s_Materials.Clear();
            // the instance slot copies every material of −1 — a missing one would throw in its constructor
            if (index < 0 && property.WriteMode == MaterialWriteMode.Instance && holes)
            {
                reason = "no material at an index";
                return false;
            }
            if (material == null)
            {
                reason = "no material at the index";
                return false;
            }
            reason = null;
            return true;
        }

        // a named property of the shader's type — false with the reason
        internal static bool Named(Material material, string name, ShaderPropertyType type, ShaderPropertyType alsoType, string what, out int id, out string reason)
        {
            id = 0;
            if (string.IsNullOrEmpty(name))
            {
                reason = "name the " + what + " property";
                return false;
            }
            int at = material.shader != null ? material.shader.FindPropertyIndex(name) : -1;
            if (at < 0)
            {
                reason = "the material has no property " + name;
                return false;
            }
            ShaderPropertyType found = material.shader.GetPropertyType(at);
            if (found != type && found != alsoType)
            {
                reason = "the property " + name + " is " + (found == ShaderPropertyType.Int ? "an Integer" : "a " + found) + ", not a " + what;
                return false;
            }
            id = Shader.PropertyToID(name);
            reason = null;
            return true;
        }
    }

    // a renderer's color — the write mode picks the slot type, the property is found at the prepare (an empty name is the
    // main color: the shader's [MainColor], else _Color — index −1 asks material 0's shader)
    internal sealed class MaterialColorBinding : ValueTargetBinding<Color, ColorOps>
    {
        readonly MaterialColorItem m_Color;

        internal MaterialColorBinding(MaterialColorItem item)
            : base(item)
        {
            m_Color = item;
        }

        // observation
        internal int PropertyId { get; private set; }

        protected override bool Acquire(in PrepareContext context, bool direct, out ValueSlot<Color, ColorOps> slot)
        {
            slot = null;
            if (!MaterialPrepare.Find(m_Item, in context, out Renderer renderer, out Material material, out int index, out string reason))
                return Refuse(reason);
            int id;
            if (string.IsNullOrEmpty(m_Color.m_PropertyName))
            {
                if (!MainColor(material.shader, out id))
                    return Refuse("the shader has no main color — name the color property");
            }
            else
            {
                id = Shader.PropertyToID(m_Color.m_PropertyName);
            }
            if (!material.HasColor(id))
                return Refuse("the material has no such color property");
            PropertyId = id;
            if (m_Color.m_WriteMode == MaterialWriteMode.Instance)
            {
                if (direct)
                    slot = Layers.CreatePrivate(renderer, id, index, () => new MaterialInstanceColorSlot(renderer, id, index));
                else
                    slot = Layers.GetOrCreate(renderer, id, index, () => new MaterialInstanceColorSlot(renderer, id, index));
            }
            else
            {
                if (direct)
                    slot = Layers.CreatePrivate(renderer, id, index, () => new MaterialBlockColorSlot(renderer, id, index));
                else
                    slot = Layers.GetOrCreate(renderer, id, index, () => new MaterialBlockColorSlot(renderer, id, index));
            }
            return true;
        }

        internal static bool MainColor(Shader shader, out int id)
        {
            if (shader != null)
            {
                int count = shader.GetPropertyCount();
                for (int i = 0; i < count; i++)
                {
                    if ((shader.GetPropertyFlags(i) & ShaderPropertyFlags.MainColor) != 0 && shader.GetPropertyType(i) == ShaderPropertyType.Color)
                    {
                        id = shader.GetPropertyNameId(i);
                        return true;
                    }
                }
                int color = shader.FindPropertyIndex("_Color");
                if (color >= 0 && shader.GetPropertyType(color) == ShaderPropertyType.Color)
                {
                    id = shader.GetPropertyNameId(color);
                    return true;
                }
            }
            id = 0;
            return false;
        }
    }

    // a float — Float · Range (ShaderLab's old Int is a Float); an Integer is refused (SetFloat logs and writes nothing). A
    // Range is not clamped — the shader does not know it
    internal sealed class MaterialFloatBinding : ValueTargetBinding<float, FloatOps>
    {
        readonly MaterialFloatItem m_Float;

        internal MaterialFloatBinding(MaterialFloatItem item)
            : base(item)
        {
            m_Float = item;
        }

        protected override bool Acquire(in PrepareContext context, bool direct, out ValueSlot<float, FloatOps> slot)
        {
            slot = null;
            if (!MaterialPrepare.Find(m_Item, in context, out Renderer renderer, out Material material, out int index, out string reason)
                || !MaterialPrepare.Named(material, m_Float.m_PropertyName, ShaderPropertyType.Float, ShaderPropertyType.Range, "float", out int id, out reason))
                return Refuse(reason);
            if (m_Float.m_WriteMode == MaterialWriteMode.Instance)
            {
                if (direct)
                    slot = Layers.CreatePrivate(renderer, id, index, () => new MaterialInstanceFloatSlot(renderer, id, index));
                else
                    slot = Layers.GetOrCreate(renderer, id, index, () => new MaterialInstanceFloatSlot(renderer, id, index));
            }
            else
            {
                if (direct)
                    slot = Layers.CreatePrivate(renderer, id, index, () => new MaterialBlockFloatSlot(renderer, id, index));
                else
                    slot = Layers.GetOrCreate(renderer, id, index, () => new MaterialBlockFloatSlot(renderer, id, index));
            }
            return true;
        }
    }

    // a Vector property only — a color through a block's SetVector skips the linear conversion (a color takes Material Color)
    internal sealed class MaterialVectorBinding : ValueTargetBinding<Vector4, Vector4Ops>
    {
        readonly MaterialVectorItem m_Vector;

        internal MaterialVectorBinding(MaterialVectorItem item)
            : base(item)
        {
            m_Vector = item;
        }

        protected override bool Acquire(in PrepareContext context, bool direct, out ValueSlot<Vector4, Vector4Ops> slot)
        {
            slot = null;
            if (!MaterialPrepare.Find(m_Item, in context, out Renderer renderer, out Material material, out int index, out string reason)
                || !MaterialPrepare.Named(material, m_Vector.m_PropertyName, ShaderPropertyType.Vector, ShaderPropertyType.Vector, "vector", out int id, out reason))
                return Refuse(reason);
            if (m_Vector.m_WriteMode == MaterialWriteMode.Instance)
            {
                if (direct)
                    slot = Layers.CreatePrivate(renderer, id, index, () => new MaterialInstanceVectorSlot(renderer, id, index));
                else
                    slot = Layers.GetOrCreate(renderer, id, index, () => new MaterialInstanceVectorSlot(renderer, id, index));
            }
            else
            {
                if (direct)
                    slot = Layers.CreatePrivate(renderer, id, index, () => new MaterialBlockVectorSlot(renderer, id, index));
                else
                    slot = Layers.GetOrCreate(renderer, id, index, () => new MaterialBlockVectorSlot(renderer, id, index));
            }
            return true;
        }
    }

    // a texture's offset (zw) and scale (xy) — the texture property, an empty name the main texture ([MainTexture], else
    // _MainTex — the engine's mainTexture). One _ST slot (Vector4) takes both: Persist puts a binding's half over the slot's
    // other half, an offset goes in its half, and a remembered value (the play's start · the first value · the origin) puts
    // its half over the slot's other half now
    internal static class TextureST
    {
        static readonly Vector4 s_Identity = new Vector4(1f, 1f, 0f, 0f);

        internal static bool Resolve(Material material, string name, out int textureId, out int stId, out string reason)
        {
            textureId = 0;
            stId = 0;
            if (string.IsNullOrEmpty(name))
                name = MainTexture(material.shader);
            if (!material.HasTexture(name))
            {
                reason = "the material has no texture " + name;
                return false;
            }
            textureId = Shader.PropertyToID(name);
            stId = Shader.PropertyToID(name + "_ST");
            reason = null;
            return true;
        }

        internal static string MainTexture(Shader shader)
        {
            if (shader != null)
            {
                int count = shader.GetPropertyCount();
                for (int i = 0; i < count; i++)
                {
                    if ((shader.GetPropertyFlags(i) & ShaderPropertyFlags.MainTexture) != 0 && shader.GetPropertyType(i) == ShaderPropertyType.Texture)
                        return shader.GetPropertyName(i);
                }
            }
            return "_MainTex";
        }

        internal static ValueSlot<Vector4, Vector4Ops> Slot(PropertyLayerRegistry layers, Renderer renderer, int stId, int index, int textureId, MaterialWriteMode mode, bool direct)
        {
            if (mode == MaterialWriteMode.Instance)
            {
                if (direct)
                    return layers.CreatePrivate(renderer, stId, index, () => new MaterialInstanceTextureSlot(renderer, stId, index, textureId));
                return layers.GetOrCreate(renderer, stId, index, () => new MaterialInstanceTextureSlot(renderer, stId, index, textureId));
            }
            if (direct)
                return layers.CreatePrivate(renderer, stId, index, () => new MaterialBlockTextureSlot(renderer, stId, index));
            return layers.GetOrCreate(renderer, stId, index, () => new MaterialBlockTextureSlot(renderer, stId, index));
        }

        // the slot's value now — the other half a binding keeps
        internal static Vector4 Now(ValueSlot<Vector4, Vector4Ops> slot) => slot != null ? slot.Base : s_Identity;

        // a half as the motion's value (x, y)
        internal static Vector4 Half(in Vector4 st, bool zw) => zw ? new Vector4(st.z, st.w, 0f, 0f) : new Vector4(st.x, st.y, 0f, 0f);

        internal static Vector4 With(Vector4 st, bool zw, float x, float y)
        {
            if (zw)
            {
                st.z = x;
                st.w = y;
            }
            else
            {
                st.x = x;
                st.y = y;
            }
            return st;
        }
    }

    internal sealed class TextureOffsetBinding : ValueTargetBinding<Vector4, Vector4Ops>
    {
        readonly TextureOffsetItem m_Texture;

        internal TextureOffsetBinding(TextureOffsetItem item)
            : base(item)
        {
            m_Texture = item;
        }

        protected override bool Acquire(in PrepareContext context, bool direct, out ValueSlot<Vector4, Vector4Ops> slot)
        {
            slot = null;
            if (!MaterialPrepare.Find(m_Item, in context, out Renderer renderer, out Material material, out int index, out string reason)
                || !TextureST.Resolve(material, m_Texture.m_PropertyName, out int texture, out int st, out reason))
                return Refuse(reason);
            slot = TextureST.Slot(Layers, renderer, st, index, texture, m_Texture.m_WriteMode, direct);
            return true;
        }

        protected override Vector4 ToMotion(in Vector4 value) => TextureST.Half(value, true);

        protected override Vector4 FromMotion(in Vector4 value) => TextureST.With(TextureST.Now(Slot), true, value.x, value.y);

        protected override Vector4 MapOffset(in Vector4 offset) => TextureST.With(Vector4.zero, true, offset.x, offset.y);

        protected override Vector4 Stored(in Vector4 value) => TextureST.With(TextureST.Now(Slot), true, value.z, value.w);
    }

    internal sealed class TextureScaleBinding : ValueTargetBinding<Vector4, Vector4Ops>
    {
        readonly TextureScaleItem m_Texture;

        internal TextureScaleBinding(TextureScaleItem item)
            : base(item)
        {
            m_Texture = item;
        }

        protected override bool Acquire(in PrepareContext context, bool direct, out ValueSlot<Vector4, Vector4Ops> slot)
        {
            slot = null;
            if (!MaterialPrepare.Find(m_Item, in context, out Renderer renderer, out Material material, out int index, out string reason)
                || !TextureST.Resolve(material, m_Texture.m_PropertyName, out int texture, out int st, out reason))
                return Refuse(reason);
            slot = TextureST.Slot(Layers, renderer, st, index, texture, m_Texture.m_WriteMode, direct);
            return true;
        }

        protected override Vector4 ToMotion(in Vector4 value) => TextureST.Half(value, false);

        protected override Vector4 FromMotion(in Vector4 value) => TextureST.With(TextureST.Now(Slot), false, value.x, value.y);

        protected override Vector4 MapOffset(in Vector4 offset) => TextureST.With(Vector4.zero, false, offset.x, offset.y);

        protected override Vector4 Stored(in Vector4 value) => TextureST.With(TextureST.Now(Slot), false, value.x, value.y);
    }

    // the written color = the motion's color × the multiplier — the alpha as the motion gives it; an override's target too
    // (Flash · Blink). An empty name is _EmissionColor. The keyword is the material's — neither mode changes it (a build
    // drops the variants no material uses)
    internal sealed class EmissionBinding : ValueTargetBinding<Color, ColorOps>
    {
        readonly EmissionItem m_Emission;
        float m_Multiplier = 1f;

        internal EmissionBinding(EmissionItem item)
            : base(item)
        {
            m_Emission = item;
        }

        protected override bool Acquire(in PrepareContext context, bool direct, out ValueSlot<Color, ColorOps> slot)
        {
            slot = null;
            if (!MaterialPrepare.Find(m_Item, in context, out Renderer renderer, out Material material, out int index, out string reason))
                return Refuse(reason);
            if (!(m_Emission.m_Multiplier > 0f))
                return Refuse("the multiplier is not above 0");
            string name = string.IsNullOrEmpty(m_Emission.m_PropertyName) ? "_EmissionColor" : m_Emission.m_PropertyName;
            if (!MaterialPrepare.Named(material, name, ShaderPropertyType.Color, ShaderPropertyType.Color, "color", out int id, out reason))
                return Refuse(reason);
            m_Multiplier = m_Emission.m_Multiplier;
            if (m_Emission.m_WriteMode == MaterialWriteMode.Instance)
            {
                if (direct)
                    slot = Layers.CreatePrivate(renderer, id, index, () => new MaterialInstanceColorSlot(renderer, id, index));
                else
                    slot = Layers.GetOrCreate(renderer, id, index, () => new MaterialInstanceColorSlot(renderer, id, index));
            }
            else
            {
                if (direct)
                    slot = Layers.CreatePrivate(renderer, id, index, () => new MaterialBlockColorSlot(renderer, id, index));
                else
                    slot = Layers.GetOrCreate(renderer, id, index, () => new MaterialBlockColorSlot(renderer, id, index));
            }
            return true;
        }

        Color Times(in Color c) => new Color(c.r * m_Multiplier, c.g * m_Multiplier, c.b * m_Multiplier, c.a);

        protected override Vector4 ToMotion(in Color value) => new Vector4(value.r / m_Multiplier, value.g / m_Multiplier, value.b / m_Multiplier, value.a);

        protected override Color FromMotion(in Vector4 value) => Times(new Color(value.x, value.y, value.z, value.w));

        protected override Color MapOffset(in Color offset) => Times(offset);

        protected override Color MapOverride(in Color target) => Times(target);
    }

    // an index's material — SetParentBinding's form (the play's start · the origin · the first value · an instant · the
    // hand-over). A start that is a copy of the renderer's copies slot is taken as its original (the copy goes with the
    // instance slots). The materials are read at the prepare
    internal sealed class MaterialSwapBinding : ItemBinding
    {
        readonly MaterialSwapItem m_Item;
        PropertyLayerRegistry m_Layers;
        MaterialSwapSlot m_Slot;
        int m_Reserved;
        long[] m_Serials;
        bool[] m_HasStart;
        Material[][] m_Starts;
        Material[] m_Origin;
        Material[] m_First;
        bool m_HasOrigin;
        bool m_HasFirst;

        internal MaterialSwapBinding(MaterialSwapItem item)
        {
            m_Item = item;
        }

        // observation
        internal MaterialSwapSlot Slot => m_Slot;

        internal override bool Prepare(in PrepareContext context)
        {
            FeedbackMotion motion = m_Item.m_Motion;
            if (motion == null)
                return Refuse("no motion");
            if (!m_Item.Traits.Supports(motion.Kind))
            {
#if DEBUG
                return Refuse("the target does not take " + motion.Kind);
#else
                return Refuse(null);
#endif
            }
            Renderer renderer = TargetResolver.Find<Renderer>(in m_Item.m_Target, in context, out TargetFailure failure);
            if (renderer == null)
                return Refuse(TargetResolver.Describe(failure));
            if (m_Item.m_Material == null)
                return Refuse("no material to swap in");
            int index = m_Item.m_MaterialIndex < 0 ? -1 : m_Item.m_MaterialIndex;
            Material[] materials = renderer.sharedMaterials;
            if (materials.Length == 0 || index >= materials.Length)
                return Refuse("material index out of range");
            m_Layers = context.Context.Layers;
            m_Slot = m_Layers.GetOrCreate(renderer, 0, index, () => new MaterialSwapSlot(renderer, index));
            int n = context.MaxOverlap;
            m_Slot.Reserve(n);
            m_Reserved = n;
            m_Serials = new long[n];
            m_HasStart = new bool[n];
            m_Starts = new Material[n][];
            for (int i = 0; i < n; i++)
            {
                m_Serials[i] = -1;
                m_Starts[i] = new Material[materials.Length];
            }
            m_Origin = new Material[materials.Length];
            m_First = new Material[materials.Length];
            return true;
        }

        bool Live => m_Slot != null && m_Slot.IsAlive && !m_Slot.Detached;

        internal override bool IsAlive => Live;

        void Take(Material[] into)
        {
            m_Slot.Read(into);
            if (!m_Layers.TryFind(m_Slot.Renderer, 0, -1, out MaterialCopiesSlot copies))
                return;
            for (int i = 0; i < into.Length; i++)
                into[i] = copies.OriginalOf(into[i]);
        }

        // Set — the material at any time; backward it writes the origin (nothing before a forward play)
        internal override void Execute(int record, in BeginContext context)
        {
            bool fresh = m_Serials[record] != context.Serial || !m_HasStart[record];
            if (m_Serials[record] != context.Serial)
            {
                m_Serials[record] = context.Serial;
                m_HasStart[record] = false;
            }
            if (!Live)
                return;
            if (!m_HasStart[record])
            {
                Take(m_Starts[record]);
                m_HasStart[record] = true;
            }
            if (fresh)
            {
                if (!m_HasFirst)
                {
                    Array.Copy(m_Starts[record], m_First, m_First.Length);
                    m_HasFirst = true;
                }
                if (!context.Reverse)
                {
                    Array.Copy(m_Starts[record], m_Origin, m_Origin.Length);
                    m_HasOrigin = true;
                }
            }
            if (context.Reverse)
            {
                if (m_HasOrigin)
                    m_Slot.WriteState(m_Origin);
                return;
            }
            m_Slot.WriteMaterial(m_Item.m_Material);
        }

        internal override void Begin(int record, in BeginContext context)
        {
        }

        internal override void Evaluate(int record, in MotionTime time)
        {
        }

        internal override void End(int record, EndReason reason)
        {
            if (reason == EndReason.Revert && m_HasStart[record] && Live)
                m_Slot.WriteState(m_Starts[record]);
        }

        internal override void Restore()
        {
            if (m_HasFirst && Live)
                m_Slot.WriteState(m_First);
            m_HasOrigin = false;
            m_HasFirst = false;
        }

        // the same item's old binding of the same index
        internal override void Inherit(ItemBinding old)
        {
            if (!(old is MaterialSwapBinding from) || m_Slot == null || from.m_Slot == null || !m_Slot.Key.Equals(from.m_Slot.Key) || from.m_Origin.Length != m_Origin.Length)
                return;
            Array.Copy(from.m_Origin, m_Origin, m_Origin.Length);
            m_HasOrigin = from.m_HasOrigin;
            Array.Copy(from.m_First, m_First, m_First.Length);
            m_HasFirst = from.m_HasFirst;
        }

        internal override void Release()
        {
            if (m_Slot == null)
                return;
            m_Layers.Release(m_Slot, m_Reserved);
            m_Slot = null;
        }
    }

    // a renderer's enabled — the toggle target's form; turning a renderer off turns no player or driver off (no guard)
    internal sealed class RendererVisibilityBinding : ToggleTargetBinding<RendererEnabledSlot>
    {
        internal RendererVisibilityBinding(RendererVisibilityItem item)
            : base(item)
        {
        }

        protected override bool Acquire(in PrepareContext context, out RendererEnabledSlot slot)
        {
            slot = null;
            Renderer renderer = TargetResolver.Find<Renderer>(in m_Item.m_Target, in context, out TargetFailure failure);
            if (renderer == null)
                return Refuse(TargetResolver.Describe(failure));
            slot = Layers.GetOrCreate(renderer, 0, 0, () => new RendererEnabledSlot(renderer));
            return true;
        }
    }
}
