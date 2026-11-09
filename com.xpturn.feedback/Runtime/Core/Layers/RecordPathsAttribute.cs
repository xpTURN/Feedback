using System;

namespace xpTURN.Feedback
{
    // what a slot type writes — serialized paths of its owner type, put on the key's target (a subtype too: a RectTransform
    // takes Transform's rows). A GameObject owner is the target's GameObject. {index} is the key's second aux, opened within
    // the path's array (−1 = every index). Data only: the editor reads it with TypeCache, the runtime never does
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    internal sealed class RecordPathsAttribute : Attribute
    {
        internal RecordPathsAttribute(Type owner, params string[] paths)
        {
            Owner = owner;
            Paths = paths;
        }

        internal Type Owner { get; }
        internal string[] Paths { get; }
    }

    // nothing serialized to record — the slot puts its own state back (a block: the layer's revert, a Persist at the
    // preview's end)
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    internal sealed class NotRecordedAttribute : Attribute
    {
        internal NotRecordedAttribute(string reason) => Reason = reason;

        internal string Reason { get; }
    }

    // what a slot keeps for the preview besides its rows when the editor can't name its type (an optional assembly's — the
    // editor's table keys on Core's slot types). The kind is named, not the slot: a serialized path's value (a string · an int
    // the animation can't bind) · a CanvasRenderer's color (not serialized) · a component's public properties that are not
    // serialized (TMP's maxVisible* — the path names them, space separated)
    internal enum PreviewKeep
    {
        SerializedPath = 0,
        CanvasRendererColor = 1,
        RuntimeProperties = 2,
    }

    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    internal sealed class PreviewKeepsAttribute : Attribute
    {
        internal PreviewKeepsAttribute(PreviewKeep kind, string path = null)
        {
            Kind = kind;
            Path = path;
        }

        internal PreviewKeep Kind { get; }

        internal string Path { get; }
    }
}
