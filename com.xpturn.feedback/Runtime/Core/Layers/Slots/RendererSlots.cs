using UnityEngine;

namespace xpTURN.Feedback
{
    // a renderer's enabled — a toggle (GameObjectActiveSlot's form)
    [RecordPaths(typeof(Renderer), "m_Enabled")]
    internal sealed class RendererEnabledSlot : ToggleSlot
    {
        readonly Renderer m_Target;

        internal RendererEnabledSlot(Renderer target)
        {
            m_Target = target;
        }

        internal override bool IsAlive => m_Target != null;

        protected override bool Read() => m_Target.enabled;

        protected override void Write(bool value) => m_Target.enabled = value;
    }
}
