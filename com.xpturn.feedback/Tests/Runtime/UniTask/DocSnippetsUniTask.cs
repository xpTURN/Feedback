using Cysharp.Threading.Tasks;
using UnityEngine;

namespace xpTURN.Feedback.Tests.Snippets
{
    // doc: api-unitask
    public class Fireworks : MonoBehaviour
    {
        [SerializeField] FeedbackPlayer left;
        [SerializeField] FeedbackPlayer right;

        public async UniTask CelebrateAsync()
        {
            var (leftEnd, rightEnd) = await UniTask.WhenAll(
                left.PlayAsUniTask(destroyCancellationToken),
                right.PlayAsUniTask(PlayArgs.Default.WithIntensity(0.5f), destroyCancellationToken));
            Debug.Log($"Both ended: {leftEnd}, {rightEnd}");
        }
    }
    // end
}
