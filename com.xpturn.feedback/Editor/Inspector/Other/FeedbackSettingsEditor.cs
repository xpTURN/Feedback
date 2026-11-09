using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace xpTURN.Feedback.Editor
{
    // the project's settings — the fields, cut to their ranges when they are edited (the runtime reads a value out of range
    // as the field's default), the receivers' contributions explained (a layer's 'maximum overlap' warning is fixed here), and
    // a note when the asset is not where the runtime reads it
    [CustomEditor(typeof(FeedbackSettings))]
    internal sealed class FeedbackSettingsEditor : UnityEditor.Editor
    {
        internal const string ContributionsText = "C — the shakes one camera shake receiver takes at once. A layer's 'maximum overlap' warning on a receiver is fixed by raising this.";
        internal const string NotLoadedText = "The runtime reads Resources/FeedbackSettings — this asset is not there and is not read.";

        internal HelpBox NotLoaded;

        public override VisualElement CreateInspectorGUI()
        {
            var root = new VisualElement();
            root.AddToClassList(InspectorStyles.Root);
            InspectorStyles.Attach(root);
            NotLoaded = new HelpBox(NotLoadedText, HelpBoxMessageType.Warning);
            NotLoaded.style.display = targets.Length == 1 && !AssetMenus.IsRuntimeSettings(AssetDatabase.GetAssetPath(target)) ? DisplayStyle.Flex : DisplayStyle.None;
            root.Add(NotLoaded);
            SerializedProperty it = serializedObject.GetIterator();
            bool enter = true;
            while (it.NextVisible(enter))
            {
                enter = false;
                if (it.name == "m_Script")
                    continue;
                var field = new PropertyField(it.Copy());
                field.BindProperty(it.Copy());
                root.Add(field);
                if (it.name == "m_ReceiverContributions")
                {
                    var hint = new Label(ContributionsText);
                    hint.AddToClassList(InspectorStyles.Hint);
                    root.Add(hint);
                }
            }
            root.TrackSerializedObjectValue(serializedObject, so => Clamp(so));
            return root;
        }

        // each field to its range — true when one was cut
        internal static bool Clamp(SerializedObject so)
        {
            so.Update();
            bool cut = false;
            cut |= AtLeast(so.FindProperty("m_FlashLimit"), 0);
            cut |= AtLeast(so.FindProperty("m_TimeScaleCapacity"), 1);
            cut |= AtLeast(so.FindProperty("m_ShakeReceiversPerPlay"), 1);
            cut |= AtLeast(so.FindProperty("m_ReceiverContributions"), 1);
            SerializedProperty dt = so.FindProperty("m_MaximumUnscaledDelta");
            if (dt != null && !(dt.floatValue >= 0f && dt.floatValue < float.PositiveInfinity))
            {
                dt.floatValue = 0f;
                cut = true;
            }
            SerializedProperty categories = so.FindProperty("m_Categories");
            for (int i = 0; categories != null && i < categories.arraySize; i++)
            {
                SerializedProperty scale = categories.GetArrayElementAtIndex(i).FindPropertyRelative("m_Scale");
                if (!(scale.floatValue >= 0f && scale.floatValue < float.PositiveInfinity))
                {
                    scale.floatValue = scale.floatValue < 0f ? 0f : 1f;
                    cut = true;
                }
            }
            if (cut)
                so.ApplyModifiedProperties();
            return cut;
        }

        static bool AtLeast(SerializedProperty p, int min)
        {
            if (p == null || p.intValue >= min)
                return false;
            p.intValue = min;
            return true;
        }
    }
}
