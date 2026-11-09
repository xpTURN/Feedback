using System;
using System.IO;
using System.Reflection;
using UnityEngine;
using UnityEngine.LowLevel;
using UnityEngine.PlayerLoop;
using xpTURN.Feedback;

namespace RecompileCheck
{
    // the record the tool reads (a line per event, in order) and the package state it holds. The scheduler is internal and
    // this assembly is not a friend of the package, so it is read by reflection
    public static class RecompileRecord
    {
        const BindingFlags Internal = BindingFlags.NonPublic | BindingFlags.Instance;
        static readonly Type s_Scheduler = typeof(FeedbackPlayerLoop).Assembly.GetType("xpTURN.Feedback.FeedbackScheduler", true);

        public static string FilePath => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "RecompileCheck.txt"));

        public static void Write(string line) => File.AppendAllText(FilePath, line + "\n");

        static object Default => s_Scheduler.GetProperty("Default", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);

        public static string DefaultState()
        {
            object scheduler = Default;
            if (scheduler == null)
                return "null";
            return (bool)s_Scheduler.GetProperty("IsShutdown", Internal).GetValue(scheduler) ? "closed" : "open";
        }

        // what the players reserved in it; -1 without a Default
        public static int DefaultReserved()
        {
            object scheduler = Default;
            return scheduler == null ? -1 : (int)s_Scheduler.GetProperty("ReservedCount", Internal).GetValue(scheduler);
        }

        // the frame number its Revert raises; -1 without a Default
        public static int DefaultFrame()
        {
            object scheduler = Default;
            if (scheduler == null)
                return -1;
            object context = s_Scheduler.GetProperty("Context", Internal).GetValue(scheduler);
            return (int)context.GetType().GetProperty("Frame", Internal).GetValue(context);
        }

        public static int CountOurs() => Count(PlayerLoop.GetCurrentPlayerLoop());

        static int Count(PlayerLoopSystem system)
        {
            int count = system.type != null && system.type.DeclaringType == typeof(FeedbackPlayerLoop) ? 1 : 0;
            if (system.subSystemList != null)
                foreach (PlayerLoopSystem child in system.subSystemList)
                    count += Count(child);
            return count;
        }

        // the first system of EarlyUpdate · PreLateUpdate · PostLateUpdate
        public static string Fronts()
        {
            PlayerLoopSystem loop = PlayerLoop.GetCurrentPlayerLoop();
            return Front(loop, typeof(EarlyUpdate)) + "/" + Front(loop, typeof(PreLateUpdate)) + "/" + Front(loop, typeof(PostLateUpdate));
        }

        static string Front(PlayerLoopSystem loop, Type phase)
        {
            foreach (PlayerLoopSystem system in loop.subSystemList)
                if (system.type == phase)
                    return system.subSystemList != null && system.subSystemList.Length > 0 && system.subSystemList[0].type != null ? system.subSystemList[0].type.Name : "-";
            return "?";
        }

        public static string State() => "default=" + DefaultState() + " stageFrame=" + DefaultFrame() + " ours=" + CountOurs() + " fronts=" + Fronts();
    }
}
