using System;

namespace xpTURN.Feedback
{
    // a FeedbackPlayer field whose empty value means the first FeedbackPlayer on its component's object. Data only: the
    // editor's player field reads it, the runtime never does
    [AttributeUsage(AttributeTargets.Field, Inherited = false)]
    internal sealed class OwnObjectPlayerAttribute : Attribute
    {
    }
}
