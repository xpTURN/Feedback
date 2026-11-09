using UnityEngine;

namespace xpTURN.Feedback.Tests.Editor.Docs.Snippets
{
    // doc: api-playing
    public class Chest : MonoBehaviour
    {
        [SerializeField] FeedbackPlayer open;
        PlaybackHandle opening;

        void OnEnable() => open.Completed += OnOpened;
        void OnDisable() => open.Completed -= OnOpened;

        public void Open(float strength)
            => opening = open.Play(PlayArgs.Default.WithIntensity(strength).WithPosition(transform.position));

        public void Interrupt() => open.Stop(opening, StopMode.Revert);

        void OnOpened(FeedbackPlayer player, PlaybackHandle handle)
        {
            if (handle == opening)
                Debug.Log("The chest is open.");
        }
    }
    // end

    // doc: api-await
    public class LevelExit : MonoBehaviour
    {
        [SerializeField] FeedbackPlayer outro;

        public async Awaitable LeaveAsync()
        {
            PlaybackEnd end = await outro.PlayAsync(destroyCancellationToken);
            if (end == PlaybackEnd.Completed)
                Debug.Log("Load the next level.");
        }
    }
    // end

    // doc: api-broadcasting
    public class Explosion : MonoBehaviour
    {
        [SerializeField] FeedbackChannel shakes;

        public void Explode(float power)
            => FeedbackChannels.Send(FeedbackCommand.Play(ChannelRef.Of(shakes), PlayArgs.Default.WithIntensity(power)), transform.position);
    }

    public class ComboMeter : MonoBehaviour, IFeedbackSignalReceiver
    {
        [SerializeField] FeedbackChannel combo;
        float total;

        void OnEnable() => FeedbackSignals.Register(ChannelRef.Of(combo), this);
        void OnDisable() => FeedbackSignals.Unregister(ChannelRef.Of(combo), this);

        public void OnFeedbackSignal(in FeedbackSignal signal) => total += signal.Value;
    }
    // end

    // doc: api-global
    public class GameOptions : MonoBehaviour
    {
        public void SetFlashes(bool on) => FeedbackCategories.SetEnabled(FeedbackCategory.Flash, on);

        public void SetShakeStrength(float strength) => FeedbackCategories.SetScale(FeedbackCategory.Camera, strength);

        public void SetPaused(bool paused) => FeedbackTimeScale.BaseScale = paused ? 0f : 1f;
    }
    // end

    // doc: api-spring
    public class Bobber : MonoBehaviour
    {
        SpringFloat height = new SpringFloat(0f, frequency: 4f, damping: 0.3f);

        public void Kick(float speed) => height.Bump(speed);

        void Update()
        {
            height.Step(Time.deltaTime);
            transform.localPosition = new Vector3(0f, height.Value, 0f);
        }
    }
    // end

    // doc: api-custom-target
    public class EngineHum : MonoBehaviour, IFeedbackTarget<float>
    {
        [SerializeField] AudioSource hum;

        public float FeedbackValue
        {
            get => hum.volume;
            set => hum.volume = value;
        }
    }
    // end
}
