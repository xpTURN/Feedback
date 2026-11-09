using System;
using UnityEngine;

namespace xpTURN.Feedback.Tests
{
    // the game's LateUpdate — the hook sees what the game sees, after the Update stage and before LateApply. No UnityEditor
    // here
    public sealed class LayerMaterialWatcher : MonoBehaviour
    {
        public Action OnLateUpdate;

        void LateUpdate() => OnLateUpdate?.Invoke();
    }
}
