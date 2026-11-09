using System.Linq;
using System.Reflection;
using NUnit.Framework;

namespace xpTURN.Feedback.Tests.Editor
{
    // the scheduler's public surface — two types, Reinstall and the handle's equality
    public class SchedulerSurfaceTests
    {
        static readonly string[] s_SchedulerTypes =
        {
            "PlaybackHandle", "FeedbackPlayerLoop", "FeedbackScheduler", "FeedbackContext", "FeedbackService", "IPlaybackRunner", "PlaybackSlot",
            "FrameTime", "AbortReason", "ContextKind", "ActivePlayback", "ILayerStages", "PropertyLayerRegistry", "ConsumptionStage",
        };

        const BindingFlags Declared = BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly;

        [Test]
        public void PT06_PublicSurface()
        {
            var core = typeof(FeedbackScheduler).Assembly;
            var publicTypes = s_SchedulerTypes.Where(name => core.GetType("xpTURN.Feedback." + name, true).IsPublic).ToArray();
            Assert.That(publicTypes, Is.EquivalentTo(new[] { "PlaybackHandle", "FeedbackPlayerLoop" }));
            Assert.That(typeof(FeedbackPlayerLoop).GetMembers(Declared).Select(m => m.Name), Is.EquivalentTo(new[] { "Reinstall" }));
            Assert.That(typeof(PlaybackHandle).GetMembers(Declared).Select(m => m.Name),
                Is.EquivalentTo(new[] { "Equals", "Equals", "GetHashCode", "op_Equality", "op_Inequality", "ToString" }));
        }
    }
}
