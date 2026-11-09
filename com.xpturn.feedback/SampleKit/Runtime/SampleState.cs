using UnityEngine;

namespace xpTURN.Feedback.Samples
{
    // the one writer of the categories, the flash limit and the base time scale for the samples: the HUD's on/off, intensity
    // and time scale, the accessibility settings and a sample's own time scale compose here (on = before ∧ After ∧ category on ·
    // scale = before × intensity × category scale · base time scale = before × HUD × sample). The values from before the first
    // write come back when a scene with a marker or the hub starts and at a sample change — never from quitting on (the context
    // is closed then; the next play starts from the settings)
    public static class SampleState
    {
        const int Count = 9;
        public const float MaxIntensity = 2f;
        static bool s_Captured;
        static bool s_Quitting;
        static readonly bool[] s_Enabled0 = new bool[Count];
        static readonly float[] s_Scale0 = new float[Count];
        static int s_Flash0;
        static float s_Base0;
        static readonly bool[] s_CategoryOn = new bool[Count];
        static readonly float[] s_CategoryScale = new float[Count];

        public static bool After { get; private set; } = true;
        public static float Intensity { get; private set; } = 1f;
        public static float TimeScale { get; private set; } = 1f;
        public static float SampleTimeScale { get; private set; } = 1f;
        public static bool Captured => s_Captured;

        // a recompile during play reloads the domain without SubsystemRegistration — the static constructor starts the new one
        static SampleState() => Begin();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Begin()
        {
            s_Quitting = false;
            Application.quitting -= OnQuitting;
            Application.quitting += OnQuitting;
            Reset();
        }

        static void OnQuitting() => s_Quitting = true;

        static void Reset()
        {
            s_Captured = false;
            SampleLayers.Reset();
            After = true;
            Intensity = 1f;
            TimeScale = 1f;
            SampleTimeScale = 1f;
            for (int i = 0; i < Count; i++)
            {
                s_CategoryOn[i] = true;
                s_CategoryScale[i] = 1f;
            }
        }

        // the category as the settings left it (before the samples' first write)
        public static bool BaseEnabled(FeedbackCategory category) => s_Captured ? s_Enabled0[(int)category] : FeedbackCategories.IsEnabled(category);

        public static void SetAfter(bool after)
        {
            After = after;
            Apply();
        }

        public static void SetIntensity(float intensity)
        {
            Intensity = Mathf.Clamp(float.IsNaN(intensity) ? 1f : intensity, 0f, MaxIntensity);
            Apply();
        }

        public static void SetCategory(FeedbackCategory category, bool on, float scale)
        {
            if (category <= FeedbackCategory.None || (int)category >= Count)
                return;
            s_CategoryOn[(int)category] = on;
            s_CategoryScale[(int)category] = float.IsNaN(scale) || float.IsInfinity(scale) ? 1f : Mathf.Max(0f, scale);
            Apply();
        }

        public static void SetFlashLimit(int perSecond)
        {
            if (!Capture())
                return;
            FeedbackCategories.SetFlashLimit(Mathf.Max(0, perSecond));
        }

        // the HUD's time scale
        public static void SetTimeScale(float scale)
        {
            TimeScale = Mathf.Clamp(float.IsNaN(scale) ? 1f : scale, 0f, 1f);
            ApplyTime();
        }

        // a sample's own time scale (S7's pause — 0)
        public static void SetSampleTimeScale(float scale)
        {
            SampleTimeScale = Mathf.Clamp(float.IsNaN(scale) ? 1f : scale, 0f, 100f);
            ApplyTime();
        }

        static void ApplyTime()
        {
            if (Capture())
                FeedbackTimeScale.BaseScale = Mathf.Min(100f, s_Base0 * TimeScale * SampleTimeScale);
        }

        static bool Capture()
        {
            if (!Application.isPlaying || s_Quitting)
                return false;
            if (s_Captured)
                return true;
            for (int i = 1; i < Count; i++)
            {
                s_Enabled0[i] = FeedbackCategories.IsEnabled((FeedbackCategory)i);
                s_Scale0[i] = FeedbackCategories.GetScale((FeedbackCategory)i);
            }
            s_Flash0 = FeedbackCategories.FlashLimit;
            s_Base0 = FeedbackTimeScale.BaseScale;
            s_Captured = true;
            return true;
        }

        static void Apply()
        {
            if (!Capture())
                return;
            for (int i = 1; i < Count; i++)
            {
                var c = (FeedbackCategory)i;
                FeedbackCategories.SetEnabled(c, s_Enabled0[i] && After && s_CategoryOn[i]);
                FeedbackCategories.SetScale(c, s_Scale0[i] * Intensity * s_CategoryScale[i]);
            }
        }

        // the values from before the first write, and the samples' own state back to its start
        public static void Restore()
        {
            if (s_Captured && Application.isPlaying && !s_Quitting)
            {
                for (int i = 1; i < Count; i++)
                {
                    FeedbackCategories.SetEnabled((FeedbackCategory)i, s_Enabled0[i]);
                    FeedbackCategories.SetScale((FeedbackCategory)i, s_Scale0[i]);
                }
                FeedbackCategories.SetFlashLimit(s_Flash0);
                FeedbackTimeScale.BaseScale = s_Base0;
            }
            Reset();
        }
    }
}
