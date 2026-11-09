using UnityEngine;
using UnityEngine.Rendering;

namespace xpTURN.Feedback.Samples.Basics
{
    // the Ambient Intensity entry's stage: while it is shown the scene's ambient light comes from the sky — the intensity the entry's
    // item changes is the sky source's multiplier, which the gallery's flat ambient color ignores; put back when the stage hides
    public sealed class GalleryAmbientStage : MonoBehaviour
    {
        AmbientMode m_Mode;

        void OnEnable()
        {
            m_Mode = RenderSettings.ambientMode;
            RenderSettings.ambientMode = AmbientMode.Skybox;
        }

        void OnDisable()
        {
            RenderSettings.ambientMode = m_Mode;
        }
    }
}
