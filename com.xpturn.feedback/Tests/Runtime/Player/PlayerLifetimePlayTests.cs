using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace xpTURN.Feedback.Tests
{
    // PlayMode — the runtime's lifetime in real callbacks: the configured time, destruction, clones, a lost runtime, a player
    // that never woke. The rows share the loop's own Default: a reservation is seen as a difference, and TearDown lets a frame
    // pass so Destroy releases. A row checks everything and judges at the end. No UnityEditor here
    public sealed class PlayerLifetimePlayTests
    {
        readonly List<string> m_Fail = new List<string>();
        readonly List<GameObject> m_Made = new List<GameObject>();
        readonly List<FeedbackPlayer> m_Players = new List<FeedbackPlayer>();
        readonly List<FeedbackScheduler> m_Schedulers = new List<FeedbackScheduler>();
        FeedbackScheduler m_SavedDefault;
        bool m_DefaultSwapped;

        [SetUp]
        public void SetUp()
        {
            m_Fail.Clear();
            PlayerTestBinding.Reset();
            PlayerTestBinding.Register();
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (m_DefaultSwapped)
            {
                FeedbackScheduler.Default = m_SavedDefault;
                m_DefaultSwapped = false;
            }
            foreach (GameObject go in m_Made)
            {
                if (go != null)
                    Object.Destroy(go);
            }
            m_Made.Clear();
            yield return null;
            // a red row can leave a runtime behind
            foreach (FeedbackPlayer player in m_Players)
                player.ReleaseRuntime();
            m_Players.Clear();
            foreach (FeedbackScheduler scheduler in m_Schedulers)
                scheduler.Shutdown();
            m_Schedulers.Clear();
            PlayerTestBinding.Reset();
        }

        void Check(bool ok, string what)
        {
            if (!ok)
                m_Fail.Add(what);
        }

        void End() => Assert.That(m_Fail, Is.Empty);

        static bool Has(FeedbackPlayer player) => player.Runtime != null;

        FeedbackScheduler Scheduler(ContextKind kind)
        {
            var scheduler = new FeedbackScheduler(kind);
            m_Schedulers.Add(scheduler);
            return scheduler;
        }

        void SwapDefault(FeedbackScheduler scheduler)
        {
            if (!m_DefaultSwapped)
            {
                m_SavedDefault = FeedbackScheduler.Default;
                m_DefaultSwapped = true;
            }
            FeedbackScheduler.Default = scheduler;
        }

        void RestoreDefault()
        {
            FeedbackScheduler.Default = m_SavedDefault;
            m_DefaultSwapped = false;
        }

        // made inactive, so the fields are set before Awake. Overlap 2: the reservation is 2
        FeedbackPlayer Make(PlayerInitialization initialization, bool activate = true, bool componentEnabled = true, Type before = null, Type after = null)
        {
            var go = new GameObject("player");
            m_Made.Add(go);
            go.SetActive(false);
            if (before != null)
                go.AddComponent(before);
            var player = go.AddComponent<FeedbackPlayer>();
            m_Players.Add(player);
            if (after != null)
                go.AddComponent(after);
            player.m_Initialization = initialization;
            player.m_Policy = PlayPolicy.Overlap;
            player.m_MaxOverlap = 2;
            player.m_Items.Add(new PlayerTestAction { m_Label = "a", m_Length = 0.5f });
            player.enabled = componentEnabled;
            if (activate)
                go.SetActive(true);
            return player;
        }

        FeedbackPlayer Clone(FeedbackPlayer source)
        {
            FeedbackPlayer clone = Object.Instantiate(source);
            m_Made.Add(clone.gameObject);
            m_Players.Add(clone);
            return clone;
        }

        static string Try(Func<bool> call)
        {
            try
            {
                return call().ToString();
            }
            catch (Exception e)
            {
                return e.GetType().Name;
            }
        }

        // ---- the configured time is "at the latest then"

        [UnityTest]
        public IEnumerator CP01_ConfiguredTime()
        {
            yield return null;
            FeedbackPlayer awake = Make(PlayerInitialization.Awake);
            FeedbackPlayer start = Make(PlayerInitialization.Start);
            FeedbackPlayer lazy = Make(PlayerInitialization.Lazy);
            Check(Has(awake), "Awake mode is prepared inside the activating call");
            Check(!Has(start), "Start mode is not prepared right after the activation");
            Check(!Has(lazy), "Lazy mode is not prepared right after the activation");
            yield return null;
            Check(Has(start), "Start mode is prepared a frame later");
            for (int i = 0; i < 3; i++)
                yield return null;
            Check(!Has(lazy), "Lazy mode is not prepared by the frames");
            Check(lazy.EnsureRuntime() && Has(lazy), "Lazy mode is prepared by the call");

            FeedbackPlayer early = Make(PlayerInitialization.Start);
            Check(early.EnsureRuntime(), "a call before Start prepares");
            PlayerRuntime first = early.Runtime;
            yield return null;
            Check(first != null && ReferenceEquals(first, early.Runtime), "Start does not make the runtime again");

            FeedbackPlayer disabledAwake = Make(PlayerInitialization.Awake, componentEnabled: false);
            FeedbackPlayer disabledStart = Make(PlayerInitialization.Start, componentEnabled: false);
            FeedbackPlayer inactive = Make(PlayerInitialization.Awake, activate: false);
            yield return null;
            yield return null;
            Check(Has(disabledAwake), "a disabled component in Awake mode is prepared");
            Check(!Has(disabledStart), "a disabled component in Start mode is not prepared");
            Check(!Has(inactive), "an inactive object is not prepared");
            disabledStart.enabled = true;
            inactive.gameObject.SetActive(true);
            Check(!Has(disabledStart), "enabling does not prepare a Start-mode player that has not started");
            Check(Has(inactive), "the activating call prepares an Awake-mode player");
            yield return null;
            Check(Has(disabledStart), "its Start does, a frame later");
            End();
        }

        // ---- destruction — and its window: the mark stands before the object is null

        [UnityTest]
        public IEnumerator CP02_Destruction()
        {
            yield return null;
            FeedbackScheduler scheduler = FeedbackScheduler.Default;
            FeedbackPlayer player = Make(PlayerInitialization.Awake);
            PlayerTestBinding binding = PlayerTestBinding.Made.LastOrDefault();
            int reserved = scheduler.ReservedCount;
            Object.Destroy(player.gameObject);
            yield return null;
            Check(scheduler.ReservedCount == reserved - 2, "Destroy gives the reservation back within a frame (" + (scheduler.ReservedCount - reserved) + ")");
            Check(binding != null && binding.Released == 1, "the binding is released once (" + (binding != null ? binding.Released : -1) + ")");
            Check(Try(player.EnsureRuntime) == "False" && Try(player.Rebuild) == "False" && !Has(player), "a destroyed player's EnsureRuntime · Rebuild are false and make no runtime");
            Check(scheduler.ReservedCount == reserved - 2, "nothing is reserved by the calls on a destroyed player (" + (scheduler.ReservedCount - reserved) + ")");

            FeedbackPlayer immediate = Make(PlayerInitialization.Awake);
            int before = scheduler.ReservedCount;
            Object.DestroyImmediate(immediate.gameObject);
            Check(scheduler.ReservedCount == before - 2 && !Has(immediate), "DestroyImmediate releases inside the call (" + (scheduler.ReservedCount - before) + ")");

            // a script after the player calls from its own OnDestroy
            FeedbackPlayer watched = Make(PlayerInitialization.Awake, after: typeof(PlayerReleaseWitness));
            var witness = watched.GetComponent<PlayerReleaseWitness>();
            witness.Player = watched;
            before = scheduler.ReservedCount;
            Object.Destroy(watched.gameObject);
            yield return null;
            yield return null;
            Check(witness.Called == "the player's runtime none → ensure False · rebuild False", "a call from a later script's OnDestroy is false (" + witness.Called + ")");
            Check(!Has(watched) && scheduler.ReservedCount == before - 2, "nothing is left after the destruction (" + (scheduler.ReservedCount - before) + ")");
            End();
        }

        // ---- clones — neither the runtime nor didStart is copied

        [UnityTest]
        public IEnumerator CP03_Clones()
        {
            yield return null;
            FeedbackScheduler scheduler = FeedbackScheduler.Default;
            FeedbackPlayer awake = Make(PlayerInitialization.Awake);
            FeedbackPlayer start = Make(PlayerInitialization.Start);
            yield return null;
            Check(Has(awake) && Has(start), "the sources are prepared");
            int reserved = scheduler.ReservedCount;
            FeedbackPlayer cloneOfAwake = Clone(awake);
            Check(Has(cloneOfAwake) && !ReferenceEquals(cloneOfAwake.Runtime, awake.Runtime) && scheduler.ReservedCount == reserved + 2, "a clone of a prepared Awake player has its own runtime at once (" + (scheduler.ReservedCount - reserved) + ")");
            FeedbackPlayer cloneOfStart = Clone(start);
            Check(!Has(cloneOfStart), "a clone of a started Start player is not prepared right after Instantiate");
            yield return null;
            Check(Has(cloneOfStart) && !ReferenceEquals(cloneOfStart.Runtime, start.Runtime), "it is a frame later, by its own Start");
            End();
        }

        // ---- a lost runtime (a recompile during play, simulated) · another Default · a closed bound scheduler · a closed Default

        [UnityTest]
        public IEnumerator CP04_LostRuntime()
        {
            yield return null;
            FeedbackPlayer awake = Make(PlayerInitialization.Awake);
            FeedbackPlayer start = Make(PlayerInitialization.Start);
            FeedbackPlayer lazy = Make(PlayerInitialization.Lazy);
            yield return null;
            lazy.EnsureRuntime();
            FeedbackPlayer notStarted = Make(PlayerInitialization.Start);
            foreach (FeedbackPlayer player in new[] { awake, start, lazy, notStarted })
            {
                player.ReleaseRuntime();
                player.enabled = false;
                player.enabled = true;
            }
            Check(Has(awake), "OnEnable prepares an Awake-mode player again");
            Check(Has(start), "OnEnable prepares a started Start-mode player again");
            Check(!Has(lazy), "OnEnable does not prepare a Lazy player");
            Check(!Has(notStarted), "OnEnable does not prepare a Start-mode player that has not started");
            yield return null;
            Check(Has(notStarted), "its Start does");

            // another Default: a new session's, a recompile's
            FeedbackScheduler saved = FeedbackScheduler.Default;
            FeedbackScheduler other = Scheduler(ContextKind.Test);
            int reserved = saved.ReservedCount;
            PlayerRuntime old = awake.Runtime;
            SwapDefault(other);
            Check(awake.EnsureRuntime(), "the entry is true on a new Default");
            Check(awake.Runtime != null && !ReferenceEquals(old, awake.Runtime) && awake.Runtime.Scheduler == other, "the runtime is made again on the new Default");
            Check(saved.ReservedCount == reserved - 2 && other.ReservedCount == 2, "the old reservation is given back (" + (saved.ReservedCount - reserved) + " · " + other.ReservedCount + ")");
            RestoreDefault();
            awake.EnsureRuntime();

            // a bound scheduler comes first; one that closed is dropped — the loop's scheduler takes over
            FeedbackPlayer bound = Make(PlayerInitialization.Lazy);
            FeedbackScheduler preview = Scheduler(ContextKind.Preview);
            bound.Bind(preview);
            Check(bound.EnsureRuntime() && bound.Runtime.Scheduler == preview, "a bound scheduler comes first");
            preview.Shutdown();
            Check(bound.EnsureRuntime() && bound.Runtime != null && bound.Runtime.Scheduler == FeedbackScheduler.Default, "a closed bound scheduler is dropped: the runtime is made on Default");

            // a closed Default (the app is quitting): nothing is prepared on it
            FeedbackPlayer late = Make(PlayerInitialization.Lazy);
            FeedbackScheduler closed = Scheduler(ContextKind.Test);
            closed.Shutdown();
            SwapDefault(closed);
            Check(!late.EnsureRuntime() && late.Runtime == null && !late.Rebuild(), "a closed Default is false");
            Check(!bound.EnsureRuntime() && bound.Runtime == null, "a prepared player's entry is false on a closed Default and leaves no runtime");
            RestoreDefault();
            End();
        }

        // ---- on Default only a player whose OnDestroy will come — one that woke, or whose object is active

        [UnityTest]
        public IEnumerator CP05_NeverAwake()
        {
            yield return null;
            FeedbackScheduler scheduler = FeedbackScheduler.Default;
            int reserved = scheduler.ReservedCount;
            FeedbackPlayer never = Make(PlayerInitialization.Start, activate: false);
            Check(Try(never.EnsureRuntime) == "False" && Try(never.Rebuild) == "False" && !Has(never) && scheduler.ReservedCount == reserved, "a player that never woke is not prepared (" + (scheduler.ReservedCount - reserved) + ")");
            Object.Destroy(never.gameObject);
            yield return null;
            yield return null;
            Check(Try(never.EnsureRuntime) == "False" && Try(never.Rebuild) == "False" && scheduler.ReservedCount == reserved, "destroyed without ever waking: false, no exception, nothing reserved (" + (scheduler.ReservedCount - reserved) + ")");

            FeedbackPlayer late = Make(PlayerInitialization.Awake, activate: false);
            Check(!late.EnsureRuntime() && !Has(late), "an inactive Awake-mode player is not prepared by a call");
            late.gameObject.SetActive(true);
            Check(Has(late), "it is when it wakes");

            // a script before the player calls from its own Awake — the object is active, the player has not woken yet
            int before = scheduler.ReservedCount;
            FeedbackPlayer called = Make(PlayerInitialization.Lazy, activate: false, before: typeof(PlayerEarlyCaller));
            var caller = called.GetComponent<PlayerEarlyCaller>();
            caller.Player = called;
            called.gameObject.SetActive(true);
            Check(caller.Seen.Count > 0 && caller.Seen[0] == "Awake none→ensure True", "a call from an earlier script's Awake prepares (" + (caller.Seen.Count > 0 ? caller.Seen[0] : "-") + ")");
            PlayerRuntime first = called.Runtime;
            yield return null;
            Check(first != null && ReferenceEquals(first, called.Runtime) && scheduler.ReservedCount == before + 2, "the same runtime a frame later, reserved once (" + (scheduler.ReservedCount - before) + ")");
            Object.Destroy(called.gameObject);
            yield return null;
            Check(scheduler.ReservedCount == before, "its OnDestroy gives the reservation back (" + (scheduler.ReservedCount - before) + ")");

            FeedbackPlayer woke = Make(PlayerInitialization.Lazy);
            yield return null;
            woke.gameObject.SetActive(false);
            Check(woke.EnsureRuntime() && Has(woke), "a player that woke is prepared while its object is inactive");
            End();
        }
    }
}
