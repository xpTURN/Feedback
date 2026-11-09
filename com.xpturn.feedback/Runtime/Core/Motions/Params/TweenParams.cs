using System;
using UnityEngine;

namespace xpTURN.Feedback
{
    // saved — the values are the saved format, add members at the end only
    public enum TweenMode
    {
        To = 0,
        From = 1,
        FromTo = 2,
        By = 3,
    }

    // Tween's saved parameters
    [Serializable]
    internal struct TweenParams
    {
        [SerializeField] internal TweenMode m_Mode;
        [SerializeField] internal Vector4 m_From;
        [SerializeField] internal Vector4 m_To;
        [SerializeField] internal Ease m_Ease;
        [SerializeField, Min(1)] internal int m_Loops;
        [SerializeField] internal bool m_Yoyo;

        internal static TweenParams Default => new TweenParams { m_To = Vector4.one, m_Loops = 1 };
    }
}
