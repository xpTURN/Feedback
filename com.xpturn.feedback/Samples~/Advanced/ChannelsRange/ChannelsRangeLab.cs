using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using xpTURN.Feedback;
using xpTURN.Feedback.Samples;

namespace xpTURN.Feedback.Samples.Advanced
{
    // S9 Channels & Range: every lamp has a receiver on the Red or the Blue channel (an asset — not channel 0, which every default
    // would hear) and a player gated by its distance from the play's position (5 m, a linear falloff into the intensity). A click
    // — or Action1 at the cursor marker the move keys drive — sends a Play command with that position on the chosen channels; the
    // send returns how many receivers took it (a lamp beyond the gate still counts — its player refuses the play). Before sends
    // nothing (the lamps' colour punch is out of every category)
    public sealed class ChannelsRangeLab : MonoBehaviour
    {
        public const int Red = 0, Blue = 1, Both = 2;
        const float CursorSpeed = 6f;
        static readonly string[] s_Names = { "Red", "Blue", "Red + Blue" };
        static readonly string[][] s_Received = Received();

        [SerializeField] FeedbackChannel m_Red;
        [SerializeField] FeedbackChannel m_Blue;
        [SerializeField] FeedbackPlayer m_Blast;
        [SerializeField] FeedbackPlayer m_Glow;
        [SerializeField] Transform m_Cursor;
        [SerializeField] Transform m_Ring;
        [SerializeField] Text m_Status;
        [SerializeField] float m_Half = 5.5f;

        [NonSerialized] InputAction m_Fire, m_Move, m_Point, m_PickRed, m_PickBlue, m_PickBoth;
        int m_Filter = Both;
        int m_Last = -1;
        Vector2 m_LastPointer;
        string m_Shown;

        internal int Filter => m_Filter;
        internal int LastReceived => m_Last;

        static string[][] Received()
        {
            var s = new string[3][];
            for (int f = 0; f < 3; f++)
            {
                s[f] = new string[101];
                for (int n = 0; n < s[f].Length; n++)
                    s[f][n] = "Sending on " + s_Names[f] + " — received by " + n;
            }
            return s;
        }

        void Update()
        {
            if (m_Fire == null)
            {
                m_Fire = SampleInput.Find("Action1");
                m_Move = SampleInput.Find("Move");
                m_Point = SampleInput.Find("Point");
                m_PickRed = SampleInput.Find("Option1");
                m_PickBlue = SampleInput.Find("Option2");
                m_PickBoth = SampleInput.Find("Option3");
            }
            if (m_PickRed != null && m_PickRed.WasPressedThisFrame())
                SetFilter(Red);
            if (m_PickBlue != null && m_PickBlue.WasPressedThisFrame())
                SetFilter(Blue);
            if (m_PickBoth != null && m_PickBoth.WasPressedThisFrame())
                SetFilter(Both);
            Vector2 pointer = m_Point != null ? m_Point.ReadValue<Vector2>() : Vector2.zero;
            bool pointerMoved = (pointer - m_LastPointer).sqrMagnitude > 1f;
            m_LastPointer = pointer;
            if (m_Cursor != null)
            {
                Vector2 move = m_Move != null ? m_Move.ReadValue<Vector2>() : Vector2.zero;
                Vector3 at = m_Cursor.position + new Vector3(move.x, 0f, move.y) * (CursorSpeed * Time.unscaledDeltaTime);
                if (pointerMoved && !SampleInput.PointerOverUI && Floor(pointer, out Vector3 hit))
                    at = hit;
                at.x = Mathf.Clamp(at.x, -m_Half, m_Half);
                at.z = Mathf.Clamp(at.z, -m_Half, m_Half);
                m_Cursor.position = new Vector3(at.x, m_Cursor.position.y, at.z);
            }
            if (m_Fire != null && m_Fire.WasPressedThisFrame())
            {
                bool fromPointer = m_Fire.activeControl != null && m_Fire.activeControl.device is Pointer;
                if (fromPointer)
                {
                    if (!SampleInput.PointerOverUI && Floor(pointer, out Vector3 hit))
                        Explode(hit);
                }
                else if (m_Cursor != null)
                    Explode(m_Cursor.position);
            }
            Show();
        }

        static bool Floor(Vector2 screen, out Vector3 hit)
        {
            hit = default;
            Camera cam = Camera.main;
            if (cam == null)
                return false;
            Ray ray = cam.ScreenPointToRay(screen);
            if (Mathf.Abs(ray.direction.y) < 1e-4f)
                return false;
            float t = -ray.origin.y / ray.direction.y;
            if (t <= 0f)
                return false;
            hit = ray.origin + ray.direction * t;
            return true;
        }

        public void SetFilter(int filter) => m_Filter = Mathf.Clamp(filter, Red, Both);

        public void Explode(Vector3 at)
        {
            at.y = 0f;
            if (m_Ring != null)
            {
                m_Ring.position = new Vector3(at.x, m_Ring.position.y, at.z);
                m_Ring.gameObject.SetActive(true);
            }
            if (m_Blast != null)
            {
                m_Blast.transform.position = new Vector3(at.x, m_Blast.transform.position.y, at.z);
                m_Blast.Play(new PlayArgs().WithPosition(at));
            }
            if (!SampleState.After)
            {
                m_Last = 0;
                return;
            }
            // the light is out of every category — Before leaves it dark with the sends
            if (m_Glow != null)
                m_Glow.Play();
            int got = 0;
            if (m_Filter != Blue && m_Red != null)
                got += FeedbackChannels.Send(FeedbackCommand.Play(ChannelRef.Of(m_Red)), at);
            if (m_Filter != Red && m_Blue != null)
                got += FeedbackChannels.Send(FeedbackCommand.Play(ChannelRef.Of(m_Blue)), at);
            m_Last = got;
        }

        void Show()
        {
            if (m_Status == null)
                return;
            string s = s_Received[m_Filter][Mathf.Clamp(Math.Max(m_Last, 0), 0, 100)];
            if (!ReferenceEquals(s, m_Shown))
            {
                m_Shown = s;
                m_Status.text = s;
            }
        }
    }
}
