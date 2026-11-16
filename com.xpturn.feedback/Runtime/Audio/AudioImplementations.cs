using UnityEngine;

namespace xpTURN.Feedback
{
    // the Audio assembly's registration
    static class AudioImplementations
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Register()
        {
            FeedbackImplementations.Register<AudioVolumeItem>(item => new AudioVolumeBinding(item));
            FeedbackImplementations.Register<AudioPitchItem>(item => new AudioPitchBinding(item));
            FeedbackImplementations.Register<AudioStereoPanItem>(item => new AudioStereoPanBinding(item));
            FeedbackImplementations.Register<AudioLowPassCutoffItem>(item => new AudioLowPassCutoffBinding(item));
            FeedbackImplementations.Register<AudioHighPassCutoffItem>(item => new AudioHighPassCutoffBinding(item));
            FeedbackImplementations.Register<AudioEchoWetMixItem>(item => new AudioEchoWetMixBinding(item));
            FeedbackImplementations.Register<AudioDistortionLevelItem>(item => new AudioDistortionLevelBinding(item));
            FeedbackImplementations.Register<AudioReverbLevelItem>(item => new AudioReverbLevelBinding(item));
            FeedbackImplementations.Register<AudioListenerVolumeItem>(item => new AudioListenerVolumeBinding(item));
            FeedbackImplementations.Register<AudioSourceControlItem>(item => new AudioSourceControlBinding(item));
            FeedbackImplementations.Register<AudioPlayClipItem>(item => new AudioPlayClipBinding(item));
            FeedbackImplementations.Register<AudioMixerParameterItem>(item => new AudioMixerParameterBinding(item));
            FeedbackImplementations.Register<AudioMixerSnapshotItem>(item => new AudioMixerSnapshotBinding(item));
            // the pooled voices and their extension service
            FeedbackImplementations.Register<AudioPlayAtPointItem>(item => new AudioPlayAtPointBinding(item));
            FeedbackContext.RegisterExtension(AudioVoiceService.Key, context => new AudioVoiceService(context));
        }

#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
        static void RegisterInEditor() => Register();
#endif
    }
}
