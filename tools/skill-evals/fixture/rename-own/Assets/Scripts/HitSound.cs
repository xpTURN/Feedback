using System;
using UnityEngine;
using xpTURN.Feedback;

[CustomFeedback("Audio/Hit Sound", Aliases = "sound sfx", Requires = typeof(AudioSource))]
[Serializable]
public sealed class HitSound : CustomActionItem
{
    [SerializeField] AudioClip clip;
    [SerializeField] float volume = 1f;

    protected override CustomAction CreateAction() => new Action(this);

    sealed class Action : CustomAction
    {
        readonly HitSound item;
        AudioSource source;

        public Action(HitSound item) => this.item = item;

        protected override bool Prepare(in CustomPrepare context) => context.TryFindTarget(out source);

        protected override void Fire(in CustomPlay play)
        {
            if (play.Swept)
                return;
            source.PlayOneShot(item.clip, item.volume * play.Intensity * play.CategoryScale);
        }
    }
}
