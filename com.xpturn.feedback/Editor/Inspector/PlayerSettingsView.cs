using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

namespace xpTURN.Feedback.Editor
{
    // the player's settings — the basic ones, the rest under a fold (its state in SessionState). Bound to the editor's
    // SerializedObject, so several players are edited together. The UnityEvents are a row per kind (one player); the remap
    // curve says what it does outside its keys
    internal sealed class PlayerSettingsView : VisualElement
    {
        internal const string AdvancedKey = "xpTURN.Feedback.Inspector.Advanced";
        internal const string RemapHintText = "Outside its keys the curve keeps the end value — to use an argument above 1, move the last key.";

        static readonly string[] s_Basic = { "m_Initialization", "m_AutoPlay", "m_Policy", "m_Direction", "m_Intensity", "m_TimeMode" };
        static readonly string[] s_Advanced =
        {
            "m_MaxOverlap", "m_DurationMultiplier", "m_StartDelay", "m_Speed", "m_RandomizeDuration", "m_DurationRange", "m_Cooldown",
            "m_Chance", "m_RemapIntensity", "m_IntensityRemap", "m_UseDistanceGate", "m_GateReference", "m_GateAnchor", "m_GateDistance",
            "m_UseGateFalloff", "m_GateFalloff", "m_ApplyInitialState", "m_DisableBehavior", "m_UnityEvents", "m_Anchors",
        };

        internal readonly Foldout Advanced;
        internal Label RemapHint;
        internal readonly List<string> Bound = new List<string>();

        internal PlayerSettingsView(SerializedObject so)
        {
            AddToClassList(InspectorStyles.Settings);
            foreach (string name in s_Basic)
                AddField(this, so, name);
            Advanced = new Foldout { text = "Advanced", value = SessionState.GetBool(AdvancedKey, false) };
            Advanced.RegisterValueChangedCallback(e =>
            {
                if (e.target == Advanced)
                    SessionState.SetBool(AdvancedKey, e.newValue);
            });
            foreach (string name in s_Advanced)
                AddField(Advanced, so, name);
            Add(Advanced);
        }

        void AddField(VisualElement parent, SerializedObject so, string name)
        {
            SerializedProperty p = so.FindProperty(name);
            if (p == null)
                return;
            Bound.Add(name);
            if (name == "m_UnityEvents" && !so.isEditingMultipleObjects)
            {
                parent.Add(new UnityEventListField(p));
                return;
            }
            var field = new PropertyField(p);
            field.BindProperty(p);
            parent.Add(field);
            if (name == "m_IntensityRemap")
            {
                RemapHint = new Label(RemapHintText);
                RemapHint.AddToClassList(InspectorStyles.Hint);
                parent.Add(RemapHint);
            }
        }
    }
}
