using UnityEngine;

namespace xpTURN.Feedback.Tests.Editor.Docs.Snippets
{
    // doc: readme-hit
    public class Enemy : MonoBehaviour
    {
        [SerializeField] FeedbackPlayer onHit;
        [SerializeField] float maxHealth = 100f;

        public void TakeDamage(float damage)
            => onHit.Play(PlayArgs.Default.WithPosition(transform.position).WithIntensity(damage / maxHealth));
    }
    // end
}
