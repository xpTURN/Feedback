using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEditor;
using UnityEditor.Timeline;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace xpTURN.Feedback.Editor
{
    // a Feedback clip's Graph in its Inspector: the slot the clip's list writes, composed as the clip plays it, on the clip's
    // time — 0 to its length in the timeline's seconds. Driven: the clip's arguments (its seed, forward, the driven mark) and
    // at each tick a seek to the clip's local time (its clip-in, speed and loop — the clip behaviour's target) with a zero
    // tick after it; Triggered (so set, a list that cannot be scrubbed, a simulated target in the preview): a play from the
    // clip's start on the player's clock at the sample seed, again after its end when the clip loops. The list: in Play mode
    // the one the player picks for the clip's arguments (a director's options too), else the clip's Preset's. The sum only, in
    // a Graph Preview box whose switch is off at first (per clip asset, the session's): off, the box is its title row and
    // builds nothing; on, built again after an edit of the clip, its TimelineClip (start, length, clip-in, speed,
    // extrapolation), its track's binding, the player or the list, a preview's start or end, the settings and a new width. A
    // clip that draws nothing shows its note only. The head is the window's time inside the clip; a press moves it there while
    // the window's preview holds the player
    internal sealed class ClipGraphField : VisualElement
    {
        internal const string TitleTooltip = "What the clip's list writes to one property, composed as the clip plays it, on the clip's time";
        internal const string SwitchTooltip = "On: draws the graph. Off: nothing is built";
        internal const string SwitchPrefix = "xpTURN.Feedback.Inspector.ClipGraph.";
        internal const string NoPlayerText = "No player — the Timeline window shows this clip on a track bound to a Feedback Player";
        internal const string UnlistedText = "The clip's Preset is not one of the player's lists — the clip plays nothing";
        internal const string NoListText = "The clip's arguments pick none of the player's lists — the clip plays nothing";
        internal const string NotPreparedText = "The player is not prepared yet — drawn with the clip's Preset";
        internal const string TriggeredText = "Triggered — each play draws its own seed; drawn at the sample seed";
        internal const string NotScrubbedText = "The list cannot be scrubbed — drawn as Triggered, as it plays";
        internal const string SimulatedText = "A simulated target — drawn as Triggered, as the Timeline window's preview plays it";
        internal const string BackwardText = "The player plays backward — the graph shows the list forward";
        internal const string ToggleText = "The player turns its direction at each play — the graph shows the list forward";
        internal const string ExtrapolationText = "Before and after the clip (its extrapolation) not drawn";
        internal const string ScrubText = "Turn on the Timeline window's preview to scrub";
        static readonly string[] s_Sized = { "X", "Y", "Z", "Size" };

        internal readonly Toggle Switch;
        // what the switch shows — the header, the graph and the notes
        internal readonly VisualElement Body;
        internal readonly VisualElement Header;
        internal readonly Label Notes;
        internal readonly MotionGraphView View;
        internal readonly List<Toggle> Components = new List<Toggle>();
        internal PopupField<string> GroupChoice;
        internal SlotGraphModel Model;
        internal int Builds;
        // what the last build drew: the clip, its player, the list's owner, played as Triggered
        internal TimelineClip Clip;
        internal FeedbackPlayer Player;
        internal Object Owner;
        internal bool Triggered;

        readonly FeedbackClip m_Asset;
        readonly string m_SwitchKey;
        Signature m_Signature;
        string m_Settings;
        string m_Refusal;
        // the last build's words before the line's and its list's info
        List<string> m_Before = new List<string>();
        TimelinePlayerInfo m_Info;
        bool m_Dirty = true;
        bool m_Scheduled;
        int m_Columns;
        int m_Polls;

        // what a build reads beyond the clip asset's fields — another value builds again
        struct Signature
        {
            internal TimelineClip Clip;
            internal TimelineAsset Timeline;
            internal TrackAsset Track;
            internal FeedbackPlayer Player;
            internal Object Owner;
            internal double Start;
            internal double Duration;
            internal double ClipIn;
            internal double TimeScale;
            internal TimelineClip.ClipExtrapolation Pre;
            internal TimelineClip.ClipExtrapolation Post;
            internal int PlayerDirty;
            internal int OwnerDirty;
            internal bool Playing;
            internal PropertyLayerRegistry Live;

            internal bool Same(in Signature o)
                => ReferenceEquals(Clip, o.Clip) && ReferenceEquals(Timeline, o.Timeline) && ReferenceEquals(Track, o.Track) && ReferenceEquals(Player, o.Player)
                    && ReferenceEquals(Owner, o.Owner) && Start == o.Start && Duration == o.Duration && ClipIn == o.ClipIn && TimeScale == o.TimeScale
                    && Pre == o.Pre && Post == o.Post && PlayerDirty == o.PlayerDirty && OwnerDirty == o.OwnerDirty && Playing == o.Playing && ReferenceEquals(Live, o.Live);
        }

        internal ClipGraphField(FeedbackClip asset, SerializedObject so)
        {
            AddToClassList(InspectorStyles.Graph);
            AddToClassList(InspectorStyles.GraphBox);
            InspectorStyles.Attach(this);
            m_Asset = asset;
            m_SwitchKey = SwitchPrefix + asset.GetEntityId();
            Switch = SlotGraphField.TitleRow(this, SessionState.GetBool(m_SwitchKey, false), TitleTooltip, SwitchTooltip);
            Switch.RegisterValueChangedCallback(e =>
            {
                if (e.target != Switch)
                    return;
                SessionState.SetBool(m_SwitchKey, e.newValue);
                Body.style.display = e.newValue ? DisplayStyle.Flex : DisplayStyle.None;
                if (e.newValue)
                    Changed();
            });
            Body = new VisualElement { style = { display = Switch.value ? DisplayStyle.Flex : DisplayStyle.None } };
            Add(Body);
            Header = new VisualElement();
            Header.AddToClassList(InspectorStyles.GraphHeader);
            Body.Add(Header);
            View = new MotionGraphView { Scrubbed = Scrub };
            View.RegisterCallback<GeometryChangedEvent>(_ =>
            {
                if (Switch.value && ColumnsNow() != m_Columns)
                    Changed();
            });
            Body.Add(View);
            Notes = new Label();
            Notes.AddToClassList(InspectorStyles.GraphNotes);
            Body.Add(Notes);
            this.TrackSerializedObjectValue(so, _ => Changed());
            schedule.Execute(Poll).Every(100);
            if (Switch.value)
                Changed();
        }

        string GroupKey => m_SwitchKey + ".Slot";

        int ColumnsNow()
        {
            float w = View.contentRect.width;
            return float.IsNaN(w) || w < 16f ? 300 : Math.Min(ItemGraphModel.MaxColumns, (int)w);
        }

        // an edit, a TimelineClip's change, a session, settings or a width: built again at the next frame, once — only while on
        internal void Changed()
        {
            m_Dirty = true;
            if (!Switch.value || m_Scheduled)
                return;
            m_Scheduled = true;
            schedule.Execute(() =>
            {
                m_Scheduled = false;
                if (m_Dirty && Switch.value)
                    Build();
            });
        }

        // the clip, its player and its list's owner as the window shows them now
        Signature Read()
        {
            var s = new Signature { Timeline = TimelineEditor.inspectedAsset, Playing = Application.isPlaying };
            s.Clip = TimelinePlayerInfo.ClipOf(m_Asset);
            if (s.Clip == null)
                return s;
            s.Start = s.Clip.start;
            s.Duration = s.Clip.duration;
            s.ClipIn = s.Clip.clipIn;
            s.TimeScale = s.Clip.timeScale;
            s.Pre = s.Clip.preExtrapolationMode;
            s.Post = s.Clip.postExtrapolationMode;
            s.Track = s.Clip.GetParentTrack();
            s.Player = TimelinePlayerInfo.Bound(s.Track);
            if (s.Player == null)
                return s;
            s.PlayerDirty = EditorUtility.GetDirtyCount(s.Player);
            s.Owner = m_Asset.m_Preset != null ? m_Asset.m_Preset : PresetLinks.Owner(s.Player);
            s.OwnerDirty = s.Owner != null && s.Owner != s.Player ? EditorUtility.GetDirtyCount(s.Owner) : 0;
            s.Live = SlotGraphModel.LiveLayers(s.Player, EditorApplication.isPlayingOrWillChangePlaymode);
            return s;
        }

        void Poll()
        {
            if (!Switch.value || panel == null)
                return;
            Signature now = Read();
            if (!now.Same(in m_Signature))
                Changed();
            if (Model != null && ++m_Polls % 3 == 0 && SlotGraphField.SettingsSignature(Application.isPlaying) != m_Settings)
                Changed();
            int before = View.Heads.Count;
            View.Heads.Clear();
            PlayableDirector director = TimelineEditor.inspectedDirector;
            if (Clip != null && director != null && director.time >= Clip.start && director.time < Clip.end)
                View.Heads.Add(new TimelineModel.Head(director.time - Clip.start, false));
            if (before > 0 || View.Heads.Count > 0)
                View.MarkDirtyRepaint();
        }

        // the list the clip plays: in Play mode the player's pick for the clip's arguments, else the clip's Preset's (the
        // player's own list for none or its linked preset)
        bool ListOf(FeedbackPlayer player, in PlayArgs args, out List<FeedbackItem> items, out Object owner, out bool unprepared, out string refusal)
        {
            items = null;
            owner = null;
            unprepared = false;
            refusal = null;
            if (Application.isPlaying)
            {
                if (player.Runtime != null)
                {
                    PlayerRuntime runtime = player.Select(in args);
                    if (runtime == null)
                    {
                        refusal = NoListText;
                        return false;
                    }
                    items = runtime.Items;
                    owner = runtime.ItemsOwner;
                    return true;
                }
                unprepared = true;
            }
            FeedbackPreset preset = m_Asset.m_Preset;
            if (!TimelinePlayerInfo.Lists(player, preset))
            {
                refusal = UnlistedText;
                return false;
            }
            if (preset == null || ReferenceEquals(preset, player.m_Preset))
            {
                items = player.Items;
                owner = PresetLinks.Owner(player);
            }
            else
            {
                items = preset.m_Items;
                owner = preset;
            }
            return true;
        }

        internal void Build()
        {
            m_Dirty = false;
            m_Signature = Read();
            m_Settings = SlotGraphField.SettingsSignature(Application.isPlaying);
            m_Columns = ColumnsNow();
            Model = null;
            m_Info = null;
            m_Before = new List<string>();
            Clip = m_Signature.Clip;
            Player = m_Signature.Player;
            Owner = null;
            Triggered = false;
            if (Clip == null || Player == null)
            {
                Show(null, NoPlayerText);
                return;
            }
            PlayableDirector director = TimelineEditor.inspectedDirector;
            PlayArgs args = FeedbackClipBehaviour.Args(m_Asset, Player, director != null ? director.gameObject : null);
            if (!ListOf(Player, in args, out List<FeedbackItem> items, out Object owner, out bool unprepared, out string refusal))
            {
                Show(null, refusal);
                return;
            }
            Owner = owner;
            TimelinePlayerInfo info = TimelinePlayerInfo.Of(Player, owner as FeedbackPreset);
            m_Info = info;
            Triggered = TimelinePlayerInfo.PlaysTriggered(m_Asset, info);
            bool driven = !Triggered;
            args = driven ? FeedbackClipBehaviour.DrivenArgs(in args, m_Asset) : args.WithSeed(GraphInputs.SampleSeed).WithDirection(PlayerDirection.Forward);
            var inputs = new GraphInputs(args.Intensity, args.Seed, driven, Application.isPlaying, false);
            TimelineClip clip = Clip;
            bool loop = m_Asset.m_Loop;
            SlotGraphModel.ClipSeek seek = driven ? (x, low, high) => FeedbackClipBehaviour.Target(clip.ToLocalTimeUnbound(clip.start + x), loop, low, high) : (SlotGraphModel.ClipSeek)null;
            string preferred = SessionState.GetString(GroupKey, null);
            Builds++;
            Model = SlotGraphModel.BuildClip(Player, items, owner, in inputs, in args, clip.duration, seek, loop, m_Columns, string.IsNullOrEmpty(preferred) ? null : preferred,
                m_Signature.Live);
            if (unprepared)
                m_Before.Add(NotPreparedText);
            Show(Model, null);
        }

        void Show(SlotGraphModel model, string refusal)
        {
            View.Model = model?.Sum;
            View.Names = model != null && model.Capped ? s_Sized : MotionGraphView.ComponentNames;
            View.Cap = model != null && model.Capped ? model.Cap : float.NaN;
            if (model != null)
                GroupChoice = SlotGraphField.BuildHeader(Header, model, View, Components, GroupKey, Changed);
            else
            {
                Header.Clear();
                Components.Clear();
                GroupChoice = null;
            }
            Notes.text = model != null ? NotesText() : refusal;
            SlotGraphField.ShowDrawn(Header, View, model);
            View.Refresh();
        }

        static string Number(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);

        // what the line is and where the clip's play differs from it
        internal string NotesText()
        {
            TimelinePlayerInfo info = m_Info;
            bool driven = !Triggered;
            var parts = new List<string>(m_Before);
            if (Model.Groups.Count == 0)
                parts.Add("No item of this list writes a value the graph draws");
            else
            {
                parts.Add(SlotGraphField.FormText(Model));
                if (driven)
                    parts.Add("Driven · seed " + m_Asset.m_Seed + " · intensity " + Number(Model.Inputs.Intensity));
                else
                {
                    if (m_Asset.m_Timing != FeedbackClipTiming.Triggered)
                        parts.Add(info.Model.Seekable ? SimulatedText : NotScrubbedText);
                    parts.Add(TriggeredText + " · intensity " + Number(Model.Inputs.Intensity));
                    if (Player.m_Direction == PlayerDirection.Backward)
                        parts.Add(BackwardText);
                    else if (Player.m_Direction == PlayerDirection.Toggle)
                        parts.Add(ToggleText);
                }
                if (m_Asset.m_Loop)
                {
                    double total = info.Model.Total;
                    parts.Add(driven && total > 0 && !double.IsInfinity(total) ? "Loops every " + Number(total / Clip.timeScale) + " s" : driven ? "Loops" : "Plays again at each end");
                }
                parts.Add(m_Asset.m_End == FeedbackClipEnd.Revert ? "Reverts at the clip's end" : m_Asset.m_End == FeedbackClipEnd.Hold ? "Holds at the clip's end" : "Completes at the clip's end");
                if (driven && (Clip.preExtrapolationMode != TimelineClip.ClipExtrapolation.None || Clip.postExtrapolationMode != TimelineClip.ClipExtrapolation.None))
                    parts.Add(ExtrapolationText);
                PlayPolicy overlap = TimelineEditor.inspectedAsset != null && TimelineEditor.inspectedDirector != null
                    ? TimelinePlayerInfo.OverlapOf(TimelineEditor.inspectedAsset, TimelineEditor.inspectedDirector, m_Asset)
                    : PlayPolicy.Overlap;
                if (overlap == PlayPolicy.Restart)
                    parts.Add(TimelinePlayerInfo.RestartText);
                else if (overlap == PlayPolicy.Ignore)
                    parts.Add(TimelinePlayerInfo.IgnoreText);
                parts.Add(Model.Seeded ? "the preview's start values" : "the scene's current values");
                if (Model.Capped)
                    parts.Add(Model.Inputs.Live ? "Camera shakes capped at " + Number(Model.Cap) : "The game caps camera shakes at " + Number(Model.Cap) + " — the preview does not");
                if (Model.RandomLengths)
                    parts.Add("Random lengths at this seed");
            }
            if (Model.NotDrawn.Count > 0)
                parts.Add("Not drawn: " + string.Join(", ", Model.NotDrawn) + " (a channel's receivers are found when it plays · a target not found)");
            if (Model.Refused != null)
                parts.Add("Nothing drawn: " + Model.Refused);
            if (m_Refusal != null)
                parts.Add(m_Refusal);
            return string.Join(" · ", parts);
        }

        // a press on the graph: the window's time to that place of the clip — while the window's preview holds the player and
        // its lock is alive (the window keeps a time its director was evaluated at)
        internal void Scrub(double seconds)
        {
            PreviewSession session = PreviewSession.Current;
            PlayableDirector master = TimelineEditor.masterDirector;
            if (Clip == null || Player == null || master == null || Application.isPlaying || session == null || !session.HoldsTrackPlayer(Player) || !session.TrackLockAlive)
            {
                m_Refusal = ScrubText;
            }
            else
            {
                m_Refusal = null;
                master.time = TimelineEditor.GetMasterTimeFromInspectedTime(Clip.start + seconds);
                master.Evaluate();
            }
            if (Model != null)
                Notes.text = NotesText();
        }
    }
}
