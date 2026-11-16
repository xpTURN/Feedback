using UnityEngine;
using UnityEngine.Audio;

namespace xpTURN.Feedback.Tests
{
    // the Audio items' fixtures — a source not playing, then the five filters (a filter needs its source first; the low pass
    // keeps its one-key curve, so it is taken) · the listener's volume is global (an empty leaf). The source, Play Clip and
    // Play At Point take a short silent clip made at the first fixture (a source with no clip warns on Play). No listener: the leaves pile
    // up in a test, and listeners on them warn every frame — the measured cycles tick by hand inside one frame, out of reach
    // of the engine's frame warning. The mixer's two are global (an empty leaf) on the package's test mixer (its build setup
    // puts it under Resources): the exposed volume, snapshot B with Back To A
    static class AudioFixtures
    {
        static AudioClip s_Clip;
        static AudioMixer s_Mixer;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Register()
        {
            ModuleFixtures.Add("audio", Leaf, new[]
            {
                typeof(AudioVolumeItem), typeof(AudioPitchItem), typeof(AudioStereoPanItem), typeof(AudioLowPassCutoffItem), typeof(AudioHighPassCutoffItem),
                typeof(AudioEchoWetMixItem), typeof(AudioDistortionLevelItem), typeof(AudioReverbLevelItem),
                typeof(AudioSourceControlItem), typeof(AudioPlayClipItem), typeof(AudioPlayAtPointItem),
            }, Fields);
            ModuleFixtures.Add("audio-listener", _ => { }, new[] { typeof(AudioListenerVolumeItem) }, null);
            ModuleFixtures.Add("audio-mixer", _ => { }, new[] { typeof(AudioMixerParameterItem), typeof(AudioMixerSnapshotItem) }, Mixer);
        }

        static AudioClip Silent()
        {
            if (s_Clip == null)
                s_Clip = AudioClip.Create("fixture", 2205, 1, 44100, false);
            return s_Clip;
        }

        static void Mixer(TargetItem item)
        {
            if (s_Mixer == null)
                s_Mixer = Resources.Load<AudioMixer>("FeedbackTestMixer");
            if (s_Mixer == null)
                return;
            if (item is AudioMixerParameterItem parameter)
            {
                parameter.m_Mixer = s_Mixer;
                parameter.m_Parameter = "FeedbackTestVolume";
            }
            else if (item is AudioMixerSnapshotItem snapshot)
            {
                snapshot.m_Snapshot = s_Mixer.FindSnapshot("B");
                snapshot.m_BackTo = s_Mixer.FindSnapshot("A");
            }
        }

        static void Fields(TargetItem item)
        {
            if (item is AudioPlayClipItem clip)
                clip.m_Clip = Silent();
            if (item is AudioPlayAtPointItem point)
                point.m_Resource = Silent();
        }

        static void Leaf(GameObject leaf)
        {
            var source = leaf.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.clip = Silent();
            leaf.AddComponent<AudioLowPassFilter>();
            leaf.AddComponent<AudioHighPassFilter>();
            leaf.AddComponent<AudioEchoFilter>();
            leaf.AddComponent<AudioDistortionFilter>();
            leaf.AddComponent<AudioReverbFilter>();
        }
    }
}
