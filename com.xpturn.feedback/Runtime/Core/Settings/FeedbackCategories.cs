using UnityEngine;

namespace xpTURN.Feedback
{
    // the categories' state of one context — on · scale · the flash limit and its window of starts. Filled from the
    // settings when the context first asks. None is outside the gate (on, scale 1)
    internal sealed class CategoryGate
    {
        readonly FeedbackContext m_Context;
        readonly bool[] m_Enabled;
        readonly float[] m_Scale;
        int m_FlashLimit;
        // the starts in the last second: when · how many flashes. A start is taken while the sum is under the limit and counts
        // one or more, so the window holds at most `limit` entries
        double[] m_FlashTimes;
        int[] m_FlashCounts;
        int m_FlashHead;
        int m_FlashUsed;
        // the starts the limit held back — a preview's notice tells them
        int m_FlashHeld;

        internal CategoryGate(FeedbackContext context, SettingsValues values)
        {
            m_Context = context;
            m_Enabled = (bool[])values.Enabled.Clone();
            m_Scale = (float[])values.Scale.Clone();
            m_FlashLimit = values.FlashLimit;
            int capacity = Mathf.Max(1, m_FlashLimit);
            m_FlashTimes = new double[capacity];
            m_FlashCounts = new int[capacity];
        }

        internal bool IsEnabled(FeedbackCategory category)
        {
            int i = (int)category;
            return i <= 0 || i >= m_Enabled.Length || m_Enabled[i];
        }

        // what a running play multiplies its output by — a category turned off is 0
        internal float Scale(FeedbackCategory category)
        {
            int i = (int)category;
            if (i <= 0 || i >= m_Enabled.Length)
                return 1f;
            return m_Enabled[i] ? m_Scale[i] : 0f;
        }

        internal float RawScale(FeedbackCategory category)
        {
            int i = (int)category;
            return i <= 0 || i >= m_Scale.Length ? 1f : m_Scale[i];
        }

        internal int FlashLimit => m_FlashLimit;

        internal void SetEnabled(FeedbackCategory category, bool enabled) => m_Enabled[(int)category] = enabled;

        internal void SetScale(FeedbackCategory category, float scale) => m_Scale[(int)category] = scale;

        // a raised limit keeps the window's entries; a lowered one keeps them too — the next start reads the new limit
        internal void SetFlashLimit(int limit)
        {
            m_FlashLimit = limit;
            if (limit <= m_FlashTimes.Length)
                return;
            var times = new double[limit];
            var counts = new int[limit];
            for (int k = 0; k < m_FlashUsed; k++)
            {
                int at = (m_FlashHead + k) % m_FlashTimes.Length;
                times[k] = m_FlashTimes[at];
                counts[k] = m_FlashCounts[at];
            }
            m_FlashTimes = times;
            m_FlashCounts = counts;
            m_FlashHead = 0;
        }

        // a start of `count` flashes — taken while the last second's flashes are under the limit, on the context's
        // clock (unscaled)
        internal bool TryFlash(int count)
        {
            double now = m_Context.Now;
            while (m_FlashUsed > 0 && now - m_FlashTimes[m_FlashHead] >= 1.0)
            {
                m_FlashHead = (m_FlashHead + 1) % m_FlashTimes.Length;
                m_FlashUsed--;
            }
            long sum = 0;
            for (int k = 0; k < m_FlashUsed; k++)
                sum += m_FlashCounts[(m_FlashHead + k) % m_FlashTimes.Length];
            if (sum >= m_FlashLimit)
            {
                m_FlashHeld++;
                return false;
            }
            if (m_FlashUsed == m_FlashTimes.Length)
            {
                // cannot happen while each entry counts one or more — the oldest goes
                m_FlashHead = (m_FlashHead + 1) % m_FlashTimes.Length;
                m_FlashUsed--;
            }
            int tail = (m_FlashHead + m_FlashUsed) % m_FlashTimes.Length;
            m_FlashTimes[tail] = now;
            m_FlashCounts[tail] = count;
            m_FlashUsed++;
            return true;
        }

        // observation
        internal int FlashEntries => m_FlashUsed;

        internal int FlashHeld => m_FlashHeld;

        // the flashes of the starts in the last second — the window is not trimmed (a read changes nothing; FlashEntries
        // counts the starts, the old ones until the next start)
        internal int FlashesInLastSecond(double now)
        {
            long sum = 0;
            for (int k = 0; k < m_FlashUsed; k++)
            {
                int at = (m_FlashHead + k) % m_FlashTimes.Length;
                if (now - m_FlashTimes[at] < 1.0)
                    sum += m_FlashCounts[at];
            }
            return (int)sum;
        }
    }

    // the public switches — the loop's context (Default), started from the settings each session. Outside play there is
    // none: a write warns once and does nothing, a read is the default. None, an undefined category, a negative or
    // non-finite scale and a negative limit are ignored with a warning
    /// <summary>Turns effect categories on and off, scales them and limits flashes, for the play session.</summary>
    public static class FeedbackCategories
    {
        internal const string NoContextWarning = "FeedbackCategories was called with no running Feedback context (outside play) and was ignored.";
        internal const string ArgumentWarning = "FeedbackCategories was given a category or a value it does not take (None, an undefined category, a negative or non-finite scale, a negative limit); it was ignored.";
#if DEBUG
        static bool s_Warned;
        static bool s_WarnedArgument;
#endif

        static CategoryGate Current()
        {
            FeedbackScheduler s = FeedbackScheduler.Default;
            return s != null && !s.IsShutdown ? s.Context.Categories : null;
        }

        // a write's context
        static CategoryGate Gate()
        {
            CategoryGate gate = Current();
#if DEBUG
            if (gate == null && !s_Warned)
            {
                s_Warned = true;
                Debug.LogWarning(NoContextWarning);
            }
#endif
            return gate;
        }

        static void WarnArgument()
        {
#if DEBUG
            if (!s_WarnedArgument)
            {
                s_WarnedArgument = true;
                Debug.LogWarning(ArgumentWarning);
            }
#endif
        }

        static bool Gated(FeedbackCategory category) => category > FeedbackCategory.None && (int)category < SettingsValues.CategoryCount;

        /// <summary>Turns <paramref name="category"/> on or off; while it is off, new plays leave it out, the offsets and overrides of running plays weigh 0 and its rumble stops — Persist writes go on.</summary>
        public static void SetEnabled(FeedbackCategory category, bool enabled)
        {
            if (!Gated(category))
            {
                WarnArgument();
                return;
            }
            Gate()?.SetEnabled(category, enabled);
        }

        /// <summary>True when <paramref name="category"/> is on; true outside play.</summary>
        public static bool IsEnabled(FeedbackCategory category) => Current()?.IsEnabled(category) ?? true;

        /// <summary>Scales the strength of <paramref name="category"/>'s offsets and overrides; 1 leaves them as they are. Persist writes (tweens, sets) are not scaled.</summary>
        public static void SetScale(FeedbackCategory category, float scale)
        {
            if (!Gated(category) || !(scale >= 0f && scale < float.PositiveInfinity))
            {
                WarnArgument();
                return;
            }
            Gate()?.SetScale(category, scale);
        }

        /// <summary>The scale of <paramref name="category"/>; 1 outside play.</summary>
        public static float GetScale(FeedbackCategory category) => Current()?.RawScale(category) ?? 1f;

        /// <summary>Lets a flash start while fewer than <paramref name="perSecond"/> flashes started in the last second, then counts all of its own (a Blink counts each blink); 0 lets none start.</summary>
        public static void SetFlashLimit(int perSecond)
        {
            if (perSecond < 0)
            {
                WarnArgument();
                return;
            }
            Gate()?.SetFlashLimit(perSecond);
        }

        /// <summary>The most flashes that start in any second; 3 outside play.</summary>
        public static int FlashLimit => Current()?.FlashLimit ?? 3;
    }
}
