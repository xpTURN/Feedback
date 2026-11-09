using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace xpTURN.Feedback.Tests
{
    // the Update stage on the real loop (a speed's offset reaches the frame's animator update · the game's Update reads the base)
    // and a Fixed (Animate Physics) animator — its offsets show with the direct write only, a Tween (Persist) shows anyway. The
    // clip's time is the normalized time × its 10 s; the rates are against an animator with no item
    [PrebuildSetup("xpTURN.Feedback.Tests.Editor.FeedbackTestAssets")]
    [PostBuildCleanup("xpTURN.Feedback.Tests.Editor.FeedbackTestAssets")]
    public sealed class AnimationPlayTests
    {
        readonly List<Object> m_Made = new List<Object>();
        readonly List<FeedbackPlayer> m_Players = new List<FeedbackPlayer>();

        [TearDown]
        public void TearDown()
        {
            foreach (FeedbackPlayer p in m_Players)
                if (p != null) p.ReleaseRuntime();
            foreach (Object o in m_Made)
                if (o != null) Object.Destroy(o);
            m_Made.Clear(); m_Players.Clear();
        }

        Animator Make(string name, AnimatorUpdateMode mode)
        {
            var go = new GameObject(name);
            m_Made.Add(go);
            new GameObject("Child").transform.SetParent(go.transform, false);
            Animator animator = AnimationFixtures.Make(go);
            animator.updateMode = mode;
            return animator;
        }

        FeedbackPlayer Player(Animator target, FeedbackMotion motion, bool direct)
        {
            var go = new GameObject("player");
            m_Made.Add(go);
            var p = go.AddComponent<FeedbackPlayer>();
            var item = new AnimatorSpeedItem { m_Motion = motion, m_DirectWrite = direct };
            item.m_Target = new TargetRef { m_Mode = TargetRefMode.Direct, m_Object = target, m_Path = "", m_Anchor = "" };
            item.m_Timing.m_Duration = 2f;
            p.m_Items.Add(item);
            m_Players.Add(p);
            return p;
        }

        static PunchMotion Punch() => new PunchMotion { m_Params = new PunchParams { m_Amplitude = new Vector4(2f, 0, 0, 0), m_Vibrato = 0.25f, m_Decay = 0f } };

        static TweenMotion TweenTo(float v)
        {
            var m = new TweenMotion();
            m.m_Params.m_Mode = TweenMode.To;
            m.m_Params.m_To = new Vector4(v, 0, 0, 0);
            return m;
        }

        static float T(Animator a) => a.GetCurrentAnimatorStateInfo(0).normalizedTime * 10f;

        // the clips' advance over 0.2 ~ 0.8 s of the plays, each against the reference's
        static IEnumerator Rates(Animator reference, Animator[] animators, float[] into, float[] speedsInUpdate)
        {
            float end = Time.time + 0.2f;
            while (Time.time < end)
                yield return null;
            float r0 = T(reference);
            var t0 = new float[animators.Length];
            for (int i = 0; i < animators.Length; i++)
                t0[i] = T(animators[i]);
            end = Time.time + 0.6f;
            while (Time.time < end)
            {
                yield return null;
                for (int i = 0; i < animators.Length; i++)
                    speedsInUpdate[i] = Mathf.Max(speedsInUpdate[i], animators[i].speed);
            }
            float dr = T(reference) - r0;
            for (int i = 0; i < animators.Length; i++)
                into[i] = (T(animators[i]) - t0[i]) / Mathf.Max(1e-6f, dr);
        }

        // ---- a speed's Punch (an offset) is in the Update stage — the frame's animator update runs it faster · the game's
        // Update reads the base (put back at EarlyUpdate)
        [UnityTest]
        public IEnumerator AQ05a_TheUpdateStageOnTheLoop()
        {
            Assert.That(AnimationFixtures.Controller, Is.Not.Null, "the test controller (the prebuild setup)");
            Animator reference = Make("reference", AnimatorUpdateMode.Normal);
            Animator normal = Make("normal", AnimatorUpdateMode.Normal);
            FeedbackPlayer p = Player(normal, Punch(), false);
            yield return null;
            p.Play();
            var rates = new float[1];
            var speeds = new float[1];
            yield return Rates(reference, new[] { normal }, rates, speeds);
            Assert.That(rates[0], Is.GreaterThan(1.3f), "the punch runs the clip faster — " + rates[0]);
            Assert.That(speeds[0], Is.EqualTo(1f), "the game's Update reads the base");
            p.Stop(StopMode.Revert);
            yield return null;
            Assert.That(normal.speed, Is.EqualTo(1f), "back");
        }

        // ---- a Fixed animator evaluates after the revert — a Punch shows with the direct write only · a Tween To (Persist) shows
        // without it
        [UnityTest]
        public IEnumerator AQ05b_AFixedAnimatorAndTheDirectWrite()
        {
            Animator reference = Make("reference", AnimatorUpdateMode.Normal);
            Animator offset = Make("fixed", AnimatorUpdateMode.Fixed);
            Animator direct = Make("fixedDirect", AnimatorUpdateMode.Fixed);
            Animator tween = Make("fixedTween", AnimatorUpdateMode.Fixed);
            var players = new[] { Player(offset, Punch(), false), Player(direct, Punch(), true), Player(tween, TweenTo(4f), false) };
            yield return null;
            foreach (FeedbackPlayer p in players)
                p.Play();
            var rates = new float[3];
            var speeds = new float[3];
            yield return Rates(reference, new[] { offset, direct, tween }, rates, speeds);
            Assert.That(rates[0], Is.EqualTo(1f).Within(0.1f), "the punch without the direct write is not seen — " + rates[0]);
            Assert.That(rates[1], Is.GreaterThan(1.3f), "the direct write's punch is seen — " + rates[1]);
            Assert.That(rates[2], Is.GreaterThan(1.5f), "a tween is seen — " + rates[2]);
            foreach (FeedbackPlayer p in players)
                p.Stop(StopMode.Revert);
            yield return null;
            Assert.That((offset.speed, direct.speed, tween.speed), Is.EqualTo((1f, 1f, 1f)), "back");
        }

        // ---- a player prepared at its Awake while its animator's object is inactive (never active · turned off after it ran) —
        // no exception and no warning; once the object is on, a play writes the Int parameter's Set and the Float Parameter's
        // tween
        [UnityTest]
        public IEnumerator AQ05c_AnInactiveAnimatorAtTheAwake()
        {
            var logs = new List<string>();
            Application.LogCallback log = (message, stack, type) =>
            {
                if (type != LogType.Log)
                    logs.Add(type + ": " + message);
            };
            Application.logMessageReceived += log;
            try
            {
                foreach (bool never in new[] { true, false })
                {
                    string how = never ? "never active" : "turned off";
                    var target = new GameObject(how);
                    m_Made.Add(target);
                    if (never)
                        target.SetActive(false);
                    Animator animator = AnimationFixtures.Make(target);
                    yield return null;
                    if (!never)
                        target.SetActive(false);
                    var go = new GameObject("player");
                    m_Made.Add(go);
                    go.SetActive(false);
                    var p = go.AddComponent<FeedbackPlayer>();
                    m_Players.Add(p);
                    p.m_Initialization = PlayerInitialization.Awake;
                    var count = new AnimatorParameterItem { m_Parameter = "Count", m_Kind = AnimatorParameterKind.Int, m_Int = 3, m_Motion = new SetMotion() };
                    count.m_Target = new TargetRef { m_Mode = TargetRefMode.Direct, m_Object = animator, m_Path = "", m_Anchor = "" };
                    var speed = new AnimatorFloatItem { m_Parameter = "Speed", m_Motion = TweenTo(0.8f) };
                    speed.m_Target = new TargetRef { m_Mode = TargetRefMode.Direct, m_Object = animator, m_Path = "", m_Anchor = "" };
                    speed.m_Timing.m_Duration = 0.1f;
                    p.m_Items.Add(count);
                    p.m_Items.Add(speed);
                    go.SetActive(true);
                    yield return null;
                    Assert.That((p.Runtime != null, p.Runtime?.Binding(0) != null, p.Runtime?.Binding(1) != null), Is.EqualTo((true, true, true)), how + " — prepared at the Awake");
                    target.SetActive(true);
                    yield return null;
                    p.Play();
                    float end = Time.time + 0.3f;
                    while (Time.time < end)
                        yield return null;
                    Assert.That((animator.GetInteger("Count"), Mathf.Abs(animator.GetFloat("Speed") - 0.8f) < 1e-4f), Is.EqualTo((3, true)), how + " — written once it is on");
                }
                Assert.That(logs, Is.Empty, "no exception, no warning");
            }
            finally
            {
                Application.logMessageReceived -= log;
            }
        }

        // ---- the same with a name the controller lacks and names of another kind — the editor's prepare refuses them (the
        // editor lists an inactive object's parameters), a player judges them at the first run on the initialized animator (a
        // player lists none): either way nothing is written and the engine warns of nothing. A Trigger written to a Bool sets it
        // (the engine's one write across kinds), so that item shows a missing judgment in a player too
        [UnityTest]
        public IEnumerator AQ05d_AnInactiveAnimatorsWrongNames()
        {
            var logs = new List<string>();
            Application.LogCallback log = (message, stack, type) =>
            {
                if (type != LogType.Log && !message.StartsWith("A Feedback item was skipped", StringComparison.Ordinal))
                    logs.Add(type + ": " + message);
            };
            Application.logMessageReceived += log;
            try
            {
                foreach (bool never in new[] { true, false })
                {
                    string how = never ? "never active" : "turned off";
                    var target = new GameObject(how);
                    m_Made.Add(target);
                    if (never)
                        target.SetActive(false);
                    Animator animator = AnimationFixtures.Make(target);
                    yield return null;
                    if (!never)
                        target.SetActive(false);
                    var go = new GameObject("player");
                    m_Made.Add(go);
                    go.SetActive(false);
                    var p = go.AddComponent<FeedbackPlayer>();
                    m_Players.Add(p);
                    p.m_Initialization = PlayerInitialization.Awake;
                    TargetRef to = new TargetRef { m_Mode = TargetRefMode.Direct, m_Object = animator, m_Path = "", m_Anchor = "" };
                    p.m_Items.Add(new AnimatorParameterItem { m_Parameter = "Nope", m_Kind = AnimatorParameterKind.Int, m_Int = 3, m_Motion = new SetMotion(), m_Target = to });
                    p.m_Items.Add(new AnimatorParameterItem { m_Parameter = "Count", m_Kind = AnimatorParameterKind.Bool, m_Motion = new SetMotion(), m_Target = to });
                    p.m_Items.Add(new AnimatorParameterItem { m_Parameter = "On", m_Kind = AnimatorParameterKind.Trigger, m_Motion = new SetMotion(), m_Target = to });
                    var nope = new AnimatorFloatItem { m_Parameter = "Nope", m_Motion = TweenTo(0.8f), m_Target = to };
                    nope.m_Timing.m_Duration = 0.1f;
                    var count = new AnimatorFloatItem { m_Parameter = "Count", m_Motion = TweenTo(3f), m_Target = to };
                    count.m_Timing.m_Duration = 0.1f;
                    p.m_Items.Add(nope);
                    p.m_Items.Add(count);
                    go.SetActive(true);
                    yield return null;
                    target.SetActive(true);
                    yield return null;
                    p.Play();
                    float end = Time.time + 0.3f;
                    while (Time.time < end)
                        yield return null;
                    Assert.That((animator.GetInteger("Count"), animator.GetBool("On"), animator.GetFloat("Speed")), Is.EqualTo((0, false, 0f)), how + " — nothing written");
                }
                Assert.That(logs, Is.Empty, "no exception, no engine warning");
            }
            finally
            {
                Application.logMessageReceived -= log;
            }
        }
    }
}
