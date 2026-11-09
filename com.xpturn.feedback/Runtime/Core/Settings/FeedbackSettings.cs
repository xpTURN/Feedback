using System;
using UnityEngine;

namespace xpTURN.Feedback
{
    // the categories' state is the context's copy. The window and the asset menu are the editor's
    /// <summary>The project's start values, read from Resources/FeedbackSettings — the defaults without it; the runtime never writes it.</summary>
    public sealed class FeedbackSettings : ScriptableObject
    {
        internal const string ResourcePath = "FeedbackSettings";

        [SerializeField] internal CategoryDefault[] m_Categories = new CategoryDefault[0];
        [SerializeField] internal int m_FlashLimit = 3;
        [SerializeField] internal int m_TimeScaleCapacity = 8;
        [SerializeField] internal bool m_AdjustFixedDeltaTime;
        [SerializeField] internal int m_ShakeReceiversPerPlay = 4;
        [SerializeField] internal int m_ReceiverContributions = 8;
        // 0 (or below, or NaN) follows the engine's Time.maximumDeltaTime
        [SerializeField] internal float m_MaximumUnscaledDelta;
        // the screen overlay canvas's sorting order — a 16-bit value
        [SerializeField] internal int m_OverlaySortingOrder = 32000;
        // the PostFX overlay volumes' layer (the cameras' volume masks take it) and their base priority — each rented volume
        // is above it by its preparation's order
        [SerializeField] internal int m_PostFXLayer;
        [SerializeField] internal float m_PostFXPriority = 1000f;
    }

    // one category's start state — a category the list leaves out starts on at scale 1
    [Serializable]
    internal struct CategoryDefault
    {
        [SerializeField] internal FeedbackCategory m_Category;
        [SerializeField] internal bool m_Enabled;
        [SerializeField] internal float m_Scale;
    }

    // the values a context took — from the asset or the defaults (a test sets its own)
    internal sealed class SettingsValues
    {
        internal const int CategoryCount = 9;

        internal readonly bool[] Enabled = new bool[CategoryCount];
        internal readonly float[] Scale = new float[CategoryCount];
        internal int FlashLimit = 3;
        internal int TimeScaleCapacity = 8;
        internal bool AdjustFixedDeltaTime;
        internal int ShakeReceiversPerPlay = 4;
        internal int ReceiverContributions = 8;
        internal float MaximumUnscaledDelta;
        internal int OverlaySortingOrder = 32000;
        internal int PostFXLayer;
        internal float PostFXPriority = 1000f;

        internal SettingsValues()
        {
            for (int i = 0; i < CategoryCount; i++)
            {
                Enabled[i] = true;
                Scale[i] = 1f;
            }
        }

        // a value out of its field's range reads as the field's default. An undefined category and None are left out;
        // a category listed twice takes the later row
        internal static SettingsValues From(FeedbackSettings asset)
        {
            var values = new SettingsValues();
            if (asset == null)
                return values;
            if (asset.m_Categories != null)
            {
                foreach (CategoryDefault c in asset.m_Categories)
                {
                    int i = (int)c.m_Category;
                    if (i <= 0 || i >= CategoryCount)
                        continue;
                    values.Enabled[i] = c.m_Enabled;
                    values.Scale[i] = c.m_Scale >= 0f && c.m_Scale < float.PositiveInfinity ? c.m_Scale : 1f;
                }
            }
            values.FlashLimit = asset.m_FlashLimit >= 0 ? asset.m_FlashLimit : 3;
            values.TimeScaleCapacity = asset.m_TimeScaleCapacity >= 1 ? asset.m_TimeScaleCapacity : 8;
            values.ShakeReceiversPerPlay = asset.m_ShakeReceiversPerPlay >= 1 ? asset.m_ShakeReceiversPerPlay : 4;
            values.ReceiverContributions = asset.m_ReceiverContributions >= 1 ? asset.m_ReceiverContributions : 8;
            values.MaximumUnscaledDelta = asset.m_MaximumUnscaledDelta > 0f && asset.m_MaximumUnscaledDelta < float.PositiveInfinity ? asset.m_MaximumUnscaledDelta : 0f;
            values.AdjustFixedDeltaTime = asset.m_AdjustFixedDeltaTime;
            // a Canvas wraps a value past 16 bits without a word (40000 → −25536)
            values.OverlaySortingOrder = asset.m_OverlaySortingOrder >= short.MinValue && asset.m_OverlaySortingOrder <= short.MaxValue ? asset.m_OverlaySortingOrder : 32000;
            values.PostFXLayer = asset.m_PostFXLayer >= 0 && asset.m_PostFXLayer <= 31 ? asset.m_PostFXLayer : 0;
            values.PostFXPriority = asset.m_PostFXPriority > float.NegativeInfinity && asset.m_PostFXPriority < float.PositiveInfinity ? asset.m_PostFXPriority : 1000f;
            return values;
        }

        // a runtime context reads the asset (each session anew); a preview's or a test's starts from the defaults
        internal static SettingsValues Load(ContextKind kind)
            => kind == ContextKind.Runtime ? From(Resources.Load<FeedbackSettings>(FeedbackSettings.ResourcePath)) : From(null);
    }
}
