using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools.Constraints;
using static xpTURN.Feedback.Tests.Editor.MotionEvaluatorTests;
using Is = UnityEngine.TestTools.Constraints.Is;

namespace xpTURN.Feedback.Tests.Editor
{
    // The constraint also counts allocations that are not the delegate's — in 6000.3 an empty delegate is reported 1 ~ 9
    // times in 200 and the evaluators 3 ~ 5% of the time (6000.6: none). The code's own allocation is there every time, so
    // "allocates" takes five reports in a row. The delegate measured is one already run
    public sealed class MotionAllocationTests
    {
        static bool Allocates(TestDelegate run)
        {
            for (int i = 0; i < 5; i++)
                if (!Is.AllocatingGCMemory().ApplyTo(run).IsSuccess)
                    return false;
            return true;
        }

        [Test]
        public void E07_EvaluationAllocatesNothing()
        {
            TweenParams custom = Tween(TweenMode.FromTo, EaseType.Custom);
            custom.m_Ease.m_Curve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
            foreach (var e in All(4).Concat(new MotionEvaluator[] { new TweenEvaluator(custom, 4) }))
            {
                var s = new MotionState();
                var args = new MotionBegin { Seed = 9u, Intensity = 1f };
                var current = new Vector4(1f, 2f, 3f, 4f);
                var time = new MotionTime(0.4f, 0.4 * Item);

                void Run()
                {
                    e.Begin(ref s, current, args);
                    e.Evaluate(s, time, out MotionSample sample);
                    e.EvaluateShape(s, time);
                    if (e is TweenEvaluator tween)
                    {
                        tween.Endpoints(s, out Vector4 from, out Vector4 to);
                        tween.Progress(time);
                    }
                }

                TestDelegate run = Run;
                run();
                Assert.That(Allocates(run), Is.False, e.GetType().Name);
            }
        }

        // the positive control — a curve's keys getter allocates
        [Test]
        public void E07_TheKeysGetterIsSeenAllocating()
        {
            AnimationCurve curve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
            int sink = 0;
            TestDelegate run = () => sink += curve.keys.Length;
            run();
            Assert.That(Allocates(run), Is.True);
        }
    }
}
