using System;
using TMPro;
using UnityEngine;

namespace xpTURN.Feedback.Samples
{
    // TMP's Essential Resources by Resources.Load (the instance getter opens the import window in the editor) — read once a
    // session; a sample builds its TMP parts only when they are there and shows the notice otherwise
    public static class SampleTmp
    {
        public const string Notice = "TextMesh Pro's Essential Resources are not in the project — Window > TextMeshPro > Import TMP Essential Resources";

        internal static Func<bool> s_Probe = () => Resources.Load<TMP_Settings>("TMP Settings") != null;
        static bool s_Read;
        static bool s_Ready;

        // a recompile during play runs no SubsystemRegistration — the static constructor starts it over too
        static SampleTmp() => s_Read = false;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        internal static void Reset() => s_Read = false;

        public static bool Ready
        {
            get
            {
                if (!s_Read)
                {
                    s_Ready = s_Probe();
                    s_Read = true;
                }
                return s_Ready;
            }
        }
    }
}
