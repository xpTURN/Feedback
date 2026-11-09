using UnityEngine;

namespace xpTURN.Feedback.Tests
{
    // values serialized and written by setters, as an engine property is — no dirty. The fallback keeps what AnimationMode
    // animates of it — the float, the vector — and not the string, which the save state tests use as serialized state a
    // preview cannot record. No serialized counter: an OnValidate that bumped one would shake the file comparisons
    public sealed class PreviewTestTarget : MonoBehaviour
    {
        [SerializeField] float m_Value;
        [SerializeField] Vector3 m_Offset;
        [SerializeField] string m_Label = "";

        public float Value
        {
            get => m_Value;
            set => m_Value = value;
        }

        public Vector3 Offset
        {
            get => m_Offset;
            set => m_Offset = value;
        }

        public string Label
        {
            get => m_Label;
            set => m_Label = value;
        }
    }
}
