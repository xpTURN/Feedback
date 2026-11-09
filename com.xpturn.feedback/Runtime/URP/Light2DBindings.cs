using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace xpTURN.Feedback
{
    // a 2D light's intensity · color · radius · falloff — LightIntensityBinding's form
    internal sealed class Light2DIntensityBinding : ValueTargetBinding<float, FloatOps>
    {
        internal Light2DIntensityBinding(Light2DIntensityItem item)
            : base(item)
        {
        }

        protected override bool Acquire(in PrepareContext context, bool direct, out ValueSlot<float, FloatOps> slot)
        {
            slot = null;
            Light2D light = TargetResolver.Find<Light2D>(in m_Item.m_Target, in context, out TargetFailure failure);
            if (light == null)
                return Refuse(TargetResolver.Describe(failure));
            if (direct)
                slot = Layers.CreatePrivate(light, 0, 0, () => new Light2DIntensitySlot(light));
            else
                slot = Layers.GetOrCreate(light, 0, 0, () => new Light2DIntensitySlot(light));
            return true;
        }
    }

    internal sealed class Light2DColorBinding : ValueTargetBinding<Color, ColorOps>
    {
        internal Light2DColorBinding(Light2DColorItem item)
            : base(item)
        {
        }

        protected override bool Acquire(in PrepareContext context, bool direct, out ValueSlot<Color, ColorOps> slot)
        {
            slot = null;
            Light2D light = TargetResolver.Find<Light2D>(in m_Item.m_Target, in context, out TargetFailure failure);
            if (light == null)
                return Refuse(TargetResolver.Describe(failure));
            if (direct)
                slot = Layers.CreatePrivate(light, 0, 0, () => new Light2DColorSlot(light));
            else
                slot = Layers.GetOrCreate(light, 0, 0, () => new Light2DColorSlot(light));
            return true;
        }
    }

    internal sealed class Light2DRadiusBinding : ValueTargetBinding<float, FloatOps>
    {
        internal Light2DRadiusBinding(Light2DRadiusItem item)
            : base(item)
        {
        }

        protected override bool Acquire(in PrepareContext context, bool direct, out ValueSlot<float, FloatOps> slot)
        {
            slot = null;
            Light2D light = TargetResolver.Find<Light2D>(in m_Item.m_Target, in context, out TargetFailure failure);
            if (light == null)
                return Refuse(TargetResolver.Describe(failure));
            if (direct)
                slot = Layers.CreatePrivate(light, 0, 0, () => new Light2DRadiusSlot(light));
            else
                slot = Layers.GetOrCreate(light, 0, 0, () => new Light2DRadiusSlot(light));
            return true;
        }
    }

    internal sealed class Light2DFalloffBinding : ValueTargetBinding<float, FloatOps>
    {
        internal Light2DFalloffBinding(Light2DFalloffItem item)
            : base(item)
        {
        }

        protected override bool Acquire(in PrepareContext context, bool direct, out ValueSlot<float, FloatOps> slot)
        {
            slot = null;
            Light2D light = TargetResolver.Find<Light2D>(in m_Item.m_Target, in context, out TargetFailure failure);
            if (light == null)
                return Refuse(TargetResolver.Describe(failure));
            if (direct)
                slot = Layers.CreatePrivate(light, 0, 0, () => new Light2DFalloffSlot(light));
            else
                slot = Layers.GetOrCreate(light, 0, 0, () => new Light2DFalloffSlot(light));
            return true;
        }
    }
}
