using TMPro;
using UnityEngine;

namespace xpTURN.Feedback
{
    // how a floating text looks and moves — sizes in the overlay's reference pixels (1920 × 1080). The scale's start
    // range is the scale's own (not the opacity's)
    /// <summary>How a floating text looks and moves; sizes are in the overlay's reference pixels (1920 × 1080).</summary>
    [CreateAssetMenu(menuName = "xpTURN/Feedback/Floating Text Style", fileName = "FloatingTextStyle")]
    public sealed class FloatingTextStyle : ScriptableObject
    {
        [SerializeField] internal TMP_FontAsset m_Font;
        [SerializeField] internal float m_FontSize = FloatingTextLook.DefaultFontSize;
        [SerializeField] internal GameObject m_Prefab;
        [SerializeField] internal Vector2 m_Lifetime = new Vector2(0.8f, 0.8f);
        [SerializeField] internal Vector2 m_Distance = new Vector2(0f, 80f);
        [SerializeField] internal AnimationCurve m_MoveX = AnimationCurve.Linear(0f, 0f, 1f, 1f);
        [SerializeField] internal AnimationCurve m_MoveY = AnimationCurve.Linear(0f, 0f, 1f, 1f);
        [SerializeField] internal float m_Spread;
        [SerializeField] internal float m_Scatter;
        [SerializeField] internal AnimationCurve m_Scale = AnimationCurve.Linear(0f, 1f, 1f, 1f);
        [SerializeField] internal Vector2 m_ScaleStart = new Vector2(1f, 1f);
        [SerializeField] internal AnimationCurve m_Alpha = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(0.7f, 1f), new Keyframe(1f, 0f));
        [SerializeField] internal Vector2 m_AlphaStart = new Vector2(1f, 1f);
        [SerializeField] internal Gradient m_Color = new Gradient();
    }

    // a style's values — the asset's, or the code's defaults when the item names none (no ScriptableObject is made: the
    // preview recovery keeps a prefixed hidden one)
    internal struct FloatingTextLook
    {
        internal Vector2 Lifetime;
        internal Vector2 Distance;
        internal AnimationCurve MoveX;
        internal AnimationCurve MoveY;
        internal float Spread;
        internal float Scatter;
        internal AnimationCurve Scale;
        internal Vector2 ScaleStart;
        internal AnimationCurve Alpha;
        internal Vector2 AlphaStart;
        internal Gradient Color;

        internal const float DefaultFontSize = 36f;

        static readonly AnimationCurve s_Move = AnimationCurve.Linear(0f, 0f, 1f, 1f);
        static readonly AnimationCurve s_Scale = AnimationCurve.Linear(0f, 1f, 1f, 1f);
        static readonly AnimationCurve s_Alpha = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(0.7f, 1f), new Keyframe(1f, 0f));
        static readonly Gradient s_Color = new Gradient();

        internal static FloatingTextLook Of(FloatingTextStyle style)
        {
            if (style == null)
                return new FloatingTextLook
                {
                    Lifetime = new Vector2(0.8f, 0.8f),
                    Distance = new Vector2(0f, 80f),
                    MoveX = s_Move,
                    MoveY = s_Move,
                    Scale = s_Scale,
                    ScaleStart = new Vector2(1f, 1f),
                    Alpha = s_Alpha,
                    AlphaStart = new Vector2(1f, 1f),
                    Color = s_Color,
                };
            return new FloatingTextLook
            {
                Lifetime = style.m_Lifetime,
                Distance = style.m_Distance,
                MoveX = style.m_MoveX,
                MoveY = style.m_MoveY,
                Spread = style.m_Spread,
                Scatter = style.m_Scatter,
                Scale = style.m_Scale,
                ScaleStart = style.m_ScaleStart,
                Alpha = style.m_Alpha,
                AlphaStart = style.m_AlphaStart,
                Color = style.m_Color,
            };
        }
    }
}
