using UnityEngine;

namespace xpTURN.Feedback
{
    // the camera's value targets — LightIntensityBinding's form. The main camera is found at the first Begin when there is
    // none at the prepare; a channel target is refused (no lens receiver)
    internal sealed class CameraFieldOfViewBinding : ValueTargetBinding<float, FloatOps>
    {
        internal CameraFieldOfViewBinding(CameraFieldOfViewItem item)
            : base(item)
        {
        }

        protected override bool Acquire(in PrepareContext context, bool direct, out ValueSlot<float, FloatOps> slot)
        {
            slot = null;
            Camera camera = TargetResolver.Find<Camera>(in m_Item.m_Target, in context, out TargetFailure failure);
            if (camera == null)
                return Refuse(TargetResolver.Describe(failure));
            if (direct)
                slot = Layers.CreatePrivate(camera, 0, 0, () => new CameraFieldOfViewSlot(camera));
            else
                slot = Layers.GetOrCreate(camera, 0, 0, () => new CameraFieldOfViewSlot(camera));
            return true;
        }
    }

    internal sealed class CameraOrthographicSizeBinding : ValueTargetBinding<float, FloatOps>
    {
        internal CameraOrthographicSizeBinding(CameraOrthographicSizeItem item)
            : base(item)
        {
        }

        protected override bool Acquire(in PrepareContext context, bool direct, out ValueSlot<float, FloatOps> slot)
        {
            slot = null;
            Camera camera = TargetResolver.Find<Camera>(in m_Item.m_Target, in context, out TargetFailure failure);
            if (camera == null)
                return Refuse(TargetResolver.Describe(failure));
            if (direct)
                slot = Layers.CreatePrivate(camera, 0, 0, () => new CameraOrthographicSizeSlot(camera));
            else
                slot = Layers.GetOrCreate(camera, 0, 0, () => new CameraOrthographicSizeSlot(camera));
            return true;
        }
    }

    internal sealed class CameraClippingPlanesBinding : ValueTargetBinding<Vector2, Vector2Ops>
    {
        internal CameraClippingPlanesBinding(CameraClippingPlanesItem item)
            : base(item)
        {
        }

        protected override bool Acquire(in PrepareContext context, bool direct, out ValueSlot<Vector2, Vector2Ops> slot)
        {
            slot = null;
            Camera camera = TargetResolver.Find<Camera>(in m_Item.m_Target, in context, out TargetFailure failure);
            if (camera == null)
                return Refuse(TargetResolver.Describe(failure));
            if (direct)
                slot = Layers.CreatePrivate(camera, 0, 0, () => new CameraClippingPlanesSlot(camera));
            else
                slot = Layers.GetOrCreate(camera, 0, 0, () => new CameraClippingPlanesSlot(camera));
            return true;
        }
    }

    internal sealed class CameraBackgroundColorBinding : ValueTargetBinding<Color, ColorOps>
    {
        internal CameraBackgroundColorBinding(CameraBackgroundColorItem item)
            : base(item)
        {
        }

        protected override bool Acquire(in PrepareContext context, bool direct, out ValueSlot<Color, ColorOps> slot)
        {
            slot = null;
            Camera camera = TargetResolver.Find<Camera>(in m_Item.m_Target, in context, out TargetFailure failure);
            if (camera == null)
                return Refuse(TargetResolver.Describe(failure));
            if (direct)
                slot = Layers.CreatePrivate(camera, 0, 0, () => new CameraBackgroundColorSlot(camera));
            else
                slot = Layers.GetOrCreate(camera, 0, 0, () => new CameraBackgroundColorSlot(camera));
            return true;
        }
    }

    // the motion's value v is the zoom in stops — factor k = 2^v, held at MinFactor or more. A perspective camera's field of
    // view goes to 2·atan(tan(θ/2)/k), an orthographic one's size to size/k — the same magnification; the projection is read
    // at the prepare (a later switch takes effect at the next prepare). Persist (Tween) measures from the binding's origin as
    // factor 1; an offset (Punch) becomes the property's offset from the slot's base, so it adds with the field of view's or
    // the size's own items in the one slot
    internal sealed class CameraZoomBinding : ValueTargetBinding<float, FloatOps>
    {
        internal const float MinFactor = 0.01f;

        bool m_Orthographic;

        internal CameraZoomBinding(CameraZoomItem item)
            : base(item)
        {
        }

        internal bool Orthographic => m_Orthographic;

        protected override bool Acquire(in PrepareContext context, bool direct, out ValueSlot<float, FloatOps> slot)
        {
            slot = null;
            Camera camera = TargetResolver.Find<Camera>(in m_Item.m_Target, in context, out TargetFailure failure);
            if (camera == null)
                return Refuse(TargetResolver.Describe(failure));
            m_Orthographic = camera.orthographic;
            if (m_Orthographic)
                slot = direct ? Layers.CreatePrivate(camera, 0, 0, () => new CameraOrthographicSizeSlot(camera)) : Layers.GetOrCreate(camera, 0, 0, () => new CameraOrthographicSizeSlot(camera));
            else
                slot = direct ? Layers.CreatePrivate(camera, 0, 0, () => new CameraFieldOfViewSlot(camera)) : Layers.GetOrCreate(camera, 0, 0, () => new CameraFieldOfViewSlot(camera));
            return true;
        }

        internal static float Factor(float stops) => Mathf.Max(Mathf.Pow(2f, stops), MinFactor);

        // the property zoomed by k
        internal static float Zoomed(float value, float k, bool orthographic)
        {
            if (orthographic)
                return value / k;
            return 2f * Mathf.Rad2Deg * Mathf.Atan(Mathf.Tan(value * 0.5f * Mathf.Deg2Rad) / k);
        }

        // the k that takes origin to value
        static float FactorBetween(float origin, float value, bool orthographic)
        {
            if (orthographic)
                return value != 0f ? origin / value : 1f;
            float t = Mathf.Tan(value * 0.5f * Mathf.Deg2Rad);
            return t != 0f ? Mathf.Tan(origin * 0.5f * Mathf.Deg2Rad) / t : 1f;
        }

        float Reference => HasOrigin ? Origin : Slot != null ? Slot.Base : 0f;

        protected override Vector4 ToMotion(in float value)
            => new Vector4(Mathf.Log(Mathf.Max(FactorBetween(Reference, value, m_Orthographic), MinFactor), 2f), 0f, 0f, 0f);

        protected override float FromMotion(in Vector4 value) => Zoomed(Reference, Factor(value.x), m_Orthographic);

        protected override float MapOffset(in float offset)
        {
            float b = Slot.Base;
            return Zoomed(b, Factor(offset), m_Orthographic) - b;
        }
    }
}
