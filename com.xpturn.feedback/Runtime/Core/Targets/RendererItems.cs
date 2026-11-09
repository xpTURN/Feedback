using System;
using UnityEngine;

namespace xpTURN.Feedback
{
    // saved — add members at the end only
    public enum MaterialWriteMode
    {
        PropertyBlock = 0,
        Instance = 1,
    }

    [FeedbackTarget("Renderer/Material Color", PreviewGrade.Full, Aliases = "tint color flash", Requires = typeof(Renderer))]
    [Serializable]
    public sealed class MaterialColorItem : TargetItem, IMaterialPropertyItem
    {
        static readonly TargetTraits s_Traits = new TargetTraits(valueKind: ValueKind.Color,
            supportedMotions: MotionMask.Tween | MotionMask.Flash | MotionMask.Gradient | MotionMask.Punch | MotionMask.Oscillate | MotionMask.Blink | MotionMask.Spring,
            category: FeedbackCategory.None, flashSensitive: true, applyMode: ApplyMode.Offset);

        [SerializeField] internal string m_PropertyName = "";     // empty = the main color ([MainColor], else _Color)
        [SerializeField] internal int m_MaterialIndex = -1;       // -1 = every material (one block, base value from material 0)
        [SerializeField] internal MaterialWriteMode m_WriteMode;

        internal override TargetTraits Traits => s_Traits;

        string IMaterialPropertyItem.PropertyName => m_PropertyName;
        int IMaterialPropertyItem.MaterialIndex => m_MaterialIndex;
        MaterialWriteMode IMaterialPropertyItem.WriteMode => m_WriteMode;
    }

    // the material property items' common fields — the prepare and the rules read them. No public base: the six abstract
    // bases are closed
    internal interface IMaterialPropertyItem
    {
        string PropertyName { get; }
        int MaterialIndex { get; }
        MaterialWriteMode WriteMode { get; }
    }

    // a Float or Range property — named (an Integer is refused: SetFloat logs and writes nothing)
    [FeedbackTarget("Renderer/Material Float", PreviewGrade.Full, Requires = typeof(Renderer))]
    [Serializable]
    public sealed class MaterialFloatItem : TargetItem, IMaterialPropertyItem
    {
        static readonly TargetTraits s_Traits = new TargetTraits(valueKind: ValueKind.Float,
            supportedMotions: MotionMask.Tween | MotionMask.Flash | MotionMask.Punch | MotionMask.Shake | MotionMask.Curve | MotionMask.Oscillate | MotionMask.Spring,
            category: FeedbackCategory.None, flashSensitive: false, applyMode: ApplyMode.Offset);

        [SerializeField] internal string m_PropertyName = "";
        [SerializeField] internal int m_MaterialIndex = -1;
        [SerializeField] internal MaterialWriteMode m_WriteMode;

        internal override TargetTraits Traits => s_Traits;

        string IMaterialPropertyItem.PropertyName => m_PropertyName;
        int IMaterialPropertyItem.MaterialIndex => m_MaterialIndex;
        MaterialWriteMode IMaterialPropertyItem.WriteMode => m_WriteMode;
    }

    // a Vector property — named (a color takes Material Color: a block's SetVector skips the linear conversion)
    [FeedbackTarget("Renderer/Material Vector", PreviewGrade.Full, Requires = typeof(Renderer))]
    [Serializable]
    public sealed class MaterialVectorItem : TargetItem, IMaterialPropertyItem
    {
        static readonly TargetTraits s_Traits = new TargetTraits(valueKind: ValueKind.Vector4, supportedMotions: MotionMask.Tween | MotionMask.Curve | MotionMask.Oscillate,
            category: FeedbackCategory.None, flashSensitive: false, applyMode: ApplyMode.Offset);

        [SerializeField] internal string m_PropertyName = "";
        [SerializeField] internal int m_MaterialIndex = -1;
        [SerializeField] internal MaterialWriteMode m_WriteMode;

        internal override TargetTraits Traits => s_Traits;

        string IMaterialPropertyItem.PropertyName => m_PropertyName;
        int IMaterialPropertyItem.MaterialIndex => m_MaterialIndex;
        MaterialWriteMode IMaterialPropertyItem.WriteMode => m_WriteMode;
    }

    // a texture's offset — the property is the texture (empty = the main texture: [MainTexture], else _MainTex)
    [FeedbackTarget("Renderer/Texture Offset", PreviewGrade.Full, Requires = typeof(Renderer))]
    [Serializable]
    public sealed class TextureOffsetItem : TargetItem, IMaterialPropertyItem
    {
        static readonly TargetTraits s_Traits = new TargetTraits(valueKind: ValueKind.Vector2, supportedMotions: MotionMask.Tween | MotionMask.Curve | MotionMask.Oscillate,
            category: FeedbackCategory.None, flashSensitive: false, applyMode: ApplyMode.Offset);

        [SerializeField] internal string m_PropertyName = "";
        [SerializeField] internal int m_MaterialIndex = -1;
        [SerializeField] internal MaterialWriteMode m_WriteMode;

        internal override TargetTraits Traits => s_Traits;

        string IMaterialPropertyItem.PropertyName => m_PropertyName;
        int IMaterialPropertyItem.MaterialIndex => m_MaterialIndex;
        MaterialWriteMode IMaterialPropertyItem.WriteMode => m_WriteMode;
    }

    // a texture's scale (its tiling — composed by addition)
    [FeedbackTarget("Renderer/Texture Scale", PreviewGrade.Full, Requires = typeof(Renderer))]
    [Serializable]
    public sealed class TextureScaleItem : TargetItem, IMaterialPropertyItem
    {
        static readonly TargetTraits s_Traits = new TargetTraits(valueKind: ValueKind.Vector2, supportedMotions: MotionMask.Tween | MotionMask.Curve | MotionMask.Oscillate,
            category: FeedbackCategory.None, flashSensitive: false, applyMode: ApplyMode.Offset);

        [SerializeField] internal string m_PropertyName = "";
        [SerializeField] internal int m_MaterialIndex = -1;
        [SerializeField] internal MaterialWriteMode m_WriteMode;

        internal override TargetTraits Traits => s_Traits;

        string IMaterialPropertyItem.PropertyName => m_PropertyName;
        int IMaterialPropertyItem.MaterialIndex => m_MaterialIndex;
        MaterialWriteMode IMaterialPropertyItem.WriteMode => m_WriteMode;
    }

    // the HDR emission color × the multiplier (the alpha as the motion gives it). The material's emission keyword is its own —
    // a build keeps only the variants its materials use
    [FeedbackTarget("Renderer/Emission", PreviewGrade.Full, Requires = typeof(Renderer))]
    [Serializable]
    public sealed class EmissionItem : TargetItem, IMaterialPropertyItem
    {
        static readonly TargetTraits s_Traits = new TargetTraits(valueKind: ValueKind.Color, supportedMotions: MotionMask.Tween | MotionMask.Flash | MotionMask.Oscillate | MotionMask.Blink,
            category: FeedbackCategory.None, flashSensitive: true, applyMode: ApplyMode.Offset);

        [SerializeField] internal string m_PropertyName = "";     // empty = _EmissionColor
        [SerializeField] internal int m_MaterialIndex = -1;
        [SerializeField] internal MaterialWriteMode m_WriteMode;
        [SerializeField] internal float m_Multiplier = 1f;

        internal override TargetTraits Traits => s_Traits;

        string IMaterialPropertyItem.PropertyName => m_PropertyName;
        int IMaterialPropertyItem.MaterialIndex => m_MaterialIndex;
        MaterialWriteMode IMaterialPropertyItem.WriteMode => m_WriteMode;
    }

    // a material index's material for another — Set. The value is not read; −1 is every index
    [FeedbackTarget("Renderer/Material Swap", PreviewGrade.Full, Requires = typeof(Renderer))]
    [Serializable]
    public sealed class MaterialSwapItem : TargetItem
    {
        static readonly TargetTraits s_Traits = new TargetTraits(valueKind: ValueKind.Bool, supportedMotions: MotionMask.Set,
            category: FeedbackCategory.None, flashSensitive: false, applyMode: ApplyMode.Offset);

        [SerializeField] internal Material m_Material;
        [SerializeField] internal int m_MaterialIndex;

        internal override TargetTraits Traits => s_Traits;
    }

    // a renderer's enabled — a toggle
    [FeedbackTarget("Renderer/Visibility", PreviewGrade.Full, Aliases = "enabled show hide", Requires = typeof(Renderer))]
    [Serializable]
    public sealed class RendererVisibilityItem : TargetItem
    {
        static readonly TargetTraits s_Traits = new TargetTraits(valueKind: ValueKind.Bool, supportedMotions: MotionMask.Set | MotionMask.Blink,
            category: FeedbackCategory.None, flashSensitive: true, applyMode: ApplyMode.Offset);

        internal override TargetTraits Traits => s_Traits;
    }
}
