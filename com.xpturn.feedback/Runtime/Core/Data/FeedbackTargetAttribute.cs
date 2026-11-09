using System;

namespace xpTURN.Feedback
{
    // full · simulated · display only · none
    internal enum PreviewGrade
    {
        Full = 0,
        Simulated = 1,
        DisplayOnly = 2,
        None = 3,
    }

    // editor metadata read with TypeCache — the runtime never reads it
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    internal sealed class FeedbackTargetAttribute : Attribute
    {
        public FeedbackTargetAttribute(string path, PreviewGrade preview)
        {
            Path = path;
            Preview = preview;
        }

        public string Path { get; }
        public PreviewGrade Preview { get; }
        public string Aliases { get; set; } = "";    // search words, space separated
        public Type Requires { get; set; }           // what a Direct target · a drop must be (editor only — the candidates, the drop)
        public bool Global { get; set; }             // no target — the binding reads none, the editor hides it
        public string RequiresName { get; set; }     // a module type Core can't name — its assembly-qualified name (the editor resolves it)
        public string Implementation { get; set; } = "";   // the implementing assembly — empty is Core
    }
}
