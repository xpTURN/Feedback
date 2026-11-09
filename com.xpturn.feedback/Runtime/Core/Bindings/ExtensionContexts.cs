using UnityEngine;

namespace xpTURN.Feedback
{
    /// <summary>Why a custom feedback's act ended; it differs from PlaybackEnd, which tells why a whole play ended.</summary>
    public enum CustomEnd
    {
        /// <summary>The act reached its end.</summary>
        Completed = 0,
        /// <summary>The play stopped and kept its values: Stop(Hold), the player turned off with Stop Hold, a later play the policy made room for, a canceled token, a Rebuild, a Restore, the player destroyed or released.</summary>
        Hold = 1,
        /// <summary>The play stopped and put its values back: Stop(Revert) or the player turned off with Stop Revert; an executed instant hears it too, and may hear it again.</summary>
        Revert = 2,
        /// <summary>The play jumped to its end: Stop(Complete), or a Skip of a play that cannot seek.</summary>
        Complete = 3,
        /// <summary>The act ended without its play: its target was destroyed, an item threw, or play mode ended.</summary>
        Aborted = 4,
    }

    // the binding that keeps what TryFindTarget found — its end ends the item
    internal interface IExtensionTargetSink
    {
        void Found(Object target);
    }

    /// <summary>What a custom feedback prepares against, once for each player that prepares it.</summary>
    public readonly struct CustomPrepare
    {
        readonly PrepareContext m_Context;
        readonly TargetRef m_Target;
        readonly IExtensionTargetSink m_Sink;

        internal CustomPrepare(in PrepareContext context, in TargetRef target, IExtensionTargetSink sink)
        {
            m_Context = context;
            m_Target = target;
            m_Sink = sink;
        }

        /// <summary>The GameObject that holds the player or the motion driver.</summary>
        public GameObject Owner => m_Context.Owner;

        /// <summary>The player that prepares the item; null in a motion driver.</summary>
        public FeedbackPlayer Player => m_Context.Player;

        /// <summary>How many plays of the item can overlap: the slots run 0 to Plays − 1.</summary>
        public int Plays => m_Context.MaxOverlap;

        /// <summary>True in the inspector's preview.</summary>
        public bool InPreview => m_Context.Context.Kind == ContextKind.Preview;

        /// <summary>Finds the item's target as the type T, a component type or an interface; the item ends when the found object is destroyed.</summary>
        public bool TryFindTarget<T>(out T target) where T : class
        {
            target = TargetResolver.FindTarget<T>(in m_Target, in m_Context, out _);
            if (target is Object found && found != null)
            {
                m_Sink?.Found(found);
                return true;
            }
            target = null;
            return false;
        }
    }

    /// <summary>One occurrence of a custom feedback as it starts or fires.</summary>
    public readonly struct CustomPlay
    {
        /// <summary>The occurrence's slot, 0 to Plays − 1: overlapping plays use different slots.</summary>
        public readonly int Slot;
        /// <summary>The play's intensity times the item's random output.</summary>
        public readonly float Intensity;
        /// <summary>The category's scale, 0 when the category is off; multiply your act's strength by it to follow the accessibility settings.</summary>
        public readonly float CategoryScale;
        /// <summary>The item's seed for this play, the same for the same play's seed.</summary>
        public readonly uint Seed;
        /// <summary>The occurrence's number within the play: repeats and loops count up.</summary>
        public readonly long Occurrence;
        /// <summary>True when the occurrence runs backward: a backward play, a backward Seek, or an item turned by its Play Direction.</summary>
        public readonly bool Reverse;
        /// <summary>True when a Seek or a Skip swept over the occurrence rather than a frame reaching it; a sound or a haptic usually returns.</summary>
        public readonly bool Swept;
        /// <summary>The occurrence's real length in seconds, its length divided by the player's speed; infinity when it has no end.</summary>
        public readonly double Length;
        /// <summary>True when the play has a place: Play's position, or the player's own once the player is gone.</summary>
        public readonly bool HasPosition;
        /// <summary>The play's place when HasPosition is true.</summary>
        public readonly Vector3 Position;

        internal CustomPlay(int slot, in BeginContext context, float scale)
        {
            Slot = slot;
            Intensity = context.Intensity;
            CategoryScale = scale;
            Seed = context.ItemSeed;
            Occurrence = context.Occurrence;
            Reverse = context.Reverse;
            Swept = context.Swept;
            Length = context.Seconds;
            HasPosition = context.HasPosition;
            Position = context.Position;
        }
    }

    /// <summary>A timed custom action's frame.</summary>
    public readonly struct CustomTick
    {
        /// <summary>The occurrence's slot, the one its Begin had.</summary>
        public readonly int Slot;
        /// <summary>Where the act is, 0 to 1; a backward play goes from 1 to 0.</summary>
        public readonly float T;
        /// <summary>Seconds into the occurrence on the timeline, the player's speed applied; an item turned by its Play Direction counts from its end.</summary>
        public readonly double Elapsed;
        /// <summary>The category's scale this frame, 0 once the category is turned off.</summary>
        public readonly float CategoryScale;

        internal CustomTick(int slot, in MotionTime time, float scale)
        {
            Slot = slot;
            T = time.T;
            Elapsed = time.Seconds;
            CategoryScale = scale;
        }
    }

    // a custom action's code, one object for each preparation — its state is its own (a preset's players share the item)
    /// <summary>The code of a custom action; override the calls you need, each does nothing by default.</summary>
    public abstract class CustomAction
    {
        /// <summary>Makes the action.</summary>
        protected CustomAction()
        {
        }

        /// <summary>Gets what the plays need; false leaves the item out of this player. Release follows even after false or an exception.</summary>
        protected internal virtual bool Prepare(in CustomPrepare context) => true;

        /// <summary>An instant's forward act; an exception is logged and the play goes on.</summary>
        protected internal virtual void Fire(in CustomPlay play)
        {
        }

        /// <summary>An instant's backward act, undoing what Fire did; it can come without a Fire (a backward Seek, an item turned by its Play Direction).</summary>
        protected internal virtual void Undo(in CustomPlay play)
        {
        }

        /// <summary>A timed act starts, backward too (Reverse); after an exception the slot gets no Update until its next Begin.</summary>
        protected internal virtual void Begin(in CustomPlay play)
        {
        }

        /// <summary>A timed act's frame, every frame its play runs.</summary>
        protected internal virtual void Update(in CustomTick tick)
        {
        }

        /// <summary>A timed act ends, or an executed instant is reverted; it comes after an exception too.</summary>
        protected internal virtual void End(int slot, CustomEnd end)
        {
        }

        /// <summary>The player's Restore, after its plays stopped with Hold.</summary>
        protected internal virtual void Restore()
        {
        }

        /// <summary>Gives back what Prepare got: the player is released, rebuilt or destroyed.</summary>
        protected internal virtual void Release()
        {
        }
    }

    // a custom effect's code, one object for each preparation
    /// <summary>The code of a custom effect: Apply takes the motion's value every frame its play runs.</summary>
    public abstract class CustomEffect
    {
        /// <summary>Makes the effect.</summary>
        protected CustomEffect()
        {
        }

        /// <summary>Gets what the plays need; false leaves the item out of this player. Release follows even after false or an exception.</summary>
        protected internal virtual bool Prepare(in CustomPrepare context) => true;

        /// <summary>An occurrence starts, before its first Apply.</summary>
        protected internal virtual void Begin(in CustomPlay play)
        {
        }

        /// <summary>The value for a slot: an offset motion's value sits on the item's base and takes the category's scale; slot −1 puts the base back for a Revert or a Restore.</summary>
        protected internal abstract void Apply(int slot, float value);

        /// <summary>An occurrence ends; it comes after an exception too.</summary>
        protected internal virtual void End(int slot, CustomEnd end)
        {
        }

        /// <summary>Gives back what Prepare got: the player is released, rebuilt or destroyed.</summary>
        protected internal virtual void Release()
        {
        }
    }
}
