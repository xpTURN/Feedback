using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

namespace xpTURN.Feedback
{
    // the mixer takes a source's pitch, the listener's volume and the high pass, echo, distortion and reverb values when they
    // are written, not at the frame's end (a volume, a pan and a low pass it takes there — they are held the same way): a
    // revert at the frame's start would be heard until the apply writes the composition again. While contributions remain,
    // the revert keeps the value written and the apply writes over it — or the base, when it writes nothing and no one wrote
    // meanwhile. The slots renormalize, so the value kept reads as the base
    internal struct HeldRevert
    {
        bool m_Holding;
        bool m_Owed;
        float m_Base;
        float m_Left;

        // around the slot's revert — held only when the same frame's apply follows (a live contribution, the registry open,
        // no release waiting)
        internal void Begin(LayerSlot slot) => m_Holding = slot.LiveContributions > 0 && !slot.ReleasePending && slot.Registry != null && !slot.Registry.IsClosed;

        internal void End() => m_Holding = false;

        // the restore — true when it is held (the value left is the composition)
        internal bool Hold(float baseValue, float now)
        {
            if (!m_Holding)
                return false;
            m_Owed = true;
            m_Base = baseValue;
            m_Left = now;
            return true;
        }

        // after the apply — true with the base to write when the apply wrote nothing and the value is still the one left
        internal bool Owed(bool wrote, float now, out float baseValue)
        {
            baseValue = m_Base;
            if (!m_Owed)
                return false;
            m_Owed = false;
            return !wrote && BitConverter.SingleToInt32Bits(now) == BitConverter.SingleToInt32Bits(m_Left);
        }
    }

    // an Audio Source's volume — the engine keeps 0 to 1; the judgment on the value read back takes that in
    [RecordPaths(typeof(AudioSource), "m_Volume")]
    internal sealed class AudioVolumeSlot : ValueSlot<float, FloatOps>
    {
        readonly AudioSource m_Target;
        HeldRevert m_Held;

        internal AudioVolumeSlot(AudioSource target) : base(ConsumptionStage.LateApply, Composition.Additive, true) => m_Target = target;

        internal override bool IsAlive => m_Target != null;

        internal override void Revert()
        {
            m_Held.Begin(this);
            base.Revert();
            m_Held.End();
        }

        internal override void Apply()
        {
            base.Apply();
            if (m_Held.Owed(Pending, m_Target.volume, out float b))
                Put(b);
        }

        protected override float Read() => m_Target.volume;

        protected override void Write(in float value) => m_Target.volume = value;

        protected override void Restore(in float baseValue)
        {
            if (!m_Held.Hold(baseValue, m_Target.volume))
                Put(baseValue);
        }

        void Put(float value) => m_Target.volume = value;
    }

    // the engine takes a pitch past ±3 — the composition and a Persist are kept to −3 to 3, a revert writes the base as it was
    [RecordPaths(typeof(AudioSource), "m_Pitch")]
    internal sealed class AudioPitchSlot : ValueSlot<float, FloatOps>
    {
        readonly AudioSource m_Target;
        HeldRevert m_Held;

        internal AudioPitchSlot(AudioSource target) : base(ConsumptionStage.LateApply, Composition.Additive, true) => m_Target = target;

        internal override bool IsAlive => m_Target != null;

        internal override void Revert()
        {
            m_Held.Begin(this);
            base.Revert();
            m_Held.End();
        }

        internal override void Apply()
        {
            base.Apply();
            if (m_Held.Owed(Pending, m_Target.pitch, out float b))
                Put(b);
        }

        protected override float Read() => m_Target.pitch;

        protected override void Write(in float value) => m_Target.pitch = Mathf.Clamp(value, -3f, 3f);

        protected override void Restore(in float baseValue)
        {
            if (!m_Held.Hold(baseValue, m_Target.pitch))
                Put(baseValue);
        }

        void Put(float value) => m_Target.pitch = value;
    }

    // the engine keeps −1 to 1
    [RecordPaths(typeof(AudioSource), "Pan2D")]
    internal sealed class AudioStereoPanSlot : ValueSlot<float, FloatOps>
    {
        readonly AudioSource m_Target;
        HeldRevert m_Held;

        internal AudioStereoPanSlot(AudioSource target) : base(ConsumptionStage.LateApply, Composition.Additive, true) => m_Target = target;

        internal override bool IsAlive => m_Target != null;

        internal override void Revert()
        {
            m_Held.Begin(this);
            base.Revert();
            m_Held.End();
        }

        internal override void Apply()
        {
            base.Apply();
            if (m_Held.Owed(Pending, m_Target.panStereo, out float b))
                Put(b);
        }

        protected override float Read() => m_Target.panStereo;

        protected override void Write(in float value) => m_Target.panStereo = value;

        protected override void Restore(in float baseValue)
        {
            if (!m_Held.Hold(baseValue, m_Target.panStereo))
                Put(baseValue);
        }

        void Put(float value) => m_Target.panStereo = value;
    }

    // the engine keeps 10 to 22000 Hz and stores the cutoff in the filter's curve, which the preview's recorder can't take:
    // the slot puts its own values back — an offset's revert as every slot, a preview's Persist the first value when the
    // preview ends
    [NotRecorded("the cutoff is stored in a curve — the slot writes the base back")]
    internal sealed class AudioLowPassCutoffSlot : ValueSlot<float, FloatOps>
    {
        readonly AudioLowPassFilter m_Target;
        HeldRevert m_Held;
        bool m_Preview;
        bool m_Persisted;
        float m_First;

        internal AudioLowPassCutoffSlot(AudioLowPassFilter target) : base(ConsumptionStage.LateApply, Composition.Additive, true) => m_Target = target;

        internal override bool IsAlive => m_Target != null;

        internal override void Revert()
        {
            m_Held.Begin(this);
            base.Revert();
            m_Held.End();
        }

        internal override void Apply()
        {
            base.Apply();
            if (m_Held.Owed(Pending, m_Target.cutoffFrequency, out float b))
                Put(b);
        }

        internal override void Attached() => m_Preview = Registry != null && Registry.IsPreview;

        protected override float Read() => m_Target.cutoffFrequency;

        protected override void Write(in float value) => m_Target.cutoffFrequency = value;

        protected override void Restore(in float baseValue)
        {
            if (!m_Held.Hold(baseValue, m_Target.cutoffFrequency))
                Put(baseValue);
        }

        void Put(float value) => m_Target.cutoffFrequency = value;

        protected override void WriteDirect(in float value)
        {
            if (m_Preview && !m_Persisted)
            {
                m_First = m_Target.cutoffFrequency;
                m_Persisted = true;
            }
            m_Target.cutoffFrequency = value;
        }

        internal override void Dropped()
        {
            if (!m_Persisted || m_Target == null)
                return;
            m_Persisted = false;
            m_Target.cutoffFrequency = m_First;
        }
    }

    // the engine takes any cutoff — the composition and a Persist are kept to 10 to 22000 Hz, a revert writes the base as it was
    [RecordPaths(typeof(AudioHighPassFilter), "m_CutoffFrequency")]
    internal sealed class AudioHighPassCutoffSlot : ValueSlot<float, FloatOps>
    {
        readonly AudioHighPassFilter m_Target;
        HeldRevert m_Held;

        internal AudioHighPassCutoffSlot(AudioHighPassFilter target) : base(ConsumptionStage.LateApply, Composition.Additive, true) => m_Target = target;

        internal override bool IsAlive => m_Target != null;

        internal override void Revert()
        {
            m_Held.Begin(this);
            base.Revert();
            m_Held.End();
        }

        internal override void Apply()
        {
            base.Apply();
            if (m_Held.Owed(Pending, m_Target.cutoffFrequency, out float b))
                Put(b);
        }

        protected override float Read() => m_Target.cutoffFrequency;

        protected override void Write(in float value) => m_Target.cutoffFrequency = Mathf.Clamp(value, 10f, 22000f);

        protected override void Restore(in float baseValue)
        {
            if (!m_Held.Hold(baseValue, m_Target.cutoffFrequency))
                Put(baseValue);
        }

        void Put(float value) => m_Target.cutoffFrequency = value;
    }

    // kept to 0 to 1 — a revert writes the base as it was
    [RecordPaths(typeof(AudioEchoFilter), "m_WetMix")]
    internal sealed class AudioEchoWetMixSlot : ValueSlot<float, FloatOps>
    {
        readonly AudioEchoFilter m_Target;
        HeldRevert m_Held;

        internal AudioEchoWetMixSlot(AudioEchoFilter target) : base(ConsumptionStage.LateApply, Composition.Additive, true) => m_Target = target;

        internal override bool IsAlive => m_Target != null;

        internal override void Revert()
        {
            m_Held.Begin(this);
            base.Revert();
            m_Held.End();
        }

        internal override void Apply()
        {
            base.Apply();
            if (m_Held.Owed(Pending, m_Target.wetMix, out float b))
                Put(b);
        }

        protected override float Read() => m_Target.wetMix;

        protected override void Write(in float value) => m_Target.wetMix = Mathf.Clamp01(value);

        protected override void Restore(in float baseValue)
        {
            if (!m_Held.Hold(baseValue, m_Target.wetMix))
                Put(baseValue);
        }

        void Put(float value) => m_Target.wetMix = value;
    }

    // kept to 0 to 1 — a revert writes the base as it was
    [RecordPaths(typeof(AudioDistortionFilter), "m_DistortionLevel")]
    internal sealed class AudioDistortionLevelSlot : ValueSlot<float, FloatOps>
    {
        readonly AudioDistortionFilter m_Target;
        HeldRevert m_Held;

        internal AudioDistortionLevelSlot(AudioDistortionFilter target) : base(ConsumptionStage.LateApply, Composition.Additive, true) => m_Target = target;

        internal override bool IsAlive => m_Target != null;

        internal override void Revert()
        {
            m_Held.Begin(this);
            base.Revert();
            m_Held.End();
        }

        internal override void Apply()
        {
            base.Apply();
            if (m_Held.Owed(Pending, m_Target.distortionLevel, out float b))
                Put(b);
        }

        protected override float Read() => m_Target.distortionLevel;

        protected override void Write(in float value) => m_Target.distortionLevel = Mathf.Clamp01(value);

        protected override void Restore(in float baseValue)
        {
            if (!m_Held.Hold(baseValue, m_Target.distortionLevel))
                Put(baseValue);
        }

        void Put(float value) => m_Target.distortionLevel = value;
    }

    // the engine drops a level write unless the preset is User: the first write of an episode turns another preset to User,
    // the revert writes the base and turns it back (its fourteen values come back with it). A Persist keeps User — an episode's
    // turn becomes the Persist's, so a later revert keeps the value; a preview's Persist turns the preset back when the preview
    // ends. Kept to −10000 to 2000 mB — a revert writes the base as it was. Another's write while turned leaves the turn: the
    // next revert turns the preset back
    [RecordPaths(typeof(AudioReverbFilter), "m_ReverbLevel")]
    internal sealed class AudioReverbLevelSlot : ValueSlot<float, FloatOps>
    {
        readonly AudioReverbFilter m_Target;
        HeldRevert m_Held;
        bool m_Preview;
        AudioReverbPreset m_HeldPreset;
        bool m_Turned;
        AudioReverbPreset m_PersistHeld;
        bool m_PersistTurned;

        internal AudioReverbLevelSlot(AudioReverbFilter target) : base(ConsumptionStage.LateApply, Composition.Additive, true) => m_Target = target;

        internal override bool IsAlive => m_Target != null;

        internal override void Revert()
        {
            m_Held.Begin(this);
            base.Revert();
            m_Held.End();
        }

        internal override void Apply()
        {
            base.Apply();
            if (m_Held.Owed(Pending, m_Target.reverbLevel, out float b))
                Put(b);
        }

        internal override void Attached() => m_Preview = Registry != null && Registry.IsPreview;

        protected override float Read() => m_Target.reverbLevel;

        protected override void Write(in float value) => m_Target.reverbLevel = Mathf.Clamp(value, -10000f, 2000f);

        protected override void BeforeWrite()
        {
            if (m_Turned || m_Target.reverbPreset == AudioReverbPreset.User)
                return;
            m_HeldPreset = m_Target.reverbPreset;
            m_Target.reverbPreset = AudioReverbPreset.User;
            m_Turned = true;
        }

        protected override void Restore(in float baseValue)
        {
            if (!m_Held.Hold(baseValue, m_Target.reverbLevel))
                Put(baseValue);
        }

        void Put(float value)
        {
            m_Target.reverbLevel = value;
            if (!m_Turned)
                return;
            m_Turned = false;
            m_Target.reverbPreset = m_HeldPreset;
        }

        protected override void WriteDirect(in float value)
        {
            if (m_Turned)
            {
                m_Turned = false;
                HoldForPreview(m_HeldPreset);
            }
            else if (m_Target.reverbPreset != AudioReverbPreset.User)
            {
                HoldForPreview(m_Target.reverbPreset);
                m_Target.reverbPreset = AudioReverbPreset.User;
            }
            Write(value);
        }

        void HoldForPreview(AudioReverbPreset preset)
        {
            if (!m_Preview || m_PersistTurned)
                return;
            m_PersistHeld = preset;
            m_PersistTurned = true;
        }

        internal override void Dropped()
        {
            if (!m_Preview || m_Target == null)
                return;
            if (m_Turned)
            {
                m_Turned = false;
                m_Target.reverbPreset = m_HeldPreset;
            }
            if (m_PersistTurned)
            {
                m_PersistTurned = false;
                m_Target.reverbPreset = m_PersistHeld;
            }
        }
    }

    // the listener's volume — the engine takes any value: kept to 0 to 1, a revert writes the base as it was. Not written in a
    // preview (the editor's whole sound) — the value read back is the base, so the judgment holds
    [NotRecorded("not serialized — the editor's whole sound")]
    internal sealed class AudioListenerVolumeSlot : ValueSlot<float, FloatOps>
    {
        HeldRevert m_Held;
        bool m_Preview;

        internal AudioListenerVolumeSlot() : base(ConsumptionStage.LateApply, Composition.Additive, true)
        {
        }

        internal override bool IsAlive => true;

        internal override void Revert()
        {
            m_Held.Begin(this);
            base.Revert();
            m_Held.End();
        }

        internal override void Apply()
        {
            base.Apply();
            if (m_Held.Owed(Pending, AudioListener.volume, out float b))
                Put(b);
        }

        internal override void Attached() => m_Preview = Registry != null && Registry.IsPreview;

        protected override float Read() => AudioListener.volume;

        protected override void Write(in float value)
        {
            if (!m_Preview)
                AudioListener.volume = Mathf.Clamp01(value);
        }

        protected override void Restore(in float baseValue)
        {
            if (!m_Held.Hold(baseValue, AudioListener.volume))
                Put(baseValue);
        }

        void Put(float value)
        {
            if (!m_Preview)
                AudioListener.volume = value;
        }
    }

    // an exposed parameter's number — the slot's key on its mixer. Names to numbers on the preparation path; a hash could give
    // two names one slot
    internal static class AudioMixerNames
    {
        static readonly Dictionary<string, int> s_Ids = new Dictionary<string, int>(StringComparer.Ordinal);

        internal static int Id(string name)
        {
            if (!s_Ids.TryGetValue(name, out int id))
            {
                id = s_Ids.Count + 1;
                s_Ids.Add(name, id);
            }
            return id;
        }
    }

    // an Audio Mixer's exposed parameter. The mixer takes it at the frame's end, and a write later in the frame wins over a
    // clear: each revert clears it (the snapshot has it again) and the apply writes the composition — nothing waits, and the
    // end is the last revert's clear. Read back after a clear, the value is the one the last audio update took until the
    // next; the slot renormalizes, so that reads as the base. While a Persist holds the parameter the revert writes the base (a clear would
    // drop the Persist's value), and a Persist back to the value it found clears. Not written in a preview — the editor's
    // mixer takes no write
    [NotRecorded("not serialized — the mixer's value while it plays; the snapshots keep theirs")]
    internal sealed class AudioMixerParameterSlot : ValueSlot<float, FloatOps>
    {
        readonly AudioMixer m_Mixer;
        readonly string m_Name;
        bool m_Preview;
        bool m_Persisted;
        float m_Origin;

        internal AudioMixerParameterSlot(AudioMixer mixer, string name) : base(ConsumptionStage.LateApply, Composition.Additive, true)
        {
            m_Mixer = mixer;
            m_Name = name;
        }

        internal override bool IsAlive => m_Mixer != null;

        internal override void Attached() => m_Preview = Registry != null && Registry.IsPreview;

        protected override float Read()
        {
            m_Mixer.GetFloat(m_Name, out float value);
            return value;
        }

        protected override void Write(in float value)
        {
            if (!m_Preview)
                m_Mixer.SetFloat(m_Name, value);
        }

        protected override void Restore(in float baseValue)
        {
            if (m_Preview)
                return;
            if (m_Persisted)
                m_Mixer.SetFloat(m_Name, baseValue);
            else
                m_Mixer.ClearFloat(m_Name);
        }

        // the first Persist keeps the value before it — the base, not the read: in a composed frame the read is still the
        // composition after the revert's clear
        protected override void WriteDirect(in float value)
        {
            if (m_Preview)
                return;
            if (!m_Persisted)
            {
                m_Origin = Base;
                m_Persisted = true;
            }
            else if (BitConverter.SingleToInt32Bits(value) == BitConverter.SingleToInt32Bits(m_Origin))
            {
                m_Persisted = false;
                m_Mixer.ClearFloat(m_Name);
                return;
            }
            m_Mixer.SetFloat(m_Name, value);
        }
    }
}
