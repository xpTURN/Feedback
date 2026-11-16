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
        // the text a Variant key picks this list by, on every player that links it or holds it in its Variants
        [SerializeField] internal string m_Alias = "";
#if DEBUG
        // once a preset, whatever players link it
        [System.NonSerialized] internal bool m_WarnedFormat;
#endif

        /// <summary>The alias a Variant key picks this preset's list by — FeedbackPlayer.VariantKey, PlayArgs.WithVariant, a Timeline clip's options; empty matches no key.</summary>
        public string Alias => m_Alias ?? "";

        // ---- the format version: data only, no Unity API — any thread

        void ISerializationCallbackReceiver.OnBeforeSerialize()
        {
        }

        void ISerializationCallbackReceiver.OnAfterDeserialize() => FeedbackFormat.Upgrade(ref m_FormatVersion, m_Items);
    }
}
