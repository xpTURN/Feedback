using System;
using System.Numerics;
using NUnit.Framework;

namespace xpTURN.Feedback.Tests.Motion
{
    // golden values from an independent implementation (Python), the zero state, avalanche, tags, and every pinned value
    // (tags · enum values · ToUnit below 1)
    public sealed class RandomHashTests
    {
        [Test]
        public void XorShiftMatchesTheGoldenValues()
        {
            uint[] golden = { 723471715u, 2497366906u, 2064144800u, 2008045182u, 3532304609u, 374114282u, 1350636274u, 691148861u };
            var random = new XorShiftRandom(2463534242u);
            foreach (uint g in golden)
                Assert.That(random.Next(), Is.EqualTo(g));
        }

        [Test]
        public void ZeroStateStartsFromTheFixedConstant()
        {
            uint[] golden = { 1085196063u, 2447379481u, 2618286376u };
            var seeded = new XorShiftRandom(0u);
            var fresh = default(XorShiftRandom);
            foreach (uint g in golden)
            {
                Assert.That(seeded.Next(), Is.EqualTo(g), "seed 0");
                Assert.That(fresh.Next(), Is.EqualTo(g), "default struct");
            }
        }

        [Test]
        public void HashMatchesTheGoldenValues()
        {
            uint[] golden = { 3793747938u, 589301622u, 1588581860u, 1140386848u, 1121974961u, 3262051103u, 902953348u, 2198054738u };
            for (uint i = 0; i < golden.Length; i++)
                Assert.That(Hash32.Of(77u, i), Is.EqualTo(golden[i]), $"i {i}");
        }

        [TestCase(Hash32.Component, 0x100u, 1876129245u)]
        [TestCase(Hash32.Octave, 0x200u, 2328294252u)]
        [TestCase(Hash32.Copy, 0x300u, 2845059673u)]
        [TestCase(Hash32.Occurrence, 0x400u, 146260515u)]
        [TestCase(Hash32.Chance, 0x500u, 3275285950u)]
        [TestCase(Hash32.Duration, 0x600u, 3612188204u)]
        [TestCase(Hash32.Output, 0x700u, 3143671592u)]
        [TestCase(Hash32.Pick, 0x800u, 2134513043u)]
        public void TagsArePinnedAndTaggedMatchesTheGoldenValue(uint tag, uint pinned, uint golden)
        {
            Assert.That(tag, Is.EqualTo(pinned));
            Assert.That(Hash32.Tagged(77u, tag, 1u), Is.EqualTo(golden));
        }

        [Test]
        public void ATaggedHashDiffersFromTheUntagged()
        {
            for (uint s = 0; s < 1000; s++)
                Assert.That(Hash32.Tagged(s, Hash32.Component, 1u), Is.Not.EqualTo(Hash32.Of(s, 1u)), $"seed {s}");
        }

        [Test]
        public void OneFlippedBitChangesAboutHalfTheBits()
        {
            var random = new XorShiftRandom(5u);
            long sumIndex = 0, sumSeed = 0;
            const int n = 10000;
            for (int j = 0; j < n; j++)
            {
                uint seed = random.Next(), i = random.Next();
                uint h = Hash32.Of(seed, i);
                int bit = j % 32;
                sumIndex += BitOperations.PopCount(h ^ Hash32.Of(seed, i ^ (1u << bit)));
                sumSeed += BitOperations.PopCount(h ^ Hash32.Of(seed ^ (1u << bit), i));
            }
            Assert.That(sumIndex / (double)n, Is.InRange(14.0, 18.0), "index bit");
            Assert.That(sumSeed / (double)n, Is.InRange(14.0, 18.0), "seed bit");
        }

        [Test]
        public void UnitsStayBelowOne()
        {
            Assert.That(Hash32.ToUnit(uint.MaxValue), Is.LessThan(1f));
            Assert.That(Hash32.ToUnit(0u), Is.EqualTo(0f));
            var random = new XorShiftRandom(3u);
            for (int j = 0; j < 100000; j++)
            {
                float u = random.NextUnit();
                Assert.That(u >= 0f && u < 1f, $"{u}");
                double d = Hash32.Unit(3u, (uint)j);
                Assert.That(d >= 0.0 && d < 1.0, $"{d}");
            }
        }

        [Test]
        public void SavedEnumValuesArePinned()
        {
            string[] ease =
            {
                "Linear", "SineIn", "SineOut", "SineInOut", "QuadIn", "QuadOut", "QuadInOut", "CubicIn", "CubicOut", "CubicInOut",
                "QuartIn", "QuartOut", "QuartInOut", "QuintIn", "QuintOut", "QuintInOut", "ExpoIn", "ExpoOut", "ExpoInOut",
                "CircIn", "CircOut", "CircInOut", "BackIn", "BackOut", "BackInOut", "ElasticIn", "ElasticOut", "ElasticInOut",
                "BounceIn", "BounceOut", "BounceInOut", "Custom",
            };
            Assert.That(Enum.GetNames(typeof(EaseType)), Has.Length.EqualTo(ease.Length));
            for (int i = 0; i < ease.Length; i++)
                Assert.That((int)Enum.Parse<EaseType>(ease[i]), Is.EqualTo(i), ease[i]);

            string[] wave = { "Sine", "Triangle", "Square", "Saw", "PingPong" };
            Assert.That(Enum.GetNames(typeof(Waveform)), Has.Length.EqualTo(wave.Length));
            for (int i = 0; i < wave.Length; i++)
                Assert.That((int)Enum.Parse<Waveform>(wave[i]), Is.EqualTo(i), wave[i]);
        }
    }
}
