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

        internal static MotionState Begin(MotionEvaluator e, Vector4 current, float intensity = 1f, uint seed = 12345u)
        {
            var s = new MotionState();
            var args = new MotionBegin { Seed = seed, Intensity = intensity };
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

        // the eleven — Spring in both modes, Gradient in both writes
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
            Assert.That(types.Length, Is.EqualTo(11));
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
    }
}
