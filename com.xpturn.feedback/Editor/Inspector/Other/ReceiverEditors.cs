using System;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace xpTURN.Feedback.Editor
{
    // the two receivers — the channel (a number or an asset), the other fields, and in play mode whether the receiver is
    // registered (an inactive one, one before the scheduler, a feedback receiver without a player, is not)
    internal static class ReceiverInspector
    {
        internal const string RegisteredText = "Registered — the channel's sends reach it.";
        internal const string NotRegisteredText = "Not registered — inactive, no scheduler yet, or (a feedback receiver) no player to command.";

        internal static VisualElement Build(UnityEditor.Editor editor, Func<bool> registered)
        {
            var root = new VisualElement();
            root.AddToClassList(InspectorStyles.Root);
            InspectorStyles.Attach(root);
            SerializedObject so = editor.serializedObject;
            SerializedProperty it = so.GetIterator();
            bool enter = true;
            while (it.NextVisible(enter))
            {
                enter = false;
                if (it.name == "m_Script")
                    continue;
                if (it.name == "m_Channel" && !so.isEditingMultipleObjects)
                {
                    root.Add(new ChannelRefField(it.Copy(), it.displayName));
                    continue;
                }
                var field = new PropertyField(it.Copy());
                field.BindProperty(it.Copy());
                root.Add(field);
            }
            if (EditorApplication.isPlayingOrWillChangePlaymode && !so.isEditingMultipleObjects)
            {
                var state = new HelpBox("", HelpBoxMessageType.Info);
                state.AddToClassList(InspectorStyles.Status);
                void Refresh() => state.text = registered() ? RegisteredText : NotRegisteredText;
                Refresh();
                state.schedule.Execute(Refresh).Every(250);
                root.Add(state);
            }
            return root;
        }
    }

    [CustomEditor(typeof(FeedbackReceiver))]
    [CanEditMultipleObjects]
    internal sealed class FeedbackReceiverEditor : UnityEditor.Editor
    {
        public override VisualElement CreateInspectorGUI() => ReceiverInspector.Build(this, () => target != null && ((FeedbackReceiver)target).IsRegistered);
    }

    [CustomEditor(typeof(CameraShakeReceiver))]
    [CanEditMultipleObjects]
    internal sealed class CameraShakeReceiverEditor : UnityEditor.Editor
    {
        public override VisualElement CreateInspectorGUI() => ReceiverInspector.Build(this, () => target != null && ((CameraShakeReceiver)target).IsRegistered);
    }
}
