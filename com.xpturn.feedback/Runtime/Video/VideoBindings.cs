using UnityEngine;
using UnityEngine.Video;

namespace xpTURN.Feedback
{
    internal enum VideoPlayState
    {
        Stopped = 0,
        Playing = 1,
        Paused = 2,
    }

    // a Video Player's play state — a state, Persist only (Particle Control's slot form)
    [NotRecorded("no preview — the binding skips in a preview context")]
    internal sealed class VideoPlayStateSlot : LayerSlot
    {
        readonly VideoPlayer m_Target;
        int m_Reserved;

        internal VideoPlayStateSlot(VideoPlayer target)
            : base(ConsumptionStage.Update)
        {
            m_Target = target;
        }

        internal override bool IsAlive => m_Target != null;

        internal override bool Pending => false;

        internal override int LiveContributions => 0;

        internal override int Reserved => m_Reserved;

        internal override Vector4 BaseVector => Vector4.zero;

        internal void Reserve(int count) => m_Reserved += count;

        internal override void Unreserve(int count) => m_Reserved -= System.Math.Min(count, m_Reserved);

        internal override void Revert()
        {
        }

        internal override void Apply()
        {
        }

        internal override void Close()
        {
        }

        internal override void EndAll()
        {
        }

        internal VideoPlayState Read() => m_Target.isPlaying ? VideoPlayState.Playing : m_Target.isPaused ? VideoPlayState.Paused : VideoPlayState.Stopped;

        internal void Write(VideoPlayState state)
        {
            switch (state)
            {
                case VideoPlayState.Playing:
                    m_Target.Play();
                    break;
                case VideoPlayState.Paused:
                    m_Target.Pause();
                    break;
                default:
                    m_Target.Stop();
                    break;
            }
        }

        internal void Seek(float time)
        {
            if (m_Target.canSetTime)
                m_Target.time = time;
        }
    }

    // Play · Pause — the play's start state, the first (Restore), the origin (a backward play) · Seek is not undone
    // (Particle Control's Clear). A preview context skips it (no preview — Flow's form)
    internal sealed class VideoPlayerControlBinding : ItemBinding
    {
        readonly VideoPlayerControlItem m_Item;
        PropertyLayerRegistry m_Layers;
        VideoPlayStateSlot m_Slot;
        int m_Reserved;
        bool m_Preview;
        long[] m_Serials;
        bool[] m_HasStart;
        VideoPlayState[] m_Starts;
        VideoPlayState m_Origin;
        VideoPlayState m_First;
        bool m_HasOrigin;
        bool m_HasFirst;

        internal VideoPlayerControlBinding(VideoPlayerControlItem item)
        {
            m_Item = item;
        }

        internal VideoPlayStateSlot Slot => m_Slot;

        internal override bool Prepare(in PrepareContext context)
        {
            FeedbackMotion motion = m_Item.m_Motion;
            if (motion == null)
                return Refuse("no motion");
            if (!m_Item.Traits.Supports(motion.Kind))
            {
#if DEBUG
                return Refuse("the target does not take " + motion.Kind);
#else
                return Refuse(null);
#endif
            }
            VideoPlayer player = TargetResolver.Find<VideoPlayer>(in m_Item.m_Target, in context, out TargetFailure failure);
            if (player == null)
                return Refuse(TargetResolver.Describe(failure));
            m_Preview = context.Context.Kind == ContextKind.Preview;
            m_Layers = context.Context.Layers;
            m_Slot = m_Layers.GetOrCreate(player, 0, 0, () => new VideoPlayStateSlot(player));
            int n = context.MaxOverlap;
            m_Slot.Reserve(n);
            m_Reserved = n;
            m_Serials = new long[n];
            m_HasStart = new bool[n];
            m_Starts = new VideoPlayState[n];
            for (int i = 0; i < n; i++)
                m_Serials[i] = -1;
            return true;
        }

        bool Live => m_Slot != null && m_Slot.IsAlive && !m_Slot.Detached;

        internal override bool IsAlive => Live;

        internal override void Execute(int record, in BeginContext context)
        {
            if (m_Preview)
                return;
            bool fresh = m_Serials[record] != context.Serial || !m_HasStart[record];
            if (m_Serials[record] != context.Serial)
            {
                m_Serials[record] = context.Serial;
                m_HasStart[record] = false;
            }
            if (!Live)
                return;
            if (!m_HasStart[record])
            {
                m_Starts[record] = m_Slot.Read();
                m_HasStart[record] = true;
            }
            if (fresh)
            {
                if (!m_HasFirst)
                {
                    m_First = m_Starts[record];
                    m_HasFirst = true;
                }
                if (!context.Reverse)
                {
                    m_Origin = m_Starts[record];
                    m_HasOrigin = true;
                }
            }
            if (m_Item.m_Action == VideoAction.Seek)
            {
                if (!context.Reverse)
                    m_Slot.Seek(m_Item.m_Time);
                return;
            }
            if (context.Reverse)
            {
                if (m_HasOrigin)
                    m_Slot.Write(m_Origin);
                return;
            }
            m_Slot.Write(m_Item.m_Action == VideoAction.Play ? VideoPlayState.Playing : VideoPlayState.Paused);
        }

        internal override void Begin(int record, in BeginContext context)
        {
        }

        internal override void Evaluate(int record, in MotionTime time)
        {
        }

        internal override void End(int record, EndReason reason)
        {
            if (reason == EndReason.Revert && m_Item.m_Action != VideoAction.Seek && m_HasStart[record] && Live)
                m_Slot.Write(m_Starts[record]);
        }

        internal override void Restore()
        {
            if (m_HasFirst && m_Item.m_Action != VideoAction.Seek && Live)
                m_Slot.Write(m_First);
            m_HasOrigin = false;
            m_HasFirst = false;
        }

        internal override void Inherit(ItemBinding old)
        {
            if (!(old is VideoPlayerControlBinding from) || m_Slot == null || from.m_Slot == null || !m_Slot.Key.Equals(from.m_Slot.Key))
                return;
            m_Origin = from.m_Origin;
            m_HasOrigin = from.m_HasOrigin;
            m_First = from.m_First;
            m_HasFirst = from.m_HasFirst;
        }

        internal override void Release()
        {
            if (m_Slot == null)
                return;
            m_Layers.Release(m_Slot, m_Reserved);
            m_Slot = null;
        }
    }
}
