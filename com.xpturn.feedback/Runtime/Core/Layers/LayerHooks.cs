using System;
using UnityEngine;

namespace xpTURN.Feedback
{
    // the Physics optional assembly fills this — Core does not know Rigidbody. direct = a direct-mode slot. Core never clears
    // it: the order inside SubsystemRegistration is not set
    internal static class LayerHooks
    {
        internal static Action<Transform, bool> TransformSlotCreated;

        // a Collider's enabled for Component Enabled — Core cannot tell a Collider from another component. Null without the
        // Physics assembly (the module off): the prepare refuses
        internal static Func<Component, bool> ColliderCheck;
        internal static Func<Component, bool> ColliderEnabledRead;
        internal static Action<Component, bool> ColliderEnabledWrite;
    }
}
