using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace xpTURN.Feedback
{
    // a context's pools — one per prefab. The resting instances wait under an inactive container (their own active state as
    // made — the prepare wakes none); the live ones are one array with their owner (the binding · its record · the play) and
    // their lifetime. Runtime: the container is DontDestroyOnLoad; a test context's is in the active scene; a preview's is
    // hidden (HideAndDontSave · the preview prefix — every instance too, so the recovery finds one moved out of it).
    // A spawn is placed under the inactive container, then given its parent — it wakes placed
    internal sealed class PoolService : FeedbackService
    {
        internal const string GrowWarning = "Feedback pool of '{0}' grew to {1}: raise its pool size.";
        internal const string ContainerName = "xpTURN.Feedback.Pools";

        sealed class Pool
        {
            internal GameObject Prefab;
            internal GameObject[] Resting = new GameObject[4];
            internal int RestingCount;
            internal int Size;
            internal int Made;
            internal int Grown;
            // an optional assembly's warm of each instance made
            internal System.Action<GameObject, FeedbackContext> Warm;
#if DEBUG
            internal bool Warned;
#endif
        }

        struct Live
        {
            internal GameObject Instance;
            internal Pool Pool;
            internal float Remaining;
            internal bool Unscaled;
            internal ItemBinding Owner;
            internal System.Action<GameObject> Expire;
            internal System.Func<GameObject, bool> Done;
            internal int Record;
            internal long Serial;
        }

        readonly Dictionary<GameObject, Pool> m_Pools = new Dictionary<GameObject, Pool>();
        Live[] m_Live = new Live[16];
        int m_LiveCount;
        int m_Made;
        Transform m_Container;

        internal PoolService(FeedbackContext context)
            : base(context)
        {
        }

        bool IsPreview => Context.Kind == ContextKind.Preview;

        bool Persists => Context.Kind == ContextKind.Runtime && Application.isPlaying;

        internal Transform Container
        {
            get
            {
                if (m_Container == null)
                {
                    var go = new GameObject(IsPreview ? PropertyLayerRegistry.PreviewPrefix + "Pools" : ContainerName);
                    if (IsPreview)
                        go.hideFlags = HideFlags.HideAndDontSave;
                    go.SetActive(false);
                    if (Persists)
                        Object.DontDestroyOnLoad(go);
                    m_Container = go.transform;
                }
                return m_Container;
            }
        }

        // ---- observation
        internal int LiveCount => m_LiveCount;

        internal GameObject LiveAt(int i) => m_Live[i].Instance;

        internal int RestingOf(GameObject prefab) => m_Pools.TryGetValue(prefab, out Pool p) ? p.RestingCount : 0;

        internal int MadeOf(GameObject prefab) => m_Pools.TryGetValue(prefab, out Pool p) ? p.Made : 0;

        internal int GrownOf(GameObject prefab) => m_Pools.TryGetValue(prefab, out Pool p) ? p.Grown : 0;

        // the preview's automatic end waits while a live one has a lifetime left
        internal bool HoldsTimed
        {
            get
            {
                for (int i = 0; i < m_LiveCount; i++)
                {
                    if (m_Live[i].Instance != null && (!float.IsPositiveInfinity(m_Live[i].Remaining) || m_Live[i].Done != null))
                        return true;
                }
                return false;
            }
        }

        // the debugger: a row a pool — and one for the instances made without a pool
        internal void Describe(List<PoolRow> into)
        {
            into.Clear();
            int unpooled = 0;
            for (int i = 0; i < m_LiveCount; i++)
            {
                if (m_Live[i].Pool == null)
                    unpooled++;
            }
            foreach (KeyValuePair<GameObject, Pool> pair in m_Pools)
            {
                Pool pool = pair.Value;
                int live = 0;
                for (int i = 0; i < m_LiveCount; i++)
                {
                    if (m_Live[i].Pool == pool)
                        live++;
                }
                into.Add(new PoolRow(pool.Prefab, true, live, pool.RestingCount, pool.Grown));
            }
            if (unpooled > 0)
                into.Add(new PoolRow(null, false, unpooled, 0, 0));
        }

        // the preparation path — the largest size asked. The new instances' transforms are read once here: a transform's first
        // read allocates
        internal void Reserve(GameObject prefab, int size) => Reserve(prefab, size, null);

        // a static warm (no capture) — each instance made (reserved or grown) is given to it once, after it is placed under the
        // container
        internal void Reserve(GameObject prefab, int size, System.Action<GameObject, FeedbackContext> warm)
        {
            if (Refuses() || prefab == null)
                return;
            Pool pool = PoolOf(prefab);
            if (warm != null)
                pool.Warm = warm;
            if (size > pool.Size)
                pool.Size = size;
            while (pool.Made < pool.Size)
                Push(pool, Make(pool.Prefab, pool));
            // the live array holds every pooled instance without growing on a take
            if (m_Live.Length < m_Made)
                System.Array.Resize(ref m_Live, m_Made);
        }

        Pool PoolOf(GameObject prefab)
        {
            if (!m_Pools.TryGetValue(prefab, out Pool pool))
            {
                pool = new Pool { Prefab = prefab };
                m_Pools.Add(prefab, pool);
            }
            return pool;
        }

        static readonly List<Component> s_Warm = new List<Component>(8);

        GameObject Make(GameObject prefab, Pool pool)
        {
            GameObject instance = Object.Instantiate(prefab, Container, false);
            if (IsPreview)
            {
                instance.name = PropertyLayerRegistry.PreviewPrefix + prefab.name;
                instance.hideFlags = HideFlags.HideAndDontSave;
            }
            instance.transform.SetParent(Container, false);
            // every root component once — a component's first access makes its wrapper (allocates)
            instance.GetComponents(s_Warm);
            s_Warm.Clear();
            if (pool != null && pool.Warm != null)
                pool.Warm(instance, Context);
            if (pool != null)
            {
                pool.Made++;
                m_Made++;
            }
            return instance;
        }

        static void Push(Pool pool, GameObject instance)
        {
            if (pool.RestingCount == pool.Resting.Length)
                System.Array.Resize(ref pool.Resting, pool.Resting.Length * 2);
            pool.Resting[pool.RestingCount++] = instance;
        }

        // a resting one — a destroyed one is dropped; none left: one more (an allocation — a warning once in development
        // builds)
        GameObject Pop(Pool pool)
        {
            while (pool.RestingCount > 0)
            {
                GameObject instance = pool.Resting[--pool.RestingCount];
                pool.Resting[pool.RestingCount] = null;
                if (instance != null)
                    return instance;
                pool.Made--;
                m_Made--;
            }
            if (pool.Prefab == null)
                return null;
            pool.Grown++;
#if DEBUG
            if (!pool.Warned)
            {
                pool.Warned = true;
                Debug.LogWarning(string.Format(GrowWarning, pool.Prefab.name, pool.Made + 1), pool.Prefab);
            }
#endif
            return Make(pool.Prefab, pool);
        }

        // an instance placed before it wakes: the place under the inactive container, then the parent. None (the scene root)
        // of a DontDestroyOnLoad container goes to the active scene — a made instance's scene
        internal GameObject Spawn(GameObject prefab, bool pooled, Vector3 position, Quaternion rotation, Transform parent, float lifetime, bool unscaled, ItemBinding owner, int record, long serial)
            => Spawn(prefab, pooled, position, rotation, parent, lifetime, unscaled, owner, record, serial, null, null);

        // an optional assembly's expiry (at the lifetime's end — without one the instance goes back) and its end (true gives it
        // back) — static delegates, no capture
        internal GameObject Spawn(GameObject prefab, bool pooled, Vector3 position, Quaternion rotation, Transform parent, float lifetime, bool unscaled, ItemBinding owner, int record, long serial,
            System.Action<GameObject> expire, System.Func<GameObject, bool> done)
        {
            if (Refuses() || prefab == null)
                return null;
            Pool pool = null;
            GameObject instance;
            if (pooled)
            {
                pool = PoolOf(prefab);
                instance = Pop(pool);
                if (instance == null)
                    return null;
            }
            else
                instance = Make(prefab, null);
            Transform t = instance.transform;
            t.SetPositionAndRotation(position, rotation);
            if (!instance.activeSelf)
                instance.SetActive(true);
            t.SetParent(parent, true);
            if (parent == null && Persists)
                SceneManager.MoveGameObjectToScene(instance, SceneManager.GetActiveScene());
            if (m_LiveCount == m_Live.Length)
                System.Array.Resize(ref m_Live, m_Live.Length * 2);
            m_Live[m_LiveCount++] = new Live
            {
                Instance = instance,
                Pool = pool,
                Remaining = lifetime > 0f ? lifetime : float.PositiveInfinity,
                Unscaled = unscaled,
                Owner = owner,
                Record = record,
                Serial = serial,
                Expire = expire,
                Done = done,
            };
            return instance;
        }

        // a live one back — to its pool (under the container, which puts it to sleep), a made one destroyed
        void ReturnAt(int i)
        {
            Live live = m_Live[i];
            m_Live[i] = m_Live[--m_LiveCount];
            m_Live[m_LiveCount] = default;
            if (live.Instance == null)
            {
                if (live.Pool != null)
                {
                    live.Pool.Made--;
                    m_Made--;
                }
                return;
            }
            if (live.Pool == null)
            {
                Destroy(live.Instance);
                return;
            }
            live.Instance.transform.SetParent(Container, false);
            Push(live.Pool, live.Instance);
        }

        // a despawn: true when the object is one of the live ones
        internal bool TryReturn(GameObject instance)
        {
            for (int i = 0; i < m_LiveCount; i++)
            {
                if (ReferenceEquals(m_Live[i].Instance, instance))
                {
                    ReturnAt(i);
                    return true;
                }
            }
            return false;
        }

        // the owner's live ones — of a record (−1: every record) of a play (−1: every play)
        internal int ReturnOwned(ItemBinding owner, int record, long serial)
        {
            int n = 0;
            for (int i = m_LiveCount - 1; i >= 0; i--)
            {
                Live live = m_Live[i];
                if (!ReferenceEquals(live.Owner, owner) || (record >= 0 && live.Record != record) || (serial >= 0 && live.Serial != serial))
                    continue;
                ReturnAt(i);
                n++;
            }
            return n;
        }

        // a binding let go: its live ones keep their lifetime, no owner
        internal void Disown(ItemBinding owner)
        {
            for (int i = 0; i < m_LiveCount; i++)
            {
                if (ReferenceEquals(m_Live[i].Owner, owner))
                    m_Live[i].Owner = null;
            }
        }

        // a Rebuild: the new binding takes the old one's
        internal void Reown(ItemBinding from, ItemBinding to)
        {
            for (int i = 0; i < m_LiveCount; i++)
            {
                if (ReferenceEquals(m_Live[i].Owner, from))
                    m_Live[i].Owner = to;
            }
        }

        // RunUpdate's services step: a destroyed live one let go (a scene's unload, the game's Destroy), the lifetimes on the
        // spawn's clock
        internal override void Update(in FrameTime time)
        {
            for (int i = m_LiveCount - 1; i >= 0; i--)
            {
                if (m_Live[i].Instance == null)
                {
                    ReturnAt(i);
                    continue;
                }
                if (m_Live[i].Done != null && m_Live[i].Done(m_Live[i].Instance))
                {
                    ReturnAt(i);
                    continue;
                }
                if (float.IsPositiveInfinity(m_Live[i].Remaining))
                    continue;
                m_Live[i].Remaining -= m_Live[i].Unscaled ? time.UnscaledDelta : time.ScaledDelta;
                if (m_Live[i].Remaining > 0f)
                    continue;
                if (m_Live[i].Expire == null)
                    ReturnAt(i);
                else
                {
                    m_Live[i].Remaining = float.PositiveInfinity;
                    m_Live[i].Expire(m_Live[i].Instance);
                }
            }
        }

        // Destroy in play, DestroyImmediate in the edit mode — Destroy there logs an error and destroys nothing
        static void Destroy(Object o)
        {
            if (Application.isPlaying)
                Object.Destroy(o);
            else
                Object.DestroyImmediate(o);
        }

        // every instance — live and resting — and the container
        internal override void Shutdown()
        {
            for (int i = 0; i < m_LiveCount; i++)
            {
                if (m_Live[i].Instance != null)
                    Destroy(m_Live[i].Instance);
                m_Live[i] = default;
            }
            m_LiveCount = 0;
            foreach (KeyValuePair<GameObject, Pool> pair in m_Pools)
            {
                Pool pool = pair.Value;
                for (int i = 0; i < pool.RestingCount; i++)
                {
                    if (pool.Resting[i] != null)
                        Destroy(pool.Resting[i]);
                    pool.Resting[i] = null;
                }
                pool.RestingCount = 0;
            }
            m_Pools.Clear();
            m_Made = 0;
            if (m_Container != null)
                Destroy(m_Container.gameObject);
            m_Container = null;
        }
    }

    // a pool's row of the debugger — the prefab, its live, resting and grown counts; one row not pooled: the instances made
    internal readonly struct PoolRow
    {
        internal readonly GameObject Prefab;
        internal readonly bool Pooled;
        internal readonly int Live;
        internal readonly int Resting;
        internal readonly int Grown;

        internal PoolRow(GameObject prefab, bool pooled, int live, int resting, int grown)
        {
            Prefab = prefab;
            Pooled = pooled;
            Live = live;
            Resting = resting;
            Grown = grown;
        }
    }
}
