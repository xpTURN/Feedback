using System.Diagnostics;

namespace xpTURN.Feedback
{
    // Math's contract checks compile only in the dotnet build — Tests~/Math defines FEEDBACK_MATH_ASSERT and Unity never
    // does, so there the calls vanish with their arguments. In Unity Debug.Assert fails silently and only costs time on
    // every call. A violation in Unity takes the safe path each caller documents
    internal static class MathAssert
    {
        [Conditional("FEEDBACK_MATH_ASSERT")]
        internal static void That(bool condition, string message) => Debug.Assert(condition, message);
    }
}
