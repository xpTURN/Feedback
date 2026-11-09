using System;
using UnityEngine;

namespace xpTURN.Feedback
{
    /// <summary>A SpringFloat for each component of a Color, not clamped — cut what leaves 0 to 1.</summary>
    [Serializable]
    public struct SpringColor
    {
        /// <summary>The current value.</summary>
        public Color Value;
        /// <summary>The current velocity.</summary>
        public Color Velocity;
        /// <summary>The value it moves toward.</summary>
        public Color Target;
        /// <summary>The natural frequency in hertz.</summary>
        [Min(0.001f)] public float Frequency;
        /// <summary>The damping ratio: below 1 it overshoots; from 1 a spring at rest settles without overshooting, though one already moving toward the target can pass it once.</summary>
        [Min(0.01f)] public float Damping;
        SpringStepper m_Stepper;

        /// <summary>A spring at rest at <paramref name="value"/>; new and default have Frequency and Damping 0, which barely moves.</summary>
        public SpringColor(Color value, float frequency = 6f, float damping = 0.4f)
        {
            Value = value;
            Velocity = default;
            Target = value;
            Frequency = frequency;
            Damping = damping;
            m_Stepper = default;
        }

        /// <summary>Advances the spring by <paramref name="dt"/> seconds.</summary>
        public void Step(float dt)
        {
            m_Stepper.Prepare(dt, Frequency, Damping);
            m_Stepper.Advance(ref Value.r, ref Velocity.r, Target.r);
            m_Stepper.Advance(ref Value.g, ref Velocity.g, Target.g);
            m_Stepper.Advance(ref Value.b, ref Velocity.b, Target.b);
            m_Stepper.Advance(ref Value.a, ref Velocity.a, Target.a);
        }

        /// <summary>Adds <paramref name="velocity"/> to the velocity — a kick.</summary>
        public void Bump(Color velocity) => Velocity += velocity;

        /// <summary>Sets the target and keeps the velocity.</summary>
        public void MoveTo(Color target) => Target = target;

        /// <summary>Jumps to the target and stops.</summary>
        public void Snap()
        {
            Value = Target;
            Velocity = default;
        }

        /// <summary>True when every component is within <paramref name="threshold"/> of the target and slower than it.</summary>
        public readonly bool IsSettled(float threshold)
            => Mathf.Abs(Value.r - Target.r) <= threshold && Mathf.Abs(Velocity.r) <= threshold
               && Mathf.Abs(Value.g - Target.g) <= threshold && Mathf.Abs(Velocity.g) <= threshold
               && Mathf.Abs(Value.b - Target.b) <= threshold && Mathf.Abs(Velocity.b) <= threshold
               && Mathf.Abs(Value.a - Target.a) <= threshold && Mathf.Abs(Velocity.a) <= threshold;
    }
}
