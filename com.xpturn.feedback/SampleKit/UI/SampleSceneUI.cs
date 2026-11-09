using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace xpTURN.Feedback.Samples
{
    // on the Common event system (every sample scene and the hub): hands the samples' actions over, makes sure of the HUD
    // (a sample scene played directly) and measures whether the pointer is over UI before the samples' Update
    [DefaultExecutionOrder(-10000)]
    public sealed class SampleSceneUI : MonoBehaviour
    {
        [SerializeField] InputActionAsset m_Actions;

        void Awake() => SampleHud.Ensure();

        // also after a recompile during play (the actions' enabled state does not cross a domain reload)
        void OnEnable() => SampleInput.Use(m_Actions);

        void Update()
        {
            EventSystem es = EventSystem.current;
            SampleInput.PointerOverUI = es != null && es.IsPointerOverGameObject();
        }
    }
}
