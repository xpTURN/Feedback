using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace xpTURN.Feedback.Tests.Editor
{
    // FeedbackUITrigger: the rows of an event and only theirs, the five actions, the left button, Require Interactable (the
    // events that begin something), the selection's source, the row's player or the object's, no allocation on its path, and
    // the hover's backward play in place. The handlers are called as uGUI calls them — the routing is the play-mode test's
    public sealed class UITriggerTests
    {
        static readonly List<string> s_Warnings = new List<string>();
        static readonly List<string> s_Played = new List<string>();
        readonly List<Object> m_Made = new List<Object>();
        readonly List<FeedbackPlayer> m_Players = new List<FeedbackPlayer>();
        readonly List<FeedbackScheduler> m_Schedulers = new List<FeedbackScheduler>();

        static void OnLog(string condition, string stack, LogType type)
        {
            if (type == LogType.Warning)
                s_Warnings.Add(condition);
        }

        static void OnPlayed(FeedbackPlayer player, PlaybackHandle handle) => s_Played.Add(player.name);

        [SetUp]
        public void SetUp()
        {
            s_Warnings.Clear();
            s_Played.Clear();
            PlayerTestBinding.Reset();
            PlayerTestBinding.Register();
            PlayerTestBinding.Hook = (b, what, record) =>
            {
                if ((what == "Begin" || what == "Execute") && b.LastBegin.Reverse && PlayerTestBinding.Log.Count > 0)
                    PlayerTestBinding.Log[PlayerTestBinding.Log.Count - 1] += "~";
            };
            Application.logMessageReceived += OnLog;
        }

        [TearDown]
        public void TearDown()
        {
            Application.logMessageReceived -= OnLog;
            foreach (FeedbackPlayer player in m_Players)
            {
                if (player != null)
                    player.ReleaseRuntime();
            }
            foreach (Object o in m_Made)
            {
                if (o != null)
                    Object.DestroyImmediate(o);
            }
            foreach (FeedbackScheduler s in m_Schedulers)
            {
                if (!s.IsShutdown)
                    s.Shutdown();
            }
            m_Players.Clear();
            m_Made.Clear();
            m_Schedulers.Clear();
            PlayerTestBinding.Reset();
        }

        FeedbackScheduler Sched()
        {
            var s = new FeedbackScheduler(ContextKind.Test);
            m_Schedulers.Add(s);
            return s;
        }

        GameObject Go(string name)
        {
            var go = new GameObject(name);
            m_Made.Add(go);
            return go;
        }

        FeedbackPlayer PlayerOn(GameObject go, FeedbackScheduler s, params FeedbackItem[] items)
        {
            var player = go.AddComponent<FeedbackPlayer>();
            m_Players.Add(player);
            player.m_Items.AddRange(items);
            player.Bind(s);
            player.Played += OnPlayed;
            return player;
        }

        static PlayerTestAction Act(string label, float length) => new PlayerTestAction { m_Label = label, m_Length = length };

        static UITriggerEntry Row(UITriggerEvent kind, UITriggerAction action, FeedbackPlayer player = null, StopMode mode = StopMode.Hold, UISelectSource source = UISelectSource.Any)
            => new UITriggerEntry { m_Event = kind, m_Action = action, m_Player = player, m_StopMode = mode, m_Source = source };

        static FeedbackUITrigger Trigger(GameObject go, params UITriggerEntry[] rows)
        {
            var trigger = go.AddComponent<FeedbackUITrigger>();
            trigger.m_Entries.AddRange(rows);
            return trigger;
        }

        static PointerEventData Pointer(PointerEventData.InputButton button = PointerEventData.InputButton.Left) => new PointerEventData(null) { button = button };

        // the handler uGUI would call for the event
        static void Send(FeedbackUITrigger t, UITriggerEvent kind, BaseEventData data = null)
        {
            switch (kind)
            {
                case UITriggerEvent.PointerEnter: ((IPointerEnterHandler)t).OnPointerEnter(data as PointerEventData ?? Pointer()); break;
                case UITriggerEvent.PointerExit: ((IPointerExitHandler)t).OnPointerExit(data as PointerEventData ?? Pointer()); break;
                case UITriggerEvent.PointerDown: ((IPointerDownHandler)t).OnPointerDown(data as PointerEventData ?? Pointer()); break;
                case UITriggerEvent.PointerUp: ((IPointerUpHandler)t).OnPointerUp(data as PointerEventData ?? Pointer()); break;
                case UITriggerEvent.PointerClick: ((IPointerClickHandler)t).OnPointerClick(data as PointerEventData ?? Pointer()); break;
                case UITriggerEvent.Select: ((ISelectHandler)t).OnSelect(data ?? new BaseEventData(null)); break;
                case UITriggerEvent.Deselect: ((IDeselectHandler)t).OnDeselect(data ?? new BaseEventData(null)); break;
                case UITriggerEvent.Submit: ((ISubmitHandler)t).OnSubmit(data ?? new BaseEventData(null)); break;
                case UITriggerEvent.Cancel: ((ICancelHandler)t).OnCancel(data ?? new BaseEventData(null)); break;
            }
        }

        static string TakeLog()
        {
            string text = string.Join(" ", PlayerTestBinding.Log);
            PlayerTestBinding.Log.Clear();
            return text;
        }

        static string TakePlayed()
        {
            string text = string.Join(" ", s_Played);
            s_Played.Clear();
            return text;
        }

        static readonly UITriggerEvent[] AllEvents = (UITriggerEvent[])Enum.GetValues(typeof(UITriggerEvent));

        // each event runs its rows and no other; the five actions; two rows on one event in list order; an event without rows
        [Test]
        public void UT01_TheRowsOfAnEventRun()
        {
            FeedbackScheduler s = Sched();
            GameObject go = Go("button");
            FeedbackPlayer own = PlayerOn(go, s, Act("a", 1f));
            FeedbackPlayer other = PlayerOn(Go("other"), s, Act("b", 1f));
            var waitItem = new WaitForSignalItem { m_Timeout = 5f };
            PlayerTestAction after = Act("after", 0.1f);
            after.m_Timing.m_Rule = StartRule.AfterPrevious;
            FeedbackPlayer waiting = PlayerOn(Go("waiting"), s, waitItem, after);
            FeedbackUITrigger t = Trigger(go,
                Row(UITriggerEvent.PointerEnter, UITriggerAction.Play),
                Row(UITriggerEvent.PointerExit, UITriggerAction.Stop, null, StopMode.Revert),
                Row(UITriggerEvent.PointerDown, UITriggerAction.PlayForward, other),
                Row(UITriggerEvent.PointerUp, UITriggerAction.PlayBackward, other),
                Row(UITriggerEvent.PointerClick, UITriggerAction.Signal, waiting),
                Row(UITriggerEvent.Submit, UITriggerAction.Play, other),
                Row(UITriggerEvent.Submit, UITriggerAction.Play));
            var steps = new List<string>();
            Send(t, UITriggerEvent.PointerEnter);
            steps.Add("enter[" + TakePlayed() + "|" + TakeLog() + "]");
            Send(t, UITriggerEvent.PointerExit);
            steps.Add("exit[" + TakePlayed() + "|" + TakeLog() + "]");
            Send(t, UITriggerEvent.PointerDown);
            steps.Add("down[" + TakePlayed() + "|" + TakeLog() + "]");
            s.Tick(0.1f);
            TakeLog();
            Send(t, UITriggerEvent.PointerUp);
            steps.Add("up[" + TakePlayed() + "|" + TakeLog() + "]");
            other.Stop();
            waiting.Play();
            TakePlayed();
            TakeLog();
            Send(t, UITriggerEvent.PointerClick);
            s.Tick(0.05f);
            steps.Add("click[" + TakePlayed() + "|" + TakeLog() + "]");
            Send(t, UITriggerEvent.Submit);
            steps.Add("submit[" + TakePlayed() + "|" + TakeLog() + "]");
            own.Stop();
            other.Stop();
            TakeLog();
            foreach (UITriggerEvent kind in new[] { UITriggerEvent.Select, UITriggerEvent.Deselect, UITriggerEvent.Cancel })
                Send(t, kind);
            steps.Add("none[" + TakePlayed() + "|" + TakeLog() + "]");
            Assert.That(steps, Is.EqualTo(new[]
            {
                "enter[button|a.Begin#0 a.Evaluate(0)]",
                "exit[|a.End:Revert a.Settle]",
                "down[other|b.Begin#0 b.Evaluate(0)]",
                "up[|b.Evaluate(0.1)]",
                "click[|after.Begin#0 after.Evaluate(0.5)]",
                "submit[other button|b.Begin#0 b.Evaluate(0) a.Begin#0 a.Evaluate(0)]",
                "none[|]",
            }));
        }

        // Down, Up and Click answer the left button only
        [Test]
        public void UT02_TheLeftButtonOnly()
        {
            FeedbackScheduler s = Sched();
            GameObject go = Go("button");
            PlayerOn(go, s, Act("a", 1f)).m_Policy = PlayPolicy.Overlap;
            FeedbackUITrigger t = Trigger(go,
                Row(UITriggerEvent.PointerDown, UITriggerAction.Play),
                Row(UITriggerEvent.PointerUp, UITriggerAction.Play),
                Row(UITriggerEvent.PointerClick, UITriggerAction.Play));
            var counts = new List<int>();
            foreach (PointerEventData.InputButton button in new[] { PointerEventData.InputButton.Right, PointerEventData.InputButton.Middle, PointerEventData.InputButton.Left })
            {
                foreach (UITriggerEvent kind in new[] { UITriggerEvent.PointerDown, UITriggerEvent.PointerUp, UITriggerEvent.PointerClick })
                    Send(t, kind, Pointer(button));
                counts.Add(s_Played.Count);
                s_Played.Clear();
            }
            Assert.That(counts, Is.EqualTo(new[] { 0, 0, 3 }), "right · middle · left");
        }

        // one player per event: the events whose rows ran, sent in enum order
        string Ran(FeedbackUITrigger t)
        {
            foreach (UITriggerEvent kind in AllEvents)
                Send(t, kind);
            return TakePlayed();
        }

        FeedbackUITrigger PerEvent(GameObject go, FeedbackScheduler s)
        {
            var rows = new List<UITriggerEntry>();
            foreach (UITriggerEvent kind in AllEvents)
                rows.Add(Row(kind, UITriggerAction.Play, PlayerOn(Go(kind.ToString()), s, Act(kind.ToString(), 1f))));
            return Trigger(go, rows.ToArray());
        }

        // a Selectable that is not interactable — its own flag or a parent group's — mutes the rows of the events that begin
        // something; Exit, Up and Deselect run. The setting off, or no Selectable: every row runs
        [Test]
        public void UT03_RequireInteractable()
        {
            FeedbackScheduler s = Sched();
            const string all = "PointerEnter PointerExit PointerDown PointerUp PointerClick Select Deselect Submit Cancel";
            const string ending = "PointerExit PointerUp Deselect";

            GameObject plain = Go("plain");
            string noSelectable = Ran(PerEvent(plain, s));

            GameObject flagged = Go("flagged");
            var button = flagged.AddComponent<Button>();
            FeedbackUITrigger onFlagged = PerEvent(flagged, s);
            string interactable = Ran(onFlagged);
            button.interactable = false;
            string notInteractable = Ran(onFlagged);
            onFlagged.m_RequireInteractable = false;
            string settingOff = Ran(onFlagged);

            GameObject group = Go("group");
            CanvasGroup canvasGroup = group.AddComponent<CanvasGroup>();
            GameObject grouped = Go("grouped");
            grouped.transform.SetParent(group.transform, false);
            var groupedButton = grouped.AddComponent<Button>();
            FeedbackUITrigger onGrouped = PerEvent(grouped, s);
            canvasGroup.interactable = false;
            string groupOff = Ran(onGrouped);

            Assert.That(new[] { noSelectable, interactable, notInteractable, settingOff }, Is.EqualTo(new[] { all, all, ending, all }), "no Selectable · interactable · not · the setting off");
            Assert.That((groupedButton.IsInteractable(), groupOff), Is.EqualTo((false, ending)), "a parent group not interactable");
        }

        // Select and Deselect rows by source: a pointer's (PointerEventData), the navigation's (AxisEventData), another
        // (BaseEventData); Any takes the three
        [Test]
        public void UT04_TheSelectionSource()
        {
            FeedbackScheduler s = Sched();
            GameObject go = Go("button");
            var rows = new List<UITriggerEntry>();
            foreach (UITriggerEvent kind in new[] { UITriggerEvent.Select, UITriggerEvent.Deselect })
                foreach (UISelectSource source in (UISelectSource[])Enum.GetValues(typeof(UISelectSource)))
                    rows.Add(Row(kind, UITriggerAction.Play, PlayerOn(Go(kind + "-" + source), s, Act(kind + "-" + source, 1f)), StopMode.Hold, source));
            FeedbackUITrigger t = Trigger(go, rows.ToArray());
            var got = new List<string>();
            foreach (UITriggerEvent kind in new[] { UITriggerEvent.Select, UITriggerEvent.Deselect })
            {
                foreach (BaseEventData data in new BaseEventData[] { new PointerEventData(null), new AxisEventData(null), new BaseEventData(null) })
                {
                    Send(t, kind, data);
                    got.Add(TakePlayed());
                }
            }
            Assert.That(got, Is.EqualTo(new[]
            {
                "Select-Any Select-Pointer", "Select-Any Select-Navigation", "Select-Any Select-Other",
                "Deselect-Any Deselect-Pointer", "Deselect-Any Deselect-Navigation", "Deselect-Any Deselect-Other",
            }));
        }

        // an empty player cell is the player on the trigger's object — looked up while there is none; without one the row does
        // nothing and warns once for the trigger
        [Test]
        public void UT05_TheRowsPlayer()
        {
            FeedbackScheduler s = Sched();
            GameObject go = Go("bare");
            FeedbackUITrigger t = Trigger(go, Row(UITriggerEvent.PointerEnter, UITriggerAction.Play));
            Send(t, UITriggerEvent.PointerEnter);
            Send(t, UITriggerEvent.PointerEnter);
            string without = TakePlayed();
            int warned = s_Warnings.Count(w => w == FeedbackUITrigger.NoPlayerWarning);
            PlayerOn(go, s, Act("a", 1f));
            Send(t, UITriggerEvent.PointerEnter);
            Assert.That((without, warned), Is.EqualTo(("", 1)), "nothing, one warning");
            Assert.That(TakePlayed(), Is.EqualTo("bare"), "a player added later is found");
        }

        static FeedbackUITrigger s_Trigger;
        static PointerEventData s_Pointer;
        static AxisEventData s_Axis;
        static object s_Sink;

        // three events through the static cells — the delegate captures nothing
        static void Events()
        {
            ((IPointerEnterHandler)s_Trigger).OnPointerEnter(s_Pointer);
            ((IPointerExitHandler)s_Trigger).OnPointerExit(s_Pointer);
            ((ISelectHandler)s_Trigger).OnSelect(s_Axis);
        }

        // true only when five runs in a row allocate — 6000.3 counts unrelated allocations now and then
        static bool Allocates(TestDelegate run)
        {
            for (int i = 0; i < 5; i++)
            {
                if (!UnityEngine.TestTools.Constraints.Is.AllocatingGCMemory().ApplyTo(run).IsSuccess)
                    return false;
            }
            return true;
        }

        // the trigger's path allocates nothing once warm: a Stop row on an idle player, a Play row Ignore refuses, an event
        // without rows (the positive control allocates)
        [Test]
        public void UT06_NoAllocation()
        {
            FeedbackScheduler s = Sched();
            GameObject go = Go("button");
            FeedbackPlayer idle = PlayerOn(Go("idle"), s, Act("i", 1f));
            FeedbackPlayer busy = PlayerOn(Go("busy"), s, Act("b", 10f));
            busy.m_Policy = PlayPolicy.Ignore;
            busy.Play();
            idle.EnsureRuntime();
            s_Trigger = Trigger(go,
                Row(UITriggerEvent.PointerEnter, UITriggerAction.Stop, idle),
                Row(UITriggerEvent.PointerExit, UITriggerAction.Play, busy),
                Row(UITriggerEvent.Submit, UITriggerAction.Play, busy));
            s_Pointer = Pointer();
            s_Axis = new AxisEventData(null);
            PlayerTestBinding.Logging = false;
            try
            {
                Events();
                Assert.That(Allocates(Events), Is.False, "the trigger's path");
                Assert.That(Allocates(() => s_Sink = new object[8]), Is.True, "the positive control");
                Assert.That(busy.IsPlaying, Is.True, "the refused plays left the busy play");
            }
            finally
            {
                s_Trigger = null;
                s_Pointer = null;
                s_Axis = null;
            }
        }

        static string V(GameObject g)
        {
            Vector3 p = g.transform.localPosition;
            return "(" + p.x.ToString("0.###", CultureInfo.InvariantCulture) + "," + p.y.ToString("0.###", CultureInfo.InvariantCulture) + "," + p.z.ToString("0.###", CultureInfo.InvariantCulture) + ")";
        }

        // the hover rows: Enter plays forward, Exit turns the play back where it is — no move at the Exit, back over the time
        // played; an Enter during the backward play turns it forward where it is
        [Test]
        public void UT07_TheHoverTurnsInPlace()
        {
            FeedbackScheduler s = Sched();
            GameObject target = Go("target");
            target.transform.localPosition = new Vector3(1f, 0f, 0f);
            var tween = new PositionItem
            {
                m_Motion = new TweenMotion { m_Params = new TweenParams { m_Mode = TweenMode.To, m_To = new Vector3(0f, 8f, 0f), m_Ease = Ease.Of(EaseType.Linear), m_Loops = 1 } },
                m_Target = new TargetRef { m_Mode = TargetRefMode.Direct, m_Object = target.transform, m_Path = "", m_Anchor = "" },
            };
            tween.m_Timing.m_Duration = 0.4f;
            GameObject go = Go("button");
            PlayerOn(go, s, tween);
            FeedbackUITrigger t = Trigger(go,
                Row(UITriggerEvent.PointerEnter, UITriggerAction.PlayForward),
                Row(UITriggerEvent.PointerExit, UITriggerAction.PlayBackward));
            var seq = new List<string>();
            Send(t, UITriggerEvent.PointerEnter);
            s.Tick(0.1f);
            s.Tick(0.1f);
            seq.Add(V(target));
            Send(t, UITriggerEvent.PointerExit);
            seq.Add(V(target));
            s.Tick(0.1f);
            seq.Add(V(target));
            Send(t, UITriggerEvent.PointerEnter);
            seq.Add(V(target));
            s.Tick(0.1f);
            seq.Add(V(target));
            Assert.That(seq, Is.EqualTo(new[] { "(0.5,4,0)", "(0.5,4,0)", "(0.75,2,0)", "(0.75,2,0)", "(0.5,4,0)" }));
        }
    }
}
