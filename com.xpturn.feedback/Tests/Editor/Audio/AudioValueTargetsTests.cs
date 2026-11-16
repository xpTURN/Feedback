using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using xpTURN.Feedback.Editor;
using Object = UnityEngine.Object;

namespace xpTURN.Feedback.Tests.Editor
{
    // the Audio value items — the composition and the category, the slots' ranges (a base outside comes back as it was), the
    // reverb's preset, the preview (the recorded rows · the low pass's own revert · the listener left alone), the low pass's
    // distance curve, the default motions and a project with audio disabled. The Audio assembly's types are not named: the
    // items play through players (DevProject — the dev project registers them). Direct Tick on test contexts · the preview on
    // the test's clock
    public sealed class AudioValueTargetsTests
    {
        const float Step = 0.05f;
        const string AudioSlots = ", xpTURN.Feedback.Audio";
        readonly List<Object> m_Made = new List<Object>();
        readonly List<FeedbackScheduler> m_S = new List<FeedbackScheduler>();
        readonly List<FeedbackPlayer> m_P = new List<FeedbackPlayer>();
        readonly List<string> m_Logs = new List<string>();
        readonly List<string> m_Errors = new List<string>();
        static double s_Now;
        Func<double> m_Clock;
        float m_Listener;

        [SetUp]
        public void SetUp()
        {
            m_Clock = PreviewClock.Source;
            PreviewClock.Source = () => s_Now;
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            m_Listener = AudioListener.volume;
            Application.logMessageReceived += OnLog;
        }

        [TearDown]
        public void TearDown()
        {
            Application.logMessageReceived -= OnLog;
            PreviewSession.Current?.Stop();
            foreach (FeedbackPlayer p in m_P)
                if (p != null) p.ReleaseRuntime();
            foreach (FeedbackScheduler s in m_S)
                if (!s.IsShutdown) s.Shutdown();
            foreach (Object o in m_Made)
                if (o != null) Object.DestroyImmediate(o);
            m_Made.Clear(); m_S.Clear(); m_P.Clear(); m_Logs.Clear(); m_Errors.Clear();
            AudioListener.volume = m_Listener;
            PreviewClock.Source = m_Clock;
        }

        void OnLog(string message, string stack, LogType type)
        {
            m_Logs.Add(message);
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
                m_Errors.Add(message);
        }

        int Logged(string part) => m_Logs.Count(m => m.Contains(part));

        static void Pump(int n) { for (int i = 0; i < n; i++) { s_Now += 1.0 / 120.0; PreviewSession.Current?.Update(); } }
        static void Tick(FeedbackScheduler s, int n) { for (int i = 0; i < n; i++) s.Tick(Step); }
        FeedbackScheduler Sched() { var s = new FeedbackScheduler(ContextKind.Test); m_S.Add(s); return s; }

        FeedbackPlayer Player(FeedbackScheduler s, params FeedbackItem[] items)
        {
            var go = new GameObject("Player");
            m_Made.Add(go);
            FeedbackPlayer p = go.AddComponent<FeedbackPlayer>();
            p.m_Items.AddRange(items);
            if (s != null)
            {
                p.Bind(s);
                p.EnsureRuntime();
            }
            m_P.Add(p);
            return p;
        }

        // a source not playing — a filter needs it first
        GameObject Sound(string name)
        {
            var go = new GameObject(name);
            m_Made.Add(go);
            go.AddComponent<AudioSource>().playOnAwake = false;
            return go;
        }

        static T With<T>(T item, FeedbackMotion motion, float duration, Object target) where T : TargetItem
        {
            item.m_Target = new TargetRef { m_Mode = TargetRefMode.Direct, m_Object = target, m_Path = "", m_Anchor = "" };
            item.m_Motion = motion;
            item.m_Timing.m_Duration = duration;
            return item;
        }

        // an offset held at its value — a flat curve at 1
        static CurveMotion Hold(float offset)
            => new CurveMotion { m_Params = new CurveParams { m_Curve = AnimationCurve.Constant(0f, 1f, 1f), m_RemapZero = Vector4.zero, m_RemapOne = Vector4.one * offset } };

        // keeps the play running — a Stop with Revert after a Tween's end reverts it · a preview runs past it
        static PositionItem Keeper(Component target, float d)
        {
            var k = new PositionItem { m_Target = new TargetRef { m_Mode = TargetRefMode.Direct, m_Object = target.transform, m_Path = "", m_Anchor = "" }, m_Motion = new PunchMotion { m_Params = new PunchParams { m_Amplitude = new Vector4(0.01f, 0, 0, 0), m_Vibrato = 1f, m_Decay = 0f } } };
            k.m_Timing.m_Duration = d;
            return k;
        }

        static SetMotion Set() => new SetMotion { m_Params = new SetParams { m_Value = new Vector4(1f, 0, 0, 0) } };

        static TweenMotion TweenTo(float v)
        {
            var m = new TweenMotion();
            m.m_Params.m_Mode = TweenMode.To;
            m.m_Params.m_To = new Vector4(v, 0, 0, 0);
            return m;
        }

        static int Bits(float f) => BitConverter.ToInt32(BitConverter.GetBytes(f), 0);

        static string F(float f) => f.ToString("R", CultureInfo.InvariantCulture);

        static string Keys(AnimationCurve c) => c == null ? "null" : string.Join(" ", c.keys.Select(k => F(k.time) + "," + F(k.value) + "," + F(k.inTangent) + "," + F(k.outTangent)));

        // a reverb's fourteen values
        static string Fourteen(AudioReverbFilter r)
            => string.Join(" ", new[] { r.dryLevel, r.room, r.roomHF, r.roomLF, r.decayTime, r.decayHFRatio, r.reflectionsLevel, r.reflectionsDelay, r.reverbLevel,
                r.reverbDelay, r.hfReference, r.lfReference, r.diffusion, r.density }.Select(F));

        static Dictionary<string, string> SnapOf(Object o)
        {
            var d = new Dictionary<string, string>();
            SerializedProperty it = new SerializedObject(o).GetIterator();
            while (it.Next(true))
            {
                switch (it.propertyType)
                {
                    case SerializedPropertyType.Float: d[it.propertyPath] = it.floatValue.ToString("R", CultureInfo.InvariantCulture); break;
                    case SerializedPropertyType.Integer: d[it.propertyPath] = it.longValue.ToString(CultureInfo.InvariantCulture); break;
                    case SerializedPropertyType.Boolean: d[it.propertyPath] = it.boolValue.ToString(); break;
                    case SerializedPropertyType.Enum: d[it.propertyPath] = it.intValue.ToString(CultureInfo.InvariantCulture); break;
                }
            }
            return d;
        }

        static string Diff(Dictionary<string, string> a, Dictionary<string, string> b)
            => string.Join(" · ", a.Where(kv => !b.TryGetValue(kv.Key, out string v) || v != kv.Value).Select(kv => kv.Key + " " + kv.Value + " → " + (b.TryGetValue(kv.Key, out string w) ? w : "none")));

        // ---- the composition — two offsets add · the engine's clamp read back · the base's bits · the category's scale halves an
        // offset, off leaves the item out · a Tween is a Persist: no scale, and a running one goes on when Audio turns off
        [Test, Category(OptionalAssemblyTests.DevProjectCategory)]
        public void AV01_VolumeOffsetsAddAndTheCategoryScalesThem()
        {
            FeedbackScheduler s = Sched();
            AudioSource a = Sound("V").GetComponent<AudioSource>();
            a.volume = 0.6f;
            FeedbackPlayer p1 = Player(s, With(new AudioVolumeItem(), Hold(-0.2f), 1f, a));
            FeedbackPlayer p2 = Player(s, With(new AudioVolumeItem(), Hold(-0.1f), 1f, a));
            p1.Play(); p2.Play(); Tick(s, 3);
            Assert.That(a.volume, Is.EqualTo(0.3f).Within(1e-5f), "two offsets add");
            p1.Stop(StopMode.Revert); p2.Stop(StopMode.Revert); s.Tick(0f);
            Assert.That(Bits(a.volume), Is.EqualTo(Bits(0.6f)), "the base's bits");
            FeedbackPlayer up = Player(s, With(new AudioVolumeItem(), Hold(0.3f), 1f, a), With(new AudioVolumeItem(), Hold(0.3f), 1f, a));
            up.Play(); Tick(s, 3);
            Assert.That(a.volume, Is.EqualTo(1f), "the engine keeps 1");
            up.Stop(StopMode.Revert); s.Tick(0f);
            Assert.That(Bits(a.volume), Is.EqualTo(Bits(0.6f)), "the base after the engine's clamp");
            s.Context.Categories.SetScale(FeedbackCategory.Audio, 0.5f);
            p1.Play(); Tick(s, 3);
            Assert.That(a.volume, Is.EqualTo(0.5f).Within(1e-5f), "the scale halves the offset");
            p1.Stop(StopMode.Revert); s.Tick(0f);
            s.Context.Categories.SetEnabled(FeedbackCategory.Audio, false);
            p1.Play(); Tick(s, 3);
            Assert.That(Bits(a.volume), Is.EqualTo(Bits(0.6f)), "off — the item is left out");
            p1.Stop(StopMode.Revert); s.Tick(0f);
            s.Context.Categories.SetEnabled(FeedbackCategory.Audio, true);
            FeedbackPlayer t = Player(s, With(new AudioVolumeItem(), TweenTo(0.2f), 0.2f, a), Keeper(a, 2f));
            t.Play(); Tick(s, 6);
            Assert.That(a.volume, Is.EqualTo(0.2f).Within(1e-5f), "a Tween takes no scale");
            t.Stop(StopMode.Revert); s.Tick(0f);
            Assert.That(a.volume, Is.EqualTo(0.6f).Within(1e-5f), "the Tween's start");
            t.Play(); Tick(s, 2);
            s.Context.Categories.SetEnabled(FeedbackCategory.Audio, false);
            Tick(s, 4);
            Assert.That(a.volume, Is.EqualTo(0.2f).Within(1e-5f), "a running Tween goes on when Audio turns off");
        }

        // ---- the slots' ranges — the pitch kept to ±3 (the engine takes 4) · a base of 4 back as it was · the pan's engine clamp ·
        // the listener's volume kept to 0 ~ 1 (the engine takes any) · a base of 1.5 back as it was
        [Test, Category(OptionalAssemblyTests.DevProjectCategory)]
        public void AV02_PitchPanAndTheListenerKeepTheirRanges()
        {
            FeedbackScheduler s = Sched();
            AudioSource a = Sound("P").GetComponent<AudioSource>();
            FeedbackPlayer up = Player(s, With(new AudioPitchItem(), Hold(3f), 1f, a));
            up.Play(); Tick(s, 3);
            Assert.That(a.pitch, Is.EqualTo(3f), "1 + 3 kept to 3");
            up.Stop(StopMode.Revert); s.Tick(0f);
            Assert.That(Bits(a.pitch), Is.EqualTo(Bits(1f)), "the base's bits");
            a.pitch = 4f;
            Assert.That(a.pitch, Is.EqualTo(4f), "the engine takes a base of 4");
            FeedbackPlayer down = Player(s, With(new AudioPitchItem(), Hold(-0.5f), 1f, a));
            down.Play(); Tick(s, 3);
            Assert.That(a.pitch, Is.EqualTo(3f), "4 − 0.5 kept to 3");
            down.Stop(StopMode.Revert); s.Tick(0f);
            Assert.That(Bits(a.pitch), Is.EqualTo(Bits(4f)), "a base outside the range comes back as it was");
            FeedbackPlayer pan = Player(s, With(new AudioStereoPanItem(), Hold(0.8f), 1f, a), With(new AudioStereoPanItem(), Hold(0.8f), 1f, a));
            pan.Play(); Tick(s, 3);
            Assert.That(a.panStereo, Is.EqualTo(1f), "the engine keeps 1");
            pan.Stop(StopMode.Revert); s.Tick(0f);
            Assert.That(Bits(a.panStereo), Is.EqualTo(Bits(0f)), "the pan's base");
            AudioListener.volume = 0.8f;
            FeedbackPlayer l = Player(s, With(new AudioListenerVolumeItem(), Hold(0.5f), 1f, null));
            l.Play(); Tick(s, 3);
            Assert.That(AudioListener.volume, Is.EqualTo(1f), "0.8 + 0.5 kept to 1");
            l.Stop(StopMode.Revert); s.Tick(0f);
            Assert.That(Bits(AudioListener.volume), Is.EqualTo(Bits(0.8f)), "the listener's base");
            AudioListener.volume = 1.5f;
            Assert.That(AudioListener.volume, Is.EqualTo(1.5f), "the engine takes a base of 1.5");
            FeedbackPlayer ld = Player(s, With(new AudioListenerVolumeItem(), Hold(-2f), 1f, null));
            ld.Play(); Tick(s, 3);
            Assert.That(AudioListener.volume, Is.EqualTo(0f), "1.5 − 2 kept to 0");
            ld.Stop(StopMode.Revert); s.Tick(0f);
            Assert.That(Bits(AudioListener.volume), Is.EqualTo(Bits(1.5f)), "a base outside the range comes back as it was");
        }

        // up and down past the range — the value kept to it, the base back; a base outside the range (the engine takes it) back as
        // it was
        void Kept(FeedbackScheduler s, Func<TargetItem> item, Component target, Func<float> read, Action<float> write, float up, float down, float min, float max, float outside)
        {
            string name = target.GetType().Name;
            float b = read();
            FeedbackPlayer pu = Player(s, With(item(), Hold(up), 1f, target));
            pu.Play(); Tick(s, 3);
            Assert.That(read(), Is.EqualTo(max), name + " up to " + F(max));
            pu.Stop(StopMode.Revert); s.Tick(0f);
            Assert.That(Bits(read()), Is.EqualTo(Bits(b)), name + " the base after up");
            FeedbackPlayer pd = Player(s, With(item(), Hold(down), 1f, target));
            pd.Play(); Tick(s, 3);
            Assert.That(read(), Is.EqualTo(min), name + " down to " + F(min));
            pd.Stop(StopMode.Revert); s.Tick(0f);
            Assert.That(Bits(read()), Is.EqualTo(Bits(b)), name + " the base after down");
            if (float.IsNaN(outside))
                return;
            write(outside);
            Assert.That(read(), Is.EqualTo(outside), name + " the engine takes " + F(outside));
            pu.Play(); Tick(s, 3);
            Assert.That(read(), Is.EqualTo(max), name + " from outside, kept");
            pu.Stop(StopMode.Revert); s.Tick(0f);
            Assert.That(Bits(read()), Is.EqualTo(Bits(outside)), name + " a base outside the range back as it was");
        }

        // ---- the filters — the slots keep the high pass, the wet mix, the distortion and the reverb level in range (the engine
        // takes any); the low pass's engine clamp read back, its base read (a new filter is 5007.73, not the inspector's 5000)
        [Test, Category(OptionalAssemblyTests.DevProjectCategory)]
        public void AV03_TheFiltersKeepTheirRanges()
        {
            FeedbackScheduler s = Sched();
            GameObject go = Sound("F");
            var high = go.AddComponent<AudioHighPassFilter>();
            var echo = go.AddComponent<AudioEchoFilter>();
            var dist = go.AddComponent<AudioDistortionFilter>();
            var rev = go.AddComponent<AudioReverbFilter>();
            var low = Sound("F2").AddComponent<AudioLowPassFilter>();
            Assert.That(rev.reverbPreset, Is.EqualTo(AudioReverbPreset.User), "a User reverb — no preset to turn");
            echo.wetMix = 0.5f;
            Kept(s, () => new AudioHighPassCutoffItem(), high, () => high.cutoffFrequency, v => high.cutoffFrequency = v, 30000f, -10000f, 10f, 22000f, 30000f);
            Kept(s, () => new AudioEchoWetMixItem(), echo, () => echo.wetMix, v => echo.wetMix = v, 1f, -1f, 0f, 1f, 1.5f);
            Kept(s, () => new AudioDistortionLevelItem(), dist, () => dist.distortionLevel, v => dist.distortionLevel = v, 1f, -1f, 0f, 1f, 1.5f);
            Kept(s, () => new AudioReverbLevelItem(), rev, () => rev.reverbLevel, v => rev.reverbLevel = v, 5000f, -20000f, -10000f, 2000f, 3000f);
            Assert.That(rev.reverbPreset, Is.EqualTo(AudioReverbPreset.User), "still User");
            Assert.That(low.cutoffFrequency, Is.EqualTo(5007.73f).Within(0.01f), "a new low pass reads 5007.73");
            Kept(s, () => new AudioLowPassCutoffItem(), low, () => low.cutoffFrequency, v => low.cutoffFrequency = v, 30000f, -10000f, 10f, 22000f, float.NaN);
        }

        // ---- the reverb's preset — Hallway turns to User while an offset plays and comes back with its fourteen values · a
        // Persist keeps User, and an offset's revert after it keeps the value · a filter at User from the start keeps its preset
        [Test, Category(OptionalAssemblyTests.DevProjectCategory)]
        public void AV04_TheReverbTurnsItsPresetAndBack()
        {
            FeedbackScheduler s = Sched();
            AudioReverbFilter r = Sound("R").AddComponent<AudioReverbFilter>();
            r.reverbPreset = AudioReverbPreset.Hallway;
            Assert.That(r.reverbPreset, Is.Not.EqualTo(AudioReverbPreset.User), "a preset to turn");
            string hallway = Fourteen(r);
            float level = r.reverbLevel;
            FeedbackPlayer p = Player(s, With(new AudioReverbLevelItem(), Hold(500f), 1f, r));
            p.Play(); Tick(s, 3);
            Assert.That(r.reverbPreset, Is.EqualTo(AudioReverbPreset.User), "turned to User");
            Assert.That(r.reverbLevel, Is.EqualTo(level + 500f).Within(1e-3f), "the offset written");
            p.Stop(StopMode.Revert); s.Tick(0f);
            Assert.That((r.reverbPreset, Fourteen(r)), Is.EqualTo((AudioReverbPreset.Hallway, hallway)), "the preset back with its fourteen");
            FeedbackPlayer t = Player(s, With(new AudioReverbLevelItem(), TweenTo(1000f), 0.2f, r));
            t.Play(); Tick(s, 6);
            Assert.That((r.reverbPreset, r.reverbLevel), Is.EqualTo((AudioReverbPreset.User, 1000f)), "a Persist keeps User");
            p.Play(); Tick(s, 3);
            Assert.That(r.reverbLevel, Is.EqualTo(1500f).Within(1e-3f), "an offset over the Persist");
            p.Stop(StopMode.Revert); s.Tick(0f);
            Assert.That((r.reverbPreset, r.reverbLevel), Is.EqualTo((AudioReverbPreset.User, 1000f)), "the offset's revert keeps the Persist");
            AudioReverbFilter u = Sound("U").AddComponent<AudioReverbFilter>();
            Assert.That(u.reverbPreset, Is.EqualTo(AudioReverbPreset.User), "a new filter is User");
            u.reverbLevel = 300f;
            FeedbackPlayer pu = Player(s, With(new AudioReverbLevelItem(), Hold(100f), 1f, u));
            pu.Play(); Tick(s, 3);
            Assert.That(u.reverbLevel, Is.EqualTo(400f).Within(1e-3f), "the offset on User");
            pu.Stop(StopMode.Revert); s.Tick(0f);
            Assert.That((u.reverbPreset, u.reverbLevel), Is.EqualTo((AudioReverbPreset.User, 300f)), "User kept · the base");
        }

        // ---- the preview — the slots' recorded rows (the low pass and the listener record none) · offsets and a reverb Persist
        // on Hallway seen, then every serialized value back, the preset back, the scene clean
        [Test, Category(OptionalAssemblyTests.DevProjectCategory)]
        public void AV05_ThePreviewPutsTheRecordedValuesBack()
        {
            foreach (var (slot, owner, path) in new[]
            {
                ("AudioVolumeSlot", typeof(AudioSource), "m_Volume"), ("AudioPitchSlot", typeof(AudioSource), "m_Pitch"), ("AudioStereoPanSlot", typeof(AudioSource), "Pan2D"),
                ("AudioHighPassCutoffSlot", typeof(AudioHighPassFilter), "m_CutoffFrequency"), ("AudioEchoWetMixSlot", typeof(AudioEchoFilter), "m_WetMix"),
                ("AudioDistortionLevelSlot", typeof(AudioDistortionFilter), "m_DistortionLevel"), ("AudioReverbLevelSlot", typeof(AudioReverbFilter), "m_ReverbLevel"),
            })
            {
                Type t = Type.GetType("xpTURN.Feedback." + slot + AudioSlots);
                Assert.That(t, Is.Not.Null, slot);
                var rows = (RecordPathsAttribute)Attribute.GetCustomAttribute(t, typeof(RecordPathsAttribute));
                Assert.That(rows, Is.Not.Null, slot + " records");
                Assert.That(rows.Owner, Is.EqualTo(owner), slot + " owner");
                Assert.That(rows.Paths, Is.EqualTo(new[] { path }), slot + " paths");
            }
            foreach (string slot in new[] { "AudioLowPassCutoffSlot", "AudioListenerVolumeSlot" })
                Assert.That(Attribute.IsDefined(Type.GetType("xpTURN.Feedback." + slot + AudioSlots), typeof(NotRecordedAttribute)), Is.True, slot);
            GameObject go = Sound("Pre");
            AudioSource a = go.GetComponent<AudioSource>();
            var high = go.AddComponent<AudioHighPassFilter>();
            var echo = go.AddComponent<AudioEchoFilter>();
            var dist = go.AddComponent<AudioDistortionFilter>();
            var rev = go.AddComponent<AudioReverbFilter>();
            rev.reverbPreset = AudioReverbPreset.Hallway;
            Component[] all = { a, high, echo, dist, rev };
            Dictionary<string, string>[] before = all.Select(SnapOf).ToArray();
            string hallway = Fourteen(rev);
            FeedbackPlayer player = Player(null, With(new AudioVolumeItem(), Hold(-0.3f), 1f, a), With(new AudioPitchItem(), Hold(0.5f), 1f, a),
                With(new AudioStereoPanItem(), Hold(0.5f), 1f, a), With(new AudioHighPassCutoffItem(), Hold(1000f), 1f, high),
                With(new AudioEchoWetMixItem(), Hold(-0.5f), 1f, echo), With(new AudioDistortionLevelItem(), Hold(0.3f), 1f, dist),
                With(new AudioReverbLevelItem(), TweenTo(1000f), 0.1f, rev));
            s_Now += 1.0;
            Assert.That(PreviewSession.Start(player, default), Is.EqualTo(PreviewRefusal.None));
            Pump(24);
            float[] seen = { a.volume, a.pitch, a.panStereo, high.cutoffFrequency, echo.wetMix, dist.distortionLevel };
            float[] offsets = { 0.7f, 1.5f, 0.5f, 6000f, 0.5f, 0.8f };
            for (int i = 0; i < seen.Length; i++)
                Assert.That(seen[i], Is.EqualTo(offsets[i]).Within(1e-4f), "the offset in the preview — " + i);
            Assert.That((rev.reverbPreset, rev.reverbLevel), Is.EqualTo((AudioReverbPreset.User, 1000f)), "the reverb's Persist on User");
            PreviewSession.Current?.Stop();
            for (int i = 0; i < all.Length; i++)
                Assert.That(Diff(before[i], SnapOf(all[i])), Is.Empty, all[i].GetType().Name + " serialized");
            Assert.That((rev.reverbPreset, Fourteen(rev)), Is.EqualTo((AudioReverbPreset.Hallway, hallway)), "the preset back with its fourteen");
            Assert.That(SceneManager.GetActiveScene().isDirty, Is.False, "clean");
        }

        // ---- the low pass in the preview — nothing recorded: an offset's revert and a Persist's first value bring the cutoff
        // and its one-key curve back (the base is the value read, 5007.73 on a new filter)
        [Test, Category(OptionalAssemblyTests.DevProjectCategory)]
        public void AV06_TheLowPassPutsItselfBack()
        {
            AudioLowPassFilter low = Sound("L").AddComponent<AudioLowPassFilter>();
            float b = low.cutoffFrequency;
            string keys = Keys(low.customCutoffCurve);
            Assert.That(low.customCutoffCurve.length, Is.EqualTo(1), "the one-key curve");
            FeedbackPlayer off = Player(null, With(new AudioLowPassCutoffItem(), Hold(-3000f), 1f, low));
            s_Now += 1.0;
            Assert.That(PreviewSession.Start(off, default), Is.EqualTo(PreviewRefusal.None));
            Pump(24);
            Assert.That(low.cutoffFrequency, Is.EqualTo(b - 3000f).Within(0.5f), "the offset in the preview");
            PreviewSession.Current?.Stop();
            Assert.That((Bits(low.cutoffFrequency), Keys(low.customCutoffCurve)), Is.EqualTo((Bits(b), keys)), "the offset's revert — the base and the curve");
            FeedbackPlayer persist = Player(null, With(new AudioLowPassCutoffItem(), TweenTo(500f), 0.1f, low), Keeper(low, 1f));
            s_Now += 1.0;
            Assert.That(PreviewSession.Start(persist, default), Is.EqualTo(PreviewRefusal.None));
            Pump(24);
            Assert.That(low.cutoffFrequency, Is.EqualTo(500f).Within(0.5f), "the Tween's end in the preview");
            PreviewSession.Current?.Stop();
            Assert.That((Bits(low.cutoffFrequency), Keys(low.customCutoffCurve)), Is.EqualTo((Bits(b), keys)), "the Persist's first value — the base and the curve");
            Assert.That(SceneManager.GetActiveScene().isDirty, Is.False, "clean");
        }

        // ---- the listener's volume — prepared in the preview and not written there (the editor's whole sound); written on a test
        // context
        [Test, Category(OptionalAssemblyTests.DevProjectCategory)]
        public void AV07_TheListenerIsLeftAloneInThePreview()
        {
            AudioListener.volume = 0.7f;
            FeedbackPlayer p = Player(null, With(new AudioListenerVolumeItem(), Hold(-0.5f), 1f, null));
            s_Now += 1.0;
            Assert.That(PreviewSession.Start(p, default), Is.EqualTo(PreviewRefusal.None));
            Pump(24);
            Assert.That(p.Runtime?.Binding(0), Is.Not.Null, "prepared in the preview");
            Assert.That(Bits(AudioListener.volume), Is.EqualTo(Bits(0.7f)), "not written in the preview");
            PreviewSession.Current?.Stop();
            Assert.That(Bits(AudioListener.volume), Is.EqualTo(Bits(0.7f)), "after the preview");
            FeedbackScheduler s = Sched();
            FeedbackPlayer q = Player(s, With(new AudioListenerVolumeItem(), Hold(-0.5f), 1f, null));
            q.Play(); Tick(s, 3);
            Assert.That(AudioListener.volume, Is.EqualTo(0.2f).Within(1e-5f), "written on a test context");
        }

        // ---- a low pass that follows a distance curve (keys set as the source's 3D Low-Pass curve sets them) is refused with
        // one reason and its curve is left · the one-key curve is taken
        [Test, Category(OptionalAssemblyTests.DevProjectCategory)]
        public void AV08_ALowPassOnADistanceCurveIsRefused()
        {
            FeedbackScheduler s = Sched();
            AudioLowPassFilter low = Sound("C").AddComponent<AudioLowPassFilter>();
            low.customCutoffCurve = new AnimationCurve(new Keyframe(0f, 0.8f), new Keyframe(1f, 0.2f));
            string keys = Keys(low.customCutoffCurve);
            Assert.That(low.customCutoffCurve.length, Is.EqualTo(2), "a distance curve");
            FeedbackPlayer p = Player(s, With(new AudioLowPassCutoffItem(), Hold(-1000f), 1f, low));
            Assert.That(p.Runtime.Binding(0), Is.Null, "refused");
            Assert.That(Logged("distance curve"), Is.EqualTo(1), "one reason");
            p.Play(); Tick(s, 3);
            Assert.That(Keys(low.customCutoffCurve), Is.EqualTo(keys), "the curve as it was");
            AudioLowPassFilter one = Sound("C1").AddComponent<AudioLowPassFilter>();
            Assert.That(Player(s, With(new AudioLowPassCutoffItem(), Hold(-1000f), 1f, one)).Runtime.Binding(0), Is.Not.Null, "a one-key curve is taken");
        }

        // ---- the frame's revert keeps the composition while contributions remain (the mixer takes a pitch, a filter's value and
        // the listener's volume when they are written — a base written at the frame's start would be heard until the apply) · a
        // contribution that ends between the revert and the apply leaves the base · another's write between them is the new base
        [Test, Category(OptionalAssemblyTests.DevProjectCategory)]
        public void AV11_TheRevertKeepsTheCompositionUntilTheApply()
        {
            FeedbackScheduler s = Sched();
            GameObject go = Sound("H");
            AudioSource a = go.GetComponent<AudioSource>();
            var low = go.AddComponent<AudioLowPassFilter>();
            var high = go.AddComponent<AudioHighPassFilter>();
            var echo = go.AddComponent<AudioEchoFilter>();
            var dist = go.AddComponent<AudioDistortionFilter>();
            var rev = go.AddComponent<AudioReverbFilter>();
            rev.reverbPreset = AudioReverbPreset.Hallway;
            echo.wetMix = 0.5f;
            var cases = new (Func<TargetItem> Item, Object Target, Func<float> Read, Action<float> Write, float Offset)[]
            {
                (() => new AudioVolumeItem(), a, () => a.volume, v => a.volume = v, -0.25f),
                (() => new AudioPitchItem(), a, () => a.pitch, v => a.pitch = v, 0.5f),
                (() => new AudioStereoPanItem(), a, () => a.panStereo, v => a.panStereo = v, 0.5f),
                (() => new AudioLowPassCutoffItem(), low, () => low.cutoffFrequency, v => low.cutoffFrequency = v, -2000f),
                (() => new AudioHighPassCutoffItem(), high, () => high.cutoffFrequency, v => high.cutoffFrequency = v, 1000f),
                (() => new AudioEchoWetMixItem(), echo, () => echo.wetMix, v => echo.wetMix = v, 0.25f),
                (() => new AudioDistortionLevelItem(), dist, () => dist.distortionLevel, v => dist.distortionLevel = v, 0.25f),
                (() => new AudioReverbLevelItem(), rev, () => rev.reverbLevel, v => rev.reverbLevel = v, 500f),
                (() => new AudioListenerVolumeItem(), null, () => AudioListener.volume, v => AudioListener.volume = v, -0.25f),
            };
            AudioListener.volume = 1f;
            foreach (var c in cases)
            {
                string n = c.Item().GetType().Name;
                float b = c.Read();
                FeedbackPlayer p = Player(s, With(c.Item(), Hold(c.Offset), 1f, c.Target));
                p.Play(); Tick(s, 3);
                float composed = c.Read();
                Assert.That(composed, Is.EqualTo(b + c.Offset).Within(Math.Abs(c.Offset) * 1e-3f), n + " composed");
                s.RunRevert();
                Assert.That(Bits(c.Read()), Is.EqualTo(Bits(composed)), n + " the revert keeps the composition");
                s.RunUpdate(new FrameTime(Step, Step));
                s.RunLateApply();
                Assert.That(Bits(c.Read()), Is.EqualTo(Bits(composed)), n + " the apply writes it again");
                if (c.Target == rev)
                    Assert.That(rev.reverbPreset, Is.EqualTo(AudioReverbPreset.User), n + " User while it plays");
                s.RunRevert();
                p.Stop(StopMode.Revert);
                s.RunUpdate(new FrameTime(Step, Step));
                s.RunLateApply();
                Assert.That(Bits(c.Read()), Is.EqualTo(Bits(b)), n + " ended between the revert and the apply — the base");
                FeedbackPlayer q = Player(s, With(c.Item(), Hold(c.Offset), 1f, c.Target));
                q.Play(); Tick(s, 3);
                s.RunRevert();
                float other = b + c.Offset * 0.5f;
                c.Write(other);
                s.RunUpdate(new FrameTime(Step, Step));
                s.RunLateApply();
                Assert.That(c.Read(), Is.EqualTo(other + c.Offset).Within(Math.Abs(c.Offset) * 1e-3f), n + " another's write is the new base");
                q.Stop(StopMode.Revert); s.Tick(0f);
                // the reverb turns its preset back at the end — the preset's own level replaces another's
                float end = c.Target == rev ? b : other;
                Assert.That(c.Read(), Is.EqualTo(end).Within(Math.Abs(c.Offset) * 1e-3f), n + " back to another's write");
                c.Write(b);
            }
            Assert.That(rev.reverbPreset, Is.EqualTo(AudioReverbPreset.Hallway), "the reverb's preset back");
        }

        // a new component's value · the Tween's end · the punch's (and the curve's) swing · the shake's · the oscillation's · the
        // spring's bump · the range
        static readonly (Type Item, float New, float To, float Punch, float Shake, float Oscillate, float Bump, float Min, float Max)[] Defaults =
        {
            (typeof(AudioVolumeItem), 1f, 0f, -0.5f, 0.25f, 0.25f, -30f, 0f, 1f),
            (typeof(AudioPitchItem), 1f, 2f, 0.5f, 0.1f, 0.1f, 30f, -3f, 3f),
            (typeof(AudioStereoPanItem), 0f, -1f, 0.5f, 0.5f, 1f, 30f, -1f, 1f),
            (typeof(AudioLowPassCutoffItem), 5007.73f, 500f, -2000f, 0f, 2000f, -125000f, 10f, 22000f),
            (typeof(AudioHighPassCutoffItem), 5000f, 2000f, 1000f, 0f, 1000f, 62500f, 10f, 22000f),
            (typeof(AudioEchoWetMixItem), 1f, 0f, -0.5f, 0f, 0.5f, -30f, 0f, 1f),
            (typeof(AudioDistortionLevelItem), 0.5f, 1f, 0.5f, 0f, 0.25f, 30f, 0f, 1f),
            (typeof(AudioReverbLevelItem), 0f, 2000f, 1000f, 0f, 1000f, 62500f, -10000f, 2000f),
            (typeof(AudioListenerVolumeItem), 1f, 0f, -0.5f, 0f, 0f, -30f, 0f, 1f),
        };

        // ---- the default motions — heard from a new component's own values (read here) and inside the range
        [Test]
        public void AV09_TheDefaultMotionsAreHeardFromANewComponent()
        {
            GameObject go = Sound("New");
            AudioSource a = go.GetComponent<AudioSource>();
            var news = new Dictionary<Type, float>
            {
                { typeof(AudioVolumeItem), a.volume }, { typeof(AudioPitchItem), a.pitch }, { typeof(AudioStereoPanItem), a.panStereo },
                { typeof(AudioLowPassCutoffItem), go.AddComponent<AudioLowPassFilter>().cutoffFrequency }, { typeof(AudioHighPassCutoffItem), go.AddComponent<AudioHighPassFilter>().cutoffFrequency },
                { typeof(AudioEchoWetMixItem), go.AddComponent<AudioEchoFilter>().wetMix }, { typeof(AudioDistortionLevelItem), go.AddComponent<AudioDistortionFilter>().distortionLevel },
                { typeof(AudioReverbLevelItem), go.AddComponent<AudioReverbFilter>().reverbLevel }, { typeof(AudioListenerVolumeItem), 1f },
            };
            foreach (var d in Defaults)
            {
                string n = d.Item.Name;
                TargetTraits traits = ((TargetItem)Activator.CreateInstance(d.Item)).Traits;
                Assert.That(news[d.Item], Is.EqualTo(d.New).Within(0.01f), n + " a new component's value");
                float to = ((TweenMotion)MotionDefaults.Create(d.Item, MotionKind.Tween, out _)).m_Params.m_To.x;
                Assert.That((to, to != d.New, to >= d.Min && to <= d.Max), Is.EqualTo((d.To, true, true)), n + " the Tween's end — heard, in range");
                float punch = ((PunchMotion)MotionDefaults.Create(d.Item, MotionKind.Punch, out _)).m_Params.m_Amplitude.x;
                float curve = ((CurveMotion)MotionDefaults.Create(d.Item, MotionKind.Curve, out _)).m_Params.m_RemapOne.x;
                Assert.That((punch, curve, d.New + punch >= d.Min && d.New + punch <= d.Max), Is.EqualTo((d.Punch, d.Punch, true)), n + " the punch's and the curve's swing — in range");
                if (traits.Supports(MotionKind.Shake))
                    Assert.That(((ShakeMotion)MotionDefaults.Create(d.Item, MotionKind.Shake, out _)).m_Params.m_Amplitude.x, Is.EqualTo(d.Shake), n + " the shake");
                if (traits.Supports(MotionKind.Oscillate))
                    Assert.That(((OscillateMotion)MotionDefaults.Create(d.Item, MotionKind.Oscillate, out _)).m_Params.m_Amplitude.x, Is.EqualTo(d.Oscillate), n + " the oscillation");
                Assert.That(((SpringMotion)MotionDefaults.Create(d.Item, MotionKind.Spring, out _)).m_Params.m_Value.x, Is.EqualTo(d.Bump), n + " the spring's bump");
            }
        }

        // ---- a project with Unity's audio disabled (Project Settings > Audio) — every item (the values, the two actions and the
        // mixer's two) is refused with one reason, its property left, no error; with audio on the same items are taken. The disabled branch runs in a copy of the dev project
        // with audio disabled — the output line names the branch
        [Test, Category(OptionalAssemblyTests.DevProjectCategory)]
        public void AV10_AProjectWithAudioDisabledSkipsTheItems()
        {
            bool off = AudioSettings.GetConfiguration().sampleRate == 0;
            TestContext.WriteLine("audio off: " + off);
            FeedbackScheduler s = Sched();
            GameObject go = Sound("Off");
            AudioSource a = go.GetComponent<AudioSource>();
            var low = go.AddComponent<AudioLowPassFilter>();
            var high = go.AddComponent<AudioHighPassFilter>();
            var echo = go.AddComponent<AudioEchoFilter>();
            var dist = go.AddComponent<AudioDistortionFilter>();
            var rev = go.AddComponent<AudioReverbFilter>();
            Component[] all = { a, low, high, echo, dist, rev };
            Dictionary<string, string>[] before = all.Select(SnapOf).ToArray();
            float listener = AudioListener.volume;
            AudioClip clip = AudioClip.Create("off", 4410, 1, 44100, false);
            m_Made.Add(clip);
            var mixer = AssetDatabase.LoadAssetAtPath<UnityEngine.Audio.AudioMixer>("Packages/com.xpturn.feedback/Tests/Editor/Audio/FeedbackTestMixer.mixer");
            Assert.That(mixer, Is.Not.Null, "the test mixer");
            TargetItem[] items =
            {
                With(new AudioVolumeItem(), Hold(-0.5f), 1f, a), With(new AudioPitchItem(), Hold(0.5f), 1f, a), With(new AudioStereoPanItem(), Hold(0.5f), 1f, a),
                With(new AudioLowPassCutoffItem(), Hold(-1000f), 1f, low), With(new AudioHighPassCutoffItem(), Hold(1000f), 1f, high),
                With(new AudioEchoWetMixItem(), Hold(-0.5f), 1f, echo), With(new AudioDistortionLevelItem(), Hold(0.3f), 1f, dist),
                With(new AudioReverbLevelItem(), Hold(500f), 1f, rev), With(new AudioListenerVolumeItem(), Hold(-0.5f), 1f, null),
                With(new AudioSourceControlItem(), Set(), 0f, a), With(new AudioPlayClipItem { m_Clip = clip }, Set(), 0f, a),
                With(new AudioMixerParameterItem { m_Mixer = mixer, m_Parameter = "FeedbackTestVolume" }, Hold(-5f), 1f, null),
                With(new AudioMixerSnapshotItem { m_Snapshot = mixer.FindSnapshot("B"), m_BackTo = mixer.FindSnapshot("A") }, Set(), 0f, null),
                With(new AudioPlayAtPointItem { m_Resource = clip }, Set(), 0f, go.transform),
            };
            foreach (TargetItem item in items)
            {
                FeedbackPlayer p = Player(s, item);
                Assert.That(p.Runtime.Binding(0) == null, Is.EqualTo(off), item.GetType().Name + (off ? " refused" : " taken"));
                if (off)
                    p.Play();
            }
            Assert.That(Logged("audio is disabled"), Is.EqualTo(off ? items.Length : 0), "one reason each");
            if (!off)
                return;
            Tick(s, 3);
            for (int i = 0; i < all.Length; i++)
                Assert.That(Diff(before[i], SnapOf(all[i])), Is.Empty, all[i].GetType().Name + " left");
            Assert.That(AudioListener.volume, Is.EqualTo(listener), "the listener left");
            Assert.That(m_Errors, Is.Empty, "no error");
        }
    }
}
