using System;
using System.ComponentModel;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace xpTURN.Feedback
{
    // a Timeline track bound to a FeedbackPlayer — each clip one play of it. A clip made on the track draws its seed
    /// <summary>A Timeline track bound to a FeedbackPlayer; each of its clips is one play of that player.</summary>
    [TrackColor(0.95f, 0.55f, 0.2f)]
    [TrackBindingType(typeof(FeedbackPlayer))]
    [TrackClipType(typeof(FeedbackClip))]
    [DisplayName("xpTURN/Feedback Track")]
    public sealed class FeedbackTrack : TrackAsset
    {
        static uint s_Seeds;
        static bool s_Seeded;

        // the Timeline window's preview starts (or its graph is built again): the bound player joins the preview's session
        /// <summary>Timeline calls it when its window's preview starts; the bound player joins that preview.</summary>
        public override void GatherProperties(PlayableDirector director, IPropertyCollector driver)
        {
            if (TrackPreview.Hook != null && director != null && director.GetGenericBinding(this) is FeedbackPlayer player && player != null)
                TrackPreview.Hook.Gather(director, player);
            base.GatherProperties(director, driver);
        }

        protected override void OnCreateClip(TimelineClip clip)
        {
            if (clip.asset is FeedbackClip feedback)
                feedback.m_Seed = NextSeed();
            base.OnCreateClip(clip);
        }

        // a seed a clip, never 0 — seeded once by the clock, the next value each; the editor's duplicate draws one too
        internal static uint NextSeed()
        {
            if (!s_Seeded)
            {
                s_Seeds = (uint)Environment.TickCount;
                s_Seeded = true;
            }
            do
                s_Seeds = s_Seeds * 1664525u + 1013904223u;
            while (s_Seeds == 0u);
            return s_Seeds;
        }
    }
}
