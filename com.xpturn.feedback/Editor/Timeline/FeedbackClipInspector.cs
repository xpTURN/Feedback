using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine.Timeline;
using UnityEngine.UIElements;

namespace xpTURN.Feedback.Editor
{
    // a Feedback clip's Inspector in the Timeline window — the default fields, and a warning when the clip's Preset is not one
    // of the bound player's lists, with the fix: the preset added to the player's Variants (an Undo step on the player)
    [CustomEditor(typeof(FeedbackClip))]
    [CanEditMultipleObjects]
    internal sealed class FeedbackClipInspector : UnityEditor.Editor
    {
        internal const string AddText = "Add to Variants";

        internal HelpBox Notice;

        public override VisualElement CreateInspectorGUI()
        {
            var root = new VisualElement();
            InspectorElement.FillDefaultInspector(root, serializedObject, this);
            Notice = new HelpBox(TimelinePlayerInfo.NotVariantText, HelpBoxMessageType.Warning);
            Notice.Add(new Button(Add) { text = AddText });
            root.Add(Notice);
            Show();
            root.TrackSerializedObjectValue(serializedObject, _ => Show());
            return root;
        }

        // the player the clip's track is bound to in the Timeline window, and the clip's preset it does not list
        bool Unlisted(out FeedbackPlayer player, out FeedbackPreset preset)
        {
            player = null;
            preset = null;
            if (targets.Length != 1 || !(target is FeedbackClip asset) || asset.m_Preset == null)
                return false;
            TimelineClip clip = TimelinePlayerInfo.ClipOf(asset);
            player = clip != null ? TimelinePlayerInfo.Bound(clip.GetParentTrack()) : null;
            preset = asset.m_Preset;
            return player != null && !TimelinePlayerInfo.Lists(player, preset);
        }

        void Show()
        {
            if (Notice != null)
                Notice.style.display = Unlisted(out _, out _) ? DisplayStyle.Flex : DisplayStyle.None;
        }

        internal void Add()
        {
            if (!Unlisted(out FeedbackPlayer player, out FeedbackPreset preset))
                return;
            var so = new SerializedObject(player);
            SerializedProperty variants = so.FindProperty("m_Variants");
            variants.InsertArrayElementAtIndex(variants.arraySize);
            variants.GetArrayElementAtIndex(variants.arraySize - 1).objectReferenceValue = preset;
            so.ApplyModifiedProperties();
            Show();
        }
    }
}
