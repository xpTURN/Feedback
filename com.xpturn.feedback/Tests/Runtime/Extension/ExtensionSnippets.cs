using System;
using UnityEngine;

namespace xpTURN.Feedback.Tests.Extension
{
    // the scripting API's examples of a user's feedbacks — compiled here, against the public surface alone

    // stands for a sound middleware's runtime
    public static class MyAudio
    {
        public static void Post(string sound, Vector3 position, float volume)
        {
        }

        public static void SetParameter(string name, float value)
        {
        }
    }

    // doc: api-custom-action
    [CustomFeedback("Audio/Middleware One Shot", Aliases = "sound sfx fmod wwise", Requires = typeof(Transform))]
    [Serializable]
    public sealed class MiddlewareOneShot : CustomActionItem
    {
        [SerializeField] string sound = "event:/Hit";
        [SerializeField] float volume = 1f;

        public override FeedbackCategory Category => FeedbackCategory.Audio;

        protected override CustomAction CreateAction() => new Action(this);

        sealed class Action : CustomAction
        {
            readonly MiddlewareOneShot item;
            Transform emitter;

            public Action(MiddlewareOneShot item) => this.item = item;

            protected override bool Prepare(in CustomPrepare context) => context.TryFindTarget(out emitter);

            protected override void Fire(in CustomPlay play)
            {
                // a Seek or a Skip that sweeps over the moment does not play it
                if (play.Swept)
                    return;
                MyAudio.Post(item.sound, emitter.position, item.volume * play.Intensity * play.CategoryScale);
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
    [CustomFeedback("Audio/Middleware Parameter", Global = true)]
    [Serializable]
    public sealed class MiddlewareParameter : CustomEffectItem
    {
        [SerializeField] string parameter = "Volume";

        protected override CustomEffect CreateEffect() => new Effect(this);

        sealed class Effect : CustomEffect
        {
            readonly MiddlewareParameter item;

            public Effect(MiddlewareParameter item) => this.item = item;

            // every frame the play runs; slot −1 puts the item's base back after a Revert or a Restore
            protected override void Apply(int slot, float value) => MyAudio.SetParameter(item.parameter, value);
        }
    }
    // end
}
