using System;
using System.Collections.Generic;
using UnityEngine;

namespace xpTURN.Feedback.Tests.Extension
{
    // what the probes' calls left — the tests read and clear it
    public static class ProbeLog
    {
        public static readonly List<string> Calls = new List<string>();
        public static readonly List<float> Values = new List<float>();
        public static readonly List<int> Slots = new List<int>();
        public static readonly List<double> Seconds = new List<double>();
        public static bool Throw;
        public static bool Refuse;

        public static void Clear()
        {
            Calls.Clear();
            Values.Clear();
            Slots.Clear();
            Seconds.Clear();
            Throw = false;
            Refuse = false;
        }

        internal static string B(bool b) => b ? "1" : "0";
    }

    [CustomFeedback("Probe/One Shot", Aliases = "probe sound")]
    [Serializable]
    public sealed class ProbeOneShot : CustomActionItem
    {
        [SerializeField] public float m_Volume = 1f;

        protected override CustomAction CreateAction() => new Action();

        sealed class Action : CustomAction
        {
            protected override void Fire(in CustomPlay play)
            {
                ProbeLog.Calls.Add("Fire s" + play.Slot + " i" + play.Intensity.ToString("0.##") + " c" + play.CategoryScale.ToString("0.##") + " r" + ProbeLog.B(play.Reverse) + " w" + ProbeLog.B(play.Swept));
                if (ProbeLog.Throw)
                    throw new InvalidOperationException("probe fire");
            }

            protected override void Undo(in CustomPlay play) => ProbeLog.Calls.Add("Undo s" + play.Slot + " r" + ProbeLog.B(play.Reverse) + " w" + ProbeLog.B(play.Swept));

            protected override void End(int slot, CustomEnd end) => ProbeLog.Calls.Add("End s" + slot + " " + end);
        }
    }

    [CustomFeedback("Probe/Haptic")]
    [Serializable]
    public sealed class ProbeHaptic : CustomActionItem
    {
        public override FeedbackCategory Category => FeedbackCategory.Haptics;

        protected override CustomAction CreateAction() => new Action();

        sealed class Action : CustomAction
        {
            protected override void Fire(in CustomPlay play) => ProbeLog.Calls.Add("Haptic c" + play.CategoryScale.ToString("0.##"));
        }
    }

    // a declared Flash reads as None
    [CustomFeedback("Probe/Flash")]
    [Serializable]
    public sealed class ProbeFlash : CustomActionItem
    {
        public override FeedbackCategory Category => FeedbackCategory.Flash;

        protected override CustomAction CreateAction() => new CustomActionNone();
    }

    [CustomFeedback("Probe/Timed")]
    [Serializable]
    public sealed class ProbeTimed : CustomActionItem
    {
        [SerializeField] public float m_Length = 0.5f;

        public override float Duration => m_Length;

        protected override CustomAction CreateAction() => new Action();

        sealed class Action : CustomAction
        {
            protected override bool Prepare(in CustomPrepare context)
            {
                ProbeLog.Calls.Add("Prepare n" + context.Plays + " p" + ProbeLog.B(context.InPreview));
                if (ProbeLog.Throw && ProbeLog.Refuse)
                    throw new InvalidOperationException("probe prepare");
                return !ProbeLog.Refuse;
            }

            protected override void Begin(in CustomPlay play)
            {
                ProbeLog.Calls.Add("Begin s" + play.Slot + " r" + ProbeLog.B(play.Reverse));
                if (ProbeLog.Throw)
                    throw new InvalidOperationException("probe begin");
            }

            protected override void Update(in CustomTick tick)
            {
                ProbeLog.Values.Add(tick.T);
                ProbeLog.Slots.Add(tick.Slot);
                ProbeLog.Seconds.Add(tick.Elapsed);
            }

            protected override void End(int slot, CustomEnd end) => ProbeLog.Calls.Add("End s" + slot + " " + end);

            protected override void Restore() => ProbeLog.Calls.Add("Restore");

            protected override void Release() => ProbeLog.Calls.Add("Release");
        }
    }

    [CustomFeedback("Probe/Haptic Timed")]
    [Serializable]
    public sealed class ProbeHapticTimed : CustomActionItem
    {
        public override float Duration => 1f;

        public override FeedbackCategory Category => FeedbackCategory.Haptics;

        protected override CustomAction CreateAction() => new Action();

        sealed class Action : CustomAction
        {
            protected override void Update(in CustomTick tick) => ProbeLog.Values.Add(tick.CategoryScale);
        }
    }

    [CustomFeedback("Probe/Run In Preview", Preview = CustomPreview.Run)]
    [Serializable]
    public sealed class ProbeRun : CustomActionItem
    {
        public override float Duration => 1f;

        protected override CustomAction CreateAction() => new Action();

        sealed class Action : CustomAction
        {
            protected override bool Prepare(in CustomPrepare context)
            {
                ProbeLog.Calls.Add("Run prepare p" + ProbeLog.B(context.InPreview));
                return true;
            }

            protected override void Begin(in CustomPlay play) => ProbeLog.Calls.Add("Run begin");
        }
    }

    [CustomFeedback("Probe/Value", Global = true)]
    [Serializable]
    public sealed class ProbeValue : CustomEffectItem
    {
        protected override CustomEffect CreateEffect() => new Effect();

        sealed class Effect : CustomEffect
        {
            protected override void Apply(int slot, float value)
            {
                ProbeLog.Values.Add(value);
                ProbeLog.Slots.Add(slot);
                if (ProbeLog.Throw)
                    throw new InvalidOperationException("probe apply");
            }

            protected override void End(int slot, CustomEnd end) => ProbeLog.Calls.Add("End s" + slot + " " + end);
        }
    }

    [CustomFeedback("Probe/Haptic Value", Global = true)]
    [Serializable]
    public sealed class ProbeHapticValue : CustomEffectItem
    {
        public override FeedbackCategory Category => FeedbackCategory.Haptics;

        protected override CustomEffect CreateEffect() => new Effect();

        sealed class Effect : CustomEffect
        {
            protected override void Apply(int slot, float value)
            {
                ProbeLog.Values.Add(value);
                ProbeLog.Slots.Add(slot);
            }
        }
    }

    [CustomFeedback("Probe/Target Value", Requires = typeof(Transform))]
    [Serializable]
    public sealed class ProbeTargetValue : CustomEffectItem
    {
        protected override CustomEffect CreateEffect() => new Effect();

        sealed class Effect : CustomEffect
        {
            Transform m_Transform;

            protected override bool Prepare(in CustomPrepare context) => context.TryFindTarget(out m_Transform);

            protected override void Apply(int slot, float value)
            {
                if (m_Transform != null)
                    m_Transform.localPosition = new Vector3(value, 0f, 0f);
            }

            protected override void End(int slot, CustomEnd end) => ProbeLog.Calls.Add("End s" + slot + " " + end);
        }
    }

    // no attribute — the menu's own group
    [Serializable]
    public sealed class ProbeNoAttribute : CustomActionItem
    {
        protected override CustomAction CreateAction() => new CustomActionNone();
    }

    // a package path — both leaves stay
    [CustomFeedback("Flow/Unity Event")]
    [Serializable]
    public sealed class ProbeClash : CustomActionItem
    {
        protected override CustomAction CreateAction() => new CustomActionNone();
    }

    // two user types on one path
    [CustomFeedback("Probe/Twin")]
    [Serializable]
    public sealed class ProbeTwinA : CustomActionItem
    {
        protected override CustomAction CreateAction() => new CustomActionNone();
    }

    [CustomFeedback("Probe/Twin")]
    [Serializable]
    public sealed class ProbeTwinB : CustomActionItem
    {
        protected override CustomAction CreateAction() => new CustomActionNone();
    }

    // a parameterless constructor that is not public
    [CustomFeedback("Probe/Hidden Action")]
    [Serializable]
    public sealed class ProbeHiddenAction : CustomActionItem
    {
        [SerializeField] public float m_Gain = 2f;

        ProbeHiddenAction()
        {
        }

        public static ProbeHiddenAction Make() => new ProbeHiddenAction();

        protected override CustomAction CreateAction() => new CustomActionNone();
    }

    [CustomFeedback("Probe/Hidden Effect", Global = true)]
    [Serializable]
    public sealed class ProbeHiddenEffect : CustomEffectItem
    {
        ProbeHiddenEffect()
        {
        }

        protected override CustomEffect CreateEffect() => new Effect();

        sealed class Effect : CustomEffect
        {
            protected override void Apply(int slot, float value)
            {
            }
        }
    }

    // a target the rules check: a Light
    [CustomFeedback("Probe/Light Action", Requires = typeof(Light))]
    [Serializable]
    public sealed class ProbeLightAction : CustomActionItem
    {
        protected override CustomAction CreateAction() => new CustomActionNone();
    }

    // fields named as the package's rules name theirs — a user's fields always show
    [CustomFeedback("Probe/Named Fields", Global = true)]
    [Serializable]
    public sealed class ProbeNamedFields : CustomActionItem
    {
        [SerializeField] public float m_Interpolation = 1f;
        [SerializeField] public float m_Min;
        [SerializeField] public bool m_DirectWrite;

        protected override CustomAction CreateAction() => new CustomActionNone();
    }

    // a middleware's event reference as it is saved — a struct of numbers and a path
    [Serializable]
    public struct ProbeEventRef
    {
        public int m_A;
        public int m_B;
        public string m_Path;
    }

    // the field kinds a user's feedback saves — Keep Play Mode Changes carries them
    [CustomFeedback("Probe/Field Kinds", Global = true)]
    [Serializable]
    public sealed class ProbeFieldKinds : CustomActionItem
    {
        [SerializeField] public float m_Gain;
        [SerializeField] public int m_Count;
        [SerializeField] public string m_Event = "";
        [SerializeField] public Vector3 m_Offset;
        [SerializeField] public Color m_Tint;
        [SerializeField] public ProbeEventRef m_Ref;
        [SerializeField] public UnityEngine.Object m_Asset;
        [SerializeField] public LayerMask m_Mask;
        [SerializeField] public AnimationCurve m_Curve = AnimationCurve.Linear(0f, 0f, 1f, 1f);
        [SerializeField] public FeedbackCategory m_Kind;

        protected override CustomAction CreateAction() => new CustomActionNone();
    }

    // registered by a test with its own factory — the exact registration wins
    [Serializable]
    public sealed class ProbeOverridden : CustomActionItem
    {
        protected override CustomAction CreateAction() => new CustomActionNone();
    }

    public sealed class CustomActionNone : CustomAction
    {
    }

    // ---- the allocation gate's items: no allocation in a call
    public static class GateCounts
    {
        public static int Fires;
        public static int Updates;
        public static int Applies;

        public static void Clear()
        {
            Fires = 0;
            Updates = 0;
            Applies = 0;
        }
    }

    [CustomFeedback("Probe/Gate Instant")]
    [Serializable]
    public sealed class GateInstant : CustomActionItem
    {
        protected override CustomAction CreateAction() => new Action();

        sealed class Action : CustomAction
        {
            protected override void Fire(in CustomPlay play) => GateCounts.Fires++;

            protected override void Undo(in CustomPlay play) => GateCounts.Fires++;
        }
    }

    [CustomFeedback("Probe/Gate Timed")]
    [Serializable]
    public sealed class GateTimed : CustomActionItem
    {
        public override float Duration => 0.3f;

        protected override CustomAction CreateAction() => new Action();

        sealed class Action : CustomAction
        {
            protected override void Update(in CustomTick tick) => GateCounts.Updates++;
        }
    }

    [CustomFeedback("Probe/Gate Effect")]
    [Serializable]
    public sealed class GateEffect : CustomEffectItem
    {
        protected override CustomEffect CreateEffect() => new Effect();

        sealed class Effect : CustomEffect
        {
            protected override void Apply(int slot, float value) => GateCounts.Applies++;
        }
    }
}
