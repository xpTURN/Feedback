using System;
using UnityEngine;
using UnityEngine.Audio;

namespace xpTURN.Feedback
{
    internal static class AudioPrepare
    {
        internal const string NoAudio = "Unity's audio is disabled in this project (Project Settings > Audio)";
        internal const string DistanceCurve = "the low pass follows a distance curve (the source's 3D Low-Pass curve) — a cutoff write would replace it";
        internal const string NoClip = "no clip";
        internal const string NotAClip = "the clip is not an AudioClip";
        internal const string NoMixer = "no mixer";
        internal const string NotAMixer = "the mixer is not an AudioMixer";
        internal const string NoParameter = "name the exposed parameter";
        internal const string NoSnapshot = "no snapshot";
        internal const string NotASnapshot = "the snapshot is not an AudioMixerSnapshot";
        internal const string BackToNotASnapshot = "Back To is not an AudioMixerSnapshot";
        internal const string BackToOtherMixer = "Back To belongs to another mixer";
        internal const string NoResource = "no resource";
        internal const string NotAResource = "the resource is not an AudioResource";
        internal const string NotAGroup = "the output is not an AudioMixerGroup";
        internal const string PlayerOutputNotAGroup = "the player's Audio Output is not an AudioMixerGroup";
        internal const string ProjectOutputNotAGroup = "the project's Default Audio Output is not an AudioMixerGroup";
        internal const string NoVoiceService = "no audio voice service";

        // a project with audio disabled reports a sample rate of 0 — the configuration allocates nothing (the output sample rate
        // logs an error there)
        internal static bool Disabled => AudioSettings.GetConfiguration().sampleRate == 0;

        // the target component — refused in a project with audio disabled or when it is not found
        internal static bool Find<T>(in TargetRef target, in PrepareContext context, out T component, out string why) where T : Component
        {
            component = null;
            why = null;
            if (Disabled)
            {
                why = NoAudio;
                return false;
            }
            component = TargetResolver.Find<T>(in target, in context, out TargetFailure failure);
            if (component != null)
                return true;
            why = TargetResolver.Describe(failure);
            return false;
        }
    }

    // a source's volume · pitch · pan, a filter's value — the component's slot per slot type
    internal sealed class AudioVolumeBinding : ValueTargetBinding<float, FloatOps>
    {
        internal AudioVolumeBinding(AudioVolumeItem item) : base(item)
        {
        }

        protected override bool Acquire(in PrepareContext context, bool direct, out ValueSlot<float, FloatOps> slot)
        {
            slot = null;
            if (!AudioPrepare.Find(in m_Item.m_Target, in context, out AudioSource source, out string why))
                return Refuse(why);
            slot = direct ? Layers.CreatePrivate(source, 0, 0, () => new AudioVolumeSlot(source)) : Layers.GetOrCreate(source, 0, 0, () => new AudioVolumeSlot(source));
            return true;
        }
    }

    internal sealed class AudioPitchBinding : ValueTargetBinding<float, FloatOps>
    {
        internal AudioPitchBinding(AudioPitchItem item) : base(item)
        {
        }

        protected override bool Acquire(in PrepareContext context, bool direct, out ValueSlot<float, FloatOps> slot)
        {
            slot = null;
            if (!AudioPrepare.Find(in m_Item.m_Target, in context, out AudioSource source, out string why))
                return Refuse(why);
            slot = direct ? Layers.CreatePrivate(source, 0, 0, () => new AudioPitchSlot(source)) : Layers.GetOrCreate(source, 0, 0, () => new AudioPitchSlot(source));
            return true;
        }
    }

    internal sealed class AudioStereoPanBinding : ValueTargetBinding<float, FloatOps>
    {
        internal AudioStereoPanBinding(AudioStereoPanItem item) : base(item)
        {
        }

        protected override bool Acquire(in PrepareContext context, bool direct, out ValueSlot<float, FloatOps> slot)
        {
            slot = null;
            if (!AudioPrepare.Find(in m_Item.m_Target, in context, out AudioSource source, out string why))
                return Refuse(why);
            slot = direct ? Layers.CreatePrivate(source, 0, 0, () => new AudioStereoPanSlot(source)) : Layers.GetOrCreate(source, 0, 0, () => new AudioStereoPanSlot(source));
            return true;
        }
    }

    // a filter that follows a distance curve is refused — a cutoff write replaces the curve with one key, and the property
    // reads the curve's first key only
    internal sealed class AudioLowPassCutoffBinding : ValueTargetBinding<float, FloatOps>
    {
        internal AudioLowPassCutoffBinding(AudioLowPassCutoffItem item) : base(item)
        {
        }

        protected override bool Acquire(in PrepareContext context, bool direct, out ValueSlot<float, FloatOps> slot)
        {
            slot = null;
            if (!AudioPrepare.Find(in m_Item.m_Target, in context, out AudioLowPassFilter filter, out string why))
                return Refuse(why);
            AnimationCurve curve = filter.customCutoffCurve;
            if (curve != null && curve.length > 1)
                return Refuse(AudioPrepare.DistanceCurve);
            slot = direct ? Layers.CreatePrivate(filter, 0, 0, () => new AudioLowPassCutoffSlot(filter)) : Layers.GetOrCreate(filter, 0, 0, () => new AudioLowPassCutoffSlot(filter));
            return true;
        }
    }

    internal sealed class AudioHighPassCutoffBinding : ValueTargetBinding<float, FloatOps>
    {
        internal AudioHighPassCutoffBinding(AudioHighPassCutoffItem item) : base(item)
        {
        }

        protected override bool Acquire(in PrepareContext context, bool direct, out ValueSlot<float, FloatOps> slot)
        {
            slot = null;
            if (!AudioPrepare.Find(in m_Item.m_Target, in context, out AudioHighPassFilter filter, out string why))
                return Refuse(why);
            slot = direct ? Layers.CreatePrivate(filter, 0, 0, () => new AudioHighPassCutoffSlot(filter)) : Layers.GetOrCreate(filter, 0, 0, () => new AudioHighPassCutoffSlot(filter));
            return true;
        }
    }

    internal sealed class AudioEchoWetMixBinding : ValueTargetBinding<float, FloatOps>
    {
        internal AudioEchoWetMixBinding(AudioEchoWetMixItem item) : base(item)
        {
        }

        protected override bool Acquire(in PrepareContext context, bool direct, out ValueSlot<float, FloatOps> slot)
        {
            slot = null;
            if (!AudioPrepare.Find(in m_Item.m_Target, in context, out AudioEchoFilter filter, out string why))
                return Refuse(why);
            slot = direct ? Layers.CreatePrivate(filter, 0, 0, () => new AudioEchoWetMixSlot(filter)) : Layers.GetOrCreate(filter, 0, 0, () => new AudioEchoWetMixSlot(filter));
            return true;
        }
    }

    internal sealed class AudioDistortionLevelBinding : ValueTargetBinding<float, FloatOps>
    {
        internal AudioDistortionLevelBinding(AudioDistortionLevelItem item) : base(item)
        {
        }

        protected override bool Acquire(in PrepareContext context, bool direct, out ValueSlot<float, FloatOps> slot)
        {
            slot = null;
            if (!AudioPrepare.Find(in m_Item.m_Target, in context, out AudioDistortionFilter filter, out string why))
                return Refuse(why);
            slot = direct ? Layers.CreatePrivate(filter, 0, 0, () => new AudioDistortionLevelSlot(filter)) : Layers.GetOrCreate(filter, 0, 0, () => new AudioDistortionLevelSlot(filter));
            return true;
        }
    }

    internal sealed class AudioReverbLevelBinding : ValueTargetBinding<float, FloatOps>
    {
        internal AudioReverbLevelBinding(AudioReverbLevelItem item) : base(item)
        {
        }

        protected override bool Acquire(in PrepareContext context, bool direct, out ValueSlot<float, FloatOps> slot)
        {
            slot = null;
            if (!AudioPrepare.Find(in m_Item.m_Target, in context, out AudioReverbFilter filter, out string why))
                return Refuse(why);
            slot = direct ? Layers.CreatePrivate(filter, 0, 0, () => new AudioReverbLevelSlot(filter)) : Layers.GetOrCreate(filter, 0, 0, () => new AudioReverbLevelSlot(filter));
            return true;
        }
    }

    // no target is read — the one slot is (null, 0) · a shared slot whatever the direct write (a shader global's form)
    internal sealed class AudioListenerVolumeBinding : ValueTargetBinding<float, FloatOps>
    {
        internal AudioListenerVolumeBinding(AudioListenerVolumeItem item) : base(item)
        {
        }

        protected override bool ReadsTarget => false;

        protected override bool Acquire(in PrepareContext context, bool direct, out ValueSlot<float, FloatOps> slot)
        {
            slot = null;
            if (AudioPrepare.Disabled)
                return Refuse(AudioPrepare.NoAudio);
            slot = Layers.GetOrCreate(null, 0, 0, () => new AudioListenerVolumeSlot());
            return true;
        }
    }

    // the debugger's item notes — constants, nothing made
    internal static class AudioNotes
    {
        internal const string Sounding = "sounding";
        internal const string PausedWithPlayer = "paused with the player";
        internal const string Paused = "paused";
    }

    internal enum AudioPlayState
    {
        Stopped = 0,
        Playing = 1,
        Paused = 2,
    }

    // a source's play state from the engine — a pause has no property: not playing with its place kept. A stop goes back to
    // 0; a clip's end goes back to 0, or to a place set by hand, and its first frame may still hold the clip's length — read
    // as stopped. Under the game's listener pause a source that keeps its place reads as playing — it goes on with
    // the listener. A source with no clip (none, or another resource) has no place to read — the engine warns on it — and
    // reads as stopped when it is not playing. A write that reads the same state does nothing (Play on a playing source
    // would start it over)
    internal static class AudioPlayStates
    {
        internal static AudioPlayState Read(AudioSource source)
        {
            if (source.isPlaying)
                return AudioPlayState.Playing;
            if (source.clip == null || source.timeSamples <= 0 || source.timeSamples >= source.clip.samples)
                return AudioPlayState.Stopped;
            return AudioListener.pause && !source.ignoreListenerPause ? AudioPlayState.Playing : AudioPlayState.Paused;
        }

        internal static void Write(AudioSource source, AudioPlayState state)
        {
            if (Read(source) == state)
                return;
            switch (state)
            {
                case AudioPlayState.Playing:
                    if (source.isActiveAndEnabled)
                        source.Play();
                    break;
                case AudioPlayState.Paused:
                    source.Pause();
                    break;
                default:
                    source.Stop();
                    break;
            }
        }
    }

    // Play · Stop · Pause · Unpause — Video Player Control's form: the play's start state (a Revert), the first (Restore · a
    // preview's end), the origin (a backward play). A sound this play started (Play · Unpause, forward, not swept) pauses
    // with the player's Pause, and stops when the play ends paused, aborts, or the binding is released
    internal sealed class AudioSourceControlBinding : ItemBinding
    {
        readonly AudioSourceControlItem m_Item;
        AudioSource m_Source;
        bool m_Preview;
        long[] m_Serials;
        bool[] m_HasStart;
        AudioPlayState[] m_Starts;
        bool[] m_Sounded;
        bool[] m_Paused;
        AudioPlayState m_Origin;
        AudioPlayState m_First;
        bool m_HasOrigin;
        bool m_HasFirst;

        internal AudioSourceControlBinding(AudioSourceControlItem item)
        {
            m_Item = item;
        }

        internal override bool EndsInstants => true;

        internal override bool Prepare(in PrepareContext context)
        {
            FeedbackMotion motion = m_Item.m_Motion;
            if (motion == null)
                return Refuse("no motion");
            if (!m_Item.Traits.Supports(motion.Kind))
            {
#if DEBUG
                return Refuse("the target does not take " + motion.Kind);
#else
                return Refuse(null);
#endif
            }
            if (!AudioPrepare.Find(in m_Item.m_Target, in context, out AudioSource source, out string why))
                return Refuse(why);
            m_Source = source;
            m_Preview = context.Context.Kind == ContextKind.Preview;
            int n = context.MaxOverlap;
            m_Serials = new long[n];
            m_HasStart = new bool[n];
            m_Starts = new AudioPlayState[n];
            m_Sounded = new bool[n];
            m_Paused = new bool[n];
            for (int i = 0; i < n; i++)
                m_Serials[i] = -1;
            return true;
        }

        internal override bool IsAlive => m_Source != null;

        internal override void Execute(int record, in BeginContext context)
        {
            bool fresh = m_Serials[record] != context.Serial || !m_HasStart[record];
            if (m_Serials[record] != context.Serial)
            {
                m_Serials[record] = context.Serial;
                m_HasStart[record] = false;
                m_Sounded[record] = false;
                m_Paused[record] = false;
            }
            if (m_Source == null)
                return;
            if (!m_HasStart[record])
            {
                m_Starts[record] = AudioPlayStates.Read(m_Source);
                m_HasStart[record] = true;
            }
            if (fresh)
            {
                if (!m_HasFirst)
                {
                    m_First = m_Starts[record];
                    m_HasFirst = true;
                }
                if (!context.Reverse)
                {
                    m_Origin = m_Starts[record];
                    m_HasOrigin = true;
                }
            }
            if (context.Reverse)
            {
                if (m_HasOrigin)
                    AudioPlayStates.Write(m_Source, m_Origin);
                m_Sounded[record] = false;
                return;
            }
            AudioSourceAction action = m_Item.m_Action;
            if (action == AudioSourceAction.Stop)
                m_Source.Stop();
            else if (action == AudioSourceAction.Pause)
                m_Source.Pause();
            else if (!context.Swept && m_Source.isActiveAndEnabled)
            {
                if (action == AudioSourceAction.Play)
                {
                    m_Source.Play();
                    m_Sounded[record] = true;
                }
                else
                {
                    // a source not paused is someone else's sound — Unpause owns only what it starts
                    bool paused = AudioPlayStates.Read(m_Source) == AudioPlayState.Paused;
                    m_Source.UnPause();
                    m_Sounded[record] |= paused;
                }
            }
        }

        internal override void Begin(int record, in BeginContext context)
        {
        }

        internal override void Evaluate(int record, in MotionTime time)
        {
        }

        internal override void Suspend(int record, bool paused)
        {
            if (m_Source == null || !m_Sounded[record])
                return;
            if (paused)
            {
                // a sound the game's listener pause holds reads as not playing, yet goes on with the listener — no member
                // tells that pause from the source's own, so under it the sound is taken as playing and paused, as a Play At
                // Point voice is
                if (m_Paused[record] || !(m_Source.isPlaying || (AudioListener.pause && !m_Source.ignoreListenerPause)))
                    return;
                m_Source.Pause();
                m_Paused[record] = true;
            }
            else if (m_Paused[record])
            {
                m_Paused[record] = false;
                m_Source.UnPause();
            }
        }

        // Revert — the start state · Aborted — the sound stops · the other ends leave it ringing, but one paused stops
        internal override void End(int record, EndReason reason)
        {
            if (m_Source == null)
                return;
            if (reason == EndReason.Revert)
            {
                if (m_HasStart[record])
                    AudioPlayStates.Write(m_Source, m_Starts[record]);
                m_Sounded[record] = false;
            }
            else if (reason == EndReason.Aborted || m_Paused[record])
            {
                if (m_Sounded[record])
                    m_Source.Stop();
                m_Sounded[record] = false;
            }
            m_Paused[record] = false;
        }

        internal override void Restore()
        {
            if (m_HasFirst && m_Source != null)
                AudioPlayStates.Write(m_Source, m_First);
            m_HasOrigin = false;
            m_HasFirst = false;
        }

        internal override void Inherit(ItemBinding old)
        {
            if (!(old is AudioSourceControlBinding from) || m_Source == null || !ReferenceEquals(m_Source, from.m_Source))
                return;
            m_Origin = from.m_Origin;
            m_HasOrigin = from.m_HasOrigin;
            m_First = from.m_First;
            m_HasFirst = from.m_HasFirst;
        }

        // the record's sound paused with the player, or playing (its mark alone outlives a sound that reached its end)
        internal override string DebugNote(int record)
        {
            if (m_Source == null)
                return null;
            if (m_Paused[record])
                return AudioNotes.PausedWithPlayer;
            return m_Sounded[record] && m_Source.isPlaying ? AudioNotes.Sounding : null;
        }

        // the sounds it started stop — a preview's end puts the first state back instead
        internal override void Release()
        {
            if (m_Source == null)
                return;
            if (m_Preview)
            {
                if (m_HasFirst)
                    AudioPlayStates.Write(m_Source, m_First);
            }
            else
            {
                for (int i = 0; i < m_Sounded.Length; i++)
                {
                    if (!m_Sounded[i])
                        continue;
                    m_Source.Stop();
                    break;
                }
            }
            m_Source = null;
        }
    }

    // a one-shot — forward, not swept, an enabled source, a volume above 0 (the item's × the play's intensity × the category's
    // scale). No sound of its own: a one-shot can't be stopped alone. A preview stops the source at its end when the source
    // was idle at the first one-shot
    internal sealed class AudioPlayClipBinding : ItemBinding
    {
        readonly AudioPlayClipItem m_Item;
        AudioSource m_Source;
        AudioClip m_Clip;
        CategoryGate m_Gate;
        FeedbackCategory m_Category;
        bool m_Preview;
        bool m_Touched;
        bool m_WasIdle;

        internal AudioPlayClipBinding(AudioPlayClipItem item)
        {
            m_Item = item;
        }

        internal override bool Prepare(in PrepareContext context)
        {
            FeedbackMotion motion = m_Item.m_Motion;
            if (motion == null)
                return Refuse("no motion");
            if (!m_Item.Traits.Supports(motion.Kind))
            {
#if DEBUG
                return Refuse("the target does not take " + motion.Kind);
#else
                return Refuse(null);
#endif
            }
            if (!AudioPrepare.Find(in m_Item.m_Target, in context, out AudioSource source, out string why))
                return Refuse(why);
            if (m_Item.m_Clip == null)
                return Refuse(AudioPrepare.NoClip);
            m_Clip = m_Item.m_Clip as AudioClip;
            if (m_Clip == null)
                return Refuse(AudioPrepare.NotAClip);
            m_Source = source;
            m_Gate = context.Context.Categories;
            m_Category = m_Item.EffectiveCategory;
            m_Preview = context.Context.Kind == ContextKind.Preview;
            return true;
        }

        internal override bool IsAlive => m_Source != null;

        internal override void Execute(int record, in BeginContext context)
        {
            if (context.Reverse || context.Swept || m_Source == null || !m_Source.isActiveAndEnabled)
                return;
            float volume = AudioMath.OneShotVolume(m_Item.m_Volume, context.Intensity, m_Gate.Scale(m_Category));
            if (volume <= 0f)
                return;
            if (m_Preview && !m_Touched)
            {
                m_Touched = true;
                m_WasIdle = !m_Source.isPlaying;
            }
            m_Source.PlayOneShot(m_Clip, volume);
        }

        internal override void Begin(int record, in BeginContext context)
        {
        }

        internal override void Evaluate(int record, in MotionTime time)
        {
        }

        internal override void End(int record, EndReason reason)
        {
        }

        internal override void Release()
        {
            if (m_Preview && m_Touched && m_WasIdle && m_Source != null)
                m_Source.Stop();
            m_Source = null;
        }
    }

    // an Audio Mixer's exposed parameter — no target is read; the slot is the mixer's and the name's. An unknown name is
    // refused here (Edit mode reads it too): a mixer's call with one allocates every time
    internal sealed class AudioMixerParameterBinding : ValueTargetBinding<float, FloatOps>
    {
        readonly AudioMixerParameterItem m_Global;

        internal AudioMixerParameterBinding(AudioMixerParameterItem item) : base(item) => m_Global = item;

        protected override bool ReadsTarget => false;

        protected override bool Acquire(in PrepareContext context, bool direct, out ValueSlot<float, FloatOps> slot)
        {
            slot = null;
            if (AudioPrepare.Disabled)
                return Refuse(AudioPrepare.NoAudio);
            if (m_Global.m_Mixer == null)
                return Refuse(AudioPrepare.NoMixer);
            var mixer = m_Global.m_Mixer as AudioMixer;
            if (mixer == null)
                return Refuse(AudioPrepare.NotAMixer);
            string name = m_Global.m_Parameter;
            if (string.IsNullOrEmpty(name))
                return Refuse(AudioPrepare.NoParameter);
            if (!mixer.GetFloat(name, out _))
                return Refuse("no exposed parameter '" + name + "' on " + mixer.name);
            int id = AudioMixerNames.Id(name);
            slot = direct ? Layers.CreatePrivate(mixer, id, 0, () => new AudioMixerParameterSlot(mixer, name)) : Layers.GetOrCreate(mixer, id, 0, () => new AudioMixerParameterSlot(mixer, name));
            return true;
        }
    }

    // a transition to a snapshot — forward (a sweep too: the mix is a state, not a sound) when the Audio category's scale is
    // above 0. A Revert, Restore() and a play going backward go to Back To once this item has transitioned — a skipped
    // transition leaves the snapshot someone else chose. A paused player does not hold a transition. Nothing in a preview
    internal sealed class AudioMixerSnapshotBinding : ItemBinding
    {
        readonly AudioMixerSnapshotItem m_Item;
        AudioMixerSnapshot m_Snapshot;
        AudioMixerSnapshot m_BackTo;
        CategoryGate m_Gate;
        FeedbackCategory m_Category;
        bool m_Preview;
        long[] m_Serials;
        bool[] m_Moved;
        bool m_Ever;

        internal AudioMixerSnapshotBinding(AudioMixerSnapshotItem item)
        {
            m_Item = item;
        }

        internal override bool Prepare(in PrepareContext context)
        {
            FeedbackMotion motion = m_Item.m_Motion;
            if (motion == null)
                return Refuse("no motion");
            if (!m_Item.Traits.Supports(motion.Kind))
            {
#if DEBUG
                return Refuse("the target does not take " + motion.Kind);
#else
                return Refuse(null);
#endif
            }
            if (AudioPrepare.Disabled)
                return Refuse(AudioPrepare.NoAudio);
            if (m_Item.m_Snapshot == null)
                return Refuse(AudioPrepare.NoSnapshot);
            var snapshot = m_Item.m_Snapshot as AudioMixerSnapshot;
            if (snapshot == null)
                return Refuse(AudioPrepare.NotASnapshot);
            AudioMixerSnapshot back = null;
            if (m_Item.m_BackTo != null)
            {
                back = m_Item.m_BackTo as AudioMixerSnapshot;
                if (back == null)
                    return Refuse(AudioPrepare.BackToNotASnapshot);
                if (back.audioMixer != snapshot.audioMixer)
                    return Refuse(AudioPrepare.BackToOtherMixer);
            }
            m_Snapshot = snapshot;
            m_BackTo = back;
            m_Gate = context.Context.Categories;
            m_Category = m_Item.EffectiveCategory;
            m_Preview = context.Context.Kind == ContextKind.Preview;
            int n = context.MaxOverlap;
            m_Serials = new long[n];
            m_Moved = new bool[n];
            for (int i = 0; i < n; i++)
                m_Serials[i] = -1;
            return true;
        }

        internal override bool IsAlive => m_Snapshot != null;

        float Seconds => Math.Max(0f, m_Item.m_TransitionTime);

        internal override void Execute(int record, in BeginContext context)
        {
            if (m_Serials[record] != context.Serial)
            {
                m_Serials[record] = context.Serial;
                m_Moved[record] = false;
            }
            if (m_Preview || m_Snapshot == null)
                return;
            if (context.Reverse)
            {
                if (m_Ever && m_BackTo != null)
                    m_BackTo.TransitionTo(Seconds);
                m_Moved[record] = false;
                return;
            }
            if (m_Gate.Scale(m_Category) <= 0f)
                return;
            m_Snapshot.TransitionTo(Seconds);
            m_Moved[record] = true;
            m_Ever = true;
        }

        internal override void Begin(int record, in BeginContext context)
        {
        }

        internal override void Evaluate(int record, in MotionTime time)
        {
        }

        internal override void End(int record, EndReason reason)
        {
            if (reason != EndReason.Revert || !m_Moved[record])
                return;
            m_Moved[record] = false;
            if (m_BackTo != null && m_Snapshot != null)
                m_BackTo.TransitionTo(Seconds);
        }

        internal override void Restore()
        {
            if (m_Ever && m_BackTo != null && m_Snapshot != null)
                m_BackTo.TransitionTo(Seconds);
            m_Ever = false;
            for (int i = 0; i < m_Moved.Length; i++)
                m_Moved[i] = false;
        }

        internal override void Inherit(ItemBinding old)
        {
            if (old is AudioMixerSnapshotBinding from && m_BackTo != null && m_BackTo == from.m_BackTo)
                m_Ever = from.m_Ever;
        }

        internal override void Release()
        {
            m_Snapshot = null;
        }
    }

    // a pooled voice at a place — AudioVoiceService keeps it (its end · Stop After · the fade out · the pause), the pools make
    // and keep the instances. Forward, not swept, a volume above 0: a voice taken, set and played. A Revert or an abort stops
    // the record's voices, a play ending paused stops its paused ones, a backward play stops the origin play's, Restore() all;
    // a released binding leaves them ringing
    internal sealed class AudioPlayAtPointBinding : ItemBinding
    {
        readonly AudioPlayAtPointItem m_Item;
        FeedbackContext m_Context;
        AudioVoiceService m_Voices;
        GameObject m_Template;
        Transform m_Target;
        AudioResource m_Resource;
        AudioMixerGroup m_Output;
        CategoryGate m_Gate;
        FeedbackCategory m_Category;
        bool m_Unscaled;
        long[] m_Serials;
        long m_Origin = -1;

        internal AudioPlayAtPointBinding(AudioPlayAtPointItem item)
        {
            m_Item = item;
        }

        internal override bool EndsInstants => true;

        internal override bool Prepare(in PrepareContext context)
        {
            FeedbackMotion motion = m_Item.m_Motion;
            if (motion == null)
                return Refuse("no motion");
            if (!m_Item.Traits.Supports(motion.Kind))
            {
#if DEBUG
                return Refuse("the target does not take " + motion.Kind);
#else
                return Refuse(null);
#endif
            }
            if (AudioPrepare.Disabled)
                return Refuse(AudioPrepare.NoAudio);
            m_Target = TargetResolver.Find<Transform>(in m_Item.m_Target, in context, out TargetFailure failure);
            if (m_Target == null)
                return Refuse(TargetResolver.Describe(failure));
            if (m_Item.m_Resource == null)
                return Refuse(AudioPrepare.NoResource);
            m_Resource = m_Item.m_Resource as AudioResource;
            if (m_Resource == null)
                return Refuse(AudioPrepare.NotAResource);
            if (!Output(in context, out m_Output, out string why))
                return Refuse(why);
            m_Context = context.Context;
            m_Voices = AudioVoiceService.Of(m_Context);
            if (m_Voices == null)
                return Refuse(AudioPrepare.NoVoiceService);
            m_Template = m_Voices.Template;
            int size = Math.Max(1, m_Item.m_PoolSize);
            m_Context.Pools.Reserve(m_Template, size);
            m_Voices.ReserveLive(size);
            m_Gate = m_Context.Categories;
            m_Category = m_Item.EffectiveCategory;
            ItemClock clock = m_Item.EffectiveClock;
            m_Unscaled = clock == ItemClock.Unscaled || (clock == ItemClock.Player && context.Player != null && context.Player.m_TimeMode == PlayerTimeMode.Unscaled);
            int n = context.MaxOverlap;
            m_Serials = new long[n];
            for (int i = 0; i < n; i++)
                m_Serials[i] = -1;
            return true;
        }

        // the item's output, else the player's Audio Output, else the project's Default Audio Output — none: no group
        bool Output(in PrepareContext context, out AudioMixerGroup group, out string why)
        {
            group = null;
            why = null;
            UnityEngine.Object output = m_Item.m_Output;
            string wrong = AudioPrepare.NotAGroup;
            if (output == null && context.Player != null)
            {
                output = context.Player.m_AudioOutput;
                wrong = AudioPrepare.PlayerOutputNotAGroup;
            }
            if (output == null)
            {
                output = context.Context.Settings.AudioOutput;
                wrong = AudioPrepare.ProjectOutputNotAGroup;
            }
            if (output == null)
                return true;
            group = output as AudioMixerGroup;
            if (group != null)
                return true;
            why = wrong;
            return false;
        }

        internal override bool IsAlive => m_Target != null && m_Voices != null && !m_Voices.IsClosed;

        internal override void Execute(int record, in BeginContext context)
        {
            if (context.Reverse)
            {
                if (m_Serials[record] != context.Serial)
                    m_Serials[record] = -1;
                if (m_Origin >= 0)
                    m_Voices?.Stop(this, -1, m_Origin, false);
                return;
            }
            if (m_Serials[record] != context.Serial)
            {
                m_Serials[record] = context.Serial;
                m_Origin = context.Serial;
            }
            if (context.Swept || m_Target == null || m_Voices == null || m_Voices.IsClosed)
                return;
            float volume = AudioMath.VoiceVolume(m_Item.m_Volume, context.Intensity, m_Gate.Scale(m_Category));
            if (volume <= 0f)
                return;
            Vector3 position = context.HasPosition ? context.Position : m_Target.TransformPoint(m_Item.m_Offset);
            GameObject instance = m_Context.Pools.Spawn(m_Template, true, position, Quaternion.identity, null, 0f, m_Unscaled, null, 0, 0, null, AudioVoiceService.Held);
            if (instance == null)
                return;
            if (!instance.TryGetComponent(out AudioSource source))
            {
                m_Context.Pools.TryReturn(instance);
                return;
            }
            source.resource = m_Resource;
            source.outputAudioMixerGroup = m_Output;
            source.volume = volume;
            source.pitch = AudioMath.VoicePitch(m_Item.m_Pitch, m_Item.m_PitchRandom, context.ItemSeed, context.Occurrence);
            source.spatialBlend = m_Item.m_SpatialBlend;
            source.minDistance = m_Item.m_MinDistance;
            source.maxDistance = m_Item.m_MaxDistance;
            source.ignoreListenerPause = m_Item.m_IgnoreListenerPause;
            source.Play();
            m_Voices.Launch(instance, source, this, record, context.Serial, m_Unscaled, volume, m_Item.m_StopAfter, m_Item.m_FadeOut);
        }

        internal override void Begin(int record, in BeginContext context)
        {
        }

        internal override void Evaluate(int record, in MotionTime time)
        {
        }

        internal override void Suspend(int record, bool paused)
        {
            if (m_Voices != null && m_Serials[record] >= 0)
                m_Voices.Suspend(this, record, m_Serials[record], paused);
        }

        // Revert · Aborted — the record's voices stop · the other ends leave them ringing, but one paused stops
        internal override void End(int record, EndReason reason)
        {
            if (m_Voices != null && m_Serials[record] >= 0)
                m_Voices.Stop(this, record, m_Serials[record], reason != EndReason.Revert && reason != EndReason.Aborted);
        }

        internal override void Restore()
        {
            m_Voices?.Stop(this, -1, -1, false);
            m_Origin = -1;
        }

        internal override void Inherit(ItemBinding old)
        {
            if (!(old is AudioPlayAtPointBinding from) || m_Voices == null || from.m_Voices != m_Voices)
                return;
            m_Voices.Reown(from, this);
            m_Origin = from.m_Origin;
        }

        internal override void Release()
        {
            m_Voices?.Disown(this);
        }

        // the record's voices — one playing, or all paused with the player
        internal override string DebugNote(int record)
        {
            if (m_Voices == null || m_Serials[record] < 0)
                return null;
            int live = m_Voices.VoicesOf(this, record, m_Serials[record], out int paused);
            return live == 0 ? null : paused == live ? AudioNotes.Paused : AudioNotes.Sounding;
        }
    }
}
