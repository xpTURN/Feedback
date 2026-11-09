using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

namespace xpTURN.Feedback.Editor
{
    // a ChannelRef — a number or a FeedbackChannel asset, the mode written as intValue. The target field's Channel mode and
    // the receivers use it
    internal sealed class ChannelRefField : VisualElement
    {
        internal readonly PopupField<string> Mode;
        internal readonly IntegerField Id = new IntegerField("Id");
        internal readonly ObjectField Asset = new ObjectField("Asset") { objectType = typeof(FeedbackChannel), allowSceneObjects = false };

        internal ChannelRefField(SerializedProperty channel, string label)
        {
            SerializedProperty mode = channel.FindPropertyRelative("m_Mode");
            Mode = new PopupField<string>(label, new List<string>(Enum.GetNames(typeof(ChannelRefMode))), ((ChannelRefMode)mode.intValue).ToString());
            Id.BindProperty(channel.FindPropertyRelative("m_Id"));
            Asset.BindProperty(channel.FindPropertyRelative("m_Asset"));
            Show((ChannelRefMode)mode.intValue);
            Mode.RegisterValueChangedCallback(e =>
            {
                if (!Enum.TryParse(e.newValue, out ChannelRefMode m))
                    return;
                mode.serializedObject.Update();
                mode.intValue = (int)m;
                mode.serializedObject.ApplyModifiedProperties();
                Show(m);
            });
            Add(Mode);
            Add(Id);
            Add(Asset);
        }

        void Show(ChannelRefMode m)
        {
            Id.style.display = m == ChannelRefMode.Int ? DisplayStyle.Flex : DisplayStyle.None;
            Asset.style.display = m == ChannelRefMode.Asset ? DisplayStyle.Flex : DisplayStyle.None;
        }
    }
}
