using System;
using System.Collections.Generic;
using UnityEngine;

namespace xpTURN.Feedback
{
    // a value form's reads and writes on a material — the instance slots write a copy. Structs, so a slot's call has no
    // virtual call (the TOps form)
    internal interface IMaterialAccess<T>
    {
        bool Has(Material material, int id);

        T Get(Material material, int id);

        void Set(Material material, int id, in T value);
    }

    // and on a MaterialPropertyBlock — the block slots read the material while no block holds the value
    internal interface IBlockAccess<T> : IMaterialAccess<T>
    {
        bool Has(MaterialPropertyBlock block, int id);

        T Get(MaterialPropertyBlock block, int id);

        void Set(MaterialPropertyBlock block, int id, in T value);
    }

    internal readonly struct ColorAccess : IBlockAccess<Color>
    {
        public bool Has(Material material, int id) => material.HasColor(id);

        public Color Get(Material material, int id) => material.GetColor(id);

        public void Set(Material material, int id, in Color value) => material.SetColor(id, value);

        public bool Has(MaterialPropertyBlock block, int id) => block.HasColor(id);

        public Color Get(MaterialPropertyBlock block, int id) => block.GetColor(id);

        public void Set(MaterialPropertyBlock block, int id, in Color value) => block.SetColor(id, value);
    }

    internal readonly struct FloatAccess : IBlockAccess<float>
    {
        public bool Has(Material material, int id) => material.HasFloat(id);

        public float Get(Material material, int id) => material.GetFloat(id);

        public void Set(Material material, int id, in float value) => material.SetFloat(id, value);

        public bool Has(MaterialPropertyBlock block, int id) => block.HasFloat(id);

        public float Get(MaterialPropertyBlock block, int id) => block.GetFloat(id);

        public void Set(MaterialPropertyBlock block, int id, in float value) => block.SetFloat(id, value);
    }

    // a Vector property — and a texture's _ST vector in a block (xy the scale, zw the offset — the material's _ST reads the
    // same)
    internal readonly struct VectorAccess : IBlockAccess<Vector4>
    {
        public bool Has(Material material, int id) => material.HasVector(id);

        public Vector4 Get(Material material, int id) => material.GetVector(id);

        public void Set(Material material, int id, in Vector4 value) => material.SetVector(id, value);

        public bool Has(MaterialPropertyBlock block, int id) => block.HasVector(id);

        public Vector4 Get(MaterialPropertyBlock block, int id) => block.GetVector(id);

        public void Set(MaterialPropertyBlock block, int id, in Vector4 value) => block.SetVector(id, value);
    }

    // a texture's ST in a material through the texture's scale and offset (m_TexEnvs) — the id is the _ST's, the texture is the
    // struct's
    internal readonly struct TextureAccess : IMaterialAccess<Vector4>
    {
        readonly int m_Texture;

        internal TextureAccess(int textureId)
        {
            m_Texture = textureId;
        }

        public bool Has(Material material, int id) => material.HasTexture(m_Texture);

        public Vector4 Get(Material material, int id)
        {
            Vector2 scale = material.GetTextureScale(m_Texture);
            Vector2 offset = material.GetTextureOffset(m_Texture);
            return new Vector4(scale.x, scale.y, offset.x, offset.y);
        }

        public void Set(Material material, int id, in Vector4 value)
        {
            material.SetTextureScale(m_Texture, new Vector2(value.x, value.y));
            material.SetTextureOffset(m_Texture, new Vector2(value.z, value.w));
        }
    }

    // a renderer's property through a MaterialPropertyBlock — the whole renderer (index −1) or one material index. The revert
    // puts back the block taken right before the apply's write (none → null — the whole as the whole, an index as its index),
    // never the base. The base is the value the material shows — the slot's block, for an index slot the whole block while
    // its own is empty, else the material (−1: material 0) — and an index slot whose block is empty starts from the whole
    // block: an index block hides the whole block for its material. The block slots of every value form keep these rules here
    internal sealed class MaterialBlock
    {
        readonly Renderer m_Renderer;
        readonly int m_Id;
        readonly int m_Index;
        readonly MaterialPropertyBlock m_Snapshot = new MaterialPropertyBlock();
        readonly MaterialPropertyBlock m_Work = new MaterialPropertyBlock();
        readonly MaterialPropertyBlock m_Check = new MaterialPropertyBlock();
        readonly MaterialPropertyBlock m_PersistSnapshot = new MaterialPropertyBlock();
        readonly List<Material> m_Materials = new List<Material>(4);
        bool m_HadBlock;
        bool m_Persisted;
        bool m_PersistHad;

        internal MaterialBlock(Renderer renderer, int propertyId, int index)
        {
            m_Renderer = renderer;
            m_Id = propertyId;
            m_Index = index;
        }

        // an index past the materials drops the slot — no block call, so no error log. Unity drops that index's block when
        // the array shrinks
        internal bool IsAlive
        {
            get
            {
                if (m_Renderer == null)
                    return false;
                m_Renderer.GetSharedMaterials(m_Materials);
                return m_Materials.Count > Math.Max(m_Index, 0);
            }
        }

        void Get(MaterialPropertyBlock block)
        {
            if (m_Index < 0)
                m_Renderer.GetPropertyBlock(block);
            else
                m_Renderer.GetPropertyBlock(block, m_Index);
        }

        void Set(MaterialPropertyBlock block)
        {
            if (m_Index < 0)
                m_Renderer.SetPropertyBlock(block);
            else
                m_Renderer.SetPropertyBlock(block, m_Index);
        }

        internal T Read<T, TAccess>(TAccess access) where TAccess : struct, IBlockAccess<T>
        {
            Get(m_Check);
            if (access.Has(m_Check, m_Id))
                return access.Get(m_Check, m_Id);
            if (m_Index >= 0 && m_Check.isEmpty)
            {
                m_Renderer.GetPropertyBlock(m_Check);
                if (access.Has(m_Check, m_Id))
                    return access.Get(m_Check, m_Id);
            }
            m_Renderer.GetSharedMaterials(m_Materials);
            Material m = m_Materials[Math.Max(m_Index, 0)];
            return m != null ? access.Get(m, m_Id) : default;
        }

        // others' properties stay — an index slot with no block of its own starts from a copy of the whole block
        internal void Write<T, TAccess>(TAccess access, in T value) where TAccess : struct, IBlockAccess<T>
        {
            Get(m_Work);
            if (m_Index >= 0 && m_Work.isEmpty)
                m_Renderer.GetPropertyBlock(m_Work);
            access.Set(m_Work, m_Id, value);
            Set(m_Work);
        }

        // whether there was a block is the snapshot's emptiness — HasPropertyBlock counts others' index blocks too
        internal void BeforeWrite()
        {
            Get(m_Snapshot);
            m_HadBlock = !m_Snapshot.isEmpty;
        }

        internal void Restore() => Set(m_HadBlock ? m_Snapshot : null);

        // the first Persist keeps the block of that time and the value shown then. A later Persist back to that value (the
        // player's Stop(Revert) · Restore()) puts that block back, so no block with the old value stays
        internal void WriteDirect<T, TOps, TAccess>(TAccess access, in T value, ref T origin) where TOps : struct, IValueOps<T> where TAccess : struct, IBlockAccess<T>
        {
            if (!m_Persisted)
            {
                Get(m_PersistSnapshot);
                m_PersistHad = !m_PersistSnapshot.isEmpty;
                origin = Read<T, TAccess>(access);
                m_Persisted = true;
            }
            else if (default(TOps).BitEquals(value, origin))
            {
                Set(m_PersistHad ? m_PersistSnapshot : null);
                m_Persisted = false;
                return;
            }
            Write(access, value);
        }

        // a preview's Persist goes with the session — the stop puts back what was recorded, and a block is not serialized.
        // The registry's close drops every slot: the block of the first Persist comes back (none → null). The runtime keeps
        // its Persist
        internal void Dropped(PropertyLayerRegistry registry)
        {
            if (!m_Persisted || registry == null || !registry.IsPreview || m_Renderer == null)
                return;
            Set(m_PersistHad ? m_PersistSnapshot : null);
            m_Persisted = false;
        }
    }

    // a property written into the renderer's copies — one index or every index (−1) whose material has the property: another
    // shader's lacks it, and a read there logs an error. A written color does not read back bit for bit in Linear and drifts
    // when read and written again, so the base goes on and each copy's value is put back on its own by the same rule. The
    // copies are the renderer's copies slot's, taken at the attach (a nested GetOrCreate — filed after the property slot);
    // the drop lets it go, not while the registry closes (the close drops the copies slot itself)
    internal sealed class MaterialInstance
    {
        readonly Renderer m_Renderer;
        readonly int m_Id;
        readonly int m_Index;
        MaterialCopiesSlot m_Copies;
        int[] m_At = Array.Empty<int>();
        bool m_Restored;
        bool m_Let;

        internal MaterialInstance(Renderer renderer, int propertyId, int index)
        {
            m_Renderer = renderer;
            m_Id = propertyId;
            m_Index = index;
        }

        internal int Count => m_At.Length;

        internal bool IsAlive => m_Renderer != null && m_Copies != null && m_Copies.Live;

        // observation
        internal MaterialCopiesSlot Copies => m_Copies;

        Material CopyAt(int k) => m_Copies.Copy(m_At[k]);

        bool Takes<T, TAccess>(TAccess access, int index) where TAccess : struct, IMaterialAccess<T>
        {
            Material original = m_Copies.Original(index);
            return original != null && access.Has(original, m_Id);
        }

        internal void Attach<T, TAccess>(PropertyLayerRegistry registry, TAccess access) where TAccess : struct, IMaterialAccess<T>
        {
            Renderer renderer = m_Renderer;
            m_Copies = registry.GetOrCreate(renderer, 0, -1, () => new MaterialCopiesSlot(renderer));
            int from = m_Index < 0 ? 0 : m_Index;
            int to = m_Index < 0 ? m_Copies.Count : Math.Min(m_Index + 1, m_Copies.Count);
            int n = 0;
            for (int i = from; i < to; i++)
            {
                if (Takes<T, TAccess>(access, i))
                    n++;
            }
            m_At = new int[n];
            n = 0;
            for (int i = from; i < to; i++)
            {
                if (!Takes<T, TAccess>(access, i))
                    continue;
                m_At[n++] = i;
                m_Copies.Ensure(i);
            }
        }

        internal T Read<T, TAccess>(TAccess access) where TAccess : struct, IMaterialAccess<T>
            => m_At.Length > 0 ? access.Get(CopyAt(0), m_Id) : default;

        internal void Write<T, TAccess>(TAccess access, in T value) where TAccess : struct, IMaterialAccess<T>
        {
            for (int k = 0; k < m_At.Length; k++)
            {
                Material copy = CopyAt(k);
                if (copy != null)
                    access.Set(copy, m_Id, value);
            }
        }

        // a copy whose value is our own restore read back keeps the value taken before
        internal void BeforeWrite<T, TOps, TAccess>(TAccess access, T[] before, T[] after) where TOps : struct, IValueOps<T> where TAccess : struct, IMaterialAccess<T>
        {
            for (int k = 0; k < m_At.Length; k++)
            {
                Material copy = CopyAt(k);
                if (copy == null)
                    continue;
                T now = access.Get(copy, m_Id);
                if (!(m_Restored && default(TOps).BitEquals(now, after[k])))
                    before[k] = now;
            }
            m_Restored = false;
        }

        internal void Restore<T, TAccess>(TAccess access, T[] before, T[] after) where TAccess : struct, IMaterialAccess<T>
        {
            for (int k = 0; k < m_At.Length; k++)
            {
                Material copy = CopyAt(k);
                if (copy == null)
                    continue;
                access.Set(copy, m_Id, before[k]);
                after[k] = access.Get(copy, m_Id);
            }
            m_Restored = true;
        }

        internal void Dropped(PropertyLayerRegistry registry)
        {
            if (m_Let)
                return;
            m_Let = true;
            if (m_Copies != null && registry != null && !registry.IsClosed)
                registry.Release(m_Copies, 0);
        }
    }

    // the block slots — one per value form, each with its own record attribute (the recorder's table looks the concrete
    // type up)
    [NotRecorded("a block is not serialized — the layer puts the block back, and a preview's Persist at its end")]
    internal sealed class MaterialBlockColorSlot : ValueSlot<Color, ColorOps>
    {
        readonly MaterialBlock m_Block;
        Color m_PersistOrigin;

        internal MaterialBlockColorSlot(Renderer renderer, int propertyId, int index)
            : base(ConsumptionStage.LateApply, Composition.Additive, false)
        {
            m_Block = new MaterialBlock(renderer, propertyId, index);
        }

        internal override bool IsAlive => m_Block.IsAlive;

        protected override Color Read() => m_Block.Read<Color, ColorAccess>(default);

        protected override void Write(in Color value) => m_Block.Write(default(ColorAccess), value);

        protected override void BeforeWrite() => m_Block.BeforeWrite();

        protected override void Restore(in Color baseValue) => m_Block.Restore();

        protected override void WriteDirect(in Color value) => m_Block.WriteDirect<Color, ColorOps, ColorAccess>(default, value, ref m_PersistOrigin);

        internal override void Dropped() => m_Block.Dropped(Registry);
    }

    [NotRecorded("a block is not serialized — the layer puts the block back, and a preview's Persist at its end")]
    internal sealed class MaterialBlockFloatSlot : ValueSlot<float, FloatOps>
    {
        readonly MaterialBlock m_Block;
        float m_PersistOrigin;

        internal MaterialBlockFloatSlot(Renderer renderer, int propertyId, int index)
            : base(ConsumptionStage.LateApply, Composition.Additive, false)
        {
            m_Block = new MaterialBlock(renderer, propertyId, index);
        }

        internal override bool IsAlive => m_Block.IsAlive;

        protected override float Read() => m_Block.Read<float, FloatAccess>(default);

        protected override void Write(in float value) => m_Block.Write(default(FloatAccess), value);

        protected override void BeforeWrite() => m_Block.BeforeWrite();

        protected override void Restore(in float baseValue) => m_Block.Restore();

        protected override void WriteDirect(in float value) => m_Block.WriteDirect<float, FloatOps, FloatAccess>(default, value, ref m_PersistOrigin);

        internal override void Dropped() => m_Block.Dropped(Registry);
    }

    [NotRecorded("a block is not serialized — the layer puts the block back, and a preview's Persist at its end")]
    internal sealed class MaterialBlockVectorSlot : ValueSlot<Vector4, Vector4Ops>
    {
        readonly MaterialBlock m_Block;
        Vector4 m_PersistOrigin;

        internal MaterialBlockVectorSlot(Renderer renderer, int propertyId, int index)
            : base(ConsumptionStage.LateApply, Composition.Additive, false)
        {
            m_Block = new MaterialBlock(renderer, propertyId, index);
        }

        internal override bool IsAlive => m_Block.IsAlive;

        protected override Vector4 Read() => m_Block.Read<Vector4, VectorAccess>(default);

        protected override void Write(in Vector4 value) => m_Block.Write(default(VectorAccess), value);

        protected override void BeforeWrite() => m_Block.BeforeWrite();

        protected override void Restore(in Vector4 baseValue) => m_Block.Restore();

        protected override void WriteDirect(in Vector4 value) => m_Block.WriteDirect<Vector4, Vector4Ops, VectorAccess>(default, value, ref m_PersistOrigin);

        internal override void Dropped() => m_Block.Dropped(Registry);
    }

    // a texture's _ST — one slot for the offset and the scale (the key's property is the _ST id); each binding writes its half
    // over the slot's other half
    [NotRecorded("a block is not serialized — the layer puts the block back, and a preview's Persist at its end")]
    internal sealed class MaterialBlockTextureSlot : ValueSlot<Vector4, Vector4Ops>
    {
        readonly MaterialBlock m_Block;
        Vector4 m_PersistOrigin;

        internal MaterialBlockTextureSlot(Renderer renderer, int stId, int index)
            : base(ConsumptionStage.LateApply, Composition.Additive, false)
        {
            m_Block = new MaterialBlock(renderer, stId, index);
        }

        internal override bool IsAlive => m_Block.IsAlive;

        protected override Vector4 Read() => m_Block.Read<Vector4, VectorAccess>(default);

        protected override void Write(in Vector4 value) => m_Block.Write(default(VectorAccess), value);

        protected override void BeforeWrite() => m_Block.BeforeWrite();

        protected override void Restore(in Vector4 baseValue) => m_Block.Restore();

        protected override void WriteDirect(in Vector4 value) => m_Block.WriteDirect<Vector4, Vector4Ops, VectorAccess>(default, value, ref m_PersistOrigin);

        internal override void Dropped() => m_Block.Dropped(Registry);
    }

    // the instance slots — the renderer's copies slot records the references
    [NotRecorded("the renderer's copies slot records the references")]
    internal sealed class MaterialInstanceColorSlot : ValueSlot<Color, ColorOps>
    {
        readonly MaterialInstance m_Instance;
        Color[] m_Before;
        Color[] m_After;

        internal MaterialInstanceColorSlot(Renderer renderer, int propertyId, int index)
            : base(ConsumptionStage.LateApply, Composition.Additive, true)
        {
            m_Instance = new MaterialInstance(renderer, propertyId, index);
        }

        internal override void Attached()
        {
            m_Instance.Attach<Color, ColorAccess>(Registry, default);
            m_Before = new Color[m_Instance.Count];
            m_After = new Color[m_Instance.Count];
        }

        internal override bool IsAlive => m_Instance.IsAlive;

        // observation
        internal MaterialCopiesSlot Copies => m_Instance.Copies;

        protected override Color Read() => m_Instance.Read<Color, ColorAccess>(default);

        protected override void Write(in Color value) => m_Instance.Write(default(ColorAccess), value);

        protected override void BeforeWrite() => m_Instance.BeforeWrite<Color, ColorOps, ColorAccess>(default, m_Before, m_After);

        protected override void Restore(in Color baseValue) => m_Instance.Restore(default(ColorAccess), m_Before, m_After);

        internal override void Dropped() => m_Instance.Dropped(Registry);
    }

    [NotRecorded("the renderer's copies slot records the references")]
    internal sealed class MaterialInstanceFloatSlot : ValueSlot<float, FloatOps>
    {
        readonly MaterialInstance m_Instance;
        float[] m_Before;
        float[] m_After;

        internal MaterialInstanceFloatSlot(Renderer renderer, int propertyId, int index)
            : base(ConsumptionStage.LateApply, Composition.Additive, false)
        {
            m_Instance = new MaterialInstance(renderer, propertyId, index);
        }

        internal override void Attached()
        {
            m_Instance.Attach<float, FloatAccess>(Registry, default);
            m_Before = new float[m_Instance.Count];
            m_After = new float[m_Instance.Count];
        }

        internal override bool IsAlive => m_Instance.IsAlive;

        protected override float Read() => m_Instance.Read<float, FloatAccess>(default);

        protected override void Write(in float value) => m_Instance.Write(default(FloatAccess), value);

        protected override void BeforeWrite() => m_Instance.BeforeWrite<float, FloatOps, FloatAccess>(default, m_Before, m_After);

        protected override void Restore(in float baseValue) => m_Instance.Restore(default(FloatAccess), m_Before, m_After);

        internal override void Dropped() => m_Instance.Dropped(Registry);
    }

    [NotRecorded("the renderer's copies slot records the references")]
    internal sealed class MaterialInstanceVectorSlot : ValueSlot<Vector4, Vector4Ops>
    {
        readonly MaterialInstance m_Instance;
        Vector4[] m_Before;
        Vector4[] m_After;

        internal MaterialInstanceVectorSlot(Renderer renderer, int propertyId, int index)
            : base(ConsumptionStage.LateApply, Composition.Additive, false)
        {
            m_Instance = new MaterialInstance(renderer, propertyId, index);
        }

        internal override void Attached()
        {
            m_Instance.Attach<Vector4, VectorAccess>(Registry, default);
            m_Before = new Vector4[m_Instance.Count];
            m_After = new Vector4[m_Instance.Count];
        }

        internal override bool IsAlive => m_Instance.IsAlive;

        protected override Vector4 Read() => m_Instance.Read<Vector4, VectorAccess>(default);

        protected override void Write(in Vector4 value) => m_Instance.Write(default(VectorAccess), value);

        protected override void BeforeWrite() => m_Instance.BeforeWrite<Vector4, Vector4Ops, VectorAccess>(default, m_Before, m_After);

        protected override void Restore(in Vector4 baseValue) => m_Instance.Restore(default(VectorAccess), m_Before, m_After);

        internal override void Dropped() => m_Instance.Dropped(Registry);
    }

    // a texture's ST in the copies — written through the texture's scale and offset (the key's property is the _ST id)
    [NotRecorded("the renderer's copies slot records the references")]
    internal sealed class MaterialInstanceTextureSlot : ValueSlot<Vector4, Vector4Ops>
    {
        readonly MaterialInstance m_Instance;
        readonly TextureAccess m_Access;
        Vector4[] m_Before;
        Vector4[] m_After;

        internal MaterialInstanceTextureSlot(Renderer renderer, int stId, int index, int textureId)
            : base(ConsumptionStage.LateApply, Composition.Additive, false)
        {
            m_Instance = new MaterialInstance(renderer, stId, index);
            m_Access = new TextureAccess(textureId);
        }

        internal override void Attached()
        {
            m_Instance.Attach<Vector4, TextureAccess>(Registry, m_Access);
            m_Before = new Vector4[m_Instance.Count];
            m_After = new Vector4[m_Instance.Count];
        }

        internal override bool IsAlive => m_Instance.IsAlive;

        protected override Vector4 Read() => m_Instance.Read<Vector4, TextureAccess>(m_Access);

        protected override void Write(in Vector4 value) => m_Instance.Write(m_Access, value);

        protected override void BeforeWrite() => m_Instance.BeforeWrite<Vector4, Vector4Ops, TextureAccess>(m_Access, m_Before, m_After);

        protected override void Restore(in Vector4 baseValue) => m_Instance.Restore(m_Access, m_Before, m_After);

        internal override void Dropped() => m_Instance.Dropped(Registry);
    }

    // one renderer's copies — every instance property slot of the renderer (shared or private) writes into them. For the
    // renderer, not per slot: hand-made copies put in through sharedMaterials on the preparation path — renderer.material(s)
    // logs an Error per material in edit mode. The references are recorded at its attach; an index's copy is made and put in
    // when the first slot of that index asks (an index slot copies its index only). Dropped with the last slot: the originals
    // back where our copies still are (the game's own material at an index stays), the copies destroyed, once
    [RecordPaths(typeof(Renderer), "m_Materials.Array.data[{index}]")]
    internal sealed class MaterialCopiesSlot : LayerSlot
    {
        readonly Renderer m_Renderer;
        readonly Material[] m_Originals;
        readonly Material[] m_Copies;
        bool m_Freed;

        internal MaterialCopiesSlot(Renderer renderer)
            : base(ConsumptionStage.LateApply)
        {
            m_Renderer = renderer;
            m_Originals = renderer.sharedMaterials;
            m_Copies = new Material[m_Originals.Length];
        }

        // the swaps are its writes — recorded before them
        internal override void Attached() => RecordOnce();

        internal int Count => m_Copies.Length;

        internal Material Original(int index) => m_Originals[index];

        internal Material Copy(int index) => m_Copies[index];

        internal bool Live => !m_Freed;

        // the index's copy, made and put in once. A preview's copies are hidden — the reference is recorded, a save during it
        // writes the original and not the copy. A preview's copy carries the recovery's prefix — a reload whose hook was
        // missed leaves none behind
        internal void Ensure(int index)
        {
            if (index < 0 || index >= m_Copies.Length || m_Copies[index] != null || m_Originals[index] == null)
                return;
            bool preview = Registry != null && Registry.IsPreview;
            var copy = new Material(m_Originals[index]) { name = (preview ? PropertyLayerRegistry.PreviewPrefix : "") + m_Originals[index].name + " (Feedback)" };
            if (preview)
                copy.hideFlags = HideFlags.HideAndDontSave;
            m_Copies[index] = copy;
            Material[] now = m_Renderer.sharedMaterials;
            if (index < now.Length)
            {
                now[index] = copy;
                m_Renderer.sharedMaterials = now;
            }
        }

        // a copy's original — any other material is itself
        internal Material OriginalOf(Material material)
        {
            for (int i = 0; i < m_Copies.Length; i++)
            {
                if (m_Copies[i] != null && ReferenceEquals(material, m_Copies[i]))
                    return m_Originals[i];
            }
            return material;
        }

        internal override bool IsAlive => m_Renderer != null;

        internal override bool Pending => false;

        internal override int LiveContributions => 0;

        internal override int Reserved => 0;

        internal override Vector4 BaseVector => Vector4.zero;

        internal override void Revert()
        {
        }

        internal override void Apply()
        {
        }

        internal override void Close()
        {
        }

        internal override void EndAll()
        {
        }

        internal override void Unreserve(int count)
        {
        }

        internal override void Dropped()
        {
            if (m_Freed)
                return;
            m_Freed = true;
            if (m_Renderer != null)
            {
                Material[] now = m_Renderer.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < m_Copies.Length && i < now.Length; i++)
                {
                    if (m_Copies[i] != null && ReferenceEquals(now[i], m_Copies[i]))
                    {
                        now[i] = m_Originals[i];
                        changed = true;
                    }
                }
                if (changed)
                    m_Renderer.sharedMaterials = now;
            }
            for (int i = 0; i < m_Copies.Length; i++)
            {
                if (m_Copies[i] == null)
                    continue;
                if (Application.isPlaying)
                    UnityEngine.Object.Destroy(m_Copies[i]);
                else
                    UnityEngine.Object.DestroyImmediate(m_Copies[i]);
            }
        }
    }

    // a material index's material — a state, Persist only (Material Swap). −1 is every index. Written through a list the slot
    // keeps (no array per write); the references are its rows
    [RecordPaths(typeof(Renderer), "m_Materials.Array.data[{index}]")]
    internal sealed class MaterialSwapSlot : LayerSlot
    {
        readonly Renderer m_Renderer;
        readonly int m_Index;
        readonly List<Material> m_List = new List<Material>(4);
        int m_Reserved;

        internal MaterialSwapSlot(Renderer renderer, int index)
            : base(ConsumptionStage.Update)
        {
            m_Renderer = renderer;
            m_Index = index;
        }

        internal Renderer Renderer => m_Renderer;

        internal override bool IsAlive => m_Renderer != null;

        internal override bool Pending => false;

        internal override int LiveContributions => 0;

        internal override int Reserved => m_Reserved;

        internal override Vector4 BaseVector => Vector4.zero;

        internal void Reserve(int count) => m_Reserved += count;

        internal override void Unreserve(int count) => m_Reserved -= Math.Min(count, m_Reserved);

        internal override void Revert()
        {
        }

        internal override void Apply()
        {
        }

        internal override void Close()
        {
        }

        internal override void EndAll()
        {
        }

        // the renderer's materials now — past its count null
        internal void Read(Material[] into)
        {
            m_Renderer.GetSharedMaterials(m_List);
            for (int i = 0; i < into.Length; i++)
                into[i] = i < m_List.Count ? m_List[i] : null;
        }

        // Persist — the door
        internal void WriteMaterial(Material material)
        {
            RecordOnce();
            m_Renderer.GetSharedMaterials(m_List);
            for (int i = 0; i < m_List.Count; i++)
            {
                if (m_Index < 0 || i == m_Index)
                    m_List[i] = material;
            }
            m_Renderer.SetSharedMaterials(m_List);
        }

        internal void WriteState(Material[] state)
        {
            RecordOnce();
            m_Renderer.GetSharedMaterials(m_List);
            for (int i = 0; i < m_List.Count && i < state.Length; i++)
            {
                if (m_Index < 0 || i == m_Index)
                    m_List[i] = state[i];
            }
            m_Renderer.SetSharedMaterials(m_List);
        }
    }
}
