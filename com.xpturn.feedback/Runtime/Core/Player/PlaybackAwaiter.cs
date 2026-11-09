using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

namespace xpTURN.Feedback
{
    // a pooled completion source as a play's listener. The Awaitable is taken before Play — a play that ends inside Play
    // completes and resets the source there. A refusal is a default handle with the generation unchanged — a t = 0 fault
    // ends through the listener first and moves it. The end gives the caller's own listener its call first (its exception
    // logged), then the result — the awaiting code runs inside SetResult — and the source goes back to the pool. The pool is
    // static, the main thread's, and never shrinks
    internal sealed class PlaybackAwaiter : IPlaybackListener
    {
        static readonly Stack<PlaybackAwaiter> s_Pool = new Stack<PlaybackAwaiter>(4);
        internal static int Created;
        internal static int Pooled => s_Pool.Count;

        readonly AwaitableCompletionSource<PlaybackEnd> m_Source = new AwaitableCompletionSource<PlaybackEnd>();
        IPlaybackListener m_Forward;
        int m_Generation;

        // the first use makes two: the awaiting code runs inside SetResult, before its source is back
        static PlaybackAwaiter Take()
        {
            if (s_Pool.Count == 0)
            {
                for (int i = Created == 0 ? 2 : 1; i > 0; i--)
                {
                    Created++;
                    s_Pool.Push(new PlaybackAwaiter());
                }
            }
            return s_Pool.Pop();
        }

        internal static Awaitable<PlaybackEnd> Play(FeedbackPlayer player, in PlayArgs args, CancellationToken token)
        {
            PlaybackAwaiter awaiter = Take();
            int generation = awaiter.m_Generation;
            awaiter.m_Forward = args.Listener;
            Awaitable<PlaybackEnd> awaitable = awaiter.m_Source.Awaitable;
            PlaybackHandle handle = player.Play(args.WithListener(awaiter), token);
            if (handle == default && awaiter.m_Generation == generation)
                awaiter.Finish(default, PlaybackEnd.Refused, false);
            return awaitable;
        }

        public void OnPlaybackEnded(PlaybackHandle handle, PlaybackEnd end) => Finish(handle, end, true);

        void Finish(PlaybackHandle handle, PlaybackEnd end, bool forward)
        {
            m_Generation++;
            IPlaybackListener caller = m_Forward;
            m_Forward = null;
            try
            {
                if (forward && caller != null)
                {
                    try
                    {
                        caller.OnPlaybackEnded(handle, end);
                    }
                    catch (Exception e)
                    {
                        Debug.LogException(e);
                    }
                }
                m_Source.SetResult(end);
            }
            finally
            {
                m_Source.Reset();
                s_Pool.Push(this);
            }
        }
    }
}
