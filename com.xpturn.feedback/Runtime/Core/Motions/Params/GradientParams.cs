using System;
using UnityEngine;

namespace xpTURN.Feedback
{
    // saved — the values are the saved format, add members at the end only
    public enum GradientWrite
    {
        Override = 0,
        Persist = 1,
    }

    // Gradient's saved parameters
    // Default makes a new gradient each call — a shared static instance would share it
    [Serializable]
    internal struct GradientParams
    {
        [SerializeField] internal Gradient m_Gradient;
        [SerializeField] internal GradientWrite m_Mode;

        internal static GradientParams Default => new GradientParams { m_Gradient = new Gradient() };
    }
}
