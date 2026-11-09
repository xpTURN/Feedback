using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace xpTURN.Feedback.Editor
{
    internal enum RunnerKind : byte
    {
        Player,
        Driver,
        Other,
    }

    // one playback of the scheduler's query — a player's from its record, a driver's from its observation
    internal struct PlayRow
    {
        internal PlaybackHandle Handle;
        internal Object Owner;
        internal RunnerKind Kind;
        internal int Slot;
        internal int State;
        internal long Position;             // microseconds on the nominal axis
        internal double Total;              // this play's resolved length (+∞ when endless)
        internal double Nominal;            // the player's nominal length as last resolved
        internal bool Reverse;
        internal bool Paused;
        internal bool OffNominal;
        internal uint Seed;
        internal float Intensity;
        internal long Serial;
        internal int OpenWaits;
        internal int UntimedWaits;
        internal bool HasListener;
        internal int Token;
        internal double DriverSeconds;
        // the play that waits on this one (its listener is a flow item's wait link) — its player and play
        internal Object Parent;
        internal long ParentSerial;
        // the owner's group (first met order) · the record's start on the context's clock (NaN for a runner that is not a player)
        internal int Group;
        internal double StartedAt;
    }

    // keys by reference — the engine's Equals takes two destroyed objects for one
    internal sealed class ReferenceKeys<T> : IEqualityComparer<T> where T : class
    {
        internal static readonly ReferenceKeys<T> Instance = new ReferenceKeys<T>();
        public bool Equals(T a, T b) => ReferenceEquals(a, b);
        public int GetHashCode(T o) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(o);
    }

    // one program item of the selected play
    internal struct ItemRow
    {
        internal int ListIndex;
        internal FeedbackItem Item;
        internal bool Running;
        internal bool Touched;
        internal bool Turned;
        internal float Output;
        internal string Note;
    }

    // one entry of the selected player's token store
    internal struct TokenRow
    {
        internal int Index;
        internal bool Used;
        internal bool Canceled;
        internal bool Requested;
        internal long LastUse;
        internal bool Waited;
    }

    // the plays of one owner, in the order the owner is first met
    internal struct GroupRow
    {
        internal Object Owner;
        internal int Count;
        internal int Missed;
        internal int MissedQuiet;
    }

    // one read of a scheduler into rows kept across reads (no allocation once their capacity holds). Read only — the services
    // through the made-only accessors, never a public read that makes a service or prepares a player (the IL test keeps it
    // so). The selection is (handle, owner): the handle finds the play across compactions, the owner keeps its player's
    // tokens and missed signals after the play ends
    internal sealed class DebuggerReader
    {
        internal const string Changed = "(changed)";

        internal readonly List<PlayRow> Plays = new List<PlayRow>(64);
        internal readonly List<GroupRow> Groups = new List<GroupRow>(32);
        internal readonly List<ItemRow> Items = new List<ItemRow>(32);
        internal readonly List<TokenRow> Tokens = new List<TokenRow>(8);
        internal readonly List<LayerSlotInfo> Slots = new List<LayerSlotInfo>(64);
        // the layer rows' sort keys: the target and the slot type in first-met order
        internal readonly List<int> TargetOrder = new List<int>(64);
        internal readonly List<int> SlotOrder = new List<int>(64);
        readonly Dictionary<Object, int> m_GroupOf = new Dictionary<Object, int>(64, ReferenceKeys<Object>.Instance);
        readonly Dictionary<Object, int> m_TargetOf = new Dictionary<Object, int>(64, ReferenceKeys<Object>.Instance);
        readonly Dictionary<System.Type, int> m_TypeOf = new Dictionary<System.Type, int>(16);
        internal readonly List<ChannelRow> Channels = new List<ChannelRow>(8);
        // the pools' rows
        internal readonly List<PoolRow> PoolRows = new List<PoolRow>(8);
        // each extension made — its name and rows (no type named)
        internal readonly List<string> ExtensionNames = new List<string>(4);
        internal readonly List<List<ServiceRow>> ExtensionRows = new List<List<ServiceRow>>(4);

        internal FeedbackScheduler Source;
        internal CategoryGate Gate;
        internal TimeScaleService TimeScale;
        internal ChannelService ChannelService;
        internal PoolService Pools;
        internal double Now;
        internal PlaybackHandle Selected;
        internal Object SelectedOwner;
        internal bool SelectedEnded;

        internal void Select(int row)
        {
            Selected = Plays[row].Handle;
            SelectedOwner = Plays[row].Owner;
            SelectedEnded = false;
        }

        internal void ClearSelection()
        {
            Selected = default;
            SelectedOwner = null;
            SelectedEnded = false;
        }

        internal void Read(FeedbackScheduler scheduler)
        {
            if (!ReferenceEquals(scheduler, Source))
                ClearSelection();
            Source = scheduler;
            Plays.Clear();
            Groups.Clear();
            Items.Clear();
            Tokens.Clear();
            Slots.Clear();
            TargetOrder.Clear();
            SlotOrder.Clear();
            Channels.Clear();
            PoolRows.Clear();
            Gate = null;
            TimeScale = null;
            ChannelService = null;
            Pools = null;
            Now = 0;
            if (scheduler == null)
                return;
            bool found = false;
            int count = scheduler.SlotCount;
            for (int i = 0; i < count; i++)
            {
                if (!scheduler.TryGetActive(i, out ActivePlayback a))
                    continue;
                var row = new PlayRow { Handle = a.Handle, Owner = a.DebugOwner, Slot = i, State = a.State, Token = -1, StartedAt = double.NaN };
                bool selected = Selected != default && a.Handle == Selected;
                if (a.Runner is PlayerRuntime runtime)
                {
                    row.Kind = RunnerKind.Player;
                    PlaybackRecord r = runtime.Record(a.State);
                    row.Position = runtime.Position(a.State);
                    row.Total = r.Resolved.TotalSeconds;
                    row.Nominal = runtime.Nominal.TotalSeconds;
                    row.Reverse = r.Reverse;
                    row.Paused = r.Paused;
                    row.OffNominal = !r.Reverse && r.Cursor.IsOffNominal;
                    row.Seed = r.Seed;
                    row.Intensity = r.Intensity;
                    row.Serial = r.Serial;
                    row.StartedAt = r.StartedAt;
                    row.OpenWaits = r.Reverse ? 0 : r.Cursor.OpenSignalWaits(out row.UntimedWaits);
                    row.HasListener = r.Listener != null;
                    if (r.Listener is IWaitLink link && link.Runtime != null && !link.Runtime.IsReleased && link.Runtime.Serial(link.Record) == link.ChainSerial)
                    {
                        row.Parent = link.Runtime.DebugOwner;
                        row.ParentSerial = link.ChainSerial;
                    }
                    row.Token = r.Token;
                    if (selected)
                        ReadItems(runtime, r, a.State, a.DebugOwner as FeedbackPlayer);
                }
                else if (a.Runner is MotionDriver driver)
                {
                    row.Kind = RunnerKind.Driver;
                    row.Seed = driver.Seed;
                    row.DriverSeconds = driver.Seconds;
                }
                else
                {
                    row.Kind = RunnerKind.Other;
                }
                found |= selected;
                Plays.Add(row);
            }
            SelectedEnded = Selected != default && !found;
            // a destroyed owner is let go (the engine's null)
            if (SelectedOwner == null)
                SelectedOwner = null;
            if (SelectedOwner is FeedbackPlayer owner && owner.Runtime != null)
                ReadTokens(owner.Runtime);
            Group();
            m_TargetOf.Clear();
            m_TypeOf.Clear();
            PropertyLayerRegistry layers = scheduler.Context.Layers;
            int slots = layers.SlotCount;
            for (int i = 0; i < slots; i++)
            {
                if (layers.TryGetSlot(i, out LayerSlotInfo info))
                {
                    Slots.Add(info);
                    Object target = info.Target;
                    int t = -1;
                    if (!ReferenceEquals(target, null) && !m_TargetOf.TryGetValue(target, out t))
                        m_TargetOf.Add(target, t = m_TargetOf.Count);
                    TargetOrder.Add(t);
                    int k = -1;
                    if (info.SlotType != null && !m_TypeOf.TryGetValue(info.SlotType, out k))
                        m_TypeOf.Add(info.SlotType, k = m_TypeOf.Count);
                    SlotOrder.Add(k);
                }
            }
            FeedbackContext context = scheduler.Context;
            Now = context.Now;
            Gate = context.CategoriesIfMade;
            TimeScale = context.TimeScaleIfMade;
            ChannelService = context.ChannelsIfMade;
            ChannelService?.Describe(Channels);
            Pools = context.PoolsIfMade;
            Pools?.Describe(PoolRows);
            ExtensionNames.Clear();
            int e = 0;
            for (int i = 0; i < context.ServiceCount; i++)
            {
                FeedbackService service = context.ServiceAt(i);
                if (service.IsClosed || service.DebugName == null)
                    continue;
                if (ExtensionRows.Count <= e)
                    ExtensionRows.Add(new List<ServiceRow>(8));
                ExtensionNames.Add(service.DebugName);
                service.Describe(ExtensionRows[e]);
                e++;
            }
        }

        // the item's type — '(changed)' when the program's list number is past the player's list now (a link changed before
        // the next Rebuild)
        internal string ItemName(int row)
        {
            FeedbackItem item = Items[row].Item;
            return item != null ? item.GetType().Name : Changed;
        }

        // a shader property's name where the engine keeps the table (6000.6), else the number
        internal static string AuxName(int aux)
        {
#if UNITY_6000_6_OR_NEWER
            string name = Shader.PropertyIDToName(aux);
            if (name != null)
                return name;
#endif
            return aux.ToString();
        }

        // one pass: each play's owner to its group through a by-reference map
        void Group()
        {
            m_GroupOf.Clear();
            for (int i = 0; i < Plays.Count; i++)
            {
                PlayRow row = Plays[i];
                Object owner = row.Owner;
                int g;
                if (ReferenceEquals(owner, null))
                    g = -1;
                else if (m_GroupOf.TryGetValue(owner, out g))
                {
                    GroupRow r = Groups[g];
                    r.Count++;
                    Groups[g] = r;
                }
                else
                {
                    g = Groups.Count;
                    m_GroupOf.Add(owner, g);
                    var player = owner as FeedbackPlayer;
                    Groups.Add(new GroupRow
                    {
                        Owner = owner,
                        Count = 1,
                        Missed = player != null ? player.MissedSignals : 0,
                        MissedQuiet = player != null ? player.MissedQuietSignals : 0,
                    });
                }
                row.Group = g;
                Plays[i] = row;
            }
        }

        void ReadItems(PlayerRuntime runtime, PlaybackRecord record, int state, FeedbackPlayer player)
        {
            List<FeedbackItem> items = player != null ? player.Items : null;
            int n = runtime.ItemCount;
            for (int i = 0; i < n; i++)
            {
                int list = runtime.ListIndex(i);
                FeedbackItem item = items != null && list >= 0 && list < items.Count ? items[list] : null;
                Items.Add(new ItemRow { ListIndex = list, Item = item, Running = record.Running[i], Touched = record.Touched[i], Turned = record.Turned[i], Output = record.Output[i], Note = runtime.Binding(i)?.DebugNote(state) });
            }
        }

        void ReadTokens(PlayerRuntime runtime)
        {
            PlaybackTokens tokens = runtime.Tokens;
            if (tokens == null)
                return;
            for (int i = 0; i < tokens.Size; i++)
            {
                PlaybackTokens.Entry e = tokens.At(i);
                if (e == null)
                    continue;
                Tokens.Add(new TokenRow
                {
                    Index = i,
                    Used = e.Used,
                    Canceled = e.Canceled,
                    Requested = e.Used && e.Token.IsCancellationRequested,
                    LastUse = e.LastUse,
                    Waited = runtime.IsWaitedOn(i),
                });
            }
        }
    }
}
