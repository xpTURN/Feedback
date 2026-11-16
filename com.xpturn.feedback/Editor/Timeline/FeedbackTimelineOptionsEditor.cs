using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

namespace xpTURN.Feedback.Editor
{
    // the options' inspector — the key, the intensity and the Hits rows; + adds a row of intensity 1 (the list's own + gives
    // the first row 0 and copies the last one after)
    [CustomEditor(typeof(FeedbackTimelineOptions))]
    internal sealed class FeedbackTimelineOptionsEditor : UnityEditor.Editor
    {
        internal const string VariantKeyTooltip = "The alias every Feedback clip of this director picks its list by on its track's player, ahead of the clip's Preset — a player with no list of that alias plays the clip as it is. Read when a clip starts, in Play mode";
        internal const string IntensityTooltip = "Every Feedback clip's intensity of this director is multiplied by it";
        internal const string HitsTooltip = "Rows by a clip's Hit Key: the clips with that key play with the row's intensity too, and its Variant Key — when their player has a list of that alias — ahead of the director's";

        internal ListView Hits;

        public override VisualElement CreateInspectorGUI()
        {
            var root = new VisualElement();
            root.Add(new PropertyField(serializedObject.FindProperty("m_VariantKey"), "Variant Key") { tooltip = VariantKeyTooltip });
            root.Add(new PropertyField(serializedObject.FindProperty("m_Intensity"), "Intensity") { tooltip = IntensityTooltip });
            SerializedProperty hits = serializedObject.FindProperty("m_Hits");
            Hits = new ListView
            {
                bindingPath = "m_Hits",
                headerTitle = "Hits",
                showFoldoutHeader = true,
                showAddRemoveFooter = true,
                showBoundCollectionSize = false,
                reorderable = true,
                reorderMode = ListViewReorderMode.Animated,
                virtualizationMethod = CollectionVirtualizationMethod.DynamicHeight,
                tooltip = HitsTooltip,
            };
            Hits.onAdd = _ =>
            {
                serializedObject.Update();
                hits.arraySize++;
                SerializedProperty row = hits.GetArrayElementAtIndex(hits.arraySize - 1);
                row.FindPropertyRelative("m_HitKey").stringValue = "";
                row.FindPropertyRelative("m_Intensity").floatValue = 1f;
                row.FindPropertyRelative("m_VariantKey").stringValue = "";
                serializedObject.ApplyModifiedProperties();
            };
            Hits.BindProperty(hits);
            root.Add(Hits);
            return root;
        }
    }
}
