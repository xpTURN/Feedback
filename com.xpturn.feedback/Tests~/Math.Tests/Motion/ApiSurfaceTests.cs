using System.Linq;
using NUnit.Framework;

namespace xpTURN.Feedback.Tests.Motion
{
    // the Math assembly's public types are the saved enums only — the kernels are internal
    public sealed class ApiSurfaceTests
    {
        [Test]
        public void OnlyTheSavedEnumsArePublic()
        {
            var names = typeof(EaseType).Assembly.GetExportedTypes().Select(t => t.FullName).OrderBy(n => n).ToArray();
            Assert.That(names, Is.EqualTo(new[] { "xpTURN.Feedback.EaseType", "xpTURN.Feedback.StartRule", "xpTURN.Feedback.Waveform" }));
        }
    }
}
