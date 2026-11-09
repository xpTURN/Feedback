using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.Timeline;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace xpTURN.Feedback.Editor
{
    // what the Timeline window shows of a track's player — its list's model, what the list holds, its validation's errors and
    // warnings — built once a player and again when the player, its linked preset or its settings change (their dirty
    // counts: an edit, an Undo, a link; a script's direct write is not seen). The window asks at each draw: an ask that
    // finds the info built allocates nothing. A clip asset is looked up in the window's timeline (the inspected one — a
    // sub-timeline's when the window entered it) through a map checked at each ask; a clip the window does not show has no
    // length. The overlaps of a player's clips are found once a timeline and again when a Feedback track, its binding, its
    // player or the director changes
    internal sealed class TimelinePlayerInfo
    {
        internal const string NoPlayerText = "Bind a FeedbackPlayer to the track — the clip plays nothing.";
        internal const string NotScrubbedText = "Driven, but the list cannot be scrubbed — it plays as Triggered.";
        internal const string TimeScaleText = "Time Scale items do nothing in a Driven clip.";
        internal const string RestartText = "Overlaps another clip of the same player — the later clip ends the earlier play.";
        internal const string IgnoreText = "Overlaps another clip of the same player — the later clip does not play.";
        internal const string AutoText = "Auto — Driven when the list can be scrubbed, else Triggered.";
        internal const string AutoDrivenText = "Auto — Driven: the timeline's time moves the play.";
        internal const string AutoTriggeredText = "Auto — Triggered: the list cannot be scrubbed, so the play starts with the clip and runs on the player's clock.";
        internal const string DrivenText = "Driven — the timeline's time moves the play.";
        internal const string TriggeredText = "Triggered — the play starts with the clip and runs on the player's clock.";
        internal const string SimulatedText = "Plays as Triggered in the Timeline window's preview (a simulated target).";
        internal const string HitStopText = "A Hit Stop stops the game — the director with it — for its length.";
        internal const float MinRow = 2f;
        internal const int MaxRepeats = 64;
        internal const float DimAlpha = 0.45f;

        internal TimelineModel Model;
        internal int Count;
        internal bool TimeScale;
        internal bool HitStop;
        internal bool Simulated;
        internal string TrackText = "";
        // the build's number — a clip's texts follow it
        internal int Version;

        FeedbackPreset m_Preset;
        int m_PlayerDirty;
        int m_PresetDirty;
        float m_Multiplier;
        float m_StartDelay;

        // a bar's piece on a clip's background
        internal readonly struct StripRect
        {
            internal readonly Rect Rect;
            internal readonly Color Color;

            internal StripRect(Rect rect, Color color)
            {
                Rect = rect;
                Color = color;
            }
        }

        sealed class ClipText
        {
            internal int Version = int.MinValue;
            internal FeedbackClipTiming Timing;
            internal uint Seed;
            internal PlayPolicy Overlap;
            internal string Error = "";
            internal string Tooltip = "";
        }

        readonly struct Span
        {
            internal readonly FeedbackPlayer Player;
            internal readonly TimelineClip Clip;

            internal Span(FeedbackPlayer player, TimelineClip clip)
            {
                Player = player;
                Clip = clip;
            }
        }

        static readonly Dictionary<FeedbackPlayer, TimelinePlayerInfo> s_Players = new Dictionary<FeedbackPlayer, TimelinePlayerInfo>();
        static readonly Dictionary<FeedbackClip, TimelineClip> s_Clips = new Dictionary<FeedbackClip, TimelineClip>();
        static readonly Dictionary<FeedbackClip, ClipText> s_Texts = new Dictionary<FeedbackClip, ClipText>();
        static readonly Dictionary<FeedbackClip, PlayPolicy> s_Overlaps = new Dictionary<FeedbackClip, PlayPolicy>();
        static readonly List<int> s_Signature = new List<int>();
        static readonly List<int> s_Next = new List<int>();
        static readonly List<Span> s_Spans = new List<Span>();
        static readonly List<TimelineClip> s_ClipList = new List<TimelineClip>();
        static readonly List<RuleResult> s_Problems = new List<RuleResult>();
        static TimelineAsset s_OverlapTimeline;
        static PlayableDirector s_OverlapDirector;
        static int s_Versions;

        // observation
        internal static int Builds;
        internal static int OverlapBuilds;

        static TimelinePlayerInfo() => EditorSceneManager.sceneClosed += _ => Clear();

        internal static void Clear()
        {
            s_Players.Clear();
            s_Clips.Clear();
            s_Texts.Clear();
            s_Overlaps.Clear();
            s_Signature.Clear();
            s_OverlapTimeline = null;
            s_OverlapDirector = null;
        }

        internal static TimelinePlayerInfo Of(FeedbackPlayer player)
        {
            if (!s_Players.TryGetValue(player, out TimelinePlayerInfo info))
            {
                info = new TimelinePlayerInfo();
                s_Players.Add(player, info);
            }
            info.Refresh(player);
            return info;
        }

        void Refresh(FeedbackPlayer player)
        {
            FeedbackPreset preset = player.m_Preset;
            int playerDirty = EditorUtility.GetDirtyCount(player);
            int presetDirty = preset != null ? EditorUtility.GetDirtyCount(preset) : 0;
            List<FeedbackItem> items = player.Items;
            if (Model != null && playerDirty == m_PlayerDirty && presetDirty == m_PresetDirty && ReferenceEquals(preset, m_Preset)
                && items.Count == Count && player.m_DurationMultiplier == m_Multiplier && player.m_StartDelay == m_StartDelay)
                return;
            m_Preset = preset;
            m_PlayerDirty = playerDirty;
            m_PresetDirty = presetDirty;
            m_Multiplier = player.m_DurationMultiplier;
            m_StartDelay = player.m_StartDelay;
            Count = items.Count;
            Builds++;
            Version = ++s_Versions;
            Model = TimelineModel.Build(items, m_Multiplier, m_StartDelay);
            TimeScale = false;
            HitStop = false;
            foreach (FeedbackItem item in items)
            {
                if (!(item is EffectItem effect) || !effect.m_Active)
                    continue;
                TimeScale |= effect is TimeScaleItem;
                HitStop |= effect is HitStopItem;
            }
            Simulated = PreviewSession.Simulates(player);
            s_Problems.Clear();
            foreach (RuleResult r in FeedbackValidator.Run(player))
            {
                if (r.Severity == RuleSeverity.Error || r.Severity == RuleSeverity.Warning)
                    s_Problems.Add(r);
            }
            TrackText = s_Problems.Count == 0 ? "" : "The player has " + FeedbackValidator.Summary(s_Problems) + " — see its Inspector.";
            s_Problems.Clear();
        }

        // ---- the clip asset's length

        internal static double Length(FeedbackClip asset)
        {
            TimelineClip clip = ClipOf(asset);
            FeedbackPlayer player = clip != null ? Bound(clip.GetParentTrack()) : null;
            if (player == null)
                return double.PositiveInfinity;
            double total = Of(player).Model.Total;
            return total > 0 && !double.IsInfinity(total) ? total : double.PositiveInfinity;
        }

        // the player bound to a track by the window's director
        internal static FeedbackPlayer Bound(TrackAsset track)
        {
            PlayableDirector director = TimelineEditor.inspectedDirector;
            return director != null && track != null ? director.GetGenericBinding(track) as FeedbackPlayer : null;
        }

        // the clip of an asset in the window's timeline — the map's, when it is still in its track there
        internal static TimelineClip ClipOf(FeedbackClip asset)
        {
            TimelineAsset timeline = TimelineEditor.inspectedAsset;
            if (asset == null || timeline == null)
                return null;
            if (s_Clips.TryGetValue(asset, out TimelineClip clip) && Holds(timeline, clip, asset))
                return clip;
            clip = Find(timeline, asset);
            if (clip != null)
                s_Clips[asset] = clip;
            else
                s_Clips.Remove(asset);
            return clip;
        }

        static bool Holds(TimelineAsset timeline, TimelineClip clip, FeedbackClip asset)
        {
            if (clip == null || !ReferenceEquals(clip.asset, asset))
                return false;
            TrackAsset track = clip.GetParentTrack();
            if (track == null || track.timelineAsset != timeline)
                return false;
            IReadOnlyList<TimelineClip> clips = ClipsOf(track);
            for (int i = 0; i < clips.Count; i++)
            {
                if (ReferenceEquals(clips[i], clip))
                    return true;
            }
            return false;
        }

        static TimelineClip Find(TimelineAsset timeline, FeedbackClip asset)
        {
            for (int t = 0; t < timeline.outputTrackCount; t++)
            {
                if (!(timeline.GetOutputTrack(t) is FeedbackTrack track))
                    continue;
                IReadOnlyList<TimelineClip> clips = ClipsOf(track);
                for (int i = 0; i < clips.Count; i++)
                {
                    if (ReferenceEquals(clips[i].asset, asset))
                        return clips[i];
                }
            }
            return null;
        }

        static IReadOnlyList<TimelineClip> ClipsOf(TrackAsset track)
        {
            if (track.GetClips() is TimelineClip[] clips)
                return clips;
            s_ClipList.Clear();
            s_ClipList.AddRange(track.GetClips());
            return s_ClipList;
        }

        // ---- the clip's texts

        // the reasons a clip does not play as set (a line each) and its explanation — empty reasons when the window shows the
        // timeline without its director
        internal static void Texts(TimelineClip clip, out string error, out string tooltip)
        {
            error = "";
            tooltip = "";
            if (clip == null || !(clip.asset is FeedbackClip asset))
                return;
            TrackAsset track = clip.GetParentTrack();
            PlayableDirector director = TimelineEditor.inspectedDirector;
            TimelineAsset timeline = TimelineEditor.inspectedAsset;
            bool known = director != null && track != null && timeline != null && track.timelineAsset == timeline;
            FeedbackPlayer player = known ? director.GetGenericBinding(track) as FeedbackPlayer : null;
            TimelinePlayerInfo info = player != null ? Of(player) : null;
            int version = !known ? -1 : info != null ? info.Version : 0;
            PlayPolicy overlap = info != null ? OverlapOf(timeline, director, asset) : PlayPolicy.Overlap;
            if (!s_Texts.TryGetValue(asset, out ClipText text))
            {
                text = new ClipText();
                s_Texts.Add(asset, text);
            }
            if (text.Version != version || text.Timing != asset.m_Timing || text.Seed != asset.m_Seed || text.Overlap != overlap)
            {
                text.Version = version;
                text.Timing = asset.m_Timing;
                text.Seed = asset.m_Seed;
                text.Overlap = overlap;
                text.Error = known ? ErrorOf(asset, info, overlap) : "";
                text.Tooltip = TooltipOf(asset, info);
            }
            error = text.Error;
            tooltip = text.Tooltip;
        }

        static string ErrorOf(FeedbackClip asset, TimelinePlayerInfo info, PlayPolicy overlap)
        {
            if (info == null)
                return NoPlayerText;
            var lines = new List<string>(3);
            bool seekable = info.Model.Seekable;
            if (asset.m_Timing == FeedbackClipTiming.Driven && !seekable)
                lines.Add(NotScrubbedText);
            if (asset.m_Timing != FeedbackClipTiming.Triggered && seekable && info.TimeScale)
                lines.Add(TimeScaleText);
            if (overlap == PlayPolicy.Restart)
                lines.Add(RestartText);
            else if (overlap == PlayPolicy.Ignore)
                lines.Add(IgnoreText);
            return string.Join("\n", lines);
        }

        static string TooltipOf(FeedbackClip asset, TimelinePlayerInfo info)
        {
            var lines = new List<string>(5);
            bool seekable = info != null && info.Model.Seekable;
            switch (asset.m_Timing)
            {
                case FeedbackClipTiming.Driven:
                    lines.Add(DrivenText);
                    break;
                case FeedbackClipTiming.Triggered:
                    lines.Add(TriggeredText);
                    break;
                default:
                    lines.Add(info == null ? AutoText : seekable ? AutoDrivenText : AutoTriggeredText);
                    break;
            }
            if (info != null)
            {
                double total = info.Model.Total;
                lines.Add("List " + (double.IsInfinity(total) ? "∞" : total.ToString("0.###") + " s") + " · " + info.Count + (info.Count == 1 ? " item" : " items"));
            }
            lines.Add("Seed " + asset.m_Seed);
            if (info != null && info.Simulated && asset.m_Timing != FeedbackClipTiming.Triggered && seekable)
                lines.Add(SimulatedText);
            if (info != null && info.HitStop)
                lines.Add(HitStopText);
            return string.Join("\n", lines);
        }

        // ---- the overlaps

        // the policy that cuts a clip — another clip of the same player overlaps it; Overlap when none does
        static PlayPolicy OverlapOf(TimelineAsset timeline, PlayableDirector director, FeedbackClip asset)
        {
            Sign(timeline, director, s_Next);
            if (!ReferenceEquals(timeline, s_OverlapTimeline) || !ReferenceEquals(director, s_OverlapDirector) || !Same(s_Signature, s_Next))
            {
                s_OverlapTimeline = timeline;
                s_OverlapDirector = director;
                s_Signature.Clear();
                s_Signature.AddRange(s_Next);
                FindOverlaps(timeline, director);
            }
            return s_Overlaps.TryGetValue(asset, out PlayPolicy policy) ? policy : PlayPolicy.Overlap;
        }

        static void Sign(TimelineAsset timeline, PlayableDirector director, List<int> into)
        {
            into.Clear();
            into.Add(EditorUtility.GetDirtyCount(director));
            for (int t = 0; t < timeline.outputTrackCount; t++)
            {
                if (!(timeline.GetOutputTrack(t) is FeedbackTrack track))
                    continue;
                into.Add(track.GetHashCode());
                into.Add(EditorUtility.GetDirtyCount(track));
                var player = director.GetGenericBinding(track) as FeedbackPlayer;
                into.Add(player != null ? player.GetHashCode() : 0);
                into.Add(player != null ? EditorUtility.GetDirtyCount(player) : 0);
            }
        }

        static bool Same(List<int> a, List<int> b)
        {
            if (a.Count != b.Count)
                return false;
            for (int i = 0; i < a.Count; i++)
            {
                if (a[i] != b[i])
                    return false;
            }
            return true;
        }

        static void FindOverlaps(TimelineAsset timeline, PlayableDirector director)
        {
            OverlapBuilds++;
            s_Overlaps.Clear();
            s_Spans.Clear();
            for (int t = 0; t < timeline.outputTrackCount; t++)
            {
                if (!(timeline.GetOutputTrack(t) is FeedbackTrack track))
                    continue;
                var player = director.GetGenericBinding(track) as FeedbackPlayer;
                if (player == null || player.m_Policy == PlayPolicy.Overlap)
                    continue;
                IReadOnlyList<TimelineClip> clips = ClipsOf(track);
                for (int i = 0; i < clips.Count; i++)
                {
                    if (clips[i].asset is FeedbackClip)
                        s_Spans.Add(new Span(player, clips[i]));
                }
            }
            for (int i = 0; i < s_Spans.Count; i++)
            {
                for (int j = i + 1; j < s_Spans.Count; j++)
                {
                    Span a = s_Spans[i];
                    Span b = s_Spans[j];
                    if (!ReferenceEquals(a.Player, b.Player) || a.Clip.start >= b.Clip.end || b.Clip.start >= a.Clip.end)
                        continue;
                    s_Overlaps[(FeedbackClip)a.Clip.asset] = a.Player.m_Policy;
                    s_Overlaps[(FeedbackClip)b.Clip.asset] = a.Player.m_Policy;
                }
            }
            s_Spans.Clear();
        }

        // ---- the clip's background

        // the bars of the clip's player on its background region
        internal static bool Strip(TimelineClip clip, Rect area, double start, double end, List<StripRect> into)
        {
            into.Clear();
            if (clip == null || !(clip.asset is FeedbackClip asset))
                return false;
            TimelineAsset timeline = TimelineEditor.inspectedAsset;
            TrackAsset track = clip.GetParentTrack();
            if (timeline == null || track == null || track.timelineAsset != timeline)
                return false;
            FeedbackPlayer player = Bound(track);
            if (player == null)
                return false;
            TimelinePlayerInfo info = Of(player);
            bool triggered = asset.m_Timing == FeedbackClipTiming.Triggered || !info.Model.Seekable || (info.Simulated && !Application.isPlaying);
            Place(info.Model, area, start, end, asset.m_Loop, triggered, into);
            return into.Count > 0;
        }

        // the list's bars on a clip's area — local time t at x between the area's start and end times; a looping clip repeats
        // the list at each length, an endless bar runs to the area's end; a clip played as Triggered is dimmed (its times are
        // nominal — the player's clock runs it). The rows share the area's height, 2 px at least — the rows past it are left
        // out. The edges are whole pixels: bars that meet leave no gap. As the inspector's strip: the delay a thin line, the first run the category's colour (dimmed when the item is
        // off, grey for a flow item), the repeats faint, a loop block behind its members
        internal static void Place(TimelineModel model, Rect area, double start, double end, bool loop, bool dim, List<StripRect> into)
        {
            into.Clear();
            int count = model.Bars.Count;
            if (count == 0 || end <= start || area.width <= 0f || area.height <= 0f)
                return;
            float row = area.height / count;
            int rows = count;
            if (row < MinRow)
            {
                row = MinRow;
                rows = Math.Max(1, (int)(area.height / MinRow));
            }
            double total = model.Total;
            bool repeats = loop && total > 0 && !double.IsInfinity(total);
            int first = repeats ? Math.Max(0, (int)Math.Floor(start / total)) : 0;
            int last = repeats ? (int)Math.Floor(end / total) : 0;
            if (last - first >= MaxRepeats)
                last = first + MaxRepeats - 1;
            float alpha = dim ? DimAlpha : 1f;
            double scale = area.width / (end - start);
            for (int k = first; k <= last; k++)
            {
                double offset = repeats ? k * total : 0;
                foreach (TimelineModel.Band band in model.Bands)
                    Box(into, area, X(area, start, scale, band.Start + offset), area.y, X(area, start, scale, band.End + offset), area.yMax, new Color(1f, 1f, 1f, 0.06f * alpha));
                for (int i = 0; i < rows; i++)
                {
                    TimelineModel.Bar b = model.Bars[i];
                    Color c = b.Flow ? new Color(0.6f, 0.6f, 0.6f) : InspectorStyles.CategoryColor(b.Category);
                    if (b.Off)
                        c.a = 0.35f;
                    c.a *= alpha;
                    float y = area.y + i * row;
                    float gap = row > 3f ? 1f : 0f;
                    float top = b.Background ? y + row * 0.5f : y;
                    float bottom = y + row - gap;
                    if (b.DelayStart < b.Start)
                        Box(into, area, X(area, start, scale, b.DelayStart + offset), y + (row - gap) * 0.5f - 0.5f, X(area, start, scale, b.Start + offset), y + (row - gap) * 0.5f + 0.5f, new Color(c.r, c.g, c.b, c.a * 0.5f));
                    float x0 = X(area, start, scale, b.Start + offset);
                    Box(into, area, x0, top, Math.Max(x0 + 1.5f, X(area, start, scale, b.RunEnd + offset)), bottom, c);
                    if (b.End > b.RunEnd)
                        Box(into, area, X(area, start, scale, b.RunEnd + offset), top, X(area, start, scale, b.End + offset), bottom, new Color(c.r, c.g, c.b, c.a * 0.4f));
                }
            }
        }

        static float X(Rect area, double start, double scale, double t)
            => double.IsInfinity(t) ? area.xMax : area.x + (float)((t - start) * scale);

        static void Box(List<StripRect> into, Rect area, float x0, float y0, float x1, float y1, Color c)
        {
            x0 = Mathf.Round(Math.Max(x0, area.xMin));
            x1 = Mathf.Round(Math.Min(x1, area.xMax));
            if (x1 <= x0 || y1 <= y0)
                return;
            into.Add(new StripRect(new Rect(x0, y0, x1 - x0, y1 - y0), c));
        }
    }
}
