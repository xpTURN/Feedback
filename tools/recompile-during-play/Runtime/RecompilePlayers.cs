using System;
using System.Globalization;
using System.Reflection;
using System.Threading;
using UnityEngine;
using UnityEngine.InputSystem;
using xpTURN.Feedback;

namespace RecompileCheck
{
    // players across the reload — all Awake-initialized but lz (Lazy). cont plays at the reload · det's object is destroyed
    // at frame 30 (its finite play goes on, detached) · en plays on enable · st plays on start · res ended before the reload ·
    // ini wrote its first frame and plays at the reload · tog turned its Toggle cell · lz played once. The package's fields
    // and its runtime are internal and this assembly is not a friend: reflection, like the record's scheduler
    public static class RecompilePlayers
    {
        const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        static readonly Assembly s_Core = typeof(FeedbackPlayer).Assembly;
        static readonly string[] s_Names = { "cont", "det", "en", "st", "res", "ini", "tog", "lz" };

        static void Set(object target, string field, object value) => target.GetType().GetField(field, Any).SetValue(target, value);

        static object Get(object target, string field) => target.GetType().GetField(field, Any).GetValue(target);

        static FeedbackItem Tween(Transform target, TweenMode mode, Vector3 value, float duration)
        {
            object parameters = Activator.CreateInstance(s_Core.GetType("xpTURN.Feedback.TweenParams", true));
            Set(parameters, "m_Mode", mode);
            Set(parameters, "m_From", (Vector4)value);
            Set(parameters, "m_To", (Vector4)value);
            Set(parameters, "m_Loops", 1);
            var motion = new TweenMotion();
            Set(motion, "m_Params", parameters);
            object reference = new TargetRef();
            Set(reference, "m_Mode", TargetRefMode.Direct);
            Set(reference, "m_Object", target);
            Set(reference, "m_Path", "");
            Set(reference, "m_Anchor", "");
            var item = new PositionItem();
            Set(item, "m_Motion", motion);
            Set(item, "m_Target", reference);
            object timing = Get(item, "m_Timing");
            Set(timing, "m_Duration", duration);
            Set(item, "m_Timing", timing);
            return item;
        }

        static FeedbackPlayer Make(string name, Vector3 at, TweenMode mode, Vector3 value, float duration, Action<FeedbackPlayer> set)
        {
            var target = new GameObject(name + "-t");
            target.transform.localPosition = at;
            var owner = new GameObject(name);
            owner.SetActive(false);
            FeedbackPlayer player = owner.AddComponent<FeedbackPlayer>();
            object items = player.GetType().GetField("m_Items", Any).GetValue(player);
            items.GetType().GetMethod("Add").Invoke(items, new object[] { Tween(target.transform, mode, value, duration) });
            Set(player, "m_Initialization", PlayerInitialization.Awake);
            set?.Invoke(player);
            owner.SetActive(true);
            return player;
        }

        public static void MakeAll()
        {
            var ten = new Vector3(10f, 0f, 0f);
            Make("cont", Vector3.zero, TweenMode.To, ten, 10f, null).Play();
            Make("det", Vector3.zero, TweenMode.To, ten, 15f, null).Play();
            Make("en", Vector3.zero, TweenMode.From, new Vector3(0f, 5f, 0f), 0.5f, p => Set(p, "m_AutoPlay", PlayerAutoPlay.OnEnable));
            Make("st", Vector3.zero, TweenMode.To, ten, 1f, p => Set(p, "m_AutoPlay", PlayerAutoPlay.OnStart));
            Make("res", Vector3.zero, TweenMode.To, ten, 0.2f, null).Play();
            Make("ini", new Vector3(1f, 0f, 0f), TweenMode.From, new Vector3(0f, 5f, 0f), 10f, p => Set(p, "m_ApplyInitialState", true)).Play();
            Make("tog", Vector3.zero, TweenMode.To, ten, 0.2f, p => Set(p, "m_Direction", PlayerDirection.Toggle)).Play();
            Make("lz", Vector3.zero, TweenMode.To, ten, 0.5f, p => Set(p, "m_Initialization", PlayerInitialization.Lazy)).Play();
        }

        // two receivers across the reload — rx (a FeedbackReceiver on its own Awake player, channel 1) and shk (a
        // CameraShakeReceiver, channel 2). Each registers again on the new Default (OnEnable)
        public static void MakeReceivers()
        {
            var target = new GameObject("rx-t");
            var rx = new GameObject("rx");
            rx.SetActive(false);
            FeedbackPlayer player = rx.AddComponent<FeedbackPlayer>();
            object items = player.GetType().GetField("m_Items", Any).GetValue(player);
            items.GetType().GetMethod("Add").Invoke(items, new object[] { Tween(target.transform, TweenMode.To, new Vector3(10f, 0f, 0f), 0.5f) });
            Set(player, "m_Initialization", PlayerInitialization.Awake);
            FeedbackReceiver receiver = rx.AddComponent<FeedbackReceiver>();
            Set(receiver, "m_Channel", ChannelRef.Of(1));
            rx.SetActive(true);
            var shk = new GameObject("shk");
            shk.SetActive(false);
            CameraShakeReceiver camera = shk.AddComponent<CameraShakeReceiver>();
            Set(camera, "m_Channel", ChannelRef.Of(2));
            shk.SetActive(true);
        }

        static readonly Type s_SchedulerType = s_Core.GetType("xpTURN.Feedback.FeedbackScheduler", true);

        // the Default's live receivers on the two channels ('-' with no open Default) and the components' marks
        public static string Receivers()
        {
            object scheduler = s_SchedulerType.GetProperty("Default", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
            string counts = "rxCount=- shkCount=-";
            if (scheduler != null && !(bool)s_SchedulerType.GetProperty("IsShutdown", Any).GetValue(scheduler))
            {
                object context = s_SchedulerType.GetProperty("Context", Any).GetValue(scheduler);
                object channels = context.GetType().GetProperty("Channels", Any).GetValue(context);
                int a = (int)channels.GetType().GetMethod("CommandReceivers", Any).Invoke(channels, new object[] { ChannelRef.Of(1) });
                int b = (int)channels.GetType().GetMethod("ShakeReceivers", Any).Invoke(channels, new object[] { ChannelRef.Of(2) });
                counts = "rxCount=" + a + " shkCount=" + b;
            }
            GameObject rx = GameObject.Find("rx");
            FeedbackReceiver r = rx != null ? rx.GetComponent<FeedbackReceiver>() : null;
            GameObject shk = GameObject.Find("shk");
            CameraShakeReceiver c = shk != null ? shk.GetComponent<CameraShakeReceiver>() : null;
            string reg = r == null ? "-" : ((bool)typeof(FeedbackReceiver).GetProperty("IsRegistered", Any).GetValue(r)).ToString();
            string live = c == null ? "-" : ((bool)typeof(CameraShakeReceiver).GetProperty("IsLive", Any).GetValue(c)).ToString();
            return counts + " rxReg=" + reg + " shkLive=" + live;
        }

        // a Play on channel 1: the number that took it and whether rx's player plays
        public static string SendToReceiver()
        {
            int taken = FeedbackChannels.Send(FeedbackCommand.Play(ChannelRef.Of(1)));
            GameObject rx = GameObject.Find("rx");
            FeedbackPlayer p = rx != null ? rx.GetComponent<FeedbackPlayer>() : null;
            return "send=" + taken + " rxPlaying=" + (p != null && p.IsPlaying);
        }

        // listeners that write the record — the end, the Default's state, the frame
        sealed class RecordListener : IPlaybackListener
        {
            readonly string m_Name;
            internal RecordListener(string name) => m_Name = name;
            public void OnPlaybackEnded(PlaybackHandle handle, PlaybackEnd end)
                => RecompileRecord.Write("listener name=" + m_Name + " end=" + end + " default=" + RecompileRecord.DefaultState() + " frame=" + Time.frameCount);
        }

        static CancellationTokenSource s_Source;

        // before the reload: ls (a listener) and tk (a listener and a token) — Lazy, so the reservation after the reload is the
        // tool's 7. The shutdown before the reload ends both: ContextShutdown, the Default closed
        public static string MakeListeners()
        {
            FeedbackPlayer ls = Make("ls", Vector3.zero, TweenMode.To, new Vector3(10f, 0f, 0f), 20f, p => Set(p, "m_Initialization", PlayerInitialization.Lazy));
            PlaybackHandle a = ls.Play(PlayArgs.Default.WithListener(new RecordListener("ls")));
            s_Source = new CancellationTokenSource();
            FeedbackPlayer tk = Make("tk", Vector3.zero, TweenMode.To, new Vector3(10f, 0f, 0f), 20f, p => Set(p, "m_Initialization", PlayerInitialization.Lazy));
            PlaybackHandle b = tk.Play(PlayArgs.Default.WithListener(new RecordListener("tk")), s_Source.Token);
            return "ls=" + (a != default) + " tk=" + (b != default);
        }

        // after the reload: ex waits on the exit token and ex2 has a listener only — and a raw callback on the exit token. The
        // engine cancels the exit token before OnApplicationQuit: ex is Stopped with the Default open, ex2 ends at the shutdown
        public static string MakeExitPlays()
        {
            Application.exitCancellationToken.Register(() => RecompileRecord.Write("exit-token default=" + RecompileRecord.DefaultState() + " frame=" + Time.frameCount));
            FeedbackPlayer ex = Make("ex", Vector3.zero, TweenMode.To, new Vector3(10f, 0f, 0f), 30f, p => Set(p, "m_Initialization", PlayerInitialization.Lazy));
            PlaybackHandle a = ex.Play(PlayArgs.Default.WithListener(new RecordListener("ex")), Application.exitCancellationToken);
            FeedbackPlayer ex2 = Make("ex2", Vector3.zero, TweenMode.To, new Vector3(10f, 0f, 0f), 30f, p => Set(p, "m_Initialization", PlayerInitialization.Lazy));
            PlaybackHandle b = ex2.Play(PlayArgs.Default.WithListener(new RecordListener("ex2")));
            return "ex=" + (a != default) + " ex2=" + (b != default) + " exitToken=" + Application.exitCancellationToken.CanBeCanceled;
        }

        // two MotionDrivers across the reload — dl (Loop · Oscillate on its own position) and df (Follow · SetTarget (5, 0, 0)).
        // The fields are internal: reflection
        public static string MakeDrivers()
        {
            var dl = new GameObject("dl");
            dl.SetActive(false);
            MotionDriver loop = dl.AddComponent<MotionDriver>();
            object op = Activator.CreateInstance(s_Core.GetType("xpTURN.Feedback.OscillateParams", true));
            Set(op, "m_Amplitude", new Vector4(1f, 0f, 0f, 0f));
            Set(op, "m_Frequency", 1f);
            var osc = new OscillateMotion();
            Set(osc, "m_Params", op);
            Set(loop, "m_Item", Self(new PositionItem(), osc));
            dl.SetActive(true);
            var df = new GameObject("df");
            df.SetActive(false);
            MotionDriver follow = df.AddComponent<MotionDriver>();
            object sp = Activator.CreateInstance(s_Core.GetType("xpTURN.Feedback.SpringParams", true));
            Set(sp, "m_Frequency", 2f);
            Set(sp, "m_Damping", 1f);
            var spring = new SpringMotion();
            Set(spring, "m_Params", sp);
            Set(follow, "m_Item", Self(new PositionItem(), spring));
            Set(follow, "m_Mode", MotionDriverMode.Follow);
            df.SetActive(true);
            follow.SetTarget(new Vector4(5f, 0f, 0f, 0f));
            return Drivers();
        }

        static FeedbackItem Self(PositionItem item, FeedbackMotion motion)
        {
            object reference = new TargetRef();
            Set(reference, "m_Mode", TargetRefMode.Self);
            Set(reference, "m_Path", "");
            Set(reference, "m_Anchor", "");
            Set(item, "m_Motion", motion);
            Set(item, "m_Target", reference);
            return item;
        }

        static object Prop(object target, string name) => target.GetType().GetProperty(name, Any).GetValue(target);

        // name=r(unning)p(repared)@seconds:x — target x for the follower
        public static string Drivers()
        {
            var line = new System.Text.StringBuilder();
            foreach (string name in new[] { "dl", "df" })
            {
                GameObject go = GameObject.Find(name);
                MotionDriver d = go != null ? go.GetComponent<MotionDriver>() : null;
                line.Append(' ').Append(name).Append('=');
                if (d == null)
                {
                    line.Append('-');
                    continue;
                }
                line.Append((bool)Prop(d, "IsRunning") ? 'r' : '_').Append((bool)Prop(d, "IsPrepared") ? 'p' : '_')
                    .Append('@').Append(((double)Prop(d, "Seconds")).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture))
                    .Append(':').Append(go.transform.localPosition.x.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));
                if (name == "df")
                    line.Append(":t").Append(((Vector4)Prop(d, "FollowTarget")).x.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));
            }
            return "drivers" + line;
        }

        // a spawn pool across the reload — sp (an Awake player with one pooled Spawn of pool-tpl, a scene template, pool 3,
        // lifetime 0 — it lives until a despawn or the close) played twice before the reload. The pools' container is
        // DontDestroyOnLoad and the close destroys it with every instance (Object.Destroy — the frame's end)
        public static string MakePools()
        {
            var template = new GameObject("pool-tpl");
            template.transform.localPosition = new Vector3(0f, -1000f, 0f);
            var sp = new GameObject("sp");
            sp.SetActive(false);
            FeedbackPlayer player = sp.AddComponent<FeedbackPlayer>();
            var item = new SpawnItem();
            Set(item, "m_Prefab", template);
            Set(item, "m_Pooled", true);
            Set(item, "m_PoolSize", 3);
            Set(item, "m_Lifetime", 0f);
            object reference = new TargetRef();
            Set(reference, "m_Mode", TargetRefMode.Self);
            Set(reference, "m_Path", "");
            Set(reference, "m_Anchor", "");
            Set(item, "m_Motion", new SetMotion());
            Set(item, "m_Target", reference);
            object items = player.GetType().GetField("m_Items", Any).GetValue(player);
            items.GetType().GetMethod("Add").Invoke(items, new object[] { item });
            Set(player, "m_Initialization", PlayerInitialization.Awake);
            sp.SetActive(true);
            bool a = player.Play() != default;
            bool b = player.Play() != default;
            return "plays=" + a + "," + b + " " + Pools();
        }

        // the extension services alive at the reload — a screen flash (the overlay canvas) and a bloom (the overlay volume) on
        // Awake players, and a rumble on a recording gamepad. Their close destroys at once: right after the reload none of their
        // objects is left, a play after it makes them again, and the rumble's close sends (0, 0)
        public sealed class RecordPad : Gamepad
        {
            internal static int Sent;
            internal static string Last = "-";

            public override void SetMotorSpeeds(float lowFrequency, float highFrequency)
            {
                Sent++;
                Last = lowFrequency.ToString("0.00", CultureInfo.InvariantCulture) + "," + highFrequency.ToString("0.00", CultureInfo.InvariantCulture);
                if (lowFrequency == 0f && highFrequency == 0f)
                    RecompileRecord.Write("rumble-zero default=" + RecompileRecord.DefaultState() + " frame=" + Time.frameCount);
                base.SetMotorSpeeds(lowFrequency, highFrequency);
            }
        }

        static T Motion<T>(string parameters, params (string Field, object Value)[] fields) where T : new()
        {
            object p = Activator.CreateInstance(s_Core.GetType("xpTURN.Feedback." + parameters, true));
            foreach ((string field, object value) in fields)
                Set(p, field, value);
            var motion = new T();
            Set(motion, "m_Params", p);
            return motion;
        }

        static FeedbackPlayer Service(string name, TargetItem item, object motion)
        {
            var owner = new GameObject(name);
            owner.SetActive(false);
            FeedbackPlayer player = owner.AddComponent<FeedbackPlayer>();
            Set(item, "m_Motion", motion);
            object timing = Get(item, "m_Timing");
            Set(timing, "m_Duration", 30f);
            Set(item, "m_Timing", timing);
            object items = player.GetType().GetField("m_Items", Any).GetValue(player);
            items.GetType().GetMethod("Add").Invoke(items, new object[] { item });
            Set(player, "m_Initialization", PlayerInitialization.Awake);
            owner.SetActive(true);
            return player;
        }

        public static string MakeServices()
        {
            InputSystem.RegisterLayout<RecordPad>();
            InputSystem.AddDevice<RecordPad>().MakeCurrent();
            bool a = Service("svc-flash", new ScreenFlashItem(), Motion<FlashMotion>("FlashParams", ("m_Target", new Vector4(1f, 1f, 1f, 0.5f)), ("m_Attack", 0.5f), ("m_Hold", 0f), ("m_Release", 0.5f))).Play() != default;
            bool b = Service("svc-bloom", new PostFXBloomItem(), Motion<TweenMotion>("TweenParams", ("m_Mode", TweenMode.To), ("m_To", Vector4.one), ("m_Loops", 1))).Play() != default;
            bool c = Service("svc-rumble", new GamepadRumbleItem(), Motion<TweenMotion>("TweenParams", ("m_Mode", TweenMode.To), ("m_To", new Vector4(0.5f, 0.25f, 0f, 0f)), ("m_Loops", 1))).Play() != default;
            return "plays=" + a + "," + b + "," + c + " " + Services();
        }

        public static string Services()
        {
            int overlay = 0, postfx = 0;
            foreach (GameObject go in Resources.FindObjectsOfTypeAll<GameObject>())
            {
                if (!go.scene.IsValid() || !go.name.StartsWith("xpTURN.Feedback.Runtime.", StringComparison.Ordinal))
                    continue;
                if (go.name.Contains("PostFX"))
                    postfx++;
                else
                    overlay++;
            }
            return "overlay=" + overlay + " postfx=" + postfx + " sent=" + RecordPad.Sent + " last=" + RecordPad.Last;
        }

        public static string ReplayServices()
        {
            bool a = Player("svc-flash") != null && Player("svc-flash").Play() != default;
            bool b = Player("svc-bloom") != null && Player("svc-bloom").Play() != default;
            return "plays=" + a + "," + b + " " + Services();
        }

        // the scene objects (DontDestroyOnLoad too, inactive too): the pools' containers · pool-tpl's clones · the active ones
        public static string Pools()
        {
            int containers = 0, clones = 0, active = 0, ddol = 0;
            foreach (GameObject go in Resources.FindObjectsOfTypeAll<GameObject>())
            {
                if (!go.scene.IsValid())
                    continue;
                if (go.name == "xpTURN.Feedback.Pools")
                {
                    containers++;
                    if (go.scene.name == "DontDestroyOnLoad")
                        ddol++;
                }
                else if (go.name == "pool-tpl(Clone)")
                {
                    clones++;
                    if (go.activeInHierarchy)
                        active++;
                }
            }
            return "containers=" + containers + " ddol=" + ddol + " clones=" + clones + " active=" + active;
        }

        public static string Respawn()
        {
            FeedbackPlayer sp = Player("sp");
            return "play=" + (sp != null && sp.Play() != default) + " " + Pools();
        }

        public static void DestroyDetached()
        {
            GameObject det = GameObject.Find("det");
            if (det != null)
                UnityEngine.Object.Destroy(det);
        }

        static FeedbackPlayer Player(string name)
        {
            GameObject go = GameObject.Find(name);
            return go != null ? go.GetComponent<FeedbackPlayer>() : null;
        }

        static object Runtime(FeedbackPlayer player) => typeof(FeedbackPlayer).GetProperty("Runtime", Any).GetValue(player);

        public static string Place(string name)
        {
            GameObject target = GameObject.Find(name + "-t");
            if (target == null)
                return "-";
            Vector3 v = target.transform.localPosition;
            return v.x.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + "," + v.y.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
        }

        // name=r(untime)p(laying)@x,y for each — a destroyed one is '-'
        public static string State()
        {
            var line = new System.Text.StringBuilder();
            foreach (string name in s_Names)
            {
                FeedbackPlayer p = Player(name);
                line.Append(' ').Append(name).Append('=');
                if (p == null)
                    line.Append("-@").Append(Place(name));
                else
                    line.Append(Runtime(p) != null ? 'r' : '_').Append(p.IsPlaying ? 'p' : '_').Append('@').Append(Place(name));
            }
            return line.ToString().TrimStart();
        }

        public static int Runtimes()
        {
            int n = 0;
            foreach (string name in s_Names)
            {
                FeedbackPlayer p = Player(name);
                if (p != null && Runtime(p) != null)
                    n++;
            }
            return n;
        }

        // after the reload: Restore on res (nothing to put back), the next Toggle play's direction, lz prepares at its Play
        public static string Act()
        {
            Player("res").Restore();
            string res = Place("res");
            FeedbackPlayer tog = Player("tog");
            PlaybackHandle handle = tog.Play();
            object runtime = Runtime(tog);
            string direction = "?";
            int records = (int)runtime.GetType().GetProperty("RecordCount", Any).GetValue(runtime);
            for (int r = 0; r < records; r++)
            {
                object record = runtime.GetType().GetMethod("Record", Any).Invoke(runtime, new object[] { r });
                if ((bool)Get(record, "Active") && ((PlaybackHandle)Get(record, "Handle")).Equals(handle))
                    direction = (bool)Get(record, "Reverse") ? "B" : "F";
            }
            FeedbackPlayer lz = Player("lz");
            bool before = Runtime(lz) != null;
            lz.Play();
            bool after = Runtime(lz) != null;
            return "res=" + res + " tog=" + direction + " lz=" + (before ? 1 : 0) + (after ? 1 : 0);
        }
    }
}
