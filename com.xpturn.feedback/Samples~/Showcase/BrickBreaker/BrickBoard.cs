using UnityEngine;

namespace xpTURN.Feedback.Samples.Showcase
{
    // S13's board, kinematic and pure (no Unity object): a ball, a paddle, 32 bricks (the top two rows take two hits) and a boss —
    // circle against boxes in sub-steps of at most MaxStep, one brick or one boss hit per sub-step. A step's events go to a fixed
    // array the lab reads after it; the same inputs give the same state (the game does not wait for any feedback)
    public sealed class BrickBoard
    {
        public enum Kind { Paddle, Crack, Break, BossHit, BossDefeat, Lost, Cleared }

        public struct Event
        {
            public Kind Kind;
            public int Index;
            public Vector2 At;
            // the ball's speed against the slowest (paddle hits — 0 at the start speed, 1 at the top)
            public float Strength;
        }

        public const int Columns = 8, Rows = 4, BrickCount = Columns * Rows, HardRows = 2;
        public const float HalfWidth = 6f, Top = 9f;
        public const float BallRadius = 0.18f, StartSpeed = 7f, SpeedStep = 0.25f, TopSpeed = 11f;
        public const float PaddleY = 0.6f, PaddleHalfWidth = 1.1f, PaddleHalfHeight = 0.15f, PaddleLimit = HalfWidth - PaddleHalfWidth;
        public const float BrickHalfWidth = 0.7f, BrickHalfHeight = 0.25f, BrickPitchX = 1.5f, BrickPitchY = 0.6f, BrickTopY = 8f;
        public const float BossY = 7f, BossHalfWidth = 1.5f, BossHalfHeight = 0.6f, BossSwing = 3f, BossPeriod = 4f;
        public const int BossHealth = 12, RageAt = 4;
        // a hit inside it bounces the ball and leaves the health — the boss's flashes stay under the limit
        public const float BossCooldown = 0.5f;
        public const float MaxStep = 0.15f, LostY = -0.6f, MaxBounce = 60f * Mathf.Deg2Rad;
        const int EventCapacity = 32;

        readonly int[] m_Hits = new int[BrickCount];
        readonly Event[] m_Events = new Event[EventCapacity];
        Vector2 m_Ball, m_Velocity;
        float m_Speed, m_PaddleX, m_BossTime, m_BossGuard;
        bool m_Held = true;
        int m_Left, m_BossHp, m_EventCount;
        bool m_BossActive;

        public Vector2 Ball => m_Ball;
        public Vector2 Velocity => m_Velocity;
        public float PaddleX => m_PaddleX;
        public bool Held => m_Held;
        public int BricksLeft => m_Left;
        public bool BossActive => m_BossActive;
        public int BossHp => m_BossHp;
        public bool Raging => m_BossActive && m_BossHp <= RageAt;
        public int EventCount => m_EventCount;
        public Event EventAt(int i) => m_Events[i];
        public int HitsLeft(int brick) => m_Hits[brick];

        public BrickBoard() => Reset();

        public static Vector2 BrickCenter(int i) => new Vector2(-HalfWidth + BrickPitchX * 0.5f + BrickPitchX * (i % Columns), BrickTopY - BrickPitchY * (i / Columns));

        public Vector2 BossCenter => new Vector2(BossSwing * Mathf.Sin(m_BossTime * (Raging ? 1.5f : 1f) * 2f * Mathf.PI / BossPeriod), BossY);

        public void Reset()
        {
            for (int i = 0; i < BrickCount; i++)
                m_Hits[i] = i / Columns < HardRows ? 2 : 1;
            m_Left = BrickCount;
            m_BossActive = false;
            m_BossHp = 0;
            m_BossTime = 0f;
            m_BossGuard = 0f;
            m_PaddleX = 0f;
            m_Speed = StartSpeed;
            Hold();
        }

        // the ball back on the paddle (a lost ball · a boss entrance), the speed kept
        public void Hold()
        {
            m_Held = true;
            m_Velocity = Vector2.zero;
            m_Ball = new Vector2(m_PaddleX, PaddleY + PaddleHalfHeight + BallRadius);
        }

        // the tests' seam: the ball somewhere, moving (not held)
        internal void Put(Vector2 ball, Vector2 velocity)
        {
            m_Held = false;
            m_Ball = ball;
            m_Velocity = velocity;
            m_Speed = velocity.magnitude;
        }

        public void Launch()
        {
            if (!m_Held)
                return;
            m_Held = false;
            m_Velocity = new Vector2(Mathf.Sin(15f * Mathf.Deg2Rad), Mathf.Cos(15f * Mathf.Deg2Rad)) * m_Speed;
        }

        // the remaining bricks out at once (Action3) — no score, no events
        public void ClearBricks()
        {
            for (int i = 0; i < BrickCount; i++)
                m_Hits[i] = 0;
            m_Left = 0;
        }

        public void StartBoss()
        {
            m_BossActive = true;
            m_BossHp = BossHealth;
            m_BossTime = 0f;
            m_BossGuard = 0f;
            Hold();
        }

        public void Step(float dt, float paddleX)
        {
            m_EventCount = 0;
            m_PaddleX = Mathf.Clamp(paddleX, -PaddleLimit, PaddleLimit);
            if (dt <= 0f)
                return;
            if (m_BossActive)
            {
                m_BossTime += dt;
                m_BossGuard = Mathf.Max(0f, m_BossGuard - dt);
            }
            if (m_Held)
            {
                m_Ball = new Vector2(m_PaddleX, PaddleY + PaddleHalfHeight + BallRadius);
                return;
            }
            float travel = m_Velocity.magnitude * dt;
            int parts = Mathf.Max(1, Mathf.CeilToInt(travel / MaxStep));
            float part = dt / parts;
            for (int k = 0; k < parts && !m_Held; k++)
                SubStep(part);
        }

        void SubStep(float dt)
        {
            m_Ball += m_Velocity * dt;
            // walls
            if (m_Ball.x < -HalfWidth + BallRadius) { m_Ball.x = -HalfWidth + BallRadius; m_Velocity.x = Mathf.Abs(m_Velocity.x); }
            if (m_Ball.x > HalfWidth - BallRadius) { m_Ball.x = HalfWidth - BallRadius; m_Velocity.x = -Mathf.Abs(m_Velocity.x); }
            if (m_Ball.y > Top - BallRadius) { m_Ball.y = Top - BallRadius; m_Velocity.y = -Mathf.Abs(m_Velocity.y); }
            // the paddle — falling only; the place of the hit picks the angle
            if (m_Velocity.y < 0f && Overlap(new Vector2(m_PaddleX, PaddleY), PaddleHalfWidth, PaddleHalfHeight, out _))
            {
                float offset = Mathf.Clamp((m_Ball.x - m_PaddleX) / PaddleHalfWidth, -1f, 1f);
                m_Speed = Mathf.Min(TopSpeed, m_Speed + SpeedStep);
                float angle = offset * MaxBounce;
                m_Velocity = new Vector2(Mathf.Sin(angle), Mathf.Cos(angle)) * m_Speed;
                m_Ball.y = PaddleY + PaddleHalfHeight + BallRadius;
                Add(Kind.Paddle, -1, m_Ball, (m_Speed - StartSpeed) / (TopSpeed - StartSpeed));
            }
            // one brick or the boss per sub-step
            if (!HitBrick() && m_BossActive)
                HitBoss();
            if (m_Ball.y < LostY)
            {
                Add(Kind.Lost, -1, m_Ball, 0f);
                Hold();
            }
        }

        bool HitBrick()
        {
            for (int i = 0; i < BrickCount; i++)
            {
                if (m_Hits[i] == 0 || !Overlap(BrickCenter(i), BrickHalfWidth, BrickHalfHeight, out Vector2 normal))
                    continue;
                Bounce(normal);
                m_Hits[i]--;
                if (m_Hits[i] > 0)
                    Add(Kind.Crack, i, BrickCenter(i), 0f);
                else
                {
                    m_Left--;
                    Add(Kind.Break, i, BrickCenter(i), 0f);
                    if (m_Left == 0 && !m_BossActive)
                        Add(Kind.Cleared, -1, BrickCenter(i), 0f);
                }
                return true;
            }
            return false;
        }

        void HitBoss()
        {
            Vector2 c = BossCenter;
            if (m_BossHp <= 0 || !Overlap(c, BossHalfWidth, BossHalfHeight, out Vector2 normal))
                return;
            Bounce(normal);
            if (m_BossGuard > 0f)
                return;
            m_BossGuard = BossCooldown;
            m_BossHp--;
            if (m_BossHp > 0)
                Add(Kind.BossHit, -1, c, 0f);
            else
            {
                Add(Kind.BossDefeat, -1, c, 0f);
                m_BossActive = false;
                Hold();
            }
        }

        // the ball against a box: the nearest point within the circle — out along the normal of the least overlap
        bool Overlap(Vector2 center, float halfW, float halfH, out Vector2 normal)
        {
            normal = Vector2.zero;
            Vector2 d = m_Ball - center;
            Vector2 nearest = new Vector2(Mathf.Clamp(d.x, -halfW, halfW), Mathf.Clamp(d.y, -halfH, halfH));
            Vector2 gap = d - nearest;
            if (gap.sqrMagnitude >= BallRadius * BallRadius)
                return false;
            float outX = halfW + BallRadius - Mathf.Abs(d.x);
            float outY = halfH + BallRadius - Mathf.Abs(d.y);
            if (outX < outY)
            {
                normal = new Vector2(Mathf.Sign(d.x), 0f);
                m_Ball.x = center.x + normal.x * (halfW + BallRadius);
            }
            else
            {
                normal = new Vector2(0f, Mathf.Sign(d.y));
                m_Ball.y = center.y + normal.y * (halfH + BallRadius);
            }
            return true;
        }

        void Bounce(Vector2 normal)
        {
            if (normal.x != 0f)
                m_Velocity.x = Mathf.Abs(m_Velocity.x) * normal.x;
            else
                m_Velocity.y = Mathf.Abs(m_Velocity.y) * normal.y;
        }

        void Add(Kind kind, int index, Vector2 at, float strength)
        {
            if (m_EventCount == EventCapacity)
                return;
            m_Events[m_EventCount++] = new Event { Kind = kind, Index = index, At = at, Strength = strength };
        }
    }
}
