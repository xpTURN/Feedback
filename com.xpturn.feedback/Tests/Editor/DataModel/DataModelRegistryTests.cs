using System.Linq;
using NUnit.Framework;

namespace xpTURN.Feedback.Tests.Editor
{
    public sealed class RegistryTarget : TargetItem
    {
        static readonly TargetTraits s_Traits = new TargetTraits(valueKind: ValueKind.Float, supportedMotions: MotionMask.Tween,
            category: FeedbackCategory.Motion, flashSensitive: false, applyMode: ApplyMode.Offset);

        internal override TargetTraits Traits => s_Traits;
    }

    public abstract class RegistryTestAction : ActionItem
    {
        internal override FeedbackCategory DeclaredCategory => FeedbackCategory.Motion;
        internal override float NaturalDuration => 0f;
    }

    public sealed class RegistryAction : RegistryTestAction
    {
    }

    public class RegistryBase : RegistryTestAction
    {
    }

    // no test registers these two
    public sealed class RegistryDerived : RegistryBase
    {
    }

    public sealed class RegistryUnregistered : RegistryTestAction
    {
    }

    public sealed class RegistryReplaced : RegistryTestAction
    {
    }

    internal sealed class RegistryBinding : ItemBinding
    {
        internal readonly EffectItem Item;
        internal readonly string Tag;

        internal RegistryBinding(EffectItem item, string tag)
        {
            Item = item;
            Tag = tag;
        }

        internal override bool Prepare(in PrepareContext context) => true;

        internal override void Begin(int record, in BeginContext context)
        {
        }

        internal override void Evaluate(int record, in MotionTime time)
        {
        }

        internal override void End(int record, EndReason reason)
        {
        }
    }

    // the registry is global and never cleared — each test registers the types it uses,
    // and the 'not registered' cases use types no test registers, so no test depends on the order
    public sealed class DataModelRegistryTests
    {
        [Test]
        public void CreateReturnsTheBindingOfTheRegisteredFactory()
        {
            RegistryBinding made = null;
            FeedbackImplementations.Register<RegistryTarget>(item => made = new RegistryBinding(item, "target"));
            FeedbackImplementations.Register<RegistryAction>(item => made = new RegistryBinding(item, "action"));

            var target = new RegistryTarget();
            Assert.That(FeedbackImplementations.Create(target), Is.SameAs(made), "target");
            Assert.That(made.Item, Is.SameAs(target), "the factory got the item");
            Assert.That(made.Tag, Is.EqualTo("target"));

            var action = new RegistryAction();
            Assert.That(FeedbackImplementations.Create(action), Is.SameAs(made), "action");
            Assert.That(made.Item, Is.SameAs(action), "the factory got the item");
            Assert.That(made.Tag, Is.EqualTo("action"));
        }

        [Test]
        public void OnlyTheExactRegisteredTypeGetsABinding()
        {
            FeedbackImplementations.Register<RegistryBase>(item => new RegistryBinding(item, "base"));

            Assert.That(FeedbackImplementations.Create(new RegistryBase()), Is.Not.Null, "the registered type");
            Assert.That(FeedbackImplementations.Create(new RegistryDerived()), Is.Null, "a type derived from a registered type");
            Assert.That(FeedbackImplementations.Create(new RegistryUnregistered()), Is.Null, "a type no test registers");
            Assert.That(FeedbackImplementations.Create(null), Is.Null, "null");
        }

        [Test]
        public void RegisteringAgainReplacesTheFactoryAndListsTheTypeOnce()
        {
            FeedbackImplementations.Register<RegistryReplaced>(item => new RegistryBinding(item, "first"));
            FeedbackImplementations.Register<RegistryReplaced>(item => new RegistryBinding(item, "second"));

            var binding = FeedbackImplementations.Create(new RegistryReplaced()) as RegistryBinding;
            Assert.That(binding?.Tag, Is.EqualTo("second"), "the later factory wins");
            Assert.That(FeedbackImplementations.RegisteredTypes.Count(t => t == typeof(RegistryReplaced)), Is.EqualTo(1), "RegisteredTypes");
        }
    }
}
