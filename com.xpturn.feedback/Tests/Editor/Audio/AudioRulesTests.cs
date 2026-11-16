using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Audio;
using xpTURN.Feedback.Editor;
using Object = UnityEngine.Object;
using F = xpTURN.Feedback.Tests.Editor.ValidationFixtures;

namespace xpTURN.Feedback.Tests.Editor
{
    // the object a test puts in place of Project Settings > Audio — FB-T04 reads and fixes its Disable Unity Audio
    internal sealed class AudioSettingsDouble : ScriptableObject
    {
        [SerializeField] internal bool m_DisableAudio;
    }

    // group T — each rule in its cases, its severity, its fixes; the prepare skips the same items for the same reason (the rules
    // make the Audio assembly's checks again). The items need their implementation (DevProject). The project's audio setting is a
    // seam — no test changes it; a mixer a fix writes is a copy
    public sealed class AudioRulesTests
    {
        const string Mixers = "Packages/com.xpturn.feedback/Tests/Editor/Audio/";
        const string ResourcesDir = "Assets/Resources";
        const string SettingsPath = "Assets/Resources/FeedbackSettings.asset";
        const BindingFlags All = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        F m_Fx;
        Func<Object> m_Manager;
        bool m_HadResources;
        readonly List<Object> m_Made = new List<Object>();
        readonly List<FeedbackScheduler> m_S = new List<FeedbackScheduler>();
        readonly List<FeedbackPlayer> m_Bound = new List<FeedbackPlayer>();
        readonly List<string> m_Logs = new List<string>();

        [SetUp]
        public void SetUp()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            m_Fx = new F();
            m_Manager = AudioRuleReads.AudioManager;
            m_HadResources = AssetDatabase.IsValidFolder(ResourcesDir);
            Application.logMessageReceived += OnLog;
        }

        [TearDown]
        public void TearDown()
        {
            Application.logMessageReceived -= OnLog;
            AudioRuleReads.AudioManager = m_Manager;
            foreach (FeedbackPlayer p in m_Bound)
                if (p != null) p.ReleaseRuntime();
            foreach (FeedbackScheduler s in m_S)
                if (!s.IsShutdown) s.Shutdown();
            m_Fx.Clean();
            foreach (Object o in m_Made)
                if (o != null) Object.DestroyImmediate(o);
            AssetDatabase.DeleteAsset(SettingsPath);
            if (!m_HadResources && AssetDatabase.IsValidFolder(ResourcesDir))
                AssetDatabase.DeleteAsset(ResourcesDir);
            m_Made.Clear(); m_S.Clear(); m_Bound.Clear(); m_Logs.Clear();
        }

        void OnLog(string message, string stack, LogType type) => m_Logs.Add(message);

        static AudioMixer Mixer(string name) => AssetDatabase.LoadAssetAtPath<AudioMixer>(Mixers + name + ".mixer");

        AudioClip Clip(int samples = 4410)
        {
            var clip = AudioClip.Create("ar", samples, 1, 44100, false);
            m_Made.Add(clip);
            return clip;
        }

        Texture2D Texture()
        {
            var t = new Texture2D(2, 2);
            m_Made.Add(t);
            return t;
        }

        AudioSource Source(string name, bool clip = true)
        {
            var go = new GameObject(name);
            m_Made.Add(go);
            var s = go.AddComponent<AudioSource>();
            s.playOnAwake = false;
            if (clip)
                s.clip = Clip();
            return s;
        }

        // an Audio Random Container made in code (the type is internal) — two elements of the clip, its loop mode
        AudioResource Container(AudioClip clip, string loop)
        {
            Type t = typeof(AudioResource).Assembly.GetType("UnityEngine.Audio.AudioRandomContainer");
            Type e = typeof(AudioResource).Assembly.GetType("UnityEngine.Audio.AudioContainerElement");
            Assert.That(t != null && e != null, Is.True, "the editor knows the container's types");
            var arc = (AudioResource)Activator.CreateInstance(t, true);
            Array list = Array.CreateInstance(e, 2);
            for (int i = 0; i < 2; i++)
            {
                var el = (Object)Activator.CreateInstance(e, true);
                e.GetProperty("audioClip", All).SetValue(el, clip);
                e.GetProperty("enabled", All)?.SetValue(el, true);
                list.SetValue(el, i);
                m_Made.Add(el);
            }
            t.GetProperty("elements", All).SetValue(arc, list);
            PropertyInfo mode = t.GetProperty("loopMode", All);
            mode.SetValue(arc, Enum.Parse(mode.PropertyType, loop));
            m_Made.Add(arc);
            return arc;
        }

        static T On<T>(T item, Object target, FeedbackMotion motion = null) where T : TargetItem
        {
            item.m_Target = target != null
                ? new TargetRef { m_Mode = TargetRefMode.Direct, m_Object = target, m_Path = "", m_Anchor = "" }
                : new TargetRef { m_Mode = TargetRefMode.Self, m_Path = "", m_Anchor = "" };
            item.m_Motion = motion ?? new SetMotion { m_Params = new SetParams { m_Value = new Vector4(1f, 0, 0, 0) } };
            return item;
        }

        static TweenMotion Tween => new TweenMotion { m_Params = new TweenParams { m_To = new Vector4(0.5f, 0, 0, 0), m_Loops = 1 } };

        static void Want(Component host, string id, int[] items, RuleSeverity severity)
        {
            List<RuleResult> results = F.Of(host, id);
            Assert.That(results.Select(r => r.Item).OrderBy(i => i), Is.EqualTo(items), id);
            Assert.That(results.All(r => r.Severity == severity), Is.True, id + " " + severity);
        }

        static string Message(Component host, string id, int item) => F.Of(host, id).Single(r => r.Item == item).Message;

        // the items prepared by a bound player — each list index's skip reason (null: prepared)
        string[] Skips(Func<FeedbackItem[]> items, Action<FeedbackPlayer> set = null)
        {
            var s = new FeedbackScheduler(ContextKind.Test);
            m_S.Add(s);
            var go = new GameObject("Prepared");
            m_Made.Add(go);
            var p = go.AddComponent<FeedbackPlayer>();
            FeedbackItem[] list = items();
            p.m_Items.AddRange(list);
            set?.Invoke(p);
            m_Logs.Clear();
            p.Bind(s);
            p.EnsureRuntime();
            m_Bound.Add(p);
            var reasons = new string[list.Length];
            for (int i = 0; i < list.Length; i++)
            {
                string head = PlayerRuntime.SkippedWarning + i + " (";
                string log = m_Logs.FirstOrDefault(l => l.StartsWith(head, StringComparison.Ordinal));
                reasons[i] = log == null ? null : log.IndexOf("): ", StringComparison.Ordinal) is int at && at >= 0 ? log.Substring(at + 3) : "";
            }
            return reasons;
        }

        // ---- T01 — an asset missing, of another type, a Back To of another mixer · the right ones · an inactive item · the prepare
        [Test, Category(OptionalAssemblyTests.DevProjectCategory)]
        public void AR01_TheAssetRule()
        {
            AudioSource source = Source("AR01Source");
            AudioMixer mixer = Mixer("FeedbackTestMixer");
            AudioMixer other = Mixer("FeedbackTestMixerOther");
            Texture2D tex = Texture();
            AudioClip clip = Clip();
            Func<FeedbackItem[]> items = () =>
            {
                var off = On(new AudioPlayClipItem { m_Clip = tex }, source);
                off.m_Active = false;
                return new FeedbackItem[]
                {
                    On(new AudioPlayClipItem(), source),
                    On(new AudioPlayClipItem { m_Clip = tex }, source),
                    On(new AudioPlayClipItem { m_Clip = clip }, source),
                    On(new AudioPlayAtPointItem(), source.transform),
                    On(new AudioPlayAtPointItem { m_Resource = tex }, source.transform),
                    On(new AudioPlayAtPointItem { m_Resource = clip }, source.transform),
                    On(new AudioMixerParameterItem { m_Parameter = "FeedbackTestVolume" }, null, Tween),
                    On(new AudioMixerParameterItem { m_Mixer = tex, m_Parameter = "FeedbackTestVolume" }, null, Tween),
                    On(new AudioMixerSnapshotItem(), null),
                    On(new AudioMixerSnapshotItem { m_Snapshot = tex }, null),
                    On(new AudioMixerSnapshotItem { m_Snapshot = mixer.FindSnapshot("A"), m_BackTo = tex }, null),
                    On(new AudioMixerSnapshotItem { m_Snapshot = mixer.FindSnapshot("A"), m_BackTo = other.FindSnapshot("A") }, null),
                    On(new AudioMixerSnapshotItem { m_Snapshot = mixer.FindSnapshot("A"), m_BackTo = mixer.FindSnapshot("B") }, null),
                    off,
                };
            };
            FeedbackPlayer p = m_Fx.Player("AR01", items());
            Want(p, "FB-T01", new[] { 0, 1, 3, 4, 6, 7, 8, 9, 10, 11 }, RuleSeverity.Warning);
            string[] said =
            {
                "No clip", "The clip is not an Audio Clip", null, "No resource", "The resource is not an Audio Resource", null, "No mixer", "The mixer is not an Audio Mixer",
                "No snapshot", "The snapshot is not an Audio Mixer Snapshot", "Back To is not an Audio Mixer Snapshot", "Back To belongs to another mixer",
            };
            for (int i = 0; i < said.Length; i++)
            {
                if (said[i] != null)
                    Assert.That(Message(p, "FB-T01", i), Is.EqualTo(said[i] + " — the item is skipped"), "item " + i);
            }
            string[] skipped = Skips(items);
            Assert.That(skipped.Take(13), Is.EqualTo(new[]
            {
                "no clip", "the clip is not an AudioClip", null, "no resource", "the resource is not an AudioResource", null, "no mixer", "the mixer is not an AudioMixer",
                "no snapshot", "the snapshot is not an AudioMixerSnapshot", "Back To is not an AudioMixerSnapshot", "Back To belongs to another mixer", null,
            }), "the prepare skips the same items");
        }

        // ---- T02 — the name: empty · not exposed · another case (its fix) · exposed — from the mixer's serialized list, with no
        // console warning (the control: GetFloat warns on a name the mixer does not expose) · with Unity's audio off too
        [Test, Category(OptionalAssemblyTests.DevProjectCategory)]
        public void AR02_TheExposedNameRule()
        {
            AudioMixer mixer = Mixer("FeedbackTestMixer");
            Func<FeedbackItem[]> items = () => new FeedbackItem[]
            {
                On(new AudioMixerParameterItem { m_Mixer = mixer, m_Parameter = "" }, null, Tween),
                On(new AudioMixerParameterItem { m_Mixer = mixer, m_Parameter = "Nope" }, null, Tween),
                On(new AudioMixerParameterItem { m_Mixer = mixer, m_Parameter = "feedbacktestvolume" }, null, Tween),
                On(new AudioMixerParameterItem { m_Mixer = mixer, m_Parameter = "FeedbackTestVolume" }, null, Tween),
            };
            FeedbackPlayer p = m_Fx.Player("AR02", items());
            m_Logs.Clear();
            Assert.That(mixer.GetFloat("Nope", out _), Is.False);
            Assert.That(m_Logs.Count(l => l.Contains("Exposed name does not exist")), Is.EqualTo(1), "the control — GetFloat warns");
            m_Logs.Clear();
            Want(p, "FB-T02", new[] { 0, 1, 2 }, RuleSeverity.Warning);
            Assert.That(m_Logs.Count(l => l.Contains("Exposed name does not exist")), Is.Zero, "the rule reads the list — no warning");
            Assert.That(Message(p, "FB-T02", 0), Is.EqualTo("Name the exposed parameter — the item is skipped"));
            Assert.That(Message(p, "FB-T02", 1), Is.EqualTo("The mixer FeedbackTestMixer exposes no parameter 'Nope' — the item is skipped"));
            Assert.That(F.Of(p, "FB-T02").Single(r => r.Item == 1).Fixes, Is.Empty, "no near name");
            var off = ScriptableObject.CreateInstance<AudioSettingsDouble>();
            m_Made.Add(off);
            off.m_DisableAudio = true;
            AudioRuleReads.AudioManager = () => off;
            Want(p, "FB-T02", new[] { 0, 1, 2 }, RuleSeverity.Warning);
            AudioRuleReads.AudioManager = m_Manager;
            RuleResult near = F.Of(p, "FB-T02").Single(r => r.Item == 2);
            FeedbackValidator.Apply(near, near.Fixes.Single(f => f.Name == "Use 'FeedbackTestVolume'"));
            Assert.That(((AudioMixerParameterItem)p.m_Items[2]).m_Parameter, Is.EqualTo("FeedbackTestVolume"));
            Want(p, "FB-T02", new[] { 0, 1 }, RuleSeverity.Warning);
            string[] skipped = Skips(items);
            Assert.That(skipped, Is.EqualTo(new[] { "name the exposed parameter", "no exposed parameter 'Nope' on FeedbackTestMixer", "no exposed parameter 'feedbacktestvolume' on FeedbackTestMixer", null }), "the prepare skips the same items");
        }

        // ---- T03 — the output chain: the item's · the player's · the project's (a settings asset of the test's) · a right
        // earlier one ends it · a Motion Driver goes from the item to the project's
        [Test, Category(OptionalAssemblyTests.DevProjectCategory)]
        public void AR03_TheOutputRule()
        {
            Assume.That(AssetDatabase.LoadAssetAtPath<FeedbackSettings>(SettingsPath), Is.Null, "the project has its own settings asset");
            AudioSource source = Source("AR03Source");
            AudioMixerGroup master = Mixer("FeedbackTestMixer").FindMatchingGroups("Master")[0];
            Texture2D tex = Texture();
            AudioClip clip = Clip();
            AudioPlayAtPointItem Point(Object output) => On(new AudioPlayAtPointItem { m_Resource = clip, m_Output = output }, source.transform);
            FeedbackPlayer item = m_Fx.Player("AR03Item", Point(tex));
            FeedbackPlayer player = m_Fx.Player("AR03Player", Point(null));
            player.m_AudioOutput = tex;
            FeedbackPlayer front = m_Fx.Player("AR03Front", Point(master));
            front.m_AudioOutput = tex;
            Want(item, "FB-T03", new[] { 0 }, RuleSeverity.Warning);
            Assert.That(Message(item, "FB-T03", 0), Is.EqualTo("The output is not an Audio Mixer Group — the item is skipped"));
            Assert.That(Message(player, "FB-T03", 0), Is.EqualTo("The player's Audio Output is not an Audio Mixer Group — the item is skipped"));
            Want(front, "FB-T03", new int[0], RuleSeverity.Warning);
            FeedbackPlayer project = m_Fx.Player("AR03Project", Point(null));
            Want(project, "FB-T03", new int[0], RuleSeverity.Warning);
            if (!AssetDatabase.IsValidFolder(ResourcesDir))
                AssetDatabase.CreateFolder("Assets", "Resources");
            var settings = ScriptableObject.CreateInstance<FeedbackSettings>();
            settings.m_AudioOutput = tex;
            AssetDatabase.CreateAsset(settings, SettingsPath);
            Assert.That(Message(project, "FB-T03", 0), Is.EqualTo("The project's Default Audio Output is not an Audio Mixer Group — the item is skipped"));
            var driverGo = new GameObject("AR03Driver");
            m_Made.Add(driverGo);
            var driver = driverGo.AddComponent<MotionDriver>();
            driver.m_Item = Point(null);
            Assert.That(F.Of(driver, "FB-T03").Select(r => r.Message), Is.EqualTo(new[] { "The project's Default Audio Output is not an Audio Mixer Group — the item is skipped" }), "a driver has no player");
            Assert.That(Skips(() => new FeedbackItem[] { Point(tex) })[0], Is.EqualTo("the output is not an AudioMixerGroup"));
            Assert.That(Skips(() => new FeedbackItem[] { Point(null) }, b => b.m_AudioOutput = tex)[0], Is.EqualTo("the player's Audio Output is not an AudioMixerGroup"));
        }

        // ---- T04 — Unity's audio off (the seam): each audio item once · its fix turns it on, one undo · on: quiet · the setting
        // the default reads is the project's
        [Test, Category(OptionalAssemblyTests.DevProjectCategory)]
        public void AR04_TheAudioOffRule()
        {
            AudioSource source = Source("AR04Source");
            var off = ScriptableObject.CreateInstance<AudioSettingsDouble>();
            m_Made.Add(off);
            off.m_DisableAudio = true;
            AudioRuleReads.AudioManager = () => off;
            FeedbackPlayer p = m_Fx.Player("AR04", On(new AudioPlayClipItem { m_Clip = Clip() }, source), On(new AudioVolumeItem(), source, Tween), F.Position());
            Want(p, "FB-T04", new[] { 0, 1 }, RuleSeverity.Warning);
            Assert.That(Message(p, "FB-T04", 0), Is.EqualTo("Unity's audio is disabled in this project (Project Settings > Audio) — the item makes no sound"));
            Undo.ClearAll();
            RuleResult r = F.Of(p, "FB-T04").First();
            FeedbackValidator.Apply(r, r.Fixes.Single(f => f.Name == AudioOffRule.FixName));
            Assert.That(off.m_DisableAudio, Is.False, "turned on");
            Want(p, "FB-T04", new int[0], RuleSeverity.Warning);
            Undo.PerformUndo();
            Assert.That(off.m_DisableAudio, Is.True, "one undo");
            AudioRuleReads.AudioManager = m_Manager;
            Object manager = m_Manager();
            Assert.That(manager != null ? manager.GetType().FullName : null, Is.EqualTo("UnityEditor.AudioManager"), "the project's audio settings");
        }

        // ---- T05 — a low pass on a distance curve (two keys) · one key (a new filter's) · the prepare
        [Test, Category(OptionalAssemblyTests.DevProjectCategory)]
        public void AR05_TheDistanceCurveRule()
        {
            AudioSource curved = Source("AR05Curved");
            AudioLowPassFilter filter = curved.gameObject.AddComponent<AudioLowPassFilter>();
            filter.customCutoffCurve = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0.2f));
            AudioSource plain = Source("AR05Plain");
            AudioLowPassFilter flat = plain.gameObject.AddComponent<AudioLowPassFilter>();
            Func<FeedbackItem[]> items = () => new FeedbackItem[] { On(new AudioLowPassCutoffItem(), filter, Tween), On(new AudioLowPassCutoffItem(), flat, Tween) };
            Assert.That(flat.customCutoffCurve.length, Is.EqualTo(1), "a new filter's curve");
            FeedbackPlayer p = m_Fx.Player("AR05", items());
            Want(p, "FB-T05", new[] { 0 }, RuleSeverity.Warning);
            Assert.That(Message(p, "FB-T05", 0), Is.EqualTo("The low pass follows a distance curve (its source's 3D Low-Pass curve) — the item is skipped"));
            Assert.That(Skips(items), Is.EqualTo(new[] { "the low pass follows a distance curve (the source's 3D Low-Pass curve) — a cutoff write would replace it", null }));
        }

        // ---- T06 — a source off (the component · an object above) for Play · Unpause · a Play Clip · Stop is quiet · in a
        // prefab asset an on source is quiet (isActiveAndEnabled is false there) while its scene copy turned off speaks
        [Test, Category(OptionalAssemblyTests.DevProjectCategory)]
        public void AR06_TheSourceOffRule()
        {
            AudioSource disabled = Source("AR06Disabled");
            disabled.enabled = false;
            AudioSource under = Source("AR06Under");
            var parent = new GameObject("AR06Parent");
            m_Made.Add(parent);
            under.transform.SetParent(parent.transform, false);
            parent.SetActive(false);
            AudioSource on = Source("AR06On");
            FeedbackPlayer p = m_Fx.Player("AR06",
                On(new AudioSourceControlItem { m_Action = AudioSourceAction.Play }, disabled),
                On(new AudioSourceControlItem { m_Action = AudioSourceAction.Unpause }, under),
                On(new AudioSourceControlItem { m_Action = AudioSourceAction.Stop }, disabled),
                On(new AudioPlayClipItem { m_Clip = Clip() }, disabled),
                On(new AudioSourceControlItem { m_Action = AudioSourceAction.Play }, on));
            Want(p, "FB-T06", new[] { 0, 1, 3 }, RuleSeverity.Info);
            Assert.That(Message(p, "FB-T06", 0), Is.EqualTo("The source is off — while it is off the item makes no sound"));
            var root = new GameObject("AR06Root");
            var child = new GameObject("src");
            child.transform.SetParent(root.transform, false);
            AudioSource own = child.AddComponent<AudioSource>();
            own.playOnAwake = false;
            own.clip = Clip();
            FeedbackPlayer local = root.AddComponent<FeedbackPlayer>();
            local.m_Items.Add(On(new AudioSourceControlItem { m_Action = AudioSourceAction.Play }, own));
            GameObject asset = PrefabUtility.SaveAsPrefabAsset(root, F.Dir + "/AR06.prefab");
            own.enabled = false;
            Want(local, "FB-T06", new[] { 0 }, RuleSeverity.Info);
            Object.DestroyImmediate(root);
            var prefabPlayer = asset.GetComponent<FeedbackPlayer>();
            Assert.That(asset.GetComponentInChildren<AudioSource>(true).isActiveAndEnabled, Is.False, "a prefab asset's on source reads as not active");
            Want(prefabPlayer, "FB-T06", new int[0], RuleSeverity.Info);
        }

        // ---- T07 — a source with nothing to play for Play · Unpause · a clip · a container · Stop are quiet
        [Test, Category(OptionalAssemblyTests.DevProjectCategory)]
        public void AR07_TheEmptySourceRule()
        {
            AudioSource empty = Source("AR07Empty", false);
            AudioSource clip = Source("AR07Clip");
            AudioSource container = Source("AR07Container", false);
            container.resource = Container(Clip(), "Cycles");
            FeedbackPlayer p = m_Fx.Player("AR07",
                On(new AudioSourceControlItem { m_Action = AudioSourceAction.Play }, empty),
                On(new AudioSourceControlItem { m_Action = AudioSourceAction.Unpause }, empty),
                On(new AudioSourceControlItem { m_Action = AudioSourceAction.Stop }, empty),
                On(new AudioSourceControlItem { m_Action = AudioSourceAction.Play }, clip),
                On(new AudioSourceControlItem { m_Action = AudioSourceAction.Play }, container));
            Assert.That((empty.resource == null, container.clip == null), Is.EqualTo((true, true)));
            Want(p, "FB-T07", new[] { 0, 1 }, RuleSeverity.Warning);
            Assert.That(Message(p, "FB-T07", 0), Is.EqualTo("The source has no clip or resource — the item makes no sound"));
        }

        // ---- T08 — a snapshot's clock: Unscaled · Player with an Unscaled player against a Normal mixer · Scaled · an Unscaled
        // mixer are quiet · the two fixes (the mixer's by its serialized field — dirty, one undo) · the package's test mixer
        // unchanged (the fixes write a copy)
        [Test, Category(OptionalAssemblyTests.DevProjectCategory)]
        public void AR08_TheSnapshotClockRule()
        {
            string original = Path.GetFullPath(Mixers + "FeedbackTestMixer.mixer");
            string hash = Hash(original);
            string copyPath = F.Dir + "/AR08.mixer";
            Assert.That(AssetDatabase.CopyAsset(Mixers + "FeedbackTestMixer.mixer", copyPath), Is.True);
            var copy = AssetDatabase.LoadAssetAtPath<AudioMixer>(copyPath);
            AudioMixerSnapshot a = copy.FindSnapshot("A");
            AudioMixerSnapshotItem Snap(ItemClock clock)
            {
                AudioMixerSnapshotItem t = On(new AudioMixerSnapshotItem { m_Snapshot = a }, null);
                t.m_Timing.m_Clock = clock;
                return t;
            }
            FeedbackPlayer p = m_Fx.Player("AR08", Snap(ItemClock.Unscaled), Snap(ItemClock.Scaled), Snap(ItemClock.Player));
            FeedbackPlayer unscaled = m_Fx.Player("AR08Unscaled", Snap(ItemClock.Player));
            unscaled.m_TimeMode = PlayerTimeMode.Unscaled;
            Assert.That(copy.updateMode, Is.EqualTo(AudioMixerUpdateMode.Normal));
            Want(p, "FB-T08", new[] { 0 }, RuleSeverity.Info);
            Want(unscaled, "FB-T08", new[] { 0 }, RuleSeverity.Info);
            Assert.That(Message(p, "FB-T08", 0), Is.EqualTo("The mixer " + copy.name + "'s Update Mode is Normal — at time scale 0 its transition holds while the item goes on"));
            RuleResult r = F.Of(p, "FB-T08").Single();
            FeedbackValidator.Apply(r, r.Fixes.Single(f => f.Name == "Use the scaled clock"));
            Assert.That(p.m_Items[0].m_Timing.m_Clock, Is.EqualTo(ItemClock.Scaled));
            Undo.ClearAll();
            RuleResult m = F.Of(unscaled, "FB-T08").Single();
            FeedbackValidator.Apply(m, m.Fixes.Single(f => f.Name == "Set " + copy.name + "'s Update Mode to Unscaled Time — every transition of " + copy.name + " changes"));
            Assert.That((copy.updateMode, EditorUtility.IsDirty(copy)), Is.EqualTo((AudioMixerUpdateMode.UnscaledTime, true)), "written by its serialized field");
            Want(unscaled, "FB-T08", new int[0], RuleSeverity.Info);
            Undo.PerformUndo();
            Assert.That(copy.updateMode, Is.EqualTo(AudioMixerUpdateMode.Normal), "one undo");
            Assert.That(Hash(original), Is.EqualTo(hash), "the package's test mixer unchanged");
        }

        static string Hash(string path)
        {
            using SHA256 sha = SHA256.Create();
            return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path)));
        }

        // ---- T09 — an endless container without Stop After (its fix) · with Stop After · Clips · Cycles · a clip are quiet
        [Test, Category(OptionalAssemblyTests.DevProjectCategory)]
        public void AR09_TheEndlessContainerRule()
        {
            AudioSource source = Source("AR09Source");
            AudioClip clip = Clip();
            AudioResource endless = Container(clip, "Infinite");
            var stopped = On(new AudioPlayAtPointItem { m_Resource = endless, m_StopAfter = 0.5f }, source.transform);
            FeedbackPlayer p = m_Fx.Player("AR09",
                On(new AudioPlayAtPointItem { m_Resource = endless }, source.transform),
                stopped,
                On(new AudioPlayAtPointItem { m_Resource = Container(clip, "Clips") }, source.transform),
                On(new AudioPlayAtPointItem { m_Resource = Container(clip, "Cycles") }, source.transform),
                On(new AudioPlayAtPointItem { m_Resource = clip }, source.transform));
            Want(p, "FB-T09", new[] { 0 }, RuleSeverity.Warning);
            Assert.That(Message(p, "FB-T09", 0), Is.EqualTo("An endless container without Stop After — its sounds play until a Revert or Restore()"));
            RuleResult r = F.Of(p, "FB-T09").Single();
            FeedbackValidator.Apply(r, r.Fixes.Single(f => f.Name == "Stop after 1 s"));
            Assert.That(((AudioPlayAtPointItem)p.m_Items[0]).m_StopAfter, Is.EqualTo(1f));
            Want(p, "FB-T09", new int[0], RuleSeverity.Warning);
        }

        // ---- the setting the rule reads is the one the engine started with — in the dev project both on, in a copy with Unity's
        // audio disabled both off (a restart applies the setting)
        [Test]
        public void AR10_TheProjectsSettingIsRead()
        {
            bool off = AudioRuleReads.AudioOff();
            Assert.That(off, Is.EqualTo(UnityEngine.AudioSettings.GetConfiguration().sampleRate == 0), "the setting · the running engine");
            Debug.Log("audio rule reads off: " + off);
        }
    }
}
