namespace xpTURN.Feedback
{
    // one play's buffers, made at the initialization. The cursor and the sampler keep the resolved buffer they were made with
    // — a record's three belong together
    internal sealed class PlaybackRecord
    {
        internal readonly ResolvedTimeline Resolved;
        internal readonly TimelineSampler Sampler;
        internal readonly TimelineCursor Cursor;

        internal bool Active;
        internal bool Paused;
        // a track's play: driven from its Play (unconditional, nominal, no time scale), held while the clock must not move it
        internal bool Driven;
        internal bool Held;
        internal bool Finished;
        internal float Intensity;
        internal uint Seed;
        internal long Serial;               // the play's number in this player — 0 before the first play
        internal double StartedAt;          // the context's time when the play was accepted
        internal PlaybackHandle Handle;
        internal readonly bool[] Running;   // by program item — a timed item between its Begin and its End
        internal readonly bool[] Touched;
        internal readonly bool[] Turned;    // by program item — fixed when the play is accepted   // by program item — begun or executed in this play (Stop(Revert))
        internal readonly float[] Output;   // by program item — this play's random output multiplier
        internal readonly int[] Ended;      // the items the pass or the end ended, each once
        internal readonly bool[] InEnded;
        internal int EndedCount;
        // a backward play is the sampler's head on ν's clock
        internal bool Reverse;
        internal TimelineClock ReverseClock;
        // inside the play's events a Seek · Skip waits for the pass's end — once a pass
        internal bool InEvents;
        internal bool PendingSkip;
        // a Seek's or a Skip's sweep runs — its events carry Swept (the sampler alone can't tell a sweep from the backward head)
        internal bool Sweeping;
        internal bool HasPendingSeek;
        internal long PendingSeek;          // on the nominal axis, microseconds
        // a turn asked inside the play's events waits for the pass's end too
        internal bool HasPendingTurn;
        internal bool PendingTurnReverse;
        // the Begin window's repeat boundaries come after Played
        internal bool InBegin;
        internal int BeginLooped;
        // the play's place — a channel shake's distance — and the player's place at the Play, read when the player is gone
        internal bool HasPosition;
        internal UnityEngine.Vector3 Position;
        internal UnityEngine.Vector3 OwnerPlace;
        // the play's listener — taken when it is called — and its token's entry
        internal IPlaybackListener Listener;
        internal int Token = -1;

        internal PlaybackRecord(TimelineProgram program)
        {
            Resolved = new ResolvedTimeline(program);
            Sampler = new TimelineSampler(program, Resolved);
            Cursor = new TimelineCursor(program, Resolved, Sampler);
            int count = program.ItemCount;
            Running = new bool[count];
            Touched = new bool[count];
            Turned = new bool[count];
            Output = new float[count];
            Ended = new int[count];
            InEnded = new bool[count];
        }
    }
}
