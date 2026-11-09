using System;

namespace xpTURN.Feedback
{
    // saved in category overrides and indexes the gate's arrays — add members at the end only
    /// <summary>The category an effect belongs to — its item's override first, then Flash for a flash, then the feedback's own.</summary>
    public enum FeedbackCategory
    {
        /// <summary>In no category: never turned off or scaled.</summary>
        None = 0,
        /// <summary>Moving, turning and scaling objects.</summary>
        Motion = 1,
        /// <summary>Camera moves, shakes and lens changes.</summary>
        Camera = 2,
        /// <summary>Flashes and blinks of a flash-sensitive target; the flash limit counts them.</summary>
        Flash = 3,
        /// <summary>Post-processing effects.</summary>
        PostFX = 4,
        /// <summary>Hit stops and time scale changes.</summary>
        Time = 5,
        /// <summary>Particle effects; an item's category override puts an effect here.</summary>
        Particles = 6,
        /// <summary>UI effects; an item's category override puts an effect here.</summary>
        UI = 7,
        /// <summary>Gamepad rumble, and your own haptic feedbacks.</summary>
        Haptics = 8,
    }

    // the closed motion set. Not saved — MotionMask uses 1 << value
    internal enum MotionKind
    {
        Set = 0,
        Tween = 1,
        Punch = 2,
        Shake = 3,
        Curve = 4,
        Flash = 5,
        Gradient = 6,
        Oscillate = 7,
        Blink = 8,
        Wiggle = 9,
        Spring = 10,
    }

    // not saved. Rotation targets are Vector3 (euler)
    internal enum ValueKind
    {
        Float = 0,
        Vector2 = 1,
        Vector3 = 2,
        Vector4 = 3,
        Color = 4,
        Bool = 5,
    }

    // one bit per MotionKind, 1 << value
    [Flags]
    internal enum MotionMask
    {
        None = 0,
        Set = 1 << 0,
        Tween = 1 << 1,
        Punch = 1 << 2,
        Shake = 1 << 3,
        Curve = 1 << 4,
        Flash = 1 << 5,
        Gradient = 1 << 6,
        Oscillate = 1 << 7,
        Blink = 1 << 8,
        Wiggle = 1 << 9,
        Spring = 1 << 10,
    }

    internal enum ApplyMode
    {
        Offset = 0,
        WeightTowardTarget = 1,
    }

    // immutable — a concrete target returns one static readonly instance
    internal sealed class TargetTraits
    {
        internal readonly ValueKind ValueKind;
        internal readonly MotionMask SupportedMotions;
        internal readonly FeedbackCategory Category;
        internal readonly bool FlashSensitive;
        internal readonly ApplyMode ApplyMode;

        internal TargetTraits(ValueKind valueKind, MotionMask supportedMotions, FeedbackCategory category, bool flashSensitive, ApplyMode applyMode)
        {
            ValueKind = valueKind;
            SupportedMotions = supportedMotions;
            Category = category;
            FlashSensitive = flashSensitive;
            ApplyMode = applyMode;
        }

        // bit test, not Enum.HasFlag — that boxes on Mono · IL2CPP
        internal bool Supports(MotionKind kind) => (SupportedMotions & (MotionMask)(1 << (int)kind)) != 0;
    }
}
