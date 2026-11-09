#if !UNITY_6000_3_OR_NEWER
namespace xpTURN.Feedback.Editor
{
    internal static class EntityIdCompat
    {
        internal static int GetEntityId(this UnityEngine.Object o) => o.GetInstanceID();
    }
}
#endif
