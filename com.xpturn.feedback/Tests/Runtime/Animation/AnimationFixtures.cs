using UnityEngine;

namespace xpTURN.Feedback.Tests
{
    // the animator items' fixtures — the module leaf's Animator on the test controller (made before the build in Resources —
    // FeedbackTestAnimator · always animated: a batch PlayMode has no camera) and the names it has. Registered at the runtime's
    // load (no UnityEditor here)
    static class AnimationFixtures
    {
        internal const string ControllerName = "FeedbackTestAnimator";
        static RuntimeAnimatorController s_Controller;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Register()
            => ModuleFixtures.Add("animation", Leaf, new[] { typeof(AnimatorParameterItem), typeof(AnimatorFloatItem), typeof(AnimatorSpeedItem), typeof(AnimatorLayerWeightItem), typeof(AnimatorStateItem) }, Fields);

        internal static RuntimeAnimatorController Controller => s_Controller != null ? s_Controller : s_Controller = Resources.Load<RuntimeAnimatorController>(ControllerName);

        internal static Animator Make(GameObject go)
        {
            var animator = go.AddComponent<Animator>();
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            animator.runtimeAnimatorController = Controller;
            return animator;
        }

        static void Leaf(GameObject leaf) => Make(leaf);

        static void Fields(TargetItem item)
        {
            switch (item)
            {
                case AnimatorParameterItem p:
                    p.m_Parameter = "Count";
                    p.m_Kind = AnimatorParameterKind.Int;
                    p.m_Pick = AnimatorValuePick.Increment;
                    break;
                case AnimatorFloatItem f:
                    f.m_Parameter = "Speed";
                    break;
                case AnimatorLayerWeightItem w:
                    w.m_Layer = "Upper";
                    break;
                case AnimatorStateItem s:
                    s.m_State = "Other";
                    break;
            }
        }
    }
}
