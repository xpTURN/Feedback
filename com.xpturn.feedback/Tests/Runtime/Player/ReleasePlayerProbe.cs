using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace xpTURN.Feedback.Tests
{
    // a probe that runs only with "-feedbackReleaseProbe <file>" — a non-development player built with the test assemblies
    // (tools/check-player-gates.sh); with no argument it returns at once. ① every registered target item × each motion it does
    // not take: the prepare refuses and keeps no reason string — the joined reason is the development build's only, at each
    // site that joins one (value targets · SetActive · the camera shake) ② the exit's order in a player: the exit token,
    // OnApplicationQuit, quitting and the plays' ends ③ a Volume Blend without a profile refused with no reason · the PostFX
    // overlay on with a Via Scripting camera before any render (-nographics renders none) — no error (URP's types by name)
    // ④ an impulse from no source refused with no reason · an activation's blend installs the blend override and the
    // Default's close takes it back (Cinemachine's types by name) ⑤ a rumble's Player reference that finds no PlayerInput
    // refused with no reason · a rumble on a recording gamepad driven while playing and (0, 0) after the Default's close (the
    // Input System's test assembly by name — absent where a release clone drops the Input System)
    static class ReleasePlayerProbe
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Run()
        {
            string[] args = Environment.GetCommandLineArgs();
            int at = Array.IndexOf(args, "-feedbackReleaseProbe");
            if (at < 0 || at + 1 >= args.Length)
                return;
            var go = new GameObject("release probe");
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.AddComponent<ReleasePlayerProbeDriver>().Path = args[at + 1];
        }
    }

    sealed class ReleasePlayerProbeDriver : MonoBehaviour, IPlaybackListener
    {
        internal string Path;
        FieldInfo m_BlendOverride;
        MethodInfo m_RumbleLast;

        void Write(string line) => File.AppendAllText(Path, line + "\n");

        public void OnPlaybackEnded(PlaybackHandle handle, PlaybackEnd end)
            => Write("end " + end + " default=" + (FeedbackScheduler.Default.IsShutdown ? "closed" : "open"));

        IEnumerator Start()
        {
            Write("start debug=" + Debug.isDebugBuild
#if DEBUG
                  + " DEBUG"
#endif
            );
            // ① the refused pairs — SetActive's Punch first, then all of them
            var target = new TargetRef { m_Mode = TargetRefMode.Self, m_Path = "", m_Anchor = "" };
            var context = new PrepareContext(gameObject, null, FeedbackScheduler.Default.Context, 1);
            var item = new SetActiveItem { m_Motion = new PunchMotion(), m_Target = target };
            ItemBinding binding = FeedbackImplementations.Create(item);
            bool prepared = binding.Prepare(in context);
            Write("refused prepared=" + prepared + " reason=" + (binding.SkipReason == null ? "null" : "'" + binding.SkipReason + "'"));
            binding.Release();
            var motions = new List<Type>();
            foreach (Type t in typeof(FeedbackMotion).Assembly.GetTypes())
            {
                if (t.IsSubclassOf(typeof(FeedbackMotion)) && !t.IsAbstract)
                    motions.Add(t);
            }
            int pairs = 0, joined = 0;
            string first = null;
            var sites = new SortedSet<string>(StringComparer.Ordinal);
            foreach (Type itemType in FeedbackImplementations.RegisteredTypes)
            {
                if (!itemType.IsSubclassOf(typeof(TargetItem)) || itemType.IsAbstract)
                    continue;
                foreach (Type motionType in motions)
                {
                    var pairItem = (TargetItem)Activator.CreateInstance(itemType);
                    var motion = (FeedbackMotion)Activator.CreateInstance(motionType);
                    if (pairItem.Traits.Supports(motion.Kind))
                        continue;
                    pairItem.m_Motion = motion;
                    pairItem.m_Target = target;
                    ItemBinding pair = FeedbackImplementations.Create(pairItem);
                    pair.Prepare(in context);
                    pairs++;
                    if (pair.SkipReason != null)
                    {
                        joined++;
                        sites.Add(pair.GetType().Name);
                        if (first == null)
                            first = pair.SkipReason;
                    }
                    pair.Release();
                }
            }
            Write("refused pairs=" + pairs + " joined=" + joined + " sites=" + string.Join(",", sites) + (first != null ? " first='" + first + "'" : ""));
            // ③ the blend · the overlay before a render
            ItemBinding blend = FeedbackImplementations.Create(new PostFXVolumeBlendItem { m_Motion = new TweenMotion(), m_Target = target });
            if (blend == null)
                Write("postfx absent");
            else
            {
                bool blendPrepared = blend.Prepare(in context);
                Write("postfx refused prepared=" + blendPrepared + " reason=" + (blend.SkipReason == null ? "null" : "'" + blend.SkipReason + "'"));
                blend.Release();
            }
            int errors = 0;
            string firstError = null;
            Application.LogCallback count = (condition, stack, type) =>
            {
                if (type != LogType.Error && type != LogType.Exception)
                    return;
                errors++;
                firstError ??= condition;
            };
            Type cameraData = Type.GetType(PostFXNames.CameraData);
            if (blend != null && cameraData != null)
            {
                var camera = new GameObject("postfx camera");
                DontDestroyOnLoad(camera);
                camera.AddComponent<Camera>();
                Component data = camera.AddComponent(cameraData);
                FieldInfo mode = cameraData.GetField("m_VolumeFrameworkUpdateModeOption", BindingFlags.NonPublic | BindingFlags.Instance);
                mode?.SetValue(data, Enum.ToObject(mode.FieldType, 1));
                var overlay = new GameObject("postfx");
                DontDestroyOnLoad(overlay);
                var po = overlay.AddComponent<FeedbackPlayer>();
                var vignette = new PostFXVignetteItem { m_Motion = new FlashMotion { m_Params = FlashParams.Default } };
                vignette.m_Timing.m_Duration = 100f;
                po.m_Items.Add(vignette);
                Application.logMessageReceived += count;
                po.Play();
            }
            // ④ the impulse · the activation's blend override
            FieldInfo blendOverride = Type.GetType("Unity.Cinemachine.CinemachineCore, Unity.Cinemachine")?.GetField("GetBlendOverride", BindingFlags.Public | BindingFlags.Static);
            ItemBinding impulse = FeedbackImplementations.Create(new CinemachineImpulseItem { m_Mode = CinemachineImpulseMode.Source });
            if (impulse == null)
                Write("cinemachine absent");
            else
            {
                bool impulsePrepared = impulse.Prepare(in context);
                Write("cinemachine refused prepared=" + impulsePrepared + " reason=" + (impulse.SkipReason == null ? "null" : "'" + impulse.SkipReason + "'"));
                impulse.Release();
                var vcam = new GameObject("cinemachine camera");
                DontDestroyOnLoad(vcam);
                vcam.AddComponent(Type.GetType(CinemachineNames.Camera));
                var pv = vcam.AddComponent<FeedbackPlayer>();
                var activate = new CinemachineActivateCameraItem { m_Motion = new SetMotion(), m_Target = target };
                activate.m_Blend.m_Override = true;
                pv.m_Items.Add(activate);
                pv.Play();
            }
            m_BlendOverride = impulse != null ? blendOverride : null;
            // ⑤ the rumble's refusal · a rumble on a recording gamepad
            ItemBinding rumble = FeedbackImplementations.Create(new GamepadRumbleItem { m_Device = GamepadRumbleDevice.Player, m_Motion = new FlashMotion() });
            Type rumbleProbe = Type.GetType("xpTURN.Feedback.Tests.RumbleReleaseProbe, xpTURN.Feedback.Tests.InputSystem");
            if (rumble == null || rumbleProbe == null)
                Write("rumble absent");
            else
            {
                bool rumblePrepared = rumble.Prepare(in context);
                Write("rumble refused prepared=" + rumblePrepared + " reason=" + (rumble.SkipReason == null ? "null" : "'" + rumble.SkipReason + "'"));
                rumble.Release();
                var pr = new GameObject("rumble player");
                DontDestroyOnLoad(pr);
                rumbleProbe.GetMethod("Begin", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { pr });
                m_RumbleLast = rumbleProbe.GetMethod("Last", BindingFlags.Static | BindingFlags.NonPublic);
            }
            // ② two plays: one with the exit token and a listener, one with a listener only
            var a = new GameObject("a");
            DontDestroyOnLoad(a);
            var pa = a.AddComponent<FeedbackPlayer>();
            var longItem = new PositionItem { m_Motion = new PunchMotion(), m_Target = target };
            longItem.m_Timing.m_Duration = 100f;
            pa.m_Items.Add(longItem);
            pa.Play(PlayArgs.Default.WithListener(this), Application.exitCancellationToken);
            var b = new GameObject("b");
            DontDestroyOnLoad(b);
            var pb = b.AddComponent<FeedbackPlayer>();
            var longItem2 = new PositionItem { m_Motion = new PunchMotion(), m_Target = target };
            longItem2.m_Timing.m_Duration = 100f;
            pb.m_Items.Add(longItem2);
            pb.Play(PlayArgs.Default.WithListener(this));
            Application.exitCancellationToken.Register(() => Write("exit token default=" + (FeedbackScheduler.Default.IsShutdown ? "closed" : "open")));
            Application.quitting += () => Write("quitting");
            for (int i = 0; i < 5; i++)
                yield return null;
            Application.logMessageReceived -= count;
            if (blend != null)
                Write("postfx errors=" + errors + " initialized=" + Initialized() + (firstError != null ? " first='" + firstError + "'" : ""));
            if (m_BlendOverride != null)
                Write("cinemachine override=" + Owner(m_BlendOverride));
            if (m_RumbleLast != null)
                Write("rumble playing last=" + m_RumbleLast.Invoke(null, null));
            Write("quit");
            Application.Quit();
        }

        // the blend override's owner — its target's type, or "null"
        static string Owner(FieldInfo field)
        {
            var d = field.GetValue(null) as Delegate;
            return d == null ? "null" : d.Target != null ? d.Target.GetType().Name : d.Method.Name;
        }

        // Core RP's volume manager by name — "unknown" when its members are gone
        static string Initialized()
        {
            Type manager = Type.GetType("UnityEngine.Rendering.VolumeManager, Unity.RenderPipelines.Core.Runtime");
            object instance = manager?.GetProperty("instance", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
            object up = instance != null ? manager.GetProperty("isInitialized", BindingFlags.Public | BindingFlags.Instance)?.GetValue(instance) : null;
            return up != null ? up.ToString() : "unknown";
        }

        void OnApplicationQuit() => Write("OnApplicationQuit default=" + (FeedbackScheduler.Default.IsShutdown ? "closed" : "open"));

        void OnDestroy()
        {
            if (m_BlendOverride != null)
                Write("cinemachine closed override=" + Owner(m_BlendOverride));
            if (m_RumbleLast != null)
                Write("rumble closed last=" + m_RumbleLast.Invoke(null, null));
            Write("OnDestroy default=" + (FeedbackScheduler.Default.IsShutdown ? "closed" : "open"));
        }
    }
}
