using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace xpTURN.Feedback
{
    // PlayAsync's rules on UniTask — the end's reason as a value, a refused play Refused at once. A pooled IUniTaskSource
    // becomes the play's listener and goes back to UniTask's TaskPool when its result is taken — a discarded call keeps its
    // source. The main thread's
    /// <summary>Awaits a FeedbackPlayer's play as a UniTask.</summary>
    public static class FeedbackPlayerUniTaskExtensions
    {
        /// <summary>Plays the list and returns a UniTask that ends with the play's end reason; a refused play ends at once with <see cref="PlaybackEnd.Refused"/>.</summary>
        public static UniTask<PlaybackEnd> PlayAsUniTask(this FeedbackPlayer player, CancellationToken token = default)
            => PlaybackUniTaskSource.Play(player, default, token);

        /// <summary>Plays the list with <paramref name="args"/> and returns a UniTask that ends with the play's end reason; a refused play ends at once with <see cref="PlaybackEnd.Refused"/>.</summary>
        public static UniTask<PlaybackEnd> PlayAsUniTask(this FeedbackPlayer player, in PlayArgs args, CancellationToken token = default)
            => PlaybackUniTaskSource.Play(player, in args, token);
    }

    internal sealed class PlaybackUniTaskSource : IUniTaskSource<PlaybackEnd>, ITaskPoolNode<PlaybackUniTaskSource>, IPlaybackListener
    {
        static TaskPool<PlaybackUniTaskSource> s_Pool;
        internal static int Created;
        PlaybackUniTaskSource m_Next;
        UniTaskCompletionSourceCore<PlaybackEnd> m_Core;
        IPlaybackListener m_Forward;
        int m_Generation;

        public ref PlaybackUniTaskSource NextNode => ref m_Next;

        static PlaybackUniTaskSource() => TaskPool.RegisterSizeGetter(typeof(PlaybackUniTaskSource), () => s_Pool.Size);

        internal static UniTask<PlaybackEnd> Play(FeedbackPlayer player, in PlayArgs args, CancellationToken token)
        {
            if (!s_Pool.TryPop(out PlaybackUniTaskSource source))
            {
                Created++;
                source = new PlaybackUniTaskSource();
            }
            int generation = source.m_Generation;
            short version = source.m_Core.Version;
            source.m_Forward = args.Listener;
            PlaybackHandle handle = player.Play(args.WithListener(source), token);
            if (handle == default && source.m_Generation == generation)
                source.Finish(default, PlaybackEnd.Refused, false);
            return new UniTask<PlaybackEnd>(source, version);
        }

        public void OnPlaybackEnded(PlaybackHandle handle, PlaybackEnd end) => Finish(handle, end, true);

        void Finish(PlaybackHandle handle, PlaybackEnd end, bool forward)
        {
            m_Generation++;
            IPlaybackListener caller = m_Forward;
            m_Forward = null;
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
            m_Core.TrySetResult(end);
        }

        public PlaybackEnd GetResult(short token)
        {
            try
            {
                return m_Core.GetResult(token);
            }
            finally
            {
                m_Core.Reset();
                s_Pool.TryPush(this);
            }
        }

        void IUniTaskSource.GetResult(short token) => GetResult(token);

        public UniTaskStatus GetStatus(short token) => m_Core.GetStatus(token);

        public UniTaskStatus UnsafeGetStatus() => m_Core.UnsafeGetStatus();

        public void OnCompleted(Action<object> continuation, object state, short token) => m_Core.OnCompleted(continuation, state, token);
    }
}
