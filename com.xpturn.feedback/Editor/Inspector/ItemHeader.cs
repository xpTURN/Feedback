using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace xpTURN.Feedback.Editor
{
    // an item's header — the fold, the switch, the category colour, the label or what it targets, the motion, the time, the
    // validation's slot, and in edit mode Solo · Mute · ▶ (not on a flow item — it is never masked; not in play mode)
    internal sealed class ItemHeader : VisualElement
    {
        internal readonly Foldout Fold = new Foldout { text = "", value = false };
        internal readonly Toggle Active = new Toggle();
        internal readonly VisualElement Swatch = new VisualElement();
        internal readonly Label Summary = new Label();
        internal readonly Label Motion = new Label();
        internal readonly Label Time = new Label();
        internal readonly Toggle Solo = new Toggle { text = "S", tooltip = "Solo — this preview plays this item and the other solo items" };
        internal readonly Toggle Mute = new Toggle { text = "M", tooltip = "Mute — this preview skips this item" };
        internal readonly Button Play;
        internal readonly Button Warning = new Button();
        string m_Category;

        internal ItemHeader(Action play)
        {
            AddToClassList(InspectorStyles.Header);
            Swatch.AddToClassList(InspectorStyles.Category);
            Summary.AddToClassList(InspectorStyles.Summary);
            Play = new Button(play) { iconImage = Background.FromTexture2D(EditorGUIUtility.IconContent("PlayButton").image as Texture2D), tooltip = "Preview this item alone" };
            Add(Fold);
            Add(Active);
            Add(Swatch);
            Add(Summary);
            Add(Motion);
            Add(Time);
            Warning.AddToClassList(InspectorStyles.ValidationWarning);
            Warning.style.display = DisplayStyle.None;
            Add(Warning);
            Add(Solo);
            Add(Mute);
            Add(Play);
        }

        internal void Show(FeedbackItem item, bool missing)
        {
            Summary.text = item == null ? (missing ? "(missing type)" : "(empty)") : ItemWords.Summary(item);
            Motion.text = item is TargetItem t ? (t.m_Motion != null ? t.m_Motion.Kind.ToString() : "(no motion)") : "";
            Time.text = item == null ? "" : ItemWords.Time(item);
            if (m_Category != null)
                Swatch.RemoveFromClassList(m_Category);
            m_Category = item is EffectItem e ? InspectorStyles.CategoryClass(e.EffectiveCategory) : null;
            if (m_Category != null)
                Swatch.AddToClassList(m_Category);
            bool controls = item is EffectItem && !EditorApplication.isPlayingOrWillChangePlaymode;
            DisplayStyle shown = controls ? DisplayStyle.Flex : DisplayStyle.None;
            Solo.style.display = shown;
            Mute.style.display = shown;
            Play.style.display = shown;
        }
    }

    // the header's words — what the item targets, its time
    internal static class ItemWords
    {
        internal static string Name(Type type)
        {
            if (!FeedbackCatalog.Meta(type, out string path, out _))
                return type.Name;
            int slash = path.LastIndexOf('/');
            return slash < 0 ? path : path.Substring(slash + 1);
        }

        internal static string Summary(FeedbackItem item)
        {
            string name = string.IsNullOrEmpty(item.m_Label) ? Name(item.GetType()) : item.m_Label;
            // the player a Play Player plays · a note's first line
            if (item is PlayPlayerItem play)
                return name + " · " + (play.m_Player != null ? play.m_Player.name : "(none)");
            if (item is DebugItem note && note.m_Action == DebugAction.Note && !string.IsNullOrEmpty(note.m_Message))
                return name + " · " + FirstLine(note.m_Message);
            if (!(item is TargetItem t))
                return name;
            return name + " · " + (TargetChecks.Global(item.GetType()) ? "Global" : Target(t.m_Target));
        }

        static string FirstLine(string text)
        {
            int end = text.IndexOf('\n');
            string line = end < 0 ? text : text.Substring(0, end);
            return line.Length > 40 ? line.Substring(0, 40) + "…" : line;
        }

        internal static string Target(in TargetRef target)
        {
            switch (target.m_Mode)
            {
                case TargetRefMode.Direct:
                    return target.m_Object != null ? target.m_Object.name : "(none)";
                case TargetRefMode.Self:
                    return "Self";
                case TargetRefMode.Child:
                    return target.m_ChildLookup == ChildLookup.Index ? "Child #" + target.m_Index
                        : target.m_ChildLookup == ChildLookup.Path ? "Child " + target.m_Path : "First child";
                case TargetRefMode.Parent:
                    return "Parent";
                case TargetRefMode.Anchor:
                    return "@" + target.m_Anchor;
                case TargetRefMode.MainCamera:
                    return "Main Camera";
                default:
                    return target.m_Channel.m_Mode == ChannelRefMode.Int ? "Channel " + target.m_Channel.m_Id
                        : "Channel " + (target.m_Channel.m_Asset != null ? target.m_Channel.m_Asset.name : "(none)");
            }
        }

        // 'delay + length × runs' — ∞ · (bg); an action item's own length; a marker has none
        internal static string Time(FeedbackItem item)
        {
            if (ItemRules.Marker(item))
                return "";
            ItemTiming t = item.m_Timing;
            string length = item is ActionItem open && open.Opens ? (open.NaturalDuration < float.PositiveInfinity ? "≤" + open.NaturalDuration.ToString("0.##") : "wait")
                : item is ActionItem a ? a.NaturalDuration.ToString("0.##")
                : item is WaitForSignalItem w ? (w.m_Timeout > 0f ? "≤" + w.m_Timeout.ToString("0.##") : "signal")
                : t.m_Infinite ? "∞" : t.m_Duration.ToString("0.##");
            string runs = t.m_RepeatForever ? " × ∞" : t.m_RepeatCount > 0 ? " × " + (1 + t.m_RepeatCount) : "";
            string delay = t.m_Delay > 0f && !(item is LoopEndItem) ? t.m_Delay.ToString("0.##") + " + " : "";
            return delay + length + runs + (t.m_Background && ItemRules.Visible(item, ItemRules.Background) ? " (bg)" : "") + Way(item);
        }

        // the item's own way
        static string Way(FeedbackItem item)
        {
            if (!(item is EffectItem e))
                return "";
            switch (e.m_PlayDirection)
            {
                case ItemPlayDirection.Forward: return " ▶";
                case ItemPlayDirection.Backward: return " ◀";
                case ItemPlayDirection.Opposite: return " ⇄";
                default: return "";
            }
        }
    }
}
