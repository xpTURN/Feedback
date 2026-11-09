using System;
using UnityEngine;

namespace xpTURN.Feedback
{
    // a spring for game code — the exact step, so any dt gives the same path, and MoveTo keeps the velocity. A mutable
    // struct: keep it in a field or an array element — through a List<T> indexer, a property, a readonly field, a foreach
    // variable or an in parameter, Step moves a copy. new and default have Frequency and Damping 0 (the domain's bottom — it
    // barely moves); the constructor gives the Spring motion's 6 Hz · 0.4
    /// <summary>A spring for game code: Step moves Value toward Target along the same path for any dt.</summary>
    [Serializable]
    public struct SpringFloat
    {
        /// <summary>The current value.</summary>
        public float Value;
        /// <summary>The current velocity.</summary>
        public float Velocity;
        /// <summary>The value it moves toward.</summary>
        public float Target;
        /// <summary>The natural frequency in hertz.</summary>
        [Min(0.001f)] public float Frequency;
        /// <summary>The damping ratio: below 1 it overshoots; from 1 a spring at rest settles without overshooting, though one already moving toward the target can pass it once.</summary>
        [Min(0.01f)] public float Damping;
        SpringStepper m_Stepper;

        /// <summary>A spring at rest at <paramref name="value"/>; new and default have Frequency and Damping 0, which barely moves.</summary>
        public SpringFloat(float value, float frequency = 6f, float damping = 0.4f)
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
            m_Stepper.Advance(ref Value, ref Velocity, Target);
        }

        /// <summary>Adds <paramref name="velocity"/> to the velocity — a kick.</summary>
        public void Bump(float velocity) => Velocity += velocity;

        /// <summary>Sets the target and keeps the velocity.</summary>
        public void MoveTo(float target) => Target = target;

        /// <summary>Jumps to the target and stops.</summary>
        public void Snap()
        {
            Value = Target;
            Velocity = default;
        }

        /// <summary>True when every component is within <paramref name="threshold"/> of the target and slower than it.</summary>
        public readonly bool IsSettled(float threshold)
            => Mathf.Abs(Value - Target) <= threshold && Mathf.Abs(Velocity) <= threshold;
    }
}
