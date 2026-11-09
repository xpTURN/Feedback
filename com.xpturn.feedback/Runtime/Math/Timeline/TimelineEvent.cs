using System;

namespace xpTURN.Feedback
{
    internal enum TimelineEventKind : byte
    {
        Started = 0,
        Ended = 1,
        Fired = 2,
        LoopIteration = 3,
        SignalWaitBegan = 4,
        Finished = 5,
    }

    // shared by the sampler and the cursor.
    internal readonly struct TimelineEvent : IEquatable<TimelineEvent>
    {
        public readonly TimelineEventKind Kind;
        public readonly bool Reverse;
        public readonly bool Skipped;
        public readonly bool Truncated;
        public readonly int Item;               // -1: LoopIteration · Finished
        public readonly int Block;              // LoopIteration only, else -1
        public readonly long Occurrence;        // item: running start count; LoopIteration: the block's iteration
        public readonly long Time;              // nominal axis, microseconds
        public readonly long OccurrenceStart;   // start of the occurrence — T = (Time − OccurrenceStart) / natural duration

        public TimelineEvent(TimelineEventKind kind, bool reverse, bool skipped, bool truncated, int item, int block, long occurrence, long time, long occurrenceStart)
        {
            Kind = kind;
            Reverse = reverse;
            Skipped = skipped;
            Truncated = truncated;
            Item = item;
            Block = block;
            Occurrence = occurrence;
            Time = time;
            OccurrenceStart = occurrenceStart;
        }

        // a forward event read backwards
        public TimelineEvent Flipped()
        {
            var kind = Kind switch
            {
                TimelineEventKind.Started => TimelineEventKind.Ended,
                TimelineEventKind.Ended => TimelineEventKind.Started,
                _ => Kind,
            };
            return new TimelineEvent(kind, !Reverse, Skipped, Truncated, Item, Block, Occurrence, Time, OccurrenceStart);
        }

        public TimelineEvent WithOccurrence(long occurrence)
            => new TimelineEvent(Kind, Reverse, Skipped, Truncated, Item, Block, occurrence, Time, OccurrenceStart);

        public bool Equals(TimelineEvent other)
            => Kind == other.Kind && Reverse == other.Reverse && Skipped == other.Skipped && Truncated == other.Truncated
               && Item == other.Item && Block == other.Block && Occurrence == other.Occurrence && Time == other.Time
               && OccurrenceStart == other.OccurrenceStart;

        public override bool Equals(object obj) => obj is TimelineEvent other && Equals(other);

        public override int GetHashCode()
            => HashCode.Combine((int)Kind | (Reverse ? 16 : 0) | (Skipped ? 32 : 0) | (Truncated ? 64 : 0), Item, Block, Occurrence, Time, OccurrenceStart);

        public override string ToString()
            => $"{(Reverse ? "~" : "")}{Kind}(item {Item}, block {Block}, occ {Occurrence}, t {Time}, a {OccurrenceStart}{(Skipped ? ", skipped" : "")}{(Truncated ? ", truncated" : "")})";
    }
}
