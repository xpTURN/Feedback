using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace xpTURN.Feedback.Tests.Editor
{
    // the runtime reads the saved data, it does not write it: the player's whole serialized data (managed references entered ·
    // values as bits · object references as identity — JSON writes scene references as 0, a false green) the same after the
    // prepare, mid play, each end and the release. The representative nine and one instance in two slots. SetAnchor writes
    // m_Anchors by contract — it is not on these paths; the format upgrade happens on load
    public sealed class PlayerReadOnlyTests
    {
        static readonly List<string> s_Warnings = new List<string>();
        readonly List<Object> m_Made = new List<Object>();
        readonly List<FeedbackScheduler> m_Schedulers = new List<FeedbackScheduler>();

        static void OnLog(string condition, string stack, LogType type)
        {
            if (type != LogType.Log)
                s_Warnings.Add(type + ": " + condition);
        }

        [SetUp]
        public void SetUp()
        {
            s_Warnings.Clear();
            PlayerRuntime.ResetSession();
            Application.logMessageReceived += OnLog;
        }

        [TearDown]
        public void TearDown()
        {
            Application.logMessageReceived -= OnLog;
            foreach (Object o in m_Made)
            {
                if (o is FeedbackPlayer player && player != null)
                    player.ReleaseRuntime();
            }
            foreach (Object o in m_Made)
            {
                if (o != null && !(o is Component))
                    Object.DestroyImmediate(o);
            }
            m_Made.Clear();
            foreach (FeedbackScheduler s in m_Schedulers)
            {
                if (!s.IsShutdown)
                    s.Shutdown();
            }
            m_Schedulers.Clear();
        }

        // every property, entering managed references: path = value (floats as bits, objects by identity)
        static List<string> Snapshot(Object target)
        {
            var lines = new List<string>();
            var so = new SerializedObject(target);
            SerializedProperty p = so.GetIterator();
            while (p.Next(true))
            {
                string v;
                switch (p.propertyType)
                {
                    case SerializedPropertyType.ObjectReference:
                        v = p.objectReferenceValue != null ? "ref " + System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(p.objectReferenceValue).ToString(CultureInfo.InvariantCulture) + " " + p.objectReferenceValue.name : "null";
                        break;
                    case SerializedPropertyType.ManagedReference: v = "mref " + p.managedReferenceId + " " + p.managedReferenceFullTypename; break;
                    case SerializedPropertyType.Float: v = BitConverter.SingleToInt32Bits(p.floatValue).ToString(CultureInfo.InvariantCulture); break;
                    case SerializedPropertyType.Integer:
                    case SerializedPropertyType.Enum: v = p.longValue.ToString(CultureInfo.InvariantCulture); break;
                    case SerializedPropertyType.Boolean: v = p.boolValue.ToString(); break;
                    case SerializedPropertyType.String: v = p.stringValue; break;
                    case SerializedPropertyType.Vector2: v = p.vector2Value.ToString("R"); break;
                    case SerializedPropertyType.Vector3: v = p.vector3Value.ToString("R"); break;
                    case SerializedPropertyType.Vector4: v = p.vector4Value.ToString("R"); break;
                    case SerializedPropertyType.Color: v = p.colorValue.ToString("R"); break;
                    case SerializedPropertyType.AnimationCurve: v = p.animationCurveValue != null ? string.Join(";", p.animationCurveValue.keys.Select(k => k.time.ToString("R") + ":" + k.value.ToString("R"))) : "null"; break;
                    default: v = p.propertyType.ToString(); break;
                }
                lines.Add(p.propertyPath + " = " + v);
            }
            return lines;
        }

        static List<string> Diff(List<string> before, List<string> now)
        {
            var diff = before.Zip(now, (a, b) => a == b ? null : a + " → " + b).Where(d => d != null).ToList();
            if (before.Count != now.Count)
                diff.Add("lines " + before.Count + " → " + now.Count);
            return diff;
        }

        GameObject Go(string name, bool cube = false)
        {
            GameObject go = cube ? GameObject.CreatePrimitive(PrimitiveType.Cube) : new GameObject(name);
            go.name = name;
            m_Made.Add(go);
            return go;
        }

        static TargetRef Direct(Object o) => new TargetRef { m_Mode = TargetRefMode.Direct, m_Object = o, m_Path = "", m_Anchor = "" };
        static TargetRef Self => new TargetRef { m_Mode = TargetRefMode.Self, m_Path = "", m_Anchor = "" };

        static T Item<T>(FeedbackMotion motion, TargetRef target, float duration = 0.5f) where T : TargetItem, new()
        {
            var item = new T { m_Motion = motion, m_Target = target };
            item.m_Timing.m_Duration = duration;
            return item;
        }

        static void Tick(FeedbackScheduler s, int n)
        {
            for (int i = 0; i < n; i++)
                s.Tick(0.02f);
        }

        // the representative nine on a cube, the first in a second slot too (one instance)
        FeedbackPlayer Representative(out FeedbackScheduler s)
        {
            s = new FeedbackScheduler(ContextKind.Test);
            m_Schedulers.Add(s);
            GameObject owner = Go("owner", cube: true);
            GameObject child = Go("child");
            child.transform.SetParent(owner.transform, false);
            Light light = Go("light").AddComponent<Light>();
            Camera cam = Go("camera").AddComponent<Camera>();
            cam.enabled = false;
            var player = owner.AddComponent<FeedbackPlayer>();
            m_Made.Add(player);
            var shared = Item<PositionItem>(new PunchMotion { m_Params = new PunchParams { m_Amplitude = Vector4.one, m_Vibrato = 5f, m_Decay = 1f } }, Self);
            player.m_Items.Add(shared);
            player.m_Items.Add(Item<RotationItem>(new ShakeMotion { m_Params = new ShakeParams { m_Amplitude = Vector4.one * 10f, m_Frequency = 10f } }, Self));
            player.m_Items.Add(Item<ScaleItem>(new TweenMotion { m_Params = new TweenParams { m_Mode = TweenMode.To, m_From = Vector4.one, m_To = Vector4.one * 2f, m_Ease = Ease.Of(EaseType.Linear), m_Loops = 1 } }, Self));
            player.m_Items.Add(Item<MaterialColorItem>(new FlashMotion { m_Params = new FlashParams { m_Target = new Vector4(1, 0, 0, 1), m_Attack = 0.1f, m_Hold = 0.1f, m_Release = 0.1f } }, Self));
            player.m_Items.Add(Item<LightIntensityItem>(new OscillateMotion { m_Params = new OscillateParams { m_Amplitude = Vector4.one, m_Frequency = 1f } }, Direct(light)));
            player.m_Items.Add(Item<SetActiveItem>(new BlinkMotion { m_Params = new BlinkParams { m_Value = Vector4.one, m_Period = 0.1f, m_Duty = 0.5f, m_StartOn = true } }, Direct(child)));
            player.m_Items.Add(Item<CameraShakeItem>(new ShakeMotion { m_Params = new ShakeParams { m_Amplitude = Vector4.one, m_Frequency = 10f } }, Direct(cam)));
            player.m_Items.Add(new UnityEventItem());
            player.m_Items.Add(new HitStopItem { m_FreezeDuration = 0.05f });
            player.m_Items.Add(shared);
            player.Bind(s);
            return player;
        }

        sealed class Listener : IPlaybackListener
        {
            public void OnPlaybackEnded(PlaybackHandle handle, PlaybackEnd end)
            {
            }
        }

        [Test]
        public void FE06_PlaybackLeavesTheSavedDataAlone([Values("hold", "revert", "complete", "end")] string end)
        {
            var fails = new List<string>();
            FeedbackPlayer player = Representative(out FeedbackScheduler s);
            List<string> before = Snapshot(player);
            if (!player.EnsureRuntime() || player.Runtime.ItemCount != 10)
                fails.Add("not prepared");
            var points = new List<(string, List<string>)> { ("prepare", Snapshot(player)) };
            PlaybackHandle h = player.Play();
            Tick(s, 5);
            if (h == default(PlaybackHandle) || !player.IsPlaying)
                fails.Add("not playing");
            points.Add(("mid", Snapshot(player)));
            // the public API on the way (read-only by contract — SetAnchor is the one that writes): pause · seek · signal ·
            // a listener · a second Play · a rebuild and a new play · skip · restore
            player.Pause();
            Tick(s, 1);
            player.Resume();
            player.Seek(0.3f);
            player.Signal();
            player.SetListener(h, new Listener());
            player.Play();
            Tick(s, 2);
            player.Rebuild();
            h = player.Play();
            Tick(s, 2);
            player.Skip();
            player.Restore();
            h = player.Play();
            Tick(s, 3);
            points.Add(("api", Snapshot(player)));
            if (end == "end")
                Tick(s, 60);
            else
                player.Stop(end == "hold" ? StopMode.Hold : end == "revert" ? StopMode.Revert : StopMode.Complete);
            Tick(s, 2);
            if (player.IsPlaying)
                fails.Add("still playing");
            points.Add(("after", Snapshot(player)));
            player.ReleaseRuntime();
            points.Add(("release", Snapshot(player)));
            foreach ((string label, List<string> now) in points)
            {
                List<string> diff = Diff(before, now);
                if (diff.Count > 0)
                    fails.Add(label + ": " + diff.Count + " differ — " + string.Join(" | ", diff.Take(4)));
            }
            if (before.Count < 500)
                fails.Add("only " + before.Count + " lines");
            if (s_Warnings.Any(w => !w.Contains("static Collider") && !w.Contains("Signal found no play waiting")))
                fails.Add("warnings: " + string.Join(" | ", s_Warnings.Take(3)));
            Assert.That(fails, Is.Empty);
        }

        // the link: the representative nine moved into a preset two players link — the preset's data and the players' the same
        // after the prepares, the plays (the public API on the way), the ends and the releases. The items are read by both
        // players at once
        [Test]
        public void FE06b_ALinkedPlaybackLeavesThePresetAlone([Values("hold", "end")] string end)
        {
            var fails = new List<string>();
            FeedbackPlayer a = Representative(out FeedbackScheduler s);
            var preset = ScriptableObject.CreateInstance<FeedbackPreset>();
            m_Made.Add(preset);
            preset.m_Items.AddRange(a.m_Items);
            a.m_Items.Clear();
            a.m_Preset = preset;
            GameObject second = Go("second", cube: true);
            var b = second.AddComponent<FeedbackPlayer>();
            m_Made.Add(b);
            b.m_Preset = preset;
            b.Bind(s);
            List<string> before = Snapshot(preset);
            List<string> beforeA = Snapshot(a);
            if (!a.EnsureRuntime() || !b.EnsureRuntime() || a.Runtime.ItemCount != 10 || b.Runtime.ItemCount != 10)
                fails.Add("not prepared");
            var points = new List<(string, List<string>)> { ("prepare", Snapshot(preset)) };
            a.Play();
            b.Play();
            Tick(s, 5);
            points.Add(("mid", Snapshot(preset)));
            a.Pause();
            Tick(s, 1);
            a.Resume();
            a.Seek(0.3f);
            b.Rebuild();
            b.Play();
            Tick(s, 2);
            a.Skip();
            a.Restore();
            a.Play();
            Tick(s, 3);
            points.Add(("api", Snapshot(preset)));
            if (end == "end")
                Tick(s, 60);
            else
            {
                a.Stop(StopMode.Hold);
                b.Stop(StopMode.Hold);
            }
            Tick(s, 2);
            points.Add(("after", Snapshot(preset)));
            a.ReleaseRuntime();
            b.ReleaseRuntime();
            points.Add(("release", Snapshot(preset)));
            foreach ((string label, List<string> now) in points)
            {
                List<string> diff = Diff(before, now);
                if (diff.Count > 0)
                    fails.Add(label + ": " + diff.Count + " differ — " + string.Join(" | ", diff.Take(4)));
            }
            if (Diff(beforeA, Snapshot(a)).Count > 0)
                fails.Add("the player's own data changed");
            if (before.Count < 500)
                fails.Add("only " + before.Count + " lines");
            if (s_Warnings.Any(w => !w.Contains("static Collider") && !w.Contains("Signal found no play waiting")))
                fails.Add("warnings: " + string.Join(" | ", s_Warnings.Take(3)));
            Assert.That(fails, Is.Empty);
        }

        [Test]
        public void FE07_TheSnapshotSeesValuesAndReferences()
        {
            var fails = new List<string>();
            GameObject owner = Go("owner");
            Light a = Go("lightA").AddComponent<Light>();
            Light b = Go("lightB").AddComponent<Light>();
            var player = owner.AddComponent<FeedbackPlayer>();
            m_Made.Add(player);
            var item = Item<LightIntensityItem>(new OscillateMotion { m_Params = new OscillateParams { m_Amplitude = Vector4.one, m_Frequency = 1f } }, Direct(a));
            player.m_Items.Add(item);
            player.m_Items.Add(item);
            List<string> before = Snapshot(player);
            item.m_Timing.m_Duration += 1f;
            List<string> value = Snapshot(player);
            item.m_Target.m_Object = b;
            List<string> reference = Snapshot(player);
            int dv = Diff(before, value).Count, dr = Diff(value, reference).Count;
            if (dv != 2 || dr < 2)
                fails.Add("value " + dv + " · reference " + dr + " lines differ");
            Assert.That(fails, Is.Empty);
        }
    }
}
