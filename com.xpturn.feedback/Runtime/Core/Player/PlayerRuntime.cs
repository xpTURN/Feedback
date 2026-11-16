using System;
using System.Collections.Generic;
using UnityEngine;

namespace xpTURN.Feedback
{
    // what a player makes once, at its initialization — the timeline program, a binding per effect item, N play records and
    // the nominal resolve. Every allocation of a player is here. Released once: the plays end quietly, then the bindings,
    // then the scheduler's reservation. The scheduler's runner — the pipeline, the tick, the ends — is
    // PlayerRuntime.Playback.cs
    internal sealed partial class PlayerRuntime : IPlaybackRunner
    {
        internal const string MissingItemWarning = "A FeedbackPlayer has empty item slots (missing types). They keep their place in the timeline and do nothing.";
        internal const string UnregisteredWarning = "A Feedback item type has no implementation registered and is skipped (is its package installed?): ";
        internal const string SkippedWarning = "A Feedback item was skipped — its target or motion could not be prepared: item ";

        // the default seeds of a session: the hash of a counter that starts at 0 each session. Not a promise — the engine
        // does not promise the order of the prepares; PlayArgs.Seed is
        const uint SessionSeed = 0x5EED5EEDu;

#if DEBUG
        // one warning per type and session — a hundred players with one missing package warn once
        static readonly HashSet<Type> s_WarnedTypes = new HashSet<Type>();
#endif
        static uint s_SeedCounter;

        readonly FeedbackPlayer m_Player;
        // the list this runtime plays and the object whose data holds it — the player's list (its own or the linked preset's)
        // or a variant preset's
        readonly List<FeedbackItem> m_Items;
        readonly UnityEngine.Object m_Owner;
        readonly FeedbackScheduler m_Scheduler;
        // the player's transform, read once here — the engine makes an object's C# transform on the first read
        readonly Transform m_Transform;
        readonly TimelineProgram m_Program;
        readonly int[] m_ListIndex;             // program item → list index (an inactive flow item is not in the program)
        // program item → what its seed hashes: the folded managed reference id the list's owner gave the item, else its list
        // index — an item with an id keeps its random parts when items are added, removed or moved
        readonly uint[] m_SeedKey;
        readonly bool[] m_KeyIsId;
        readonly ItemBinding[] m_Bindings;      // by program item — null = no effect (a flow item or a skipped one)
        readonly EffectItem[] m_Effects;        // by program item — the conditions and the randomness a Play reads; null for a flow item and a null slot
        readonly bool[] m_Skip;                 // by program item — an effect item without a binding keeps its place
        readonly ResolvedTimeline m_Nominal;
        readonly PlaybackRecord[] m_Records;
        // a backward play is confirmed here first (the backward check), and a backward play is evaluated through this
        // sample — the runner evaluates one record at a time
        readonly ResolvedTimeline m_Judge;
        readonly TimelineSample m_Sample;
        // a play's inputs while it is confirmed, and what the accepted plays left — a refused play writes none of them
        readonly float[] m_Scale;
        readonly bool[] m_PlaySkip;
        readonly double[] m_ItemLastPlay;
        readonly int[] m_ItemPlays;
        XorShiftRandom m_Random;
        long m_Serial;
        // the player's stamps of this runtime's first play since its prepare or Restore (0: none) and of its last play — the
        // order the player takes its lists back in
        long m_FirstStamp;
        long m_LastStamp;
        float m_NominalFactor;
        float m_NominalDelay;
        bool m_Released;
        // the player is destroyed and its finite plays go on
        bool m_Detached;
        // each effect item's effective category (copied at the prepare), whether the flash limit counts it (Flash or Flashes)
        // and the context's gate
        readonly FeedbackCategory[] m_Category;
        readonly bool[] m_Flashes;
        readonly CategoryGate m_Gate;
        // a dry scheduler's runtime (the editor's graph): only the bindings that describe values, no event, no admission, the
        // player's own state untouched
        readonly bool m_Dry;
        // the play a dry Replay copies, while it is accepted
        PlaybackRecord m_Replay;
        // made at the first token
        PlaybackTokens m_Tokens;

        // items · owner: the list and its owner. old: Rebuild's runtime of the same list — its snapshots are handed over.
        // initial: the first frame is written at the end. only: the one list index prepared (the editor's graph of an item
        // alone) — the others keep their places
        internal PlayerRuntime(FeedbackPlayer player, List<FeedbackItem> items, UnityEngine.Object owner, FeedbackScheduler scheduler, PlayerRuntime old, bool initial, int only = -1)
        {
            m_Player = player;
            m_Items = items;
            m_Owner = owner;
            m_Scheduler = scheduler;
            m_Dry = scheduler.Context.Layers.Dry;
            m_Transform = player.transform;
            player.TouchAnchors();
            int overlap = player.MaxOverlap;
            var context = new PrepareContext(player.gameObject, player, scheduler.Context, overlap, this, -1);
            var descs = new List<TimelineItemDesc>(items.Count);
            var listIndex = new List<int>(items.Count);
            var bindings = new List<ItemBinding>(items.Count);
            var effects = new List<EffectItem>(items.Count);
            var skip = new List<bool>(items.Count);
            int missing = TimelineConversion.DescribeList(items, descs, listIndex);
            var seedKey = new uint[descs.Count];
            var keyIsId = new bool[descs.Count];
            for (int k = 0; k < descs.Count; k++)
            {
                int i = listIndex[k];
                FeedbackItem item = items[i];
                seedKey[k] = PlayMath.SeedKey(owner, item, i, out keyIsId[k]);
                ItemBinding binding = null;
                bool effect = descs[k].Kind == TimelineItemKind.Effect;
                if (item != null && effect && item.m_Active && (only < 0 || only == i))
                    binding = Prepare((EffectItem)item, i, in context);
                bindings.Add(binding);
                effects.Add(effect ? (EffectItem)item : null);
                skip.Add(effect && binding == null);
            }
#if DEBUG
            if (missing > 0)
                Debug.LogWarning(MissingItemWarning, player);
#endif
            m_Program = TimelineProgram.Compile(descs.ToArray());
            m_ListIndex = listIndex.ToArray();
            m_SeedKey = seedKey;
            m_KeyIsId = keyIsId;
            m_Bindings = bindings.ToArray();
            m_Effects = effects.ToArray();
            m_Skip = skip.ToArray();
            int count = m_ListIndex.Length;
            m_Gate = scheduler.Context.Categories;
            m_Category = new FeedbackCategory[count];
            m_Flashes = new bool[count];
            for (int i = 0; i < count; i++)
            {
                EffectItem e = m_Effects[i];
                if (e == null)
                    continue;
                m_Category[i] = e.EffectiveCategory;
                m_Flashes[i] = m_Category[i] == FeedbackCategory.Flash || e.Flashes;
            }
            m_Scale = new float[count];
            m_PlaySkip = new bool[count];
            m_ItemLastPlay = new double[count];
            m_ItemPlays = new int[count];
            for (int i = 0; i < count; i++)
                m_ItemLastPlay[i] = double.NegativeInfinity;
            m_Nominal = new ResolvedTimeline(m_Program);
            m_Judge = new ResolvedTimeline(m_Program);
            m_Sample = new TimelineSample(m_Program);
            m_Records = new PlaybackRecord[overlap];
            for (int r = 0; r < overlap; r++)
                m_Records[r] = new PlaybackRecord(m_Program);
            scheduler.Reserve(overlap);
            ResolveNominal();
            unchecked
            {
                m_Random = new XorShiftRandom(Hash32.Of(SessionSeed, s_SeedCounter++));
            }
            if (old != null)
                Inherit(old);
            if (initial)
                ApplyInitial();
        }

        internal FeedbackScheduler Scheduler => m_Scheduler;
        internal TimelineProgram Program => m_Program;
        internal ResolvedTimeline Nominal => m_Nominal;
        internal int RecordCount => m_Records.Length;
        internal PlaybackRecord Record(int index) => m_Records[index];
        internal int ItemCount => m_ListIndex.Length;
        internal int ListIndex(int programItem) => m_ListIndex[programItem];
        internal ItemBinding Binding(int programItem) => m_Bindings[programItem];

        // what a program item's seeds hash — the managed reference id or the list index this runtime took when it was made
        internal uint SeedKey(int programItem, out bool isId)
        {
            isId = m_KeyIsId[programItem];
            return m_SeedKey[programItem];
        }

        // a dry runtime takes another's seed keys — the ids the list had when that one was made
        internal void UseSeedKeys(uint[] keys, bool[] isId)
        {
            if (!m_Dry || keys == null || isId == null || keys.Length != m_SeedKey.Length || isId.Length != m_KeyIsId.Length)
                return;
            Array.Copy(keys, m_SeedKey, keys.Length);
            Array.Copy(isId, m_KeyIsId, isId.Length);
        }
        internal ReadOnlySpan<bool> Skip => m_Skip;
        internal bool IsReleased => m_Released;
        internal bool ToggleBackward => m_Player.m_ToggleBackward;
        internal bool IsDetached => m_Detached;
        internal List<FeedbackItem> Items => m_Items;
        internal UnityEngine.Object ItemsOwner => m_Owner;
        internal long FirstStamp => m_FirstStamp;
        internal long LastStamp => m_LastStamp;
        // the newest play's number — a stop of the plays that run now leaves the ones started after it
        internal long LastSerial => m_Serial;

        // a record's place on the nominal axis — the cursor's forward, the head's backward
        internal long Position(int record) => m_Records[record].Reverse ? m_Records[record].Sampler.ReversePosition : m_Records[record].Cursor.Position;

        // the tests' way to a known random
        internal void Reseed(uint seed) => m_Random = new XorShiftRandom(seed);

        // an item's failure is its own: the item keeps its place and has no effect, the others prepare
        ItemBinding Prepare(EffectItem effect, int listIndex, in PrepareContext context)
        {
            ItemBinding binding = null;
            try
            {
                binding = FeedbackImplementations.Create(effect);
                if (binding == null)
                {
                    if (!m_Dry)
                        WarnUnregistered(effect.GetType(), m_Player);
                    return null;
                }
                // a dry runtime's other items keep their places — prepared, they would play sounds, raise events, spawn
                if (m_Dry && !DescribesValues(binding))
                    return null;
                PrepareContext own = context.ForItem(listIndex);
                if (binding.Prepare(in own))
                    return binding;
#if DEBUG
                if (!m_Dry)
                    Debug.LogWarning(SkippedWarning + listIndex + " (" + effect.GetType().Name + ")" + (binding.SkipReason != null ? ": " + binding.SkipReason : ""), m_Player);
#endif
            }
            catch (Exception e)
            {
                m_Scheduler.Context.LogException(e);
            }
            // a binding that refused or threw gives back what it got
            Release(binding);
            return null;
        }

        static bool DescribesValues(ItemBinding binding)
        {
            var channels = new List<GraphChannel>();
            binding.Describe(channels);
            return channels.Count > 0;
        }

        // the first player that meets the type is the context — a click on the log finds one user of it. MotionDriver shares
        // the once per type and session
        internal static void WarnUnregistered(Type type, UnityEngine.Object player)
        {
#if DEBUG
            if (s_WarnedTypes.Add(type))
                Debug.LogWarning(UnregisteredWarning + type.Name, player);
#endif
        }

        // MotionDriver draws its seeds from the session's counter too
        internal static uint NextSessionSeed()
        {
            unchecked
            {
                return Hash32.Of(SessionSeed, s_SeedCounter++);
            }
        }

        // the nominal play: no random part, every clock at ratio 1. The start delay takes no multiplier
        internal void ResolveNominal()
        {
            m_NominalFactor = Factor(m_Player.m_DurationMultiplier);
            m_NominalDelay = Offset(m_Player.m_StartDelay);
            m_Program.Resolve(default, m_Skip, m_NominalFactor, m_NominalDelay, m_Nominal);
        }

        // resolved again when the multiplier or the start delay changed since
        internal float TotalDuration
        {
            get
            {
                if (Factor(m_Player.m_DurationMultiplier) != m_NominalFactor || Offset(m_Player.m_StartDelay) != m_NominalDelay)
                    ResolveNominal();
                return (float)m_Nominal.TotalSeconds;
            }
        }

        // a driven play can seek the list — the nominal resolve's (a play's skipped items keep their place: a driven play's
        // resolve is the same), resolved again when the multiplier or the start delay changed since
        internal bool Drivable
        {
            get
            {
                if (Factor(m_Player.m_DurationMultiplier) != m_NominalFactor || Offset(m_Player.m_StartDelay) != m_NominalDelay)
                    ResolveNominal();
                return m_Nominal.IsSeekable;
            }
        }

        // each binding of an item the old runtime had too takes that binding's snapshots — the binding checks the property —
        // and the stamp of the first play they were taken at
        internal void Inherit(PlayerRuntime old)
        {
            m_FirstStamp = old.m_FirstStamp;
            for (int i = 0; i < m_Bindings.Length; i++)
            {
                ItemBinding binding = m_Bindings[i];
                if (binding == null)
                    continue;
                for (int j = 0; j < old.m_Bindings.Length; j++)
                {
                    if (!ReferenceEquals(old.m_Effects[j], m_Effects[i]) || old.m_Bindings[j] == null)
                        continue;
                    try
                    {
                        binding.Inherit(old.m_Bindings[j]);
                    }
                    catch (Exception e)
                    {
                        m_Scheduler.Context.LogException(e);
                    }
                    break;
                }
            }
        }

        // the first frame of the set direction, no play — Backward writes each item's end (an endless one none) in list
        // order: the later item wins, the one a backward play reaches first. Forward and Toggle (its first play) write t = 0
        // in reverse list order: the earlier item wins. An item the direction leaves out writes nothing. The intensity is a
        // Play without an argument's — the remap of 1 × the base intensity; no falloff, no random output
        void ApplyInitial()
        {
            FeedbackPlayer player = m_Player;
            bool backward = player.m_Direction == PlayerDirection.Backward;
            float intensity = PlayMath.PlayIntensity(player, 1f, 1f);
            int count = m_Bindings.Length;
            for (int k = 0; k < count; k++)
            {
                int i = backward ? k : count - 1 - k;
                ItemBinding binding = m_Bindings[i];
                EffectItem effect = m_Effects[i];
                if (binding == null || effect == null)
                    continue;
                DirectionCondition condition = effect.m_Conditions.m_Direction;
                if (backward ? condition == DirectionCondition.ForwardOnly : condition == DirectionCondition.ReverseOnly)
                    continue;
                MotionTime time = default;
                // a turned item's first frame is its other end
                if (backward != Turns(i, backward, m_Nominal.ItemDuration(i)))
                {
                    long natural = m_Nominal.ItemDuration(i);
                    if (natural == TimelineTime.Infinite)
                        continue;
                    time = new MotionTime(1f, TimelineTime.ToSeconds(natural));
                }
                try
                {
                    binding.ApplyInitial(in time, effect.m_Intensity == IntensityResponse.Ignore ? 1f : intensity);
                }
                catch (Exception e)
                {
                    m_Scheduler.Context.LogException(e);
                }
            }
        }

        // a multiplier: finite and not negative, else 1
        internal static float Factor(float value) => value >= 0f && value < float.PositiveInfinity ? value : 1f;

        // a start delay: finite and positive, else 0
        internal static float Offset(float seconds) => seconds > 0f && seconds < float.PositiveInfinity ? seconds : 0f;

        // ---- the release

        // once: the plays end quietly — Hold, no event: the player is going (the end Destroyed is this way) — then the
        // bindings, then the reservation
        internal void Release(PlaybackEnd end)
        {
            if (m_Released)
                return;
            m_Released = true;
            for (int r = 0; r < m_Records.Length; r++)
            {
                if (m_Records[r].Active)
                    End(m_Records[r], r, EndReason.Hold, end, false);
            }
            for (int i = 0; i < m_Bindings.Length; i++)
                Release(m_Bindings[i]);
            m_Scheduler.Unreserve(m_Records.Length);
            m_Tokens?.Clear();
        }

        // the player is destroyed with Continue — its finite plays go on without it. The ones nothing can finish (not
        // seekable: endless, an open wait, saturated) and the paused · held ones end now, quietly (Hold, no event — their listeners
        // hear Destroyed). true = a play goes on: the last one's end releases
        internal bool Detach()
        {
            m_Detached = true;
            for (int r = 0; r < m_Records.Length; r++)
            {
                PlaybackRecord record = m_Records[r];
                if (record.Active && (!record.Resolved.IsSeekable || record.Paused || record.Held))
                    End(record, r, EndReason.Hold, PlaybackEnd.Destroyed, false);
            }
            if (!m_Released && ActiveCount() == 0)
                Release(PlaybackEnd.Destroyed);
            return !m_Released;
        }

        // a detached play with no live item left in it ends quietly at its next tick — a scene unloaded under it
        bool AnyAlive(PlaybackRecord record)
        {
            ItemBinding[] bindings = m_Bindings;
            for (int i = 0; i < bindings.Length; i++)
            {
                if (bindings[i] != null && !record.Resolved.ItemSkipped(i) && bindings[i].IsAlive)
                    return true;
            }
            return false;
        }

        // a detached runtime goes with its last play
        void ReleaseIfDone()
        {
            if (m_Detached && !m_Released && ActiveCount() == 0)
                Release(PlaybackEnd.Destroyed);
        }

        // ---- the token store

        internal PlaybackTokens Tokens => m_Tokens;

        int AcquireToken(System.Threading.CancellationToken token)
        {
            if (!token.CanBeCanceled)
                return -1;
            if (m_Tokens == null)
                m_Tokens = new PlaybackTokens(this, m_Records.Length);
            return m_Tokens.Acquire(token);
        }

        internal bool IsWaitedOn(int entry)
        {
            PlaybackRecord[] records = m_Records;
            for (int r = 0; r < records.Length; r++)
            {
                if (records[r].Active && records[r].Token == entry)
                    return true;
            }
            return false;
        }

        // the token of entry was canceled (main thread): the plays that wait on it stop — Hold, Stopped. One a Stopped
        // subscriber starts is not among them
        internal void StopWaiting(int entry)
        {
            long last = m_Serial;
            for (int r = 0; r < m_Records.Length; r++)
            {
                PlaybackRecord record = m_Records[r];
                if (record.Active && record.Serial <= last && record.Token == entry)
                    End(record, r, EndReason.Hold, PlaybackEnd.Stopped, true);
            }
        }

        void Release(ItemBinding binding)
        {
            if (binding == null)
                return;
            try
            {
                binding.Release();
            }
            catch (Exception e)
            {
                m_Scheduler.Context.LogException(e);
            }
        }

        // domain reload off: the static cells outlive the session — the seed counter and the warned types
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        internal static void ResetSession()
        {
            s_SeedCounter = 0;
#if DEBUG
            s_WarnedTypes.Clear();
#endif
        }
    }
}
