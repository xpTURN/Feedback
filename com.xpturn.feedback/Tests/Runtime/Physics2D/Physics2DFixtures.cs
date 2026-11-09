using UnityEngine;

namespace xpTURN.Feedback.Tests
{
    // the 2D physics items' fixtures — the module leaf's body (gravity 0 · a circle collider)
    static class Physics2DFixtures
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Register()
            => ModuleFixtures.Add("physics2d", Leaf, new[] { typeof(Rigidbody2DForceItem), typeof(Rigidbody2DVelocityItem) }, null);

        internal static Rigidbody2D Body(GameObject go)
        {
            go.AddComponent<CircleCollider2D>();
            var body = go.AddComponent<Rigidbody2D>();
            body.gravityScale = 0f;
            return body;
        }

        static void Leaf(GameObject leaf) => Body(leaf);
    }
}
