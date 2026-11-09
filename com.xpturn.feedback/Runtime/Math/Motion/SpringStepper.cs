namespace xpTURN.Feedback
{
    // the exact step and its cache, shared by the four public springs and MotionDriver's follow. The keys are the values
    // as given — NaN never matches, so it rebuilds at the domain's bottom.
    // The valid flag keeps a new stepper's zero keys from reusing its all-zero matrix (a jump to the target)
    internal struct SpringStepper
    {
        float _dt;
        float _frequency;
        float _damping;
        bool _valid;
        SpringCoeffs _matrix;

        internal void Prepare(float dt, float frequency, float damping)
        {
            if (_valid && dt == _dt && frequency == _frequency && damping == _damping)
                return;
            _matrix = SpringMath.Step(SpringShape.Of(frequency, damping), dt);
            _dt = dt;
            _frequency = frequency;
            _damping = damping;
            _valid = true;
        }

        // one component. Public fields take anything, and one NaN would stay for good: a non-finite target skips the step,
        // a non-finite value restarts at the target and a non-finite velocity at 0 (cut, never asserted)
        internal readonly void Advance(ref float value, ref float velocity, float target)
        {
            if (!IsFinite(target))
                return;
            if (!IsFinite(value))
                value = target;
            if (!IsFinite(velocity))
                velocity = 0f;
            float y = value - target;
            float v = velocity;
            value = target + _matrix.A11 * y + _matrix.A12 * v;
            velocity = _matrix.A21 * y + _matrix.A22 * v;
        }

        static bool IsFinite(float x) => x >= -float.MaxValue && x <= float.MaxValue;
    }
}
