using UnityEditor;
using UnityEditor.Timeline;
using UnityEngine;
using UnityEngine.Timeline;

namespace xpTURN.Feedback.Editor
{
    // a Feedback track in the Timeline window — Timeline's binding texts first, else its player's validation errors and
    // warnings; the player's icon
    [CustomTimelineEditor(typeof(FeedbackTrack))]
    internal sealed class FeedbackTrackEditor : TrackEditor
    {
        internal const string IconPath = "Packages/com.xpturn.feedback/Editor/Icons/FeedbackPlayer.png";

        static Texture2D s_Icon;
        static bool s_Loaded;

        internal static Texture2D Icon
        {
            get
            {
                if (!s_Loaded)
                {
                    s_Loaded = true;
                    s_Icon = AssetDatabase.LoadAssetAtPath<Texture2D>(IconPath);
                }
                return s_Icon;
            }
        }

        public override TrackDrawOptions GetTrackOptions(TrackAsset track, Object binding)
        {
            TrackDrawOptions options = base.GetTrackOptions(track, binding);
            options.icon = Icon;
            if (string.IsNullOrEmpty(options.errorText) && binding is FeedbackPlayer player && player != null)
                options.errorText = TimelinePlayerInfo.Of(player).TrackText;
            return options;
        }
    }
}
