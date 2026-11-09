using System.Collections.Generic;
using UnityEngine;

namespace xpTURN.Feedback.Tests
{
    // FeedbackPlayer's stand-in — the list field, the format version and the hook call.
    // A component from an editor-only assembly cannot be added to a GameObject, so it lives in the runtime test assembly
    public sealed class DataModelTestContainer : MonoBehaviour, ISerializationCallbackReceiver
    {
        // what OnAfterDeserialize saw before the hook — static, so a reload that replaces the object keeps it
        public static readonly List<(int Version, int Count, string FirstLabel)> Seen = new List<(int Version, int Count, string FirstLabel)>();

        [SerializeReference] public List<FeedbackItem> m_Items = new List<FeedbackItem>();
        public int m_FormatVersion = FeedbackFormat.Current;

        public void OnBeforeSerialize()
        {
        }

        public void OnAfterDeserialize()
        {
            lock (Seen)
                Seen.Add((m_FormatVersion, m_Items.Count, m_Items.Count > 0 && m_Items[0] != null ? m_Items[0].m_Label : null));
            FeedbackFormat.Upgrade(ref m_FormatVersion, m_Items);
        }

        // a persistent listener target
        public void OnTestEvent()
        {
        }
    }
}
