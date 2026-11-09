using UnityEngine;
using xpTURN.Feedback;

namespace xpTURN.Feedback.Samples.Basics
{
    // the gallery's Custom/Color Value target — a component of the user's that takes the value (IFeedbackTarget<Color>) and draws it
    // as its cube's color (a property block — the shared material stays as it is)
    public sealed class GalleryValueColor : MonoBehaviour, IFeedbackTarget<Color>
    {
        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");

        [SerializeField] Color m_Value = new Color(0.25f, 0.5f, 1f);

        Renderer m_Renderer;
        MaterialPropertyBlock m_Block;

        public Color FeedbackValue
        {
            get => m_Value;
            set
            {
                m_Value = value;
                Draw();
            }
        }

        void OnEnable() => Draw();

        void Draw()
        {
            if (m_Renderer == null && !TryGetComponent(out m_Renderer))
                return;
            m_Block ??= new MaterialPropertyBlock();
            m_Block.SetColor(BaseColor, m_Value);
            m_Renderer.SetPropertyBlock(m_Block);
        }
    }
}
