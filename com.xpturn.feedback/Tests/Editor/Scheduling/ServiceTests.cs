using System;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;

namespace xpTURN.Feedback.Tests.Editor
{
    // EditMode, no play. The factory table lives for the domain, so each test makes its own keys and never shares a
    // registration. The services write no global state
    public class ServiceTests
    {
        static readonly FrameTime s_Frame = new FrameTime(0.016f, 0.016f);
        static FeedbackService s_Sink;

        static FeedbackScheduler NewScheduler() => new FeedbackScheduler(ContextKind.Test);

        static ServiceKey Register(Func<FeedbackContext, FeedbackService> factory)
        {
            var key = new ServiceKey();
            FeedbackContext.RegisterExtension(key, factory);
            return key;
        }

        // ---- a missing factory

        [Test]
        public void VT01_MissingFactoryIsNull()
        {
            var scheduler = NewScheduler();
            var key = new ServiceKey();
            Assert.That(scheduler.Context.GetExtension(key), Is.Null, "no factory");
            FeedbackContext.RegisterExtension(key, c => new TestService(c, "late"));
            Assert.That(scheduler.Context.GetExtension(key), Is.Not.Null, "registered later — the absence is not kept");
        }

        // ---- one instance per context

        [Test]
        public void VT02_OnePerContext()
        {
            var key = Register(c => new TestService(c, "one"));
            var a = NewScheduler();
            var b = NewScheduler();
            var first = a.Context.GetExtension(key);
            Assert.That(first, Is.Not.Null);
            Assert.That(a.Context.GetExtension(key), Is.SameAs(first), "the same context, the same instance");
            Assert.That(b.Context.GetExtension(key), Is.Not.SameAs(first), "another context, another instance");
            Assert.That(first.Context, Is.SameAs(a.Context), "made with the context asked");
        }

        // ---- the service order

        // Update in the order added, Shutdown in reverse; an extension first asked inside another service's Update has no
        // Update that frame; a factory asking for its dependency — the grown array keeps both, the dependency runs first and
        // closes last
        [Test]
        public void VT03_ServiceOrder()
        {
            var record = new CallRecord();
            var scheduler = NewScheduler();
            scheduler.Context.AddService(new TestService(scheduler.Context, "A", record));
            var key = Register(c => new TestService(c, "X", record));
            scheduler.Context.GetExtension(key);
            scheduler.Context.AddService(new TestService(scheduler.Context, "B", record));
            scheduler.Tick(s_Frame);
            scheduler.Shutdown();
            Assert.That(record.Joined, Is.EqualTo("A.Update,X.Update,B.Update,B.Shutdown,X.Shutdown,A.Shutdown"), "Update in order, Shutdown reversed");

            var inner = new CallRecord();
            var second = NewScheduler();
            var lateKey = Register(c => new TestService(c, "L", inner));
            var caller = new TestService(second.Context, "C", inner) { OnUpdate = () => second.Context.GetExtension(lateKey) };
            second.Context.AddService(caller);
            second.Tick(s_Frame);
            Assert.That(inner.Joined, Is.EqualTo("C.Update"), "no Update in the frame it was first asked");
            second.Tick(s_Frame);
            Assert.That(inner.Joined, Is.EqualTo("C.Update,C.Update,L.Update"));

            var nested = new CallRecord();
            var third = NewScheduler();
            var user = new ServiceKey();
            var dependency = new ServiceKey();
            int made = 0;
            FeedbackContext.RegisterExtension(dependency, c =>
            {
                made++;
                return new TestService(c, "D", nested);
            });
            FeedbackContext.RegisterExtension(user, c =>
            {
                c.GetExtension(dependency);
                return new TestService(c, "U", nested);
            });
            Assert.That(third.Context.GetExtension(user), Is.Not.Null);
            var kept = third.Context.GetExtension(dependency);
            Assert.That(third.Context.GetExtension(dependency), Is.SameAs(kept));
            Assert.That(made, Is.EqualTo(1), "the dependency made once");
            third.Tick(s_Frame);
            third.Shutdown();
            Assert.That(nested.Joined, Is.EqualTo("D.Update,U.Update,U.Shutdown,D.Shutdown"), "the dependency first, closed last");
        }

        // ---- a closed context

        [Test]
        public void VT04_ClosedContext()
        {
            var scheduler = NewScheduler();
            var beforeKey = Register(c => new TestService(c, "before"));
            var before = (TestService)scheduler.Context.GetExtension(beforeKey);
            scheduler.Shutdown();
            Assert.That(before.IsClosed, Is.True, "closed with the context");
            Assert.That(scheduler.Context.GetExtension(beforeKey), Is.SameAs(before), "the same instance, closed");
            var afterKey = Register(c => new TestService(c, "after"));
            var after = scheduler.Context.GetExtension(afterKey) as TestService;
            Assert.That(after, Is.Not.Null, "made on a closed context");
            Assert.That(after.IsClosed, Is.True, "and closed at once");
            Assert.That(after.Shutdowns, Is.EqualTo(1));
        }

        // ---- registering again

        [Test]
        public void VT05_RegisteringAgain()
        {
            var key = new ServiceKey();
            FeedbackContext.RegisterExtension(key, c => new TestService(c, "first"));
            var a = NewScheduler();
            var made = (TestService)a.Context.GetExtension(key);
            FeedbackContext.RegisterExtension(key, c => new TestService(c, "second"));
            Assert.That(((TestService)a.Context.GetExtension(key)).Name, Is.EqualTo("first"), "a made instance stays");
            Assert.That(((TestService)NewScheduler().Context.GetExtension(key)).Name, Is.EqualTo("second"), "a new context takes the new factory");
            FeedbackContext.RegisterExtension(key, null);
            Assert.That(NewScheduler().Context.GetExtension(key), Is.Null, "a null factory — absent");
            Assert.That(made.Name, Is.EqualTo("first"));
        }

        // ---- the keys

        [Test]
        public void VT06_Keys()
        {
            var a = new ServiceKey();
            var b = new ServiceKey();
            Assert.That(a.Id, Is.Not.EqualTo(b.Id));
            Assert.That(a.Id, Is.EqualTo(a.Id));
        }

        // ---- no allocation once made (the verdict through Assert.That)

        [Test]
        public void VT07_NoAllocationOnceMade()
        {
            var scheduler = NewScheduler();
            var key = Register(c => new TestService(c, "alloc"));
            scheduler.Context.GetExtension(key);
            TestDelegate get = () => s_Sink = scheduler.Context.GetExtension(key);
            get();
            Assert.That(get, Is.Not.AllocatingGCMemory());
        }

        // ---- the factory's contract

        // another context's service asserts once; a null is not kept — the next call asks again; an exception goes to the
        // caller and nothing is kept
        [Test]
        public void VT08_FactoryContract()
        {
            var scheduler = NewScheduler();
            var other = NewScheduler();
            var foreignKey = Register(c => new TestService(other.Context, "foreign"));
            LogAssert.Expect(LogType.Assert, FeedbackContext.FactoryContextMessage);
            scheduler.Context.GetExtension(foreignKey);

            int nullCalls = 0;
            var nullKey = Register(c =>
            {
                nullCalls++;
                return null;
            });
            Assert.That(scheduler.Context.GetExtension(nullKey), Is.Null);
            Assert.That(scheduler.Context.GetExtension(nullKey), Is.Null);
            Assert.That(nullCalls, Is.EqualTo(2), "a null is not kept");

            int thrown = 0;
            var throwKey = Register(c =>
            {
                thrown++;
                throw new InvalidOperationException("test factory");
            });
            Assert.Throws<InvalidOperationException>(() => scheduler.Context.GetExtension(throwKey), "to the caller");
            Assert.Throws<InvalidOperationException>(() => scheduler.Context.GetExtension(throwKey));
            Assert.That(thrown, Is.EqualTo(2), "nothing kept");
            LogAssert.NoUnexpectedReceived();
        }
    }
}
