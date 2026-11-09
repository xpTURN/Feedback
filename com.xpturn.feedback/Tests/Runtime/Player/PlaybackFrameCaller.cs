using System.Collections;
using UnityEngine;

namespace xpTURN.Feedback.Tests
{
    // calls Play from one place in the frame — FixedUpdate · Update · a coroutine after yield null · LateUpdate — or,
    // for an event, plays a trigger player whose Completed the row answers
    public sealed class PlaybackFrameCaller : MonoBehaviour
    {
        internal FeedbackPlayer Player;
        internal FeedbackPlayer Trigger;
        internal string Site;
        internal int AtFrame;
        internal int PlayFrame = -1;
        bool m_Done;

        void FixedUpdate()
        {
            if (Site == "FixedUpdate")
                Call();
        }

        void Update()
        {
            if (Site == "Update")
                Call();
            else if (Site == "Event" && !m_Done && Time.frameCount >= AtFrame)
            {
                m_Done = true;
                Trigger.Play();
            }
        }

        void LateUpdate()
        {
            if (Site == "LateUpdate")
                Call();
        }

        IEnumerator Start()
        {
            while (true)
            {
                yield return null;
                if (Site == "Coroutine")
                    Call();
            }
        }

        void Call()
        {
            if (m_Done || Time.frameCount < AtFrame)
                return;
            m_Done = true;
            PlayFrame = Time.frameCount;
            Player.Play();
        }
    }
}
