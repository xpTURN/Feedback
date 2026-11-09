using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace xpTURN.Feedback.Tests
{
    // the player's test items and their test binding — the EditMode and the PlayMode rows share them
    [Serializable]
    public sealed class PlayerTestAction : ActionItem
    {
        [SerializeField] internal float m_Length;

        internal override FeedbackCategory DeclaredCategory => FeedbackCategory.None;
        internal override float NaturalDuration => m_Length;
    }

    [Serializable]
    public sealed class PlayerTestTarget : TargetItem
    {
        static readonly TargetTraits s_Traits = new TargetTraits(valueKind: ValueKind.Float,
            supportedMotions: MotionMask.Set | MotionMask.Tween | MotionMask.Spring,
            category: FeedbackCategory.Motion, flashSensitive: false, applyMode: ApplyMode.Offset);

        internal override TargetTraits Traits => s_Traits;
    }

    // no test registers this one
    [Serializable]
    public sealed class PlayerTestUnregistered : ActionItem
    {
        internal override FeedbackCategory DeclaredCategory => FeedbackCategory.None;
        internal override float NaturalDuration => 0f;
    }

    // a second unregistered type — the warning is per type
    [Serializable]
    public sealed class PlayerTestUnregisteredToo : ActionItem
    {
        internal override FeedbackCategory DeclaredCategory => FeedbackCategory.None;
        internal override float NaturalDuration => 0f;
    }

    internal sealed class PlayerTestBinding : ItemBinding
    {
        internal static readonly List<PlayerTestBinding> Made = new List<PlayerTestBinding>();
        // by the item's label: what Prepare · Release do
        internal static readonly HashSet<string> Refuses = new HashSet<string>();
        internal static readonly HashSet<string> Throws = new HashSet<string>();
        internal static readonly HashSet<string> ThrowsInRelease = new HashSet<string>();
        // Prepare calls back into the player being prepared (once — the nested prepare does not): "rebuild…" calls Rebuild,
        // any other label EnsureRuntime
        internal static readonly HashSet<string> Reenters = new HashSet<string>();
        internal static readonly List<string> Reentered = new List<string>();
        static int s_Depth;

        // the playback calls in order — "label.Call…" (the event subscribers of a row add "|Kind")
        internal static readonly List<string> Log = new List<string>();
        // off for the allocation rows
        internal static bool Logging = true;
        // the record number after each call — tells two plays of one player apart
        internal static bool LogRecords;
        // "label.Begin" · "label.Evaluate" · "label.End" · "label.Execute" · "label.Settle" → throws
        internal static readonly HashSet<string> ThrowsIn = new HashSet<string>();
        // labels whose target is gone
        internal static readonly HashSet<string> Dead = new HashSet<string>();
        // user code inside a binding's call: (binding, call, record)
        internal static Action<PlayerTestBinding, string, int> Hook;
        // user code at the start of a Prepare
        internal static Action<PlayerTestBinding> OnPrepare;

        internal readonly EffectItem Item;
        internal int Prepared;
        internal int Released;
        internal PrepareContext Context;
        // what the "spawn" anchor of the context's player gave at the prepare
        internal UnityEngine.Object Anchor;
        internal BeginContext LastBegin;
        internal MotionTime LastTime;

        internal PlayerTestBinding(EffectItem item)
        {
            Item = item;
            Made.Add(this);
        }

        internal override bool Prepare(in PrepareContext context)
        {
            OnPrepare?.Invoke(this);
            Prepared++;
            Context = context;
            if (context.Player != null)
                context.Player.TryGetAnchor("spawn", out Anchor);
            if (Reenters.Contains(Item.m_Label) && s_Depth == 0)
            {
                s_Depth++;
                try
                {
                    Reentered.Add(Item.m_Label + ":" + (Item.m_Label.StartsWith("rebuild", StringComparison.Ordinal) ? context.Player.Rebuild() : context.Player.EnsureRuntime()));
                }
                finally
                {
                    s_Depth--;
                }
            }
            if (Throws.Contains(Item.m_Label))
                throw new InvalidOperationException("test prepare " + Item.m_Label);
            return !Refuses.Contains(Item.m_Label);
        }

        void Call(string what, int record, string detail)
        {
            if (!Logging)
                return;
            Log.Add(Item.m_Label + "." + what + detail + (LogRecords ? "@" + record : ""));
            Hook?.Invoke(this, what, record);
            if (ThrowsIn.Count > 0 && ThrowsIn.Contains(Item.m_Label + "." + what))
                throw new InvalidOperationException("test " + Item.m_Label + "." + what);
        }

        internal override void Begin(int record, in BeginContext context)
        {
            LastBegin = context;
            Call("Begin", record, Logging ? "#" + context.Occurrence : null);
        }

        internal override void Evaluate(int record, in MotionTime time)
        {
            LastTime = time;
            Call("Evaluate", record, Logging ? "(" + time.T.ToString("0.###", CultureInfo.InvariantCulture) + ")" : null);
        }

        internal override void End(int record, EndReason reason) => Call("End", record, Logging ? ":" + reason : null);

        internal override void Execute(int record, in BeginContext context)
        {
            LastBegin = context;
            Call("Execute", record, Logging ? "#" + context.Occurrence : null);
        }

        internal override void Settle(int record) => Call("Settle", record, null);

        internal override bool IsAlive => Dead.Count == 0 || !Dead.Contains(Item.m_Label);

        internal override void Release()
        {
            Released++;
            if (ThrowsInRelease.Contains(Item.m_Label))
                throw new InvalidOperationException("test release " + Item.m_Label);
        }

        // the registry is never cleared — registering again overwrites with the same factory
        internal static void Register()
        {
            FeedbackImplementations.Register<PlayerTestAction>(item => new PlayerTestBinding(item));
            FeedbackImplementations.Register<PlayerTestTarget>(item => new PlayerTestBinding(item));
        }

        internal static void Reset()
        {
            Made.Clear();
            Refuses.Clear();
            Throws.Clear();
            ThrowsInRelease.Clear();
            Reenters.Clear();
            Reentered.Clear();
            Log.Clear();
            Logging = true;
            LogRecords = false;
            ThrowsIn.Clear();
            Dead.Clear();
            Hook = null;
            OnPrepare = null;
        }
    }
}
