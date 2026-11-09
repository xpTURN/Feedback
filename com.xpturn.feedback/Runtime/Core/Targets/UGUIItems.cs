using System;
using System.Collections.Generic;
using UnityEngine;

namespace xpTURN.Feedback
{
    // the names Core can't type: uGUI's package types and the UI module's group
    internal static class UGUINames
    {
        internal const string Graphic = "UnityEngine.UI.Graphic, UnityEngine.UI";
        internal const string Image = "UnityEngine.UI.Image, UnityEngine.UI";
        internal const string Text = "UnityEngine.UI.Text, UnityEngine.UI";
        internal const string Selectable = "UnityEngine.UI.Selectable, UnityEngine.UI";
        internal const string CanvasGroup = "UnityEngine.CanvasGroup, UnityEngine.UIModule";
        internal const string TMPText = "TMPro.TMP_Text, Unity.TextMeshPro";
        internal const string TextMeshPro = "TMPro.TextMeshPro, Unity.TextMeshPro";
        internal const string TMPFontAsset = "TMPro.TMP_FontAsset, Unity.TextMeshPro";
        internal const string FloatingTextStyle = "xpTURN.Feedback.FloatingTextStyle, xpTURN.Feedback.UGUI";
        internal const string ProgressBar = "xpTURN.Feedback.ProgressBar, xpTURN.Feedback.UGUI";
        internal const string UITrigger = "xpTURN.Feedback.FeedbackUITrigger, xpTURN.Feedback.UGUI";
        internal const string PointerDownHandler = "UnityEngine.EventSystems.IPointerDownHandler, UnityEngine.UI";
        internal const string PointerClickHandler = "UnityEngine.EventSystems.IPointerClickHandler, UnityEngine.UI";
        internal const string NumberFormat = "xpTURN.Feedback.TMPNumberFormat, xpTURN.Feedback.UGUI";
        internal const string Assembly = "xpTURN.Feedback.UGUI";
    }

    // a Graphic's vertex color — a Text's too, a TextMeshPro text's through its own slot
    [FeedbackTarget("UI/Color", PreviewGrade.Full, Aliases = "tint text color", RequiresName = UGUINames.Graphic, Implementation = UGUINames.Assembly)]
    [Serializable]
    public sealed class GraphicColorItem : TargetItem
    {
        static readonly TargetTraits s_Traits = new TargetTraits(valueKind: ValueKind.Color,
            supportedMotions: MotionMask.Tween | MotionMask.Flash | MotionMask.Gradient | MotionMask.Blink | MotionMask.Punch | MotionMask.Spring,
            category: FeedbackCategory.None, flashSensitive: true, applyMode: ApplyMode.Offset);

        internal override TargetTraits Traits => s_Traits;
    }

    // the color's alpha — over the color's slot (Sprite Alpha's form)
    [FeedbackTarget("UI/Alpha", PreviewGrade.Full, Aliases = "fade opacity", RequiresName = UGUINames.Graphic, Implementation = UGUINames.Assembly)]
    [Serializable]
    public sealed class GraphicAlphaItem : TargetItem
    {
        static readonly TargetTraits s_Traits = new TargetTraits(valueKind: ValueKind.Float,
            supportedMotions: MotionMask.Tween | MotionMask.Flash | MotionMask.Blink | MotionMask.Punch | MotionMask.Spring,
            category: FeedbackCategory.None, flashSensitive: true, applyMode: ApplyMode.Offset);

        internal override TargetTraits Traits => s_Traits;
    }

    // the CanvasRenderer's color (not uGUI's CrossFadeColor — its coroutine allocates)
    [FeedbackTarget("UI/Cross Fade", PreviewGrade.Full, Aliases = "crossfade", RequiresName = UGUINames.Graphic, Implementation = UGUINames.Assembly)]
    [Serializable]
    public sealed class GraphicCrossFadeItem : TargetItem
    {
        static readonly TargetTraits s_Traits = new TargetTraits(valueKind: ValueKind.Color, supportedMotions: MotionMask.Tween,
            category: FeedbackCategory.None, flashSensitive: false, applyMode: ApplyMode.Offset);

        internal override TargetTraits Traits => s_Traits;
    }

    [FeedbackTarget("UI/Material", PreviewGrade.Full, RequiresName = UGUINames.Graphic, Implementation = UGUINames.Assembly)]
    [Serializable]
    public sealed class GraphicMaterialItem : TargetItem
    {
        static readonly TargetTraits s_Traits = new TargetTraits(valueKind: ValueKind.Bool, supportedMotions: MotionMask.Set,
            category: FeedbackCategory.None, flashSensitive: false, applyMode: ApplyMode.Offset);

        [SerializeField] internal Material m_Material;

        internal override TargetTraits Traits => s_Traits;
    }

    [FeedbackTarget("UI/Raycast Target", PreviewGrade.Full, Aliases = "click hit", RequiresName = UGUINames.Graphic, Implementation = UGUINames.Assembly)]
    [Serializable]
    public sealed class GraphicRaycastTargetItem : TargetItem
    {
        static readonly TargetTraits s_Traits = new TargetTraits(valueKind: ValueKind.Bool, supportedMotions: MotionMask.Set,
            category: FeedbackCategory.None, flashSensitive: false, applyMode: ApplyMode.Offset);

        internal override TargetTraits Traits => s_Traits;
    }

    // the main texture's offset · scale in the Graphic's copy of its material (_MainTex_ST — the material texture items' halves)
    [FeedbackTarget("UI/Texture Offset", PreviewGrade.Full, Aliases = "scroll uv", RequiresName = UGUINames.Graphic, Implementation = UGUINames.Assembly)]
    [Serializable]
    public sealed class GraphicTextureOffsetItem : TargetItem
    {
        static readonly TargetTraits s_Traits = new TargetTraits(valueKind: ValueKind.Vector2, supportedMotions: MotionMask.Tween,
            category: FeedbackCategory.None, flashSensitive: false, applyMode: ApplyMode.Offset);

        internal override TargetTraits Traits => s_Traits;
    }

    [FeedbackTarget("UI/Texture Scale", PreviewGrade.Full, Aliases = "tiling uv", RequiresName = UGUINames.Graphic, Implementation = UGUINames.Assembly)]
    [Serializable]
    public sealed class GraphicTextureScaleItem : TargetItem
    {
        static readonly TargetTraits s_Traits = new TargetTraits(valueKind: ValueKind.Vector2, supportedMotions: MotionMask.Tween,
            category: FeedbackCategory.None, flashSensitive: false, applyMode: ApplyMode.Offset);

        internal override TargetTraits Traits => s_Traits;
    }

    [FeedbackTarget("UI/Image Fill", PreviewGrade.Full, Aliases = "fill amount progress", RequiresName = UGUINames.Image, Implementation = UGUINames.Assembly)]
    [Serializable]
    public sealed class ImageFillItem : TargetItem
    {
        static readonly TargetTraits s_Traits = new TargetTraits(valueKind: ValueKind.Float, supportedMotions: MotionMask.Tween | MotionMask.Punch | MotionMask.Spring,
            category: FeedbackCategory.None, flashSensitive: false, applyMode: ApplyMode.Offset);

        internal override TargetTraits Traits => s_Traits;
    }

    [FeedbackTarget("UI/Image Sprite", PreviewGrade.Full, Aliases = "swap", RequiresName = UGUINames.Image, Implementation = UGUINames.Assembly)]
    [Serializable]
    public sealed class ImageSpriteItem : TargetItem
    {
        static readonly TargetTraits s_Traits = new TargetTraits(valueKind: ValueKind.Bool, supportedMotions: MotionMask.Set,
            category: FeedbackCategory.None, flashSensitive: false, applyMode: ApplyMode.Offset);

        [SerializeField] internal Sprite m_Sprite;

        internal override TargetTraits Traits => s_Traits;
    }

    // Sprite Sheet's Image list
    [FeedbackTarget("UI/Image Sprite Sheet", PreviewGrade.Full, Aliases = "flipbook frames", RequiresName = UGUINames.Image, Implementation = UGUINames.Assembly)]
    [Serializable]
    public sealed class ImageSpriteSheetItem : TargetItem
    {
        static readonly TargetTraits s_Traits = new TargetTraits(valueKind: ValueKind.Float, supportedMotions: MotionMask.Tween,
            category: FeedbackCategory.None, flashSensitive: false, applyMode: ApplyMode.Offset);

        [SerializeField] internal List<Sprite> m_Frames = new List<Sprite>();

        internal override TargetTraits Traits => s_Traits;
    }

    [FeedbackTarget("UI/Canvas Group Alpha", PreviewGrade.Full, Aliases = "fade group", RequiresName = UGUINames.CanvasGroup, Implementation = UGUINames.Assembly)]
    [Serializable]
    public sealed class CanvasGroupAlphaItem : TargetItem
    {
        static readonly TargetTraits s_Traits = new TargetTraits(valueKind: ValueKind.Float, supportedMotions: MotionMask.Tween | MotionMask.Flash | MotionMask.Blink,
            category: FeedbackCategory.None, flashSensitive: true, applyMode: ApplyMode.Offset);

        internal override TargetTraits Traits => s_Traits;
    }

    [FeedbackTarget("UI/Canvas Group Interactable", PreviewGrade.Full, RequiresName = UGUINames.CanvasGroup, Implementation = UGUINames.Assembly)]
    [Serializable]
    public sealed class CanvasGroupInteractableItem : TargetItem
    {
        static readonly TargetTraits s_Traits = new TargetTraits(valueKind: ValueKind.Bool, supportedMotions: MotionMask.Set,
            category: FeedbackCategory.None, flashSensitive: false, applyMode: ApplyMode.Offset);

        internal override TargetTraits Traits => s_Traits;
    }

    [FeedbackTarget("UI/Canvas Group Blocks Raycasts", PreviewGrade.Full, RequiresName = UGUINames.CanvasGroup, Implementation = UGUINames.Assembly)]
    [Serializable]
    public sealed class CanvasGroupBlocksRaycastsItem : TargetItem
    {
        static readonly TargetTraits s_Traits = new TargetTraits(valueKind: ValueKind.Bool, supportedMotions: MotionMask.Set,
            category: FeedbackCategory.None, flashSensitive: false, applyMode: ApplyMode.Offset);

        internal override TargetTraits Traits => s_Traits;
    }

    // a legacy Text's content — the item's string (no string is made)
    [FeedbackTarget("UI/Text", PreviewGrade.Full, Aliases = "label string content", RequiresName = UGUINames.Text, Implementation = UGUINames.Assembly)]
    [Serializable]
    public sealed class TextContentItem : TargetItem
    {
        static readonly TargetTraits s_Traits = new TargetTraits(valueKind: ValueKind.Bool, supportedMotions: MotionMask.Set,
            category: FeedbackCategory.None, flashSensitive: false, applyMode: ApplyMode.Offset);

        [SerializeField] internal string m_Text = "";

        internal override TargetTraits Traits => s_Traits;
    }

    // a legacy Text's font size — written rounded (an int)
    [FeedbackTarget("UI/Font Size", PreviewGrade.Full, RequiresName = UGUINames.Text, Implementation = UGUINames.Assembly)]
    [Serializable]
    public sealed class TextFontSizeItem : TargetItem
    {
        static readonly TargetTraits s_Traits = new TargetTraits(valueKind: ValueKind.Float, supportedMotions: MotionMask.Tween | MotionMask.Punch,
            category: FeedbackCategory.None, flashSensitive: false, applyMode: ApplyMode.Offset);

        internal override TargetTraits Traits => s_Traits;
    }

    [FeedbackTarget("UI/Interactable", PreviewGrade.Full, Aliases = "button enable disable", RequiresName = UGUINames.Selectable, Implementation = UGUINames.Assembly)]
    [Serializable]
    public sealed class SelectableInteractableItem : TargetItem
    {
        static readonly TargetTraits s_Traits = new TargetTraits(valueKind: ValueKind.Bool, supportedMotions: MotionMask.Set,
            category: FeedbackCategory.None, flashSensitive: false, applyMode: ApplyMode.Offset);

        internal override TargetTraits Traits => s_Traits;
    }
}
