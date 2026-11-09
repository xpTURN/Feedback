using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.LowLevel;
using UnityEngine.PlayerLoop;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace xpTURN.Feedback
{
    // runs FeedbackScheduler.Default's three stages at the front of EarlyUpdate · PreLateUpdate · PostLateUpdate, and its
    // lifetime. SubsystemRegistration closes the last instance and makes a new one, AfterSceneLoad installs, quitting closes.
    // In the editor EnteredEditMode closes again (a backup), beforeAssemblyReload closes, and a recompile during play makes a
    // new instance and installs again
    /// <summary>Runs the feedback stages in Unity's PlayerLoop.</summary>
    public static class FeedbackPlayerLoop
    {
        struct FeedbackRevert { }
        struct FeedbackUpdate { }
        struct FeedbackLateApply { }

        internal const string MissingPhaseWarning = "Feedback could not find a PlayerLoop phase to install into; that stage does not run.";

        // once per domain — subscribing again each session would put the handlers left from the last session ahead of ours
        static bool s_Subscribed;

        /// <summary>Puts the feedback stages back at the front of the current PlayerLoop — call it after code that changes the PlayerLoop after the first frame; calling it again is safe. Main thread only.</summary>
        public static void Reinstall()
        {
            PlayerLoopSystem loop = PlayerLoop.GetCurrentPlayerLoop();
            bool all = Install(ref loop);
            PlayerLoop.SetPlayerLoop(loop);
#if DEBUG
            if (!all)
                Debug.LogWarning(MissingPhaseWarning);
#endif
        }

        // removes ours from every list at every depth, then puts them at index 0 of the three phases (top level). A missing
        // phase is skipped; true when all three were found
        internal static bool Install(ref PlayerLoopSystem loop)
        {
            Remove(ref loop);
            bool revert = Insert(ref loop, typeof(EarlyUpdate), typeof(FeedbackRevert), OnRevert);
            bool update = Insert(ref loop, typeof(PreLateUpdate), typeof(FeedbackUpdate), OnUpdate);
            bool lateApply = Insert(ref loop, typeof(PostLateUpdate), typeof(FeedbackLateApply), OnLateApply);
            return revert && update && lateApply;
        }

        static bool IsOurs(Type type) => type == typeof(FeedbackRevert) || type == typeof(FeedbackUpdate) || type == typeof(FeedbackLateApply);

        // a copy left in another phase would run that stage twice a frame
        static void Remove(ref PlayerLoopSystem system)
        {
            if (system.subSystemList == null)
                return;
            List<PlayerLoopSystem> kept = new List<PlayerLoopSystem>(system.subSystemList.Length);
            foreach (PlayerLoopSystem child in system.subSystemList)
            {
                if (IsOurs(child.type))
                    continue;
                PlayerLoopSystem copy = child;
                Remove(ref copy);
                kept.Add(copy);
            }
            system.subSystemList = kept.ToArray();
        }

        static bool Insert(ref PlayerLoopSystem loop, Type phase, Type type, PlayerLoopSystem.UpdateFunction callback)
        {
            if (loop.subSystemList == null)
                return false;
            for (int i = 0; i < loop.subSystemList.Length; i++)
            {
                if (loop.subSystemList[i].type != phase)
                    continue;
                PlayerLoopSystem[] list = loop.subSystemList[i].subSystemList ?? Array.Empty<PlayerLoopSystem>();
                PlayerLoopSystem[] result = new PlayerLoopSystem[list.Length + 1];
                result[0] = new PlayerLoopSystem { type = type, updateDelegate = callback };
                Array.Copy(list, 0, result, 1, list.Length);
                loop.subSystemList[i].subSystemList = result;
                return true;
            }
            return false;
        }

        // ---- stages. The editor runs the loop in edit mode too — ours stay in it after play. Default is not null
        // during play: SubsystemRegistration makes it before the install, and a domain reload takes ours out of the loop

        static void OnRevert()
        {
#if UNITY_EDITOR
            if (!Application.isPlaying)
                return;
#endif
            FeedbackScheduler.Default.RunRevert();
        }

        static void OnUpdate()
        {
#if UNITY_EDITOR
            if (!Application.isPlaying)
                return;
#endif
            // the unscaled dt is cut where the game cuts the scaled one — a load or a hitch does not jump an Unscaled play
            // (Time.maximumDeltaTime, as it is now). The settings' cap when it is set — the Default's settings are read at
            // the first loop frame
            FeedbackScheduler scheduler = FeedbackScheduler.Default;
            float cap = scheduler.Context.Settings.MaximumUnscaledDelta;
            scheduler.RunUpdate(new FrameTime(Time.deltaTime, Mathf.Min(Time.unscaledDeltaTime, cap > 0f ? cap : Time.maximumDeltaTime)));
        }

        static void OnLateApply()
        {
#if UNITY_EDITOR
            if (!Application.isPlaying)
                return;
#endif
            FeedbackScheduler.Default.RunLateApply();
        }

        // ---- lifetime. Other code at SubsystemRegistration must not touch Default — the order inside it is not set

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void OnSubsystemRegistration() => Initialize();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void OnAfterSceneLoad() => Reinstall();

        // the last instance closes first, so a listener that plays on its abort is refused, not moved into the new session
        static void Initialize()
        {
            FeedbackScheduler.Default?.Shutdown();
            FeedbackScheduler.Default = new FeedbackScheduler(ContextKind.Runtime);
            if (s_Subscribed)
                return;
            s_Subscribed = true;
            Application.quitting += OnQuitting;
#if UNITY_EDITOR
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            AssemblyReloadEvents.beforeAssemblyReload += OnBeforeAssemblyReload;
#endif
        }

        // the hooks are multicast — ours never throw, so the handlers after us still run
        static void ShutdownDefault()
        {
            try
            {
                FeedbackScheduler.Default?.Shutdown();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        static void OnQuitting() => ShutdownDefault();

#if UNITY_EDITOR
        // a backup — quitting has closed it unless a handler before ours threw
        static void OnPlayModeStateChanged(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.EnteredEditMode)
                ShutdownDefault();
        }

        static void OnBeforeAssemblyReload() => ShutdownDefault();

        // a recompile during play: the reload made a new PlayerLoop without ours and left Default null. Components'
        // OnEnable comes after this and sees the new instance; delayCall takes the front back before the next frame from
        // another installer at this point (UniTask). Nothing here runs outside play — import workers call this too
        [InitializeOnLoadMethod]
        static void OnEditorLoad()
        {
            if (!EditorApplication.isPlaying)
                return;
            Initialize();
            Reinstall();
            EditorApplication.delayCall += Reinstall;
        }
#endif
    }
}
