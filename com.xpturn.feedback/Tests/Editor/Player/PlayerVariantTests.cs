using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Events;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Serialization;
using Object = UnityEngine.Object;
using UIs = UnityEngine.TestTools.Constraints.Is;

namespace xpTURN.Feedback.Tests.Editor
{
    // EditMode — a player's Variants: the presets a Play picks instead of the list. A player is bound to a test scheduler that
    // the row ticks directly. The test items log their calls by label; the value rows move a Transform with Tween items. A
    // row checks everything and judges at the end
    public sealed class PlayerVariantTests
    {
        static readonly List<string> s_Warnings = new List<string>();
        readonly List<string> m_Fail = new List<string>();
        readonly List<Object> m_Made = new List<Object>();
        readonly List<FeedbackPlayer> m_Players = new List<FeedbackPlayer>();
        readonly List<FeedbackScheduler> m_Schedulers = new List<FeedbackScheduler>();
        readonly Dictionary<PlaybackHandle, int> m_HandleNo = new Dictionary<PlaybackHandle, int>();

        static void OnLog(string condition, string stackTrace, LogType type)
        {
            if (type == LogType.Warning)
                s_Warnings.Add(condition);
        }

        [SetUp]
        public void SetUp()
        {
            m_Fail.Clear();
            s_Warnings.Clear();
            m_HandleNo.Clear();
            PlayerTestBinding.Reset();
            PlayerTestBinding.Register();
            PlayerRuntime.ResetSession();
            Application.logMessageReceived += OnLog;
        }

        [TearDown]
        public void TearDown()
        {
            Application.logMessageReceived -= OnLog;
            foreach (FeedbackPlayer player in m_Players)
            {
                if (player != null)
                    player.ReleaseRuntime();
            }
            m_Players.Clear();
            foreach (Object o in m_Made)
            {
                if (o != null)
                    Object.DestroyImmediate(o);
            }
            m_Made.Clear();
            foreach (FeedbackScheduler scheduler in m_Schedulers)
            {
                if (!scheduler.IsShutdown)
                    scheduler.Shutdown();
            }
            m_Schedulers.Clear();
            PlayerTestBinding.Reset();
        }

        void Check(bool ok, string what)
        {
            if (!ok)
                m_Fail.Add(what);
        }

        void End() => Assert.That(m_Fail, Is.Empty);

        // ---- fixtures

        FeedbackScheduler Scheduler(ContextKind kind = ContextKind.Test)
        {
            var scheduler = new FeedbackScheduler(kind);
            m_Schedulers.Add(scheduler);
            return scheduler;
        }

        FeedbackPlayer Player(FeedbackScheduler scheduler, params FeedbackItem[] items)
        {
            var go = new GameObject("player");
            m_Made.Add(go);
            var player = go.AddComponent<FeedbackPlayer>();
            m_Players.Add(player);
            player.m_Items.AddRange(items);
            if (scheduler != null)
                player.Bind(scheduler);
            return player;
        }

        FeedbackPreset Preset(string name, params FeedbackItem[] items)
        {
            var preset = ScriptableObject.CreateInstance<FeedbackPreset>();
            preset.name = name;
            preset.m_Items.AddRange(items);
            m_Made.Add(preset);
            return preset;
        }

        GameObject Made(string name)
        {
            var go = new GameObject(name);
            m_Made.Add(go);
            return go;
        }

        static PlayerTestAction Act(string label, float length)
            => new PlayerTestAction { m_Label = label, m_Length = length };

        static PlayerTestAction After(PlayerTestAction action)
        {
            action.m_Timing.m_Rule = StartRule.AfterPrevious;
            return action;
        }

        static TargetRef Direct(Object o) => new TargetRef { m_Mode = TargetRefMode.Direct, m_Object = o, m_Path = "", m_Anchor = "" };

        static TargetRef Anchored(string name) => new TargetRef { m_Mode = TargetRefMode.Anchor, m_Path = "", m_Anchor = name };

        static PositionItem To(TargetRef target, Vector3 to, float seconds)
        {
            var item = new PositionItem { m_Target = target, m_Motion = new TweenMotion { m_Params = new TweenParams { m_Mode = TweenMode.To, m_To = to, m_Ease = default, m_Loops = 1 } } };
            item.m_Timing.m_Duration = seconds;
            return item;
        }

        static PlayArgs Of(FeedbackPreset preset) => PlayArgs.Default.WithPreset(preset);

        static PlayerTestBinding B(string label) => PlayerTestBinding.Made.LastOrDefault(b => b.Item.m_Label == label);

        static string Began()
        {
            string text = string.Join(" ", PlayerTestBinding.Log.Where(l => l.Contains(".Begin")).Select(l => l.Substring(0, l.IndexOf('.'))));
            PlayerTestBinding.Log.Clear();
            return text;
        }

        static string TakeLog()
        {
            string text = string.Join(" ", PlayerTestBinding.Log);
            PlayerTestBinding.Log.Clear();
            return text;
        }

        string No(PlaybackHandle handle)
        {
            if (handle == default)
                return "0";
            if (!m_HandleNo.TryGetValue(handle, out int n))
            {
                n = m_HandleNo.Count + 1;
                m_HandleNo[handle] = n;
            }
            return n.ToString(CultureInfo.InvariantCulture);
        }

        void Watch(FeedbackPlayer p)
        {
            p.Played += (pl, h) => PlayerTestBinding.Log.Add("|Played" + No(h));
            p.Completed += (pl, h) => PlayerTestBinding.Log.Add("|Completed" + No(h));
            p.Stopped += (pl, h) => PlayerTestBinding.Log.Add("|Stopped" + No(h));
            p.Paused += pl => PlayerTestBinding.Log.Add("|Paused");
            p.Resumed += pl => PlayerTestBinding.Log.Add("|Resumed");
        }

        static string Events() => string.Join(" ", PlayerTestBinding.Log.Where(l => l.Length > 0 && l[0] == '|'));

        static string P(Transform t) => t.localPosition.ToString("F2", CultureInfo.InvariantCulture);

        static bool Near(Vector3 a, Vector3 b) => (a - b).sqrMagnitude < 1e-6f;

        // ---- T1 · T2 · T3 — which list a Play plays

        [Test]
        public void VS01_APresetPicksItsList()
        {
            FeedbackScheduler s = Scheduler();
            GameObject hit = Made("hit");
            FeedbackPreset light = Preset("light", Act("light", 0.5f), To(Anchored("hit"), new Vector3(1f, 0f, 0f), 0.5f));
            FeedbackPreset heavy = Preset("heavy", Act("heavy", 0.5f), To(Anchored("hit"), new Vector3(0f, 3f, 0f), 0.5f));
            FeedbackPlayer p = Player(s, Act("base", 0.5f));
            p.SetAnchor("hit", hit);
            p.m_Variants.Add(light);
            p.m_Variants.Add(heavy);
            PlaybackHandle a = p.Play(Of(light));
            string first = Began();
            s.Tick(1f);
            Vector3 afterLight = hit.transform.localPosition;
            PlaybackHandle b = p.Play(Of(heavy));
            string second = Began();
            s.Tick(1f);
            Vector3 afterHeavy = hit.transform.localPosition;
            PlaybackHandle c = p.Play();
            string third = Began();
            Check(a != default && b != default && c != default, "three plays");
            Check(first == "light", "the light variant plays its list alone: '" + first + "'");
            Check(second == "heavy", "the heavy variant plays its list alone: '" + second + "'");
            Check(third == "base", "a Play without a preset plays the player's list: '" + third + "'");
            Check(Near(afterLight, new Vector3(1f, 0f, 0f)) && Near(afterHeavy, new Vector3(0f, 3f, 0f)), "the variants' targets resolve through the player's anchor: " + afterLight + " · " + afterHeavy);
            Check(B("light") != null && B("light").Context.Player == p, "a variant's binding is prepared with the player as its context");
            End();
        }

        [Test]
        public void VS02_TheLinkedPresetAndNull()
        {
            FeedbackScheduler s = Scheduler();
            FeedbackPreset linked = Preset("linked", Act("linked", 0.5f));
            FeedbackPreset variant = Preset("variant", Act("variant", 0.5f));
            FeedbackPlayer p = Player(s);
            p.m_Preset = linked;
            p.m_Variants.Add(variant);
            p.Play(Of(linked));
            string viaLinked = Began();
            p.Play(Of(null));
            string viaNull = Began();
            p.Play(Of(variant));
            string viaVariant = Began();
            PlayArgs cleared = Of(variant).WithPreset(null);
            Check(viaLinked == "linked" && viaNull == "linked", "the linked preset needs no Variants entry, null is the list: '" + viaLinked + "' · '" + viaNull + "'");
            Check(viaVariant == "variant", "the variant: '" + viaVariant + "'");
            Check(Of(variant).HasPreset && Of(variant).Preset == variant && !cleared.HasPreset && cleared.Preset == null && !PlayArgs.Default.HasPreset, "WithPreset sets and clears the mark");
            Check(!s_Warnings.Any(w => w.Contains("Variants")), "no Variants warning: " + string.Join(" | ", s_Warnings));
            End();
        }

        [Test]
        public void VS03_AnUnlistedPresetIsRefused()
        {
            FeedbackScheduler s = Scheduler();
            FeedbackPreset variant = Preset("variant", Act("variant", 1f));
            FeedbackPreset stranger = Preset("stranger", Act("stranger", 1f));
            FeedbackPlayer p = Player(s, Act("base", 1f));
            p.m_Variants.Add(variant);
            Watch(p);
            PlaybackHandle running = p.Play(Of(variant));
            PlayerTestBinding.Log.Clear();
            PlaybackHandle refused = p.Play(Of(stranger));
            PlaybackHandle again = p.Play(Of(stranger));
            string log = TakeLog();
            Check(running != default && refused == default && again == default, "the stranger's plays are refused");
            Check(log == "", "nothing plays or stops: '" + log + "'");
            Check(p.IsPlaying, "the variant's play goes on");
            Check(s_Warnings.Count(w => w.Contains("Variants")) == 1, "one warning: " + string.Join(" | ", s_Warnings));
            End();
        }

        // ---- T4 — a handle reaches its own runtime

        [Test]
        public void VS04_AHandleReachesItsList()
        {
            FeedbackScheduler s = Scheduler();
            FeedbackPreset variant = Preset("variant", Act("variant", 2f));
            FeedbackPlayer p = Player(s, Act("base", 1f));
            p.m_Policy = PlayPolicy.Overlap;
            p.m_Variants.Add(variant);
            FeedbackPlayer other = Player(s, Act("other", 1f));
            PlaybackHandle hb = p.Play();
            PlaybackHandle hv = p.Play(Of(variant));
            PlaybackHandle ho = other.Play();
            s.Tick(0.25f);
            Check(Mathf.Approximately(p.GetDuration(hv), 2f) && Mathf.Approximately(p.GetDuration(hb), 1f), "GetDuration: " + p.GetDuration(hv) + " · " + p.GetDuration(hb));
            Check(p.Position(hv) > 0 && p.Position(ho) < 0, "Position: " + p.Position(hv) + " · " + p.Position(ho));
            Check(p.SetHeld(hv, true) && p.SetHeld(hv, false), "SetHeld");
            var heard = new List<PlaybackEnd>();
            Check(p.SetListener(hv, new Listener(heard)), "SetListener");
            Check(!p.Stop(ho) && p.GetDuration(ho) == 0f, "another player's handle is not this player's");
            PlayerTestBinding.Log.Clear();
            Check(p.Stop(hv), "Stop(handle)");
            string log = TakeLog();
            Check(log == "variant.End:Hold variant.Settle" && p.IsPlaying && heard.Count == 1, "only the variant's play stops: '" + log + "' · heard " + heard.Count);
            End();
        }

        sealed class Listener : IPlaybackListener
        {
            readonly List<PlaybackEnd> m_Heard;

            internal Listener(List<PlaybackEnd> heard) => m_Heard = heard;

            public void OnPlaybackEnded(PlaybackHandle handle, PlaybackEnd end) => m_Heard.Add(end);
        }

        // ---- T5 · T6 — the policy, the cooldown and the Toggle cell are the player's

        [Test]
        public void VS05_ThePolicyIsThePlayers()
        {
            FeedbackScheduler s = Scheduler();
            FeedbackPreset variant = Preset("variant", Act("variant", 1f));
            // Restart: a variant's play ends the list's, and the other way
            FeedbackPlayer r = Player(s, Act("base", 1f));
            r.m_Variants.Add(variant);
            Watch(r);
            r.Play();
            r.Play(Of(variant));
            string restart = Events();
            string restartBegan = Began();
            r.Play();
            string back = Events();
            string backBegan = Began();
            Check(restart == "|Played1 |Stopped1 |Played2" && restartBegan == "base variant", "Restart, list then variant: '" + restart + "' · '" + restartBegan + "'");
            Check(back == "|Stopped2 |Played3" && backBegan == "base", "Restart, variant then list: '" + back + "' · '" + backBegan + "'");
            // Ignore: a play of any list refuses the next
            FeedbackPlayer i = Player(s, Act("ibase", 1f));
            i.m_Policy = PlayPolicy.Ignore;
            i.m_Variants.Add(variant);
            Check(i.Play() != default && i.Play(Of(variant)) == default, "Ignore refuses a variant while the list plays");
            // Overlap: the room is each list's own
            FeedbackPlayer o = Player(s, Act("obase", 1f));
            o.m_Policy = PlayPolicy.Overlap;
            o.m_MaxOverlap = 1;
            o.m_Variants.Add(variant);
            Watch(o);
            PlayerTestBinding.Log.Clear();
            o.Play();
            o.Play(Of(variant));
            Check(Events() == "|Played4 |Played5", "Overlap of one: each list has its room: '" + Events() + "'");
            PlayerTestBinding.Log.Clear();
            // a refused play stops nothing — the cooldown refuses after the list's play began
            FeedbackPlayer c = Player(s, Act("cbase", 1f));
            c.m_Cooldown = 1f;
            c.m_Variants.Add(variant);
            Watch(c);
            c.Play();
            s.Tick(0.1f);
            PlayerTestBinding.Log.Clear();
            PlaybackHandle cooled = c.Play(Of(variant));
            Check(cooled == default && Events() == "" && c.IsPlaying, "a refused variant play leaves the list's play: '" + Events() + "'");
            End();
        }

        [Test]
        public void VS06_TheCooldownAndTheToggleAreThePlayers()
        {
            FeedbackScheduler s = Scheduler();
            FeedbackPreset variant = Preset("variant", Act("variant", 0.2f));
            FeedbackPlayer p = Player(s, Act("base", 0.2f));
            p.m_Cooldown = 1f;
            p.m_Variants.Add(variant);
            Check(p.Play() != default, "the list's play");
            s.Tick(0.5f);
            Check(p.Play(Of(variant)) == default, "the cooldown holds across the variants");
            s.Tick(0.6f);
            Check(p.Play(Of(variant)) != default, "and ends");
            s.Tick(0.5f);
            Check(p.Play() == default, "a variant's play starts the cooldown for the list too");
            FeedbackPlayer t = Player(s, Act("tbase", 0.2f));
            t.m_Direction = PlayerDirection.Toggle;
            t.m_Variants.Add(variant);
            t.Play();
            s.Tick(0.5f);
            t.Play(Of(variant));
            bool variantBackward = B("variant") != null && B("variant").LastBegin.Reverse;
            s.Tick(0.5f);
            t.Play();
            bool baseForward = B("tbase") != null && !B("tbase").LastBegin.Reverse;
            Check(variantBackward && baseForward, "one Toggle cell: list forward, variant backward, list forward (" + variantBackward + " · " + baseForward + ")");
            End();
        }

        // ---- T7 — the calls without a handle reach every list

        [Test]
        public void VS07_TheCallsWithoutAHandleReachEveryList()
        {
            FeedbackScheduler s = Scheduler();
            FeedbackPreset variant = Preset("variant", Act("variant", 1f));
            FeedbackPreset waiting = Preset("waiting", new WaitForSignalItem(), After(Act("signaled", 0.1f)));
            FeedbackPlayer p = Player(s, Act("base", 1f));
            p.m_Policy = PlayPolicy.Overlap;
            p.m_Variants.Add(variant);
            Watch(p);
            p.Play();
            p.Play(Of(variant));
            s.Tick(0.1f);
            PlayerTestBinding.Log.Clear();
            p.Pause();
            Check(Events() == "|Paused" && p.HasPaused, "one Paused for both lists: '" + Events() + "'");
            PlayerTestBinding.Log.Clear();
            p.Resume();
            Check(Events() == "|Resumed", "one Resumed: '" + Events() + "'");
            PlayerTestBinding.Log.Clear();
            bool sought = p.Seek(0.5f);
            float baseT = B("base")?.LastTime.T ?? -1f;
            float variantT = B("variant")?.LastTime.T ?? -1f;
            Check(sought && Mathf.Approximately(baseT, 0.5f) && Mathf.Approximately(variantT, 0.5f), "Seek moves both: " + baseT + " · " + variantT);
            Check(p.IsPlaying, "IsPlaying");
            PlayerTestBinding.Log.Clear();
            Check(p.Skip(), "Skip");
            Check(Events() == "|Completed1 |Completed2" || Events() == "|Completed2 |Completed1", "Skip ends both: '" + Events() + "'");
            PlayerTestBinding.Log.Clear();
            p.Play();
            p.Play(Of(variant));
            PlayerTestBinding.Log.Clear();
            p.Stop();
            string stopped = Events();
            Check(stopped.Split(' ').Count(e => e.StartsWith("|Stopped")) == 2 && !p.IsPlaying, "Stop() ends both: '" + stopped + "'");
            FeedbackPlayer w = Player(s, new WaitForSignalItem(), After(Act("wsignaled", 0.1f)));
            w.m_Policy = PlayPolicy.Overlap;
            w.m_Variants.Add(waiting);
            w.Play();
            w.Play(Of(waiting));
            int open = w.OpenSignalWaits();
            string early = Began();
            w.Signal();
            s.Tick(0.05f);
            string signaled = string.Join(" ", Began().Split(' ').OrderBy(x => x, StringComparer.Ordinal));
            Check(open == 2 && early == "", "two open waits: " + open + " · '" + early + "'");
            Check(signaled == "signaled wsignaled", "one Signal ends both waits: '" + signaled + "'");
            Check(!s_Warnings.Any(w => w.Contains("FeedbackPlayer")), "no player warning: " + string.Join(" | ", s_Warnings));
            End();
        }

        // ---- T8 — the prepare, the reservation, Rebuild and the release

        [Test]
        public void VS08_PrepareRebuildRelease()
        {
            FeedbackScheduler s = Scheduler();
            FeedbackPreset linked = Preset("linked", Act("linked", 0.5f));
            FeedbackPreset a = Preset("a", Act("a", 0.5f));
            FeedbackPreset b = Preset("b", Act("b", 0.5f));
            FeedbackPlayer p = Player(s);
            p.m_Preset = linked;
            p.m_Policy = PlayPolicy.Overlap;
            p.m_MaxOverlap = 2;
            // a list, one live variant: an empty slot, a repeat and the linked preset make none
            p.m_Variants.Add(a);
            p.m_Variants.Add(null);
            p.m_Variants.Add(a);
            p.m_Variants.Add(linked);
            Check(p.EnsureRuntime() && s.ReservedCount == 4, "the list and one variant reserve 2 × 2: " + s.ReservedCount);
            int made = PlayerTestBinding.Made.Count;
            Check(made == 2, "a binding for the list and one for the variant: " + made);
            p.m_Variants.Add(b);
            Check(p.Play(Of(b)) == default, "a variant added after the prepare waits for Rebuild");
            Check(p.Rebuild() && s.ReservedCount == 6, "Rebuild makes the new variant: " + s.ReservedCount);
            p.Play(Of(b));
            Check(Began() == "b", "and it plays");
            p.ReleaseRuntime();
            Check(s.ReservedCount == 0, "the release gives every reservation back: " + s.ReservedCount);
            End();
        }

        // ---- T9 — a variant's items take their seeds from the preset

        [Test]
        public void VS09_AVariantsSeedsAreThePresets()
        {
            FeedbackScheduler s = Scheduler();
            FeedbackPreset preset = Preset("seeded", Act("v0", 0.5f), Act("v1", 0.5f));
            for (int i = 0; i < preset.m_Items.Count; i++)
                Assert.That(ManagedReferenceUtility.SetManagedReferenceIdForObject(preset, preset.m_Items[i], 0x1234_5678_9000L + i), Is.True, "the fixed id of item " + i);
            FeedbackPlayer linked = Player(s);
            linked.m_Preset = preset;
            linked.Play(PlayArgs.Default.WithSeed(42u));
            uint viaLink = B("v1").LastBegin.ItemSeed;
            FeedbackPlayer owner = Player(s, Act("own", 0.5f));
            owner.m_Variants.Add(preset);
            owner.Play(Of(preset).WithSeed(42u));
            List<PlayerTestBinding> v1 = PlayerTestBinding.Made.Where(b => b.Item.m_Label == "v1").ToList();
            Check(v1.Count == 2 && v1[1].Context.Player == owner, "the variant has its own binding: " + v1.Count);
            uint viaVariant = v1.Count == 2 ? v1[1].LastBegin.ItemSeed : 0u;
            uint expected = Hash32.Tagged(42u, Hash32.Item, Hash32.Fold(0x1234_5678_9001L));
            Check(viaLink == expected, "the linked player's seed is the id's: " + viaLink + " · " + expected);
            Check(viaVariant == expected, "the variant's seed is the preset's id too: " + viaVariant + " · " + expected);
            End();
        }

        // ---- T10 — the first frame is the list's (Backward: a tween's first frame is its end)

        [Test]
        public void VS10_TheFirstFrameIsTheLists()
        {
            FeedbackScheduler s = Scheduler();
            GameObject t = Made("t");
            FeedbackPreset variant = Preset("variant", To(Direct(t.transform), new Vector3(5f, 0f, 0f), 0.5f));
            FeedbackPlayer p = Player(s, Act("base", 0.5f));
            p.m_ApplyInitialState = true;
            p.m_Direction = PlayerDirection.Backward;
            p.m_Variants.Add(variant);
            p.EnsureRuntime();
            Vector3 withVariant = t.transform.localPosition;
            GameObject u = Made("u");
            FeedbackPlayer control = Player(s, To(Direct(u.transform), new Vector3(5f, 0f, 0f), 0.5f));
            control.m_ApplyInitialState = true;
            control.m_Direction = PlayerDirection.Backward;
            control.EnsureRuntime();
            Check(Near(withVariant, Vector3.zero), "a variant writes no first frame: " + withVariant);
            Check(Near(u.transform.localPosition, new Vector3(5f, 0f, 0f)), "the control — the list's tween writes its end: " + u.transform.localPosition);
            End();
        }

        // ---- T11 — PlayFromEvent(FeedbackPreset)

        [Test]
        public void VS11_PlayFromEventPicksAPreset()
        {
            FeedbackScheduler s = Scheduler();
            FeedbackPreset variant = Preset("variant", Act("variant", 0.5f));
            FeedbackPlayer p = Player(s, Act("base", 0.5f));
            p.m_Variants.Add(variant);
            var call = new UnityEvent();
            UnityEventTools.AddObjectPersistentListener<FeedbackPreset>(call, p.PlayFromEvent, variant);
            call.SetPersistentListenerState(0, UnityEventCallState.EditorAndRuntime);
            call.Invoke();
            Check(Began() == "variant", "the persistent call plays the variant");
            p.PlayFromEvent((FeedbackPreset)null);
            Check(Began() == "base", "null plays the list");
            MethodInfo map = typeof(UnityEditorInternal.UnityEventDrawer).GetMethod("CalculateMethodMap", BindingFlags.NonPublic | BindingFlags.Static);
            string[] listed = ((IEnumerable)map.Invoke(null, new object[] { p, new[] { typeof(Object) }, true })).Cast<object>()
                .Select(o => (MethodInfo)o.GetType().GetField("methodInfo").GetValue(o))
                .Where(m => m.DeclaringType == typeof(FeedbackPlayer))
                .Select(m => m.Name + "(" + string.Join(",", m.GetParameters().Select(x => x.ParameterType.Name)) + ")").ToArray();
            Check(listed.Contains("PlayFromEvent(FeedbackPreset)"), "the inspector lists it for an object argument: " + string.Join(", ", listed));
            End();
        }

        // ---- T12 — the premise of T27 (two players): the Restore order decides the end value

        [Test]
        public void VS12_TwoPlayersRestoreInOrder()
        {
            FeedbackScheduler s = Scheduler();
            GameObject t = Made("t");
            FeedbackPlayer a = Player(s, To(Direct(t.transform), new Vector3(1f, 0f, 0f), 1f));
            FeedbackPlayer b = Player(s, To(Direct(t.transform), new Vector3(0f, 2f, 0f), 1f));
            a.Play();
            s.Tick(1.5f);
            b.Play();
            s.Tick(1.5f);
            a.Restore();
            b.Restore();
            Vector3 aFirst = t.transform.localPosition;
            Check(Near(aFirst, new Vector3(1f, 0f, 0f)), "the first player restored first leaves the later one's start: " + aFirst);
            GameObject u = Made("u");
            FeedbackPlayer c = Player(s, To(Direct(u.transform), new Vector3(1f, 0f, 0f), 1f));
            FeedbackPlayer d = Player(s, To(Direct(u.transform), new Vector3(0f, 2f, 0f), 1f));
            c.Play();
            s.Tick(1.5f);
            d.Play();
            s.Tick(1.5f);
            d.Restore();
            c.Restore();
            Check(Near(u.transform.localPosition, Vector3.zero), "the later player restored first gives the value before both: " + u.transform.localPosition);
            End();
        }

        // ---- T27 — one player restores its lists in the reverse order of their first plays

        Vector3 RestoreRow(bool variantFirst, bool overlap, out string trace)
        {
            FeedbackScheduler s = Scheduler();
            GameObject t = Made("t");
            FeedbackPreset variant = Preset("variant", To(Direct(t.transform), new Vector3(0f, 2f, 0f), 1f));
            FeedbackPlayer p = Player(s, To(Direct(t.transform), new Vector3(1f, 0f, 0f), 1f));
            p.m_Variants.Add(variant);
            if (overlap)
                p.m_Policy = PlayPolicy.Overlap;
            p.Play(variantFirst ? Of(variant) : PlayArgs.Default);
            s.Tick(overlap ? 0.5f : 1.5f);
            p.Play(variantFirst ? PlayArgs.Default : Of(variant));
            s.Tick(1.5f);
            trace = P(t.transform);
            p.Restore();
            return t.transform.localPosition;
        }

        [Test]
        public void VS27_RestoreAndRevertGoBackInOrder()
        {
            foreach (bool variantFirst in new[] { false, true })
            {
                foreach (bool overlap in new[] { false, true })
                {
                    Vector3 restored = RestoreRow(variantFirst, overlap, out string played);
                    Check(Near(restored, Vector3.zero), "Restore — " + (variantFirst ? "variant" : "list") + " first" + (overlap ? " · overlapping" : "") + ": " + played + " → " + restored);
                }
            }
            // Stop(Revert) ends the latest play first: each play's start value goes back, the earliest last
            foreach (bool variantFirst in new[] { false, true })
            {
                FeedbackScheduler s = Scheduler();
                GameObject t = Made("t");
                FeedbackPreset variant = Preset("variant", To(Direct(t.transform), new Vector3(0f, 2f, 0f), 1f));
                FeedbackPlayer p = Player(s, To(Direct(t.transform), new Vector3(1f, 0f, 0f), 1f));
                p.m_Variants.Add(variant);
                p.m_Policy = PlayPolicy.Overlap;
                p.Play(variantFirst ? Of(variant) : PlayArgs.Default);
                s.Tick(0.5f);
                p.Play(variantFirst ? PlayArgs.Default : Of(variant));
                s.Tick(0.25f);
                string mid = P(t.transform);
                p.Stop(StopMode.Revert);
                Check(Near(t.transform.localPosition, Vector3.zero), "Stop(Revert) — " + (variantFirst ? "variant" : "list") + " first: " + mid + " → " + P(t.transform));
            }
            End();
        }

        // ---- T13 — no allocation on the variants' paths

        static FeedbackPlayer s_AllocPlayer;
        static FeedbackScheduler s_AllocScheduler;
        static PlayArgs s_AllocArgs;
        static int s_AllocCount;

        static readonly TestDelegate s_Allocating = () => s_AllocCount += new int[4].Length;

        // a variant's play, its handle's calls, the list's play over it (Restart), the calls without a handle, a tick
        static readonly TestDelegate s_Cycle = () =>
        {
            FeedbackPlayer p = s_AllocPlayer;
            FeedbackScheduler s = s_AllocScheduler;
            PlaybackHandle h = p.Play(s_AllocArgs);
            s.Tick(0.05f);
            s_AllocCount += (int)p.GetDuration(h);
            p.SetHeld(h, true);
            p.SetHeld(h, false);
            p.Play();
            p.Pause();
            p.Resume();
            s_AllocCount += p.IsPlaying ? 1 : 0;
            p.Stop(h);
            p.Stop();
            p.Restore();
            s.Tick(0.05f);
        };

        void CycleFixture()
        {
            FeedbackScheduler s = Scheduler();
            FeedbackPreset a = Preset("a", Act("a", 0.2f));
            FeedbackPreset b = Preset("b", Act("b", 0.2f));
            FeedbackPlayer p = Player(s, Act("base", 0.2f));
            p.m_Variants.Add(a);
            p.m_Variants.Add(b);
            p.EnsureRuntime();
            s_AllocPlayer = p;
            s_AllocScheduler = s;
            s_AllocArgs = PlayArgs.Default.WithPreset(b);
        }

        static bool Allocated(TestDelegate run) => UIs.AllocatingGCMemory().ApplyTo(run).IsSuccess;

        [Test]
        public void VS13_NoAllocationOnTheVariantsPaths()
        {
            PlayerTestBinding.Logging = false;
            int control = 0;
            for (int n = 0; n < 5; n++)
            {
                if (Allocated(s_Allocating))
                    control++;
            }
            CycleFixture();
            s_Cycle();
            int first = 0;
            for (int n = 0; n < 5; n++)
            {
                CycleFixture();
                s_Cycle();
                if (Allocated(s_Cycle))
                    first++;
            }
            PlayerTestBinding.Logging = true;
            Check(control == 5, "the positive control: " + control);
            Check(first == 0, "the cycle allocates after its first run: " + first + "/5");
            End();
        }

        // ---- T28 (the runtime's part) — the preview's masks are the shown list's

        [Test]
        public void VS28_TheMasksAreTheShownLists()
        {
            FeedbackScheduler s = Scheduler(ContextKind.Preview);
            FeedbackPreset variant = Preset("variant", Act("v0", 0.5f), Act("v1", 0.5f));
            FeedbackPlayer p = Player(s, Act("b0", 0.5f), Act("b1", 0.5f));
            p.m_Policy = PlayPolicy.Overlap;
            p.m_Variants.Add(variant);
            // the list shown: its item 0 muted — the variant's item 0 plays
            p.m_PreviewMasks = new byte[] { 1, 0 };
            p.Play();
            string list = Began();
            p.Play(Of(variant));
            string other = Began();
            Check(list == "b1" && other == "v0 v1", "the list's mask stays on the list: '" + list + "' · '" + other + "'");
            // the variant shown
            p.m_PreviewMaskOwner = variant;
            p.Play(Of(variant));
            string shown = Began();
            p.Play();
            string hidden = Began();
            Check(shown == "v1" && hidden == "b0 b1", "the variant shown takes the mask: '" + shown + "' · '" + hidden + "'");
            p.m_PreviewMasks = null;
            End();
        }
    }
}
