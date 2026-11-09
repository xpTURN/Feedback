using System.Collections.Generic;
using UnityEngine;

namespace xpTURN.Feedback
{
    // an item list and its format version — copied into a player, or linked by players (they read this list — the runtime
    // reads the saved data, it does not write it). The player's settings are the player's; a Unity Preset holds the whole
    // component instead
    /// <summary>An item list that players copy or link; a linked player plays the preset's list and keeps its own settings.</summary>
    public sealed class FeedbackPreset : ScriptableObject, ISerializationCallbackReceiver
    {
        internal const string NewerFormatWarning = "A FeedbackPreset was saved by a newer version of the package — fields and item types this version does not know are lost: ";

        [SerializeReference] internal List<FeedbackItem> m_Items = new List<FeedbackItem>();
        [SerializeField] internal int m_FormatVersion = FeedbackFormat.Current;
#if DEBUG
        // once a preset, whatever players link it
        [System.NonSerialized] internal bool m_WarnedFormat;
#endif

        // ---- the format version: data only, no Unity API — any thread

        void ISerializationCallbackReceiver.OnBeforeSerialize()
        {
        }

        void ISerializationCallbackReceiver.OnAfterDeserialize() => FeedbackFormat.Upgrade(ref m_FormatVersion, m_Items);
    }
}
