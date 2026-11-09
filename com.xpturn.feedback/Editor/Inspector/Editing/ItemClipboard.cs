using System.Collections.Generic;
using UnityEngine;

namespace xpTURN.Feedback.Editor
{
    // the copied items — a hidden object outside the scenes and the assets: it outlives a domain reload. Its name is outside
    // the preview recovery's prefix
    internal sealed class ItemClipboard : ScriptableObject
    {
        internal const string ObjectName = "xpTURN.Feedback.Editor.Clipboard";

        [SerializeReference] internal List<FeedbackItem> m_Items = new List<FeedbackItem>();

        static ItemClipboard s_Instance;

        internal static ItemClipboard Instance
        {
            get
            {
                if (s_Instance != null)
                    return s_Instance;
                foreach (ItemClipboard c in Resources.FindObjectsOfTypeAll<ItemClipboard>())
                {
                    if (c.name == ObjectName)
                        return s_Instance = c;
                }
                s_Instance = CreateInstance<ItemClipboard>();
                s_Instance.name = ObjectName;
                s_Instance.hideFlags = HideFlags.HideAndDontSave;
                return s_Instance;
            }
        }

        internal static int Count => Instance.m_Items.Count;

        // copies in — the list's items stay theirs
        internal static void Set(IEnumerable<FeedbackItem> items)
        {
            ItemClipboard c = Instance;
            c.m_Items.Clear();
            c.m_Items.AddRange(ItemCopier.Copies(items));
        }

        // copies out — each paste its own instances
        internal static List<FeedbackItem> Take() => ItemCopier.Copies(Instance.m_Items);
    }
}
