namespace xpTURN.Feedback.Tests.Timeline
{
    static class TimelineDescs
    {
        public const long S = 1_000_000L;

        public static TimelineItemDesc Fx(StartRule rule = StartRule.WithPrevious, float delay = 0f, float duration = 1f)
            => new TimelineItemDesc { Kind = TimelineItemKind.Effect, Rule = rule, Delay = delay, Duration = duration };

        public static TimelineItemDesc Instant(StartRule rule = StartRule.WithPrevious, float delay = 0f)
            => new TimelineItemDesc { Kind = TimelineItemKind.Effect, Instant = true, Rule = rule, Delay = delay, Duration = 5f };

        public static TimelineItemDesc Wait(float duration, StartRule rule = StartRule.WithPrevious, float delay = 0f)
            => new TimelineItemDesc { Kind = TimelineItemKind.Wait, Rule = rule, Delay = delay, Duration = duration };

        public static TimelineItemDesc Signal(float timeout = 0f)
            => new TimelineItemDesc { Kind = TimelineItemKind.SignalWait, Timeout = timeout };

        public static TimelineItemDesc LoopStart(StartRule rule = StartRule.WithPrevious, float delay = 0f)
            => new TimelineItemDesc { Kind = TimelineItemKind.LoopStart, Rule = rule, Delay = delay };

        public static TimelineItemDesc LoopEnd(int count = 2, float gap = 0f)
            => new TimelineItemDesc { Kind = TimelineItemKind.LoopEnd, LoopCount = count, LoopGap = gap };

        public static TimelineItemDesc Hold(float delay = 0f)
            => new TimelineItemDesc { Kind = TimelineItemKind.Hold, Delay = delay };

        public static TimelineItemDesc Background(TimelineItemDesc d)
        {
            d.Background = true;
            return d;
        }

        public static TimelineItemDesc Repeat(TimelineItemDesc d, int repeat, float gap = 0f)
        {
            d.Repeat = repeat;
            d.RepeatGap = gap;
            return d;
        }

        public static TimelineItemDesc Clock(TimelineItemDesc d, TimelineClock clock)
        {
            d.Clock = clock;
            return d;
        }

        public static TimelineProgram Compile(params TimelineItemDesc[] items) => TimelineProgram.Compile(items);
    }
}
