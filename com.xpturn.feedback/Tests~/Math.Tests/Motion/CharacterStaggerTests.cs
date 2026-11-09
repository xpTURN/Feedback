using NUnit.Framework;

namespace xpTURN.Feedback.Tests.Motion
{
    // a character's share (mirrored backward) · its Tween window — every character ends inside the item · an Oscillate's lag
    // of s · k turns
    public sealed class CharacterStaggerTests
    {
        // a character's seconds — its share of the contribution's lag
        static double At(double seconds, float t, float stagger, float share, float frequency)
            => CharacterStagger.Lagged(seconds, CharacterStagger.Lag(seconds, t, stagger, frequency), share);

        [Test]
        public void S1_TheShare()
        {
            Assert.That(CharacterStagger.Share(0, 1, false), Is.EqualTo(0f), "one character");
            Assert.That(CharacterStagger.Share(0, 5, false), Is.EqualTo(0f));
            Assert.That(CharacterStagger.Share(4, 5, false), Is.EqualTo(1f));
            Assert.That(CharacterStagger.Share(1, 5, false), Is.EqualTo(0.25f));
            Assert.That(CharacterStagger.Share(1, 5, true), Is.EqualTo(0.75f), "backward mirrors");
            Assert.That(CharacterStagger.Share(4, 5, true), Is.EqualTo(0f));
        }

        [Test]
        public void S2_TheWindow()
        {
            Assert.That(CharacterStagger.Window(0.3f, 0f, 1f), Is.EqualTo(0.3f), "no stagger — the item's T");
            Assert.That(CharacterStagger.Window(0.5f, 0.5f, 0f), Is.EqualTo(1f), "the first ends at 1 − s");
            Assert.That(CharacterStagger.Window(0.5f, 0.5f, 1f), Is.EqualTo(0f), "the last starts at s");
            Assert.That(CharacterStagger.Window(0.75f, 0.5f, 1f), Is.EqualTo(0.5f));
            for (int i = 0; i <= 10; i++)
            {
                float k = i / 10f;
                Assert.That(CharacterStagger.Window(1f, 0.5f, k), Is.EqualTo(1f), "every character at 1 when the item ends — " + k);
                Assert.That(CharacterStagger.Window(0f, 0.5f, k), Is.EqualTo(0f), "every character at 0 when it starts — " + k);
            }
            Assert.That(CharacterStagger.Window(1f, 1f, 1f), Is.EqualTo(1f), "a stagger past 0.95 is cut — the last still ends");
            Assert.That(CharacterStagger.Window(0.4f, float.NaN, 1f), Is.EqualTo(0.4f), "NaN is no stagger");
            Assert.That(CharacterStagger.Window(float.NaN, 0.5f, 0.5f), Is.EqualTo(0f), "a NaN time is 0");
        }

        [Test]
        public void S3_TheLag()
        {
            Assert.That(At(2.0, 0f, 0.5f, 1f, 2f), Is.EqualTo(1.75).Within(1e-9), "s · k / F seconds");
            Assert.That(At(2.0, 0f, 0.5f, 0f, 2f), Is.EqualTo(2.0), "the first keeps the item's seconds");
            Assert.That(At(2.0, 0f, 0.5f, 1f, 0f), Is.EqualTo(2.0), "a frequency of 0 has no turn");
            Assert.That(At(2.0, 0f, 0f, 1f, 2f), Is.EqualTo(2.0), "no stagger");
            Assert.That(At(2.0, 0f, 2f, 1f, 2f), Is.EqualTo(1.525).Within(1e-6), "a stagger past 0.95 is cut as the window's");
            Assert.That(At(1.0, float.NaN, 0.5f, 1f, 2f), Is.EqualTo(1.0), "a NaN time has no lag");
        }

        // a late character waits at the press (no time below 0) and the lag closes over the item's last stagger, at most
        // 2s / F seconds — every character starts at the wave's start and ends where the first does, with no step in between
        [Test]
        public void S4_TheLagStartsAndEndsAtRest()
        {
            Assert.That(At(0.05, 0.025f, 0.5f, 1f, 2f), Is.EqualTo(0.0), "the last waits at 0 after the press");
            Assert.That(At(1.0, 0.5f, 0.5f, 1f, 2f), Is.EqualTo(0.75).Within(1e-9), "the whole lag before the last stagger");
            Assert.That(At(1.5, 0.75f, 0.5f, 1f, 2f), Is.EqualTo(1.25).Within(1e-9), "the whole lag before the last 2s / F seconds");
            Assert.That(At(1.75, 0.875f, 0.5f, 1f, 2f), Is.EqualTo(1.625).Within(1e-9), "half the lag half-way through them");
            for (int i = 0; i <= 4; i++)
                Assert.That(At(2.0, 1f, 0.5f, i / 4f, 2f), Is.EqualTo(2.0), "every character at the item's seconds at its end — " + i);
            Assert.That(At(10.0, 0f, 0.5f, 1f, 2f), Is.EqualTo(9.75).Within(1e-9), "an item without an end (T 0) keeps the whole lag");

            // a 0.3 s item at 5 turns a second — each character's seconds never go back and step at most twice the item's pace (the
            // catch-up is 1 + k / (F · D) = 1.67 of it)
            const double d = 0.3;
            const int n = 300;
            for (int i = 0; i <= 4; i++)
            {
                float k = i / 4f;
                double previous = At(0.0, 0f, 0.5f, k, 5f);
                Assert.That(previous, Is.EqualTo(0.0), "every character at 0 at the press — " + k);
                for (int j = 1; j <= n; j++)
                {
                    double v = At(d * j / n, (float)j / n, 0.5f, k, 5f);
                    Assert.That(v - previous, Is.InRange(-1e-12, 2.0 * d / n), $"the step at {j} — {k}");
                    previous = v;
                }
                Assert.That(previous, Is.EqualTo(d).Within(1e-9), "at the item's seconds at its end — " + k);
            }
        }

        // the lag follows the item's turns u = F · D — an item under one turn spreads its share s of the item, a long one
        // keeps the whole lag until the last 2s / F seconds — and a character's seconds never go back, step at most twice the
        // item's pace and end at the item's
        [Test]
        public void S5_TheLagFollowsTheTurns([Values(0.5f, 0.95f)] float s)
        {
            // (seconds, turns a second) — u 0.2 · 0.5 · 1.5 · 4 · 20. Under one turn the last character runs at exactly twice the
            // pace — the step's room is the float T's
            var items = new (double D, float F)[] { (0.2, 1f), (0.25, 2f), (0.3, 5f), (2.0, 2f), (10.0, 2f) };
            const int n = 300;
            foreach (var (d, f) in items)
            {
                double u = f * d;
                for (int i = 0; i <= 4; i++)
                {
                    float k = i / 4f;
                    double previous = At(0.0, 0f, s, k, f);
                    for (int j = 1; j <= n; j++)
                    {
                        double v = At(d * j / n, (float)j / n, s, k, f);
                        Assert.That(v - previous, Is.InRange(-1e-12, 2.0 * d / n + 1e-6 * d), $"u {u} · the step at {j} — {k}");
                        previous = v;
                    }
                    Assert.That(previous, Is.EqualTo(d).Within(1e-9), $"u {u} · at the item's seconds at its end — {k}");
                }
                if (u < 1.0)
                {
                    float t = (1f - s) / 2f;
                    Assert.That(CharacterStagger.Lag(d * t, t, s, f), Is.EqualTo(s * d).Within(1e-6), $"u {u} · the lag is s of the item");
                }
                if (u >= 2.0)
                {
                    double at = d * 0.99 - 2.0 * s / f;
                    Assert.That(CharacterStagger.Lag(at, (float)(at / d), s, f), Is.EqualTo(s / f).Within(1e-6), $"u {u} · the whole lag before the last 2s / F seconds");
                }
            }
        }
    }
}
