using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor.Events;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace xpTURN.Feedback.Tests.Editor
{
    // PlayFromEvent, the play a UnityEvent can pick: the inspector's list, persistent calls in edit and play mode, Play's
    // refusals. Handlers are static — a play-mode iterator keeps no captured local across the reload
    public sealed class PlayFromEventTests
    {
        static int s_Played;
        static readonly List<string> s_Warnings = new List<string>();

        readonly List<Object> m_Made = new List<Object>();
        readonly List<FeedbackScheduler> m_Schedulers = new List<FeedbackScheduler>();

        static void OnPlayed(FeedbackPlayer player, PlaybackHandle handle) => s_Played++;

        static void OnLog(string condition, string stack, LogType type)
        {
            if (type == LogType.Warning)
                s_Warnings.Add(condition);
        }

        [SetUp]
        public void SetUp()
        {
            s_Played = 0;
            s_Warnings.Clear();
            Application.logMessageReceived += OnLog;
        }

        [TearDown]
        public void TearDown()
        {
            Application.logMessageReceived -= OnLog;
            foreach (Object o in m_Made)
            {
                if (o != null)
                {
                    if (o is GameObject go && go.TryGetComponent(out FeedbackPlayer p))
                        p.ReleaseRuntime();
                    Object.DestroyImmediate(o);
                }
            }
            foreach (FeedbackScheduler s in m_Schedulers)
            {
                if (!s.IsShutdown)
                    s.Shutdown();
            }
            m_Made.Clear();
            m_Schedulers.Clear();
        }

        // a one-second list, bound to s when it is given
        FeedbackPlayer Player(FeedbackScheduler s)
        {
            var go = new GameObject("PlayFromEvent");
            m_Made.Add(go);
            var player = go.AddComponent<FeedbackPlayer>();
            player.m_Items.Add(new WaitItem());
            if (s != null)
            {
                player.Bind(s);
                player.EnsureRuntime();
            }
            player.Played += OnPlayed;
            return player;
        }

        FeedbackScheduler Scheduler()
        {
            var s = new FeedbackScheduler(ContextKind.Test);
            m_Schedulers.Add(s);
            return s;
        }

        // the methods of the player the UnityEvent inspector offers for these argument types
        static string[] Listed(Object target, Type[] args)
        {
            MethodInfo map = typeof(UnityEditorInternal.UnityEventDrawer).GetMethod("CalculateMethodMap", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(map, Is.Not.Null, "the inspector's method map is there to read");
            return ((IEnumerable)map.Invoke(null, new object[] { target, args, false })).Cast<object>()
                .Select(o => (MethodInfo)o.GetType().GetField("methodInfo").GetValue(o))
                .Where(m => m.DeclaringType == typeof(FeedbackPlayer))
                .Select(m => m.ReturnType.Name + " " + m.Name + "(" + string.Join(",", m.GetParameters().Select(p => p.ParameterType.Name)) + ")")
                .ToArray();
        }

        [Test]
        public void PFE01_TheInspectorListsIt()
        {
            FeedbackPlayer player = Player(null);
            Assert.That(Listed(player, Type.EmptyTypes), Is.EquivalentTo(new[] { "Void PlayFromEvent()", "Void Pause()", "Void Resume()", "Void Restore()", "Void Signal()" }));
            Assert.That(Listed(player, new[] { typeof(float) }), Is.EqualTo(new[] { "Void PlayFromEvent(Single)" }), "the intensity version");
            Assert.That(Listed(player, new[] { typeof(bool) }), Is.EqualTo(new[] { "Void PlayFromEvent(Boolean)" }), "the direction version");
            Assert.That(Listed(player, new[] { typeof(int) }).Concat(Listed(player, new[] { typeof(string) })), Is.Empty, "no other argument version");
        }

        [Test]
        public void PFE02_APersistentCallPlays()
        {
            FeedbackPlayer player = Player(Scheduler());
            var call = new UnityEvent();
            UnityEventTools.AddVoidPersistentListener(call, player.PlayFromEvent);
            Assert.That(call.GetPersistentListenerState(0), Is.EqualTo(UnityEventCallState.RuntimeOnly));
            call.Invoke();
            Assert.That(s_Played, Is.EqualTo(0), "Runtime Only — no call in edit mode");
            call.SetPersistentListenerState(0, UnityEventCallState.EditorAndRuntime);
            call.Invoke();
            Assert.That((s_Played, player.IsPlaying), Is.EqualTo((1, true)));
            player.Stop(StopMode.Revert);
            player.m_Policy = PlayPolicy.Ignore;
            player.Rebuild();
            s_Played = 0;
            call.Invoke();
            call.Invoke();
            Assert.That(s_Played, Is.EqualTo(1), "Ignore refuses the second as it refuses a second Play");
        }

        [Test]
        public void PFE03_EditModeWithoutAPreviewWarnsOnce()
        {
            FeedbackPlayer player = Player(null);
            player.PlayFromEvent();
            player.PlayFromEvent();
            Assert.That(s_Warnings, Is.EqualTo(new[] { FeedbackPlayer.EditModeWarning }));
            Assert.That((s_Played, player.IsPlaying), Is.EqualTo((0, false)));
        }

        [UnityTest]
        public IEnumerator PFE04_APlayModeCallPlays()
        {
            yield return new EnterPlayMode();
            var go = new GameObject("PlayFromEvent");
            var player = go.AddComponent<FeedbackPlayer>();
            player.m_Items.Add(new WaitItem());
            player.Played += OnPlayed;
            var call = new UnityEvent();
            UnityEventTools.AddVoidPersistentListener(call, player.PlayFromEvent);
            s_Played = 0;
            call.Invoke();
            yield return null;
            (int played, bool playing) = (s_Played, player.IsPlaying);
            Object.Destroy(go);
            yield return new ExitPlayMode();
            Assert.That((played, playing), Is.EqualTo((1, true)), "a Runtime Only call plays on the default scheduler");
        }

        // a player of these items bound to s
        FeedbackPlayer PlayerOf(FeedbackScheduler s, params FeedbackItem[] items)
        {
            var go = new GameObject("PlayFromEvent");
            m_Made.Add(go);
            var player = go.AddComponent<FeedbackPlayer>();
            player.m_Items.AddRange(items);
            player.Bind(s);
            player.Played += OnPlayed;
            return player;
        }

        // the test action's binding: the last Begin of the newest binding
        static BeginContext LastBegin() => PlayerTestBinding.Made[PlayerTestBinding.Made.Count - 1].LastBegin;

        static readonly Vector3 Origin = new Vector3(1f, 0f, 0f);
        static readonly Vector3 Goal = new Vector3(0f, 8f, 0f);

        // a 0.4 s To on t's position
        static PositionItem To(GameObject t)
        {
            var item = new PositionItem
            {
                m_Motion = new TweenMotion { m_Params = new TweenParams { m_Mode = TweenMode.To, m_From = Goal, m_To = Goal, m_Ease = Ease.Of(EaseType.Linear), m_Loops = 1 } },
                m_Target = new TargetRef { m_Mode = TargetRefMode.Direct, m_Object = t.transform, m_Path = "", m_Anchor = "" },
            };
            item.m_Timing.m_Duration = 0.4f;
            return item;
        }

        static bool Near(Vector3 a, Vector3 b) => (a - b).magnitude < 1e-4f;

        // a UnityEvent<float>'s dynamic call plays with its value as the intensity, the remap on it; a negative or NaN value is
        // Play's refusal — one warning for the player and cause
        [Test]
        public void PFE05_TheIntensityVersion()
        {
            PlayerTestBinding.Reset();
            PlayerTestBinding.Register();
            try
            {
                FeedbackPlayer player = PlayerOf(Scheduler(), new PlayerTestAction { m_Label = "a", m_Length = 1f });
                var call = new UnityEvent<float>();
                UnityEventTools.AddPersistentListener(call, player.PlayFromEvent);
                call.SetPersistentListenerState(0, UnityEventCallState.EditorAndRuntime);
                call.Invoke(0.5f);
                float plain = LastBegin().Intensity;
                player.Stop();
                player.m_RemapIntensity = true;
                player.m_IntensityRemap = AnimationCurve.Linear(0f, 0f, 1f, 2f);
                call.Invoke(0.5f);
                float remapped = LastBegin().Intensity;
                player.Stop();
                s_Played = 0;
                s_Warnings.Clear();
                call.Invoke(-1f);
                call.Invoke(float.NaN);
                Assert.That((plain, remapped), Is.EqualTo((0.5f, 1f)), "the value as the intensity, then remapped");
                Assert.That((s_Played, player.IsPlaying), Is.EqualTo((0, false)), "a negative or NaN value plays nothing");
                Assert.That(s_Warnings.Count(w => w.StartsWith(FeedbackPlayer.ArgumentWarning + "intensity", StringComparison.Ordinal)), Is.EqualTo(1), "one warning");
            }
            finally
            {
                PlayerTestBinding.Reset();
            }
        }

        // a UnityEvent<bool>'s dynamic call: true plays forward, false backward; a list that cannot play backward refuses false
        // with the backward warning
        [Test]
        public void PFE06_TheDirectionVersion()
        {
            PlayerTestBinding.Reset();
            PlayerTestBinding.Register();
            try
            {
                FeedbackScheduler s = Scheduler();
                FeedbackPlayer player = PlayerOf(s, new PlayerTestAction { m_Label = "a", m_Length = 0.2f });
                var call = new UnityEvent<bool>();
                UnityEventTools.AddPersistentListener(call, player.PlayFromEvent);
                call.SetPersistentListenerState(0, UnityEventCallState.EditorAndRuntime);
                call.Invoke(true);
                bool forward = !LastBegin().Reverse;
                player.Stop();
                call.Invoke(false);
                bool backward = LastBegin().Reverse;
                player.Stop();
                FeedbackPlayer open = PlayerOf(s, new WaitForSignalItem());
                var openCall = new UnityEvent<bool>();
                UnityEventTools.AddPersistentListener(openCall, open.PlayFromEvent);
                openCall.SetPersistentListenerState(0, UnityEventCallState.EditorAndRuntime);
                s_Warnings.Clear();
                openCall.Invoke(false);
                Assert.That((forward, backward), Is.EqualTo((true, true)), "true forward, false backward");
                Assert.That(open.IsPlaying, Is.False, "an open wait cannot play backward");
                Assert.That(s_Warnings, Is.EqualTo(new[] { FeedbackPlayer.ReverseWarning }));
            }
            finally
            {
                PlayerTestBinding.Reset();
            }
        }

        // a value against the running play turns it where it is — false during a forward play, true during the backward play
        // it made: no move in the call, one play (one Played), back over the time played; an idle player's false plays from
        // the list's end
        [Test]
        public void PFE07_AValueAgainstThePlayTurnsIt()
        {
            FeedbackScheduler s = Scheduler();
            var t = new GameObject("t");
            m_Made.Add(t);
            t.transform.localPosition = Origin;
            FeedbackPlayer player = PlayerOf(s, To(t));
            player.PlayFromEvent(true);
            s.Tick(0.1f);
            s.Tick(0.1f);
            Vector3 cut = t.transform.localPosition;
            player.PlayFromEvent(false);
            Vector3 after = t.transform.localPosition;
            s.Tick(0.1f);
            Vector3 middle = t.transform.localPosition;
            player.PlayFromEvent(true);
            Vector3 again = t.transform.localPosition;
            s.Tick(0.1f);
            Vector3 forward = t.transform.localPosition;
            player.PlayFromEvent(false);
            s.Tick(0.1f);
            s.Tick(0.1f);
            s.Tick(0.1f);
            Assert.That(Near(cut, new Vector3(0.5f, 4f, 0f)) && Near(after, cut), Is.True, "turned back in place: " + cut + " → " + after);
            Assert.That(Near(middle, new Vector3(0.75f, 2f, 0f)) && Near(again, middle) && Near(forward, new Vector3(0.5f, 4f, 0f)), Is.True,
                "turned forward in place: " + middle + " → " + again + " → " + forward);
            Assert.That(Near(t.transform.localPosition, Origin) && !player.IsPlaying && s_Played == 1, Is.True,
                "back over the time played, one play: " + t.transform.localPosition + " · playing " + player.IsPlaying + " · played " + s_Played);
            var idle = new GameObject("idle");
            m_Made.Add(idle);
            idle.transform.localPosition = Origin;
            FeedbackPlayer fresh = PlayerOf(s, To(idle));
            fresh.PlayFromEvent(false);
            Assert.That(Near(idle.transform.localPosition, Goal), Is.True, "an idle player's false starts at the end: " + idle.transform.localPosition);
        }
    }
}
