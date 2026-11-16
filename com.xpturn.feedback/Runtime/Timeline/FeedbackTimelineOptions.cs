using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Playables;

namespace xpTURN.Feedback
{
    // what the Feedback clips of the director on this GameObject play with — a clip reads it when it starts its play, in
    // Play mode: the Variant key ahead of the clip's Preset, the intensity, and the row of the clip's Hit Key. Edit mode's
    // preview plays what the clips hold. A Control track's inner timeline reads its own director's
    /// <summary>Options for the Feedback clips of the PlayableDirector on this GameObject: a Variant key every clip picks its list by, an intensity every clip's is multiplied by, and rows by a clip's Hit Key — read when a clip starts its play, in Play mode.</summary>
    [AddComponentMenu("xpTURN/Feedback/Feedback Timeline Options")]
    [RequireComponent(typeof(PlayableDirector))]
    [DisallowMultipleComponent]
    public sealed class FeedbackTimelineOptions : MonoBehaviour
    {
        internal const string ValueWarning = "FeedbackTimelineOptions was given an empty Hit Key or a negative, NaN or infinite intensity and ignored it.";

        [SerializeField] internal string m_VariantKey = "";
        [SerializeField, Min(0f)] internal float m_Intensity = 1f;
        [SerializeField] internal List<FeedbackTimelineHit> m_Hits = new List<FeedbackTimelineHit>();
#if DEBUG
        [NonSerialized] bool m_Warned;
#endif

        /// <summary>The alias every clip picks its list by on its track's player, ahead of the clip's Preset; a player with no list of that alias plays the clip as it is. Empty sets none.</summary>
        public string VariantKey
        {
            get => m_VariantKey ?? "";
            set => m_VariantKey = value ?? "";
        }

        /// <summary>The number every clip's intensity is multiplied by; a negative, NaN or infinite value is ignored with a warning.</summary>
        public float Intensity
        {
            get => m_Intensity;
            set
            {
                if (Valid(value))
                    m_Intensity = value;
                else
                    Warn();
            }
        }

        /// <summary>Sets what the clips whose Hit Key is <paramref name="hitKey"/> play with: <paramref name="intensity"/> multiplies their intensity and <paramref name="variantKey"/>, when not empty, picks their list ahead of the VariantKey; a key the table lacks is added, which may allocate once.</summary>
        public void SetHit(string hitKey, float intensity = 1f, string variantKey = null)
        {
            if (string.IsNullOrEmpty(hitKey) || !Valid(intensity))
            {
                Warn();
                return;
            }
            var row = new FeedbackTimelineHit { m_HitKey = hitKey, m_Intensity = intensity, m_VariantKey = variantKey ?? "" };
            int at = IndexOf(hitKey);
            if (at >= 0)
                m_Hits[at] = row;
            else
                m_Hits.Add(row);
        }

        /// <summary>Takes the row of <paramref name="hitKey"/> away; false when the table has none.</summary>
        public bool RemoveHit(string hitKey)
        {
            int at = IndexOf(hitKey);
            if (at < 0)
                return false;
            m_Hits.RemoveAt(at);
            return true;
        }

        /// <summary>Takes every row away.</summary>
        public void ClearHits() => m_Hits.Clear();

        /// <summary>True when the table has a row for <paramref name="hitKey"/> — the first one; <paramref name="intensity"/> and <paramref name="variantKey"/> are its values.</summary>
        public bool TryGetHit(string hitKey, out float intensity, out string variantKey)
        {
            int at = IndexOf(hitKey);
            intensity = at >= 0 ? m_Hits[at].m_Intensity : 1f;
            variantKey = at >= 0 ? m_Hits[at].m_VariantKey ?? "" : "";
            return at >= 0;
        }

        // the first row of the key — the one a clip reads
        int IndexOf(string hitKey)
        {
            if (string.IsNullOrEmpty(hitKey) || m_Hits == null)
                return -1;
            for (int i = 0; i < m_Hits.Count; i++)
            {
                if (string.Equals(m_Hits[i].m_HitKey, hitKey, StringComparison.Ordinal))
                    return i;
            }
            return -1;
        }

        static bool Valid(float value) => value >= 0f && value < float.PositiveInfinity;

        void Warn()
        {
#if DEBUG
            if (m_Warned)
                return;
            m_Warned = true;
            Debug.LogWarning(ValueWarning, this);
#endif
        }
    }

    // one row of the options' table — the clips with that Hit Key
    [Serializable]
    internal struct FeedbackTimelineHit
    {
        [SerializeField] internal string m_HitKey;
        [SerializeField, Min(0f)] internal float m_Intensity;
        [SerializeField] internal string m_VariantKey;
    }
}
