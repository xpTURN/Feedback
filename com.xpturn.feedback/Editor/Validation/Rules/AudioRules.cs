using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Audio;
using Object = UnityEngine.Object;

namespace xpTURN.Feedback.Editor
{
    // group T — the audio items (the editor names the module's types even when the module is off). The prepare's checks are the
    // Audio assembly's, which the editor does not reference: the rules make the same checks with the engine's API. Quiet for an
    // item without its implementation (no audio module — FB-B speaks)
    internal static class AudioRuleReads
    {
        internal const string Skipped = " — the item is skipped";
        internal const string AudioManagerPath = "ProjectSettings/AudioManager.asset";

        // the object holding Project Settings > Audio's Disable Unity Audio — a seam for the tests
        internal static Func<Object> AudioManager = () =>
        {
            Object[] all = AssetDatabase.LoadAllAssetsAtPath(AudioManagerPath);
            return all.Length > 0 ? all[0] : null;
        };

        // the setting builds follow — in the editor turning it off takes a restart, turning it on does not
        internal static bool AudioOff()
        {
            Object settings = AudioManager();
            SerializedProperty p = settings != null ? new SerializedObject(settings).FindProperty("m_DisableAudio") : null;
            return p != null && p.boolValue;
        }

        internal static bool Implemented(FeedbackItem item) => item != null && item.m_Active && FeedbackImplementations.IsImplemented(item.GetType());

        // a mixer's exposed parameters from its serialized data — GetFloat warns on every name a mixer does not expose. Null when
        // the data does not read
        internal static List<string> ExposedNames(AudioMixer mixer)
        {
            SerializedProperty list = mixer != null ? new SerializedObject(mixer).FindProperty("m_ExposedParameters") : null;
            if (list == null || !list.isArray)
                return null;
            var names = new List<string>(list.arraySize);
            for (int i = 0; i < list.arraySize; i++)
            {
                SerializedProperty name = list.GetArrayElementAtIndex(i).FindPropertyRelative("name");
                if (name == null)
                    return null;
                names.Add(name.stringValue);
            }
            return names;
        }

        // a container's loop mode (0 Infinite · 1 Clips · 2 Cycles) from its serialized data — −1 for a clip or when it does
        // not read
        internal static int LoopMode(AudioResource resource)
        {
            if (resource == null || resource is AudioClip)
                return -1;
            SerializedProperty p = new SerializedObject(resource).FindProperty("m_LoopMode");
            return p != null ? p.intValue : -1;
        }

        // off — the component, or an object up from its own (in a prefab asset isActiveAndEnabled is false for every component)
        internal static bool Off(Behaviour b)
        {
            if (!b.enabled)
                return true;
            for (Transform t = b.transform; t != null; t = t.parent)
            {
                if (!t.gameObject.activeSelf)
                    return true;
            }
            return false;
        }

        internal static bool Sounds(AudioSourceControlItem t) => t.m_Action == AudioSourceAction.Play || t.m_Action == AudioSourceAction.Unpause;
    }

    // FB-T01: an audio item's asset missing or of another type, a Back To of another mixer — the prepare skips the item
    internal sealed class AudioAssetRule : FeedbackRule
    {
        internal override string Id => "FB-T01";
        internal override RuleSeverity Severity => RuleSeverity.Warning;
        internal override bool Applies(Component host) => OnItems(host);

        internal override void Check(RuleContext c, List<RuleResult> into)
        {
            for (int i = 0; i < c.Items.Count; i++)
            {
                if (!AudioRuleReads.Implemented(c.Items[i]))
                    continue;
                string why = Why(c.Items[i]);
                if (why != null)
                    into.Add(Result(c, i, why + AudioRuleReads.Skipped));
            }
        }

        static string Why(FeedbackItem item)
        {
            switch (item)
            {
                case AudioPlayClipItem t:
                    return t.m_Clip == null ? "No clip" : t.m_Clip is AudioClip ? null : "The clip is not an Audio Clip";
                case AudioPlayAtPointItem t:
                    return t.m_Resource == null ? "No resource" : t.m_Resource is AudioResource ? null : "The resource is not an Audio Resource";
                case AudioMixerParameterItem t:
                    return t.m_Mixer == null ? "No mixer" : t.m_Mixer is AudioMixer ? null : "The mixer is not an Audio Mixer";
                case AudioMixerSnapshotItem t:
                    if (t.m_Snapshot == null)
                        return "No snapshot";
                    if (!(t.m_Snapshot is AudioMixerSnapshot snapshot))
                        return "The snapshot is not an Audio Mixer Snapshot";
                    if (t.m_BackTo == null)
                        return null;
                    if (!(t.m_BackTo is AudioMixerSnapshot back))
                        return "Back To is not an Audio Mixer Snapshot";
                    return back.audioMixer != snapshot.audioMixer ? "Back To belongs to another mixer" : null;
            }
            return null;
        }
    }

    // FB-T02: a Mixer Parameter's name empty or not exposed by its mixer (the mixer's serialized list — so with Unity's audio
    // off too) — the prepare skips the item. A name in another case is offered
    internal sealed class AudioExposedParameterRule : FeedbackRule
    {
        internal override string Id => "FB-T02";
        internal override RuleSeverity Severity => RuleSeverity.Warning;
        internal override bool Applies(Component host) => OnItems(host);

        internal override void Check(RuleContext c, List<RuleResult> into)
        {
            for (int i = 0; i < c.Items.Count; i++)
            {
                if (!(c.Items[i] is AudioMixerParameterItem t) || !AudioRuleReads.Implemented(t) || !(t.m_Mixer is AudioMixer mixer))
                    continue;
                string name = t.m_Parameter;
                if (string.IsNullOrEmpty(name))
                {
                    into.Add(Result(c, i, "Name the exposed parameter" + AudioRuleReads.Skipped));
                    continue;
                }
                List<string> names = AudioRuleReads.ExposedNames(mixer);
                if (names == null || names.Contains(name))
                    continue;
                RuleResult r = Result(c, i, "The mixer " + mixer.name + " exposes no parameter '" + name + "'" + AudioRuleReads.Skipped);
                string[] near = names.Where(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase)).ToArray();
                if (near.Length == 1)
                {
                    string use = near[0];
                    r.Fixes.Add(c.ItemFix("Use '" + use + "'", c.ItemPath(i, "m_Parameter"), x => x.stringValue = use));
                }
                into.Add(r);
            }
        }
    }

    // FB-T03: a Play At Point's output — the first one set of the item's, the player's Audio Output and the project's Default
    // Audio Output — not an Audio Mixer Group: the prepare skips the item
    internal sealed class AudioOutputRule : FeedbackRule
    {
        internal override string Id => "FB-T03";
        internal override RuleSeverity Severity => RuleSeverity.Warning;
        internal override bool Applies(Component host) => OnItems(host);

        internal override void Check(RuleContext c, List<RuleResult> into)
        {
            bool read = false;
            Object project = null;
            for (int i = 0; i < c.Items.Count; i++)
            {
                if (!(c.Items[i] is AudioPlayAtPointItem t) || !AudioRuleReads.Implemented(t))
                    continue;
                Object output = t.m_Output;
                string stage = "The output";
                if (output == null && c.Player != null)
                {
                    output = c.Player.m_AudioOutput;
                    stage = "The player's Audio Output";
                }
                if (output == null)
                {
                    if (!read)
                    {
                        project = SettingsValues.From(Resources.Load<FeedbackSettings>(FeedbackSettings.ResourcePath)).AudioOutput;
                        read = true;
                    }
                    output = project;
                    stage = "The project's Default Audio Output";
                }
                if (output != null && !(output is AudioMixerGroup))
                    into.Add(Result(c, i, stage + " is not an Audio Mixer Group" + AudioRuleReads.Skipped));
            }
        }
    }

    // FB-T04: audio items while Project Settings > Audio's Disable Unity Audio is on — they make no sound (in the editor after
    // a restart; the prepare reads the running engine)
    internal sealed class AudioOffRule : FeedbackRule
    {
        internal const string FixName = "Turn Unity audio on (Project Settings > Audio)";

        internal override string Id => "FB-T04";
        internal override RuleSeverity Severity => RuleSeverity.Warning;
        internal override bool Applies(Component host) => OnItems(host);

        internal override void Check(RuleContext c, List<RuleResult> into)
        {
            bool? off = null;
            for (int i = 0; i < c.Items.Count; i++)
            {
                if (!(c.Items[i] is TargetItem t) || t.Traits.Category != FeedbackCategory.Audio || !AudioRuleReads.Implemented(t))
                    continue;
                off ??= AudioRuleReads.AudioOff();
                if (!off.Value)
                    return;
                RuleResult r = Result(c, i, "Unity's audio is disabled in this project (Project Settings > Audio) — the item makes no sound");
                r.Fixes.Add(new RuleFix(FixName, () =>
                {
                    Object settings = AudioRuleReads.AudioManager();
                    if (settings != null)
                        RuleFixes.Write(settings, "m_DisableAudio", p => p.boolValue = false);
                }));
                into.Add(r);
            }
        }
    }

    // FB-T05: a Low Pass Cutoff on a filter that follows a distance curve (its source's 3D Low-Pass curve) — a cutoff write would
    // replace the curve, the prepare skips the item
    internal sealed class AudioDistanceCurveRule : FeedbackRule
    {
        internal override string Id => "FB-T05";
        internal override RuleSeverity Severity => RuleSeverity.Warning;
        internal override bool Applies(Component host) => OnItems(host);

        internal override void Check(RuleContext c, List<RuleResult> into)
        {
            for (int i = 0; i < c.Items.Count; i++)
            {
                if (c.Items[i] is AudioLowPassCutoffItem t && AudioRuleReads.Implemented(t) && c.Target(i) is AudioLowPassFilter filter
                    && filter.customCutoffCurve != null && filter.customCutoffCurve.length > 1)
                    into.Add(Result(c, i, "The low pass follows a distance curve (its source's 3D Low-Pass curve)" + AudioRuleReads.Skipped));
            }
        }
    }

    // FB-T06: a Source Control's Play or Unpause, a Play Clip, on a source that is off — while it is off the item makes no sound
    internal sealed class AudioSourceOffRule : FeedbackRule
    {
        internal override string Id => "FB-T06";
        internal override RuleSeverity Severity => RuleSeverity.Info;
        internal override bool Applies(Component host) => OnItems(host);

        internal override void Check(RuleContext c, List<RuleResult> into)
        {
            for (int i = 0; i < c.Items.Count; i++)
            {
                FeedbackItem item = c.Items[i];
                bool sounds = item is AudioSourceControlItem control ? AudioRuleReads.Sounds(control) : item is AudioPlayClipItem;
                if (sounds && AudioRuleReads.Implemented(item) && c.Target(i) is AudioSource source && AudioRuleReads.Off(source))
                    into.Add(Result(c, i, "The source is off — while it is off the item makes no sound"));
            }
        }
    }

    // FB-T07: a Source Control's Play or Unpause on a source with no clip or resource — it makes no sound
    internal sealed class AudioSourceEmptyRule : FeedbackRule
    {
        internal override string Id => "FB-T07";
        internal override RuleSeverity Severity => RuleSeverity.Warning;
        internal override bool Applies(Component host) => OnItems(host);

        internal override void Check(RuleContext c, List<RuleResult> into)
        {
            for (int i = 0; i < c.Items.Count; i++)
            {
                if (c.Items[i] is AudioSourceControlItem t && AudioRuleReads.Sounds(t) && AudioRuleReads.Implemented(t) && c.Target(i) is AudioSource source && source.resource == null)
                    into.Add(Result(c, i, "The source has no clip or resource — the item makes no sound"));
            }
        }
    }

    // FB-T08: a Mixer Snapshot on the unscaled clock while its mixer's Update Mode is Normal — at time scale 0 the transition
    // holds while the item goes on. The fixes: the item's clock scaled, or the mixer's Update Mode unscaled (every transition of
    // that mixer)
    internal sealed class AudioSnapshotClockRule : FeedbackRule
    {
        internal override string Id => "FB-T08";
        internal override RuleSeverity Severity => RuleSeverity.Info;
        internal override bool Applies(Component host) => OnItems(host);

        internal override void Check(RuleContext c, List<RuleResult> into)
        {
            for (int i = 0; i < c.Items.Count; i++)
            {
                if (!(c.Items[i] is AudioMixerSnapshotItem t) || !AudioRuleReads.Implemented(t) || !(t.m_Snapshot is AudioMixerSnapshot snapshot))
                    continue;
                AudioMixer mixer = snapshot.audioMixer;
                ItemClock clock = t.EffectiveClock;
                bool unscaled = clock == ItemClock.Unscaled || (clock == ItemClock.Player && c.Player != null && c.Player.m_TimeMode == PlayerTimeMode.Unscaled);
                if (mixer == null || !unscaled || mixer.updateMode != AudioMixerUpdateMode.Normal)
                    continue;
                RuleResult r = Result(c, i, "The mixer " + mixer.name + "'s Update Mode is Normal — at time scale 0 its transition holds while the item goes on");
                r.Fixes.Add(c.ItemFix("Use the scaled clock", c.ItemPath(i, "m_Timing.m_Clock"), x => x.intValue = (int)ItemClock.Scaled));
                r.Fixes.Add(new RuleFix("Set " + mixer.name + "'s Update Mode to Unscaled Time — every transition of " + mixer.name + " changes",
                    () => RuleFixes.Write(mixer, "m_UpdateMode", x => x.intValue = (int)AudioMixerUpdateMode.UnscaledTime)));
                into.Add(r);
            }
        }
    }

    // FB-T09: a Play At Point of an endlessly repeating Audio Random Container without Stop After — its sounds play until a Revert
    // or Restore()
    internal sealed class AudioEndlessContainerRule : FeedbackRule
    {
        internal override string Id => "FB-T09";
        internal override RuleSeverity Severity => RuleSeverity.Warning;
        internal override bool Applies(Component host) => OnItems(host);

        internal override void Check(RuleContext c, List<RuleResult> into)
        {
            for (int i = 0; i < c.Items.Count; i++)
            {
                if (!(c.Items[i] is AudioPlayAtPointItem t) || !AudioRuleReads.Implemented(t) || t.m_StopAfter > 0f || AudioRuleReads.LoopMode(t.m_Resource as AudioResource) != 0)
                    continue;
                RuleResult r = Result(c, i, "An endless container without Stop After — its sounds play until a Revert or Restore()");
                r.Fixes.Add(c.ItemFix("Stop after 1 s", c.ItemPath(i, "m_StopAfter"), x => x.floatValue = 1f));
                into.Add(r);
            }
        }
    }
}
