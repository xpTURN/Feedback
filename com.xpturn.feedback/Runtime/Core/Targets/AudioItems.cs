using System;
using UnityEngine;

namespace xpTURN.Feedback
{
    // the names Core can't type
    internal static class AudioNames
    {
        internal const string Source = "UnityEngine.AudioSource, UnityEngine.AudioModule";
        internal const string LowPass = "UnityEngine.AudioLowPassFilter, UnityEngine.AudioModule";
        internal const string HighPass = "UnityEngine.AudioHighPassFilter, UnityEngine.AudioModule";
        internal const string Echo = "UnityEngine.AudioEchoFilter, UnityEngine.AudioModule";
        internal const string Distortion = "UnityEngine.AudioDistortionFilter, UnityEngine.AudioModule";
        internal const string Reverb = "UnityEngine.AudioReverbFilter, UnityEngine.AudioModule";
        internal const string Assembly = "xpTURN.Feedback.Audio";
    }

    // an Audio Source's volume, 0 to 1. The clock starts unscaled (a pause menu at time scale 0 does not hold a fade); a
    // project with Unity's audio disabled skips it
    [FeedbackTarget("Audio/Volume", PreviewGrade.Full, Aliases = "sound loudness fade duck",
        RequiresName = AudioNames.Source, Implementation = AudioNames.Assembly)]
    [Serializable]
    public sealed class AudioVolumeItem : TargetItem
    {
        static readonly TargetTraits s_Traits = new TargetTraits(valueKind: ValueKind.Float,
            supportedMotions: MotionMask.Tween | MotionMask.Curve | MotionMask.Punch | MotionMask.Bounce | MotionMask.Shake | MotionMask.Oscillate | MotionMask.Spring,
            category: FeedbackCategory.Audio, flashSensitive: false, applyMode: ApplyMode.Offset);

        public AudioVolumeItem()
        {
            m_Timing.m_Clock = ItemClock.Unscaled;
        }

        internal override TargetTraits Traits => s_Traits;
    }

    // an Audio Source's pitch, −3 to 3 while it plays — a base outside that range comes back as it was
    [FeedbackTarget("Audio/Pitch", PreviewGrade.Full, Aliases = "sound speed tone",
        RequiresName = AudioNames.Source, Implementation = AudioNames.Assembly)]
    [Serializable]
    public sealed class AudioPitchItem : TargetItem
    {
        static readonly TargetTraits s_Traits = new TargetTraits(valueKind: ValueKind.Float,
            supportedMotions: MotionMask.Tween | MotionMask.Curve | MotionMask.Punch | MotionMask.Bounce | MotionMask.Shake | MotionMask.Oscillate | MotionMask.Spring,
            category: FeedbackCategory.Audio, flashSensitive: false, applyMode: ApplyMode.Offset);

        public AudioPitchItem()
        {
            m_Timing.m_Clock = ItemClock.Unscaled;
        }

        internal override TargetTraits Traits => s_Traits;
    }

    // an Audio Source's stereo pan, −1 (left) to 1 (right)
    [FeedbackTarget("Audio/Stereo Pan", PreviewGrade.Full, Aliases = "sound pan balance left right",
        RequiresName = AudioNames.Source, Implementation = AudioNames.Assembly)]
    [Serializable]
    public sealed class AudioStereoPanItem : TargetItem
    {
        static readonly TargetTraits s_Traits = new TargetTraits(valueKind: ValueKind.Float,
            supportedMotions: MotionMask.Tween | MotionMask.Curve | MotionMask.Punch | MotionMask.Bounce | MotionMask.Shake | MotionMask.Oscillate | MotionMask.Spring,
            category: FeedbackCategory.Audio, flashSensitive: false, applyMode: ApplyMode.Offset);

        public AudioStereoPanItem()
        {
            m_Timing.m_Clock = ItemClock.Unscaled;
        }

        internal override TargetTraits Traits => s_Traits;
    }

    // a Low Pass filter's cutoff, 10 to 22000 Hz. A filter that follows a distance curve (its source's 3D Low-Pass curve) is
    // skipped — a cutoff write would replace that curve
    [FeedbackTarget("Audio/Low Pass Cutoff", PreviewGrade.Full, Aliases = "sound filter muffle lowpass",
        RequiresName = AudioNames.LowPass, Implementation = AudioNames.Assembly)]
    [Serializable]
    public sealed class AudioLowPassCutoffItem : TargetItem
    {
        static readonly TargetTraits s_Traits = new TargetTraits(valueKind: ValueKind.Float,
            supportedMotions: MotionMask.Tween | MotionMask.Curve | MotionMask.Punch | MotionMask.Bounce | MotionMask.Oscillate | MotionMask.Spring,
            category: FeedbackCategory.Audio, flashSensitive: false, applyMode: ApplyMode.Offset);

        public AudioLowPassCutoffItem()
        {
            m_Timing.m_Clock = ItemClock.Unscaled;
        }

        internal override TargetTraits Traits => s_Traits;
    }

    // a High Pass filter's cutoff, 10 to 22000 Hz
    [FeedbackTarget("Audio/High Pass Cutoff", PreviewGrade.Full, Aliases = "sound filter thin highpass",
        RequiresName = AudioNames.HighPass, Implementation = AudioNames.Assembly)]
    [Serializable]
    public sealed class AudioHighPassCutoffItem : TargetItem
    {
        static readonly TargetTraits s_Traits = new TargetTraits(valueKind: ValueKind.Float,
            supportedMotions: MotionMask.Tween | MotionMask.Curve | MotionMask.Punch | MotionMask.Bounce | MotionMask.Oscillate | MotionMask.Spring,
            category: FeedbackCategory.Audio, flashSensitive: false, applyMode: ApplyMode.Offset);

        public AudioHighPassCutoffItem()
        {
            m_Timing.m_Clock = ItemClock.Unscaled;
        }

        internal override TargetTraits Traits => s_Traits;
    }

    // an Echo filter's wet mix, 0 to 1
    [FeedbackTarget("Audio/Echo Wet Mix", PreviewGrade.Full, Aliases = "sound echo delay",
        RequiresName = AudioNames.Echo, Implementation = AudioNames.Assembly)]
    [Serializable]
    public sealed class AudioEchoWetMixItem : TargetItem
    {
        static readonly TargetTraits s_Traits = new TargetTraits(valueKind: ValueKind.Float,
            supportedMotions: MotionMask.Tween | MotionMask.Curve | MotionMask.Punch | MotionMask.Bounce | MotionMask.Oscillate | MotionMask.Spring,
            category: FeedbackCategory.Audio, flashSensitive: false, applyMode: ApplyMode.Offset);

        public AudioEchoWetMixItem()
        {
            m_Timing.m_Clock = ItemClock.Unscaled;
        }

        internal override TargetTraits Traits => s_Traits;
    }

    // a Distortion filter's level, 0 to 1
    [FeedbackTarget("Audio/Distortion Level", PreviewGrade.Full, Aliases = "sound distort crunch",
        RequiresName = AudioNames.Distortion, Implementation = AudioNames.Assembly)]
    [Serializable]
    public sealed class AudioDistortionLevelItem : TargetItem
    {
        static readonly TargetTraits s_Traits = new TargetTraits(valueKind: ValueKind.Float,
            supportedMotions: MotionMask.Tween | MotionMask.Curve | MotionMask.Punch | MotionMask.Bounce | MotionMask.Oscillate | MotionMask.Spring,
            category: FeedbackCategory.Audio, flashSensitive: false, applyMode: ApplyMode.Offset);

        public AudioDistortionLevelItem()
        {
            m_Timing.m_Clock = ItemClock.Unscaled;
        }

        internal override TargetTraits Traits => s_Traits;
    }

    // a Reverb filter's reverb level, −10000 to 2000 mB. A preset other than User turns to User while it plays and comes back
    // at the end with its own values; a Persist keeps User
    [FeedbackTarget("Audio/Reverb Level", PreviewGrade.Full, Aliases = "sound reverb room hall",
        RequiresName = AudioNames.Reverb, Implementation = AudioNames.Assembly)]
    [Serializable]
    public sealed class AudioReverbLevelItem : TargetItem
    {
        static readonly TargetTraits s_Traits = new TargetTraits(valueKind: ValueKind.Float,
            supportedMotions: MotionMask.Tween | MotionMask.Curve | MotionMask.Punch | MotionMask.Bounce | MotionMask.Oscillate | MotionMask.Spring,
            category: FeedbackCategory.Audio, flashSensitive: false, applyMode: ApplyMode.Offset);

        public AudioReverbLevelItem()
        {
            m_Timing.m_Clock = ItemClock.Unscaled;
        }

        internal override TargetTraits Traits => s_Traits;
    }

    // the listener's volume — every sound of the game, 0 to 1. No preview: in Edit mode it is the editor's whole sound
    [FeedbackTarget("Audio/Listener Volume", PreviewGrade.None, Aliases = "sound master volume mute fade",
        Global = true, Implementation = AudioNames.Assembly)]
    [Serializable]
    public sealed class AudioListenerVolumeItem : TargetItem
    {
        static readonly TargetTraits s_Traits = new TargetTraits(valueKind: ValueKind.Float,
            supportedMotions: MotionMask.Tween | MotionMask.Curve | MotionMask.Punch | MotionMask.Bounce | MotionMask.Spring,
            category: FeedbackCategory.Audio, flashSensitive: false, applyMode: ApplyMode.Offset);

        public AudioListenerVolumeItem()
        {
            m_Timing.m_Clock = ItemClock.Unscaled;
        }

        internal override TargetTraits Traits => s_Traits;
    }

    // saved — add members at the end only
    public enum AudioSourceAction
    {
        Play = 0,
        Stop = 1,
        Pause = 2,
        Unpause = 3,
    }

    // a one-shot's volume — the item's × the play's intensity × the Audio category's scale. Below 0 is silence; above 1 the
    // engine plays it louder
    internal static class AudioMath
    {
        internal static float OneShotVolume(float volume, float intensity, float scale) => Math.Max(0f, volume * intensity * scale);

        internal const float MinVoicePitch = 0.1f;
        internal const float MaxVoicePitch = 3f;

        // a pooled voice's volume — an Audio Source's volume stops at 1
        internal static float VoiceVolume(float volume, float intensity, float scale) => Math.Min(1f, Math.Max(0f, volume * intensity * scale));

        // a pooled voice's pitch — the item's ± its random range, the same for the same seed and occurrence, 0.1 to 3 (a voice
        // at 0 never ends, one below it ends at once)
        internal static float VoicePitch(float pitch, float random, uint seed, long occurrence)
        {
            float u = Hash32.ToUnit(Hash32.Tagged(seed, Hash32.Pitch, Hash32.Fold(occurrence)));
            return Math.Min(MaxVoicePitch, Math.Max(MinVoicePitch, pitch + (2f * u - 1f) * random));
        }
    }

    // an Audio Source's play state — Play (a paused source goes on, a stopped one starts over), Stop (its one-shots too),
    // Pause, Unpause. A sound it starts pauses with the player and stops on a Revert or when the player is destroyed; a Revert
    // puts the source's state back
    [FeedbackTarget("Audio/Source Control", PreviewGrade.Simulated, Aliases = "sound play stop pause resume",
        RequiresName = AudioNames.Source, Implementation = AudioNames.Assembly)]
    [Serializable]
    public sealed class AudioSourceControlItem : TargetItem
    {
        static readonly TargetTraits s_Traits = new TargetTraits(valueKind: ValueKind.Bool, supportedMotions: MotionMask.Set,
            category: FeedbackCategory.Audio, flashSensitive: false, applyMode: ApplyMode.Offset);

        [SerializeField] internal AudioSourceAction m_Action;

        public AudioSourceControlItem()
        {
            m_Timing.m_Clock = ItemClock.Unscaled;
        }

        internal override TargetTraits Traits => s_Traits;
    }

    // a clip played once on an Audio Source (one-shots overlap) at the item's volume × the play's intensity × the Audio
    // category's scale. A one-shot can't be stopped alone — the player's pause and end leave it
    [FeedbackTarget("Audio/Play Clip", PreviewGrade.Simulated, Aliases = "sound sfx one shot",
        RequiresName = AudioNames.Source, Implementation = AudioNames.Assembly)]
    [Serializable]
    public sealed class AudioPlayClipItem : TargetItem
    {
        static readonly TargetTraits s_Traits = new TargetTraits(valueKind: ValueKind.Bool, supportedMotions: MotionMask.Set,
            category: FeedbackCategory.Audio, flashSensitive: false, applyMode: ApplyMode.Offset);

        // an AudioClip — the module's type Core can't name
        [SerializeField] internal UnityEngine.Object m_Clip;
        [SerializeField, Range(0f, 1f)] internal float m_Volume = 1f;

        public AudioPlayClipItem()
        {
            m_Timing.m_Clock = ItemClock.Unscaled;
        }

        internal override TargetTraits Traits => s_Traits;
    }

    // an Audio Mixer's exposed parameter, by name. While it plays a snapshot transition does not move the parameter; at the
    // end the snapshot has it again, at the snapshot's value of that moment. A Tween keeps its value until a Revert or
    // Restore(). Play mode only — in Edit mode a mixer takes no write
    [FeedbackTarget("Audio/Mixer Parameter", PreviewGrade.None, Aliases = "sound mixer exposed parameter duck",
        Global = true, Implementation = AudioNames.Assembly)]
    [Serializable]
    public sealed class AudioMixerParameterItem : TargetItem
    {
        static readonly TargetTraits s_Traits = new TargetTraits(valueKind: ValueKind.Float,
            supportedMotions: MotionMask.Tween | MotionMask.Curve | MotionMask.Punch | MotionMask.Bounce | MotionMask.Oscillate | MotionMask.Spring,
            category: FeedbackCategory.Audio, flashSensitive: false, applyMode: ApplyMode.Offset);

        // an AudioMixer — the module's type Core can't name
        [SerializeField] internal UnityEngine.Object m_Mixer;
        [SerializeField] internal string m_Parameter = "";

        public AudioMixerParameterItem()
        {
            m_Timing.m_Clock = ItemClock.Unscaled;
        }

        internal override TargetTraits Traits => s_Traits;
    }

    // a transition to an Audio Mixer snapshot over the transition time, on the mixer's own Update Mode clock. A Revert,
    // Restore() and a play going backward go to Back To; with none the mix stays. A zero Audio category scale skips the
    // transition. Play mode only — in Edit mode a mixer does not transition
    [FeedbackTarget("Audio/Mixer Snapshot", PreviewGrade.None, Aliases = "sound mixer snapshot transition",
        Global = true, Implementation = AudioNames.Assembly)]
    [Serializable]
    public sealed class AudioMixerSnapshotItem : TargetItem
    {
        static readonly TargetTraits s_Traits = new TargetTraits(valueKind: ValueKind.Bool, supportedMotions: MotionMask.Set,
            category: FeedbackCategory.Audio, flashSensitive: false, applyMode: ApplyMode.Offset);

        // AudioMixerSnapshots — the module's type Core can't name
        [SerializeField] internal UnityEngine.Object m_Snapshot;
        [SerializeField, Min(0f)] internal float m_TransitionTime = 0.5f;
        [SerializeField] internal UnityEngine.Object m_BackTo;

        public AudioMixerSnapshotItem()
        {
            m_Timing.m_Clock = ItemClock.Unscaled;
        }

        internal override TargetTraits Traits => s_Traits;
    }

    // a sound from the package's pool of voices — no Audio Source needed — at the play's place, else the target's (the
    // offset in its space), where it stays. The output group is the item's, else the player's Audio Output, else the
    // project's Default Audio Output. It plays to its end, or for Stop After seconds of sound and then the fade out; a
    // Revert, an abort and a backward play stop it, a destroyed player leaves it ringing. The player's pause pauses it while
    // its play runs; the game's AudioListener.pause holds it unless Ignore Listener Pause
    [FeedbackTarget("Audio/Play At Point", PreviewGrade.DisplayOnly, Aliases = "sound sfx one shot pool position 3d spatial",
        Requires = typeof(Transform), Implementation = AudioNames.Assembly)]
    [Serializable]
    public sealed class AudioPlayAtPointItem : TargetItem
    {
        static readonly TargetTraits s_Traits = new TargetTraits(valueKind: ValueKind.Bool, supportedMotions: MotionMask.Set,
            category: FeedbackCategory.Audio, flashSensitive: false, applyMode: ApplyMode.Offset);

        // an AudioResource (an AudioClip or an Audio Random Container) and an AudioMixerGroup — the module's types Core can't
        // name
        [SerializeField] internal UnityEngine.Object m_Resource;
        [SerializeField] internal UnityEngine.Object m_Output;
        [SerializeField, Range(0f, 1f)] internal float m_Volume = 1f;
        [SerializeField, Range(AudioMath.MinVoicePitch, AudioMath.MaxVoicePitch)] internal float m_Pitch = 1f;
        [SerializeField, Range(0f, 1f)] internal float m_PitchRandom;
        [SerializeField, Range(0f, 1f)] internal float m_SpatialBlend = 1f;
        [SerializeField, Min(0f)] internal float m_MinDistance = 1f;
        [SerializeField, Min(0f)] internal float m_MaxDistance = 500f;
        [SerializeField] internal Vector3 m_Offset;
        [SerializeField, Min(0f)] internal float m_StopAfter;
        [SerializeField, Min(0f)] internal float m_FadeOut = 0.1f;
        [SerializeField] internal bool m_IgnoreListenerPause;
        [SerializeField, Min(1)] internal int m_PoolSize = 8;

        public AudioPlayAtPointItem()
        {
            m_Timing.m_Clock = ItemClock.Unscaled;
        }

        internal override TargetTraits Traits => s_Traits;
    }
}
