using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.Compilation;
using CAssembly = UnityEditor.Compilation.Assembly;

namespace xpTURN.Feedback.Editor
{
    // where each play started. While the switch is on, the debugger's Played handler keeps up to sixteen frames of the call
    // (with their files) in a ring of 256. The column's frame is the first one past the package's runtime assemblies and
    // UnityEngine.Events; a play begun inside the package — a scheduler pass met first, or no frame outside it at all (an
    // AutoPlay, a Timeline clip, a flow item in the loop) — shows the call that played it instead. The switch is an editor
    // preference read once; off, the handler only reads a bool
    internal static class DebuggerCallSites
    {
        internal const string Key = "xpTURN.Feedback.Debugger.CallSites";
        internal const int Size = 256;
        internal const int Depth = 16;
        internal const int MaxWalk = 64;

        struct Entry
        {
            internal FeedbackPlayer Player;
            internal PlaybackHandle Handle;
            internal StackFrame[] Frames;
            internal int Count;
            internal string Top;
        }

        static readonly Entry[] s_Ring = new Entry[Size];
        static readonly Dictionary<System.Reflection.Assembly, bool> s_Inside = new Dictionary<System.Reflection.Assembly, bool>();
        static HashSet<string> s_Runtime;
        static int s_Head;
        static int s_Count;
        static bool s_Loaded;
        static bool s_Enabled;

        internal static bool Enabled
        {
            get
            {
                if (!s_Loaded)
                    Load();
                return s_Enabled;
            }
            set
            {
                s_Enabled = value;
                s_Loaded = true;
                EditorPrefs.SetBool(Key, value);
            }
        }

        // the kept plays
        internal static int Count => s_Count;

        internal static void Load()
        {
            s_Enabled = EditorPrefs.GetBool(Key, false);
            s_Loaded = true;
        }

        internal static void Clear()
        {
            for (int i = 0; i < Size; i++)
                s_Ring[i] = default;
            s_Head = 0;
            s_Count = 0;
        }

        // the assemblies built from the package's Runtime folder
        internal static HashSet<string> RuntimeNames()
        {
            if (s_Runtime != null)
                return s_Runtime;
            var names = new HashSet<string>();
            foreach (CAssembly a in CompilationPipeline.GetAssemblies(AssembliesType.PlayerWithoutTestAssemblies))
            {
                if (a.sourceFiles.Length > 0 && a.sourceFiles[0].Replace('\\', '/').StartsWith("Packages/com.xpturn.feedback/Runtime/"))
                    names.Add(a.name);
            }
            return s_Runtime = names;
        }

        static bool Inside(MethodBase m)
        {
            System.Type t = m?.DeclaringType;
            if (t == null)
                return false;
            System.Reflection.Assembly a = t.Assembly;
            if (!s_Inside.TryGetValue(a, out bool inside))
                s_Inside[a] = inside = RuntimeNames().Contains(a.GetName().Name);
            return inside;
        }

        static bool IsEvents(MethodBase m) => m?.DeclaringType != null && m.DeclaringType.Namespace == "UnityEngine.Events";

        // the play's own path: the diagnostics, the player's Play · PlayFromEvent · Raise, the runtime
        static bool IsPlayPath(MethodBase m)
        {
            System.Type t = m?.DeclaringType;
            if (t == null)
                return false;
            if (t == typeof(FeedbackDiagnostics) || t == typeof(PlayerRuntime))
                return true;
            return t == typeof(FeedbackPlayer) && (m.Name == "Play" || m.Name == "PlayFromEvent" || m.Name == "Raise");
        }

        static bool IsPass(MethodBase m) => m?.DeclaringType == typeof(FeedbackScheduler);

        // from the debugger's handler: its own frames are skipped up to the first runtime frame
        internal static void Capture(FeedbackPlayer player, PlaybackHandle handle)
        {
            int start = 1;
            for (; start < MaxWalk; start++)
            {
                MethodBase m = new StackFrame(start, false).GetMethod();
                if (m == null || Inside(m))
                    break;
            }
            var frames = new StackFrame[Depth];
            int n = 0;
            for (int i = start; i < start + Depth; i++)
            {
                var f = new StackFrame(i, true);
                if (f.GetMethod() == null)
                    break;
                frames[n++] = f;
            }
            string top = Top(frames, n, start + n);
            int at;
            if (s_Count == Size)
            {
                at = s_Head;
                s_Head = (s_Head + 1) % Size;
            }
            else
            {
                at = (s_Head + s_Count) % Size;
                s_Count++;
            }
            s_Ring[at] = new Entry { Player = player, Handle = handle, Frames = frames, Count = n, Top = top };
        }

        static string Name(MethodBase m) => (m.DeclaringType != null ? m.DeclaringType.Name + "." : "") + m.Name;

        static string Name(StackFrame f)
        {
            string file = f.GetFileName();
            return file == null ? Name(f.GetMethod()) : Name(f.GetMethod()) + " (" + Path.GetFileName(file) + ":" + f.GetFileLineNumber() + ")";
        }

        static string Top(StackFrame[] frames, int n, int next)
        {
            if (n == 0)
                return "";
            int caller = -1;
            for (int i = 0; i < n; i++)
            {
                MethodBase m = frames[i].GetMethod();
                if (caller < 0 && !IsPlayPath(m))
                    caller = i;
                if (IsPass(m))
                    return Name(frames[caller >= 0 ? caller : i]);
                if (!Inside(m) && !IsEvents(m))
                    return Name(frames[i]);
            }
            // past the kept frames, without their files
            for (int i = next; i < MaxWalk; i++)
            {
                MethodBase m = new StackFrame(i, false).GetMethod();
                if (m == null || IsPass(m))
                    break;
                if (!Inside(m) && !IsEvents(m))
                    return Name(m);
            }
            return Name(frames[caller >= 0 ? caller : n - 1]);
        }

        static bool Find(FeedbackPlayer player, PlaybackHandle handle, out Entry e)
        {
            for (int i = 0; i < s_Count; i++)
            {
                Entry x = s_Ring[(s_Head + s_Count - 1 - i) % Size];
                if (x.Handle == handle && ReferenceEquals(x.Player, player))
                {
                    e = x;
                    return true;
                }
            }
            e = default;
            return false;
        }

        // the column's frame — empty when the play was not kept
        internal static string TopOf(FeedbackPlayer player, PlaybackHandle handle) => Find(player, handle, out Entry e) ? e.Top : "";

        internal static int FrameCount(FeedbackPlayer player, PlaybackHandle handle) => Find(player, handle, out Entry e) ? e.Count : 0;

        // the kept frames, one a line
        internal static string FramesOf(FeedbackPlayer player, PlaybackHandle handle)
        {
            if (!Find(player, handle, out Entry e))
                return "";
            var b = new StringBuilder();
            for (int i = 0; i < e.Count; i++)
                b.Append(i == 0 ? "" : "\n").Append(Name(e.Frames[i]));
            return b.ToString();
        }
    }
}
