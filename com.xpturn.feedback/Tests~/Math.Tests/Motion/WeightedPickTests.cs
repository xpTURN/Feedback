using NUnit.Framework;

namespace xpTURN.Feedback.Tests.Motion
{
    // one index by its weight — a weight that is not finite and positive counts 0, −1 when none counts; the occurrences'
    // units (the Pick tag) land in proportion
    public sealed class WeightedPickTests
    {
        [Test]
        public void W1_TheBoundaries()
        {
            float[] w = { 1f, 3f, 0f, float.NaN, -1f, 6f };
            Assert.That(WeightedPick.Index(w, w.Length, 0f), Is.EqualTo(0));
            Assert.That(WeightedPick.Index(w, w.Length, 0.0999f), Is.EqualTo(0));
            Assert.That(WeightedPick.Index(w, w.Length, 0.1f), Is.EqualTo(1));
            Assert.That(WeightedPick.Index(w, w.Length, 0.3999f), Is.EqualTo(1));
            Assert.That(WeightedPick.Index(w, w.Length, 0.4f), Is.EqualTo(5), "0 · NaN · −1 count nothing");
            Assert.That(WeightedPick.Index(w, w.Length, 0.99999994f), Is.EqualTo(5), "the last one");
        }

        [Test]
        public void W2_WhatCountsNothing()
        {
            Assert.That(WeightedPick.Index(new[] { 2f, float.PositiveInfinity }, 2, 0.99f), Is.EqualTo(0), "+inf counts 0");
            Assert.That(WeightedPick.Index(new[] { 0f, -1f, float.NaN }, 3, 0.5f), Is.EqualTo(-1), "none counts");
            Assert.That(WeightedPick.Index(new float[0], 0, 0.5f), Is.EqualTo(-1), "empty");
            Assert.That(WeightedPick.Index(new[] { 1f, 1f, 100f }, 2, 0.9f), Is.EqualTo(1), "the count bounds the list");
        }

        [Test]
        public void W3_TheOccurrencesLandInProportion()
        {
            float[] w = { 1f, 3f, 0f, 6f };
            var counts = new int[w.Length];
            const int runs = 20000;
            for (long occurrence = 0; occurrence < runs; occurrence++)
                counts[WeightedPick.Index(w, w.Length, Hash32.ToUnit(Hash32.Tagged(77u, Hash32.Pick, Hash32.Fold(occurrence))))]++;
            Assert.That(counts[2], Is.EqualTo(0));
            Assert.That(counts[0] / (double)runs, Is.EqualTo(0.1).Within(0.01));
            Assert.That(counts[1] / (double)runs, Is.EqualTo(0.3).Within(0.015));
            Assert.That(counts[3] / (double)runs, Is.EqualTo(0.6).Within(0.015));
        }
    }
}
