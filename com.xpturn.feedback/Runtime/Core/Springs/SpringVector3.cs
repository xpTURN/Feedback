using System;
using UnityEngine;

namespace xpTURN.Feedback
{
    // SpringFloat per component — one step matrix for all three
    /// <summary>A SpringFloat for each component of a Vector3.</summary>
    [Serializable]
    public struct SpringVector3
    {
        /// <summary>The current value.</summary>
        public Vector3 Value;
        /// <summary>The current velocity.</summary>
        public Vector3 Velocity;
        /// <summary>The value it moves toward.</summary>
        public Vector3 Target;
        /// <summary>The natural frequency in hertz.</summary>
        [Min(0.001f)] public float Frequency;
        /// <summary>The damping ratio: below 1 it overshoots; from 1 a spring at rest settles without overshooting, though one already moving toward the target can pass it once.</summary>
        [Min(0.01f)] public float Damping;
        SpringStepper m_Stepper;

        /// <summary>A spring at rest at <paramref name="value"/>; new and default have Frequency and Damping 0, which barely moves.</summary>
        public SpringVector3(Vector3 value, float frequency = 6f, float damping = 0.4f)
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
            m_Stepper.Advance(ref Value.x, ref Velocity.x, Target.x);
            m_Stepper.Advance(ref Value.y, ref Velocity.y, Target.y);
            m_Stepper.Advance(ref Value.z, ref Velocity.z, Target.z);
        }

        /// <summary>Adds <paramref name="velocity"/> to the velocity — a kick.</summary>
        public void Bump(Vector3 velocity) => Velocity += velocity;

        /// <summary>Sets the target and keeps the velocity.</summary>
        public void MoveTo(Vector3 target) => Target = target;

        /// <summary>Jumps to the target and stops.</summary>
        public void Snap()
        {
            Value = Target;
            Velocity = default;
        }

        /// <summary>True when every component is within <paramref name="threshold"/> of the target and slower than it.</summary>
        public readonly bool IsSettled(float threshold)
            => Mathf.Abs(Value.x - Target.x) <= threshold && Mathf.Abs(Velocity.x) <= threshold
               && Mathf.Abs(Value.y - Target.y) <= threshold && Mathf.Abs(Velocity.y) <= threshold
               && Mathf.Abs(Value.z - Target.z) <= threshold && Mathf.Abs(Velocity.z) <= threshold;
    }
}
