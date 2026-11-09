using System.Collections.Generic;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace xpTURN.Feedback.Editor
{
    // a session's camera shake receivers. Edit mode runs no OnEnable or OnDisable, so the session binds them — those of the
    // prefab stage and the open scenes at the start, before the subject, and those of a scene opened during it. One that is
    // off waits in the parked list and is bound when it is on; the check runs when a tick is due. A shake reaches the
    // receivers bound at its Begin. The end lets every one go
    internal sealed class PreviewReceivers
    {
        readonly FeedbackScheduler m_Scheduler;
        readonly List<CameraShakeReceiver> m_Bound = new List<CameraShakeReceiver>();
        readonly List<CameraShakeReceiver> m_Parked = new List<CameraShakeReceiver>();
        readonly List<GameObject> m_Roots = new List<GameObject>();
        readonly List<CameraShakeReceiver> m_Found = new List<CameraShakeReceiver>();

        internal PreviewReceivers(FeedbackScheduler scheduler)
        {
            m_Scheduler = scheduler;
        }

        internal IReadOnlyList<CameraShakeReceiver> Bound => m_Bound;

        internal IReadOnlyList<CameraShakeReceiver> Parked => m_Parked;

        // the prefab stage first, then the main stage's open scenes in order
        internal void BindOpenScenes()
        {
            PrefabStage stage = PrefabStageUtility.GetCurrentPrefabStage();
            if (stage != null)
                BindScene(stage.scene);
            for (int i = 0; i < SceneManager.sceneCount; i++)
                BindScene(SceneManager.GetSceneAt(i));
        }

        // the roots in order, each depth first — the order shows only past a play's receiver count
        internal void BindScene(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded)
                return;
            scene.GetRootGameObjects(m_Roots);
            foreach (GameObject root in m_Roots)
            {
                root.GetComponentsInChildren(true, m_Found);
                foreach (CameraShakeReceiver receiver in m_Found)
                {
                    if (m_Bound.Contains(receiver) || m_Parked.Contains(receiver))
                        continue;
                    if (receiver.isActiveAndEnabled)
                    {
                        receiver.Bind(m_Scheduler);
                        m_Bound.Add(receiver);
                    }
                    else
                    {
                        m_Parked.Add(receiver);
                    }
                }
            }
        }

        // one turned off is let go — its contribution ends, its place comes back at the tick's revert — and parked; one
        // destroyed is dropped (Bind(null) reaches no engine object); one parked and on again is bound
        internal void Check()
        {
            for (int i = m_Bound.Count - 1; i >= 0; i--)
            {
                CameraShakeReceiver receiver = m_Bound[i];
                if (receiver != null && receiver.isActiveAndEnabled)
                    continue;
                receiver.Bind(null);
                m_Bound.RemoveAt(i);
                if (receiver != null)
                    m_Parked.Add(receiver);
            }
            for (int i = m_Parked.Count - 1; i >= 0; i--)
            {
                CameraShakeReceiver receiver = m_Parked[i];
                if (receiver == null)
                {
                    m_Parked.RemoveAt(i);
                }
                else if (receiver.isActiveAndEnabled)
                {
                    receiver.Bind(m_Scheduler);
                    m_Parked.RemoveAt(i);
                    m_Bound.Add(receiver);
                }
            }
        }

        // the end's step 2
        internal void UnbindAll()
        {
            for (int i = 0; i < m_Bound.Count; i++)
                m_Bound[i].Bind(null);
            m_Bound.Clear();
            m_Parked.Clear();
        }
    }
}
