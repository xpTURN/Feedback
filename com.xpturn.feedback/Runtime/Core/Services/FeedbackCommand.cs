namespace xpTURN.Feedback
{
    // every call a player takes, one command shape — the channel is required (Stop · Restore too). A command made without
    // its constructor is channel 0's Play
    /// <summary>What a FeedbackCommand asks the players on its channel to do.</summary>
    public enum FeedbackCommandKind
    {
        /// <summary>Play, with the command's <see cref="FeedbackCommand.Args"/>.</summary>
        Play = 0,
        /// <summary>Stop, with the command's <see cref="FeedbackCommand.Mode"/>.</summary>
        Stop = 1,
        /// <summary>Pause the plays.</summary>
        Pause = 2,
        /// <summary>Resume the paused plays.</summary>
        Resume = 3,
        /// <summary>Seek to the command's <see cref="FeedbackCommand.Time"/>.</summary>
        Seek = 4,
        /// <summary>Skip the plays to their end.</summary>
        Skip = 5,
        /// <summary>Restore the start values.</summary>
        Restore = 6,
        /// <summary>End the plays' signal waits.</summary>
        Signal = 7,
    }

    /// <summary>One call for the players on a channel; default is a Play on channel 0.</summary>
    public readonly struct FeedbackCommand
    {
        readonly FeedbackCommandKind m_Kind;
        readonly ChannelRef m_Channel;
        readonly PlayArgs m_Args;
        readonly StopMode m_Mode;
        readonly float m_Time;

        /// <summary>A command of <paramref name="kind"/> for <paramref name="channel"/>, with the default arguments.</summary>
        public FeedbackCommand(FeedbackCommandKind kind, ChannelRef channel)
            : this(kind, channel, default, StopMode.Hold, 0f)
        {
        }

        FeedbackCommand(FeedbackCommandKind kind, ChannelRef channel, in PlayArgs args, StopMode mode, float time)
        {
            m_Kind = kind;
            m_Channel = channel;
            m_Args = args;
            m_Mode = mode;
            m_Time = time;
        }

        /// <summary>What the command asks.</summary>
        public FeedbackCommandKind Kind => m_Kind;
        /// <summary>The channel whose receivers take it.</summary>
        public ChannelRef Channel => m_Channel;
        /// <summary>The arguments of a Play.</summary>
        public PlayArgs Args => m_Args;
        /// <summary>The mode of a Stop.</summary>
        public StopMode Mode => m_Mode;
        /// <summary>The time of a Seek.</summary>
        public float Time => m_Time;

        /// <summary>A Play command for <paramref name="channel"/>.</summary>
        public static FeedbackCommand Play(ChannelRef channel, in PlayArgs args = default) => new FeedbackCommand(FeedbackCommandKind.Play, channel, in args, StopMode.Hold, 0f);
        /// <summary>A Stop command for <paramref name="channel"/>.</summary>
        public static FeedbackCommand Stop(ChannelRef channel, StopMode mode = StopMode.Hold) => new FeedbackCommand(FeedbackCommandKind.Stop, channel, default, mode, 0f);
        /// <summary>A Seek command for <paramref name="channel"/>.</summary>
        public static FeedbackCommand Seek(ChannelRef channel, float time) => new FeedbackCommand(FeedbackCommandKind.Seek, channel, default, StopMode.Hold, time);
    }
}
