using System;
using TMPro;
using UnityEngine;

namespace xpTURN.Feedback
{
    // the three items' channel — not saved
    internal enum TMPCharacterChannel
    {
        Offset = 0,
        Scale = 1,
        Alpha = 2,
    }

    // one text's characters, shared by every Character item on the text. It keeps a copy of the original vertices and colours
    // (taken when TMP rebuilds the mesh — OnPreRenderText runs before the upload and before a reverse sort), each channel's
    // kept value (a Tween's end — one value for every character, the original at first) and the contributions of the
    // running records. Each character is evaluated where the vertices are written — the LateApply
    // (UpdateVertexData) and OnPreRenderText (TMP uploads) — with that mesh's characters
    [NotRecorded("the vertices are not serialized — the mesh is HideAndDontSave and TMP rebuilds it")]
    internal sealed class TMPCharacterSlot : LayerSlot
    {
        struct Contribution
        {
            internal bool Live;
            internal TMPCharacterChannel Channel;
            internal MotionEvaluator Evaluator;
            internal MotionWrite Write;
            internal MotionState State;
            internal MotionTime Time;
            // the gate's scale — an offset's multiplier; a Persist record writes above 0
            internal float Scale;
            internal float Stagger;
            internal bool Backward;
            // an Oscillate's lag — of share 1, set with the time
            internal float Frequency;
            internal double Lag;
            // a Wiggle: each character its own seed
            internal bool OwnSeeds;
            internal long Order;
            internal bool Set;
        }

        readonly TMP_Text m_Text;
        readonly Action<TMP_TextInfo> m_OnPreRender;
        Contribution[] m_Contributions = new Contribution[0];
        int m_Live;
        int m_Reserved;
        long m_Orders;
        Vector2 m_KeptOffset;
        float m_KeptScale = 1f;
        float m_KeptAlpha = 1f;
        // the copy in characterInfo's order (before a reverse sort)
        Vector3[][] m_Vertices = new Vector3[0][];
        Color32[][] m_Colors = new Color32[0][];
        int[] m_VertexCounts = new int[0];
        int m_Materials;
        // the copy's character table — the mesh's characters (a layout query between rebuilds rewrites characterInfo)
        int[] m_CharMaterial = new int[0];
        int[] m_CharVertex = new int[0];
        float[] m_CharSize = new float[0];
        int m_Characters;
        // the copy is TMP's mesh as it is now
        bool m_HasCopy;
        // the uploaded mesh holds our values
        bool m_Written;
        // the next Apply writes — a kept value changed, the last record ended
        bool m_Dirty;
        bool m_Subscribed;
        bool m_Dropped;

        internal TMPCharacterSlot(TMP_Text text)
            : base(ConsumptionStage.LateApply)
        {
            m_Text = text;
            m_OnPreRender = OnPreRender;
        }

        internal override void Attached()
        {
            m_Text.OnPreRenderText += m_OnPreRender;
            m_Subscribed = true;
        }

        // the preparation path (allocation allowed): the records' room and the copy's room at TMP's capacity
        internal void Reserve(int count)
        {
            m_Reserved += count;
            if (m_Contributions.Length < m_Reserved)
                Array.Resize(ref m_Contributions, m_Reserved);
            TMP_TextInfo info = m_Text.textInfo;
            if (info == null || info.meshInfo == null)
                return;
            Room(info.materialCount);
            for (int m = 0; m < info.materialCount && m < info.meshInfo.Length; m++)
                RoomFor(m, info.meshInfo[m].vertices != null ? info.meshInfo[m].vertices.Length : 0);
            RoomForCharacters(info.characterInfo != null ? info.characterInfo.Length : 0);
        }

        void RoomForCharacters(int count)
        {
            if (m_CharVertex.Length >= count)
                return;
            Array.Resize(ref m_CharMaterial, count);
            Array.Resize(ref m_CharVertex, count);
            Array.Resize(ref m_CharSize, count);
        }

        void Room(int materials)
        {
            if (m_Vertices.Length >= materials)
                return;
            Array.Resize(ref m_Vertices, materials);
            Array.Resize(ref m_Colors, materials);
            Array.Resize(ref m_VertexCounts, materials);
        }

        // grown keeping what it holds — a later binding's prepare must not wipe the copy
        void RoomFor(int m, int count)
        {
            if (m_Vertices[m] != null && m_Vertices[m].Length >= count)
                return;
            Array.Resize(ref m_Vertices[m], count);
            Array.Resize(ref m_Colors[m], count);
        }

        internal int Open(TMPCharacterChannel channel, MotionWrite write, MotionEvaluator evaluator, float stagger, bool backward, float frequency, bool ownSeeds)
        {
            int at = -1;
            for (int i = 0; i < m_Contributions.Length; i++)
                if (!m_Contributions[i].Live)
                {
                    at = i;
                    break;
                }
            if (at < 0)
            {
                at = m_Contributions.Length;
                Array.Resize(ref m_Contributions, at * 2 + 1);
                Registry?.LedgerGrew(this);
            }
            m_Contributions[at] = new Contribution
            {
                Live = true,
                Channel = channel,
                Evaluator = evaluator,
                Write = write,
                Stagger = stagger,
                Backward = backward,
                Frequency = frequency,
                OwnSeeds = ownSeeds,
                Order = ++m_Orders,
            };
            m_Live++;
            Wake();
            return at;
        }

        internal void Set(int handle, in MotionState state, in MotionTime time, float scale)
        {
            ref Contribution c = ref m_Contributions[handle];
            c.State = state;
            c.Time = time;
            c.Lag = CharacterStagger.Lag(time.Seconds, time.T, c.Stagger, c.Frequency);
            c.Scale = scale;
            c.Set = true;
        }

        internal void End(int handle)
        {
            if (handle < 0 || handle >= m_Contributions.Length || !m_Contributions[handle].Live)
                return;
            m_Contributions[handle].Live = false;
            m_Contributions[handle].Evaluator = null;
            m_Live--;
            m_Dirty = true;
            Wake();
        }

        internal Vector4 Kept(TMPCharacterChannel channel)
        {
            switch (channel)
            {
                case TMPCharacterChannel.Offset: return new Vector4(m_KeptOffset.x, m_KeptOffset.y, 0f, 0f);
                case TMPCharacterChannel.Scale: return new Vector4(m_KeptScale, 0f, 0f, 0f);
                default: return new Vector4(m_KeptAlpha, 0f, 0f, 0f);
            }
        }

        internal void Keep(TMPCharacterChannel channel, in Vector4 value)
        {
            switch (channel)
            {
                case TMPCharacterChannel.Offset:
                    m_KeptOffset = new Vector2(value.x, value.y);
                    break;
                case TMPCharacterChannel.Scale:
                    m_KeptScale = value.x;
                    break;
                default:
                    m_KeptAlpha = value.x;
                    break;
            }
            m_Dirty = true;
            Wake();
        }

        bool KeptIsOriginal => m_KeptOffset.x == 0f && m_KeptOffset.y == 0f && m_KeptScale == 1f && m_KeptAlpha == 1f;

        void Wake()
        {
            if (!Active && Registry != null && !Registry.IsClosed && !Detached)
                Registry.Activate(this);
        }

        internal override bool IsAlive => m_Text != null;

        internal override bool Pending => m_Dirty;

        internal override int LiveContributions => m_Live;

        internal override int Reserved => m_Reserved;

        internal override Vector4 BaseVector => Vector4.zero;

        // the vertices need no frame start — TMP rebuilds them, and the LateApply writes from the copy
        internal override void Revert()
        {
        }

        internal override void Apply()
        {
            if (m_Dropped || m_Text == null || (m_Live == 0 && !m_Dirty))
                return;
            m_Dirty = false;
            TMP_TextInfo info = m_Text.textInfo;
            if (info == null || info.meshInfo == null)
                return;
            bool reverse = m_Text.geometrySortingOrder == VertexSortingOrder.Reverse;
            if (!m_HasCopy)
                Copy(info, reverse);
            // a text without a mesh (a UGUI text under no canvas) has nothing to write
            if (m_Characters == 0)
                return;
            if (m_Live == 0 && KeptIsOriginal)
            {
                if (m_Written)
                {
                    PutBack(info, reverse);
                    Upload();
                }
                return;
            }
            RecordOnce();
            Write(info, reverse);
            Upload();
            m_Written = true;
        }

        // a text that destroyed its mesh (its own OnDestroy ran first) takes no upload
        void Upload()
        {
            if (m_Text == null || m_Text.mesh == null)
                return;
            m_Text.UpdateVertexData(TMP_VertexDataUpdateFlags.Vertices | TMP_VertexDataUpdateFlags.Colors32);
        }

        // TMP rebuilt the mesh: a fresh original before the upload and before a reverse sort — written into, TMP uploads it
        void OnPreRender(TMP_TextInfo info)
        {
            if (m_Dropped)
                return;
            m_Written = false;
            if (m_Live == 0 && KeptIsOriginal)
            {
                // a quiet text pays nothing — the next activation copies TMP's arrays
                m_HasCopy = false;
                return;
            }
            Copy(info, false);
            Write(info, false);
            m_Written = true;
        }

        internal override void Close()
        {
            Restore();
        }

        internal override void EndAll()
        {
            for (int i = 0; i < m_Contributions.Length; i++)
            {
                m_Contributions[i].Live = false;
                m_Contributions[i].Evaluator = null;
            }
            m_Live = 0;
        }

        internal override void Unreserve(int count)
        {
            m_Reserved -= count;
        }

        internal override void Dropped()
        {
            if (m_Dropped)
                return;
            Restore();
            m_Dropped = true;
            if (m_Subscribed && m_Text != null)
                m_Text.OnPreRenderText -= m_OnPreRender;
            m_Subscribed = false;
        }

        void Restore()
        {
            if (!m_Written || m_Text == null)
                return;
            TMP_TextInfo info = m_Text.textInfo;
            if (info != null && info.meshInfo != null)
            {
                PutBack(info, m_Text.geometrySortingOrder == VertexSortingOrder.Reverse);
                Upload();
            }
            m_Written = false;
        }

        // TMP's arrays as they are — a reverse sort mirrored back to characterInfo's order
        void Copy(TMP_TextInfo info, bool sorted)
        {
            int materials = Math.Min(info.materialCount, info.meshInfo.Length);
            Room(materials);
            for (int m = 0; m < materials; m++)
            {
                int n = info.meshInfo[m].vertexCount;
                RoomFor(m, n);
                Vector3[] v = info.meshInfo[m].vertices;
                Color32[] c = info.meshInfo[m].colors32;
                if (!sorted)
                {
                    Array.Copy(v, m_Vertices[m], n);
                    Array.Copy(c, m_Colors[m], n);
                }
                else
                {
                    for (int q = 0; q < n; q += 4)
                    {
                        int from = n - 4 - q;
                        Array.Copy(v, from, m_Vertices[m], q, 4);
                        Array.Copy(c, from, m_Colors[m], q, 4);
                    }
                }
                m_VertexCounts[m] = n;
            }
            m_Materials = materials;
            int count = info.characterCount;
            RoomForCharacters(count);
            for (int i = 0; i < count; i++)
            {
                ref TMP_CharacterInfo ch = ref info.characterInfo[i];
                // a space has no quad (its vertexIndex is 0 — the first character's), a hidden one a quad of size 0
                m_CharVertex[i] = ch.isVisible ? ch.vertexIndex : -1;
                m_CharMaterial[i] = ch.materialReferenceIndex;
                m_CharSize[i] = ch.ascender - ch.descender;
            }
            m_Characters = count;
            m_HasCopy = true;
        }

        void PutBack(TMP_TextInfo info, bool sorted)
        {
            int materials = Math.Min(m_Materials, info.meshInfo.Length);
            for (int m = 0; m < materials; m++)
            {
                int n = Math.Min(m_VertexCounts[m], info.meshInfo[m].vertexCount);
                Vector3[] v = info.meshInfo[m].vertices;
                Color32[] c = info.meshInfo[m].colors32;
                for (int q = 0; q + 3 < n; q += 4)
                {
                    int to = sorted ? n - 4 - q : q;
                    Array.Copy(m_Vertices[m], q, v, to, 4);
                    Array.Copy(m_Colors[m], q, c, to, 4);
                }
            }
        }

        void Write(TMP_TextInfo info, bool sorted)
        {
            int count = m_Characters;
            int persistOffset = Latest(TMPCharacterChannel.Offset), persistScale = Latest(TMPCharacterChannel.Scale), persistAlpha = Latest(TMPCharacterChannel.Alpha);
            for (int i = 0; i < count; i++)
            {
                int m = m_CharMaterial[i], v = m_CharVertex[i];
                if (v < 0 || m >= m_Materials || v + 3 >= m_VertexCounts[m])
                    continue;
                Vector2 offset = persistOffset >= 0 ? (Vector2)Persisted(persistOffset, i, count) : m_KeptOffset;
                float scale = persistScale >= 0 ? Persisted(persistScale, i, count).x : m_KeptScale;
                float alpha = persistAlpha >= 0 ? Persisted(persistAlpha, i, count).x : m_KeptAlpha;
                for (int k = 0; k < m_Contributions.Length; k++)
                {
                    ref Contribution c = ref m_Contributions[k];
                    if (!c.Live || !c.Set || c.Write != MotionWrite.Offset)
                        continue;
                    Vector4 o = Offset(ref c, i, count) * c.Scale;
                    switch (c.Channel)
                    {
                        case TMPCharacterChannel.Offset:
                            offset.x += o.x;
                            offset.y += o.y;
                            break;
                        case TMPCharacterChannel.Scale:
                            scale *= 1f + o.x;
                            break;
                        default:
                            alpha *= 1f + o.x;
                            break;
                    }
                }
                if (scale < 0f)
                    scale = 0f;
                alpha = alpha < 0f ? 0f : alpha > 1f ? 1f : alpha;
                Vector3[] src = m_Vertices[m];
                Color32[] srcColors = m_Colors[m];
                Vector3[] dst = info.meshInfo[m].vertices;
                Color32[] dstColors = info.meshInfo[m].colors32;
                int d = sorted ? m_VertexCounts[m] - 4 - v : v;
                Vector3 center = (src[v] + src[v + 2]) * 0.5f;
                float size = m_CharSize[i];
                var shift = new Vector3(offset.x * size, offset.y * size, 0f);
                for (int q = 0; q < 4; q++)
                {
                    dst[d + q] = center + (src[v + q] - center) * scale + shift;
                    Color32 color = srcColors[v + q];
                    color.a = (byte)(color.a * alpha + 0.5f);
                    dstColors[d + q] = color;
                }
            }
        }

        // the running Persist record that began last on the channel — written while its category is on
        int Latest(TMPCharacterChannel channel)
        {
            int found = -1;
            long order = -1;
            for (int k = 0; k < m_Contributions.Length; k++)
            {
                ref Contribution c = ref m_Contributions[k];
                if (c.Live && c.Set && c.Write == MotionWrite.Persist && c.Channel == channel && c.Scale > 0f && c.Order > order)
                {
                    found = k;
                    order = c.Order;
                }
            }
            return found;
        }

        Vector4 Persisted(int k, int i, int count)
        {
            ref Contribution c = ref m_Contributions[k];
            float t = CharacterStagger.Window(c.Time.T, c.Stagger, CharacterStagger.Share(i, count, c.Backward));
            c.Evaluator.Evaluate(in c.State, new MotionTime(t, c.Time.Seconds), out MotionSample sample);
            return sample.Value;
        }

        static Vector4 Offset(ref Contribution c, int i, int count)
        {
            if (c.OwnSeeds)
            {
                MotionState s = c.State;
                s.Seed = Hash32.Tagged(c.State.Seed, Hash32.Copy, (uint)i);
                c.Evaluator.Evaluate(in s, in c.Time, out MotionSample own);
                return own.Value;
            }
            double seconds = CharacterStagger.Lagged(c.Time.Seconds, c.Lag, CharacterStagger.Share(i, count, c.Backward));
            c.Evaluator.Evaluate(in c.State, new MotionTime(c.Time.T, seconds), out MotionSample sample);
            return sample.Value;
        }
    }
}
