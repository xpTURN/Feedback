using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace RecompileCheck
{
    // check-recompile-during-play.sh's driver (-executeMethod RecompileCheck.RecompileDriver.Run, not in batch mode).
    // EditorApplication.update steps through the phases and SessionState carries them over the domain reload: enter play →
    // an observer and the players, 60 frames → edit Touch.cs and refresh → the reload → 60 frames → exit play → quit. Every
    // step writes a line to RecompileRecord
    public static class RecompileDriver
    {
        const string PhaseKey = "RecompileCheck.Phase";
        const string StartKey = "RecompileCheck.Start";
        const string EditKey = "RecompileCheck.Edit";
        const string FrameKey = "RecompileCheck.Frame";
        const string StageFrameKey = "RecompileCheck.StageFrame";
        const string TouchPath = "Assets/RecompileCheck/Touch/Touch.cs";
        const int Frames = 60;
        const double TimeoutSeconds = 240;
        const double ReloadTimeoutSeconds = 60;

        const int EnterPlay = 1;
        const int BeforeEdit = 2;
        const int WaitReload = 3;
        const int AfterReload = 4;
        const int WaitExit = 5;
        const int Finish = 6;
        const int Done = 7;

        static bool s_PoolsNext;

        public static void Run()
        {
            File.Delete(RecompileRecord.FilePath);
            int option = EditorPrefs.GetInt("ScriptCompilationDuringPlay", 0);
            RecompileRecord.Write("start version=" + Application.unityVersion + " scriptChangesWhilePlaying=" + option);
            // 0 = Recompile And Continue Playing. A user setting — read, never changed
            if (option != 0)
            {
                EditorApplication.Exit(2);
                return;
            }
            SessionState.SetString(StartKey, DateTime.UtcNow.Ticks.ToString());
            SessionState.SetInt(PhaseKey, EnterPlay);
        }

        [InitializeOnLoadMethod]
        static void OnLoad()
        {
            EditorApplication.update -= Step;
            EditorApplication.update += Step;
            if (SessionState.GetInt(PhaseKey, 0) != WaitReload)
                return;
            RecompileRecord.Write("reload isPlaying=" + EditorApplication.isPlaying + " runtimes=" + RecompilePlayers.Runtimes());
            RecompileRecord.Write("receivers-reload " + RecompilePlayers.Receivers());
            RecompileRecord.Write("drivers-reload " + RecompilePlayers.Drivers());
            RecompileRecord.Write("pools-reload " + RecompilePlayers.Pools());
            RecompileRecord.Write("services-reload " + RecompilePlayers.Services());
            SessionState.SetInt(FrameKey, -1);
            SessionState.SetInt(PhaseKey, AfterReload);
        }

        static double Since(string key) => (DateTime.UtcNow - new DateTime(long.Parse(SessionState.GetString(key, DateTime.UtcNow.Ticks.ToString())))).TotalSeconds;

        static void Stop(string reason)
        {
            RecompileRecord.Write("stop: " + reason);
            SessionState.SetInt(PhaseKey, EditorApplication.isPlaying ? WaitExit : Finish);
            if (EditorApplication.isPlaying)
                EditorApplication.ExitPlaymode();
        }

        static void Step()
        {
            int phase = SessionState.GetInt(PhaseKey, 0);
            if (phase == 0 || phase == Done)
                return;
            if (phase < WaitExit && Since(StartKey) > TimeoutSeconds)
            {
                Stop("timeout at phase " + phase);
                return;
            }
            switch (phase)
            {
                case EnterPlay:
                    if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlaying)
                        return;
                    SessionState.SetInt(PhaseKey, BeforeEdit);
                    EditorApplication.EnterPlaymode();
                    return;
                case BeforeEdit:
                    if (!EditorApplication.isPlaying)
                        return;
                    if (UnityEngine.Object.FindAnyObjectByType<RecompileObserver>() == null)
                    {
                        new GameObject(nameof(RecompileObserver)).AddComponent<RecompileObserver>();
                        RecompilePlayers.MakeAll();
                        RecompilePlayers.MakeReceivers();
                        RecompileRecord.Write("players-made frame=" + Time.frameCount + " " + RecompilePlayers.State());
                        RecompileRecord.Write("receivers-made " + RecompilePlayers.Receivers() + " " + RecompilePlayers.SendToReceiver());
                        RecompileRecord.Write("listeners-made " + RecompilePlayers.MakeListeners());
                        RecompileRecord.Write("drivers-made frame=" + Time.frameCount + " " + RecompilePlayers.MakeDrivers());
                        RecompileRecord.Write("pools-made frame=" + Time.frameCount + " " + RecompilePlayers.MakePools());
                        RecompileRecord.Write("services-made frame=" + Time.frameCount + " " + RecompilePlayers.MakeServices());
                    }
                    if (Time.frameCount >= 30)
                        RecompilePlayers.DestroyDetached();
                    if (Time.frameCount < Frames)
                        return;
                    RecompileRecord.Write("before-edit frame=" + Time.frameCount + " " + RecompileRecord.State() + " players " + RecompilePlayers.State());
                    RecompileRecord.Write("drivers-before frame=" + Time.frameCount + " reserved=" + RecompileRecord.DefaultReserved() + " " + RecompilePlayers.Drivers());
                    RecompileRecord.Write("pools-before frame=" + Time.frameCount + " " + RecompilePlayers.Pools());
                    RecompileRecord.Write("services-before frame=" + Time.frameCount + " " + RecompilePlayers.Services());
                    File.WriteAllText(TouchPath, "namespace RecompileCheck { public static class Touch { public const long Value = " + DateTime.UtcNow.Ticks + "; } }\n");
                    SessionState.SetString(EditKey, DateTime.UtcNow.Ticks.ToString());
                    SessionState.SetInt(PhaseKey, WaitReload);
                    AssetDatabase.Refresh();
                    return;
                case WaitReload:
                    if (Since(EditKey) > ReloadTimeoutSeconds)
                        Stop("no reload within " + ReloadTimeoutSeconds + " s — isPlaying=" + EditorApplication.isPlaying + " isCompiling=" + EditorApplication.isCompiling);
                    return;
                case AfterReload:
                    if (!EditorApplication.isPlaying)
                    {
                        Stop("not playing after the reload");
                        return;
                    }
                    int first = SessionState.GetInt(FrameKey, -1);
                    if (first < 0)
                    {
                        SessionState.SetInt(FrameKey, Time.frameCount);
                        SessionState.SetInt(StageFrameKey, RecompileRecord.DefaultFrame());
                        RecompileRecord.Write("after-reload frame=" + Time.frameCount + " " + RecompileRecord.State() + " reserved=" + RecompileRecord.DefaultReserved() + " players " + RecompilePlayers.State());
                        RecompileRecord.Write("after-actions " + RecompilePlayers.Act());
                        RecompileRecord.Write("receivers-after " + RecompilePlayers.Receivers() + " " + RecompilePlayers.SendToReceiver());
                        RecompileRecord.Write("exit-plays " + RecompilePlayers.MakeExitPlays());
                        RecompileRecord.Write("drivers-after frame=" + Time.frameCount + " reserved=" + RecompileRecord.DefaultReserved() + " " + RecompilePlayers.Drivers());
                        RecompileRecord.Write("pools-after frame=" + Time.frameCount + " " + RecompilePlayers.Pools());
                        RecompileRecord.Write("pools-respawn frame=" + Time.frameCount + " " + RecompilePlayers.Respawn());
                        RecompileRecord.Write("services-after frame=" + Time.frameCount + " " + RecompilePlayers.ReplayServices());
                        return;
                    }
                    if (!s_PoolsNext && Time.frameCount > first)
                    {
                        s_PoolsNext = true;
                        RecompileRecord.Write("pools-next frame=" + Time.frameCount + " " + RecompilePlayers.Pools());
                        RecompileRecord.Write("services-next frame=" + Time.frameCount + " " + RecompilePlayers.Services());
                    }
                    if (Time.frameCount - first < Frames)
                        return;
                    RecompileRecord.Write("after-frames frames=" + (Time.frameCount - first) + " stages=" + (RecompileRecord.DefaultFrame() - SessionState.GetInt(StageFrameKey, 0))
                        + " " + RecompileRecord.State() + " players " + RecompilePlayers.State());
                    RecompileRecord.Write("drivers-frames frame=" + Time.frameCount + " reserved=" + RecompileRecord.DefaultReserved() + " " + RecompilePlayers.Drivers());
                    RecompileRecord.Write("pools-frames frame=" + Time.frameCount + " " + RecompilePlayers.Pools());
                    SessionState.SetInt(PhaseKey, WaitExit);
                    EditorApplication.ExitPlaymode();
                    return;
                case WaitExit:
                    if (EditorApplication.isPlaying)
                        return;
                    RecompileRecord.Write("exited " + RecompileRecord.State());
                    RecompileRecord.Write("pools-exited " + RecompilePlayers.Pools());
                    RecompileRecord.Write("services-exited " + RecompilePlayers.Services());
                    SessionState.SetInt(PhaseKey, Finish);
                    return;
                case Finish:
                    RecompileRecord.Write("done");
                    SessionState.SetInt(PhaseKey, Done);
                    EditorApplication.Exit(0);
                    return;
            }
        }
    }
}
