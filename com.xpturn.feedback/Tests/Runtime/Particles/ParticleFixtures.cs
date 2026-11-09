using UnityEngine;

namespace xpTURN.Feedback.Tests
{
    // the particle items' fixtures — the module leaf's system (no play on awake · always simulated: a batch PlayMode has no
    // camera) and a one-shot spawn template made once. Registered at the runtime's load (each play entry — the same keys
    // again): the fixture tables are PlayMode's, and a runtime test assembly names no UnityEditor
    static class ParticleFixtures
    {
        static GameObject s_Template;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Register()
            => ModuleFixtures.Add("particles", Leaf, new[] { typeof(ParticleControlItem), typeof(ParticleEmitItem), typeof(ParticleSpawnItem), typeof(ParticleSimulationSpeedItem), typeof(ParticleEmissionRateItem) }, Fields);

        internal static ParticleSystem System(GameObject go, bool looping, float duration, float lifetime, float rate)
        {
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ParticleSystem.MainModule main = ps.main;
            main.playOnAwake = false;
            main.loop = looping;
            main.duration = duration;
            main.startLifetime = lifetime;
            main.maxParticles = 1000;
            main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
            ParticleSystem.EmissionModule emission = ps.emission;
            emission.rateOverTime = rate;
            return ps;
        }

        static void Leaf(GameObject leaf) => System(leaf, true, 1f, 0.5f, 10f);

        static void Fields(TargetItem item)
        {
            switch (item)
            {
                case ParticleEmitItem e:
                    e.m_Count = 5;
                    break;
                case ParticleSpawnItem s:
                    if (s_Template == null)
                    {
                        s_Template = new GameObject("G1-particles");
                        System(s_Template, false, 0.2f, 0.2f, 20f);
                        s_Template.SetActive(false);
                    }
                    s.m_Prefab = s_Template;
                    s.m_PoolSize = 8;
                    break;
            }
        }
    }
}
