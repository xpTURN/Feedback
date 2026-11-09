using System;
using UnityEngine;
using UnityEngine.Audio;

namespace xpTURN.Feedback.Tests.Extension
{
    // the scripting API's examples of a user's feedbacks — compiled here, against the public surface alone

    // doc: api-custom-action
    [CustomFeedback("Audio/One Shot", Aliases = "sound sfx", Requires = typeof(AudioSource))]
    [Serializable]
    public sealed class OneShotSound : CustomActionItem
    {
        [SerializeField] AudioClip clip;
        [SerializeField] float volume = 1f;

        protected override CustomAction CreateAction() => new Action(this);

        sealed class Action : CustomAction
        {
            readonly OneShotSound item;
            AudioSource source;

            public Action(OneShotSound item) => this.item = item;

            protected override bool Prepare(in CustomPrepare context) => context.TryFindTarget(out source);

            protected override void Fire(in CustomPlay play)
            {
                // a Seek or a Skip that sweeps over the moment does not play it
                if (play.Swept)
                    return;
                source.PlayOneShot(item.clip, item.volume * play.Intensity * play.CategoryScale);
            }
        }
    }
    // end

    // stands for a platform's haptics plugin
    public static class MyHaptics
    {
        public static void Start(float strength)
        {
        }

        public static void SetStrength(float strength)
        {
        }

        public static void Stop()
        {
        }
    }

    // doc: api-custom-timed
    [CustomFeedback("Haptics/Phone Vibration", Global = true)]
    [Serializable]
    public sealed class PhoneVibration : CustomActionItem
    {
        [SerializeField] float length = 0.3f;

        public override float Duration => length;
        public override FeedbackCategory Category => FeedbackCategory.Haptics;

        protected override CustomAction CreateAction() => new Action();

        sealed class Action : CustomAction
        {
            bool[] running;

            protected override bool Prepare(in CustomPrepare context)
            {
                running = new bool[context.Plays];
                return true;
            }

            protected override void Begin(in CustomPlay play)
            {
                if (play.Swept || play.Reverse)
                    return;
                running[play.Slot] = true;
                MyHaptics.Start(play.Intensity * play.CategoryScale);
            }

            protected override void Update(in CustomTick tick)
            {
                // fades out; 0 at once when the Haptics category is turned off
                if (running[tick.Slot])
                    MyHaptics.SetStrength((1f - tick.T) * tick.CategoryScale);
            }

            protected override void End(int slot, CustomEnd end)
            {
                if (!running[slot])
                    return;
                running[slot] = false;
                MyHaptics.Stop();
            }
        }
    }
    // end

    // doc: api-custom-effect
    [CustomFeedback("Audio/Mixer Parameter", Global = true)]
    [Serializable]
    public sealed class MixerParameter : CustomEffectItem
    {
        [SerializeField] AudioMixer mixer;
        [SerializeField] string parameter = "Volume";

        protected override CustomEffect CreateEffect() => new Effect(this);

        sealed class Effect : CustomEffect
        {
            readonly MixerParameter item;

            public Effect(MixerParameter item) => this.item = item;

            // every frame the play runs; slot −1 puts the item's base back after a Revert or a Restore
            protected override void Apply(int slot, float value) => item.mixer.SetFloat(item.parameter, value);
        }
    }
    // end
}
