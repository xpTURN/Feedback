using Unity.Cinemachine;
using UnityEngine;

namespace xpTURN.Feedback.Tests
{
    // the Cinemachine items' fixtures — the leaf's camera with a noise (a profile) for the lens · noise · activation targets.
    // Registered at the runtime's load: the test assemblies without the package can't name the camera
    static class CinemachineFixtures
    {
        static NoiseSettings s_Profile;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Register()
            => ModuleFixtures.Add("cinemachine", Leaf, new[]
            {
                typeof(CinemachineLensFieldOfViewItem), typeof(CinemachineLensOrthographicSizeItem), typeof(CinemachineLensDutchItem),
                typeof(CinemachineNoiseAmplitudeGainItem), typeof(CinemachineNoiseFrequencyGainItem), typeof(CinemachineActivateCameraItem),
            }, null);

        static void Leaf(GameObject leaf)
        {
            leaf.AddComponent<CinemachineCamera>();
            leaf.AddComponent<CinemachineBasicMultiChannelPerlin>().NoiseProfile = Profile();
        }

        internal static NoiseSettings Profile()
        {
            if (s_Profile == null)
            {
                s_Profile = ScriptableObject.CreateInstance<NoiseSettings>();
                s_Profile.name = "G1-noise";
                s_Profile.PositionNoise = new[] { new NoiseSettings.TransformNoiseParams { X = new NoiseSettings.NoiseParams { Amplitude = 1f, Frequency = 1f } } };
            }
            return s_Profile;
        }
    }
}
