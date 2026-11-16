using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using xpTURN.Feedback.Editor;
using Object = UnityEngine.Object;

namespace xpTURN.Feedback.Tests.Editor
{
    // Play At Point in Edit mode — the refusals with their reasons, the voice's volume and pitch, the preview's hidden voice,
    // the voices' service and the output fields of the player and the project. What it does in play is AudioPoolPlayTests.
    // The package's test mixers from their own place. The Audio assembly's types are not named: the voices are read through
    // the pools and the context's services (DevProject)
    public sealed class AudioPoolTargetsTests
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

        FeedbackScheduler Sched(ContextKind kind = ContextKind.Test) { var s = new FeedbackScheduler(kind); m_S.Add(s); return s; }

        FeedbackPlayer Player(FeedbackScheduler s, FeedbackItem item, Object output = null)
        {
            var go = new GameObject("Player");
            m_Made.Add(go);
            FeedbackPlayer p = go.AddComponent<FeedbackPlayer>();
            p.m_AudioOutput = output;
            p.m_Items.Add(item);
            p.Bind(s);
            p.EnsureRuntime();
            m_P.Add(p);
            return p;
        }

        AudioClip Clip(float seconds)
        {
            AudioClip clip = AudioClip.Create("pool", Mathf.CeilToInt(seconds * 44100), 1, 44100, false);
            m_Made.Add(clip);
            return clip;
        }

        Transform Anchor()
        {
            var go = new GameObject("Anchor");
            m_Made.Add(go);
            return go.transform;
        }

        static AudioMixerGroup Master(string mixer) => AssetDatabase.LoadAssetAtPath<AudioMixer>(Mixers + mixer + ".mixer").FindMatchingGroups("Master")[0];

        static AudioPlayAtPointItem Point(Object resource, Transform target, Object output = null, FeedbackMotion motion = null)
            => new AudioPlayAtPointItem
            {
                m_Resource = resource,
                m_Output = output,
                m_Target = new TargetRef { m_Mode = TargetRefMode.Direct, m_Object = target, m_Path = "", m_Anchor = "" },
                m_Motion = motion ?? new SetMotion { m_Params = new SetParams { m_Value = new Vector4(1f, 0, 0, 0) } },
            };

        // ---- each refusal with its reason, once · the outputs of the item, the player and the project taken
        [Test, Category(OptionalAssemblyTests.DevProjectCategory)]
        public void AQ01_TheRefusals()
        {
            AudioMixerGroup group = Master("FeedbackTestMixer");
            var texture = new Texture2D(1, 1);
            m_Made.Add(texture);
            AudioClip clip = Clip(0.1f);
            Transform at = Anchor();
            FeedbackScheduler s = Sched();
            (FeedbackPlayer Player, string Reason)[] refused =
            {
                (Player(s, Point(null, at)), "no resource"),
                (Player(s, Point(texture, at)), "the resource is not an AudioResource"),
                (Player(s, Point(clip, at, texture)), "the output is not an AudioMixerGroup"),
                (Player(s, Point(clip, at), texture), "the player's Audio Output is not an AudioMixerGroup"),
            };
            foreach ((FeedbackPlayer p, string reason) in refused)
            {
                Assert.That(p.Runtime.Binding(0), Is.Null, reason + " — refused");
                Assert.That(Logged(reason), Is.EqualTo(1), reason + " — once");
            }
            FeedbackScheduler project = Sched();
            project.Context.Settings.AudioOutput = texture;
            Assert.That(Player(project, Point(clip, at)).Runtime.Binding(0), Is.Null, "the project's output — refused");
            Assert.That(Logged("the project's Default Audio Output is not an AudioMixerGroup"), Is.EqualTo(1), "the project's — once");
            // the item's own output passes over the project's wrong one · the player's too · the right types are taken
            Assert.That(Player(project, Point(clip, at, group)).Runtime.Binding(0), Is.Not.Null, "the item's output first");
            Assert.That(Player(project, Point(clip, at), group).Runtime.Binding(0), Is.Not.Null, "the player's output next");
            Assert.That(Player(s, Point(clip, at)).Runtime.Binding(0), Is.Not.Null, "no output anywhere — taken");
            // the motions it does not take
            Assert.That(Player(s, Point(clip, at, null, new TweenMotion())).Runtime.Binding(0), Is.Null, "a Tween");
            Assert.That(m_Errors, Is.Empty, "no error");
        }

        // ---- the voice's volume (0 to 1) and pitch (the item's ± its range, the same for a seed and an occurrence, 0.1 to 3)
        [Test]
        public void AQ02_TheVoicesVolumeAndPitch()
        {
            Assert.That(new[] { AudioMath.VoiceVolume(0.5f, 1f, 1f), AudioMath.VoiceVolume(0.5f, 1f, 0.5f), AudioMath.VoiceVolume(0.5f, 4f, 1f), AudioMath.VoiceVolume(-1f, 1f, 1f), AudioMath.VoiceVolume(1f, 1f, 0f) },
                Is.EqualTo(new[] { 0.5f, 0.25f, 1f, 0f, 0f }));
            Assert.That(AudioMath.VoicePitch(1.25f, 0f, 7u, 3), Is.EqualTo(1.25f), "no range — the pitch itself");
            Assert.That(AudioMath.VoicePitch(1f, 0.5f, 7u, 3), Is.EqualTo(AudioMath.VoicePitch(1f, 0.5f, 7u, 3)), "the same seed and occurrence");
            var drawn = new HashSet<float>();
            for (long o = 0; o < 32; o++)
            {
                float p = AudioMath.VoicePitch(1f, 0.5f, 7u, o);
                Assert.That(p, Is.InRange(0.5f, 1.5f), "within the range");
                drawn.Add(p);
            }
            Assert.That(drawn.Count, Is.GreaterThan(16), "the occurrences differ");
            for (uint seed = 0; seed < 64; seed++)
            {
                Assert.That(AudioMath.VoicePitch(1f, 1f, seed, 1), Is.InRange(0.1f, 2f), "a range of 1 around 1 — never 0 or below");
                Assert.That(AudioMath.VoicePitch(2.9f, 1f, seed, 1), Is.InRange(1.9f, 3f), "never above 3");
            }
        }

        // ---- the preview's voice: hidden (out of the scene), it plays in Edit mode, the pools hold the automatic end while it
        // lives, the close leaves nothing
        [Test, Category(OptionalAssemblyTests.DevProjectCategory)]
        public void AQ03_ThePreviewsHiddenVoice()
        {
            FeedbackScheduler s = Sched(ContextKind.Preview);
            FeedbackPlayer p = Player(s, Point(Clip(1f), Anchor()));
            p.Play();
            s.Tick(0.02f);
            PoolService pools = s.Context.PoolsIfMade;
            Assert.That(pools.LiveCount, Is.EqualTo(1), "a voice");
            GameObject voice = pools.LiveAt(0);
            Assert.That((voice.hideFlags, voice.name.StartsWith(PropertyLayerRegistry.PreviewPrefix), voice.scene.IsValid()), Is.EqualTo((HideFlags.HideAndDontSave, true, false)), "hidden · out of the scene");
            Assert.That(voice.GetComponent<AudioSource>().isPlaying, Is.True, "it plays in Edit mode");
            Assert.That(pools.HoldsTimed, Is.True, "the automatic end waits");
            Assert.That(Template(s).hideFlags, Is.EqualTo(HideFlags.HideAndDontSave), "the template hidden too");
            p.ReleaseRuntime();
            s.Shutdown();
            Assert.That(Resources.FindObjectsOfTypeAll<AudioSource>().Count(a => a != null && a.name.Contains(VoiceName)), Is.Zero, "nothing left");
            Assert.That(SceneManager.GetActiveScene().isDirty, Is.False, "the scene clean");
            Assert.That(m_Errors, Is.Empty, "no error");
        }

        // ---- the service: a template a context (on, under the pools' inactive container, a source that does not play on
        // awake), its rows, a template made again after the pools close
        [Test, Category(OptionalAssemblyTests.DevProjectCategory)]
        public void AQ04_TheVoicesService()
        {
            FeedbackScheduler a = Sched();
            FeedbackScheduler b = Sched();
            AudioClip clip = Clip(1f);
            Transform at = Anchor();
            FeedbackPlayer p = Player(a, Point(clip, at));
            Player(b, Point(clip, at));
            GameObject template = Template(a);
            Assert.That(template, Is.Not.Null.And.Not.SameAs(Template(b)), "a template a context");
            Assert.That(template.name, Is.EqualTo(PropertyLayerRegistry.RuntimePrefix + VoiceName), "its name");
            Assert.That((template.transform.parent, template.activeSelf, template.transform.parent.gameObject.activeSelf, template.hideFlags),
                Is.EqualTo((a.Context.Pools.Container, true, false, HideFlags.None)), "on, under the inactive container, no flags");
            AudioSource source = template.GetComponent<AudioSource>();
            Assert.That((source.playOnAwake, source.loop), Is.EqualTo((false, false)), "no play on awake · no loop");
            p.Play();
            a.Tick(0.02f);
            FeedbackService voices = Enumerable.Range(0, a.Context.ServiceCount).Select(a.Context.ServiceAt).Single(x => x.DebugName == "Audio voices");
            var rows = new List<ServiceRow>();
            voices.Describe(rows);
            Assert.That(string.Join(" · ", rows.Select(r => r.Label + " " + r.Value)), Is.EqualTo("voices live 1 · paused 0 · fading 0 · template 1"), "the debugger's rows");
            p.ReleaseRuntime();
            a.Context.Pools.Shutdown();
            Assert.That(template == null, Is.True, "the pools' close took the template");
            Player(a, Point(clip, at));
            Assert.That(Template(a), Is.Not.Null, "made again for the next preparation");
        }

        const string VoiceName = "AudioVoice";

        // the voices' template — the pools' one pooled row
        static GameObject Template(FeedbackScheduler s)
        {
            var rows = new List<PoolRow>();
            s.Context.PoolsIfMade.Describe(rows);
            return rows.Single(r => r.Pooled).Prefab;
        }

        // ---- the output fields: the project's value (play reads the asset · a preview and a test the defaults), the player's
        // and the settings' fields take an AudioMixerGroup
        [Test, Category(OptionalAssemblyTests.DevProjectCategory)]
        public void AQ05_TheOutputFields()
        {
            AudioMixerGroup group = Master("FeedbackTestMixer");
            var settings = ScriptableObject.CreateInstance<FeedbackSettings>();
            m_Made.Add(settings);
            settings.m_AudioOutput = group;
            Assert.That(SettingsValues.From(settings).AudioOutput, Is.SameAs(group), "the asset's");
            Assert.That((SettingsValues.From(null).AudioOutput, SettingsValues.Load(ContextKind.Test).AudioOutput, SettingsValues.Load(ContextKind.Preview).AudioOutput),
                Is.EqualTo(((Object)null, (Object)null, (Object)null)), "the defaults");
            var go = new GameObject("Player");
            m_Made.Add(go);
            var view = new PlayerSettingsView(new SerializedObject(go.AddComponent<FeedbackPlayer>()));
            ObjectField field = view.Advanced.Query<ObjectField>().ToList().Single(f => f.bindingPath == "m_AudioOutput");
            Assert.That((field.label, field.objectType), Is.EqualTo(("Audio Output", typeof(AudioMixerGroup))), "the player's field — Advanced");
            var editor = (FeedbackSettingsEditor)UnityEditor.Editor.CreateEditor(settings);
            m_Made.Add(editor);
            ObjectField setting = editor.CreateInspectorGUI().Query<ObjectField>().ToList().Single(f => f.bindingPath == "m_AudioOutput");
            Assert.That((setting.label, setting.objectType), Is.EqualTo(("Default Audio Output", typeof(AudioMixerGroup))), "the project's field");
        }
    }
}
