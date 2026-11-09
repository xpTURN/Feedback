using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace xpTURN.Feedback.Editor
{
    // components · GameObjects dropped on the list — the leaves whose Requires the object has (a component: its type's · a
    // GameObject: SetActive's and its components'), the item Direct to the object. An asset or another scene's object is
    // refused
    internal static class ComponentDrop
    {
        internal const string UndoName = "Add Feedback";

        // the dropped objects a drop may add — an object of the host's scene (TargetChecks)
        internal static List<Object> Accepted(Component host, IReadOnlyList<Object> dropped)
        {
            var accepted = new List<Object>();
            foreach (Object o in dropped)
            {
                if ((o is GameObject || o is Component) && TargetChecks.Check(host, o) == TargetRefusal.None && !accepted.Contains(o))
                    accepted.Add(o);
            }
            return accepted;
        }

        internal static bool Fits(Type requires, Object dropped)
        {
            if (requires == null)
                return false;
            if (requires == typeof(GameObject))
                return dropped is GameObject;
            if (dropped is GameObject go)
                return (typeof(Component).IsAssignableFrom(requires) || requires.IsInterface) && go.GetComponent(requires) != null;
            return requires.IsInstanceOfType(dropped);
        }

        internal static List<CatalogLeaf> Suggest(IReadOnlyList<CatalogLeaf> leaves, Object dropped)
            => Suggest(leaves, new[] { dropped });

        // the leaves any of the objects fits, in the leaves' order
        internal static List<CatalogLeaf> Suggest(IReadOnlyList<CatalogLeaf> leaves, IReadOnlyList<Object> dropped)
        {
            var found = new List<CatalogLeaf>();
            foreach (CatalogLeaf leaf in leaves)
            {
                Type requires = TargetChecks.Requires(leaf.Type);
                foreach (Object o in dropped)
                {
                    if (o != null && Fits(leaf.Type, requires, o))
                    {
                        found.Add(leaf);
                        break;
                    }
                }
            }
            return found;
        }

        // a Component Enabled fits a turned component · an object with one (its first)
        static bool Fits(Type item, Type requires, Object dropped)
            => item == typeof(ComponentEnabledItem) ? ComponentPick.Of(dropped) != null : Fits(requires, dropped);

        // the leaf's new item, Direct to the object — a GameObject dropped for a component's item holds that component (a
        // Component Enabled its first turned one, with the type's name and the index)
        internal static FeedbackItem Create(in CatalogLeaf leaf, Object dropped)
        {
            FeedbackItem item = ItemFactory.Create(leaf);
            if (item is ComponentEnabledItem e && ComponentPick.Of(dropped) is Component c)
            {
                e.m_Target.m_Mode = TargetRefMode.Direct;
                e.m_Target.m_Object = c;
                ComponentPick.Write(e, c);
                return item;
            }
            // a dropped player — the item plays it · controls it; a dropped signal receiver — its channel
            FeedbackPlayer player = dropped as FeedbackPlayer ?? (dropped is GameObject owner ? owner.GetComponent<FeedbackPlayer>() : null);
            if (item is PlayPlayerItem play)
            {
                play.m_Player = player;
                return item;
            }
            if (item is PlayerControlItem control)
            {
                if (player != null)
                    control.m_Players.Add(player);
                return item;
            }
            if (item is SignalBroadcastItem signal)
            {
                FeedbackSignalReceiver receiver = dropped as FeedbackSignalReceiver ?? (dropped is GameObject holder ? holder.GetComponent<FeedbackSignalReceiver>() : null);
                if (receiver != null)
                    signal.m_Channel = receiver.m_Channel;
                return item;
            }
            if (item is TargetItem t)
                t.m_Target = DroppedTarget(t.m_Target, leaf.Type, dropped);
            else if (item is CustomActionItem a)
                a.m_Target = DroppedTarget(a.m_Target, leaf.Type, dropped);
            return item;
        }

        // Direct to the object — the required component of a dropped GameObject
        static TargetRef DroppedTarget(TargetRef target, Type item, Object dropped)
        {
            Type requires = TargetChecks.Requires(item);
            target.m_Mode = TargetRefMode.Direct;
            target.m_Object = dropped is GameObject go && requires != null && (typeof(Component).IsAssignableFrom(requires) || requires.IsInterface)
                ? go.GetComponent(requires)
                : dropped;
            return target;
        }

        // one item per object the leaf fits, at `at` — one undo
        internal static int Add(SerializedObject so, string listPath, int at, in CatalogLeaf leaf, IReadOnlyList<Object> dropped)
        {
            Type requires = TargetChecks.Requires(leaf.Type);
            var items = new List<FeedbackItem>();
            foreach (Object o in dropped)
            {
                if (o != null && Fits(leaf.Type, requires, o))
                    items.Add(Create(leaf, o));
            }
            if (items.Count == 0)
                return -1;
            return ItemListEdits.Insert(so, listPath, at, items, UndoName);
        }
    }
}
