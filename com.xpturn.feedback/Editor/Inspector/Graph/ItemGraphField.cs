using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace xpTURN.Feedback.Editor
{
    // the item body's Graph: what the item writes over its time on the player's axis. Folded at first — the state is the
    // session's, per item — and only an open one builds: again after an edit of its list or its host, a change of the
    // preview session or the game's category settings, and a new width. The heads are the live plays' (0.1 s). A player's
    // open one takes the record: the followed play's frames of this item as dots, the line of that play, and a warning where
    // they differ from it
    internal sealed class ItemGraphField : VisualElement, IRecordView
    {
        internal const string FoldSuffix = ".Graph";
        internal const string Title = "Graph";

        static readonly Regex s_Index = new Regex(@"\.Array\.data\[(\d+)\]$");

        internal readonly Foldout Fold;
        internal readonly VisualElement Header;
        internal readonly Label Notes;
        internal readonly HelpBox Notice;
        internal readonly MotionGraphView View;
        internal readonly List<Toggle> Components = new List<Toggle>();
        internal PopupField<string> ChannelChoice;
        internal Toggle DotsChoice;
        internal ItemGraphModel Model;
        // how many times it built — an open graph's work
        internal int Builds;

        readonly SerializedObject m_So;
        // the list's owner, kept: an inspector's rebuild disposes the serialized object before it clears this field
        readonly Object m_Owner;
        readonly string m_Path;
        readonly Component m_Host;
        readonly string m_FoldKey;
        GraphInputs m_Inputs;
        float m_Scale = float.NaN;
        bool m_Off;
        bool m_Dirty = true;
        bool m_Scheduled;
        int m_Channel;
        int m_Columns;
        int m_Polls;
        // why the last press could not scrub — null when it did
        string m_Refusal;
        readonly RecordSeries m_Series = new RecordSeries();
        bool m_Lost;

        // the bodies that draw nothing — a non-target item, an item whose binding describes no value, a Motion Driver's
        // Follow — have no Graph
        internal static bool Draws(FeedbackItem item, Component host)
        {
            if (host is MotionDriver driver && driver.m_Mode != MotionDriverMode.Loop)
                return false;
            if (!(item is TargetItem target) || target.m_Motion == null)
                return false;
            ItemBinding binding = FeedbackImplementations.Create(target);
            if (binding == null)
                return false;
            var channels = new List<GraphChannel>();
            binding.Describe(channels);
            return channels.Count > 0;
        }

        internal ItemGraphField(SerializedProperty item, string foldKey, Component host)
        {
            AddToClassList(InspectorStyles.Graph);
            m_So = item.serializedObject;
            m_Owner = m_So.targetObject;
            m_Path = item.propertyPath;
            m_Host = host;
            m_FoldKey = foldKey != null ? foldKey + FoldSuffix : null;
            Fold = new Foldout { text = Title, value = m_FoldKey != null && SessionState.GetBool(m_FoldKey, false) };
            Fold.RegisterValueChangedCallback(e =>
            {
                if (e.target != Fold)
                    return;
                if (m_FoldKey != null)
                    SessionState.SetBool(m_FoldKey, e.newValue);
                if (e.newValue)
                {
                    Watch();
                    Changed();
                }
                else
                    Unwatch();
            });
            Add(Fold);
            Header = new VisualElement();
            Header.AddToClassList(InspectorStyles.GraphHeader);
            Fold.Add(Header);
            Notice = new HelpBox("", HelpBoxMessageType.Warning) { style = { display = DisplayStyle.None } };
            Fold.Add(Notice);
            View = new MotionGraphView { Dots = m_Series.Ring };
            View.RegisterCallback<GeometryChangedEvent>(_ =>
            {
                if (Fold.value && ColumnsNow() != m_Columns)
                    Changed();
            });
            Fold.Add(View);
            if (host is FeedbackPlayer)
                View.Scrubbed = Scrub;
            Notes = new Label();
            Notes.AddToClassList(InspectorStyles.GraphNotes);
            Fold.Add(Notes);
            this.TrackSerializedObjectValue(m_So, _ => Edited());
            // an element tracks one object: the host's settings (a linked or a variant list's player) on a child of its own
            if (host != null && host != m_So.targetObject)
            {
                var hostWatch = new VisualElement { style = { display = DisplayStyle.None } };
                Add(hostWatch);
                hostWatch.TrackSerializedObjectValue(new SerializedObject(host), _ => Edited());
            }
            RegisterCallback<AttachToPanelEvent>(_ => Watch());
            RegisterCallback<DetachFromPanelEvent>(_ => Unwatch());
            schedule.Execute(Poll).Every(100);
            if (Fold.value)
                Changed();
        }

        internal FeedbackPlayer Player => m_Host as FeedbackPlayer;

        FeedbackPlayer IRecordView.Player => Player;

        Object IRecordView.Owner => m_Owner;

        // a player's item only — a Motion Driver has no record
        void Watch()
        {
            if (Fold.value && panel != null && Player != null)
                RecordWatch.Add(this);
        }

        void Unwatch()
        {
            RecordWatch.Remove(this);
            m_Series.Clear();
            ShowNotice();
        }

        // an edit of the list or the host: the followed play is of the data before
        void Edited()
        {
            if (Player != null)
                RecordWatch.Forget(Player);
            Changed();
        }

        void IRecordView.Began(RecordFollow follow)
        {
            m_Series.Clear();
            m_Lost = false;
            ShowNotice();
            View.MarkDirtyRepaint();
            Changed();
        }

        void IRecordView.Framed(RecordFollow follow)
        {
            if (Model == null || !m_Series.Item(Model, follow, ListIndex(), Model.Channel))
                return;
            ShowNotice();
            if (follow.Lost != m_Lost)
            {
                m_Lost = follow.Lost;
                Notes.text = NotesText();
            }
            View.MarkDirtyRepaint();
        }

        void ShowNotice()
        {
            bool on = Model != null && m_Series.Ring.Differs;
            if (on)
                Notice.text = "Recorded values differ from this item's line.";
            Notice.style.display = on ? DisplayStyle.Flex : DisplayStyle.None;
        }

        RecordFollow Follow() => Player != null ? RecordWatch.FollowOf(Player, m_So.targetObject) : null;

        // a press on the graph: the host's preview scrubs to that place — a refusal's words in the notes
        void Scrub(double seconds)
        {
            PreviewBar bar = PreviewBar.Near(this, m_Host);
            if (bar == null)
                return;
            SeekRefusal refusal = bar.ScrubAt(seconds);
            m_Refusal = refusal == SeekRefusal.None ? null : PreviewTexts.Of(refusal);
            if (Model != null)
                Notes.text = NotesText();
        }

        int ColumnsNow()
        {
            float w = View.contentRect.width;
            return float.IsNaN(w) || w < 16f ? 300 : Math.Min(ItemGraphModel.MaxColumns, (int)w);
        }

        int ListIndex()
        {
            Match m = s_Index.Match(m_Path);
            return m.Success ? int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) : -1;
        }

        // an edit, a session, settings or a width: built again at the next frame, once — only while open
        internal void Changed()
        {
            m_Dirty = true;
            if (!Fold.value || m_Scheduled)
                return;
            m_Scheduled = true;
            schedule.Execute(() =>
            {
                m_Scheduled = false;
                if (m_Dirty && Fold.value)
                    Build();
            });
        }

        void Poll()
        {
            if (!Fold.value || panel == null)
                return;
            if (!GraphInputs.Of(m_Host).Same(in m_Inputs))
                Changed();
            if (Model != null && ++m_Polls % 3 == 0)
            {
                float scale = GraphInputs.CategoryScale(Model.Category, m_Inputs.Live, out bool off);
                if (BitConverter.SingleToInt32Bits(scale) != BitConverter.SingleToInt32Bits(m_Scale) || off != m_Off)
                    Changed();
            }
            int before = View.Heads.Count;
            FeedbackPlayer player = Player;
            TimelineModel.Heads(player != null && TimelineModel.HeadsShown(player) ? player : null, m_So.targetObject, View.Heads);
            if (before > 0 || View.Heads.Count > 0)
                View.MarkDirtyRepaint();
            if (DotsChoice != null && DotsChoice.value != MotionGraphView.DotsOn)
            {
                DotsChoice.SetValueWithoutNotify(MotionGraphView.DotsOn);
                View.MarkDirtyRepaint();
            }
        }

        // a Persist's start from the preview's record of this item, once the record reached it
        Vector4? SessionStart(List<FeedbackItem> items, int listIndex)
        {
            FeedbackPlayer player = Player;
            PreviewSession session = PreviewSession.Current;
            if (player == null || session == null || session.Subject != m_Host)
                return null;
            PlayerRuntime runtime = m_So.targetObject is FeedbackPreset preset ? player.RuntimeFor(preset) : player.Runtime;
            if (runtime == null || runtime.IsReleased)
                return null;
            var descs = new List<TimelineItemDesc>(items.Count);
            var map = new List<int>(items.Count);
            TimelineConversion.DescribeList(items, descs, map);
            int k = map.IndexOf(listIndex);
            if (k < 0 || k >= runtime.ItemCount)
                return null;
            for (int r = 0; r < runtime.RecordCount; r++)
            {
                PlaybackRecord record = runtime.Record(r);
                if (record.Active && record.Handle == session.Handle && record.Touched[k] && runtime.Binding(k) is ItemBinding binding
                    && binding.TryGetStartValue(r, out Vector4 start))
                    return start;
            }
            return null;
        }

        // a Persist's start from the followed play — its binding keeps the record's
        static Vector4? FollowStart(RecordFollow follow, int listIndex)
        {
            int k = follow.ProgramItem(listIndex);
            if (k < 0 || !(follow.Live.Binding(k) is ItemBinding binding) || !binding.TryGetStartValue(follow.Record, out Vector4 start))
                return null;
            return start;
        }

        internal void Build()
        {
            m_Dirty = false;
            Builds++;
            m_Columns = ColumnsNow();
            m_Inputs = GraphInputs.Of(m_Host);
            m_So.UpdateIfRequiredOrScript();
            if (m_Host is MotionDriver driver)
                Model = ItemGraphModel.BuildDriver(driver, in m_Inputs, m_Channel, m_Columns);
            else
            {
                List<FeedbackItem> items = TimelineModel.Items(m_So, ItemListEdits.ItemsPath);
                int index = ListIndex();
                RecordFollow follow = Follow();
                if (follow != null && follow.Ready)
                    Model = ItemGraphModel.Build(items, index, m_So.targetObject, Player, in m_Inputs, m_Channel, m_Columns, FollowStart(follow, index), follow);
                else
                    Model = ItemGraphModel.Build(items, index, m_So.targetObject, Player, in m_Inputs, m_Channel, m_Columns, m_Inputs.FromSession ? SessionStart(items, index) : null);
            }
            ShowNotice();
            m_Scale = Model.Scale;
            m_Off = Model.ScaleOff;
            BuildHeader();
            Notes.text = NotesText();
            View.Model = Model;
            View.Refresh();
        }

        void BuildHeader()
        {
            Header.Clear();
            Components.Clear();
            ChannelChoice = null;
            DotsChoice = null;
            if (Model.Channels.Count > 1)
            {
                var names = new List<string>();
                foreach (GraphChannel ch in Model.Channels)
                    names.Add(ch.Name);
                ChannelChoice = new PopupField<string>(names, Model.Channel);
                ChannelChoice.AddToClassList(InspectorStyles.GraphChannel);
                ChannelChoice.RegisterValueChangedCallback(e =>
                {
                    m_Channel = names.IndexOf(e.newValue);
                    Changed();
                });
                Header.Add(ChannelChoice);
            }
            if (Model.Dimension > 1)
            {
                for (int d = 0; d < Model.Dimension; d++)
                {
                    int component = d;
                    var toggle = new Toggle { text = MotionGraphView.ComponentNames[d], value = View.Shown[d] };
                    toggle.AddToClassList(InspectorStyles.GraphComponent);
                    toggle.Q<Label>(className: Toggle.textUssClassName).style.color = MotionGraphView.ComponentColors[d];
                    toggle.RegisterValueChangedCallback(e =>
                    {
                        View.Shown[component] = e.newValue;
                        View.Refresh();
                    });
                    Components.Add(toggle);
                    Header.Add(toggle);
                }
            }
            // a player's item only — a Motion Driver has no record
            if (Player != null && Model.Channels.Count > 0)
            {
                DotsChoice = View.NewDotsToggle();
                Header.Add(DotsChoice);
            }
        }

        static string Number(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);

        // what the graph is and where it differs from the preview or the game
        internal string NotesText()
        {
            var parts = new List<string>();
            if (Model.Channels.Count == 0)
                return "";
            GraphChannel ch = Model.Shown;
            if (Model.Toggle)
                parts.Add("On · off where the item drives it");
            else if (Model.Weight)
                parts.Add("The weight toward the item's value");
            if (!string.IsNullOrEmpty(ch.Unit))
                parts.Add("Values in " + ch.Unit);
            if (ch.Space != LayerSpace.Local)
                parts.Add(ch.Space == LayerSpace.Self ? "On the target's own axes" : "In world space");
            RecordFollow follow = Model.Play != null ? Follow() : null;
            if (follow != null && follow.Copy == Model.Play)
            {
                parts.Add("Play " + Model.Play.Serial + " · seed " + Model.Play.Seed + " · intensity " + Number(Model.Play.Intensity));
                int k = follow.ProgramItem(ListIndex());
                int receivers = k >= 0 && follow.Live.Binding(k) is CameraShakeBinding shake ? shake.Receivers(follow.Record) : 0;
                if (receivers > 0)
                    parts.Add("On " + receivers + (receivers == 1 ? " receiver" : " receivers"));
                if (follow.Lost)
                    parts.Add("Not compared after the play waited off its axis");
            }
            else
            {
                parts.Add(Model.Inputs.FromSession ? "Preview seed " + Model.Inputs.Seed : "Sample seed");
                if (Model.Inputs.Free)
                    parts.Add("Unconditional — Chance and random lengths are off");
            }
            string category = Model.Category.ToString();
            if (Model.ScaleOff)
                parts.Add(Model.Inputs.Live ? category + " is turned off" : category + " is off in the project settings — the preview plays it");
            else if (Model.Scale != 1f && Model.Write != MotionWrite.Persist)
                parts.Add(Model.Inputs.Live ? "Live: " + category + " ×" + Number(Model.Scale) : "The game applies " + category + " ×" + Number(Model.Scale) + " — the preview plays ×1");
            if (Model.Skip != null && !Model.ScaleOff)
                parts.Add(Model.Skip == "off" ? "The item is off" : Model.Skip == "this play" ? "Left out of this play" : "Left out of the play at this seed (" + Model.Skip + ")");
            if (Model.StartMatters && !Model.StartKnown)
                parts.Add("From the target's start, drawn at 0");
            if (Model.Endless)
                parts.Add(Model.Stays != null ? "Endless — a " + Model.Stays + " stays at its start; Repeat Forever loops it" : "Endless — the first " + Number((float)(Model.To - Model.Start)) + " s");
            FeedbackPlayer player = Player;
            PlayerDirection direction = player != null ? player.m_Direction : PlayerDirection.Forward;
            PreviewSession session = PreviewSession.Current;
            if (player != null && session != null && session.Subject == m_Host && session.Args.HasDirection)
                direction = session.Args.Direction;
            if (follow != null)
            {
                if (Model.Play.Reverse)
                    parts.Add("Played backward");
            }
            else if (direction == PlayerDirection.Backward)
                parts.Add("This player plays backward — the graph shows the list forward");
            else if (direction == PlayerDirection.Toggle)
                parts.Add("This player turns its direction each play — the graph shows the list forward");
            if (m_Refusal != null)
                parts.Add(m_Refusal);
            if (EditorApplication.isPlaying && player != null && PlayModeEdits.Pending(player))
                parts.Add("The record waits for the Rebuild");
            return string.Join(" · ", parts);
        }
    }
}
