using UnityEngine;

namespace xpTURN.Feedback.Tests
{
    // the 3D physics items' fixtures — the module leaf's body (no gravity · a sphere collider the explosion finds). Registered
    // at the runtime's load (Particles' form)
    static class PhysicsFixtures
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Register()
            => ModuleFixtures.Add("physics", Leaf, new[] { typeof(RigidbodyForceItem), typeof(RigidbodyExplosionItem), typeof(RigidbodyVelocityItem) }, Fields);

        internal static Rigidbody Body(GameObject go)
        {
            go.AddComponent<SphereCollider>();
            var body = go.AddComponent<Rigidbody>();
            body.useGravity = false;
            return body;
        }

        static void Leaf(GameObject leaf) => Body(leaf);

        static Rigidbody s_Found;

        // the explosion finds one body made once, on its own layer (Ignore Raycast) and frozen (it takes the force and stays): a
        // found collider's first C# object is the engine's (once in its life), and the leaf's collider is new in each
        // fixture
        static void Fields(TargetItem item)
        {
            if (item is RigidbodyExplosionItem e)
            {
                if (s_Found == null)
                {
                    var go = new GameObject("G1-explosion-body") { layer = 2 };
                    s_Found = Body(go);
                    s_Found.constraints = RigidbodyConstraints.FreezeAll;
                }
                e.m_Radius = 5f;
                e.m_MaxBodies = 8;
                e.m_Layers = 1 << 2;
            }
        }
    }
}
