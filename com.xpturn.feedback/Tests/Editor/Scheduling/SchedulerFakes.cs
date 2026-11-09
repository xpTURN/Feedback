using System;
using System.Collections.Generic;

namespace xpTURN.Feedback.Tests.Editor
{
    // the fakes record what the scheduler did and never log — the tests count warnings with
    // LogAssert.NoUnexpectedReceived, which counts info logs too
    internal sealed class CallRecord
    {
        readonly List<string> m_Items = new List<string>();

        internal void Add(string item) => m_Items.Add(item);

        internal string Joined => string.Join(",", m_Items);
    }

    internal sealed class FakeRunner : IPlaybackRunner
    {
        readonly CallRecord m_Record;
        readonly string m_Name;

        internal int Advances;
        internal int Aborts;
        internal AbortReason LastReason;
        internal Func<int, bool> OnAdvance;
        internal Action<int, AbortReason> OnAbort;
        internal UnityEngine.Object Owner;
        // the last frame's deltas and the engine's in the same call
        internal FrameTime LastTime;
        internal float EngineScaled;
        internal float EngineUnscaled;

        internal FakeRunner(string name, CallRecord record = null)
        {
            m_Name = name;
            m_Record = record;
        }

        public bool Advance(int state, in FrameTime time)
        {
            Advances++;
            LastTime = time;
            EngineScaled = UnityEngine.Time.deltaTime;
            EngineUnscaled = UnityEngine.Time.unscaledDeltaTime;
            m_Record?.Add(m_Name + ".Advance");
            return OnAdvance == null || OnAdvance(state);
        }

        public void Abort(int state, AbortReason reason)
        {
            Aborts++;
            LastReason = reason;
            m_Record?.Add(m_Name + ".Abort");
            OnAbort?.Invoke(state, reason);
        }

        public UnityEngine.Object DebugOwner => Owner;
    }

    // allocation-free — for SchedulerAllocationTests
    internal sealed class CountingRunner : IPlaybackRunner
    {
        internal int Advances;

        public bool Advance(int state, in FrameTime time)
        {
            Advances++;
            return true;
        }

        public void Abort(int state, AbortReason reason)
        {
        }

        public UnityEngine.Object DebugOwner => null;
    }

    internal sealed class RecordingLayers : ILayerStages
    {
        readonly CallRecord m_Record;

        internal int Reverts;
        internal int UpdateApplies;
        internal int LateApplies;
        internal bool ThrowOnRevert;

        internal RecordingLayers(CallRecord record = null)
        {
            m_Record = record;
        }

        public void Revert()
        {
            Reverts++;
            m_Record?.Add("Revert");
            if (ThrowOnRevert)
                throw new InvalidOperationException("test layer revert");
        }

        public void Apply(ConsumptionStage stage)
        {
            if (stage == ConsumptionStage.Update)
            {
                UpdateApplies++;
                m_Record?.Add("Apply(Update)");
            }
            else
            {
                LateApplies++;
                m_Record?.Add("Apply(LateApply)");
            }
        }
    }

    internal sealed class TestService : FeedbackService
    {
        readonly CallRecord m_Record;
        readonly string m_Name;

        internal int Updates;
        internal int Shutdowns;
        internal bool ThrowOnUpdate;
        internal bool ThrowOnShutdown;
        internal Action OnUpdate;

        internal TestService(FeedbackContext context, string name, CallRecord record = null)
            : base(context)
        {
            m_Name = name;
            m_Record = record;
        }

        internal string Name => m_Name;

        // a request API — false when refused
        internal bool Request() => !Refuses();

        internal override void Update(in FrameTime time)
        {
            Updates++;
            m_Record?.Add(m_Name + ".Update");
            OnUpdate?.Invoke();
            if (ThrowOnUpdate)
                throw new InvalidOperationException("test service update " + m_Name);
        }

        internal override void Shutdown()
        {
            Shutdowns++;
            m_Record?.Add(m_Name + ".Shutdown");
            if (ThrowOnShutdown)
                throw new InvalidOperationException("test service shutdown " + m_Name);
        }
    }

    // the shape of a real service's global state — the three time values, a shader global, a hidden object. Applied in
    // play only: an edit-mode time write changes the project settings
    internal sealed class GlobalStateService : FeedbackService
    {
        internal const string HiddenName = "xpTURN.Feedback.TestHidden";
        internal static readonly int GlobalId = UnityEngine.Shader.PropertyToID("_xpTURNFeedbackTestGlobal");
        internal const float AppliedTimeScale = 0.5f;
        // above the entry maximumDeltaTime, so a restore in the wrong order shows
        internal const float AppliedFixedDelta = 0.4f;
        internal const float AppliedMaximumDelta = 0.5f;
        internal const float AppliedGlobal = 7f;

        bool m_Applied;
        float m_TimeScale;
        float m_FixedDelta;
        float m_MaximumDelta;
        float m_Global;
        UnityEngine.GameObject m_Hidden;

        internal GlobalStateService(FeedbackContext context)
            : base(context)
        {
        }

        // the entry values on the first call — a service never applied has nothing to put back
        internal bool Apply()
        {
            if (Refuses())
                return false;
            if (!m_Applied)
            {
                m_Applied = true;
                m_TimeScale = UnityEngine.Time.timeScale;
                m_FixedDelta = UnityEngine.Time.fixedDeltaTime;
                m_MaximumDelta = UnityEngine.Time.maximumDeltaTime;
                m_Global = UnityEngine.Shader.GetGlobalFloat(GlobalId);
            }
            UnityEngine.Time.timeScale = AppliedTimeScale;
            UnityEngine.Time.maximumDeltaTime = AppliedMaximumDelta;
            UnityEngine.Time.fixedDeltaTime = AppliedFixedDelta;
            UnityEngine.Shader.SetGlobalFloat(GlobalId, AppliedGlobal);
            if (m_Hidden == null)
                m_Hidden = new UnityEngine.GameObject(HiddenName) { hideFlags = UnityEngine.HideFlags.HideAndDontSave };
            return true;
        }

        // fixedDeltaTime first — maximumDeltaTime written under a larger fixedDeltaTime rises to it. Each time value only
        // when it differs: a write back floors fixedDeltaTime a step, and in edit mode (the backup close, an edit-mode
        // context) any write changes the project settings
        internal override void Shutdown()
        {
            if (!m_Applied)
                return;
            if (UnityEngine.Time.fixedDeltaTime != m_FixedDelta)
                UnityEngine.Time.fixedDeltaTime = m_FixedDelta;
            if (UnityEngine.Time.maximumDeltaTime != m_MaximumDelta)
                UnityEngine.Time.maximumDeltaTime = m_MaximumDelta;
            if (UnityEngine.Time.timeScale != m_TimeScale)
                UnityEngine.Time.timeScale = m_TimeScale;
            UnityEngine.Shader.SetGlobalFloat(GlobalId, m_Global);
            if (m_Hidden != null)
                UnityEngine.Object.DestroyImmediate(m_Hidden);
        }

        internal static int HiddenCount()
        {
            int count = 0;
            foreach (UnityEngine.GameObject o in UnityEngine.Resources.FindObjectsOfTypeAll<UnityEngine.GameObject>())
                if (o.name == HiddenName)
                    count++;
            return count;
        }

        internal static void DestroyHidden()
        {
            foreach (UnityEngine.GameObject o in UnityEngine.Resources.FindObjectsOfTypeAll<UnityEngine.GameObject>())
                if (o.name == HiddenName)
                    UnityEngine.Object.DestroyImmediate(o);
        }
    }

    // a play-entry test must not change the project settings. A Time.timeScale write in edit mode dirties TimeManager (the
    // file is rewritten when the run ends); fixedDeltaTime · maximumDeltaTime writes change it without the flag (saved with
    // the next save). Taken at SetUp, checked at the end of TearDown — both in edit mode
    internal static class ProjectSettingsCheck
    {
        internal const string DirtiedMessage = "The test left ProjectSettings/TimeManager.asset dirty (a Time.timeScale write in edit mode).";
        internal const string ChangedMessage = "The test changed Time.fixedDeltaTime or Time.maximumDeltaTime in edit mode (kept in the project settings without the dirty flag).";

        internal struct State
        {
            internal bool Dirty;
            internal int FixedDeltaBits;
            internal int MaximumDeltaBits;
        }

        static UnityEngine.Object TimeManager() => UnityEditor.AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TimeManager.asset")[0];

        internal static bool TimeManagerDirty() => UnityEditor.EditorUtility.IsDirty(TimeManager());

        internal static State Take() => new State
        {
            Dirty = TimeManagerDirty(),
            FixedDeltaBits = BitConverter.SingleToInt32Bits(UnityEngine.Time.fixedDeltaTime),
            MaximumDeltaBits = BitConverter.SingleToInt32Bits(UnityEngine.Time.maximumDeltaTime),
        };

        // a new flag is cleared before failing — left, the file is written when the run ends and the tests after this one
        // start dirty. The values are compared too: ClearDirty does not put them back
        internal static void FailIfChanged(State before)
        {
            if (!before.Dirty && TimeManagerDirty())
            {
                UnityEditor.EditorUtility.ClearDirty(TimeManager());
                NUnit.Framework.Assert.Fail(DirtiedMessage);
            }
            if (BitConverter.SingleToInt32Bits(UnityEngine.Time.fixedDeltaTime) != before.FixedDeltaBits
                || BitConverter.SingleToInt32Bits(UnityEngine.Time.maximumDeltaTime) != before.MaximumDeltaBits)
                NUnit.Framework.Assert.Fail(ChangedMessage);
        }
    }
}
