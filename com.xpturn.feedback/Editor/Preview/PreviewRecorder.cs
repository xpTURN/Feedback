using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace xpTURN.Feedback.Editor
{
    // the layer calls Record with a slot's key before its first write — once a slot, the instance slot before its swap. Each
    // (object, path) goes into AnimationMode with its serialized value now: the stop puts it back and a save in between
    // writes it. A property recorded twice keeps its first value — the engine keeps it, so a slot made again records again
    // and there is no table here. The scenes of what was recorded are kept — a scene closing under any of them ends the
    // session. A slot type with no rows falls back to what animates of its target, with a warning once a type a session
    internal sealed class PreviewRecorder : ILayerRecorder
    {
        const int NegativeZero = unchecked((int)0x80000000);

        internal const string FallbackWarning = "Feedback preview: {0} has no record paths — after the preview only the animatable properties of its target come back.";

        readonly List<PreviewRecordPath> m_Paths = new List<PreviewRecordPath>();
        readonly HashSet<Scene> m_Scenes = new HashSet<Scene>();
        readonly HashSet<Type> m_Warned = new HashSet<Type>();
        // the stop clears the blocks of a renderer whose property was recorded (the engine — a save does not) — taken at its
        // first record and again right before the session's stop, whole and per material index, and put back right after the
        // stop
        readonly List<Renderer> m_Renderers = new List<Renderer>();
        readonly List<MaterialPropertyBlock[]> m_Blocks = new List<MaterialPropertyBlock[]>();

        // the scenes of what was recorded — what failed to bind is not there
        internal bool Touches(Scene scene) => m_Scenes.Contains(scene);

        public void Record(in LayerKey key)
        {
            PreviewSlotStates.OnRecord(in key);
            m_Paths.Clear();
            PreviewRecordResult result = PreviewRecordPaths.Get(in key, m_Paths);
            if (result == PreviewRecordResult.Unknown)
            {
                Warn(key.SlotType);
                RecordAll(key.Target);
                return;
            }
            if (result != PreviewRecordResult.Record)
                return;
            for (int i = 0; i < m_Paths.Count; i++)
            {
                Object owner = m_Paths[i].Owner;
                TakeBlocks(owner);
                if (Add(owner, m_Paths[i].Path))
                    m_Scenes.Add(owner is Component component ? component.gameObject.scene : ((GameObject)owner).scene);
            }
        }

        // once a slot type a session — the preflight and the fallback share it
        internal void Warn(Type slotType)
        {
            if (m_Warned.Add(slotType))
                Debug.LogWarning(string.Format(FallbackWarning, slotType.Name));
        }

        // what AnimationMode can animate of the key's component — the bindings the Animation window lists for its type: the
        // same set as converting every leaf, without a long array's leaves. A GameObject lists none of its own: its active
        // state goes by the direct binding — binding every GameObject property would let the stop undo edits of the name, the
        // layer, the component list. An asset has no GameObject root: the warning alone
        void RecordAll(Object owner)
        {
            GameObject go = owner is Component component ? component.gameObject : owner as GameObject;
            if (go == null)
                return;
            bool any = false;
            if (owner is GameObject)
                any = Add(owner, "m_IsActive");
            else
            {
                TakeBlocks(owner);
                var serialized = new SerializedObject(owner);
                Type type = owner.GetType();
                foreach (EditorCurveBinding binding in AnimationUtility.GetAnimatableBindings(go, go))
                {
                    if (binding.type != type)
                        continue;
                    SerializedProperty property = serialized.FindProperty(binding.propertyName);
                    if (property == null || !ValueOf(property, out string value, out Object reference))
                        continue;
                    var modification = new PropertyModification { target = owner, propertyPath = binding.propertyName, value = value, objectReference = reference };
                    AnimationMode.AddPropertyModification(binding, modification, true);
                    any = true;
                }
            }
            if (any)
                m_Scenes.Add(go.scene);
        }

        // a path the simulation records for an animator (its slots have no rows) — the scene is written down as a row's, so
        // the session ends when it closes
        internal bool RecordPath(Object owner, string path)
        {
            if (!Add(owner, path))
                return false;
            m_Scenes.Add(owner is Component component ? component.gameObject.scene : ((GameObject)owner).scene);
            return true;
        }

        // false when the property is missing or has no binding (long · double · a string)
        internal static bool Add(Object owner, string path)
        {
            if (!CanRecord(owner, path, out EditorCurveBinding binding, out PropertyModification modification))
                return false;
            // an override stays and none is added
            AnimationMode.AddPropertyModification(binding, modification, true);
            return true;
        }

        // a row's path records — the property is there, its value has a string and it binds. FindProperty alone lets a path
        // through that cannot be recorded (the Euler hint). The rows' test asks the same
        internal static bool CanRecord(Object owner, string path) => CanRecord(owner, path, out _, out _);

        static bool CanRecord(Object owner, string path, out EditorCurveBinding binding, out PropertyModification modification)
        {
            binding = default;
            modification = null;
            GameObject go = owner is Component component ? component.gameObject : owner as GameObject;
            if (go == null)
                return false;
            SerializedProperty property = new SerializedObject(owner).FindProperty(path);
            if (property == null || !ValueOf(property, out string value, out Object reference))
                return false;
            modification = new PropertyModification { target = owner, propertyPath = path, value = value, objectReference = reference };
            // a GameObject's own property does not bind from its root — the conversion gives no type
            if (owner is GameObject)
                binding = EditorCurveBinding.DiscreteCurve("", typeof(GameObject), path);
            else if (AnimationUtility.PropertyModificationToEditorCurveBinding(modification, go, out binding) == null)
                return false;
            return true;
        }

        // once a renderer a session — before its first record
        void TakeBlocks(Object owner)
        {
            if (!(owner is Renderer renderer) || m_Renderers.Contains(renderer))
                return;
            int count = renderer.sharedMaterials.Length;
            var blocks = new MaterialPropertyBlock[count + 1];
            blocks[0] = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(blocks[0]);
            for (int i = 0; i < count; i++)
            {
                blocks[i + 1] = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(blocks[i + 1], i);
            }
            m_Renderers.Add(renderer);
            m_Blocks.Add(blocks);
        }

        // right before the session's stop: the blocks as the layers left them. A renderer first recorded after one of our block
        // writes (a Rebuild's new item) took that write at its first record. A lost lock's stop came before the end
        internal void RetakeBlocks()
        {
            for (int k = 0; k < m_Renderers.Count; k++)
            {
                Renderer renderer = m_Renderers[k];
                if (renderer == null)
                    continue;
                MaterialPropertyBlock[] blocks = m_Blocks[k];
                renderer.GetPropertyBlock(blocks[0]);
                int count = Math.Min(blocks.Length - 1, renderer.sharedMaterials.Length);
                for (int i = 0; i < count; i++)
                    renderer.GetPropertyBlock(blocks[i + 1], i);
            }
        }

        // the end, right after the stop: each block as it was taken — an empty one as none. A renderer gone is skipped
        internal void PutBlocksBack()
        {
            for (int k = 0; k < m_Renderers.Count; k++)
            {
                Renderer renderer = m_Renderers[k];
                if (renderer == null)
                    continue;
                MaterialPropertyBlock[] blocks = m_Blocks[k];
                renderer.SetPropertyBlock(blocks[0].isEmpty ? null : blocks[0]);
                int count = Math.Min(blocks.Length - 1, renderer.sharedMaterials.Length);
                for (int i = 0; i < count; i++)
                    renderer.SetPropertyBlock(blocks[i + 1].isEmpty ? null : blocks[i + 1], i);
            }
            m_Renderers.Clear();
            m_Blocks.Clear();
        }

        // the string the stop parses back. "R" keeps a float's bits but Unity's Mono writes −0 as "0" — given "-0" the
        // engine keeps the sign
        static bool ValueOf(SerializedProperty property, out string value, out Object reference)
        {
            reference = null;
            value = null;
            switch (property.propertyType)
            {
                case SerializedPropertyType.Float:
                    if (property.type == "double")
                        return false;
                    float f = property.floatValue;
                    value = BitConverter.SingleToInt32Bits(f) == NegativeZero ? "-0" : f.ToString("R", CultureInfo.InvariantCulture);
                    return true;
                case SerializedPropertyType.Integer:
                    if (property.type == "long" || property.type == "ulong")
                        return false;
                    value = property.longValue.ToString(CultureInfo.InvariantCulture);
                    return true;
                case SerializedPropertyType.Boolean:
                    value = property.boolValue ? "1" : "0";
                    return true;
                case SerializedPropertyType.Enum:
                    value = property.intValue.ToString(CultureInfo.InvariantCulture);
                    return true;
                case SerializedPropertyType.ObjectReference:
                    value = "";
                    reference = property.objectReferenceValue;
                    return true;
                default:
                    return false;
            }
        }
    }
}
