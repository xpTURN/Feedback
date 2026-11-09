using UnityEngine;

namespace xpTURN.Feedback
{
    // a light's intensity — below 0 the engine keeps +0 and the end is the base
    internal sealed class LightIntensityBinding : ValueTargetBinding<float, FloatOps>
    {
        internal LightIntensityBinding(LightIntensityItem item)
            : base(item)
        {
        }

        protected override bool Acquire(in PrepareContext context, bool direct, out ValueSlot<float, FloatOps> slot)
        {
            slot = null;
            Light light = TargetResolver.Find<Light>(in m_Item.m_Target, in context, out TargetFailure failure);
            if (light == null)
                return Refuse(TargetResolver.Describe(failure));
            if (direct)
                slot = Layers.CreatePrivate(light, 0, 0, () => new LightIntensitySlot(light));
            else
                slot = Layers.GetOrCreate(light, 0, 0, () => new LightIntensitySlot(light));
            return true;
        }
    }

    // the light's range · color · spot angle · shadow strength — LightIntensityBinding's form
    internal sealed class LightRangeBinding : ValueTargetBinding<float, FloatOps>
    {
        internal LightRangeBinding(LightRangeItem item)
            : base(item)
        {
        }

        protected override bool Acquire(in PrepareContext context, bool direct, out ValueSlot<float, FloatOps> slot)
        {
            slot = null;
            Light light = TargetResolver.Find<Light>(in m_Item.m_Target, in context, out TargetFailure failure);
            if (light == null)
                return Refuse(TargetResolver.Describe(failure));
            if (direct)
                slot = Layers.CreatePrivate(light, 0, 0, () => new LightRangeSlot(light));
            else
                slot = Layers.GetOrCreate(light, 0, 0, () => new LightRangeSlot(light));
            return true;
        }
    }

    internal sealed class LightColorBinding : ValueTargetBinding<Color, ColorOps>
    {
        internal LightColorBinding(LightColorItem item)
            : base(item)
        {
        }

        protected override bool Acquire(in PrepareContext context, bool direct, out ValueSlot<Color, ColorOps> slot)
        {
            slot = null;
            Light light = TargetResolver.Find<Light>(in m_Item.m_Target, in context, out TargetFailure failure);
            if (light == null)
                return Refuse(TargetResolver.Describe(failure));
            if (direct)
                slot = Layers.CreatePrivate(light, 0, 0, () => new LightColorSlot(light));
            else
                slot = Layers.GetOrCreate(light, 0, 0, () => new LightColorSlot(light));
            return true;
        }
    }

    internal sealed class LightSpotAngleBinding : ValueTargetBinding<float, FloatOps>
    {
        internal LightSpotAngleBinding(LightSpotAngleItem item)
            : base(item)
        {
        }

        protected override bool Acquire(in PrepareContext context, bool direct, out ValueSlot<float, FloatOps> slot)
        {
            slot = null;
            Light light = TargetResolver.Find<Light>(in m_Item.m_Target, in context, out TargetFailure failure);
            if (light == null)
                return Refuse(TargetResolver.Describe(failure));
            if (direct)
                slot = Layers.CreatePrivate(light, 0, 0, () => new LightSpotAngleSlot(light));
            else
                slot = Layers.GetOrCreate(light, 0, 0, () => new LightSpotAngleSlot(light));
            return true;
        }
    }

    internal sealed class LightShadowStrengthBinding : ValueTargetBinding<float, FloatOps>
    {
        internal LightShadowStrengthBinding(LightShadowStrengthItem item)
            : base(item)
        {
        }

        protected override bool Acquire(in PrepareContext context, bool direct, out ValueSlot<float, FloatOps> slot)
        {
            slot = null;
            Light light = TargetResolver.Find<Light>(in m_Item.m_Target, in context, out TargetFailure failure);
            if (light == null)
                return Refuse(TargetResolver.Describe(failure));
            if (direct)
                slot = Layers.CreatePrivate(light, 0, 0, () => new LightShadowStrengthSlot(light));
            else
                slot = Layers.GetOrCreate(light, 0, 0, () => new LightShadowStrengthSlot(light));
            return true;
        }
    }
}
