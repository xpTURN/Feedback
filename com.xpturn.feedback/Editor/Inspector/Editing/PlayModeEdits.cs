using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Serialization;
using Object = UnityEngine.Object;

namespace xpTURN.Feedback.Editor
{
    internal enum EditBranch
    {
        None,
        NextPlay,
        Rebuild,
    }

    // an edit of a prepared player in play mode — by its path, the next Play reads it or it waits for a Rebuild. A waiting
    // edit is marked with the player's prepare count and cleared when the count moves (a Rebuild from anywhere). The report
    // comes late — at the next edit's apply or the frame's end — so the mark is seen after it. The auto Rebuild (off by
    // default — a running play is cut by its Hold) waits 150 ms on the editor clock, again at each edit
    [InitializeOnLoad]
    internal static class PlayModeEdits
    {
        internal const string AutoRebuildKey = "xpTURN.Feedback.Inspector.AutoRebuild";
        internal const double AutoRebuildWait = 0.15;
        const string ManagedHead = "managedReferences[";

        // the player's fields the prepare reads — the rest reach the next Play
        static readonly string[] s_PreparePaths = { "m_Items.", "m_Policy", "m_MaxOverlap", "m_Anchors", "m_ApplyInitialState", "m_Preset" };

        static readonly Dictionary<FeedbackPlayer, int> s_Pending = new Dictionary<FeedbackPlayer, int>();
        static readonly Dictionary<FeedbackPlayer, double> s_RebuildAt = new Dictionary<FeedbackPlayer, double>();
        static readonly List<FeedbackPlayer> s_Due = new List<FeedbackPlayer>();

        static PlayModeEdits()
        {
            Undo.postprocessModifications += OnModifications;
            EditorApplication.update += Update;
            EditorApplication.playModeStateChanged += _ => Clear();
        }

        internal static bool AutoRebuild
        {
            get => EditorPrefs.GetBool(AutoRebuildKey, false);
            set => EditorPrefs.SetBool(AutoRebuildKey, value);
        }

        internal static void Clear()
        {
            s_Pending.Clear();
            s_RebuildAt.Clear();
        }

        // the branch of a modification's path on a player — curvesNow: a curve · gradient, shared with the running plays
        internal static EditBranch Classify(Object target, string path, out bool curvesNow)
        {
            curvesNow = false;
            if (path.StartsWith(ManagedHead, StringComparison.Ordinal))
            {
                int close = path.IndexOf(']');
                if (close < 0 || !long.TryParse(path.Substring(ManagedHead.Length, close - ManagedHead.Length), out long id))
                    return EditBranch.Rebuild;
                string rest = close + 2 <= path.Length ? path.Substring(close + 2) : "";
                object o = ManagedReferenceUtility.GetManagedReference(target, id);
                if (o is FeedbackMotion)
                {
                    curvesNow = rest.Contains("m_Curve") || rest.Contains("m_Gradient");
                    return EditBranch.Rebuild;
                }
                if (o is FeedbackItem)
                {
                    if (rest.StartsWith("m_Label", StringComparison.Ordinal))
                        return EditBranch.None;
                    if (rest.StartsWith("m_Conditions", StringComparison.Ordinal) || rest.StartsWith("m_Random", StringComparison.Ordinal)
                        || rest.StartsWith("m_Intensity", StringComparison.Ordinal) || rest.StartsWith("m_PlayDirection", StringComparison.Ordinal))
                        return EditBranch.NextPlay;
                }
                // an item's timing · target · motion · switch · own fields, or a value inside them
                return EditBranch.Rebuild;
            }
            foreach (string p in s_PreparePaths)
            {
                if (path.StartsWith(p, StringComparison.Ordinal))
                    return EditBranch.Rebuild;
            }
            return EditBranch.NextPlay;
        }

        static UndoPropertyModification[] OnModifications(UndoPropertyModification[] modifications)
        {
            if (!Application.isPlaying)
                return modifications;
            Dictionary<FeedbackPreset, List<FeedbackPlayer>> linked = null;
            for (int i = 0; i < modifications.Length; i++)
            {
                PropertyModification edit = modifications[i].currentValue ?? modifications[i].previousValue;
                if (edit?.target is FeedbackPlayer player)
                    Report(player, edit.propertyPath);
                else if (edit?.target is FeedbackPreset preset)
                {
                    linked ??= new Dictionary<FeedbackPreset, List<FeedbackPlayer>>();
                    if (!linked.TryGetValue(preset, out List<FeedbackPlayer> players))
                        linked[preset] = players = PresetLinks.Linked(preset);
                    foreach (FeedbackPlayer p in players)
                        Report(p, preset, edit.propertyPath);
                }
            }
            return modifications;
        }

        // a prepared player's edit — one the first prepare will read anyway marks nothing
        internal static void Report(FeedbackPlayer player, string path) => Report(player, player, path);

        // an edit of the player, or of the preset it links
        internal static void Report(FeedbackPlayer player, Object edited, string path)
        {
            if (player == null || player.Runtime == null || Classify(edited, path, out _) != EditBranch.Rebuild)
                return;
            s_Pending[player] = player.PrepareCount;
            if (AutoRebuild)
                s_RebuildAt[player] = PreviewClock.Source() + AutoRebuildWait;
        }

        internal static bool Pending(FeedbackPlayer player)
        {
            if (player == null || !s_Pending.TryGetValue(player, out int count))
                return false;
            if (count == player.PrepareCount)
                return true;
            s_Pending.Remove(player);
            return false;
        }

        // the auto Rebuild's due players — a destroyed one is dropped
        internal static void Update()
        {
            if (s_RebuildAt.Count == 0)
                return;
            double now = PreviewClock.Source();
            s_Due.Clear();
            foreach (KeyValuePair<FeedbackPlayer, double> due in s_RebuildAt)
            {
                if (due.Key == null || now >= due.Value)
                    s_Due.Add(due.Key);
            }
            foreach (FeedbackPlayer player in s_Due)
            {
                s_RebuildAt.Remove(player);
                if (player != null && Application.isPlaying && AutoRebuild)
                    player.Rebuild();
            }
        }
    }
}
