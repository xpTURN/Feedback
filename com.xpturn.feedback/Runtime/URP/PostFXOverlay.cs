using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace xpTURN.Feedback
{
    // the second extension service — a context's pool of overlay volumes. Each PostFX item rents one at its preparation (a
    // component type's · a blend's) and its weight is the item's value. The volumes are made off — their layer and priority
    // taken while off — and a rent past the warmed count turns them all on and off once (the volume manager's lists grow at
    // the preparation). Runtime · test: visible (a runtime play's root is DontDestroyOnLoad — the play's exit destroys it);
    // preview: hidden with the preview prefix
    internal sealed class PostFXOverlayService : FeedbackService
    {
        internal static readonly ServiceKey Key = new ServiceKey();
        internal const float PriorityStep = 1f / 1024f;

        internal static PostFXOverlayService Of(FeedbackContext context) => context.GetExtension(Key) as PostFXOverlayService;

        // the cameras' scan — one array for every context, grown at a rent (a camera added after it grows it once in a play)
        static Camera[] s_Cameras = new Camera[8];

        readonly GameObject m_Root;
        readonly int m_Layer;
        readonly float m_Priority;
        readonly Dictionary<Type, List<Volume>> m_Resting = new Dictionary<Type, List<Volume>>();
        readonly List<Volume> m_RestingBlends = new List<Volume>(2);
        readonly List<Volume> m_Rented = new List<Volume>(8);
        readonly List<Object> m_Owned = new List<Object>(16);
        int m_Made;
        int m_Sequence;
        int m_Warmed;
        int m_On;
        bool m_WasOn;

        // observation
        internal GameObject Root => m_Root;
        internal int Made => m_Made;
        internal int RentedCount => m_Rented.Count;
        internal Volume RentedAt(int i) => m_Rented[i];
        internal int OnCount => m_On;
        internal int ViaScriptingUpdates;

        bool IsPreview => Context.Kind == ContextKind.Preview;

        string Prefix => IsPreview ? PropertyLayerRegistry.PreviewPrefix : PropertyLayerRegistry.RuntimePrefix;

        internal PostFXOverlayService(FeedbackContext context)
            : base(context)
        {
            m_Layer = context.Settings.PostFXLayer;
            m_Priority = context.Settings.PostFXPriority;
            m_Root = new GameObject(Prefix + "PostFXOverlay");
            m_Root.layer = m_Layer;
            if (IsPreview)
                m_Root.hideFlags = HideFlags.HideAndDontSave;
            else if (context.Kind == ContextKind.Runtime && Application.isPlaying)
                Object.DontDestroyOnLoad(m_Root);
        }

        void Own(Object o, string name)
        {
            o.name = Prefix + name;
            if (IsPreview)
                o.hideFlags = HideFlags.HideAndDontSave;
            m_Owned.Add(o);
        }

        Volume Make(bool profile)
        {
            var go = new GameObject(Prefix + "PostFX.Volume");
            go.layer = m_Layer;
            if (IsPreview)
                go.hideFlags = HideFlags.HideAndDontSave;
            go.transform.SetParent(m_Root.transform, false);
            go.SetActive(false);
            Volume v = go.AddComponent<Volume>();
            v.isGlobal = true;
            v.weight = 0f;
            v.enabled = false;
            go.SetActive(true);
            if (profile)
            {
                var p = ScriptableObject.CreateInstance<VolumeProfile>();
                Own(p, "PostFX.Profile");
                v.sharedProfile = p;
            }
            m_Made++;
            return v;
        }

        // a volume of one component type
        internal Volume Rent<T>() where T : VolumeComponent
        {
            Volume v = null;
            if (m_Resting.TryGetValue(typeof(T), out List<Volume> rest) && rest.Count > 0)
            {
                v = rest[rest.Count - 1];
                rest.RemoveAt(rest.Count - 1);
            }
            if (v == null)
            {
                v = Make(true);
                T c = v.sharedProfile.Add<T>(false);
                // after Add — it sets the component's flags and name
                Own(c, "PostFX." + typeof(T).Name);
            }
            Ready(v);
            return v;
        }

        // a volume holding the user's profile — sharedProfile (profile would copy it)
        internal Volume RentBlend(VolumeProfile profile)
        {
            Volume v;
            if (m_RestingBlends.Count > 0)
            {
                v = m_RestingBlends[m_RestingBlends.Count - 1];
                m_RestingBlends.RemoveAt(m_RestingBlends.Count - 1);
            }
            else
                v = Make(false);
            v.sharedProfile = profile;
            Ready(v);
            return v;
        }

        static void EnsureCameras()
        {
            if (Camera.allCamerasCount >= s_Cameras.Length)
                s_Cameras = new Camera[Mathf.NextPowerOfTwo(Camera.allCamerasCount + 1)];
        }

        // the priority while off — the order is the preparation's, a rest and a replay keep it
        void Ready(Volume v)
        {
            EnsureCameras();
            v.priority = m_Priority + m_Sequence++ * PriorityStep;
            m_Rented.Add(v);
            if (m_Rented.Count > m_Warmed)
            {
                for (int i = 0; i < m_Rented.Count; i++)
                    m_Rented[i].enabled = true;
                for (int i = 0; i < m_Rented.Count; i++)
                    m_Rented[i].enabled = m_Rented[i].weight > 0f;
                m_Warmed = Mathf.Max(4, Mathf.NextPowerOfTwo(m_Rented.Count));
            }
        }

        // back to rest: weight 0 (off) · the component's overrides off · a blend's profile let go
        internal void Return(Volume v)
        {
            if (v == null || !m_Rented.Remove(v))
                return;
            SetWeight(v, 0f);
            VolumeProfile p = v.sharedProfile;
            if (p != null && m_Owned.Contains(p))
            {
                VolumeComponent c = p.components[0];
                c.SetAllOverridesTo(false);
                if (!m_Resting.TryGetValue(c.GetType(), out List<Volume> rest))
                    m_Resting[c.GetType()] = rest = new List<Volume>(2);
                rest.Add(v);
            }
            else
            {
                v.sharedProfile = null;
                m_RestingBlends.Add(v);
            }
        }

        // 0 ~ 1 · off at 0 (an off volume costs the volume manager nothing)
        internal void SetWeight(Volume v, float w)
        {
            w = Mathf.Clamp01(w);
            bool on = w > 0f;
            if (v.enabled != on)
            {
                v.enabled = on;
                m_On += on ? 1 : -1;
            }
            v.weight = w;
        }

        // after the layers' LateApply: a Via Scripting camera's stack is updated while an overlay is on and the first frame after
        // (its stack keeps the last values otherwise)
        internal override void LateApply()
        {
            bool on = m_On > 0;
            if (on || m_WasOn)
                UpdateViaScripting();
            m_WasOn = on;
        }

        // the volume manager is up once a camera rendered — before that URP logs an error
        void UpdateViaScripting()
        {
            if (!VolumeManager.instance.isInitialized)
                return;
            EnsureCameras();
            int n = Camera.GetAllCameras(s_Cameras);
            for (int i = 0; i < n; i++)
            {
                Camera cam = s_Cameras[i];
                if (cam.TryGetComponent(out UniversalAdditionalCameraData data) && !data.requiresVolumeFrameworkUpdate)
                {
                    cam.UpdateVolumeStack(data);
                    ViaScriptingUpdates++;
                }
                s_Cameras[i] = null;
            }
        }

        internal override string DebugName => "PostFX overlay";

        internal override void Describe(List<ServiceRow> into)
        {
            into.Clear();
            into.Add(new ServiceRow("priority", m_Priority));
            into.Add(new ServiceRow("layer", m_Layer));
            into.Add(new ServiceRow("volumes", m_Made));
            into.Add(new ServiceRow("rented", m_Rented.Count));
            into.Add(new ServiceRow("on", m_On));
            into.Add(new ServiceRow("initialized", VolumeManager.instance.isInitialized ? 1f : 0f));
        }

        internal override void Shutdown()
        {
            bool shown = m_On > 0 || m_WasOn;
            m_Rented.Clear();
            m_Resting.Clear();
            m_RestingBlends.Clear();
            m_On = 0;
            // a profile leaves its components — each owned object goes
            for (int i = m_Owned.Count - 1; i >= 0; i--)
            {
                if (m_Owned[i] != null)
                    Object.DestroyImmediate(m_Owned[i]);
            }
            m_Owned.Clear();
            if (m_Root != null)
                Object.DestroyImmediate(m_Root);
            // closed while shown: a Via Scripting camera's stack would keep the overlay's last values
            if (shown)
                UpdateViaScripting();
        }
    }

    // an item's weight — its rented volume's (LateApply) · off at 0. Rented when the slot is made (the preparation), returned
    // when it is let go
    [NotRecorded("the PostFX overlay's own volume")]
    internal sealed class PostFXWeightSlot : ValueSlot<float, FloatOps>
    {
        readonly PostFXOverlayService m_Service;
        readonly Func<PostFXOverlayService, Volume> m_Rent;
        Volume m_Volume;

        internal PostFXWeightSlot(PostFXOverlayService service, Func<PostFXOverlayService, Volume> rent)
            : base(ConsumptionStage.LateApply, Composition.Additive, false)
        {
            m_Service = service;
            m_Rent = rent;
        }

        internal Volume Volume => m_Volume;

        internal override bool IsAlive => !m_Service.IsClosed;

        internal override void Attached() => m_Volume = m_Rent(m_Service);

        internal override void Dropped()
        {
            m_Service.Return(m_Volume);
            m_Volume = null;
        }

        protected override float Read() => m_Volume != null ? m_Volume.weight : 0f;

        protected override void Write(in float value)
        {
            if (m_Volume != null)
                m_Service.SetWeight(m_Volume, value);
        }
    }

    // a field into its parameter — the override box as chosen, the value through .value (its range kept)
    internal static class PostFXWrite
    {
        internal static void Set(VolumeParameter<float> p, in PostFXFloat f)
        {
            p.overrideState = f.m_Override;
            if (f.m_Override)
                p.value = f.m_Value;
        }

        internal static void Set(VolumeParameter<Color> p, in PostFXColor f)
        {
            p.overrideState = f.m_Override;
            if (f.m_Override)
                p.value = f.m_Value;
        }

        internal static void Set(VolumeParameter<Vector2> p, in PostFXVector2 f)
        {
            p.overrideState = f.m_Override;
            if (f.m_Override)
                p.value = f.m_Value;
        }

        internal static void Set(VolumeParameter<Vector4> p, in PostFXVector4 f)
        {
            p.overrideState = f.m_Override;
            if (f.m_Override)
                p.value = f.m_Value;
        }
    }

    // a PostFX item's component: the volume it rents · its fields into the component. Not a binding — the one sealed binding
    // takes each (a binding closes its generic base directly)
    internal abstract class PostFXTarget
    {
        internal abstract Volume Rent(PostFXOverlayService service);

        internal abstract void Apply(VolumeComponent component);

        internal virtual bool Valid => true;
    }

    // a PostFX item: the weight slot of a rented volume · the item's fields into its component at each start (an edit takes at the
    // next play)
    internal sealed class PostFXBinding : ValueTargetBinding<float, FloatOps>
    {
        readonly PostFXTarget m_Target;
        PostFXOverlayService m_Service;
        PostFXWeightSlot m_Weight;

        internal PostFXBinding(TargetItem item, PostFXTarget target)
            : base(item)
        {
            m_Target = target;
        }

        protected override bool ReadsTarget => false;

        protected override float TowardTarget => 1f;

        internal Volume Volume => m_Weight?.Volume;

        protected override bool Acquire(in PrepareContext context, bool direct, out ValueSlot<float, FloatOps> slot)
        {
            slot = null;
            if (!m_Target.Valid)
            {
#if DEBUG
                return Refuse("the profile is not a Volume Profile");
#else
                return Refuse(null);
#endif
            }
            PostFXOverlayService service = PostFXOverlayService.Of(context.Context);
            if (service == null)
            {
#if DEBUG
                return Refuse("no PostFX overlay service");
#else
                return Refuse(null);
#endif
            }
            m_Service = service;
            Func<PostFXOverlayService, Volume> rent = m_Target.Rent;
            m_Weight = Layers.CreatePrivate(null, 0, 0, () => new PostFXWeightSlot(service, rent));
            slot = m_Weight;
            return true;
        }

        protected override void Started(int record, in BeginContext context)
        {
            Volume v = m_Weight?.Volume;
            if (v == null || m_Service.IsClosed || v.sharedProfile == null || v.sharedProfile.components.Count == 0)
                return;
            m_Target.Apply(v.sharedProfile.components[0]);
        }
    }

    internal sealed class PostFXBloomTarget : PostFXTarget
    {
        readonly PostFXBloomItem m_I;
        internal PostFXBloomTarget(PostFXBloomItem item) => m_I = item;
        internal override Volume Rent(PostFXOverlayService s) => s.Rent<Bloom>();
        internal override void Apply(VolumeComponent component)
        {
            var c = (Bloom)component;
            PostFXWrite.Set(c.intensity, m_I.m_IntensityTarget);
            PostFXWrite.Set(c.threshold, m_I.m_Threshold);
            PostFXWrite.Set(c.scatter, m_I.m_Scatter);
            PostFXWrite.Set(c.tint, m_I.m_Tint);
        }
    }

    internal sealed class PostFXChromaticAberrationTarget : PostFXTarget
    {
        readonly PostFXChromaticAberrationItem m_I;
        internal PostFXChromaticAberrationTarget(PostFXChromaticAberrationItem item) => m_I = item;
        internal override Volume Rent(PostFXOverlayService s) => s.Rent<ChromaticAberration>();
        internal override void Apply(VolumeComponent component) => PostFXWrite.Set(((ChromaticAberration)component).intensity, m_I.m_IntensityTarget);
    }

    internal sealed class PostFXColorAdjustmentsTarget : PostFXTarget
    {
        readonly PostFXColorAdjustmentsItem m_I;
        internal PostFXColorAdjustmentsTarget(PostFXColorAdjustmentsItem item) => m_I = item;
        internal override Volume Rent(PostFXOverlayService s) => s.Rent<ColorAdjustments>();
        internal override void Apply(VolumeComponent component)
        {
            var c = (ColorAdjustments)component;
            PostFXWrite.Set(c.postExposure, m_I.m_PostExposure);
            PostFXWrite.Set(c.contrast, m_I.m_Contrast);
            PostFXWrite.Set(c.colorFilter, m_I.m_ColorFilter);
            PostFXWrite.Set(c.hueShift, m_I.m_HueShift);
            PostFXWrite.Set(c.saturation, m_I.m_Saturation);
        }
    }

    internal sealed class PostFXDepthOfFieldTarget : PostFXTarget
    {
        readonly PostFXDepthOfFieldItem m_I;
        internal PostFXDepthOfFieldTarget(PostFXDepthOfFieldItem item) => m_I = item;
        internal override Volume Rent(PostFXOverlayService s) => s.Rent<DepthOfField>();
        internal override void Apply(VolumeComponent component)
        {
            var c = (DepthOfField)component;
            c.mode.overrideState = m_I.m_OverrideMode;
            if (m_I.m_OverrideMode)
                c.mode.value = (DepthOfFieldMode)(int)m_I.m_Mode;
            PostFXWrite.Set(c.gaussianStart, m_I.m_GaussianStart);
            PostFXWrite.Set(c.gaussianEnd, m_I.m_GaussianEnd);
            PostFXWrite.Set(c.focusDistance, m_I.m_FocusDistance);
            PostFXWrite.Set(c.aperture, m_I.m_Aperture);
            PostFXWrite.Set(c.focalLength, m_I.m_FocalLength);
        }
    }

    internal sealed class PostFXFilmGrainTarget : PostFXTarget
    {
        readonly PostFXFilmGrainItem m_I;
        internal PostFXFilmGrainTarget(PostFXFilmGrainItem item) => m_I = item;
        internal override Volume Rent(PostFXOverlayService s) => s.Rent<FilmGrain>();
        internal override void Apply(VolumeComponent component)
        {
            var c = (FilmGrain)component;
            PostFXWrite.Set(c.intensity, m_I.m_IntensityTarget);
            PostFXWrite.Set(c.response, m_I.m_Response);
        }
    }

    internal sealed class PostFXLensDistortionTarget : PostFXTarget
    {
        readonly PostFXLensDistortionItem m_I;
        internal PostFXLensDistortionTarget(PostFXLensDistortionItem item) => m_I = item;
        internal override Volume Rent(PostFXOverlayService s) => s.Rent<LensDistortion>();
        internal override void Apply(VolumeComponent component)
        {
            var c = (LensDistortion)component;
            PostFXWrite.Set(c.intensity, m_I.m_IntensityTarget);
            PostFXWrite.Set(c.scale, m_I.m_Scale);
            PostFXWrite.Set(c.center, m_I.m_Center);
        }
    }

    internal sealed class PostFXMotionBlurTarget : PostFXTarget
    {
        readonly PostFXMotionBlurItem m_I;
        internal PostFXMotionBlurTarget(PostFXMotionBlurItem item) => m_I = item;
        internal override Volume Rent(PostFXOverlayService s) => s.Rent<MotionBlur>();
        internal override void Apply(VolumeComponent component)
        {
            var c = (MotionBlur)component;
            PostFXWrite.Set(c.intensity, m_I.m_IntensityTarget);
            PostFXWrite.Set(c.clamp, m_I.m_Clamp);
        }
    }

    internal sealed class PostFXPaniniProjectionTarget : PostFXTarget
    {
        readonly PostFXPaniniProjectionItem m_I;
        internal PostFXPaniniProjectionTarget(PostFXPaniniProjectionItem item) => m_I = item;
        internal override Volume Rent(PostFXOverlayService s) => s.Rent<PaniniProjection>();
        internal override void Apply(VolumeComponent component)
        {
            var c = (PaniniProjection)component;
            PostFXWrite.Set(c.distance, m_I.m_Distance);
            PostFXWrite.Set(c.cropToFit, m_I.m_CropToFit);
        }
    }

    internal sealed class PostFXVignetteTarget : PostFXTarget
    {
        readonly PostFXVignetteItem m_I;
        internal PostFXVignetteTarget(PostFXVignetteItem item) => m_I = item;
        internal override Volume Rent(PostFXOverlayService s) => s.Rent<Vignette>();
        internal override void Apply(VolumeComponent component)
        {
            var c = (Vignette)component;
            PostFXWrite.Set(c.intensity, m_I.m_IntensityTarget);
            PostFXWrite.Set(c.smoothness, m_I.m_Smoothness);
            PostFXWrite.Set(c.color, m_I.m_Color);
            PostFXWrite.Set(c.center, m_I.m_Center);
            c.rounded.overrideState = m_I.m_OverrideRounded;
            if (m_I.m_OverrideRounded)
                c.rounded.value = m_I.m_Rounded;
        }
    }

    internal sealed class PostFXWhiteBalanceTarget : PostFXTarget
    {
        readonly PostFXWhiteBalanceItem m_I;
        internal PostFXWhiteBalanceTarget(PostFXWhiteBalanceItem item) => m_I = item;
        internal override Volume Rent(PostFXOverlayService s) => s.Rent<WhiteBalance>();
        internal override void Apply(VolumeComponent component)
        {
            var c = (WhiteBalance)component;
            PostFXWrite.Set(c.temperature, m_I.m_Temperature);
            PostFXWrite.Set(c.tint, m_I.m_Tint);
        }
    }

    internal sealed class PostFXChannelMixerTarget : PostFXTarget
    {
        readonly PostFXChannelMixerItem m_I;
        internal PostFXChannelMixerTarget(PostFXChannelMixerItem item) => m_I = item;
        internal override Volume Rent(PostFXOverlayService s) => s.Rent<ChannelMixer>();
        internal override void Apply(VolumeComponent component)
        {
            var c = (ChannelMixer)component;
            PostFXWrite.Set(c.redOutRedIn, m_I.m_RedOutRedIn);
            PostFXWrite.Set(c.redOutGreenIn, m_I.m_RedOutGreenIn);
            PostFXWrite.Set(c.redOutBlueIn, m_I.m_RedOutBlueIn);
            PostFXWrite.Set(c.greenOutRedIn, m_I.m_GreenOutRedIn);
            PostFXWrite.Set(c.greenOutGreenIn, m_I.m_GreenOutGreenIn);
            PostFXWrite.Set(c.greenOutBlueIn, m_I.m_GreenOutBlueIn);
            PostFXWrite.Set(c.blueOutRedIn, m_I.m_BlueOutRedIn);
            PostFXWrite.Set(c.blueOutGreenIn, m_I.m_BlueOutGreenIn);
            PostFXWrite.Set(c.blueOutBlueIn, m_I.m_BlueOutBlueIn);
        }
    }

    internal sealed class PostFXSplitToningTarget : PostFXTarget
    {
        readonly PostFXSplitToningItem m_I;
        internal PostFXSplitToningTarget(PostFXSplitToningItem item) => m_I = item;
        internal override Volume Rent(PostFXOverlayService s) => s.Rent<SplitToning>();
        internal override void Apply(VolumeComponent component)
        {
            var c = (SplitToning)component;
            PostFXWrite.Set(c.shadows, m_I.m_Shadows);
            PostFXWrite.Set(c.highlights, m_I.m_Highlights);
            PostFXWrite.Set(c.balance, m_I.m_Balance);
        }
    }

    internal sealed class PostFXLiftGammaGainTarget : PostFXTarget
    {
        readonly PostFXLiftGammaGainItem m_I;
        internal PostFXLiftGammaGainTarget(PostFXLiftGammaGainItem item) => m_I = item;
        internal override Volume Rent(PostFXOverlayService s) => s.Rent<LiftGammaGain>();
        internal override void Apply(VolumeComponent component)
        {
            var c = (LiftGammaGain)component;
            PostFXWrite.Set(c.lift, m_I.m_Lift);
            PostFXWrite.Set(c.gamma, m_I.m_Gamma);
            PostFXWrite.Set(c.gain, m_I.m_Gain);
        }
    }

    internal sealed class PostFXShadowsMidtonesHighlightsTarget : PostFXTarget
    {
        readonly PostFXShadowsMidtonesHighlightsItem m_I;
        internal PostFXShadowsMidtonesHighlightsTarget(PostFXShadowsMidtonesHighlightsItem item) => m_I = item;
        internal override Volume Rent(PostFXOverlayService s) => s.Rent<ShadowsMidtonesHighlights>();
        internal override void Apply(VolumeComponent component)
        {
            var c = (ShadowsMidtonesHighlights)component;
            PostFXWrite.Set(c.shadows, m_I.m_Shadows);
            PostFXWrite.Set(c.midtones, m_I.m_Midtones);
            PostFXWrite.Set(c.highlights, m_I.m_Highlights);
            PostFXWrite.Set(c.shadowsStart, m_I.m_ShadowsStart);
            PostFXWrite.Set(c.shadowsEnd, m_I.m_ShadowsEnd);
            PostFXWrite.Set(c.highlightsStart, m_I.m_HighlightsStart);
            PostFXWrite.Set(c.highlightsEnd, m_I.m_HighlightsEnd);
        }
    }

    // the user's profile asset on the rented volume — nothing written into it
    internal sealed class PostFXVolumeBlendTarget : PostFXTarget
    {
        readonly PostFXVolumeBlendItem m_I;
        internal PostFXVolumeBlendTarget(PostFXVolumeBlendItem item) => m_I = item;
        internal override bool Valid => m_I.m_Profile is VolumeProfile;
        internal override Volume Rent(PostFXOverlayService s) => s.RentBlend((VolumeProfile)m_I.m_Profile);
        internal override void Apply(VolumeComponent component)
        {
        }
    }

    // a user volume's weight — Light 2D's form
    [RecordPaths(typeof(Volume), "weight")]
    internal sealed class PostFXVolumeWeightSlot : ValueSlot<float, FloatOps>
    {
        readonly Volume m_Target;

        internal PostFXVolumeWeightSlot(Volume target)
            : base(ConsumptionStage.LateApply, Composition.Additive, false)
        {
            m_Target = target;
        }

        internal override bool IsAlive => m_Target != null;

        protected override float Read() => m_Target.weight;

        protected override void Write(in float value) => m_Target.weight = Mathf.Clamp01(value);
    }

    internal sealed class PostFXVolumeWeightBinding : ValueTargetBinding<float, FloatOps>
    {
        internal PostFXVolumeWeightBinding(PostFXVolumeWeightItem item)
            : base(item)
        {
        }

        protected override bool Acquire(in PrepareContext context, bool direct, out ValueSlot<float, FloatOps> slot)
        {
            slot = null;
            Volume volume = TargetResolver.Find<Volume>(in m_Item.m_Target, in context, out TargetFailure failure);
            if (volume == null)
                return Refuse(TargetResolver.Describe(failure));
            if (direct)
                slot = Layers.CreatePrivate(volume, 0, 0, () => new PostFXVolumeWeightSlot(volume));
            else
                slot = Layers.GetOrCreate(volume, 0, 0, () => new PostFXVolumeWeightSlot(volume));
            return true;
        }
    }
}
