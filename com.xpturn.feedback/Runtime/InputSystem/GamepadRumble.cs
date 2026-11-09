using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace xpTURN.Feedback
{
    // the fourth extension service — per path this frame's request (the rumble slot reads and writes it), the ledger of the
    // devices sent a value, and after the layers' LateApply the requests spread to the devices, summed per device and
    // clamped, sent when changed (and again every few seconds while not zero), then cleared. Stops on its close (play exit ·
    // reload), a focus loss, an editor pause and the Haptics category off — each reason on its own. Owns nothing
    internal sealed class GamepadRumbleService : FeedbackService
    {
        internal static readonly ServiceKey Key = new ServiceKey();

        internal const int CurrentPath = 0;
        internal const int AllPath = 1;
        internal const float Step = 1f / 255f;
        internal const float Resend = 5f;

        // muted reasons
        internal const int FocusLost = 1;
        internal const int Paused = 2;
        internal const int CategoryOff = 4;

        Vector2[] m_Requests = new Vector2[2];
        PlayerInput[] m_Players = Array.Empty<PlayerInput>();
        Gamepad[] m_Devices = Array.Empty<Gamepad>();
        Vector2[] m_Sent = Array.Empty<Vector2>();
        float[] m_SentAt = Array.Empty<float>();
        int m_DeviceCount;
        Gamepad[] m_Targets = Array.Empty<Gamepad>();
        Vector2[] m_Sums = Array.Empty<Vector2>();
        float m_Clock;
        int m_Muted;
        Vector2 m_Last;
        readonly bool m_Preview;
        readonly bool m_ManagedOnly;
        readonly Action<bool> m_OnFocus;
        readonly Action<InputDevice, InputDeviceChange> m_OnDevice;
#if UNITY_EDITOR
        readonly Action<UnityEditor.PauseState> m_OnPause;
#endif

        internal GamepadRumbleService(FeedbackContext context)
            : base(context)
        {
            m_Preview = context.Kind == ContextKind.Preview;
            m_ManagedOnly = context.Kind == ContextKind.Test;
            Reserve(Gamepad.all.Count + 1);
            m_OnFocus = OnFocusChanged;
            m_OnDevice = OnDeviceChange;
#if !UNITY_EDITOR
            Application.focusChanged += m_OnFocus;
#endif
            InputSystem.onDeviceChange += m_OnDevice;
#if UNITY_EDITOR
            UnityEditor.EditorApplication.focusChanged += m_OnFocus;
            m_OnPause = OnPauseChanged;
            UnityEditor.EditorApplication.pauseStateChanged += m_OnPause;
#endif
        }

        internal static GamepadRumbleService Of(FeedbackContext context) => context.GetExtension(Key) as GamepadRumbleService;

        internal override string DebugName => "Gamepad rumble";

        internal int DeviceCount => m_DeviceCount;

        internal int Muted => m_Muted;

        internal int Sends { get; private set; }

        internal Vector2 Request(int path) => m_Requests[path];

        internal void SetRequest(int path, in Vector2 value) => m_Requests[path] = value;

        internal Vector2 SentTo(Gamepad pad)
        {
            int at = IndexOf(pad);
            return at < 0 ? new Vector2(-1f, -1f) : m_Sent[at];
        }

        // the preparation path: a player's path (from 2)
        internal int PathOf(PlayerInput player)
        {
            for (int i = 0; i < m_Players.Length; i++)
            {
                if (m_Players[i] == player)
                    return AllPath + 1 + i;
            }
            Array.Resize(ref m_Players, m_Players.Length + 1);
            m_Players[m_Players.Length - 1] = player;
            Array.Resize(ref m_Requests, m_Requests.Length + 1);
            return m_Requests.Length - 1;
        }

        // the preparation path · a device event: room for every gamepad
        internal void Reserve(int devices)
        {
            if (m_Devices.Length >= devices)
                return;
            Array.Resize(ref m_Devices, devices);
            Array.Resize(ref m_Sent, devices);
            Array.Resize(ref m_SentAt, devices);
            Array.Resize(ref m_Targets, devices);
            Array.Resize(ref m_Sums, devices);
        }

        internal override void Update(in FrameTime time) => m_Clock += time.UnscaledDelta;

        internal override void LateApply()
        {
            if (m_Preview || IsClosed)
            {
                Array.Clear(m_Requests, 0, m_Requests.Length);
                return;
            }
            int n = 0;
            if (m_Requests[CurrentPath] != Vector2.zero)
                n = Gather(n, Gamepad.current, m_Requests[CurrentPath]);
            if (m_Requests[AllPath] != Vector2.zero)
            {
                var all = Gamepad.all;
                for (int i = 0; i < all.Count; i++)
                    n = Gather(n, all[i], m_Requests[AllPath]);
            }
            for (int p = 0; p < m_Players.Length; p++)
            {
                Vector2 v = m_Requests[AllPath + 1 + p];
                if (v == Vector2.zero || m_Players[p] == null)
                    continue;
                var devices = m_Players[p].devices;
                for (int i = 0; i < devices.Count; i++)
                    n = Gather(n, devices[i] as Gamepad, v);
            }
            m_Muted = FeedbackCategories.IsEnabled(FeedbackCategory.Haptics) ? m_Muted & ~CategoryOff : m_Muted | CategoryOff;
            for (int i = 0; i < m_DeviceCount; i++)
            {
                if (Array.IndexOf(m_Targets, m_Devices[i], 0, n) < 0)
                    Send(i, Vector2.zero);
            }
            for (int k = 0; k < n; k++)
            {
                Vector2 v = m_Muted != 0 ? Vector2.zero : Clamp(m_Sums[k]);
                int at = IndexOf(m_Targets[k]);
                if (at < 0 && v != Vector2.zero && m_DeviceCount < m_Devices.Length)
                {
                    at = m_DeviceCount++;
                    m_Devices[at] = m_Targets[k];
                    m_Sent[at] = new Vector2(-1f, -1f);
                }
                if (at >= 0)
                    Send(at, v);
                m_Targets[k] = null;
            }
            Array.Clear(m_Requests, 0, m_Requests.Length);
        }

        int Gather(int n, Gamepad pad, in Vector2 v)
        {
            if (pad == null || (m_ManagedOnly && pad.native))
                return n;
            int at = Array.IndexOf(m_Targets, pad, 0, n);
            if (at >= 0)
            {
                m_Sums[at] += v;
                return n;
            }
            if (n == m_Targets.Length)
                return n;
            m_Targets[n] = pad;
            m_Sums[n] = v;
            return n + 1;
        }

        static Vector2 Clamp(Vector2 v)
            => new Vector2(float.IsNaN(v.x) ? 0f : Mathf.Clamp01(v.x), float.IsNaN(v.y) ? 0f : Mathf.Clamp01(v.y));

        int IndexOf(Gamepad pad) => Array.IndexOf(m_Devices, pad, 0, m_DeviceCount);

        // when changed by more than a step (zero always), and again while not zero
        void Send(int at, in Vector2 v)
        {
            Vector2 s = m_Sent[at];
            bool changed = Mathf.Abs(v.x - s.x) > Step || Mathf.Abs(v.y - s.y) > Step || (v == Vector2.zero && s != Vector2.zero);
            bool again = v != Vector2.zero && m_Clock - m_SentAt[at] >= Resend;
            if (!changed && !again)
                return;
            m_Devices[at].SetMotorSpeeds(v.x, v.y);
            m_Sent[at] = v;
            m_SentAt[at] = m_Clock;
            m_Last = v;
            Sends++;
        }

        void StopAll(bool every)
        {
            for (int i = 0; i < m_DeviceCount; i++)
            {
                if (!m_Devices[i].added || (!every && m_Sent[i] == Vector2.zero))
                    continue;
                m_Devices[i].SetMotorSpeeds(0f, 0f);
                m_Sent[i] = Vector2.zero;
                m_SentAt[i] = m_Clock;
                Sends++;
            }
            m_Last = Vector2.zero;
        }

        // a focus loss (a mobile pause too; in the editor the editor app's — the Game view's moves with a click in another
        // window) stops now; the requests go again once every reason is gone
        internal void OnFocusChanged(bool focused)
        {
            try
            {
                m_Muted = focused ? m_Muted & ~FocusLost : m_Muted | FocusLost;
                if (!focused)
                    StopAll(false);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

#if UNITY_EDITOR
        void OnPauseChanged(UnityEditor.PauseState state) => OnPaused(state == UnityEditor.PauseState.Paused);
#endif

        // the loop stands while the editor pauses — the stop is sent here
        internal void OnPaused(bool paused)
        {
            try
            {
                m_Muted = paused ? m_Muted | Paused : m_Muted & ~Paused;
                if (paused)
                    StopAll(false);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        void OnDeviceChange(InputDevice device, InputDeviceChange change)
        {
            if (change == InputDeviceChange.Added || change == InputDeviceChange.Reconnected)
            {
                Reserve(Gamepad.all.Count + 1);
                return;
            }
            if (change != InputDeviceChange.Removed && change != InputDeviceChange.Disconnected)
                return;
            int at = IndexOf(device as Gamepad);
            if (at < 0)
                return;
            m_DeviceCount--;
            m_Devices[at] = m_Devices[m_DeviceCount];
            m_Sent[at] = m_Sent[m_DeviceCount];
            m_SentAt[at] = m_SentAt[m_DeviceCount];
            m_Devices[m_DeviceCount] = null;
        }

        // the context's close (quitting · the reload's start) — every device of the ledger to (0, 0); the layers closed
        // first
        internal override void Shutdown()
        {
#if !UNITY_EDITOR
            Application.focusChanged -= m_OnFocus;
#endif
            InputSystem.onDeviceChange -= m_OnDevice;
#if UNITY_EDITOR
            UnityEditor.EditorApplication.focusChanged -= m_OnFocus;
            UnityEditor.EditorApplication.pauseStateChanged -= m_OnPause;
#endif
            try
            {
                StopAll(true);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
            Array.Clear(m_Devices, 0, m_DeviceCount);
            m_DeviceCount = 0;
        }

        internal override void Describe(List<ServiceRow> into)
        {
            into.Clear();
            int sending = 0;
            for (int i = 0; i < m_DeviceCount; i++)
            {
                if (m_Sent[i] != Vector2.zero)
                    sending++;
            }
            into.Add(new ServiceRow("devices driven", m_DeviceCount));
            into.Add(new ServiceRow("sending", sending));
            into.Add(new ServiceRow("muted", m_Muted));
            into.Add(new ServiceRow("last low", m_Last.x));
            into.Add(new ServiceRow("last high", m_Last.y));
        }
    }

    // a path's request — the read is this frame's, the service clears it after sending, so a play that does not write
    // in a frame asks for nothing (a Tween's end does not stay)
    [NotRecorded("a frame's rumble request — no scene value")]
    internal sealed class GamepadRumbleSlot : ValueSlot<Vector2, Vector2Ops>
    {
        readonly GamepadRumbleService m_Service;
        readonly int m_Path;

        internal GamepadRumbleSlot(GamepadRumbleService service, int path)
            : base(ConsumptionStage.LateApply, Composition.Additive, false)
        {
            m_Service = service;
            m_Path = path;
        }

        internal override bool IsAlive => !m_Service.IsClosed;

        protected override Vector2 Read() => m_Service.Request(m_Path);

        protected override void Write(in Vector2 value) => m_Service.SetRequest(m_Path, value);
    }

    internal sealed class GamepadRumbleBinding : ValueTargetBinding<Vector2, Vector2Ops>
    {
        internal GamepadRumbleBinding(GamepadRumbleItem item)
            : base(item)
        {
        }

        protected override bool ReadsTarget => false;

        protected override bool Acquire(in PrepareContext context, bool direct, out ValueSlot<Vector2, Vector2Ops> slot)
        {
            slot = null;
            GamepadRumbleService service = GamepadRumbleService.Of(context.Context);
            if (service == null)
                return Refuse("no gamepad rumble service");
            var item = (GamepadRumbleItem)m_Item;
            int path = GamepadRumbleService.CurrentPath;
            if (item.m_Device == GamepadRumbleDevice.All)
                path = GamepadRumbleService.AllPath;
            else if (item.m_Device == GamepadRumbleDevice.Player)
            {
                PlayerInput player = TargetResolver.Find<PlayerInput>(in item.m_Player, in context, out TargetFailure failure);
                if (player == null)
                {
#if DEBUG
                    return Refuse(TargetResolver.Describe(failure));
#else
                    return Refuse(null);
#endif
                }
                path = service.PathOf(player);
            }
            service.Reserve(Gamepad.all.Count + 1);
            slot = Layers.GetOrCreate(null, path, 0, () => new GamepadRumbleSlot(service, path));
            return true;
        }
    }
}
