using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Overlays;
using UnityEngine;
using UnityEngine.UIElements;

namespace xpTURN.Feedback.Editor
{
    // the Scene view overlay while a session runs — the subject (pressed, selected), ■, Signal for an open wait and the
    // notices (the two always there last). The lines are kept and their text set when it changed
    [Overlay(typeof(SceneView), "xpTURN.Feedback.Preview", "Feedback Preview")]
    internal sealed class PreviewOverlay : Overlay, ITransientOverlay
    {
        readonly List<PreviewNotice> m_Notices = new List<PreviewNotice>();
        readonly List<Label> m_Lines = new List<Label>();
        internal Button Subject;
        internal Button Stop;
        internal Button Signal;
        internal VisualElement Lines;

        // a track session's controls are the Timeline window's
        public bool visible => PreviewSession.Current != null && !PreviewSession.Current.IsTrack;

        public override VisualElement CreatePanelContent()
        {
            var root = new VisualElement();
            Subject = new Button(() =>
            {
                PreviewSession s = PreviewSession.Current;
                if (s != null && s.Subject != null)
                    Selection.activeObject = s.Subject;
            });
            Stop = new Button(() => PreviewSession.Current?.Stop()) { iconImage = Background.FromTexture2D(EditorGUIUtility.IconContent("StopButton").image as Texture2D) };
            Signal = new Button(() => PreviewSession.Current?.Player?.SignalQuietly()) { text = "Signal" };
            Lines = new VisualElement();
            m_Lines.Clear();
            root.Add(Subject);
            root.Add(Stop);
            root.Add(Signal);
            root.Add(Lines);
            root.schedule.Execute(Refresh).Every(100);
            Refresh();
            return root;
        }

        internal void Refresh()
        {
            PreviewSession s = PreviewSession.Current;
            if (s == null || Subject == null)
                return;
            string name = s.Subject != null ? s.Subject.name : "";
            if (Subject.text != name)
                Subject.text = name;
            Signal.style.display = s.Player != null && s.Player.OpenSignalWaits() > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            PreviewNotices.Collect(s, m_Notices);
            // the two always there go last
            int line = 0;
            for (int pass = 0; pass < 2; pass++)
            {
                foreach (PreviewNotice n in m_Notices)
                {
                    bool always = n.Kind == PreviewNoticeKind.Locked || n.Kind == PreviewNoticeKind.ExecuteAlways;
                    if (always != (pass == 1))
                        continue;
                    if (line == m_Lines.Count)
                    {
                        var label = new Label { style = { whiteSpace = WhiteSpace.Normal } };
                        m_Lines.Add(label);
                        Lines.Add(label);
                    }
                    Label l = m_Lines[line++];
                    if (l.text != n.Text)
                        l.text = n.Text;
                    l.style.opacity = always ? 0.6f : 1f;
                    l.style.display = DisplayStyle.Flex;
                }
            }
            for (int i = line; i < m_Lines.Count; i++)
                m_Lines[i].style.display = DisplayStyle.None;
        }

        internal int ShownLines
        {
            get
            {
                int n = 0;
                foreach (Label l in m_Lines)
                {
                    if (l.style.display != DisplayStyle.None)
                        n++;
                }
                return n;
            }
        }

        internal string LineText(int i) => m_Lines[i].text;
    }
}
