using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace xpTURN.Feedback.Editor
{
    // what the overlay and the bar's status line tell, in the order shown
    internal enum PreviewNoticeKind
    {
        Locked,
        Unrecorded,
        HitStop,
        TimeScale,
        UnityEvents,
        // the signals not sent · the players the flow items play, bound to the session
        SignalsSkipped,
        NestedPlayers,
        PhysicsMarks,
        PrefabChild,
        SignalWait,
        SubjectOff,
        CameraGameView,
        CameraMainScene,
        NoMainCamera,
        NoReceivers,
        CategoryOff,
        CategoryScale,
        FlashLimit,
        FlashHeld,
        ShakeReceivers,
        ExecuteAlways,
        // the screen items' overlay canvas
        ScreenGameView,
        // the PostFX items — the Scene view's Post Processing toggle (or wireframe) · the main camera's post-processing and
        // volume mask · the effects only the Game view (or play) draws
        PostFXSceneView,
        PostFXCamera,
        PostFXGameView,
        // a camera shake on a camera Cinemachine drives whose live camera has no shake extension (the brain writes the camera
        // over the shake) · an activation of a Cinemachine camera — play only
        CinemachineNoExtension,
        CinemachinePlayOnly,
        // the preview sends no gamepad command
        RumblePlayOnly,
        // a user's feedback the preview does not run · one it runs (what its code changes does not come back)
        CustomSkipped,
        CustomUnrecorded,
    }

    internal readonly struct PreviewNotice
    {
        internal readonly PreviewNoticeKind Kind;
        internal readonly string Text;

        internal PreviewNotice(PreviewNoticeKind kind, string text)
        {
            Kind = kind;
            Text = text;
        }
    }

    // collected from the session when drawn — nothing kept. The settings compare the asset's values (play) with the
    // preview's (the defaults), for the categories of the subject's items only
    internal static class PreviewNotices
    {
        // the last Scene view shows image effects — a seam (the batch has no Scene view)
        internal static Func<bool> SceneViewShowsPostFX = () =>
        {
            SceneView view = SceneView.lastActiveSceneView;
            return view == null || view.sceneViewState.imageEffectsEnabled && view.cameraMode.drawMode != DrawCameraMode.Wireframe;
        };

        // URP's camera data by name (the editor has no URP reference)
        static readonly Type s_CameraData = Type.GetType(PostFXNames.CameraData);
        static readonly PropertyInfo s_RenderPostProcessing = s_CameraData?.GetProperty("renderPostProcessing", BindingFlags.Public | BindingFlags.Instance);
        static readonly PropertyInfo s_VolumeLayerMask = s_CameraData?.GetProperty("volumeLayerMask", BindingFlags.Public | BindingFlags.Instance);

        internal static void Collect(PreviewSession session, List<PreviewNotice> notices)
        {
            notices.Clear();
            if (session == null)
                return;
            notices.Add(new PreviewNotice(PreviewNoticeKind.Locked, "Timeline and Animation window previews wait while this preview holds AnimationMode."));
            foreach (System.Type type in session.Unrecorded)
                notices.Add(new PreviewNotice(PreviewNoticeKind.Unrecorded, type.Name + " has no record paths — only its animatable properties come back."));
            FeedbackContext context = session.Scheduler.Context;
            TimeScaleService timeScale = context.TimeScaleIfMade;
            if (timeScale != null && timeScale.Count > 0)
                notices.Add(new PreviewNotice(PreviewNoticeKind.HitStop, $"Hit Stop {timeScale.RemainingAt(timeScale.Count - 1):0.00}s — the preview clock is slowed, the scene is not."));
            // the open request — under a hit stop, which wins
            else if (timeScale != null && timeScale.HasOpen)
                notices.Add(new PreviewNotice(PreviewNoticeKind.TimeScale, $"Time Scale {timeScale.Effective:0.##} — the preview clock is slowed, the scene is not."));
            if (context.SkippedUnityEvents > 0)
                notices.Add(new PreviewNotice(PreviewNoticeKind.UnityEvents, context.SkippedUnityEvents + " UnityEvent call(s) not made in the preview."));
            if (context.SkippedSignals > 0)
                notices.Add(new PreviewNotice(PreviewNoticeKind.SignalsSkipped, context.SkippedSignals + " signal(s) not sent in the preview."));
            if (context.SkippedCustom > 0)
                notices.Add(new PreviewNotice(PreviewNoticeKind.CustomSkipped, context.SkippedCustom + " custom feedback call(s) not made in the preview."));
            if (context.RanCustom > 0)
                notices.Add(new PreviewNotice(PreviewNoticeKind.CustomUnrecorded, context.RanCustom + " custom feedback(s) run in the preview — what their code changes does not come back."));
            if (session.Adopted.Count > 0)
                notices.Add(new PreviewNotice(PreviewNoticeKind.NestedPlayers, "Plays " + session.Adopted.Count + " other player(s) — their targets come back at the end too."));
            if (context.Marks != null)
                notices.Add(new PreviewNotice(PreviewNoticeKind.PhysicsMarks, "Physics forces are shown as arrows — the preview does not simulate physics."));
            if (session.Player != null)
            {
                foreach (FeedbackItem item in session.Player.Items)
                {
                    if (PreviewMasks.PrefabChild(session.Player, item))
                        notices.Add(new PreviewNotice(PreviewNoticeKind.PrefabChild, "Set Parent of a prefab instance's child is left out — edit mode cannot move it, play does."));
                }
            }
            int untimed = 0;
            int waits = session.Player != null ? session.Player.OpenSignalWaits(out untimed) : 0;
            if (waits > 0)
                notices.Add(new PreviewNotice(PreviewNoticeKind.SignalWait, "Waiting for a signal (" + waits + ") — " + (untimed > 0 ? "Signal; no timeout." : "Signal, or the timeout.")));
            if (session.Subject is Behaviour behaviour && !behaviour.isActiveAndEnabled)
                notices.Add(new PreviewNotice(PreviewNoticeKind.SubjectOff, "In play this " + behaviour.GetType().Name + " runs only while it is on."));

            var items = new List<FeedbackItem>();
            if (session.Player != null)
                items.AddRange(session.Player.Items);
            else if (session.MotionDriver != null && session.MotionDriver.m_Item != null)
                items.Add(session.MotionDriver.m_Item);
            bool shake = false;
            bool mainCamera = false;
            bool silentChannel = false;
            bool screen = false;
            bool postFX = false;
            bool overlay = false;
            bool gameViewOnly = false;
            bool playOnly = false;
            bool noExtension = false;
            bool activate = false;
            bool rumble = false;
            var categories = new HashSet<FeedbackCategory>();
            foreach (FeedbackItem item in items)
            {
                if (item == null || !item.m_Active)
                    continue;
                if (item is CameraShakeItem cameraShake)
                {
                    shake = true;
                    mainCamera |= cameraShake.m_Target.m_Mode == TargetRefMode.MainCamera;
                    if (cameraShake.m_Target.m_Mode == TargetRefMode.Channel && context.Channels.ShakeReceivers(in cameraShake.m_Target.m_Channel) == 0)
                        silentChannel = true;
                    noExtension |= ShakeMissesExtension(session, cameraShake);
                }
                activate |= item is CinemachineActivateCameraItem;
                rumble |= item is GamepadRumbleItem;
                if (item is EffectItem effect)
                    categories.Add(effect.EffectiveCategory);
                screen |= item is ScreenFlashItem || item is ScreenFadeItem || item is FloatingTextItem;
                if (item is TargetItem target && target.Traits.Category == FeedbackCategory.PostFX)
                {
                    postFX = true;
                    overlay |= !(item is PostFXVolumeWeightItem);
                    gameViewOnly |= item is PostFXDepthOfFieldItem || item is PostFXPaniniProjectionItem || item is PostFXLensDistortionItem;
                    playOnly |= item is PostFXMotionBlurItem;
                }
            }
            if (shake)
            {
                // the MainCamera way only: no main camera anywhere · the main scene's camera from a prefab stage · the Game view
                if (mainCamera && Camera.main == null)
                    notices.Add(new PreviewNotice(PreviewNoticeKind.NoMainCamera, "No main camera — the camera shake does nothing."));
                else if (mainCamera && PrefabStageUtility.GetCurrentPrefabStage() != null)
                    notices.Add(new PreviewNotice(PreviewNoticeKind.CameraMainScene, "The camera shake moves the main scene's camera (Game view)."));
                else if (noExtension)
                    notices.Add(new PreviewNotice(PreviewNoticeKind.CinemachineNoExtension, "Cinemachine drives the shaken camera — its live camera needs a Cinemachine Shake Extension to show the shake here."));
                else
                    notices.Add(new PreviewNotice(PreviewNoticeKind.CameraGameView, "The camera shake shows in the Game view."));
                if (silentChannel)
                    notices.Add(new PreviewNotice(PreviewNoticeKind.NoReceivers, "No receiver on the shake's channel — nothing moves."));
            }
            if (activate)
                notices.Add(new PreviewNotice(PreviewNoticeKind.CinemachinePlayOnly, "Activate Camera changes the live Cinemachine camera in play only."));
            if (rumble)
                notices.Add(new PreviewNotice(PreviewNoticeKind.RumblePlayOnly, "Gamepad Rumble plays in play mode only."));
            // a Screen Space Overlay canvas draws in the Game view
            if (screen)
                notices.Add(new PreviewNotice(PreviewNoticeKind.ScreenGameView, "The screen overlay shows in the Game view."));
            if (postFX)
            {
                if (!SceneViewShowsPostFX())
                    notices.Add(new PreviewNotice(PreviewNoticeKind.PostFXSceneView, "This Scene view does not show PostFX — its Post Processing toggle is off (or it draws wireframe)."));
                string camera = MainCameraHidesPostFX(context.Settings.PostFXLayer, overlay);
                if (camera != null)
                    notices.Add(new PreviewNotice(PreviewNoticeKind.PostFXCamera, camera));
                if (gameViewOnly || playOnly)
                    notices.Add(new PreviewNotice(PreviewNoticeKind.PostFXGameView, ((gameViewOnly ? "Depth of Field, Panini Projection and Lens Distortion show in the Game view. " : "")
                        + (playOnly ? "Motion Blur draws in play only." : "")).TrimEnd()));
            }

            SettingsValues play = SettingsValues.From(Resources.Load<FeedbackSettings>(FeedbackSettings.ResourcePath));
            SettingsValues preview = context.Settings;
            foreach (FeedbackCategory category in categories)
            {
                int i = (int)category;
                if (i <= 0 || i >= SettingsValues.CategoryCount)
                    continue;
                if (play.Enabled[i] != preview.Enabled[i] && !play.Enabled[i])
                    notices.Add(new PreviewNotice(PreviewNoticeKind.CategoryOff, "In play the " + category + " category starts off."));
                else if (play.Scale[i] != preview.Scale[i])
                    notices.Add(new PreviewNotice(PreviewNoticeKind.CategoryScale, "In play the " + category + " category's scale is " + play.Scale[i] + "."));
            }
            if (play.FlashLimit != preview.FlashLimit)
                notices.Add(new PreviewNotice(PreviewNoticeKind.FlashLimit, "In play the flash limit is " + play.FlashLimit + "."));
            int held = context.Categories.FlashHeld;
            if (held > 0)
                notices.Add(new PreviewNotice(PreviewNoticeKind.FlashHeld, "The flash limit held back " + held + " flash(es) — play does the same."));
            if (overlay && play.PostFXLayer != preview.PostFXLayer)
                notices.Add(new PreviewNotice(PreviewNoticeKind.PostFXCamera, "In play the PostFX overlay is on layer " + play.PostFXLayer + " (the preview's on " + preview.PostFXLayer + ")."));
            if (shake && play.ShakeReceiversPerPlay != preview.ShakeReceiversPerPlay)
                notices.Add(new PreviewNotice(PreviewNoticeKind.ShakeReceivers, "In play a shake reaches " + play.ShakeReceiversPerPlay + " receiver(s)."));
            notices.Add(new PreviewNotice(PreviewNoticeKind.ExecuteAlways, "[ExecuteAlways] scripts can write over the preview's values."));
        }

        // the shaken camera's brain drives it and its live Cinemachine camera has no shake extension (Core's hook resolves the live
        // camera — none without the package)
        static bool ShakeMissesExtension(PreviewSession session, CameraShakeItem item)
        {
            Type extension = CinemachineRuleTargets.ShakeExtension;
            if (extension == null || item.m_Target.m_Mode == TargetRefMode.Channel || session.Subject == null)
                return false;
            var prepare = new PrepareContext(session.Subject.gameObject, session.Player, null, 1);
            Component live = CinemachineRuleTargets.Live(TargetResolver.Find<Camera>(in item.m_Target, in prepare, out _));
            return live != null && live.GetComponent(extension) == null;
        }

        // why the main camera shows no PostFX — its post-processing off (a camera without URP's data has it off), the overlay's
        // layer out of its volume mask — or null (no main camera: nothing to tell)
        static string MainCameraHidesPostFX(int layer, bool overlay)
        {
            Camera main = Camera.main;
            if (main == null || s_CameraData == null || s_RenderPostProcessing == null)
                return null;
            if (!main.TryGetComponent(s_CameraData, out Component data) || !(bool)s_RenderPostProcessing.GetValue(data))
                return "The main camera's post-processing is off — PostFX does not show.";
            if (overlay && s_VolumeLayerMask != null && (((LayerMask)s_VolumeLayerMask.GetValue(data)).value & (1 << layer)) == 0)
                return "The main camera's volume mask leaves out layer " + layer + " — the PostFX overlay does not show.";
            return null;
        }
    }
}
