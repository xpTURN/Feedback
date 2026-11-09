using System.Collections.Generic;
using UnityEngine;

namespace xpTURN.Feedback.Tests.Editor
{
    // FeedbackPreset's stand-in for the format upgrade on an asset
    public sealed class DataModelFormatHolder : ScriptableObject, ISerializationCallbackReceiver
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
    }
}
