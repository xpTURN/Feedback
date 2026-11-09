using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace xpTURN.Feedback
{
    // a pooled floating text's parts — its text, its group (the alpha reaches the fallback sub-meshes), its renderers
    // for the tint (the main one and the sub-meshes')
    [AddComponentMenu("")]
    internal sealed class FloatingTextInstance : MonoBehaviour
    {
        internal TMP_Text Text;
        internal CanvasGroup Group;
        internal RectTransform Rect;
        internal readonly List<CanvasRenderer> Renderers = new List<CanvasRenderer>(4);

        internal void Bind()
        {
            Text = GetComponent<TMP_Text>();
            Group = GetComponent<CanvasGroup>();
            Rect = (RectTransform)transform;
            RefreshRenderers();
        }

        internal void RefreshRenderers() => GetComponentsInChildren(true, Renderers);
    }
}
