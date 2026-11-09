using System;
using System.Collections.Generic;
using UnityEngine;
using xpTURN.Feedback.Editor;

namespace xpTURN.Feedback.Tests.Editor
{
    // the tests' record source — the core slot types' rows (the package's source reads them from the slot types themselves)
    // and the calls counted per key. A slot type can be forgotten — no rows, the fallback's case — and the preflight's
    // question has the same answer as Get. The instance's references are the renderer's copies slot's
    internal sealed class PreviewTestSource : IPreviewRecordSource
    {
        static readonly string[] s_Position = { "m_LocalPosition.x", "m_LocalPosition.y", "m_LocalPosition.z" };
        static readonly string[] s_Rotation = { "m_LocalRotation.x", "m_LocalRotation.y", "m_LocalRotation.z", "m_LocalRotation.w" };
        static readonly string[] s_Scale = { "m_LocalScale.x", "m_LocalScale.y", "m_LocalScale.z" };
        static readonly string[] s_Intensity = { "m_Intensity" };
        static readonly string[] s_Active = { "m_IsActive" };

        readonly Dictionary<LayerKey, int> m_Calls = new Dictionary<LayerKey, int>();

        internal readonly HashSet<Type> Forgotten = new HashSet<Type>();

        internal int Calls(UnityEngine.Object target, Type slotType, int aux = 0, int aux2 = 0)
            => m_Calls.TryGetValue(new LayerKey(target, slotType, aux, aux2), out int n) ? n : 0;

        internal int AllCalls
        {
            get
            {
                int total = 0;
                foreach (int n in m_Calls.Values)
                    total += n;
                return total;
            }
        }

        public bool Knows(Type type)
            => !Forgotten.Contains(type) && (type == typeof(MaterialBlockColorSlot) || type == typeof(MaterialInstanceColorSlot) || type == typeof(MaterialCopiesSlot) || RowsOf(type) != null);

        static string[] RowsOf(Type type)
            => type == typeof(LocalPositionSlot) ? s_Position
                : type == typeof(LocalRotationSlot) ? s_Rotation
                : type == typeof(LocalScaleSlot) ? s_Scale
                : type == typeof(LightIntensitySlot) ? s_Intensity
                : type == typeof(GameObjectActiveSlot) ? s_Active
                : null;

        public PreviewRecordResult Get(in LayerKey key, List<PreviewRecordPath> paths)
        {
            m_Calls.TryGetValue(key, out int n);
            m_Calls[key] = n + 1;
            Type type = key.SlotType;
            if (Forgotten.Contains(type))
                return PreviewRecordResult.Unknown;
            if (type == typeof(MaterialBlockColorSlot) || type == typeof(MaterialInstanceColorSlot))
                return PreviewRecordResult.Skip;
            if (type == typeof(MaterialCopiesSlot))
            {
                var renderer = (Renderer)key.Target;
                int count = renderer.sharedMaterials.Length;
                for (int i = 0; i < count; i++)
                {
                    if (key.Aux2 < 0 || key.Aux2 == i)
                        paths.Add(new PreviewRecordPath(renderer, "m_Materials.Array.data[" + i + "]"));
                }
                return PreviewRecordResult.Record;
            }
            string[] rows = RowsOf(type);
            if (rows == null)
                return PreviewRecordResult.Unknown;
            foreach (string path in rows)
                paths.Add(new PreviewRecordPath(key.Target, path));
            return PreviewRecordResult.Record;
        }
    }
}
