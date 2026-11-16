using System;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace xpTURN.Feedback.Tests.Editor
{
    // end values · intensity · one evaluator per item · components · dimensions · Set · the factory · wiring · non-finite ·
    // Shortest · Flash
    public sealed class MotionEvaluatorTests
    {
        internal static readonly Vector4 A = new Vector4(1f, 2f, 3f, 4f);

        // the item's seconds — noise seconds off the lattice
        internal const float Item = 1.37f;

        internal static MotionState Begin(MotionEvaluator e, Vector4 current, float intensity = 1f, uint seed = 12345u, bool endless = false)
        {
            var s = new MotionState();
            var args = new MotionBegin { Seed = seed, Intensity = intensity, Endless = endless };
            e.Begin(ref s, current, args);
            return s;
        }

        internal static MotionSample Eval(MotionEvaluator e, in MotionState s, float t, double seconds)
        {
            e.Evaluate(s, new MotionTime(t, seconds), out MotionSample sample);
            return sample;
        }

        internal static MotionSample At(MotionEvaluator e, in MotionState s, float t) => Eval(e, s, t, t * Item);

        internal static Gradient Colors()
        {
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(new Color(0.2f, 0.4f, 0.6f), 0f), new GradientColorKey(new Color(0.9f, 0.1f, 0.5f), 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.5f, 1f) });
            return g;
        }

        internal static TweenParams Tween(TweenMode mode, EaseType ease = EaseType.QuadOut) => new TweenParams
        {
            m_Mode = mode, m_From = new Vector4(-2f, -3f, 1f, 5f), m_To = new Vector4(5f, 6f, 7f, -1f), m_Ease = Ease.Of(ease), m_Loops = 1,
        };

        internal static SpringParams Spring(SpringMode mode, float damping = 0.4f, bool relative = false) => new SpringParams
        {
            m_Mode = mode, m_Value = A * 3f, m_Relative = relative, m_Frequency = 4f, m_Damping = damping, m_Settle = 0.001f,
        };

        static PunchParams Punch(Vector4 amplitude) => new PunchParams { m_Amplitude = amplitude, m_Vibrato = 3f, m_Decay = 2f };
        static FlashParams Flash() => new FlashParams { m_Target = A, m_Attack = 0.2f, m_Hold = 0.3f, m_Release = 0.3f };
        static BlinkParams Blink() => new BlinkParams { m_Value = A, m_Period = 0.2f, m_Duty = 0.5f, m_StartOn = true };

        // the twelve — Spring in both modes, Gradient in both writes
        internal static MotionEvaluator[] All(int d) => new MotionEvaluator[]
        {
            new SetEvaluator(new SetParams { m_Value = A, m_Relative = true }, d),
            new TweenEvaluator(Tween(TweenMode.To), d),
            new PunchEvaluator(Punch(A), d),
            new ShakeEvaluator(new ShakeParams { m_Amplitude = A, m_Frequency = 7f }, d),
            new CurveEvaluator(new CurveParams { m_Curve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f), m_RemapZero = -A, m_RemapOne = A }, d),
            new FlashEvaluator(Flash(), d),
            new GradientEvaluator(new GradientParams { m_Gradient = Colors(), m_Mode = GradientWrite.Persist }, d),
            new GradientEvaluator(new GradientParams { m_Gradient = Colors(), m_Mode = GradientWrite.Override }, d),
            new OscillateEvaluator(new OscillateParams { m_Amplitude = A, m_Frequency = 2f, m_Phase = 0.1f }, d),
            new BlinkEvaluator(Blink(), d),
            new WiggleEvaluator(new WiggleParams { m_Amplitude = A, m_Frequency = 3f, m_Octaves = 2 }, d),
            new SpringEvaluator(Spring(SpringMode.MoveTo), d),
            new SpringEvaluator(Spring(SpringMode.Bump), d),
            new BounceEvaluator(new BounceParams { m_Amplitude = A, m_Count = 3, m_Restitution = 0.5f }, d),
        };

        internal static bool IsFinite(Vector4 v) => IsFinite(v.x) && IsFinite(v.y) && IsFinite(v.z) && IsFinite(v.w);
        internal static bool IsFinite(float x) => !float.IsNaN(x) && !float.IsInfinity(x);

        internal static void Near(Vector4 expected, Vector4 actual, string at, float relative)
        {
            for (int c = 0; c < 4; c++)
                Assert.That(actual[c], Is.EqualTo(expected[c]).Within(relative * Mathf.Max(1f, Mathf.Abs(expected[c]))), $"{at} c{c}");
        }

        [Test]
        public void E01a_EndValuesFollowTheTable()
        {
            var start = new Vector4(0.5f, -1f, 2f, 0f);
            var punch = new PunchEvaluator(Punch(A), 3);
            Assert.That(At(punch, Begin(punch, start), 1f).Value, Is.EqualTo(Vector4.zero), "Punch");
            var shake = new ShakeEvaluator(new ShakeParams { m_Amplitude = A, m_Frequency = 7f }, 3);
            Assert.That(At(shake, Begin(shake, start), 1f).Value, Is.EqualTo(Vector4.zero), "Shake — off the lattice");
            var tween = new TweenEvaluator(Tween(TweenMode.To), 3);
            Assert.That(At(tween, Begin(tween, start), 1f).Value, Is.EqualTo(new Vector4(5f, 6f, 7f, 0f)), "Tween To");
            var set = new SetEvaluator(new SetParams { m_Value = A, m_Relative = true }, 3);
            Assert.That(At(set, Begin(set, start, 2f), 1f).Value, Is.EqualTo(new Vector4(2.5f, 3f, 8f, 0f)), "Set Relative");
            var move = new SpringEvaluator(Spring(SpringMode.MoveTo), 3);
            Assert.That(At(move, Begin(move, start), 1f).Value, Is.EqualTo(new Vector4(3f, 6f, 9f, 0f)), "Spring MoveTo");
            var bump = new SpringEvaluator(Spring(SpringMode.Bump), 3);
            Assert.That(At(bump, Begin(bump, start), 1f).Value, Is.EqualTo(Vector4.zero), "Spring Bump");
            var bounce = new BounceEvaluator(new BounceParams { m_Amplitude = A, m_Count = 3, m_Restitution = 0.5f }, 3);
            Assert.That(At(bounce, Begin(bounce, start), 1f).Value, Is.EqualTo(Vector4.zero), "Bounce");
            var flash = new FlashEvaluator(Flash(), 3);
            Assert.That(At(flash, Begin(flash, start), 1f).Weight, Is.EqualTo(0f), "Flash");
            var blink = new BlinkEvaluator(Blink(), 3);
            var sb = Begin(blink, start);
            Assert.That(Eval(blink, sb, 0.5f, 1.05).Weight, Is.EqualTo(1f), "Blink is lit at 1.05 s (precondition)");
            Assert.That(Eval(blink, sb, 1f, 1.05).Weight, Is.EqualTo(0f), "Blink end");
            Gradient g = Colors();
            var gradient = new GradientEvaluator(new GradientParams { m_Gradient = g, m_Mode = GradientWrite.Persist }, 4);
            Color c = g.Evaluate(1f);
            Assert.That(At(gradient, Begin(gradient, start), 1f).Value, Is.EqualTo(new Vector4(c.r, c.g, c.b, c.a)), "Gradient");
        }

        [Test]
        public void E01b_Reg07_TimeMotionsChangeWithT()
        {
            foreach (var e in All(3).Where(x => !(x is SetEvaluator) && !(x is BlinkEvaluator)))
            {
                var s = Begin(e, new Vector4(0.5f, -1f, 2f, 0f));
                MotionSample p = At(e, s, 0.3f), q = At(e, s, 0.6f);
                Assert.That(p.Value != q.Value || p.Weight != q.Weight, $"{e.GetType().Name}");
            }
        }

        [Test]
        public void E01c_AnInfiniteShakeKeepsShaking()
        {
            var shake = new ShakeEvaluator(new ShakeParams { m_Amplitude = A, m_Frequency = 7f }, 1);
            var s = Begin(shake, Vector4.zero);
            float sum = 0f;
            for (int i = 1; i <= 50; i++)
                sum += Mathf.Abs(Eval(shake, s, 0f, i * 0.113).Value.x);
            Assert.That(sum, Is.GreaterThan(1f));
        }

        // an ordinary pair ends exactly in Mono without the chosen ends — its wide arithmetic hides the narrow miss
        [Test]
        public void E01d_TweenEndsExactlyAtFarApartEnds()
        {
            var p = Tween(TweenMode.FromTo, EaseType.Linear);
            p.m_From = new Vector4(1e20f, 1e-30f, -3e15f, 0f);
            p.m_To = new Vector4(1f, 1e10f, 0.25f, 0f);
            var e = new TweenEvaluator(p, 3);
            Assert.That(At(e, Begin(e, Vector4.zero), 1f).Value, Is.EqualTo(new Vector4(1f, 1e10f, 0.25f, 0f)));
        }

        [Test]
        public void E01e_CustomEaseEndsAtItsCurveEnd()
        {
            var p = Tween(TweenMode.FromTo, EaseType.Custom);
            p.m_Ease.m_Curve = new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(0.5f, 1f), new Keyframe(1f, 0f));
            var e = new TweenEvaluator(p, 3);
            Assert.That(At(e, Begin(e, Vector4.zero), 1f).Value, Is.EqualTo(new Vector4(-2f, -3f, 1f, 0f)), "a there-and-back curve ends at From");
        }

        [Test]
        public void E02a_Reg09_AmplitudeIsLinearAndOnlyChangesTakeTheIntensity()
        {
            var start = new Vector4(0.5f, -1f, 2f, 0.25f);
            foreach (var e in All(4).Where(x => x.Write == MotionWrite.Offset))
            {
                Vector4 one = At(e, Begin(e, start, 1f), 0.3f).Value, two = At(e, Begin(e, start, 2f), 0.3f).Value;
                Near(one * 2f, two, $"{e.GetType().Name} linear", 1e-5f);
            }
            var flash = new FlashEvaluator(Flash(), 4);
            Assert.That(At(flash, Begin(flash, start, 0.5f), 0.35f).Weight, Is.EqualTo(0.5f), "Flash 0.5");
            Assert.That(At(flash, Begin(flash, start, 2f), 0.35f).Weight, Is.EqualTo(1f), "Flash cap");
            var blink = new BlinkEvaluator(Blink(), 4);
            Assert.That(Eval(blink, Begin(blink, start, 0.5f), 0.5f, 1.05).Weight, Is.EqualTo(0.5f), "Blink 0.5");
            var over = new GradientEvaluator(new GradientParams { m_Gradient = Colors(), m_Mode = GradientWrite.Override }, 4);
            Assert.That(At(over, Begin(over, start, 2f), 0.5f).Weight, Is.EqualTo(1f), "Gradient cap");
            var by = new TweenEvaluator(Tween(TweenMode.By), 4);
            Near((At(by, Begin(by, start, 1f), 1f).Value - start) * 2f, At(by, Begin(by, start, 2f), 1f).Value - start, "Tween By", 1e-5f);
            var relative = new SetEvaluator(new SetParams { m_Value = A, m_Relative = true }, 4);
            Near((At(relative, Begin(relative, start, 1f), 0f).Value - start) * 2f, At(relative, Begin(relative, start, 2f), 0f).Value - start, "Set Relative", 1e-5f);
            var move = new SpringEvaluator(Spring(SpringMode.MoveTo, relative: true), 4);
            Near((At(move, Begin(move, start, 1f), 1f).Value - start) * 2f, At(move, Begin(move, start, 2f), 1f).Value - start, "Spring MoveTo Relative", 1e-5f);
            foreach (var e in new MotionEvaluator[] { new TweenEvaluator(Tween(TweenMode.To), 4), new SetEvaluator(new SetParams { m_Value = A }, 4), new SpringEvaluator(Spring(SpringMode.MoveTo), 4) })
                Assert.That(At(e, Begin(e, start, 2f), 1f).Value, Is.EqualTo(At(e, Begin(e, start, 0.5f), 1f).Value), $"{e.GetType().Name} absolute");
        }

        [Test]
        public void E02b_ANegativeIntensityGivesNoWeight()
        {
            var flash = new FlashEvaluator(Flash(), 4);
            Assert.That(At(flash, Begin(flash, Vector4.zero, -1f), 0.35f).Weight, Is.EqualTo(0f), "Flash");
            var blink = new BlinkEvaluator(Blink(), 4);
            Assert.That(Eval(blink, Begin(blink, Vector4.zero, -1f), 0.5f, 1.05).Weight, Is.EqualTo(0f), "Blink");
            var over = new GradientEvaluator(new GradientParams { m_Gradient = Colors(), m_Mode = GradientWrite.Override }, 4);
            Assert.That(At(over, Begin(over, Vector4.zero, -1f), 0.5f).Weight, Is.EqualTo(0f), "Gradient");
        }

        [Test]
        public void E02c_ANonFiniteIntensityGivesFiniteOutput()
        {
            foreach (float intensity in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
            foreach (var e in All(4))
            {
                var s = Begin(e, new Vector4(0.5f, -1f, 2f, 0.25f), intensity);
                foreach (float t in new[] { 0.3f, 1f })
                {
                    MotionSample o = Eval(e, s, t, t * Item + 0.05);
                    Assert.That(IsFinite(o.Value) && IsFinite(o.Weight), $"{e.GetType().Name} I {intensity} t {t}");
                }
            }
        }

        // preparation makes one evaluator for N play records
        [Test]
        public void E03_OneEvaluatorServesManyRecords()
        {
            MotionEvaluator[] shared = All(4), fresh = All(4);
            for (int i = 0; i < shared.Length; i++)
            {
                MotionEvaluator e = shared[i], f = fresh[i];
                var first = Begin(e, new Vector4(1f, 2f, 3f, 4f), 1f, 111u);
                var second = Begin(e, new Vector4(-1f, 0.5f, 0f, 2f), 0.7f, 222u);
                MotionSample a = At(e, first, 0.4f), b = At(e, second, 0.4f), again = At(e, first, 0.4f);
                MotionSample alone = At(f, Begin(f, new Vector4(-1f, 0.5f, 0f, 2f), 0.7f, 222u), 0.4f);
                Assert.That((again.Value, again.Weight), Is.EqualTo((a.Value, a.Weight)), $"{e.GetType().Name} the first record again");
                Assert.That((b.Value, b.Weight), Is.EqualTo((alone.Value, alone.Weight)), $"{e.GetType().Name} the second record = a fresh evaluator");
            }
        }

        [Test]
        public void E04a_Reg08_ComponentsAreIndependent()
        {
            var amplitude = new Vector4(1f, 0f, 2f, 0f);
            var offsets = new MotionEvaluator[]
            {
                new PunchEvaluator(Punch(amplitude), 4),
                new ShakeEvaluator(new ShakeParams { m_Amplitude = amplitude, m_Frequency = 7f }, 4),
                new OscillateEvaluator(new OscillateParams { m_Amplitude = amplitude, m_Frequency = 2f, m_Phase = 0.1f }, 4),
                new WiggleEvaluator(new WiggleParams { m_Amplitude = amplitude, m_Frequency = 3f, m_Octaves = 2 }, 4),
                new CurveEvaluator(new CurveParams { m_Curve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f), m_RemapOne = amplitude }, 4),
                new SpringEvaluator(new SpringParams { m_Mode = SpringMode.Bump, m_Value = amplitude, m_Frequency = 4f, m_Damping = 0.4f, m_Settle = 0.001f }, 4),
            };
            foreach (var e in offsets)
            {
                Vector4 v = At(e, Begin(e, Vector4.zero), 0.3f).Value;
                Assert.That((v.y, v.w), Is.EqualTo((0f, 0f)), $"{e.GetType().Name} a zero amplitude");
            }
            foreach (var e in new MotionEvaluator[] { new ShakeEvaluator(new ShakeParams { m_Amplitude = Vector4.one, m_Frequency = 7f }, 4),
                         new WiggleEvaluator(new WiggleParams { m_Amplitude = Vector4.one, m_Frequency = 3f, m_Octaves = 2 }, 4) })
            {
                var s = Begin(e, Vector4.zero);
                bool differ = false;
                for (int i = 1; i <= 20 && !differ; i++)
                {
                    Vector4 v = Eval(e, s, 0f, i * 0.071).Value;
                    differ = v.x != v.y || v.x != v.z || v.x != v.w;
                }
                Assert.That(differ, $"{e.GetType().Name} axes differ");
            }
        }

        // the occurrence seed — a seed-blind evaluator passes E04a: the component tag still splits the axes
        [Test]
        public void E04b_TheSeedChangesTheShape()
        {
            foreach (var e in new MotionEvaluator[] { new ShakeEvaluator(new ShakeParams { m_Amplitude = Vector4.one, m_Frequency = 7f }, 1),
                         new WiggleEvaluator(new WiggleParams { m_Amplitude = Vector4.one, m_Frequency = 3f, m_Octaves = 2 }, 1) })
            {
                MotionState one = Begin(e, Vector4.zero, 1f, 1u), two = Begin(e, Vector4.zero, 1f, 2u);
                bool differ = false;
                for (int i = 1; i <= 20 && !differ; i++)
                    differ = Eval(e, one, 0f, i * 0.071).Value.x != Eval(e, two, 0f, i * 0.071).Value.x;
                Assert.That(differ, e.GetType().Name);
            }
            var p = Blink();
            p.m_Jitter = 0.8f;
            var blink = new BlinkEvaluator(p, 1);
            MotionState b1 = Begin(blink, Vector4.zero, 1f, 1u), b2 = Begin(blink, Vector4.zero, 1f, 2u);
            int differing = 0;
            for (int i = 0; i < 400; i++)
                if (Eval(blink, b1, 0f, i * 0.0137).Weight != Eval(blink, b2, 0f, i * 0.0137).Weight)
                    differing++;
            Assert.That(differing, Is.GreaterThan(0), "Blink");
        }

        [Test]
        public void E05a_ComponentsPastTheDimensionAreZero()
        {
            for (int d = 1; d <= 3; d++)
            foreach (var e in All(d))
            {
                var s = Begin(e, new Vector4(9f, 9f, 9f, 9f));
                foreach (float t in new[] { 0f, 0.4f, 1f })
                {
                    Vector4 v = At(e, s, t).Value;
                    for (int c = d; c < 4; c++)
                        Assert.That(v[c], Is.EqualTo(0f), $"{e.GetType().Name} d {d} t {t} c{c}");
                }
            }
        }

        [Test]
        public void E05b_TheDimensionIsCutWhenMade()
        {
            PunchParams p = Punch(A);
            var five = new PunchEvaluator(p, 5);
            Assert.That(At(five, Begin(five, Vector4.zero), 0.1f).Value.w, Is.Not.EqualTo(0f), "made directly with 5 — four components");
            var zero = new PunchEvaluator(p, 0);
            Vector4 z = At(zero, Begin(zero, Vector4.zero), 0.1f).Value;
            Assert.That((z.x != 0f, z.y), Is.EqualTo((true, 0f)), "made directly with 0 — one component");
            var made = MotionEvaluators.Create(new PunchMotion { m_Params = p }, 5);
            Assert.That(At(made, Begin(made, Vector4.zero), 0.1f).Value.w, Is.Not.EqualTo(0f), "the factory with 5 — four components");
        }

        [Test]
        public void E09_SetIsInstant()
        {
            var start = Vector4.one;
            var set = new SetEvaluator(new SetParams { m_Value = A, m_Relative = true }, 4);
            Assert.That(set.IsInstant, "Set");
            Assert.That(Eval(set, Begin(set, start), 0f, 0.0).Value, Is.EqualTo(start + A), "Set at t = 0");
            var tween = new TweenEvaluator(Tween(TweenMode.To), 4);
            Assert.That(Eval(tween, Begin(tween, start), 1f, 0.0).Value, Is.EqualTo(new Vector4(5f, 6f, 7f, -1f)), "a zero-length Tween (the timeline gives T = 1)");
        }

        // the factory
        [Test]
        public void F01_TheFactoryMakesEveryMotion()
        {
            var types = typeof(FeedbackMotion).Assembly.GetTypes().Where(t => typeof(FeedbackMotion).IsAssignableFrom(t) && !t.IsAbstract).ToArray();
            Assert.That(types.Length, Is.EqualTo(12));
            foreach (var t in types)
            {
                var motion = (FeedbackMotion)Activator.CreateInstance(t, true);
                MotionEvaluator e = MotionEvaluators.Create(motion, 3);
                Assert.That(e, Is.Not.Null, t.Name);
                Assert.That(e.IsInstant, Is.EqualTo(motion is SetMotion), $"{t.Name} instant");
                Assert.That(BitConverter.SingleToInt32Bits(e.NaturalDuration), Is.EqualTo(BitConverter.SingleToInt32Bits(motion.NaturalDuration)), $"{t.Name} natural duration");
                MotionWrite expected = motion.Kind == MotionKind.Set || motion.Kind == MotionKind.Tween ? MotionWrite.Persist
                    : motion.Kind == MotionKind.Flash || motion.Kind == MotionKind.Blink || motion.Kind == MotionKind.Gradient ? MotionWrite.Override
                    : MotionWrite.Offset;
                Assert.That(e.Write, Is.EqualTo(expected), $"{t.Name} write (default parameters)");
            }
            var persist = new GradientMotion { m_Params = new GradientParams { m_Gradient = Colors(), m_Mode = GradientWrite.Persist } };
            Assert.That(MotionEvaluators.Create(persist, 4).Write, Is.EqualTo(MotionWrite.Persist), "Gradient Persist");
            Assert.That(MotionEvaluators.Create(new SpringMotion { m_Params = Spring(SpringMode.MoveTo) }, 3).Write, Is.EqualTo(MotionWrite.Persist), "Spring MoveTo");
            Assert.That(MotionEvaluators.Create(null, 3), Is.Null, "null");
            Assert.That(MotionEvaluators.Create(new DataModelTestMotion(), 3), Is.Null, "a test motion");
        }

        // wiring — the evaluator against the kernels called with the item's parameters. The properties above (ends,
        // linearity, independence, range) all hold when a parameter goes to the wrong argument
        [Test]
        public void W01_EvaluatorsPassTheParametersToTheKernels()
        {
            const uint seed = 777u;
            const float I = 0.8f;
            foreach (double seconds in new[] { 0.13, 0.41, 0.77, 1.9, 3.3 })
            {
                float t = (float)(seconds / 4.0);
                var time = new MotionTime(t, seconds);

                var punch = new PunchEvaluator(Punch(A), 1);
                punch.Evaluate(Begin(punch, Vector4.zero, I, seed), time, out MotionSample o);
                Assert.That(o.Value.x, Is.EqualTo(A.x * (MotionKernels.Punch(t, 3f, 2f) * I)).Within(1e-6f), $"Punch {seconds}");

                var bounce = new BounceEvaluator(new BounceParams { m_Amplitude = A, m_Count = 4, m_Restitution = 0.6f }, 1);
                bounce.Evaluate(Begin(bounce, Vector4.zero, I, seed), time, out o);
                Assert.That(o.Value.x, Is.EqualTo(A.x * (MotionKernels.Bounce(t, 4, 0.6f) * I)).Within(1e-6f), $"Bounce {seconds}");
                Assert.That(bounce.EvaluateShape(Begin(bounce, Vector4.zero, I, seed), time), Is.EqualTo(MotionKernels.Bounce(t, 4, 0.6f) * I).Within(1e-6f), $"Bounce shape {seconds}");

                var shake = new ShakeEvaluator(new ShakeParams { m_Amplitude = A, m_Frequency = 7f, m_Noise = ShakeNoise.Jitter, m_Falloff = Ease.Of(EaseType.QuadIn) }, 1);
                shake.Evaluate(Begin(shake, Vector4.zero, I, seed), time, out o);
                float jitter = Noise.Jitter(Hash32.Tagged(seed, Hash32.Component, 0), 7f, seconds);
                Assert.That(o.Value.x, Is.EqualTo(A.x * jitter * (1f - Easing.Evaluate(EaseType.QuadIn, 0f, 0f, t)) * I).Within(1e-5f), $"Shake Jitter {seconds}");
                var smooth = new ShakeEvaluator(new ShakeParams { m_Amplitude = A, m_Frequency = 7f }, 1);
                smooth.Evaluate(Begin(smooth, Vector4.zero, I, seed), time, out o);
                float n = Noise.SmoothHz(Hash32.Tagged(seed, Hash32.Component, 0), 7f, seconds);
                Assert.That(o.Value.x, Is.EqualTo(A.x * n * (1f - t) * I).Within(1e-5f), $"Shake Smooth {seconds}");

                var oscillate = new OscillateEvaluator(new OscillateParams { m_Amplitude = A, m_Frequency = 2f, m_Waveform = Waveform.Triangle, m_Phase = 0.3f }, 1);
                oscillate.Evaluate(Begin(oscillate, Vector4.zero, I, seed), time, out o);
                Assert.That(o.Value.x, Is.EqualTo(A.x * (MotionKernels.Wave(Waveform.Triangle, 2.0 * seconds + 0.3f) * I)).Within(1e-5f), $"Oscillate {seconds}");

                var wiggle = new WiggleEvaluator(new WiggleParams { m_Amplitude = A, m_Frequency = 3f, m_Octaves = 3 }, 1);
                wiggle.Evaluate(Begin(wiggle, Vector4.zero, I, seed), time, out o);
                Assert.That(o.Value.x, Is.EqualTo(A.x * Noise.Fbm(Hash32.Tagged(seed, Hash32.Component, 0), 3f, seconds, 3) * I).Within(1e-5f), $"Wiggle {seconds}");

                var flash = new FlashEvaluator(new FlashParams { m_Target = A, m_Attack = 0.2f, m_Hold = 0.3f, m_Release = 0.4f }, 1);
                flash.Evaluate(Begin(flash, Vector4.zero, I, seed), time, out o);
                Assert.That(o.Weight, Is.EqualTo(I * MotionKernels.Trapezoid(t, 0.2f, 0.3f, 0.4f, out _)).Within(1e-6f), $"Flash {seconds}");

                TweenParams tp = Tween(TweenMode.FromTo, EaseType.Linear);
                tp.m_Loops = 3;
                var tween = new TweenEvaluator(tp, 1);
                tween.Evaluate(Begin(tween, Vector4.zero, I, seed), time, out o);
                Assert.That(o.Value.x, Is.EqualTo(tp.m_From.x + (tp.m_To.x - tp.m_From.x) * MotionKernels.Warp(t, 3, false)).Within(1e-5f), $"Tween {seconds}");

                AnimationCurve curve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
                var remap = new CurveEvaluator(new CurveParams { m_Curve = curve, m_RemapZero = Vector4.one, m_RemapOne = A * 3f }, 1);
                remap.Evaluate(Begin(remap, Vector4.zero, I, seed), time, out o);
                Assert.That(o.Value.x, Is.EqualTo((1f + (3f - 1f) * curve.Evaluate(t)) * I).Within(1e-5f), $"Curve {seconds}");

                SpringParams sp = Spring(SpringMode.MoveTo);
                SpringShape shape = SpringShape.Of(sp.m_Frequency, sp.m_Damping);
                var move = new SpringEvaluator(sp, 1);
                move.Evaluate(Begin(move, new Vector4(-2f, 0f, 0f, 0f), I, seed), time, out o);
                float target = sp.m_Value.x;
                Assert.That(o.Value.x, Is.EqualTo(target + SpringMath.Y(shape, -2f - target, 0f, t * sp.SettleTime())).Within(1e-4f), $"Spring MoveTo {seconds}");
                SpringParams bp = Spring(SpringMode.Bump);
                var bump = new SpringEvaluator(bp, 1);
                bump.Evaluate(Begin(bump, Vector4.zero, I, seed), time, out o);
                Assert.That(o.Value.x, Is.EqualTo(SpringMath.Y(shape, 0f, I * bp.m_Value.x, t * bp.SettleTime())).Within(1e-4f), $"Spring Bump {seconds}");
            }
            var startMove = new SpringEvaluator(Spring(SpringMode.MoveTo), 1);
            Assert.That(Eval(startMove, Begin(startMove, new Vector4(-2f, 0f, 0f, 0f)), 0f, 0.0).Value.x, Is.EqualTo(-2f), "Spring MoveTo starts at its start");
            var startTween = new TweenEvaluator(Tween(TweenMode.To), 1);
            Assert.That(Eval(startTween, Begin(startTween, new Vector4(-2f, 0f, 0f, 0f)), 0f, 0.0).Value.x, Is.EqualTo(-2f), "Tween To starts at its start");
            var blink = new BlinkEvaluator(new BlinkParams { m_Value = A, m_Period = 0.25f, m_Duty = 0.3f, m_Jitter = 0.6f, m_StartOn = false }, 1);
            var bs = Begin(blink, Vector4.zero, 1f, seed);
            for (int i = 0; i < 200; i++)
            {
                double seconds = i * 0.0173;
                bool lit = MotionKernels.Blink(seed, seconds, 0.25f, 0.3f, 0.6f, false);
                Assert.That(Eval(blink, bs, 0f, seconds).Weight, Is.EqualTo(lit ? 1f : 0f), $"Blink {seconds}");
            }
        }

        // non-finite values — parameters and the intensity are cut once, curve and gradient outputs every time
        [Test]
        public void N01_NonFiniteParametersGiveFiniteOutput()
        {
            var bad = new Vector4(float.NaN, float.PositiveInfinity, float.NegativeInfinity, 1f);
            var list = new MotionEvaluator[]
            {
                new SetEvaluator(new SetParams { m_Value = bad }, 4),
                new SetEvaluator(new SetParams { m_Value = bad, m_Relative = true }, 4),
                new TweenEvaluator(new TweenParams { m_Mode = TweenMode.FromTo, m_From = bad, m_To = bad, m_Loops = 1 }, 4),
                new PunchEvaluator(Punch(bad), 4),
                new ShakeEvaluator(new ShakeParams { m_Amplitude = bad, m_Frequency = 7f }, 4),
                new CurveEvaluator(new CurveParams { m_Curve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f), m_RemapZero = bad, m_RemapOne = bad }, 4),
                new FlashEvaluator(new FlashParams { m_Target = bad, m_Attack = 0.2f, m_Hold = 0.3f, m_Release = 0.3f }, 4),
                new OscillateEvaluator(new OscillateParams { m_Amplitude = bad, m_Frequency = 2f }, 4),
                new BlinkEvaluator(new BlinkParams { m_Value = bad, m_Period = 0.2f, m_Duty = 0.5f, m_StartOn = true }, 4),
                new WiggleEvaluator(new WiggleParams { m_Amplitude = bad, m_Frequency = 3f, m_Octaves = 2 }, 4),
                new SpringEvaluator(new SpringParams { m_Mode = SpringMode.Bump, m_Value = bad, m_Frequency = 4f, m_Damping = 0.4f, m_Settle = 0.001f }, 4),
                new SpringEvaluator(new SpringParams { m_Mode = SpringMode.MoveTo, m_Value = bad, m_Frequency = 4f, m_Damping = 0.4f, m_Settle = 0.001f }, 4),
            };
            foreach (var e in list)
            {
                var s = Begin(e, Vector4.one);
                foreach (float t in new[] { 0.3f, 1f })
                    Assert.That(IsFinite(At(e, s, t).Value), $"{e.GetType().Name} t {t}");
            }
        }

        [Test]
        public void N02_NonFiniteCurveKeysGiveFiniteOutput()
        {
            var holder = ScriptableObject.CreateInstance<MotionCurveHolder>();
            try
            {
                holder.curve = new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(0.5f, 0.25f), new Keyframe(1f, 1f));
                holder.gradient = Colors();
                var so = new SerializedObject(holder);
                so.FindProperty("curve.m_Curve.Array.data[1].value").floatValue = float.NaN;
                so.FindProperty("gradient.key0.r").floatValue = float.NaN;
                so.ApplyModifiedPropertiesWithoutUndo();
                Assert.That(float.IsNaN(holder.curve.Evaluate(0.5f)), "precondition — a NaN key");
                Assert.That(float.IsNaN(holder.gradient.Evaluate(0.5f).r), "precondition — a NaN channel");

                Ease ease = Ease.Of(EaseType.Custom);
                ease.m_Curve = holder.curve;
                var list = new MotionEvaluator[]
                {
                    new TweenEvaluator(new TweenParams { m_Mode = TweenMode.FromTo, m_To = Vector4.one, m_Ease = ease, m_Loops = 1 }, 4),
                    new CurveEvaluator(new CurveParams { m_Curve = holder.curve, m_RemapOne = Vector4.one }, 4),
                    new GradientEvaluator(new GradientParams { m_Gradient = holder.gradient, m_Mode = GradientWrite.Override }, 4),
                };
                foreach (var e in list)
                {
                    var s = Begin(e, Vector4.zero);
                    foreach (float t in new[] { 0.25f, 0.5f, 0.75f })
                    {
                        Assert.That(IsFinite(Eval(e, s, t, t).Value), $"{e.GetType().Name} t {t}");
                        Assert.That(IsFinite(e.EvaluateShape(s, new MotionTime(t, t))), $"{e.GetType().Name} shape t {t}");
                    }
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(holder);
            }
        }

        // a null curve or gradient comes only from code — a loaded one is an empty curve (0) or the default (white)
        [Test]
        public void N03_NullCurvesActLikeLoadedOnes()
        {
            var curve = new CurveEvaluator(new CurveParams { m_RemapZero = Vector4.one, m_RemapOne = A }, 4);
            Assert.That(At(curve, Begin(curve, Vector4.zero, 2f), 0.4f).Value, Is.EqualTo(Vector4.one * 2f), "Curve — the curve is 0");
            var gradient = new GradientEvaluator(new GradientParams { m_Mode = GradientWrite.Override }, 4);
            Assert.That(At(gradient, Begin(gradient, Vector4.zero), 0.4f).Value, Is.EqualTo(Vector4.one), "Gradient — white");
            TweenParams p = Tween(TweenMode.FromTo, EaseType.Custom);
            var tween = new TweenEvaluator(p, 4);
            Near(Vector4.LerpUnclamped(p.m_From, p.m_To, 0.4f), At(tween, Begin(tween, Vector4.zero), 0.4f).Value, "Custom ease — Linear", 1e-5f);
        }

        // Shortest — loops, yoyo and an overshooting ease, or a separately written Progress has nowhere to differ
        [Test]
        public void S01_ProgressAndEndpointsMakeEvaluate()
        {
            TweenParams p = Tween(TweenMode.FromTo, EaseType.BackOut);
            p.m_Loops = 3;
            p.m_Yoyo = true;
            var e = new TweenEvaluator(p, 3);
            var s = Begin(e, Vector4.zero);
            float max = float.MinValue;
            for (int i = 0; i <= 100; i++)
            {
                var time = new MotionTime(i / 100f, i / 100.0);
                e.Endpoints(s, out Vector4 from, out Vector4 to);
                float k = e.Progress(time);
                max = Mathf.Max(max, k);
                e.Evaluate(s, time, out MotionSample o);
                Vector4 expected = Vector4.LerpUnclamped(from, to, k);
                expected.w = 0f;
                Near(expected, o.Value, $"t {i / 100f}", 1e-4f);
            }
            Assert.That(max, Is.GreaterThan(1.05f), "Progress keeps the overshoot");
            var start = new Vector4(0.5f, 1f, 1.5f, 2f);
            foreach (TweenMode mode in Enum.GetValues(typeof(TweenMode)))
            {
                TweenParams q = Tween(mode);
                var x = new TweenEvaluator(q, 3);
                x.Endpoints(Begin(x, start, 2f), out Vector4 from, out Vector4 to);
                (Vector4, Vector4) expected = mode == TweenMode.To ? (start, q.m_To)
                    : mode == TweenMode.From ? (q.m_From, start)
                    : mode == TweenMode.FromTo ? (q.m_From, q.m_To)
                    : (start, start + 2f * q.m_To);
                Assert.That((from, to), Is.EqualTo(expected), mode.ToString());
            }
        }

        // Flash — an In ease with Attack ≠ Release: InOut is point-symmetric, so both mirrors agree
        [Test]
        public void FL01_FlashSlopesTakeTheEaseAsATimeMirror()
        {
            var p = new FlashParams { m_Target = A, m_Attack = 0.2f, m_Hold = 0.3f, m_Release = 0.4f, m_Ease = Ease.Of(EaseType.QuadIn) };
            var e = new FlashEvaluator(p, 1);
            var s = Begin(e, Vector4.zero);
            for (int i = 0; i <= 100; i++)
            {
                float t = i / 100f;
                float h = MotionKernels.Trapezoid(t, 0.2f, 0.3f, 0.4f, out bool ramp);
                float expected = ramp ? Easing.Evaluate(EaseType.QuadIn, 0f, 0f, h) : h;
                Assert.That(At(e, s, t).Weight, Is.EqualTo(expected).Within(1e-6f), $"t {t}");
            }
        }

        [Test]
        public void FL02_FlashWeightStaysWithinTheCap()
        {
            foreach (EaseType ease in new[] { EaseType.BackOut, EaseType.ElasticOut, EaseType.BackIn, EaseType.ElasticIn })
            {
                var e = new FlashEvaluator(new FlashParams { m_Target = A, m_Attack = 0.3f, m_Hold = 0.2f, m_Release = 0.3f, m_Ease = Ease.Of(ease) }, 1);
                var s = Begin(e, Vector4.zero);
                for (int i = 0; i <= 200; i++)
                    Assert.That(At(e, s, i / 200f).Weight, Is.InRange(0f, 1f), $"{ease} t {i / 200f}");
            }
        }

        static readonly double[] s_Seconds = { 0.0, 0.13, 0.5, Item, 7.25, 1234.567 };

        // Per Axis off — the axis fields are not read: every component is the one wave, bit for bit
        [Test]
        public void PA01_PerAxisOffKeepsTheOneWave()
        {
            foreach (Waveform w in Enum.GetValues(typeof(Waveform)))
            {
                for (int d = 1; d <= 4; d++)
                {
                    var p = new OscillateParams { m_Amplitude = A, m_Frequency = 2f, m_Waveform = w, m_Phase = 0.1f };
                    var off = p;
                    off.m_AxisPhase = new Vector4(0.25f, 0.5f, 0.125f, 0.75f);
                    off.m_AxisFrequency = new Vector4(3f, 0.5f, 2f, 7f);
                    var plain = new OscillateEvaluator(p, d);
                    var e = new OscillateEvaluator(off, d);
                    foreach (float intensity in new[] { 1f, 0.6f })
                    {
                        MotionState s = Begin(e, Vector4.zero, intensity);
                        MotionState s0 = Begin(plain, Vector4.zero, intensity);
                        foreach (double seconds in s_Seconds)
                        {
                            var time = new MotionTime(0.4f, seconds);
                            e.Evaluate(s, time, out MotionSample o);
                            plain.Evaluate(s0, time, out MotionSample o0);
                            float k = MotionKernels.Wave(w, 2f * seconds + 0.1f) * intensity;
                            var expected = new Vector4(A.x * k, d > 1 ? A.y * k : 0f, d > 2 ? A.z * k : 0f, d > 3 ? A.w * k : 0f);
                            string at = $"{w} d {d} I {intensity} s {seconds}";
                            Assert.That((o.Value, o.Weight, o0.Value), Is.EqualTo((expected, 1f, expected)), at);
                            Assert.That(e.EvaluateShape(s, time), Is.EqualTo(k), at + " shape");
                        }
                    }
                }
            }
        }

        // Per Axis on — component c is A_c · Wave(F · k_c · Seconds + Phase + o_c): with a Sine, o = (0, 0.25) circles and
        // k = (1, 2) traces a figure eight; the shape is component 0's; the axis fields are cut when made
        [Test]
        public void PA02_PerAxisGivesEachComponentItsOwnWave()
        {
            var o = new Vector4(0.1f, 0.35f, 0.6f, 0.85f);
            var k = new Vector4(3f, 0.5f, 2f, 1.25f);
            foreach (Waveform w in Enum.GetValues(typeof(Waveform)))
            {
                var e = new OscillateEvaluator(new OscillateParams { m_Amplitude = A, m_Frequency = 2f, m_Waveform = w, m_Phase = 0.2f, m_PerAxis = true, m_AxisPhase = o, m_AxisFrequency = k }, 4);
                MotionState s = Begin(e, Vector4.zero, 0.8f);
                foreach (double seconds in s_Seconds)
                {
                    var time = new MotionTime(0.4f, seconds);
                    e.Evaluate(s, time, out MotionSample sample);
                    for (int c = 0; c < 4; c++)
                    {
                        float expected = A[c] * (MotionKernels.Wave(w, 2f * k[c] * seconds + 0.2f + o[c]) * 0.8f);
                        Assert.That(sample.Value[c], Is.EqualTo(expected).Within(1e-6f), $"{w} s {seconds} c{c}");
                    }
                    Assert.That(e.EvaluateShape(s, time), Is.EqualTo(MotionKernels.Wave(w, 2f * k.x * seconds + 0.2f + o.x) * 0.8f).Within(1e-6f), $"{w} s {seconds} shape");
                }
            }

            var circle = new OscillateEvaluator(new OscillateParams { m_Amplitude = new Vector4(1.5f, 1.5f, 0f, 0f), m_Frequency = 0.7f, m_PerAxis = true, m_AxisPhase = new Vector4(0f, 0.25f, 0f, 0f), m_AxisFrequency = Vector4.one }, 2);
            var eight = new OscillateEvaluator(new OscillateParams { m_Amplitude = new Vector4(1.5f, 0.5f, 0f, 0f), m_Frequency = 0.7f, m_PerAxis = true, m_AxisFrequency = new Vector4(1f, 2f, 1f, 1f) }, 2);
            MotionState sc = Begin(circle, Vector4.zero), se = Begin(eight, Vector4.zero);
            float least = float.MaxValue, most = float.MinValue;
            for (int i = 0; i <= 200; i++)
            {
                double seconds = i / 100.0;
                circle.Evaluate(sc, new MotionTime(0f, seconds), out MotionSample c);
                Assert.That(c.Value.x * c.Value.x + c.Value.y * c.Value.y, Is.EqualTo(1.5f * 1.5f).Within(1.5f * 1.5f * 1e-5f), $"circle s {seconds}");
                eight.Evaluate(se, new MotionTime(0f, seconds), out MotionSample g);
                float x = g.Value.x / 1.5f, y = g.Value.y / 0.5f;
                Assert.That(y * y, Is.EqualTo(4f * x * x * (1f - x * x)).Within(1e-5f), $"eight s {seconds}");
                least = Mathf.Min(least, g.Value.y);
                most = Mathf.Max(most, g.Value.y);
            }
            Assert.That((least < -0.49f, most > 0.49f), Is.EqualTo((true, true)), "the eight reaches both lobes");

            var cut = new OscillateEvaluator(new OscillateParams { m_Amplitude = A, m_Frequency = 2f, m_Phase = 0.2f, m_PerAxis = true, m_AxisPhase = new Vector4(float.NaN, float.PositiveInfinity, 0.25f, 0f), m_AxisFrequency = new Vector4(float.NaN, -2f, float.PositiveInfinity, 1f) }, 4);
            MotionState st = Begin(cut, Vector4.zero);
            foreach (double seconds in s_Seconds)
            {
                cut.Evaluate(st, new MotionTime(0.4f, seconds), out MotionSample v);
                var expected = new Vector4(A.x * MotionKernels.Wave(Waveform.Sine, 0.2f), A.y * MotionKernels.Wave(Waveform.Sine, 0.2f), A.z * MotionKernels.Wave(Waveform.Sine, 0.2f + 0.25f), A.w * MotionKernels.Wave(Waveform.Sine, 2f * seconds + 0.2f));
                Near(expected, v.Value, $"cut s {seconds}", 1e-6f);
            }
        }

        // ---- the envelope

        static OscillateParams FadeOscillate(bool perAxis) => new OscillateParams
        {
            m_Amplitude = A, m_Frequency = 2f, m_Waveform = Waveform.Triangle, m_Phase = 0.1f, m_PerAxis = perAxis,
            m_AxisPhase = new Vector4(0.1f, 0.35f, 0.6f, 0.85f), m_AxisFrequency = new Vector4(3f, 0.5f, 2f, 1.25f),
        };

        static ShakeParams FadeShake(ShakeNoise noise) => new ShakeParams { m_Amplitude = A, m_Frequency = 7f, m_Noise = noise, m_Falloff = Ease.Of(EaseType.QuadIn) };

        // the five shapes, each made twice — without the fade fields and with them (a Shake takes no Fade Out)
        static (string Name, MotionEvaluator Plain, MotionEvaluator Faded)[] Fades(float fadeIn, float fadeOut, Ease ease)
        {
            OscillateParams Oscillate(bool perAxis, bool fade)
            {
                OscillateParams p = FadeOscillate(perAxis);
                if (fade)
                {
                    p.m_FadeIn = fadeIn;
                    p.m_FadeOut = fadeOut;
                    p.m_FadeEase = ease;
                }
                return p;
            }
            WiggleParams Wiggle(bool fade)
            {
                var p = new WiggleParams { m_Amplitude = A, m_Frequency = 3f, m_Octaves = 2 };
                if (fade)
                {
                    p.m_FadeIn = fadeIn;
                    p.m_FadeOut = fadeOut;
                    p.m_FadeEase = ease;
                }
                return p;
            }
            ShakeParams Shake(ShakeNoise noise, bool fade)
            {
                ShakeParams p = FadeShake(noise);
                if (fade)
                {
                    p.m_FadeIn = fadeIn;
                    p.m_FadeEase = ease;
                }
                return p;
            }
            return new (string, MotionEvaluator, MotionEvaluator)[]
            {
                ("Oscillate", new OscillateEvaluator(Oscillate(false, false), 4), new OscillateEvaluator(Oscillate(false, true), 4)),
                ("Oscillate Per Axis", new OscillateEvaluator(Oscillate(true, false), 4), new OscillateEvaluator(Oscillate(true, true), 4)),
                ("Wiggle", new WiggleEvaluator(Wiggle(false), 4), new WiggleEvaluator(Wiggle(true), 4)),
                ("Shake Smooth", new ShakeEvaluator(Shake(ShakeNoise.Smooth, false), 4), new ShakeEvaluator(Shake(ShakeNoise.Smooth, true), 4)),
                ("Shake Jitter", new ShakeEvaluator(Shake(ShakeNoise.Jitter, false), 4), new ShakeEvaluator(Shake(ShakeNoise.Jitter, true), 4)),
            };
        }

        // the five as the evaluators write them — the products in the same order and cut to float where they are (an argument,
        // a return), so they match bit for bit: Mono keeps a float product wide until it is stored
        static float Noise5(string name, uint seed, int c, double seconds)
        {
            uint channel = Hash32.Tagged(seed, Hash32.Component, (uint)c);
            return name == "Wiggle" ? Noise.Fbm(channel, 3f, seconds, 2)
                : name == "Shake Jitter" ? Noise.Jitter(channel, 7f, seconds) : Noise.SmoothHz(channel, 7f, seconds);
        }

        static Vector4 Formula(string name, in MotionState s, in MotionTime time)
        {
            OscillateParams o = FadeOscillate(true);
            float f = Falloff(time.T);
            float falloff = f * s.Intensity;
            float wave = Cut(MotionKernels.Wave(Waveform.Triangle, 2f * time.Seconds + 0.1f) * s.Intensity);
            Vector4 v = default;
            for (int c = 0; c < 4; c++)
            {
                switch (name)
                {
                    case "Oscillate": v[c] = A[c] * wave; break;
                    case "Oscillate Per Axis": v[c] = A[c] * (MotionKernels.Wave(Waveform.Triangle, 2f * o.m_AxisFrequency[c] * time.Seconds + 0.1f + o.m_AxisPhase[c]) * s.Intensity); break;
                    case "Wiggle": v[c] = A[c] * Noise5(name, s.Seed, c, time.Seconds) * s.Intensity; break;
                    default: v[c] = A[c] * Noise5(name, s.Seed, c, time.Seconds) * falloff; break;
                }
            }
            return v;
        }

        static float FormulaShape(string name, in MotionState s, in MotionTime time)
        {
            OscillateParams o = FadeOscillate(true);
            switch (name)
            {
                case "Oscillate": return MotionKernels.Wave(Waveform.Triangle, 2f * time.Seconds + 0.1f) * s.Intensity;
                case "Oscillate Per Axis": return MotionKernels.Wave(Waveform.Triangle, 2f * o.m_AxisFrequency.x * time.Seconds + 0.1f + o.m_AxisPhase.x) * s.Intensity;
                case "Wiggle": return Noise5(name, s.Seed, 0, time.Seconds) * s.Intensity;
                default: return Noise5(name, s.Seed, 0, time.Seconds) * Falloff(time.T) * s.Intensity;
            }
        }

        static float Falloff(float t) => 1f - Easing.Evaluate(EaseType.QuadIn, 0f, 0f, t);

        static float Cut(float x) => x;

        // the envelope's height by hand — 0 at a faded end, linear on the slopes, the fall the rise run backwards, cut to 0 ~ 1
        static float Height(float t, float fadeIn, float fadeOut, Ease ease)
        {
            if ((t <= 0f && fadeIn > 0f) || (t >= 1f && fadeOut > 0f))
                return 0f;
            if (t < fadeIn)
                return Mathf.Clamp01(ease.Evaluate(t / fadeIn));
            if (t > 1f - fadeOut)
                return Mathf.Clamp01(ease.Evaluate((1f - t) / fadeOut));
            return 1f;
        }

        // Fade 0 is the motion as it was, bit for bit: the formula and the twin made without the fields. A Fade Ease alone
        // changes nothing, and an endless state takes no fade at all — the value and the shape
        [Test]
        public void FD01_NoFadeKeepsTheMotionBitForBit()
        {
            var eased = Fades(0f, 0f, Ease.Of(EaseType.ElasticOut));
            var endless = Fades(0.3f, 0.4f, Ease.Of(EaseType.QuadIn));
            for (int k = 0; k < eased.Length; k++)
            {
                string name = eased[k].Name;
                MotionEvaluator plain = eased[k].Plain;
                MotionState s = Begin(plain, Vector4.zero, 0.8f);
                MotionState se = Begin(endless[k].Faded, Vector4.zero, 0.8f, endless: true);
                foreach (float t in new[] { 0f, 0.1f, 0.5f, 0.85f, 1f })
                {
                    foreach (double seconds in s_Seconds)
                    {
                        var time = new MotionTime(t, seconds);
                        plain.Evaluate(s, time, out MotionSample p);
                        eased[k].Faded.Evaluate(s, time, out MotionSample e);
                        endless[k].Faded.Evaluate(se, time, out MotionSample n);
                        string at = $"{name} t {t} s {seconds}";
                        Assert.That((p.Value, p.Weight), Is.EqualTo((Formula(name, s, time), 1f)), at + " formula");
                        Assert.That((e.Value, e.Weight, n.Value, n.Weight), Is.EqualTo((p.Value, 1f, p.Value, 1f)), at + " a Fade Ease alone · endless");
                        float shape = plain.EvaluateShape(s, time);
                        Assert.That((shape, eased[k].Faded.EvaluateShape(s, time), endless[k].Faded.EvaluateShape(se, time)), Is.EqualTo((FormulaShape(name, s, time), shape, shape)), at + " shape");
                    }
                }
            }
        }

        // the envelope — T and Seconds apart: 0 s is on the noise's lattice and a Sine's zero, where a motion is 0 with no fade.
        // The value and the shape are the plain motion's times the height (a Shake's falloff kept); a faded end is exactly 0
        // where the plain one is not; Fade Out alone
        [Test]
        public void FD02_TheEnvelopeFadesInAndOut()
        {
            const float In = 0.3f, Out = 0.4f;
            Ease quad = Ease.Of(EaseType.QuadIn);
            foreach (var (name, plain, faded) in Fades(In, Out, quad))
            {
                float fadeOut = name.StartsWith("Shake", StringComparison.Ordinal) ? 0f : Out;
                MotionState s = Begin(plain, Vector4.zero, 0.8f);
                for (int i = 0; i <= 40; i++)
                {
                    float t = i / 40f;
                    var time = new MotionTime(t, 0.37 + t * 1.37);
                    plain.Evaluate(s, time, out MotionSample p);
                    faded.Evaluate(s, time, out MotionSample f);
                    float h = Height(t, In, fadeOut, quad);
                    string at = $"{name} t {t}";
                    Near(p.Value * h, f.Value, at, 1e-6f);
                    Assert.That(faded.EvaluateShape(s, time), Is.EqualTo(plain.EvaluateShape(s, time) * h).Within(1e-6f), at + " shape");
                }
                var start = new MotionTime(0f, 0.37);
                plain.Evaluate(s, start, out MotionSample p0);
                faded.Evaluate(s, start, out MotionSample f0);
                Assert.That((f0.Value, faded.EvaluateShape(s, start), p0.Value != Vector4.zero), Is.EqualTo((Vector4.zero, 0f, true)), name + " the start");
                if (fadeOut > 0f)
                {
                    var end = new MotionTime(1f, 1.74);
                    plain.Evaluate(s, end, out MotionSample p1);
                    faded.Evaluate(s, end, out MotionSample f1);
                    Assert.That((f1.Value, faded.EvaluateShape(s, end), p1.Value != Vector4.zero), Is.EqualTo((Vector4.zero, 0f, true)), name + " the end");
                }
            }

            foreach (var (name, plain, faded) in Fades(0f, Out, quad).Where(f => !f.Name.StartsWith("Shake", StringComparison.Ordinal)))
            {
                MotionState s = Begin(plain, Vector4.zero, 0.8f);
                var start = new MotionTime(0f, 0.37);
                var end = new MotionTime(1f, 1.74);
                plain.Evaluate(s, start, out MotionSample p0);
                faded.Evaluate(s, start, out MotionSample f0);
                plain.Evaluate(s, end, out MotionSample p1);
                faded.Evaluate(s, end, out MotionSample f1);
                Assert.That((f0.Value, f1.Value, p1.Value != Vector4.zero), Is.EqualTo((p0.Value, Vector4.zero, true)), name + " Fade Out alone");
            }
        }

        // an overshooting Fade Ease is cut to 0 ~ 1 — the faded motion never passes the plain one nor turns it over; a curve at
        // 0.5 at its 0 still ends at exactly 0
        [Test]
        public void FD03_TheHeightStaysWithinZeroAndOne()
        {
            Ease over = Ease.Of(EaseType.Custom);
            over.m_Curve = new AnimationCurve(new Keyframe(0f, -0.4f), new Keyframe(0.5f, 1.6f), new Keyframe(1f, 1f));
            foreach (Ease ease in new[] { Ease.Of(EaseType.BackIn), Ease.Of(EaseType.ElasticOut), over })
            {
                foreach (var (name, plain, faded) in Fades(0.3f, 0.4f, ease))
                {
                    MotionState s = Begin(plain, Vector4.zero, 0.8f);
                    for (int i = 0; i <= 200; i++)
                    {
                        float t = i / 200f;
                        var time = new MotionTime(t, 0.37 + t * 1.37);
                        float a = plain.EvaluateShape(s, time), b = faded.EvaluateShape(s, time);
                        string at = $"{ease.m_Type} {name} t {t}";
                        Assert.That(Mathf.Abs(b), Is.LessThanOrEqualTo(Mathf.Abs(a) * (1f + 1e-6f)), at + " not past");
                        Assert.That(a * b, Is.GreaterThanOrEqualTo(0f), at + " not turned over");
                    }
                }
            }
            Ease half = Ease.Of(EaseType.Custom);
            half.m_Curve = AnimationCurve.Constant(0f, 1f, 0.5f);
            foreach (var (name, plain, faded) in Fades(0.3f, 0.4f, half))
            {
                MotionState s = Begin(plain, Vector4.zero, 0.8f);
                faded.Evaluate(s, new MotionTime(0f, 0.37), out MotionSample f0);
                faded.Evaluate(s, new MotionTime(0.15f, 0.37 + 0.15 * 1.37), out MotionSample mid);
                plain.Evaluate(s, new MotionTime(0.15f, 0.37 + 0.15 * 1.37), out MotionSample pmid);
                Assert.That(f0.Value, Is.EqualTo(Vector4.zero), name + " the start");
                Near(pmid.Value * 0.5f, mid.Value, name + " the slope", 1e-6f);
                if (!name.StartsWith("Shake", StringComparison.Ordinal))
                {
                    faded.Evaluate(s, new MotionTime(1f, 1.74), out MotionSample f1);
                    Assert.That(f1.Value, Is.EqualTo(Vector4.zero), name + " the end");
                }
            }
        }

        // the begin's Endless reaches the state; an endless state's first evaluation (0 s) skips the fade
        [Test]
        public void FD04_AnEndlessStateSkipsTheFade()
        {
            var e = new OscillateEvaluator(new OscillateParams { m_Amplitude = A, m_Frequency = 1f, m_Waveform = Waveform.Square, m_FadeIn = 0.5f }, 4);
            MotionState endless = Begin(e, Vector4.zero, endless: true), finite = Begin(e, Vector4.zero);
            Assert.That((endless.Endless, finite.Endless), Is.EqualTo((true, false)), "the state");
            Assert.That(Eval(e, endless, 0f, 0.0).Value, Is.EqualTo(A), "endless — the first value");
            Assert.That(Eval(e, finite, 0f, 0.0).Value, Is.EqualTo(Vector4.zero), "finite — the fade's start");
        }
    }
}
