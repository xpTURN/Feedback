using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace xpTURN.Feedback.Tests.Editor
{
    // the concrete slots and bindings inherit their closed generic base directly and are sealed and not generic (a type closed
    // only at run time takes IL2CPP's shared slow path); every registered target item has an evaluator for each motion it
    // takes. The same rule on stand-ins of the test's own shows it can fail
    public sealed class DataModelAotTests
    {
        abstract class TestBase<T> { }
        sealed class Good : TestBase<int> { }
        class NotSealed : TestBase<int> { }
        sealed class Generic<T> : TestBase<T> { }
        abstract class Between : TestBase<float> { }
        sealed class ViaBetween : Between { }

        static List<string> Breaking(IEnumerable<Type> types, Type[] bases)
        {
            var bad = new List<string>();
            foreach (Type t in types)
            {
                if (t.IsInterface || t.IsAbstract)
                    continue;
                for (Type b = t.BaseType; b != null; b = b.BaseType)
                {
                    if (!b.IsGenericType || !bases.Contains(b.GetGenericTypeDefinition()))
                        continue;
                    if (t.IsGenericType || !t.IsSealed || t.BaseType != b)
                        bad.Add(t.Name);
                    break;
                }
            }
            return bad;
        }

        [Test]
        public void GE1_Aot()
        {
            var fail = new List<string>();
            var bases = new[] { typeof(ValueSlot<,>), typeof(ValueTargetBinding<,>) };
            Type[] core = typeof(FeedbackPlayer).Assembly.GetTypes();
            List<string> bad = Breaking(core, bases);
            int concrete = core.Count(t => !t.IsAbstract && !t.IsInterface && t.BaseType != null && t.BaseType.IsGenericType && bases.Contains(t.BaseType.GetGenericTypeDefinition()));
            if (bad.Count > 0 || concrete == 0)
                fail.Add("Core — " + concrete + " concrete · breaking [" + string.Join(" ", bad) + "]");
            // the optional assemblies registered here — their slots and bindings close the same generic value types for IL2CPP
            foreach (System.Reflection.Assembly optional in FeedbackImplementations.RegisteredTypes
                .Select(t => FeedbackImplementations.Create((EffectItem)Activator.CreateInstance(t))?.GetType().Assembly)
                .Where(a => a != null && a != typeof(FeedbackPlayer).Assembly).Distinct())
            {
                List<string> breaking = Breaking(optional.GetTypes(), bases);
                if (breaking.Count > 0)
                    fail.Add(optional.GetName().Name + " — breaking [" + string.Join(" ", breaking) + "]");
            }
            List<string> stand = Breaking(new[] { typeof(Good), typeof(NotSealed), typeof(Generic<>), typeof(ViaBetween) }, new[] { typeof(TestBase<>) });
            if (string.Join(" ", stand) != "NotSealed Generic`1 ViaBetween")
                fail.Add("the stand-ins — [" + string.Join(" ", stand) + "]");
            Type[] motions = core.Where(t => t.IsSubclassOf(typeof(FeedbackMotion)) && !t.IsAbstract).ToArray();
            int pairs = 0;
            var missing = new List<string>();
            foreach (Type itemType in FeedbackImplementations.RegisteredTypes.Where(t => t.IsSubclassOf(typeof(TargetItem))))
            {
                var item = (TargetItem)Activator.CreateInstance(itemType);
                foreach (Type mt in motions)
                {
                    var motion = (FeedbackMotion)Activator.CreateInstance(mt);
                    if (!item.Traits.Supports(motion.Kind))
                        continue;
                    pairs++;
                    if (MotionEvaluators.Create(motion, 4) == null)
                        missing.Add(itemType.Name + " " + mt.Name);
                }
            }
            if (pairs == 0 || missing.Count > 0)
                fail.Add("evaluators — " + pairs + " pairs · missing [" + string.Join(" | ", missing) + "]");
            Assert.That(fail, Is.Empty);
        }
    }
}
