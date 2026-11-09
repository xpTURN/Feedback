using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using xpTURN.Feedback;
using xpTURN.Feedback.Samples;

namespace xpTURN.Feedback.Samples.Showcase
{
    // S13 Showcase: a short brick-breaker round — the paddle sends the ball into 32 bricks (the top two rows crack first), the boss
    // comes down when they are gone and its defeat clears the stage; three lost balls end the game and Launch starts again. The
    // board runs on the game clock alone and never waits for a feedback; this lab turns its events into plays — each brick's own
    // list (crack · break by intensity range), one Impact a frame whose intensity is the combo tier (its hit stops and rumble by
    // range), the score and combo labels, a floating +score (TMP, when its resources are there), the boss's entrance, hits, rage
    // pulse and defeat. Before plays the same game with no feedback: every play is gated, the bar and the banner reach their end
    // states without a motion
    public sealed class BrickBreakerLab : MonoBehaviour
    {
        public enum Phase { Serve, Play, BossIntro, Boss, Clear, GameOver }

        public const float ComboWindow = 2f, IntroSeconds = 1.5f, HideAfter = 0.12f, CheerDelay = 1.2f, BossHideDelay = 0.4f;
        public const float CountUpSeconds = 0.4f, PaddleSpeed = 12f, NumberLife = 0.8f;
        public const int StartLives = 3, ScoreStep = 10, ScoreTop = 9990, NumberCap = 6, BrickPoints = 10, BossPoints = 50;
        public const int DefeatPoints = 1000, LifeBonus = 500;
        static readonly float[] s_Tier = { 0.35f, 0.65f, 1f };
        static readonly string[] s_Score = Scores();
        static readonly string[] s_Combo = Combos();
        static readonly string[] s_Lives = { "Lives 0", "Lives 1", "Lives 2", "Lives 3" };
        const string ClearText = "Stage Clear — Launch to play again", OverText = "Game Over — Launch to play again";
        const string ServeText = "Launch (Space) — the ball leaves the paddle", BossText = "The boss — Launch when ready";

        [SerializeField] Transform m_Paddle;
        [SerializeField] Transform m_Ball;
        [SerializeField] Transform m_Boss;
        [SerializeField] GameObject m_BossBody;
        [SerializeField] Renderer[] m_Bricks = new Renderer[0];
        [SerializeField] FeedbackPlayer[] m_BrickPlayers = new FeedbackPlayer[0];
        [SerializeField] Material m_Cracked;
        [SerializeField] FeedbackPlayer m_PaddleHit;
        [SerializeField] FeedbackPlayer m_Impact;
        [SerializeField] FeedbackPlayer m_ScoreFx;
        [SerializeField] FeedbackPlayer m_ComboFx;
        [SerializeField] FeedbackPlayer m_LifeLost;
        [SerializeField] FeedbackPlayer m_BossIntro;
        [SerializeField] FeedbackPlayer m_BossHit;
        [SerializeField] FeedbackPlayer m_BossRage;
        [SerializeField] FeedbackPlayer m_BossDefeat;
        [SerializeField] FeedbackPlayer m_Boom;
        [SerializeField] FeedbackPlayer m_Banner;
        [SerializeField] FeedbackPlayer m_Cheer;
        [SerializeField] ProgressBar m_BossBar;
        [SerializeField] GameObject m_BossBarRoot;
        [SerializeField] Text m_ScoreText;
        [SerializeField] Text m_ComboText;
        [SerializeField] Text m_LivesText;
        [SerializeField] Text m_Line;
        [SerializeField] Text m_BannerText;
        [SerializeField] GameObject m_NumbersPrefab;

        [System.NonSerialized] InputAction m_Move, m_Point, m_Launch, m_Again, m_BossNow, m_Left, m_Right;
        readonly BrickBoard m_Board = new BrickBoard();
        readonly float[] m_HideAt = new float[BrickBoard.BrickCount];
        readonly float[] m_NumberAt = new float[8];
        Material[] m_Base;
        FeedbackPlayer m_Number;
        GameObject m_Numbers, m_Notice;
        Phase m_Phase;
        int m_Lives, m_Score, m_Shown, m_From, m_Combo, m_Tier, m_NumberHead;
        float m_GameClock, m_ComboUntil, m_IntroUntil, m_CountStart, m_BossHideAt = -1f, m_CheerAt = -1f;
        Vector2 m_LastPointer;
        bool m_WasAfter = true;
        // this frame's shared plays — one each (a Restart player played twice in a frame while playing warns)
        int m_FrameTier = -1;
        bool m_FrameScore, m_FrameCombo;
        string m_ShownScore, m_ShownCombo, m_ShownLives, m_ShownLine;

        internal BrickBoard Board => m_Board;
        internal Phase Current => m_Phase;
        internal int Score => m_Score;
        internal int ShownScore => m_Shown;
        internal int Combo => m_Combo;
        internal int Tier => m_Tier;
        internal int Lives => m_Lives;
        internal float LastImpact { get; private set; }

        static string[] Scores()
        {
            var s = new string[ScoreTop / ScoreStep + 1];
            for (int i = 0; i < s.Length; i++)
                s[i] = "Score " + (i * ScoreStep);
            return s;
        }

        static string[] Combos()
        {
            var s = new string[100];
            s[0] = "";
            for (int n = 1; n < s.Length; n++)
                s[n] = n + " combo · ×" + (TierOf(n) + 1);
            return s;
        }

        // the combo's tier: 1 ~ 2 · 3 ~ 5 · 6 and on (0 · 1 · 2)
        public static int TierOf(int combo) => combo >= 6 ? 2 : combo >= 3 ? 1 : 0;

        void Awake()
        {
            m_Base = new Material[m_Bricks.Length];
            for (int i = 0; i < m_Bricks.Length; i++)
                m_Base[i] = m_Bricks[i] != null ? m_Bricks[i].sharedMaterial : null;
        }

        void OnEnable()
        {
            if (SampleTmp.Ready && m_NumbersPrefab != null && m_Numbers == null)
            {
                m_Numbers = Instantiate(m_NumbersPrefab, transform, false);
                Transform t = m_Numbers.transform.Find("Number");
                m_Number = t != null ? t.GetComponent<FeedbackPlayer>() : null;
            }
        }

        void Start()
        {
            NewRound();
            if (SampleTmp.Ready)
                return;
            SampleHud.Notify(SampleTmp.Notice);
            m_Notice = new GameObject("TMP Notice", typeof(RectTransform));
            m_Notice.transform.SetParent(transform, false);
            var canvas = m_Notice.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;
            var scaler = m_Notice.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            var line = new GameObject("Line", typeof(RectTransform));
            line.transform.SetParent(m_Notice.transform, false);
            var rt = (RectTransform)line.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.anchoredPosition = new Vector2(0f, 60f);
            rt.sizeDelta = new Vector2(1400f, 40f);
            Text text = line.AddComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = 22;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.white;
            text.raycastTarget = false;
            text.text = SampleTmp.Notice;
        }

        void OnDestroy()
        {
            if (m_Notice != null)
                Destroy(m_Notice);
        }

        // a new round: the board, the bricks, the counts — every player back to its values before its first play
        public void NewRound()
        {
            m_Board.Reset();
            for (int i = 0; i < m_Bricks.Length; i++)
            {
                m_HideAt[i] = -1f;
                if (m_Bricks[i] == null)
                    continue;
                m_Bricks[i].enabled = true;
                m_Bricks[i].sharedMaterial = m_Base[i];
            }
            foreach (FeedbackPlayer p in m_BrickPlayers)
                if (p != null)
                    p.Restore();
            Restore(m_PaddleHit);
            Restore(m_Impact);
            Restore(m_ScoreFx);
            Restore(m_ComboFx);
            Restore(m_LifeLost);
            Restore(m_BossIntro);
            Restore(m_BossHit);
            Restore(m_BossRage);
            Restore(m_BossDefeat);
            Restore(m_Boom);
            Restore(m_Banner);
            Restore(m_Cheer);
            m_Lives = StartLives;
            m_Score = m_Shown = m_From = 0;
            m_Combo = m_Tier = 0;
            m_GameClock = 0f;
            m_BossHideAt = m_CheerAt = -1f;
            m_Phase = Phase.Serve;
            if (m_BossBody != null)
                m_BossBody.SetActive(false);
            if (m_BossBarRoot != null)
                m_BossBarRoot.SetActive(false);
            if (m_BannerText != null)
                m_BannerText.text = "";
            Place();
            Show();
        }

        static void Restore(FeedbackPlayer p)
        {
            if (p != null)
                p.Restore();
        }

        // Action3: the bricks out at once (no score) and the boss on its way
        public void BossNow()
        {
            if (m_Phase != Phase.Serve && m_Phase != Phase.Play)
                return;
            m_Board.ClearBricks();
            for (int i = 0; i < m_Bricks.Length; i++)
                if (m_Bricks[i] != null)
                    m_Bricks[i].enabled = false;
            BeginBoss();
        }

        public void Launch()
        {
            if (m_Phase == Phase.Clear || m_Phase == Phase.GameOver)
            {
                if (m_Phase == Phase.Clear && m_CheerAt >= 0f)
                    return;
                NewRound();
                return;
            }
            if (m_Phase == Phase.BossIntro || !m_Board.Held)
                return;
            m_Board.Launch();
            m_Phase = m_Board.BossActive ? Phase.Boss : Phase.Play;
        }

        void Update()
        {
            if (m_Move == null)
            {
                m_Move = SampleInput.Find("Move");
                m_Point = SampleInput.Find("Point");
                m_Launch = SampleInput.Find("Action1");
                m_Again = SampleInput.Find("Action2");
                m_BossNow = SampleInput.Find("Action3");
                m_Left = SampleInput.Find("Option1");
                m_Right = SampleInput.Find("Option2");
            }
            bool after = SampleState.After;
            if (after != m_WasAfter)
                OnAfter(after);
            if (Pressed(m_Launch))
                Launch();
            if (Pressed(m_Again))
                NewRound();
            if (Pressed(m_BossNow))
                BossNow();
            float dt = Time.deltaTime;
            Step(dt, PaddleTarget(dt));
            Timers(Time.unscaledDeltaTime);
            Flush();
            Rage();
            Show();
        }

        static bool Pressed(InputAction a) => a != null && a.WasPressedThisFrame() && !(a.activeControl != null && a.activeControl.device is Pointer && SampleInput.PointerOverUI);

        // the keys and pads move the paddle on the game clock; the pointer's x is a target the paddle follows at the same speed
        // (a hit stop holds it — the same game in Before and After)
        float PaddleTarget(float dt)
        {
            float x = m_Board.PaddleX;
            float axis = m_Move != null ? m_Move.ReadValue<Vector2>().x : 0f;
            if (m_Left != null && m_Left.IsPressed())
                axis -= 1f;
            if (m_Right != null && m_Right.IsPressed())
                axis += 1f;
            axis = Mathf.Clamp(axis, -1f, 1f);
            Vector2 pointer = m_Point != null ? m_Point.ReadValue<Vector2>() : Vector2.zero;
            bool moved = (pointer - m_LastPointer).sqrMagnitude > 1f;
            m_LastPointer = pointer;
            if (axis != 0f)
            {
                m_PointerTarget = m_Pending = float.NaN;
                return x + axis * PaddleSpeed * dt;
            }
            // the UI's hover answer comes a frame late (the event system raycasts after this Update): a moved pointer's x waits a frame
            // and is taken when that answer says it was off the UI
            if (!float.IsNaN(m_Pending) && !SampleInput.PointerOverUI)
                m_PointerTarget = m_Pending;
            m_Pending = float.NaN;
            if (moved && PointerX(pointer, out float bx))
                m_Pending = bx;
            return float.IsNaN(m_PointerTarget) ? x : Mathf.MoveTowards(x, m_PointerTarget, PaddleSpeed * dt);
        }

        float m_PointerTarget = float.NaN, m_Pending = float.NaN;

        static bool PointerX(Vector2 screen, out float x)
        {
            x = 0f;
            Camera cam = Camera.main;
            if (cam == null)
                return false;
            Ray ray = cam.ScreenPointToRay(screen);
            if (Mathf.Abs(ray.direction.z) < 1e-4f)
                return false;
            float t = -ray.origin.z / ray.direction.z;
            if (t <= 0f)
                return false;
            x = ray.origin.x + ray.direction.x * t;
            return true;
        }

        // one game step: the board, its events, the game timers — the game clock is Time.deltaTime (a hit stop's 0 holds it)
        internal void Step(float dt, float paddleX)
        {
            m_Board.Step(dt, paddleX);
            m_GameClock += dt;
            for (int i = 0; i < m_Board.EventCount; i++)
                OnEvent(m_Board.EventAt(i));
            for (int i = 0; i < m_HideAt.Length; i++)
                if (m_HideAt[i] >= 0f && m_GameClock >= m_HideAt[i])
                {
                    m_HideAt[i] = -1f;
                    if (i < m_Bricks.Length && m_Bricks[i] != null)
                        m_Bricks[i].enabled = false;
                }
            if (m_Combo > 0 && m_GameClock >= m_ComboUntil)
            {
                m_Combo = 0;
                m_Tier = 0;
            }
            if (m_Phase == Phase.BossIntro && m_GameClock >= m_IntroUntil)
                m_Phase = Phase.Serve;
            Place();
        }

        void OnEvent(BrickBoard.Event e)
        {
            switch (e.Kind)
            {
                case BrickBoard.Kind.Paddle:
                    Play(m_PaddleHit, 0.5f + 0.5f * e.Strength);
                    break;
                case BrickBoard.Kind.Crack:
                    if (e.Index < m_Bricks.Length && m_Bricks[e.Index] != null && m_Cracked != null)
                        m_Bricks[e.Index].sharedMaterial = m_Cracked;
                    Play(Brick(e.Index), 0.5f);
                    break;
                case BrickBoard.Kind.Break:
                    Play(Brick(e.Index), 1f);
                    m_HideAt[e.Index] = m_GameClock + HideAfter;
                    Count(BrickPoints, e.At, true);
                    break;
                case BrickBoard.Kind.BossHit:
                    Play(m_BossHit, 1f);
                    Bar((float)m_Board.BossHp / BrickBoard.BossHealth);
                    Count(BossPoints, e.At, false);
                    break;
                case BrickBoard.Kind.BossDefeat:
                    Bar(0f);
                    Count(BossPoints, e.At, false);
                    m_Score = Mathf.Min(ScoreTop, m_Score + DefeatPoints);
                    if (m_BossRage != null && m_BossRage.IsPlaying)
                        m_BossRage.Stop(StopMode.Revert);
                    Play(m_BossDefeat, 1f);
                    Play(m_Boom, 1f);
                    m_Phase = Phase.Clear;
                    m_BossHideAt = BossHideDelay;
                    m_CheerAt = CheerDelay;
                    break;
                case BrickBoard.Kind.Lost:
                    m_Combo = m_Tier = 0;
                    Play(m_LifeLost, 1f);
                    m_FrameTier = Mathf.Max(m_FrameTier, 0);
                    m_Lives = Mathf.Max(0, m_Lives - 1);
                    if (m_Lives == 0)
                    {
                        m_Phase = Phase.GameOver;
                        BannerShow(OverText);
                    }
                    else
                        m_Phase = Phase.Serve;
                    break;
                case BrickBoard.Kind.Cleared:
                    BeginBoss();
                    break;
            }
        }

        FeedbackPlayer Brick(int i) => i >= 0 && i < m_BrickPlayers.Length ? m_BrickPlayers[i] : null;

        // a scored hit: the combo (bricks feed the shared Impact — the boss's own player has its stop), the score, a +number
        void Count(int points, Vector2 at, bool impact)
        {
            int tier = TierOf(m_Combo);
            m_Combo = Mathf.Min(99, m_Combo + 1);
            m_ComboUntil = m_GameClock + ComboWindow;
            int next = TierOf(m_Combo);
            if (next > tier)
                m_FrameCombo = true;
            m_Tier = next;
            int gained = points * (next + 1);
            m_Score = Mathf.Min(ScoreTop, m_Score + gained);
            m_FrameScore = true;
            if (impact)
                m_FrameTier = Mathf.Max(m_FrameTier, next);
            Number(gained, at);
        }

        // the floating +score — at most NumberCap alive (the pool holds 8)
        void Number(int points, Vector2 at)
        {
            if (!SampleState.After || m_Number == null)
                return;
            float now = Time.unscaledTime;
            int alive = 0;
            for (int i = 0; i < m_NumberAt.Length; i++)
                if (now - m_NumberAt[i] < NumberLife && m_NumberAt[i] > 0f)
                    alive++;
            if (alive >= NumberCap)
                return;
            m_NumberAt[m_NumberHead] = now;
            m_NumberHead = (m_NumberHead + 1) % m_NumberAt.Length;
            m_Number.Play(new PlayArgs().WithIntensity(points / 100f).WithPosition(new Vector3(at.x, at.y, -0.5f)));
        }

        void BeginBoss()
        {
            m_Board.StartBoss();
            m_Phase = Phase.BossIntro;
            m_IntroUntil = m_GameClock + IntroSeconds;
            if (m_BossBody != null)
                m_BossBody.SetActive(true);
            if (m_BossBarRoot != null)
                m_BossBarRoot.SetActive(true);
            if (m_BossBar != null)
            {
                m_BossBar.SetValueImmediate(0f);
                Bar(1f);
            }
            Play(m_BossIntro, 1f);
            Place();
        }

        // the bar moves only in After (Before is the end state at once)
        void Bar(float value)
        {
            if (m_BossBar == null)
                return;
            if (SampleState.After)
                m_BossBar.SetValue(value);
            else
                m_BossBar.SetValueImmediate(value);
        }

        void BannerShow(string text)
        {
            if (m_BannerText != null)
                m_BannerText.text = text;
            if (m_Banner == null)
                return;
            m_Banner.Play();
            // Before: the end state — the alpha (category None) arrives, the motion stays out
            if (!SampleState.After)
                m_Banner.Skip();
        }

        // the unscaled timers of a defeat (a slow motion does not stretch them)
        void Timers(float unscaledDt)
        {
            if (m_BossHideAt >= 0f)
            {
                m_BossHideAt -= unscaledDt;
                if (m_BossHideAt < 0f && m_BossBody != null)
                {
                    m_BossBody.SetActive(false);
                    if (m_BossBarRoot != null)
                        m_BossBarRoot.SetActive(false);
                }
            }
            if (m_CheerAt >= 0f)
            {
                m_CheerAt -= unscaledDt;
                if (m_CheerAt < 0f)
                {
                    m_Score = Mathf.Min(ScoreTop, m_Score + m_Lives * LifeBonus);
                    m_FrameScore = true;
                    Play(m_Cheer, 1f);
                    BannerShow(ClearText);
                }
            }
        }

        // the frame's shared plays, once each
        void Flush()
        {
            if (m_FrameTier >= 0)
            {
                LastImpact = s_Tier[m_FrameTier];
                Play(m_Impact, LastImpact);
            }
            if (m_FrameScore)
                Play(m_ScoreFx, 1f);
            if (m_FrameCombo)
                Play(m_ComboFx, s_Tier[m_Tier]);
            m_FrameTier = -1;
            m_FrameScore = m_FrameCombo = false;
        }

        // the rage pulse: wanted while raging in After, played when it is not; Before stops it (its emission is category None —
        // a turned-off category zeroes the rest)
        void Rage()
        {
            if (m_BossRage == null)
                return;
            bool want = m_Board.Raging;
            if (want && SampleState.After && !m_BossRage.IsPlaying)
                m_BossRage.Play();
            else if ((!want || !SampleState.After) && m_BossRage.IsPlaying)
                m_BossRage.Stop(StopMode.Revert);
        }

        void OnAfter(bool after)
        {
            m_WasAfter = after;
            if (!after)
                m_Shown = m_Score;
        }

        static void Play(FeedbackPlayer p, float intensity)
        {
            if (p != null && SampleState.After)
                p.Play(new PlayArgs().WithIntensity(intensity));
        }

        void Place()
        {
            if (m_Paddle != null)
                m_Paddle.localPosition = new Vector3(m_Board.PaddleX, BrickBoard.PaddleY, 0f);
            if (m_Ball != null)
                m_Ball.localPosition = new Vector3(m_Board.Ball.x, m_Board.Ball.y, 0f);
            if (m_Boss != null)
            {
                Vector2 c = m_Board.BossCenter;
                m_Boss.localPosition = new Vector3(c.x, c.y, 0f);
            }
        }

        // the labels from the tables; the score counts up over CountUpSeconds (unscaled · EaseOut) in After, at once in Before
        void Show()
        {
            if (!m_Counting && m_Shown != m_Score)
            {
                m_From = m_Shown;
                m_CountStart = Time.unscaledTime;
                m_Counting = true;
            }
            if (m_Counting)
            {
                float k = Mathf.Clamp01((Time.unscaledTime - m_CountStart) / CountUpSeconds);
                k = 1f - (1f - k) * (1f - k);
                int v = Mathf.RoundToInt(Mathf.Lerp(m_From, m_Score, k) / ScoreStep) * ScoreStep;
                m_Shown = SampleState.After ? v : m_Score;
                if (k >= 1f || m_Shown == m_Score)
                {
                    m_Shown = m_Score;
                    m_From = m_Score;
                    m_Counting = false;
                }
            }
            Set(m_ScoreText, s_Score[Mathf.Clamp(m_Shown / ScoreStep, 0, s_Score.Length - 1)], ref m_ShownScore);
            Set(m_ComboText, s_Combo[Mathf.Clamp(m_Combo, 0, s_Combo.Length - 1)], ref m_ShownCombo);
            Set(m_LivesText, s_Lives[Mathf.Clamp(m_Lives, 0, s_Lives.Length - 1)], ref m_ShownLives);
            string line = m_Phase == Phase.Serve ? (m_Board.BossActive ? BossText : ServeText) : m_Phase == Phase.BossIntro ? BossText : "";
            Set(m_Line, line, ref m_ShownLine);
        }

        bool m_Counting;

        static void Set(Text t, string s, ref string shown)
        {
            if (t != null && !ReferenceEquals(s, shown))
            {
                shown = s;
                t.text = s;
            }
        }
    }
}
