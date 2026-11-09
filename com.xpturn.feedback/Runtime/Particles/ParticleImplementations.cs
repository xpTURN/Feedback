using UnityEngine;

namespace xpTURN.Feedback
{
    // the optional assembly's registration — the runtime at SubsystemRegistration (every play entry — the same factories
    // again), the editor at its load (the preview prepares them)
    static class ParticleImplementations
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Register()
        {
            FeedbackImplementations.Register<ParticleControlItem>(item => new ParticleControlBinding(item));
            FeedbackImplementations.Register<ParticleEmitItem>(item => new ParticleEmitBinding(item));
            FeedbackImplementations.Register<ParticleSpawnItem>(item => new ParticleSpawnBinding(item));
            FeedbackImplementations.Register<ParticleSimulationSpeedItem>(item => new ParticleSimulationSpeedBinding(item));
            FeedbackImplementations.Register<ParticleEmissionRateItem>(item => new ParticleEmissionRateBinding(item));
        }

#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
        static void RegisterInEditor() => Register();
#endif
    }
}
