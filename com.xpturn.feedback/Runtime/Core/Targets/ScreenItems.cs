using System;
using UnityEngine;

namespace xpTURN.Feedback
{
    // the cover's look
    public enum ScreenFadeShape
    {
        Plain = 0,
        Wipe = 1,
        Iris = 2,
    }

    // the side a wipe comes in from · a progress bar fills from
    public enum ScreenSide
    {
        Left = 0,
        Right = 1,
        Bottom = 2,
        Top = 3,
    }

    public enum ScreenFadeCenter
    {
        Screen = 0,
        PlayPosition = 1,
    }

    public enum FloatingTextSource
    {
        Text = 0,
        Intensity = 1,
    }

    // the screen overlay's flash — an image of its own, its alpha driven (the color's RGB)
    [FeedbackTarget("Screen/Flash", PreviewGrade.Full, Aliases = "flash screen white", Global = true, Implementation = UGUINames.Assembly)]
    [Serializable]
    public sealed class ScreenFlashItem : TargetItem
    {
        static readonly TargetTraits s_Traits = new TargetTraits(valueKind: ValueKind.Float, supportedMotions: MotionMask.Flash | MotionMask.Blink,
            category: FeedbackCategory.None, flashSensitive: true, applyMode: ApplyMode.Offset);

        [SerializeField] internal Color m_Color = Color.white;

        internal override TargetTraits Traits => s_Traits;
    }

    // the screen overlay's cover — one for all fades; its value 1 covers. Its look is the last fade begun's
    [FeedbackTarget("Screen/Fade", PreviewGrade.Full, Aliases = "fade cover wipe iris black", Global = true, Implementation = UGUINames.Assembly)]
    [Serializable]
    public sealed class ScreenFadeItem : TargetItem
    {
        static readonly TargetTraits s_Traits = new TargetTraits(valueKind: ValueKind.Float, supportedMotions: MotionMask.Tween,
            category: FeedbackCategory.None, flashSensitive: false, applyMode: ApplyMode.Offset);

        [SerializeField] internal Color m_Color = Color.black;
        [SerializeField] internal ScreenFadeShape m_Shape;
        [SerializeField] internal ScreenSide m_From;
        [SerializeField] internal ScreenFadeCenter m_Center;

        internal override TargetTraits Traits => s_Traits;
    }

    // a text spawned on the screen overlay at the play's place (else the target's) — the value is not read
    [FeedbackTarget("Screen/Floating Text", PreviewGrade.DisplayOnly, Aliases = "damage number popup text", Requires = typeof(Transform), Implementation = UGUINames.Assembly)]
    [Serializable]
    public sealed class FloatingTextItem : TargetItem
    {
        static readonly TargetTraits s_Traits = new TargetTraits(valueKind: ValueKind.Bool, supportedMotions: MotionMask.Set,
            category: FeedbackCategory.None, flashSensitive: false, applyMode: ApplyMode.Offset);

        [SerializeField] internal ScriptableObject m_Style;
        [SerializeField] internal FloatingTextSource m_Source;
        [SerializeField] internal string m_Text = "";
        [SerializeField] internal string m_Format = "{0:0}";
        [SerializeField] internal float m_Multiplier = 1f;
        [SerializeField] internal Vector3 m_Offset;
        [SerializeField] internal bool m_Follow;
        [SerializeField] internal int m_PoolSize = 8;

        internal override TargetTraits Traits => s_Traits;
    }
}
