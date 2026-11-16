using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Audio;
using Object = UnityEngine.Object;

namespace xpTURN.Feedback.Tests.Editor
{
    // the Audio mixer items in Edit mode — the refusals with their reasons (Edit mode reads a mixer's exposed names; it takes
    // no write and runs no transition, so the behaviour is Play mode's — AudioMixerPlayTests). The package's test mixers from
    // their own place. The Audio assembly's types are not named: the items prepare through players (DevProject)
    public sealed class AudioMixerTargetsTests
    {
        const string Mixers = "Packages/com.xpturn.feedback/Tests/Editor/Audio/";
        readonly List<Object> m_Made = new List<Object>();
        readonly List<FeedbackScheduler> m_S = new List<FeedbackScheduler>();
        readonly List<FeedbackPlayer> m_P = new List<FeedbackPlayer>();
        readonly List<string> m_Logs = new List<string>();
        readonly List<string> m_Errors = new List<string>();

        [SetUp]
        public void SetUp()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Application.logMessageReceived += OnLog;
        }

        [TearDown]
        public void TearDown()
        {
            Application.logMessageReceived -= OnLog;
            foreach (FeedbackPlayer p in m_P)
                if (p != null) p.ReleaseRuntime();
            foreach (FeedbackScheduler s in m_S)
                if (!s.IsShutdown) s.Shutdown();
            foreach (Object o in m_Made)
                if (o != null) Object.DestroyImmediate(o);
            m_Made.Clear(); m_S.Clear(); m_P.Clear(); m_Logs.Clear(); m_Errors.Clear();
        }

        void OnLog(string message, string stack, LogType type)
        {
            m_Logs.Add(message);
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
                m_Errors.Add(message);
        }

        int Logged(string part) => m_Logs.Count(m => m.Contains(part));

        FeedbackScheduler Sched() { var s = new FeedbackScheduler(ContextKind.Test); m_S.Add(s); return s; }

        FeedbackPlayer Player(FeedbackScheduler s, params FeedbackItem[] items)
        {
            var go = new GameObject("Player");
            m_Made.Add(go);
            FeedbackPlayer p = go.AddComponent<FeedbackPlayer>();
            p.m_Items.AddRange(items);
            p.Bind(s);
            p.EnsureRuntime();
            m_P.Add(p);
            return p;
        }

        static AudioMixer Mixer(string name) => AssetDatabase.LoadAssetAtPath<AudioMixer>(Mixers + name + ".mixer");

        static AudioMixerParameterItem Parameter(Object mixer, string name, FeedbackMotion motion = null)
        {
            var item = new AudioMixerParameterItem { m_Mixer = mixer, m_Parameter = name };
            item.m_Motion = motion ?? new CurveMotion { m_Params = new CurveParams { m_Curve = AnimationCurve.Constant(0f, 1f, 1f), m_RemapZero = Vector4.zero, m_RemapOne = Vector4.one * -6f } };
            item.m_Timing.m_Duration = 0.5f;
            return item;
        }

        static AudioMixerSnapshotItem Snapshot(Object snapshot, Object back, FeedbackMotion motion = null)
            => new AudioMixerSnapshotItem { m_Snapshot = snapshot, m_BackTo = back, m_Motion = motion ?? new SetMotion { m_Params = new SetParams { m_Value = new Vector4(1f, 0, 0, 0) } } };

        // ---- each refusal with its reason, once · the same items with their fields right are taken
        [Test, Category(OptionalAssemblyTests.DevProjectCategory)]
        public void AM01_TheRefusals()
        {
            AudioMixer mixer = Mixer("FeedbackTestMixer");
            AudioMixer other = Mixer("FeedbackTestMixerOther");
            Assert.That((mixer != null, other != null), Is.EqualTo((true, true)), "the test mixers");
            AudioMixerSnapshot a = mixer.FindSnapshot("A"), b = mixer.FindSnapshot("B"), elsewhere = other.FindSnapshot("A");
            Assert.That((a != null, b != null, elsewhere != null), Is.EqualTo((true, true, true)), "the snapshots");
            var texture = new Texture2D(1, 1);
            m_Made.Add(texture);
            FeedbackScheduler s = Sched();
            (FeedbackItem Item, string Reason)[] refused =
            {
                (Parameter(null, "FeedbackTestVolume"), "no mixer"),
                (Parameter(texture, "FeedbackTestVolume"), "the mixer is not an AudioMixer"),
                (Parameter(mixer, ""), "name the exposed parameter"),
                (Parameter(mixer, "Nope"), "no exposed parameter 'Nope' on FeedbackTestMixer"),
                (Snapshot(null, null), "no snapshot"),
                (Snapshot(mixer, null), "the snapshot is not an AudioMixerSnapshot"),
                (Snapshot(b, texture), "Back To is not an AudioMixerSnapshot"),
                (Snapshot(b, elsewhere), "Back To belongs to another mixer"),
            };
            foreach ((FeedbackItem item, string reason) in refused)
            {
                Assert.That(Player(s, item).Runtime.Binding(0), Is.Null, reason + " — refused");
                Assert.That(Logged(reason), Is.EqualTo(1), reason + " — once");
            }
            FeedbackItem[] taken = { Parameter(mixer, "FeedbackTestVolume"), Parameter(mixer, "FeedbackTestPitch"), Snapshot(b, a), Snapshot(b, null) };
            foreach (FeedbackItem item in taken)
                Assert.That(Player(s, item).Runtime.Binding(0), Is.Not.Null, item.GetType().Name + " taken");
            // the motions they don't take
            Assert.That(Player(s, Snapshot(b, a, new TweenMotion())).Runtime.Binding(0), Is.Null, "a snapshot's Tween");
            Assert.That(Player(s, Parameter(mixer, "FeedbackTestVolume", new SetMotion())).Runtime.Binding(0), Is.Null, "a parameter's Set");
            Assert.That(m_Errors, Is.Empty, "no error");
        }

        // ---- Edit mode — the taken items play and stop without an error, and the mixer is left as it was (no write · no
        // transition)
        [Test, Category(OptionalAssemblyTests.DevProjectCategory)]
        public void AM02_EditModeLeavesTheMixer()
        {
            AudioMixer mixer = Mixer("FeedbackTestMixer");
            AudioMixerSnapshot a = mixer.FindSnapshot("A"), b = mixer.FindSnapshot("B");
            Assert.That(mixer.GetFloat("FeedbackTestVolume", out float before), Is.True, "Edit mode reads it");
            FeedbackScheduler s = Sched();
            FeedbackPlayer p = Player(s, Parameter(mixer, "FeedbackTestVolume"), Snapshot(b, a));
            p.Play();
            for (int i = 0; i < 4; i++)
                s.Tick(0.05f);
            mixer.GetFloat("FeedbackTestVolume", out float during);
            p.Stop(StopMode.Revert);
            s.Tick(0f);
            mixer.GetFloat("FeedbackTestVolume", out float after);
            Assert.That((during, after), Is.EqualTo((before, before)), "no write in Edit mode");
            Assert.That(m_Errors, Is.Empty, "no error");
        }
    }
}
