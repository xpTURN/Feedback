using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace xpTURN.Feedback.Editor
{
    // what a slot type keeps besides its record rows — made at the recorder's call, right before the slot's first write,
    // while a session lives. The editor names the types; the runtime has no hook (play and the players have no recorder)
    internal static class PreviewSlotStates
    {
        static readonly Dictionary<Type, Action<LayerKey>> s_Makers = new Dictionary<Type, Action<LayerKey>>
        {
            { typeof(ParentSlot), MakeParent },
            // the active scene's RenderSettings — the skybox's reference by its copies' slot
            { typeof(FogColorSlot), k => PreviewRenderSettings.Add(RenderSettingsKind.FogColor) },
            { typeof(FogDensitySlot), k => PreviewRenderSettings.Add(RenderSettingsKind.FogDensity) },
            { typeof(AmbientColorSlot), k => PreviewRenderSettings.Add(RenderSettingsKind.AmbientColor) },
            { typeof(AmbientIntensitySlot), k => PreviewRenderSettings.Add(RenderSettingsKind.AmbientIntensity) },
            { typeof(SkyboxCopySlot), k => PreviewRenderSettings.Add(RenderSettingsKind.Skybox) },
            // an end's line color (not recordable) · a renderer's first sprite (the hidden mark)
            { typeof(LineColorSlot), MakeLineColor },
            { typeof(SpriteSlot), MakeAssigned },
            { typeof(SpriteSheetSlot), MakeAssigned },
            // a user's value — the getter's base before the first write
            { typeof(CustomFloatSlot), MakeCustom<float> },
            { typeof(CustomVector3Slot), MakeCustom<Vector3> },
            { typeof(CustomColorSlot), MakeCustom<Color> },
        };

        static void MakeCustom<T>(LayerKey key)
        {
            if (!(key.Target is Component owner) || !(owner is IFeedbackTarget<T> target))
                return;
            for (int i = 0; i < PreviewSaveState.Count; i++)
            {
                if (PreviewSaveState.At(i) is CustomValueState<T> s && s.Owner == owner)
                    return;
            }
            PreviewSaveState.Add(new CustomValueState<T>(owner, target));
        }

        internal static void OnRecord(in LayerKey key)
        {
            // a simulated target by the key's target type — the editor names the module type, not an optional assembly's slot
            // type
            PreviewSession.Current?.Simulation.OnRecord(in key);
            if (PreviewSession.Current != null && s_Makers.TryGetValue(key.SlotType, out Action<LayerKey> make))
                make(key);
            // an optional assembly's slot by its marker
            else if (PreviewSession.Current != null && PreviewKeeps.Of(key.SlotType, out PreviewKeepsAttribute mark))
                PreviewKeeps.Make(in key, mark);
        }

        static void MakeLineColor(LayerKey key)
        {
            if (!(key.Target is LineRenderer target))
                return;
            for (int i = 0; i < PreviewSaveState.Count; i++)
            {
                if (PreviewSaveState.At(i) is LineColorState s && s.Target == target && (int)s.End == key.Aux2)
                    return;
            }
            PreviewSaveState.Add(new LineColorState(target, (LineEnd)key.Aux2));
        }

        // only a renderer that never had a sprite (a sprite set clears nothing a later stop would leave)
        static void MakeAssigned(LayerKey key)
        {
            if (!(key.Target is SpriteRenderer target) || SpriteAssignedState.Assigned(target))
                return;
            for (int i = 0; i < PreviewSaveState.Count; i++)
            {
                if (PreviewSaveState.At(i) is SpriteAssignedState s && s.Target == target)
                    return;
            }
            PreviewSaveState.Add(new SpriteAssignedState(target));
        }

        // the session's interception — an edit of a gradient whose end colors the preview keeps. An end whose color is not
        // the one its slot left is the user's: its state leaves without its revert (nothing at the end writes it again). true
        // when one was taken — the session ends
        internal static bool TakeUserEdit(UnityEngine.Object target, string path)
        {
            if (!(target is LineRenderer renderer) || !path.StartsWith("m_Parameters.colorGradient", StringComparison.Ordinal))
                return false;
            bool taken = false;
            for (int k = PreviewSaveState.Count - 1; k >= 0; k--)
            {
                if (!(PreviewSaveState.At(k) is LineColorState state) || state.Target != renderer || !state.UserChanged())
                    continue;
                PreviewSaveState.Remove(state);
                taken = true;
            }
            return taken;
        }

        static void MakeParent(LayerKey key)
        {
            if (key.Target is Transform target)
                PreviewSaveState.Add(new TransformParentState(target));
        }

        // PreviewHooks: a user's reparent (Undo) comes as an event the next frame — the preview's own writes have none, and
        // the session's interception does not see it. The user's place stays: its state leaves without its revert, the
        // session ends, and the place goes back on over the stop's recorded local values
        internal static void Changes(ref ObjectChangeEventStream stream)
        {
            for (int i = 0; i < stream.length; i++)
            {
                if (stream.GetEventType(i) != ObjectChangeKind.ChangeGameObjectParent)
                    continue;
                stream.GetChangeGameObjectParentEvent(i, out ChangeGameObjectParentEventArgs e);
                for (int k = PreviewSaveState.Count - 1; k >= 0; k--)
                {
                    if (!(PreviewSaveState.At(k) is TransformParentState state) || state.Target == null || !Same(in e, state.Target.gameObject))
                        continue;
                    Transform target = state.Target;
                    ParentState user = ParentState.Of(target);
                    PreviewSaveState.Remove(state);
                    PreviewSession.Current?.End(PreviewEndReason.UserEdit);
                    if (PreviewSession.Current == null && target != null)
                        user.Put(target);
                    return;
                }
            }
        }

        static bool Same(in ChangeGameObjectParentEventArgs e, GameObject go)
        {
#if UNITY_6000_6_OR_NEWER
            return e.entityId == go.GetEntityId();
#elif UNITY_6000_3_OR_NEWER
            return EditorUtility.EntityIdToObject(e.instanceId) == go;
#else
            return EditorUtility.InstanceIDToObject(e.instanceId) == go;
#endif
        }
    }

    // a user's value written with no mark — a save writes the base and the preview's value after, the end the base: a
    // Tween's · Set's value is put back only here (the slot's close reverts what its composition wrote). A direct write's
    // slot and the shared one keep one state
    internal sealed class CustomValueState<T> : IPreviewSaveState
    {
        readonly Component m_Owner;
        readonly IFeedbackTarget<T> m_Target;
        readonly T m_Before;
        readonly Scene m_Scene;
        T m_Kept;

        internal CustomValueState(Component owner, IFeedbackTarget<T> target)
        {
            m_Owner = owner;
            m_Target = target;
            m_Before = target.FeedbackValue;
            m_Scene = owner.gameObject.scene;
        }

        internal Component Owner => m_Owner;

        internal T Before => m_Before;

        public Scene Scene => m_Scene;

        public void Revert()
        {
            if (m_Owner == null)
                return;
            m_Kept = m_Target.FeedbackValue;
            m_Target.FeedbackValue = m_Before;
        }

        public void Reapply()
        {
            if (m_Owner != null)
                m_Target.FeedbackValue = m_Kept;
        }
    }

    // the parent's save state: taken right before the slot's first write — the place in its scene. A save of the object's
    // first scene, of the scene it is in now or of the one it was in at the last save's revert goes back to the first place
    // and on again after
    internal sealed class TransformParentState : IPreviewSaveState, IPreviewSaveStateScenes
    {
        readonly Transform m_Target;
        readonly ParentState m_Before;
        ParentState m_Kept;
        Scene m_KeptScene;

        internal TransformParentState(Transform target)
        {
            m_Target = target;
            m_Before = ParentState.Of(target);
        }

        internal Transform Target => m_Target;

        public Scene Scene => m_Before.Scene;

        public bool Covers(Scene scene) => scene == m_Before.Scene || (m_Target != null && m_Target.gameObject.scene == scene) || scene == m_KeptScene;

        public void Revert()
        {
            if (m_Target == null)
                return;
            m_Kept = ParentState.Of(m_Target);
            m_KeptScene = m_Target.gameObject.scene;
            m_Before.Put(m_Target);
        }

        public void Reapply()
        {
            if (m_Target != null)
                m_Kept.Put(m_Target);
        }
    }
}
