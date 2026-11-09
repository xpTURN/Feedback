namespace xpTURN.Feedback
{
    // a type switch names every concrete motion type — data used only by a bundle keeps its type through the linker. null or
    // an unknown type gives null: preparation skips the item
    internal static class MotionEvaluators
    {
        internal static MotionEvaluator Create(FeedbackMotion motion, int dimension)
        {
            switch (motion)
            {
                case SetMotion m: return new SetEvaluator(in m.m_Params, dimension);
                case TweenMotion m: return new TweenEvaluator(in m.m_Params, dimension);
                case PunchMotion m: return new PunchEvaluator(in m.m_Params, dimension);
                case ShakeMotion m: return new ShakeEvaluator(in m.m_Params, dimension);
                case CurveMotion m: return new CurveEvaluator(in m.m_Params, dimension);
                case FlashMotion m: return new FlashEvaluator(in m.m_Params, dimension);
                case GradientMotion m: return new GradientEvaluator(in m.m_Params, dimension);
                case OscillateMotion m: return new OscillateEvaluator(in m.m_Params, dimension);
                case BlinkMotion m: return new BlinkEvaluator(in m.m_Params, dimension);
                case WiggleMotion m: return new WiggleEvaluator(in m.m_Params, dimension);
                case SpringMotion m: return new SpringEvaluator(in m.m_Params, dimension);
                default: return null;
            }
        }
    }
}
