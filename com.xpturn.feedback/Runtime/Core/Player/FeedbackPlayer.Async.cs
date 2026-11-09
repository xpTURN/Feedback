using System.Threading;
using UnityEngine;

namespace xpTURN.Feedback
{
    // await a play's end — the end's reason as a value, no exception (a refused play is
    // Refused at once). Not an async method: a pooled completion source becomes the play's listener. Await it once; a
    // discarded call leaves Unity's Awaitable out of its pool. The main thread's
    public sealed partial class FeedbackPlayer
    {
        /// <summary>Plays the list and returns an Awaitable that ends with the play's end reason; a refused play ends at once with <see cref="PlaybackEnd.Refused"/>.</summary>
        public Awaitable<PlaybackEnd> PlayAsync() => PlaybackAwaiter.Play(this, default, default);

        /// <summary>Plays the list and returns an Awaitable that ends with the play's end reason; canceling <paramref name="token"/> stops the play.</summary>
        public Awaitable<PlaybackEnd> PlayAsync(CancellationToken token) => PlaybackAwaiter.Play(this, default, token);

        /// <summary>Plays the list with <paramref name="args"/> and returns an Awaitable that ends with the play's end reason; canceling <paramref name="token"/> stops the play.</summary>
        public Awaitable<PlaybackEnd> PlayAsync(in PlayArgs args, CancellationToken token = default) => PlaybackAwaiter.Play(this, in args, token);
    }
}
