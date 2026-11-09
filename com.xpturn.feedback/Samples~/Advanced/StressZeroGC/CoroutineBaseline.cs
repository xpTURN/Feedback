using System.Collections;
using UnityEngine;

namespace xpTURN.Feedback.Samples.Advanced
{
    // S11's comparison: the players' punch (0.6 up at 1 Hz, fading over its length) as one coroutine per start with a delay object —
    // the way a hand-rolled tween is often written. It allocates when it starts (the iterator and the delay), not while it runs:
    // re-triggered every second, that is what the meter shows
    public sealed class CoroutineBaseline : MonoBehaviour
    {
        [SerializeField] float m_Height = 0.6f;

        public void Kick(Transform target, float seconds) => StartCoroutine(Punch(target, seconds));

        public void StopAll() => StopAllCoroutines();

        IEnumerator Punch(Transform target, float seconds)
        {
            yield return new WaitForSeconds(0.01f);
            Vector3 home = target.localPosition;
            float t = 0f;
            while (t < seconds && target != null)
            {
                t += Time.deltaTime;
                float k = t / seconds;
                target.localPosition = home + Vector3.up * (m_Height * Mathf.Sin(t * Mathf.PI * 2f) * (1f - k));
                yield return null;
            }
            if (target != null)
                target.localPosition = home;
        }
    }
}
