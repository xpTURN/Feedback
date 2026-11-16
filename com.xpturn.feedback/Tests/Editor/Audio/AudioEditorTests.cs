using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using xpTURN.Feedback.Editor;
using Object = UnityEngine.Object;

namespace xpTURN.Feedback.Tests.Editor
{
    // the audio items in the editor — the asset fields of their types (the item keeps an Object), the Mixer Parameter's name
    // picked from its mixer's exposed list, the debugger's notes of the sounds a play started (Edit mode plays sounds). The notes
    // need the implementation (DevProject); a mixer the tests rename is a copy
    public sealed class AudioEditorTests
    {
        const string Mixers = "Packages/com.xpturn.feedback/Tests/Editor/Audio/";
        const string Dir = "Assets/AudioEditorTests";
        readonly List<Object> m_Made = new List<Object>();
        readonly List<FeedbackScheduler> m_S = new List<FeedbackScheduler>();
        readonly List<FeedbackPlayer> m_P = new List<FeedbackPlayer>();

        [SetUp]
        public void SetUp()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var listener = new GameObject("Listener", typeof(AudioListener));
            m_Made.Add(listener);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (FeedbackPlayer p in m_P)
                if (p != null) p.ReleaseRuntime();
            foreach (FeedbackScheduler s in m_S)
                if (!s.IsShutdown) s.Shutdown();
            foreach (Object o in m_Made)
                if (o != null) Object.DestroyImmediate(o);
            AssetDatabase.DeleteAsset(Dir);
            m_Made.Clear(); m_S.Clear(); m_P.Clear();
        }

        static AudioMixer Mixer(string name) => AssetDatabase.LoadAssetAtPath<AudioMixer>(Mixers + name + ".mixer");

        AudioClip Clip(int samples)
        {
            var clip = AudioClip.Create("an", samples, 1, 44100, false);
            m_Made.Add(clip);
            return clip;
        }

        FeedbackPlayer Player(params FeedbackItem[] items)
        {
            var go = new GameObject("Player");
            m_Made.Add(go);
            var p = go.AddComponent<FeedbackPlayer>();
            p.m_Items.AddRange(items);
            return p;
        }

        static T On<T>(T item, Object target) where T : TargetItem
        {
            item.m_Target = new TargetRef { m_Mode = TargetRefMode.Direct, m_Object = target, m_Path = "", m_Anchor = "" };
            item.m_Motion = new SetMotion { m_Params = new SetParams { m_Value = new Vector4(1f, 0, 0, 0) } };
            return item;
        }

        ItemBody Body(FeedbackPlayer player, int item)
        {
            var so = new SerializedObject(player);
            var body = new ItemBody();
            body.Build(so.FindProperty("m_Items.Array.data[" + item + "]"), "AudioEditorTests." + item, player);
            return body;
        }

        // ---- the asset fields: each of its type, no scene objects, bound to its property · the name field · a value of another
        // type set by script stays in the property
        [Test]
        public void AN01_TheAssetFieldsAreOfTheirTypes()
        {
            var tex = new Texture2D(2, 2);
            m_Made.Add(tex);
            FeedbackPlayer p = Player(new AudioPlayClipItem { m_Clip = tex }, new AudioPlayAtPointItem(), new AudioMixerParameterItem(), new AudioMixerSnapshotItem());
            var want = new Dictionary<string, System.Type>
            {
                { "m_Items.Array.data[0].m_Clip", typeof(AudioClip) },
                { "m_Items.Array.data[1].m_Resource", typeof(AudioResource) },
                { "m_Items.Array.data[1].m_Output", typeof(AudioMixerGroup) },
                { "m_Items.Array.data[2].m_Mixer", typeof(AudioMixer) },
                { "m_Items.Array.data[3].m_Snapshot", typeof(AudioMixerSnapshot) },
                { "m_Items.Array.data[3].m_BackTo", typeof(AudioMixerSnapshot) },
            };
            var got = new Dictionary<string, System.Type>();
            for (int i = 0; i < 4; i++)
            {
                foreach (ObjectField f in Body(p, i).Query<ObjectField>().ToList())
                {
                    if (f.bindingPath != null && want.ContainsKey(f.bindingPath))
                    {
                        Assert.That(f.allowSceneObjects, Is.False, f.bindingPath);
                        got[f.bindingPath] = f.objectType;
                    }
                }
            }
            Assert.That(got, Is.EquivalentTo(want), "every asset field of its type");
            Assert.That(Body(p, 2).Q<AudioMixerParameterField>(), Is.Not.Null, "the Mixer Parameter's name field");
            Assert.That(((AudioPlayClipItem)p.m_Items[0]).m_Clip, Is.SameAs(tex), "a value of another type stays");
        }

        // ---- the name field: the mixer's exposed names · a pick writes the name · no mixer: none · a rename in the mixer's
        // serialized data shows at once
        [Test]
        public void AN02_TheNameFieldPicksFromTheMixer()
        {
            var item = new AudioMixerParameterItem { m_Mixer = Mixer("FeedbackTestMixer") };
            FeedbackPlayer p = Player(item);
            var so = new SerializedObject(p);
            var field = new AudioMixerParameterField(so.FindProperty("m_Items.Array.data[0]"), "m_Parameter", "Parameter");
            Assert.That(field.Candidates(), Is.EqualTo(new[] { "FeedbackTestVolume", "FeedbackTestPitch" }));
            field.Choose("FeedbackTestPitch");
            Assert.That(item.m_Parameter, Is.EqualTo("FeedbackTestPitch"));
            item.m_Mixer = null;
            so.Update();
            Assert.That(field.Candidates(), Is.Empty, "no mixer");
            if (!AssetDatabase.IsValidFolder(Dir))
                AssetDatabase.CreateFolder("Assets", "AudioEditorTests");
            string copyPath = Dir + "/AN02.mixer";
            Assert.That(AssetDatabase.CopyAsset(Mixers + "FeedbackTestMixer.mixer", copyPath), Is.True);
            var copy = AssetDatabase.LoadAssetAtPath<AudioMixer>(copyPath);
            var mixer = new SerializedObject(copy);
            mixer.FindProperty("m_ExposedParameters").GetArrayElementAtIndex(0).FindPropertyRelative("name").stringValue = "Renamed";
            mixer.ApplyModifiedPropertiesWithoutUndo();
            item.m_Mixer = copy;
            so.Update();
            Assert.That(field.Candidates(), Is.EqualTo(new[] { "Renamed", "FeedbackTestPitch" }), "the rename at once");
        }

        AudioSource Source(int samples)
        {
            var go = new GameObject("Source");
            m_Made.Add(go);
            var s = go.AddComponent<AudioSource>();
            s.playOnAwake = false;
            s.spatialBlend = 0f;
            s.clip = Clip(samples);
            return s;
        }

        FeedbackPlayer Bound(FeedbackScheduler s, params FeedbackItem[] items) => Bound(s, null, items);

        FeedbackPlayer Bound(FeedbackScheduler s, System.Action<FeedbackPlayer> set, params FeedbackItem[] items)
        {
            FeedbackPlayer p = Player(items);
            set?.Invoke(p);
            var keeper = new PositionItem
            {
                m_Target = new TargetRef { m_Mode = TargetRefMode.Self, m_Path = "", m_Anchor = "" },
                m_Motion = new PunchMotion { m_Params = new PunchParams { m_Amplitude = new Vector4(0.01f, 0, 0, 0), m_Vibrato = 1f, m_Decay = 0f } },
            };
            keeper.m_Timing.m_Duration = 3f;
            p.m_Items.Add(keeper);
            p.Bind(s);
            p.EnsureRuntime();
            m_P.Add(p);
            return p;
        }

        FeedbackScheduler Sched()
        {
            var s = new FeedbackScheduler(ContextKind.Test);
            m_S.Add(s);
            return s;
        }

        static string Note(FeedbackPlayer p, int item, int record) => p.Runtime.Binding(item).DebugNote(record);

        // ---- Source Control's note: sounding · paused with the player · again sounding · none after a Revert · none once the
        // sound reached its end (its mark stays)
        [UnityTest, Category(OptionalAssemblyTests.DevProjectCategory)]
        public IEnumerator AN03_TheSourceControlsNote()
        {
            yield return null;
            FeedbackScheduler s = Sched();
            AudioSource a = Source(44100 * 4);
            FeedbackPlayer p = Bound(s, On(new AudioSourceControlItem { m_Action = AudioSourceAction.Play }, a));
            p.Play(); s.Tick(0f);
            Assert.That(a.isPlaying, Is.True, "the engine plays");
            Assert.That(Note(p, 0, 0), Is.EqualTo("sounding"));
            p.Pause(); s.Tick(0f);
            Assert.That(Note(p, 0, 0), Is.EqualTo("paused with the player"));
            p.Resume(); s.Tick(0f);
            Assert.That(Note(p, 0, 0), Is.EqualTo("sounding"));
            p.Stop(StopMode.Revert); s.Tick(0f);
            Assert.That(Note(p, 0, 0), Is.Null, "after a Revert");
            AudioSource shortOne = Source(2205);
            FeedbackPlayer q = Bound(s, On(new AudioSourceControlItem { m_Action = AudioSourceAction.Play }, shortOne));
            q.Play(); s.Tick(0f);
            Assert.That(Note(q, 0, 0), Is.EqualTo("sounding"));
            var wall = Stopwatch.StartNew();
            while (shortOne.isPlaying && wall.Elapsed.TotalSeconds < 2.0)
                yield return null;
            Assert.That((shortOne.isPlaying, q.Runtime.Serial(0) >= 0), Is.EqualTo((false, true)), "the sound ended, its play runs on");
            Assert.That(Note(q, 0, 0), Is.Null, "a sound at its end");
            Assert.That(Note(q, 1, 0), Is.Null, "a value item has none");
        }

        // ---- Play At Point's note: sounding · paused · none after a Revert · two plays — only the one with a voice sounds
        [Test, Category(OptionalAssemblyTests.DevProjectCategory)]
        public void AN04_ThePlayAtPointsNote()
        {
            FeedbackScheduler s = Sched();
            var target = new GameObject("Target");
            m_Made.Add(target);
            var point = On(new AudioPlayAtPointItem { m_Resource = Clip(44100 * 4) }, target.transform);
            FeedbackPlayer p = Bound(s, point);
            p.Play(); s.Tick(0f);
            Assert.That(Note(p, 0, 0), Is.EqualTo("sounding"));
            p.Pause(); s.Tick(0f);
            Assert.That(Note(p, 0, 0), Is.EqualTo("paused"));
            p.Resume(); s.Tick(0f);
            Assert.That(Note(p, 0, 0), Is.EqualTo("sounding"));
            p.Stop(StopMode.Revert); s.Tick(0f);
            Assert.That(Note(p, 0, 0), Is.Null, "after a Revert");
            var twice = On(new AudioPlayAtPointItem { m_Resource = Clip(44100 * 4) }, target.transform);
            FeedbackPlayer q = Bound(s, x => { x.m_Policy = PlayPolicy.Overlap; x.m_MaxOverlap = 2; }, twice);
            q.Play(PlayArgs.Default.WithIntensity(0f)); s.Tick(0f);
            q.Play(); s.Tick(0f);
            var notes = new[] { Note(q, 0, 0), Note(q, 0, 1) };
            Assert.That(notes.Where(n => n != null), Is.EqualTo(new[] { "sounding" }), "the silent play's record has none");
        }
    }
}
