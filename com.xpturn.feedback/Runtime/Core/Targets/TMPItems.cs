using System;
using UnityEngine;

namespace xpTURN.Feedback
{
    // a TextMeshPro text's values — the color · alpha are UI/Color · UI/Alpha's
    [FeedbackTarget("TMP/Font Size", PreviewGrade.Full, Aliases = "text size", RequiresName = UGUINames.TMPText, Implementation = UGUINames.Assembly)]
    [Serializable]
    public sealed class TMPFontSizeItem : TargetItem
    {
        static readonly TargetTraits s_Traits = new TargetTraits(valueKind: ValueKind.Float, supportedMotions: MotionMask.Tween | MotionMask.Punch | MotionMask.Spring,
            category: FeedbackCategory.None, flashSensitive: false, applyMode: ApplyMode.Offset);

        internal override TargetTraits Traits => s_Traits;
    }

    [FeedbackTarget("TMP/Character Spacing", PreviewGrade.Full, Aliases = "letter tracking", RequiresName = UGUINames.TMPText, Implementation = UGUINames.Assembly)]
    [Serializable]
    public sealed class TMPCharacterSpacingItem : TargetItem
    {
        static readonly TargetTraits s_Traits = new TargetTraits(valueKind: ValueKind.Float, supportedMotions: MotionMask.Tween | MotionMask.Punch | MotionMask.Oscillate | MotionMask.Spring,
            category: FeedbackCategory.None, flashSensitive: false, applyMode: ApplyMode.Offset);

        internal override TargetTraits Traits => s_Traits;
    }

    [FeedbackTarget("TMP/Word Spacing", PreviewGrade.Full, RequiresName = UGUINames.TMPText, Implementation = UGUINames.Assembly)]
    [Serializable]
    public sealed class TMPWordSpacingItem : TargetItem
    {
        static readonly TargetTraits s_Traits = new TargetTraits(valueKind: ValueKind.Float, supportedMotions: MotionMask.Tween | MotionMask.Punch | MotionMask.Oscillate | MotionMask.Spring,
            category: FeedbackCategory.None, flashSensitive: false, applyMode: ApplyMode.Offset);

        internal override TargetTraits Traits => s_Traits;
    }

    [FeedbackTarget("TMP/Line Spacing", PreviewGrade.Full, Aliases = "leading", RequiresName = UGUINames.TMPText, Implementation = UGUINames.Assembly)]
    [Serializable]
    public sealed class TMPLineSpacingItem : TargetItem
    {
        static readonly TargetTraits s_Traits = new TargetTraits(valueKind: ValueKind.Float, supportedMotions: MotionMask.Tween | MotionMask.Punch | MotionMask.Oscillate | MotionMask.Spring,
            category: FeedbackCategory.None, flashSensitive: false, applyMode: ApplyMode.Offset);

        internal override TargetTraits Traits => s_Traits;
    }

    [FeedbackTarget("TMP/Paragraph Spacing", PreviewGrade.Full, RequiresName = UGUINames.TMPText, Implementation = UGUINames.Assembly)]
    [Serializable]
    public sealed class TMPParagraphSpacingItem : TargetItem
    {
        static readonly TargetTraits s_Traits = new TargetTraits(valueKind: ValueKind.Float, supportedMotions: MotionMask.Tween | MotionMask.Punch | MotionMask.Oscillate | MotionMask.Spring,
            category: FeedbackCategory.None, flashSensitive: false, applyMode: ApplyMode.Offset);

        internal override TargetTraits Traits => s_Traits;
    }

    // the font material's properties — in the text's own copy (the shared material stays)
    [FeedbackTarget("TMP/Dilate", PreviewGrade.Full, Aliases = "bold thickness", RequiresName = UGUINames.TMPText, Implementation = UGUINames.Assembly)]
    [Serializable]
    public sealed class TMPDilateItem : TargetItem
    {
        static readonly TargetTraits s_Traits = new TargetTraits(valueKind: ValueKind.Float, supportedMotions: MotionMask.Tween | MotionMask.Flash | MotionMask.Oscillate | MotionMask.Spring,
            category: FeedbackCategory.None, flashSensitive: false, applyMode: ApplyMode.Offset);

        internal override TargetTraits Traits => s_Traits;
    }

    [FeedbackTarget("TMP/Softness", PreviewGrade.Full, Aliases = "blur", RequiresName = UGUINames.TMPText, Implementation = UGUINames.Assembly)]
    [Serializable]
    public sealed class TMPSoftnessItem : TargetItem
    {
        static readonly TargetTraits s_Traits = new TargetTraits(valueKind: ValueKind.Float, supportedMotions: MotionMask.Tween | MotionMask.Flash | MotionMask.Oscillate | MotionMask.Spring,
            category: FeedbackCategory.None, flashSensitive: false, applyMode: ApplyMode.Offset);

        internal override TargetTraits Traits => s_Traits;
    }

    [FeedbackTarget("TMP/Outline Width", PreviewGrade.Full, Aliases = "stroke", RequiresName = UGUINames.TMPText, Implementation = UGUINames.Assembly)]
    [Serializable]
    public sealed class TMPOutlineWidthItem : TargetItem
    {
        static readonly TargetTraits s_Traits = new TargetTraits(valueKind: ValueKind.Float, supportedMotions: MotionMask.Tween | MotionMask.Flash | MotionMask.Oscillate | MotionMask.Spring,
            category: FeedbackCategory.None, flashSensitive: false, applyMode: ApplyMode.Offset);

        internal override TargetTraits Traits => s_Traits;
    }

    [FeedbackTarget("TMP/Outline Color", PreviewGrade.Full, Aliases = "stroke", RequiresName = UGUINames.TMPText, Implementation = UGUINames.Assembly)]
    [Serializable]
    public sealed class TMPOutlineColorItem : TargetItem
    {
        static readonly TargetTraits s_Traits = new TargetTraits(valueKind: ValueKind.Color, supportedMotions: MotionMask.Tween | MotionMask.Flash | MotionMask.Oscillate | MotionMask.Spring,
            category: FeedbackCategory.None, flashSensitive: true, applyMode: ApplyMode.Offset);

        internal override TargetTraits Traits => s_Traits;
    }

    [FeedbackTarget("TMP/Underlay Color", PreviewGrade.Full, Aliases = "shadow", RequiresName = UGUINames.TMPText, Implementation = UGUINames.Assembly)]
    [Serializable]
    public sealed class TMPUnderlayColorItem : TargetItem
    {
        static readonly TargetTraits s_Traits = new TargetTraits(valueKind: ValueKind.Color, supportedMotions: MotionMask.Tween | MotionMask.Flash | MotionMask.Oscillate | MotionMask.Spring,
            category: FeedbackCategory.None, flashSensitive: true, applyMode: ApplyMode.Offset);

        internal override TargetTraits Traits => s_Traits;
    }

    [FeedbackTarget("TMP/Underlay Offset", PreviewGrade.Full, Aliases = "shadow", RequiresName = UGUINames.TMPText, Implementation = UGUINames.Assembly)]
    [Serializable]
    public sealed class TMPUnderlayOffsetItem : TargetItem
    {
        static readonly TargetTraits s_Traits = new TargetTraits(valueKind: ValueKind.Vector2, supportedMotions: MotionMask.Tween | MotionMask.Flash | MotionMask.Oscillate | MotionMask.Spring,
            category: FeedbackCategory.None, flashSensitive: false, applyMode: ApplyMode.Offset);

        internal override TargetTraits Traits => s_Traits;
    }

    // the text — the item's string (no string is made)
    [FeedbackTarget("TMP/Set Text", PreviewGrade.Full, Aliases = "label string content", RequiresName = UGUINames.TMPText, Implementation = UGUINames.Assembly)]
    [Serializable]
    public sealed class TMPTextItem : TargetItem
    {
        static readonly TargetTraits s_Traits = new TargetTraits(valueKind: ValueKind.Bool, supportedMotions: MotionMask.Set,
            category: FeedbackCategory.None, flashSensitive: false, applyMode: ApplyMode.Offset);

        [SerializeField] internal string m_Text = "";

        internal override TargetTraits Traits => s_Traits;
    }

    // the characters · words · lines shown — a progress 0 ~ 1
    public enum TMPRevealUnit
    {
        Character = 0,
        Word = 1,
        Line = 2,
    }

    [FeedbackTarget("TMP/Reveal", PreviewGrade.Full, Aliases = "typewriter", RequiresName = UGUINames.TMPText, Implementation = UGUINames.Assembly)]
    [Serializable]
    public sealed class TMPRevealItem : TargetItem
    {
        static readonly TargetTraits s_Traits = new TargetTraits(valueKind: ValueKind.Float, supportedMotions: MotionMask.Tween,
            category: FeedbackCategory.None, flashSensitive: false, applyMode: ApplyMode.Offset);

        [SerializeField] internal TMPRevealUnit m_Unit;

        internal override TargetTraits Traits => s_Traits;
    }

    // a number counted from one value to another — a progress 0 ~ 1, formatted into the text (TMP's {0:00.00} form)
    [FeedbackTarget("TMP/Count To", PreviewGrade.Full, Aliases = "score counter number", RequiresName = UGUINames.TMPText, Implementation = UGUINames.Assembly)]
    [Serializable]
    public sealed class TMPCountToItem : TargetItem
    {
        static readonly TargetTraits s_Traits = new TargetTraits(valueKind: ValueKind.Float, supportedMotions: MotionMask.Tween,
            category: FeedbackCategory.None, flashSensitive: false, applyMode: ApplyMode.Offset);

        [SerializeField] internal double m_From;
        [SerializeField] internal double m_To = 100;
        [SerializeField] internal string m_Format = "{0:0}";

        internal override TargetTraits Traits => s_Traits;
    }

    // the order the stagger walks the characters — saved, add members at the end only
    public enum TMPCharacterOrder
    {
        Forward = 0,
        Backward = 1,
    }

    // each character's offset — a wave (Oscillate) · a jitter (Wiggle) · a slide in (Tween) — in the character's size (its
    // point size). The stagger spreads it over the characters
    [FeedbackTarget("TMP/Character Offset", PreviewGrade.Full, Aliases = "wave jitter shake letters character fx", RequiresName = UGUINames.TMPText, Implementation = UGUINames.Assembly)]
    [Serializable]
    public sealed class TMPCharacterOffsetItem : TargetItem
    {
        static readonly TargetTraits s_Traits = new TargetTraits(valueKind: ValueKind.Vector2, supportedMotions: MotionMask.Tween | MotionMask.Oscillate | MotionMask.Wiggle,
            category: FeedbackCategory.Motion, flashSensitive: false, applyMode: ApplyMode.Offset);

        [SerializeField, Range(0f, 0.95f)] internal float m_Stagger = 0.5f;
        [SerializeField] internal TMPCharacterOrder m_Order;

        internal override TargetTraits Traits => s_Traits;
    }

    // each character's scale about its centre — a pop in (Tween From 0) · a pulse (Oscillate)
    [FeedbackTarget("TMP/Character Scale", PreviewGrade.Full, Aliases = "pop in letters character fx", RequiresName = UGUINames.TMPText, Implementation = UGUINames.Assembly)]
    [Serializable]
    public sealed class TMPCharacterScaleItem : TargetItem
    {
        static readonly TargetTraits s_Traits = new TargetTraits(valueKind: ValueKind.Float, supportedMotions: MotionMask.Tween | MotionMask.Oscillate | MotionMask.Wiggle,
            category: FeedbackCategory.Motion, flashSensitive: false, applyMode: ApplyMode.Offset);

        [SerializeField, Range(0f, 0.95f)] internal float m_Stagger = 0.5f;
        [SerializeField] internal TMPCharacterOrder m_Order;

        internal override TargetTraits Traits => s_Traits;
    }

    // each character's alpha, multiplied into its vertex colour (the rich text's alpha kept) — a fade (Tween)
    [FeedbackTarget("TMP/Character Alpha", PreviewGrade.Full, Aliases = "fade letters character fx", RequiresName = UGUINames.TMPText, Implementation = UGUINames.Assembly)]
    [Serializable]
    public sealed class TMPCharacterAlphaItem : TargetItem
    {
        static readonly TargetTraits s_Traits = new TargetTraits(valueKind: ValueKind.Float, supportedMotions: MotionMask.Tween | MotionMask.Oscillate,
            category: FeedbackCategory.None, flashSensitive: false, applyMode: ApplyMode.Offset);

        [SerializeField, Range(0f, 0.95f)] internal float m_Stagger = 0.5f;
        [SerializeField] internal TMPCharacterOrder m_Order;

        internal override TargetTraits Traits => s_Traits;
    }
}
