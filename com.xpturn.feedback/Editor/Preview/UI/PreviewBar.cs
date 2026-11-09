using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace xpTURN.Feedback.Editor
{
    // the bar over a player's or a driver's inspector — the buttons, the scrub, the arguments, the settings and the status
    // line; in play mode the player's real play. It holds no state of a session: each 0.1 s, while in a panel, it reads its
    // subject's session (a bar built again shows the session's arguments). The rows are chosen by the mode when it is built —
    // the inspector builds it again for play mode — and the refresh checks the mode too
    internal sealed class PreviewBar : VisualElement
    {
        internal const string UnconditionalKey = "xpTURN.Feedback.Preview.Unconditional";
        internal const string RepeatKey = "xpTURN.Feedback.Preview.Repeat";
        internal const string SpeedKey = "xpTURN.Feedback.Preview.Speed";

        readonly Component m_Subject;
        readonly FeedbackPlayer m_Player;
        readonly MotionDriver m_MotionDriver;
        readonly List<PreviewNotice> m_Notices = new List<PreviewNotice>();
        internal readonly VisualElement EditRows;
        internal readonly VisualElement PlayRows;
        internal readonly VisualElement ArgsRows;
        internal readonly Button PlayButton;
        internal readonly Button PauseButton;
        internal readonly Button StopButton;
        internal readonly Button SeedButton;
        internal readonly Slider Scrub;
        internal readonly Label Place;
        internal readonly Slider SpeedField;
        internal readonly FloatField IntensityField;
        internal readonly DropdownField DirectionField;
        internal readonly Toggle Unconditional;
        internal readonly Toggle RepeatToggle;
        internal readonly Toggle TuningToggle;
        internal readonly Toggle DeselectToggle;
        internal readonly Label Status;
        internal readonly HelpBox Notice;
        internal readonly Button PlayModePlay;
        internal readonly Button PlayModePause;
        internal readonly Button PlayModeStop;
        IVisualElementScheduledItem m_Refresh;
        PreviewRefusal m_Refusal;
        bool m_Refreshing;
        bool m_Playing;

        // observation
        internal static int Refreshes;

        static readonly List<string> s_Directions = new List<string> { "Player's", "Forward", "Backward" };

        internal PreviewBar(Component subject)
        {
            m_Subject = subject;
            m_Player = subject as FeedbackPlayer;
            m_MotionDriver = subject as MotionDriver;
            InspectorStyles.Attach(this);
            EditRows = new VisualElement();
            PlayRows = new VisualElement();
            ArgsRows = new VisualElement();
            Add(EditRows);
            Add(PlayRows);
            Add(ArgsRows);

            var buttons = Row();
            PlayButton = new Button(OnPlay) { iconImage = Background.FromTexture2D(EditorGUIUtility.IconContent("PlayButton").image as Texture2D) };
            PauseButton = new Button(OnPause) { iconImage = Background.FromTexture2D(EditorGUIUtility.IconContent("Pause").image as Texture2D) };
            StopButton = new Button(OnStop) { iconImage = Background.FromTexture2D(EditorGUIUtility.IconContent("StopButton").image as Texture2D) };
            SeedButton = new Button(() => Mine?.NewSeed()) { text = "New Seed" };
            buttons.Add(PlayButton);
            if (m_Player != null)
                buttons.Add(PauseButton);
            buttons.Add(StopButton);
            if (m_Player != null)
                buttons.Add(SeedButton);
            EditRows.Add(buttons);

            Scrub = new Slider(0f, 1f);
            Scrub.RegisterValueChangedCallback(e =>
            {
                if (!m_Refreshing && Mine != null)
                {
                    Mine.Scrub(e.newValue);
                    Refresh();
                }
            });
            Place = new Label();
            if (m_Player != null)
            {
                var scrub = Row();
                Scrub.AddToClassList(InspectorStyles.Grow);
                scrub.Add(Scrub);
                scrub.Add(Place);
                EditRows.Add(scrub);
            }

            SpeedField = new Slider("Speed", 0.1f, 2f) { value = Mathf.Clamp(EditorPrefs.GetFloat(SpeedKey, 1f), 0.1f, 2f) };
            SpeedField.RegisterValueChangedCallback(e =>
            {
                EditorPrefs.SetFloat(SpeedKey, e.newValue);
                if (Mine != null)
                    Mine.Speed = e.newValue;
            });
            IntensityField = new FloatField("Intensity") { value = 1f };
            IntensityField.RegisterValueChangedCallback(e =>
            {
                if (e.newValue < 0f)
                    IntensityField.SetValueWithoutNotify(0f);
                if (!m_Refreshing && Mine != null)
                    Mine.Args = Compose();
            });
            DirectionField = new DropdownField("Direction", s_Directions, 0);
            DirectionField.RegisterValueChangedCallback(e =>
            {
                if (!m_Refreshing && Mine != null)
                    Mine.Args = Compose();
            });
            EditRows.Add(SpeedField);
            if (m_Player != null)
            {
                ArgsRows.Add(IntensityField);
                ArgsRows.Add(DirectionField);
            }

            Unconditional = new Toggle("Unconditional") { value = EditorPrefs.GetBool(UnconditionalKey, true) };
            Unconditional.RegisterValueChangedCallback(e =>
            {
                EditorPrefs.SetBool(UnconditionalKey, e.newValue);
                if (Mine != null)
                    Mine.Args = Compose();
            });
            RepeatToggle = new Toggle("Repeat") { value = EditorPrefs.GetBool(RepeatKey, false) };
            RepeatToggle.RegisterValueChangedCallback(e =>
            {
                EditorPrefs.SetBool(RepeatKey, e.newValue);
                if (Mine != null)
                    Mine.Repeat = e.newValue;
            });
            TuningToggle = new Toggle("Live Tuning") { value = PreviewSession.LiveTuning };
            TuningToggle.RegisterValueChangedCallback(e => PreviewSession.LiveTuning = e.newValue);
            DeselectToggle = new Toggle("End On Deselect") { value = PreviewSession.EndOnDeselect };
            DeselectToggle.RegisterValueChangedCallback(e => PreviewSession.EndOnDeselect = e.newValue);
            if (m_Player != null)
            {
                EditRows.Add(Unconditional);
                EditRows.Add(RepeatToggle);
                EditRows.Add(TuningToggle);
            }
            EditRows.Add(DeselectToggle);

            var play = Row();
            PlayModePlay = new Button(() =>
            {
                if (m_Player != null)
                    m_Player.Play(ComposeRuntime());
            }) { iconImage = Background.FromTexture2D(EditorGUIUtility.IconContent("PlayButton").image as Texture2D) };
            PlayModePause = new Button(() =>
            {
                if (m_Player == null)
                    return;
                if (m_Player.HasPaused)
                    m_Player.Resume();
                else
                    m_Player.Pause();
            }) { iconImage = Background.FromTexture2D(EditorGUIUtility.IconContent("Pause").image as Texture2D) };
            PlayModeStop = new Button(() =>
            {
                if (m_Player != null)
                    m_Player.Stop();
            }) { iconImage = Background.FromTexture2D(EditorGUIUtility.IconContent("StopButton").image as Texture2D) };
            play.Add(PlayModePlay);
            play.Add(PlayModePause);
            play.Add(PlayModeStop);
            if (m_Player != null)
                PlayRows.Add(play);

            var slot = new VisualElement { style = { overflow = Overflow.Hidden } };
            slot.Add(new HelpBox("-\n-", HelpBoxMessageType.Warning) { style = { visibility = Visibility.Hidden } });
            Status = new Label { style = { whiteSpace = WhiteSpace.Normal, position = Position.Absolute, left = 0f, right = 0f, top = 0f } };
            slot.Add(Status);
            Notice = new HelpBox("", HelpBoxMessageType.Warning) { style = { display = DisplayStyle.None, position = Position.Absolute, left = 0f, right = 0f, top = 0f, bottom = 0f, overflow = Overflow.Hidden } };
            slot.Add(Notice);
            Add(slot);
            RegisterCallback<AttachToPanelEvent>(_ => m_Refresh = schedule.Execute(Refresh).Every(100));
            RegisterCallback<DetachFromPanelEvent>(_ => m_Refresh?.Pause());
            m_Playing = !EditorApplication.isPlaying;
            Refresh();
        }

        static VisualElement Row()
        {
            var row = new VisualElement();
            row.AddToClassList(InspectorStyles.Row);
            return row;
        }

        PreviewSession Mine
        {
            get
            {
                PreviewSession s = PreviewSession.Current;
                return s != null && m_Subject != null && s.Subject == m_Subject ? s : null;
            }
        }

        PlayerDirection? Direction
        {
            get
            {
                switch (DirectionField.index)
                {
                    case 1: return PlayerDirection.Forward;
                    case 2: return PlayerDirection.Backward;
                    default: return null;
                }
            }
        }

        internal PlayArgs Compose()
        {
            PlayArgs args = ComposeRuntime();
            return Unconditional.value ? args.WithUnconditional() : args;
        }

        PlayArgs ComposeRuntime()
        {
            PlayArgs args = PlayArgs.Default;
            if (IntensityField.value != 1f)
                args = args.WithIntensity(IntensityField.value);
            PlayerDirection? d = Direction;
            if (d.HasValue)
                args = args.WithDirection(d.Value);
            return args;
        }

        internal void OnPlay()
        {
            if (m_Subject == null)
                return;
            m_Refusal = PreviewRefusal.None;
            PreviewSession s = Mine;
            if (s != null)
                s.PlayButton();
            else
            {
                m_Refusal = m_Player != null ? PreviewSession.Start(m_Player, Compose()) : PreviewSession.Start(m_MotionDriver);
                s = Mine;
                if (s != null)
                {
                    s.Speed = SpeedField.value;
                    if (m_Player != null)
                        s.Repeat = RepeatToggle.value;
                }
            }
            Refresh();
        }

        // an item's ▶: the session started with the bar's arguments when there is none, then that item
        // alone — its delay skipped
        internal void PlayItem(int listIndex)
        {
            if (m_Player == null || EditorApplication.isPlayingOrWillChangePlaymode)
                return;
            if (Mine == null)
                OnPlay();
            Mine?.PlayItem(listIndex, true);
            Refresh();
        }

        internal void OnPause()
        {
            if (Mine == null || m_Player == null)
                return;
            if (m_Player.HasPaused)
                m_Player.Resume();
            else
                m_Player.Pause();
            Refresh();
        }

        internal void OnStop()
        {
            Mine?.Stop();
            Refresh();
        }

        internal void Refresh()
        {
            Refreshes++;
            m_Refreshing = true;
            try
            {
                bool playing = EditorApplication.isPlaying;
                if (playing != m_Playing)
                {
                    m_Playing = playing;
                    EditRows.style.display = playing ? DisplayStyle.None : DisplayStyle.Flex;
                    PlayRows.style.display = playing ? DisplayStyle.Flex : DisplayStyle.None;
                    style.display = playing && m_Player == null ? DisplayStyle.None : DisplayStyle.Flex;
                }
                if (m_Subject == null)
                {
                    Status.text = "";
                    Notice.style.display = DisplayStyle.None;
                    return;
                }
                PreviewSession s = Mine;
                PauseButton.SetEnabled(s != null);
                StopButton.SetEnabled(s != null);
                SeedButton.SetEnabled(s != null);
                string text = "";
                string notice = "";
                if (playing)
                {
                    if (PreviewSession.LastEndSubject == PreviewSession.IdOf(m_Subject))
                        text = PreviewTexts.Of(PreviewSession.LastEnd);
                }
                else if (s != null)
                {
                    PlayArgs args = s.Args;
                    IntensityField.SetValueWithoutNotify(args.Intensity);
                    DirectionField.SetValueWithoutNotify(s_Directions[!args.HasDirection ? 0 : args.Direction == PlayerDirection.Backward ? 2 : 1]);
                    SeekRefusal r = SeekRefusal.NotLive;
                    if (m_Player != null)
                    {
                        r = s.ScrubState(out double low, out double high, out double place);
                        if (r == SeekRefusal.None)
                        {
                            Scrub.lowValue = (float)low;
                            Scrub.highValue = (float)high;
                            Scrub.SetValueWithoutNotify((float)place);
                            Place.text = $"{place:0.00} / {high:0.00}";
                        }
                        else
                            Place.text = "";
                    }
                    Scrub.SetEnabled(r == SeekRefusal.None);
                    if (s.LastReplayRefused)
                        text = PreviewTexts.ReplayRefused;
                    else if (m_Player != null && r != SeekRefusal.None && r != SeekRefusal.NotLive)
                        text = PreviewTexts.Of(r);
                    else
                        notice = GameViewNotice(s);
                }
                else
                {
                    Scrub.SetEnabled(false);
                    Place.text = "";
                    if (m_Refusal != PreviewRefusal.None)
                        text = PreviewTexts.Of(m_Refusal);
                    else if (PreviewSession.LastEndSubject == PreviewSession.IdOf(m_Subject))
                        text = PreviewSession.LastEndRefused ? PreviewTexts.ReplayRefused : PreviewTexts.Of(PreviewSession.LastEnd);
                    if (m_Player != null && PreviewSession.Current != null && PreviewSession.Current.HoldsTrackPlayer(m_Player))
                        notice = PreviewTexts.TrackHeld;
                }
                Status.text = text;
                Status.tooltip = text;
                Notice.text = notice;
                Notice.tooltip = text.Length > 0 ? notice + "\n" + text : notice;
                Notice.style.display = notice.Length > 0 ? DisplayStyle.Flex : DisplayStyle.None;
                Status.style.display = notice.Length > 0 ? DisplayStyle.None : DisplayStyle.Flex;
            }
            finally
            {
                m_Refreshing = false;
            }
        }

        string GameViewNotice(PreviewSession s)
        {
            PreviewNotices.Collect(s, m_Notices);
            foreach (PreviewNotice n in m_Notices)
            {
                if (n.Kind == PreviewNoticeKind.CameraGameView || n.Kind == PreviewNoticeKind.CameraMainScene || n.Kind == PreviewNoticeKind.NoMainCamera
                    || n.Kind == PreviewNoticeKind.ScreenGameView || n.Kind == PreviewNoticeKind.PostFXSceneView || n.Kind == PreviewNoticeKind.PostFXCamera
                    || n.Kind == PreviewNoticeKind.PostFXGameView || n.Kind == PreviewNoticeKind.CinemachineNoExtension || n.Kind == PreviewNoticeKind.CinemachinePlayOnly
                    || n.Kind == PreviewNoticeKind.RumblePlayOnly)
                    return n.Text;
            }
            return "";
        }
    }
}
