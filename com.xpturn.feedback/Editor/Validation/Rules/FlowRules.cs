using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace xpTURN.Feedback.Editor
{
    // group O — the flow items. A waiting player item over a backward player is FB-C02's (an open item makes the list unable
    // to play backward)

    static class FlowRuleWalk
    {
        // the players an item waits on, with the relative path of each reference
        internal static void Waits(FeedbackItem item, List<(FeedbackPlayer Player, string Path)> into)
        {
            switch (item)
            {
                case PlayPlayerItem play when play.m_Play.m_Wait:
                    into.Add((play.m_Player, "m_Player"));
                    break;
                case RandomPlayerItem random when random.m_Play.m_Wait:
                    for (int k = 0; k < random.m_Entries.Count; k++)
                        into.Add((random.m_Entries[k]?.m_Player, "m_Entries.Array.data[" + k + "].m_Player"));
                    break;
                case PlayerChainItem chain:
                    for (int k = 0; k < chain.m_Entries.Count; k++)
                    {
                        ChainEntry e = chain.m_Entries[k];
                        if (e != null && e.m_Active && e.m_Wait)
                            into.Add((e.m_Player, "m_Entries.Array.data[" + k + "].m_Player"));
                    }
                    break;
            }
        }

        internal static bool Plays(FeedbackItem item) => item is PlayPlayerItem || item is RandomPlayerItem || item is PlayerChainItem;
    }

    // FB-O01: a play item waits on its own player — it never ends; the play refuses it (a waiting cycle)
    internal sealed class FlowSelfWaitRule : FeedbackRule
    {
        internal override string Id => "FB-O01";
        internal override RuleSeverity Severity => RuleSeverity.Error;

        internal override void Check(RuleContext c, List<RuleResult> into)
        {
            var waits = new List<(FeedbackPlayer Player, string Path)>();
            for (int i = 0; i < c.Items.Count; i++)
            {
                FeedbackItem item = c.Items[i];
                if (item == null || !item.m_Active)
                    continue;
                waits.Clear();
                FlowRuleWalk.Waits(item, waits);
                foreach ((FeedbackPlayer player, string path) in waits)
                {
                    if (player == null || player != c.Player)
                        continue;
                    RuleResult r = Result(c, i, "It waits on its own player — it would never end; the play refuses it");
                    r.Fixes.Add(c.ItemFix("Clear", c.ItemPath(i, path), p => p.objectReferenceValue = null));
                    into.Add(r);
                    break;
                }
            }
        }
    }

    // FB-O02: the scene's references make a waiting cycle (this player waits on one that waits back) — the play refuses the
    // wait that closes it. References an anchor or a channel makes at play are not seen
    internal sealed class FlowWaitCycleRule : FeedbackRule
    {
        internal override string Id => "FB-O02";
        internal override RuleSeverity Severity => RuleSeverity.Warning;

        internal override void Check(RuleContext c, List<RuleResult> into)
        {
            if (c.Player == null)
                return;
            var waits = new List<(FeedbackPlayer Player, string Path)>();
            for (int i = 0; i < c.Items.Count; i++)
            {
                FeedbackItem item = c.Items[i];
                if (item == null || !item.m_Active)
                    continue;
                waits.Clear();
                FlowRuleWalk.Waits(item, waits);
                foreach ((FeedbackPlayer player, string _) in waits)
                {
                    if (player == null || player == c.Player || !Reaches(player, c.Player))
                        continue;
                    into.Add(Result(c, i, "A waiting cycle — " + player.name + " waits back on this player; the play refuses the wait that closes it"));
                    break;
                }
            }
        }

        // a breadth walk of the waits from `from` — true when it comes back to `to`
        static bool Reaches(FeedbackPlayer from, FeedbackPlayer to)
        {
            var seen = new HashSet<FeedbackPlayer> { from };
            var queue = new Queue<FeedbackPlayer>();
            queue.Enqueue(from);
            var waits = new List<(FeedbackPlayer Player, string Path)>();
            while (queue.Count > 0)
            {
                FeedbackPlayer p = queue.Dequeue();
                List<FeedbackItem> items = p.Items;
                if (items == null)
                    continue;
                foreach (FeedbackItem item in items)
                {
                    if (item == null || !item.m_Active)
                        continue;
                    waits.Clear();
                    FlowRuleWalk.Waits(item, waits);
                    foreach ((FeedbackPlayer next, string _) in waits)
                    {
                        if (next == null)
                            continue;
                        if (next == to)
                            return true;
                        if (seen.Add(next))
                            queue.Enqueue(next);
                    }
                }
            }
            return false;
        }
    }

    // FB-O03: a flow item with nothing to play — no player, an empty list, an entry without its player (or its event's listeners)
    internal sealed class FlowEmptyRule : FeedbackRule
    {
        internal override string Id => "FB-O03";
        internal override RuleSeverity Severity => RuleSeverity.Warning;

        internal override void Check(RuleContext c, List<RuleResult> into)
        {
            for (int i = 0; i < c.Items.Count; i++)
            {
                FeedbackItem item = c.Items[i];
                if (item == null || !item.m_Active)
                    continue;
                int count;
                int empty;
                switch (item)
                {
                    case PlayPlayerItem play:
                        if (play.m_Player == null)
                            into.Add(Result(c, i, "No player to play"));
                        continue;
                    case RandomPlayerItem random:
                        count = random.m_Entries.Count;
                        empty = random.m_Entries.FindAll(e => e == null || e.m_Player == null).Count;
                        break;
                    case PlayerChainItem chain:
                        count = chain.m_Entries.Count;
                        empty = chain.m_Entries.FindAll(e => e == null || e.m_Player == null).Count;
                        break;
                    case PlayerControlItem control:
                        count = control.m_Players.Count;
                        empty = control.m_Players.FindAll(p => p == null).Count;
                        break;
                    case RandomUnityEventItem randomEvents:
                        count = randomEvents.m_Entries.Count;
                        empty = randomEvents.m_Entries.FindAll(e => e == null || e.m_Event == null || e.m_Event.GetPersistentEventCount() == 0).Count;
                        break;
                    default:
                        continue;
                }
                if (count == 0)
                {
                    into.Add(Result(c, i, "The list is empty — nothing to play"));
                    continue;
                }
                if (empty == 0)
                    continue;
                RuleResult r = Result(c, i, empty + " of " + count + " entries are empty — they are skipped");
                bool players = item is PlayerControlItem;
                bool events = item is RandomUnityEventItem;
                string list = players ? "m_Players" : "m_Entries";
                r.Fixes.Add(c.ItemFix("Remove empty entries", c.ItemPath(i, list), p =>
                {
                    for (int k = p.arraySize - 1; k >= 0; k--)
                    {
                        SerializedProperty e = p.GetArrayElementAtIndex(k);
                        bool gone = players ? e.objectReferenceValue == null
                            : events ? e.FindPropertyRelative("m_Event.m_PersistentCalls.m_Calls").arraySize == 0
                            : e.FindPropertyRelative("m_Player").objectReferenceValue == null;
                        if (gone)
                            p.DeleteArrayElementAtIndex(k);
                    }
                }));
                into.Add(r);
            }
        }
    }

    // FB-O04: a random item none of whose entries has a positive weight — it picks nothing
    internal sealed class FlowWeightRule : FeedbackRule
    {
        internal override string Id => "FB-O04";
        internal override RuleSeverity Severity => RuleSeverity.Warning;

        static bool Counts(float w) => w > 0f && w < float.PositiveInfinity;

        internal override void Check(RuleContext c, List<RuleResult> into)
        {
            for (int i = 0; i < c.Items.Count; i++)
            {
                FeedbackItem item = c.Items[i];
                if (item == null || !item.m_Active)
                    continue;
                bool any;
                if (item is RandomPlayerItem random && random.m_Entries.Count > 0)
                    any = random.m_Entries.Exists(e => e != null && e.m_Player != null && Counts(e.m_Weight));
                else if (item is RandomUnityEventItem events && events.m_Entries.Count > 0)
                    any = events.m_Entries.Exists(e => e != null && Counts(e.m_Weight));
                else
                    continue;
                if (!any)
                    into.Add(Result(c, i, "No entry has a positive weight — it picks nothing"));
            }
        }
    }

    // FB-O05: no Feedback Signal Receiver in the open scenes takes the channel — a code receiver is not seen
    internal sealed class FlowSignalReceiverRule : FeedbackRule
    {
        static readonly List<GameObject> s_Roots = new List<GameObject>();
        static readonly List<FeedbackSignalReceiver> s_Found = new List<FeedbackSignalReceiver>();

        internal override string Id => "FB-O05";
        internal override RuleSeverity Severity => RuleSeverity.Info;

        internal override void Check(RuleContext c, List<RuleResult> into)
        {
            for (int i = 0; i < c.Items.Count; i++)
            {
                if (!(c.Items[i] is SignalBroadcastItem signal) || !signal.m_Active || Received(in signal.m_Channel))
                    continue;
                into.Add(Result(c, i, "No Feedback Signal Receiver in the open scenes takes this channel (a receiver in code is not seen)"));
            }
        }

        static bool Received(in ChannelRef channel)
        {
            for (int s = 0; s < SceneManager.sceneCount; s++)
            {
                Scene scene = SceneManager.GetSceneAt(s);
                if (!scene.isLoaded)
                    continue;
                scene.GetRootGameObjects(s_Roots);
                foreach (GameObject root in s_Roots)
                {
                    root.GetComponentsInChildren(true, s_Found);
                    foreach (FeedbackSignalReceiver r in s_Found)
                    {
                        if (r.m_Channel.Matches(in channel))
                        {
                            s_Found.Clear();
                            s_Roots.Clear();
                            return true;
                        }
                    }
                }
            }
            s_Found.Clear();
            s_Roots.Clear();
            return false;
        }
    }

    // FB-O06: a Debug Break — it pauses the editor's play mode
    internal sealed class FlowDebugBreakRule : FeedbackRule
    {
        internal override string Id => "FB-O06";
        internal override RuleSeverity Severity => RuleSeverity.Info;
        internal override bool Applies(Component host) => OnItems(host);

        internal override void Check(RuleContext c, List<RuleResult> into)
        {
            for (int i = 0; i < c.Items.Count; i++)
            {
                if (!(c.Items[i] is DebugItem d) || !d.m_Active || d.m_Action != DebugAction.Break)
                    continue;
                RuleResult r = Result(c, i, "Debug Break pauses the editor's play mode");
                r.Fixes.Add(c.ItemFix("Use Log", c.ItemPath(i, "m_Action"), p => p.enumValueIndex = (int)DebugAction.Log));
                into.Add(r);
            }
        }
    }

    // FB-O07: a waiting item in the background — the list's end ends it, it does not wait
    internal sealed class FlowBackgroundWaitRule : FeedbackRule
    {
        internal override string Id => "FB-O07";
        internal override RuleSeverity Severity => RuleSeverity.Warning;

        internal override void Check(RuleContext c, List<RuleResult> into)
        {
            for (int i = 0; i < c.Items.Count; i++)
            {
                if (!(c.Items[i] is ActionItem a) || !a.m_Active || !a.Opens || !a.m_Timing.m_Background)
                    continue;
                RuleResult r = Result(c, i, "A background wait does not wait — the list's end ends it");
                r.Fixes.Add(c.ItemFix("Not in the background", c.ItemPath(i, "m_Timing.m_Background"), p => p.boolValue = false));
                into.Add(r);
            }
        }
    }

    // FB-O08: a player reference in a preset — an asset cannot hold a scene object (lost on save) and a prefab asset never plays
    internal sealed class FlowPresetPlayerRule : FeedbackRule
    {
        internal override string Id => "FB-O08";
        internal override RuleSeverity Severity => RuleSeverity.Error;
        internal override bool Applies(Component host) => false;
        internal override bool ListOnly => true;

        internal override void Check(RuleContext c, List<RuleResult> into)
        {
            if (!(c.Owner is FeedbackPreset))
                return;
            var one = new List<FeedbackItem>(1);
            var found = new List<FeedbackPlayer>();
            for (int i = 0; i < c.Items.Count; i++)
            {
                if (!(c.Items[i] is ActionItem item))
                    continue;
                one.Clear();
                one.Add(item);
                found.Clear();
                FlowPlayers.Collect(one, found);
                if (found.Count == 0)
                    continue;
                RuleResult r = Result(c, i, "A preset cannot hold a player — clear it, or play the player from the scene's own list");
                r.Fixes.Add(c.ItemFix("Clear", c.ItemPath(i), Clear));
                into.Add(r);
            }
        }

        // every player reference of the item
        static void Clear(SerializedProperty item)
        {
            SerializedProperty one = item.FindPropertyRelative("m_Player");
            if (one != null)
                one.objectReferenceValue = null;
            SerializedProperty players = item.FindPropertyRelative("m_Players");
            if (players != null)
            {
                for (int k = 0; k < players.arraySize; k++)
                    players.GetArrayElementAtIndex(k).objectReferenceValue = null;
            }
            SerializedProperty entries = item.FindPropertyRelative("m_Entries");
            if (entries == null)
                return;
            for (int k = 0; k < entries.arraySize; k++)
            {
                SerializedProperty p = entries.GetArrayElementAtIndex(k).FindPropertyRelative("m_Player");
                if (p != null)
                    p.objectReferenceValue = null;
            }
        }
    }
}
