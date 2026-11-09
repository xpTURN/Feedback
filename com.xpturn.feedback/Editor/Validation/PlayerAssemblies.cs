using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor.Compilation;

namespace xpTURN.Feedback.Editor
{
    // the assemblies a player build of the active target compiles (test assemblies too) — read once a domain (a target switch
    // reloads it). An assembly the compilation does not know (a precompiled one) is not judged
    internal static class PlayerAssemblies
    {
        static HashSet<string> s_Player;
        static HashSet<string> s_Known;

        // the type's assembly is compiled for the editor but not for players — an editor-only one, or one the target leaves out
        internal static bool Lacks(Type type)
        {
            if (s_Player == null)
            {
                s_Player = new HashSet<string>(CompilationPipeline.GetAssemblies(AssembliesType.Player).Select(a => a.name));
                s_Known = new HashSet<string>(CompilationPipeline.GetAssemblies(AssembliesType.Editor).Select(a => a.name));
            }
            string name = type.Assembly.GetName().Name;
            return s_Known.Contains(name) && !s_Player.Contains(name);
        }
    }
}
