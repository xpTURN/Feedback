using System;
using System.Collections.Generic;

namespace xpTURN.Feedback
{
    // the context of one scheduler — its kind, layers, frame number and services. Only the scheduler creates one, so two
    // schedulers never share a frame number or a service
    internal sealed class FeedbackContext
    {
        internal const string FactoryContextMessage = "A Feedback service factory returned a service of another context.";

        // key number → factory, for the domain's lifetime — never cleared, a registration overwrites
        static Func<FeedbackContext, FeedbackService>[] s_Factories = new Func<FeedbackContext, FeedbackService>[4];

        readonly List<FeedbackService> m_Services = new List<FeedbackService>(4);
        // key number → this context's instance, from its first GetExtension
        FeedbackService[] m_Extensions;
        bool m_IsShutdown;
        double m_Ticked;
#if !DEBUG
        bool m_LoggedException;
#endif

        internal FeedbackContext(ContextKind kind)
        {
            Kind = kind;
            Layers = new PropertyLayerRegistry(this);
        }

        internal ContextKind Kind { get; }

        internal PropertyLayerRegistry Layers { get; }

        // raised by RunRevert. Compared for equality only — it wraps
        internal int Frame { get; private set; }

        internal bool IsShutdown => m_IsShutdown;

        // the clock of the cooldowns, in seconds — the loop's kind reads the engine's unscaled time (fixed once a frame), a
        // test's or a preview's the sum of the unscaled dt it was ticked with. Unscaled whatever a player's time mode is
        internal double Now => Kind == ContextKind.Runtime ? UnityEngine.Time.unscaledTimeAsDouble : m_Ticked;

        internal void BeginFrame()
        {
            unchecked { Frame++; }
        }

        // ---- the settings, the categories and the two Core services — each made when first asked, so a context that
        // does not use them runs none. The services' close order is the order they were first asked

        SettingsValues m_Settings;
        CategoryGate m_Categories;
        TimeScaleService m_TimeScale;
        ChannelService m_Channels;

        internal SettingsValues Settings => m_Settings ??= SettingsValues.Load(Kind);

        // a test sets its own before the first use
        internal void UseSettings(SettingsValues values) => m_Settings = values;

        internal CategoryGate Categories => m_Categories ??= new CategoryGate(this, Settings);

        internal TimeScaleService TimeScale
        {
            get
            {
                if (m_TimeScale == null)
                {
                    m_TimeScale = new TimeScaleService(this, Settings);
                    AddService(m_TimeScale);
                }
                return m_TimeScale;
            }
        }

        internal ChannelService Channels
        {
            get
            {
                if (m_Channels == null)
                {
                    m_Channels = new ChannelService(this);
                    AddService(m_Channels);
                }
                return m_Channels;
            }
        }

        // the time service only if something made it — a preview's clock reads a hit stop's scale without making one
        internal TimeScaleService TimeScaleIfMade => m_TimeScale;

        // the gate and the channels only if something made them — the debugger reads without making a service or reading the
        // settings
        internal CategoryGate CategoriesIfMade => m_Categories;

        internal ChannelService ChannelsIfMade => m_Channels;

        // the pools — a Core service made when first asked (a spawn's prepare). The debugger and the preview's end read them
        // without making one
        PoolService m_Pools;

        internal PoolService Pools
        {
            get
            {
                if (m_Pools == null)
                {
                    m_Pools = new PoolService(this);
                    AddService(m_Pools);
                }
                return m_Pools;
            }
        }

        internal PoolService PoolsIfMade => m_Pools;

        // the global distance reference (FeedbackRange.Center): DistanceReference.Global measures from it
        internal UnityEngine.Transform RangeCenter { get; set; }

        // a preview does not invoke UnityEvents (a listener's scene write has no record): the count, and a log once per event
        // in this context — the set is made at the first skip
        internal const string UnityEventSkipped = "Feedback preview did not invoke a UnityEvent ({0}): {1} persistent listener(s), {2} for the editor too.";

        HashSet<UnityEngine.Events.UnityEventBase> m_SkippedEvents;

        internal int SkippedUnityEvents { get; private set; }

        // the signals a preview did not send
        internal int SkippedSignals;

        // a preview's custom feedbacks: the occurrences of the types it does not run · the preparations of the ones it runs (what
        // they change is not recorded)
        internal int SkippedCustom;
        internal int RanCustom;

        // a display-only item's marks — a preview's only, made at the first
        PreviewMarks m_Marks;

        internal PreviewMarks Marks => m_Marks;

        internal void Mark(PreviewMarkKind kind, UnityEngine.Vector3 origin, UnityEngine.Vector3 vector, float radius)
        {
            if (Kind != ContextKind.Preview)
                return;
            (m_Marks ??= new PreviewMarks()).Add(new PreviewMark { Kind = kind, Origin = origin, Vector = vector, Radius = radius, Until = Now + PreviewMarks.Seconds });
        }

        internal void SkipUnityEvent(UnityEngine.Events.UnityEventBase e, string what)
        {
            SkippedUnityEvents++;
            m_SkippedEvents ??= new HashSet<UnityEngine.Events.UnityEventBase>();
            if (!m_SkippedEvents.Add(e))
                return;
            int count = e.GetPersistentEventCount();
            int editor = 0;
            for (int i = 0; i < count; i++)
            {
                if (e.GetPersistentListenerState(i) == UnityEngine.Events.UnityEventCallState.EditorAndRuntime)
                    editor++;
            }
            UnityEngine.Debug.Log(string.Format(UnityEventSkipped, what, count, editor));
        }

        // the initialization path. A service added to a closed context is closed at once
        internal void AddService(FeedbackService service)
        {
            m_Services.Add(service);
            if (m_IsShutdown)
                Close(service);
        }

        // RunUpdate's services step. Up to the count on entry — a service added meanwhile starts next frame. A service that
        // throws is closed at once, so the global state it changed does not outlive it
        internal void UpdateServices(in FrameTime time)
        {
            m_Ticked += time.UnscaledDelta;
            int count = m_Services.Count;
            for (int i = 0; i < count; i++)
            {
                FeedbackService service = m_Services[i];
                if (service.IsClosed)
                    continue;
                try
                {
                    service.Update(time);
                }
                catch (Exception e)
                {
                    service.IsDisabled = true;
                    LogException(e);
                    // it may have closed the scheduler itself before throwing
                    if (!service.IsClosed)
                        Close(service);
                }
            }
        }

        // after the layers' LateApply — up to the count on entry
        internal void LateApplyServices()
        {
            int count = m_Services.Count;
            for (int i = 0; i < count; i++)
            {
                FeedbackService service = m_Services[i];
                if (service.IsClosed)
                    continue;
                try
                {
                    service.LateApply();
                }
                catch (Exception e)
                {
                    service.IsDisabled = true;
                    LogException(e);
                    if (!service.IsClosed)
                        Close(service);
                }
            }
        }

        // the services made — the debugger reads, never makes
        internal int ServiceCount => m_Services.Count;

        internal FeedbackService ServiceAt(int i) => m_Services[i];

        // called by the scheduler after its playbacks have ended. The layers put their written values back first. The last
        // added closes first: a dependency — added before the service whose factory asked for it — closes after it, and
        // services that wrote the same global value in turn put it back in reverse
        internal void Shutdown()
        {
            if (m_IsShutdown)
                return;
            m_IsShutdown = true;
            Layers.Shutdown();
            for (int i = m_Services.Count - 1; i >= 0; i--)
            {
                if (!m_Services[i].IsClosed)
                    Close(m_Services[i]);
            }
        }

        // ---- extension services: the optional assemblies register the factories; a service
        // type's static accessor casts what GetExtension returns

        // overwrites — a null factory makes the key absent. Instances already made stay
        internal static void RegisterExtension(ServiceKey key, Func<FeedbackContext, FeedbackService> factory)
        {
            int id = key.Id;
            if (id >= s_Factories.Length)
                Array.Resize(ref s_Factories, Math.Max(id + 1, s_Factories.Length * 2));
            s_Factories[id] = factory;
        }

        // one instance per context, made by the factory on the first call and added to the end of the services — closed at
        // once on a closed context. Null without a factory (the caller warns) or when the factory returns null. A factory's
        // exception goes to the caller: extensions are got at initialization
        internal FeedbackService GetExtension(ServiceKey key)
        {
            int id = key.Id;
            FeedbackService[] extensions = m_Extensions;
            if (extensions != null && id < extensions.Length && extensions[id] != null)
                return extensions[id];
            Func<FeedbackContext, FeedbackService> factory = id < s_Factories.Length ? s_Factories[id] : null;
            if (factory == null)
                return null;
            FeedbackService service = factory(this);
            if (service == null)
                return null;
            UnityEngine.Debug.Assert(service.Context == this, FactoryContextMessage);
            Keep(id, service);
            AddService(service);
            return service;
        }

        // reads the array again — a factory that asked for its dependency may have grown it
        void Keep(int id, FeedbackService service)
        {
            FeedbackService[] extensions = m_Extensions;
            if (extensions == null || id >= extensions.Length)
            {
                FeedbackService[] grown = new FeedbackService[Math.Max(id + 1, extensions == null ? 0 : extensions.Length * 2)];
                if (extensions != null)
                    Array.Copy(extensions, grown, extensions.Length);
                m_Extensions = extensions = grown;
            }
            extensions[id] = service;
        }

        void Close(FeedbackService service)
        {
            service.IsClosed = true;
            try
            {
                service.Shutdown();
            }
            catch (Exception e)
            {
                LogException(e);
            }
        }

        // every exception in development builds, the first of the session in release builds
        internal void LogException(Exception e)
        {
#if DEBUG
            UnityEngine.Debug.LogException(e);
#else
            if (m_LoggedException)
                return;
            m_LoggedException = true;
            UnityEngine.Debug.LogException(e);
#endif
        }
    }
}
