using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace xpTURN.Feedback.Editor
{
    // the player's Graph Preview, a box below the bottom bar: a slot the shown list writes and its items' values composed as
    // the game composes them, on the strip's axis — the sum, each item alone faint (one more an editor frame), the capped
    // group's size and limit. Its title row's switch is off at first (per player — in a preset's inspector per preset — the
    // session's): off, the box is its title row and builds nothing; on, built again after an edit of the list or the player,
    // a change of the preview session or the settings, and a new width. A list that draws nothing shows its note only. The
    // list's selection makes its items' lines strong; a press scrubs the preview. While on it takes the record: the followed
    // play's frames as dots, the line of that play, and a warning where the frames differ from it
    internal sealed class SlotGraphField : VisualElement, IRecordView
    {
        internal const string Title = "Graph Preview";
        internal const string TitleTooltip = "What the shown list writes to one property, composed as the game composes it, on the strip's time";
        internal const string SwitchTooltip = "On: draws the graph and follows the player's next play. Off: nothing is built or recorded";
        internal const string SwitchPrefix = "xpTURN.Feedback.Inspector.SlotGraph.";
        internal const string NoPlayerText = "Pick a Graph Player — the graph finds its targets in that player's scene";
        static readonly string[] s_Sized = { "X", "Y", "Z", "Size" };

        internal readonly Toggle Switch;
        // what the switch shows — the header, the notice, the graph and the notes
        internal readonly VisualElement Body;
        internal readonly VisualElement Header;
        internal readonly Label Notes;
        internal readonly HelpBox Notice;
        internal readonly MotionGraphView View;
        internal readonly List<Toggle> Components = new List<Toggle>();
        internal PopupField<string> GroupChoice;
        internal Toggle DotsChoice;
        internal SlotGraphModel Model;
        internal int Builds;
        // the items alone drawn so far, by list index
        internal readonly List<int> AloneItems = new List<int>();
        // a line before the notes — null for none
        internal Func<string> Note;

        readonly TimelineStrip m_Strip;
        readonly SerializedObject m_So;
        // the list's owner, kept: an inspector's rebuild disposes the serialized object before it clears this field
        readonly Object m_Owner;
        readonly FeedbackPlayer m_Player;
        readonly ItemListView m_List;
        readonly string m_SwitchKey;
        readonly List<float[]> m_AloneTicks = new List<float[]>();
        IVisualElementScheduledItem m_AloneJob;
        GraphInputs m_Inputs;
        string m_Settings;
        bool m_Dirty = true;
        bool m_Scheduled;
        bool m_Axes;
        int m_Columns;
        int m_Polls;
        string m_Refusal;
        readonly RecordSeries m_Series = new RecordSeries();
        bool m_Lost;

        // player: null draws nothing (a note) · switchOwner: the switch's object — the player when null
        internal SlotGraphField(TimelineStrip strip, SerializedObject listSo, FeedbackPlayer player, ItemListView list, Object switchOwner = null)
        {
            AddToClassList(InspectorStyles.Graph);
            AddToClassList(InspectorStyles.GraphBox);
            m_Strip = strip;
            m_So = listSo;
            m_Owner = listSo.targetObject;
            m_Player = player;
            m_List = list;
            m_SwitchKey = SwitchPrefix + (switchOwner != null ? switchOwner : player).GetEntityId();
            Switch = TitleRow(this, SessionState.GetBool(m_SwitchKey, false), TitleTooltip, SwitchTooltip);
            Switch.RegisterValueChangedCallback(e =>
            {
                if (e.target != Switch)
                    return;
                SessionState.SetBool(m_SwitchKey, e.newValue);
                Body.style.display = e.newValue ? DisplayStyle.Flex : DisplayStyle.None;
                if (e.newValue)
                {
                    Watch();
                    Changed();
                }
                else
                    Unwatch();
            });
            Body = new VisualElement { style = { display = Switch.value ? DisplayStyle.Flex : DisplayStyle.None } };
            Add(Body);
            Header = new VisualElement();
            Header.AddToClassList(InspectorStyles.GraphHeader);
            Body.Add(Header);
            Notice = new HelpBox("", HelpBoxMessageType.Warning) { style = { display = DisplayStyle.None } };
            Body.Add(Notice);
            View = new MotionGraphView { Scrubbed = Scrub, Dots = m_Series.Ring };
            // a parent that moves leaves the view's own layout as it was — its geometry and the field's call the alignment too
            View.RegisterCallback<GeometryChangedEvent>(_ => Align());
            RegisterCallback<GeometryChangedEvent>(_ => Align());
            Body.RegisterCallback<GeometryChangedEvent>(_ => Align());
            Body.Add(View);
            Notes = new Label();
            Notes.AddToClassList(InspectorStyles.GraphNotes);
            Body.Add(Notes);
            this.TrackSerializedObjectValue(listSo, _ => Edited());
            // an element tracks one object: the player's settings (a linked or a variant list's player) on a child of its own
            if (player != null && listSo.targetObject != player)
            {
                var playerWatch = new VisualElement { style = { display = DisplayStyle.None } };
                Add(playerWatch);
                playerWatch.TrackSerializedObjectValue(new SerializedObject(player), _ => Edited());
            }
            Action<IEnumerable<int>> highlight = _ => Highlight();
            // the list outlives a field built again (a preset's inspector's Graph Player): the selection's handler while attached
            RegisterCallback<AttachToPanelEvent>(_ =>
            {
                Watch();
                if (m_List != null)
                    m_List.List.selectedIndicesChanged += highlight;
            });
            RegisterCallback<DetachFromPanelEvent>(_ =>
            {
                Unwatch();
                if (m_List != null)
                    m_List.List.selectedIndicesChanged -= highlight;
            });
            strip.Canvas.RegisterCallback<GeometryChangedEvent>(_ =>
            {
                Align();
                if (Switch.value && ColumnsNow() != m_Columns)
                    Changed();
            });
            schedule.Execute(Poll).Every(100);
            if (Switch.value)
                Changed();
        }

        // a Graph Preview box's title row: the title and, at the row's right end, the switch — the switch
        internal static Toggle TitleRow(VisualElement box, bool on, string titleTooltip, string switchTooltip)
        {
            var row = new VisualElement();
            row.AddToClassList(InspectorStyles.GraphTitleRow);
            var title = new Label(Title) { tooltip = titleTooltip };
            title.AddToClassList(InspectorStyles.GraphTitle);
            row.Add(title);
            var toggle = new Toggle { value = on, tooltip = switchTooltip };
            toggle.AddToClassList(InspectorStyles.GraphSwitch);
            row.Add(toggle);
            box.Add(row);
            return toggle;
        }

        // a box's parts that only a drawn line has — the header and the graph; the notes say why when there is none
        internal static void ShowDrawn(VisualElement header, VisualElement view, SlotGraphModel model)
        {
            DisplayStyle d = model != null && model.Sum != null && model.Refused == null ? DisplayStyle.Flex : DisplayStyle.None;
            header.style.display = d;
            view.style.display = d;
        }

        string GroupKey => m_SwitchKey + ".Slot";

        FeedbackPlayer IRecordView.Player => m_Player;

        Object IRecordView.Owner => m_Owner;

        void Watch()
        {
            if (Switch.value && panel != null && m_Player != null)
                RecordWatch.Add(this);
        }

        void Unwatch()
        {
            RecordWatch.Remove(this);
            m_Series.Clear();
            ShowNotice();
        }

        // an edit of the list or the player: the followed play is of the data before
        void Edited()
        {
            RecordWatch.Forget(m_Player);
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
            if (Model == null || !m_Series.Slot(Model, follow))
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
            SlotGroup group = Model?.Shown;
            bool on = group != null && m_Series.Ring.Differs;
            if (on)
                Notice.text = "Recorded values differ from this list's line — another player, a MotionDriver or a script also writes " + group.Name + ".";
            Notice.style.display = on ? DisplayStyle.Flex : DisplayStyle.None;
        }

        int ColumnsNow()
        {
            float w = m_Strip.Canvas.contentRect.width - TimelineStrip.Pad * 2f;
            return float.IsNaN(w) || w < 16f ? 300 : Math.Min(ItemGraphModel.MaxColumns, (int)w);
        }

        // the drawing spans the strip's axis: from its left pad to its right pad
        void Align()
        {
            // the hierarchy's parent: the body
            VisualElement parent = View.hierarchy.parent;
            Rect c = m_Strip.Canvas.worldBound;
            if (parent == null || float.IsNaN(c.width) || float.IsNaN(parent.worldBound.width) || parent.worldBound.width <= 0f)
                return;
            float pMin = parent.worldBound.xMin + parent.contentRect.xMin;
            float pMax = pMin + parent.contentRect.width;
            float left = c.xMin + TimelineStrip.Pad - pMin;
            float right = pMax - (c.xMax - TimelineStrip.Pad);
            if (Math.Abs(View.resolvedStyle.marginLeft - left) > 0.5f)
                View.style.marginLeft = left;
            if (Math.Abs(View.resolvedStyle.marginRight - right) > 0.5f)
                View.style.marginRight = right;
        }

        // an edit, a session, settings or a width: built again at the next frame, once — only while on
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

        internal static string SettingsSignature(bool live)
        {
            SettingsValues v = SlotGraphModel.GameSettings(live);
            var parts = new List<string>();
            for (int i = 0; i < SettingsValues.CategoryCount; i++)
                parts.Add((v.Enabled[i] ? "1" : "0") + v.Scale[i].ToString(CultureInfo.InvariantCulture));
            parts.Add(v.MaximumShakeOffset.ToString(CultureInfo.InvariantCulture));
            parts.Add(v.MaximumShakeAngle.ToString(CultureInfo.InvariantCulture));
            return string.Join(",", parts);
        }

        void Poll()
        {
            if (!Switch.value || panel == null)
                return;
            Align();
            if (m_Player == null)
                return;
            if (!GraphInputs.Of(m_Player).Same(in m_Inputs))
                Changed();
            if (Model != null && ++m_Polls % 3 == 0 && SettingsSignature(m_Inputs.Live) != m_Settings)
                Changed();
            int before = View.Heads.Count;
            TimelineModel.Heads(TimelineModel.HeadsShown(m_Player) ? m_Player : null, m_So.targetObject, View.Heads);
            if (before > 0 || View.Heads.Count > 0)
                View.MarkDirtyRepaint();
            if (DotsChoice != null && DotsChoice.value != MotionGraphView.DotsOn)
            {
                DotsChoice.SetValueWithoutNotify(MotionGraphView.DotsOn);
                View.MarkDirtyRepaint();
            }
        }

        internal void Build()
        {
            m_Dirty = false;
            if (m_Player == null)
            {
                Model = null;
                View.Model = null;
                Header.Clear();
                Components.Clear();
                GroupChoice = null;
                DotsChoice = null;
                Notes.text = NoPlayerText;
                ShowDrawn(Header, View, null);
                View.Refresh();
                return;
            }
            Builds++;
            m_AloneJob?.Pause();
            m_AloneJob = null;
            m_AloneTicks.Clear();
            AloneItems.Clear();
            m_Axes = false;
            m_Columns = ColumnsNow();
            m_Inputs = GraphInputs.Of(m_Player);
            m_Settings = SettingsSignature(m_Inputs.Live);
            m_So.UpdateIfRequiredOrScript();
            List<FeedbackItem> items = TimelineModel.Items(m_So, ItemListEdits.ItemsPath);
            Object owner = m_So.targetObject;
            PropertyLayerRegistry live = SlotGraphModel.LiveLayers(m_Player, EditorApplication.isPlayingOrWillChangePlaymode);
            string preferred = SessionState.GetString(GroupKey, null);
            Model = SlotGraphModel.Build(m_Player, items, owner, in m_Inputs, m_Strip.Span, m_Columns, string.IsNullOrEmpty(preferred) ? null : preferred, live, null,
                RecordWatch.FollowOf(m_Player, owner));
            ShowNotice();
            View.Model = Model.Sum;
            View.Faint.Clear();
            View.Strong.Clear();
            View.Names = Model.Capped ? s_Sized : MotionGraphView.ComponentNames;
            View.Cap = Model.Capped ? Model.Cap : float.NaN;
            BuildHeader();
            Notes.text = NotesText();
            ShowDrawn(Header, View, Model);
            View.Refresh();
            Align();
            SlotGroup group = Model.Shown;
            if (group == null || group.Items.Count < 2)
                return;
            int next = 0;
            SlotGraphModel model = Model;
            m_AloneJob = schedule.Execute(() =>
            {
                if (model != Model || next >= group.Items.Count)
                {
                    m_AloneJob?.Pause();
                    return;
                }
                int listIndex = group.Items[next++];
                ItemGraphModel alone = model.Alone(m_Player, items, owner, listIndex, live, out float[] ticks);
                AloneItems.Add(listIndex);
                m_AloneTicks.Add(ticks);
                View.Faint.Add(alone);
                View.Strong.Add(Selected(listIndex));
                if (next >= group.Items.Count)
                {
                    m_Axes = model.AxesInteract(m_AloneTicks);
                    Notes.text = NotesText();
                }
                View.Refresh();
            }).Every(0);
        }

        // the alone lines' sums — tests compare them with the sum
        internal IReadOnlyList<float[]> AloneTicks => m_AloneTicks;

        bool Selected(int listIndex)
        {
            if (m_List == null)
                return false;
            foreach (int i in m_List.List.selectedIndices)
            {
                if (i == listIndex)
                    return true;
            }
            return false;
        }

        void Highlight()
        {
            for (int i = 0; i < View.Strong.Count && i < AloneItems.Count; i++)
                View.Strong[i] = Selected(AloneItems[i]);
            View.MarkDirtyRepaint();
        }

        void BuildHeader()
        {
            DotsChoice = null;
            GroupChoice = BuildHeader(Header, Model, View, Components, GroupKey, Changed);
            if (Model.Groups.Count > 0)
            {
                DotsChoice = View.NewDotsToggle();
                Header.Add(DotsChoice);
            }
        }

        // a slot graph's header: the groups' choice (the pick kept under groupKey — changed builds again) and a toggle a
        // component; the choice, null without a group
        internal static PopupField<string> BuildHeader(VisualElement header, SlotGraphModel model, MotionGraphView view, List<Toggle> components, string groupKey, Action changed)
        {
            header.Clear();
            components.Clear();
            PopupField<string> choice = null;
            if (model.Groups.Count > 0)
            {
                var names = new List<string>();
                foreach (SlotGroup g in model.Groups)
                    names.Add(g.Name + " (" + g.Items.Count + ")");
                choice = new PopupField<string>(names, model.Group);
                choice.AddToClassList(InspectorStyles.GraphChannel);
                choice.RegisterValueChangedCallback(e =>
                {
                    int g = names.IndexOf(e.newValue);
                    if (g < 0)
                        return;
                    SessionState.SetString(groupKey, model.Groups[g].Name);
                    changed();
                });
                header.Add(choice);
            }
            ItemGraphModel sum = model.Sum;
            if (sum != null && sum.Dimension > 1)
            {
                for (int d = 0; d < sum.Dimension; d++)
                {
                    int component = d;
                    var toggle = new Toggle { text = view.Names[d], value = view.Shown[d] };
                    toggle.AddToClassList(InspectorStyles.GraphComponent);
                    toggle.Q<Label>(className: Toggle.textUssClassName).style.color = MotionGraphView.ComponentColors[d];
                    toggle.RegisterValueChangedCallback(e =>
                    {
                        view.Shown[component] = e.newValue;
                        view.Refresh();
                    });
                    components.Add(toggle);
                    header.Add(toggle);
                }
            }
            return choice;
        }

        // what a slot graph's line is: a toggle's, a product's, a turn's, an offset's
        internal static string FormText(SlotGraphModel model)
        {
            if (model.Toggle)
                return "On · off — the latest started wins";
            if (model.Composition == Composition.Multiplicative)
                return "× factor − 1 from the start";
            if (model.Composition == Composition.Rotation)
                return "Turn from the start in °";
            return "From the start";
        }

        // a press on the graph: the player's preview scrubs to that place — a refusal's words in the notes
        void Scrub(double seconds)
        {
            PreviewBar bar = PreviewBar.Near(this, m_Player);
            if (bar == null)
                return;
            SeekRefusal refusal = bar.ScrubAt(seconds);
            m_Refusal = refusal == SeekRefusal.None ? null : PreviewTexts.Of(refusal);
            if (Model != null)
                Notes.text = NotesText();
        }

        static string Number(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);

        // what the graph is and where it differs from the preview or the game
        internal string NotesText()
        {
            var parts = new List<string>();
            string note = Note?.Invoke();
            if (note != null)
                parts.Add(note);
            if (Model.Groups.Count == 0)
                parts.Add("No item of this list writes a value the graph draws");
            else
            {
                parts.Add(FormText(Model));
                RecordFollow follow = Model.Follow;
                if (follow != null)
                {
                    parts.Add("the play's start values");
                    parts.Add("Play " + follow.Copy.Serial + " · seed " + follow.Copy.Seed + " · intensity " + Number(follow.Copy.Intensity));
                    if (follow.Lost)
                        parts.Add("Not compared after the play waited off its axis");
                }
                else
                {
                    parts.Add(Model.Seeded ? "the preview's start values" : "the scene's current values");
                    parts.Add(m_Inputs.FromSession ? "Preview seed " + m_Inputs.Seed : "Sample seed");
                    if (m_Inputs.Free)
                        parts.Add("Unconditional — Chance and random lengths are off");
                }
                if (Model.Capped)
                    parts.Add(m_Inputs.Live ? "Camera shakes capped at " + Number(Model.Cap) : "The game caps camera shakes at " + Number(Model.Cap) + " — the preview does not");
                if (m_Axes)
                    parts.Add("Rotations combine as a product — the axes interact");
                if (Model.RandomLengths)
                    parts.Add("Random lengths at this seed — the strip shows the set lengths");
            }
            if (Model.NotDrawn.Count > 0)
                parts.Add("Not drawn: " + string.Join(", ", Model.NotDrawn) + " (a channel's receivers are found when it plays · a target not found)");
            if (Model.Refused != null)
                parts.Add("Nothing drawn: " + Model.Refused);
            if (EditorApplication.isPlaying && PlayModeEdits.Pending(m_Player))
                parts.Add("The record waits for the Rebuild");
            if (m_Refusal != null)
                parts.Add(m_Refusal);
            return string.Join(" · ", parts);
        }
    }
}
