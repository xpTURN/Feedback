using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace xpTURN.Feedback.Editor
{
    // keep play mode changes. The marks are GlobalObjectId strings in SessionState (not saved — this editor session, across
    // reloads). The hook comes from InitializeOnLoad (a hook set by the inspector's class works only once an inspector was
    // drawn): the marked players are held at the entry (one moved to DontDestroyOnLoad has no id), recorded just before the
    // exit (still alive) and written back in edit mode as one undo group. A prefab instance's object has a folded id in play
    // mode — (object ^ prefab instance, 0) — found by folding the edit ids of its scene's prefab instance objects. A player in
    // an unsaved scene has no id and cannot be marked
    [InitializeOnLoad]
    internal static class KeepPlayModeChanges
    {
        internal const string MarksKey = "xpTURN.Feedback.Keep.Marks";
        internal const string DataKey = "xpTURN.Feedback.Keep.Data";
        internal const string TurnOffKey = "xpTURN.Feedback.Keep.TurnOffAfterPlay";
        internal const string UndoName = "Keep Play Mode Changes";
        internal const string ToggleText = "Keep Play Mode Changes";
        internal const string ToggleTooltip = "Keep this player's play mode changes after play stops — for this editor session.";
        internal const string TurnOffText = "Turn off after play";
        internal const string TurnOffTooltip = "After play stops and the changes are kept, turn keeping off — for all players.";
        internal const string UnsavedText = "Save the scene first — a player in an unsaved scene cannot be found again after play stops.";
        internal const string KeptText = "Play mode changes of this player are kept when play stops.";
        internal const string RowName = "keep-row";
        internal const string ToggleName = "keep";
        internal const string TurnOffName = "keep-off";

        internal enum Markable
        {
            Hidden,
            Disabled,
            Enabled,
        }

        static readonly Dictionary<string, FeedbackPlayer> s_Held = new Dictionary<string, FeedbackPlayer>();
        static int s_Group = -1;
        static bool s_Applying;

        // the warning of the last apply — empty when nothing was wrong
        internal static string LastAlert { get; private set; } = "";

        static KeepPlayModeChanges()
        {
            EditorApplication.playModeStateChanged += OnChange;
        }

        internal static bool TurnOffAfterPlay
        {
            get => EditorPrefs.GetBool(TurnOffKey, false);
            set => EditorPrefs.SetBool(TurnOffKey, value);
        }

        internal static string Id(Object o) => o == null ? "" : GlobalObjectId.GetGlobalObjectIdSlow(o).ToString();

        // identifier type 0: no object of a saved scene or asset (an unsaved scene's · DontDestroyOnLoad's) — one string for all
        static bool NoId(string id) => id.Length == 0 || id.StartsWith("GlobalObjectId_V1-0-", StringComparison.Ordinal);

        internal static List<string> Marks() => SessionState.GetString(MarksKey, "").Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries).ToList();

        // a prefab asset's or a prefab stage's player: hidden (its data is an asset or not in play) · an unsaved scene's: off
        internal static Markable State(FeedbackPlayer player, out string reason)
        {
            reason = null;
            if (player == null || EditorUtility.IsPersistent(player) || StageUtility.GetStage(player.gameObject) != StageUtility.GetMainStage())
                return Markable.Hidden;
            if (string.IsNullOrEmpty(player.gameObject.scene.path))
            {
                reason = UnsavedText;
                return Markable.Disabled;
            }
            return Markable.Enabled;
        }

        internal static bool IsMarked(FeedbackPlayer player) => player != null && Marks().Contains(Id(player));

        // false when the player cannot be marked; in play mode a mark holds the player at once and an unmark lets it go
        internal static bool Mark(FeedbackPlayer player, bool on)
        {
            string id = Id(player);
            if (on && (State(player, out _) != Markable.Enabled || NoId(id)))
                return false;
            List<string> marks = Marks();
            marks.Remove(id);
            if (on)
                marks.Add(id);
            SessionState.SetString(MarksKey, string.Join("|", marks));
            if (EditorApplication.isPlaying)
            {
                if (on)
                    s_Held[id] = player;
                else
                    s_Held.Remove(id);
            }
            return true;
        }

        static void OnChange(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.EnteredPlayMode)
                HoldMarked();
            else if (change == PlayModeStateChange.ExitingPlayMode)
                Capture();
            else if (change == PlayModeStateChange.EnteredEditMode)
                Apply();
        }

        internal static Object Resolve(string id) => GlobalObjectId.TryParse(id, out GlobalObjectId g) ? GlobalObjectId.GlobalObjectIdentifierToObjectSlow(g) : null;

        // a prefab instance object's play id: (object ^ prefab instance, 0)
        internal static string Fold(string id)
        {
            string[] f = id.Split('-');
            if (f.Length != 5 || f[4] == "0")
                return id;
            ulong o = ulong.Parse(f[3], CultureInfo.InvariantCulture);
            ulong prefab = ulong.Parse(f[4], CultureInfo.InvariantCulture);
            return string.Join("-", f[0], f[1], f[2], (o ^ prefab).ToString(CultureInfo.InvariantCulture), "0");
        }

        // a play id of a prefab instance's object back to the edit object — the host scene's prefab instance objects folded
        internal static Object Unfold(string id)
        {
            if (!GlobalObjectId.TryParse(id, out GlobalObjectId g) || g.identifierType != 2 || g.targetPrefabId != 0)
                return null;
            string guid = g.assetGUID.ToString();
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene s = SceneManager.GetSceneAt(i);
                if (!s.isLoaded || AssetDatabase.AssetPathToGUID(s.path) != guid)
                    continue;
                foreach (GameObject root in s.GetRootGameObjects())
                {
                    foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                    {
                        if (!PrefabUtility.IsPartOfPrefabInstance(t))
                            continue;
                        if (Fold(Id(t.gameObject)) == id)
                            return t.gameObject;
                        foreach (Component c in t.GetComponents<Component>())
                        {
                            if (c != null && Fold(Id(c)) == id)
                                return c;
                        }
                    }
                }
            }
            return null;
        }

        static void HoldMarked()
        {
            s_Held.Clear();
            foreach (string id in Marks())
            {
                if ((Resolve(id) ?? Resolve(Fold(id))) is FeedbackPlayer player)
                    s_Held[id] = player;
            }
        }

        // ExitingPlayMode: the held players (a destroyed one by its id again — gone in play: nothing to keep, nothing to say)
        static void Capture()
        {
            var sb = new StringBuilder();
            var ids = new List<string>(s_Held.Keys);
            foreach (string id in Marks())
            {
                if (!ids.Contains(id))
                    ids.Add(id);
            }
            int unknown = 0;
            foreach (string id in ids)
            {
                s_Held.TryGetValue(id, out FeedbackPlayer player);
                if (player == null)
                    player = (Resolve(id) ?? Resolve(Fold(id))) as FeedbackPlayer;
                if (player == null)
                    continue;
                sb.Append(id).Append('\u001F').Append(PlayModeRecord.Capture(player, player.m_Preset != null, out int u)).Append('\u001E');
                unknown += u;
            }
            s_Held.Clear();
            SessionState.SetString(DataKey, sb.ToString());
            SessionState.SetInt(DataKey + ".Unknown", unknown);
        }

        // opened where the first write of the apply happens — an exit with no change leaves no undo step
        internal static void BeginGroup()
        {
            if (!s_Applying || s_Group >= 0)
                return;
            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName(UndoName);
            s_Group = Undo.GetCurrentGroup();
        }

        static void EndGroup()
        {
            if (s_Group >= 0)
                Undo.CollapseUndoOperations(s_Group);
            s_Group = -1;
        }

        // EnteredEditMode: every record into its edit player — one undo group; a player that fails is left and counted
        static void Apply()
        {
            string data = SessionState.GetString(DataKey, "");
            int unknown = SessionState.GetInt(DataKey + ".Unknown", 0);
            SessionState.EraseString(DataKey);
            SessionState.EraseInt(DataKey + ".Unknown");
            LastAlert = "";
            if (data.Length == 0)
                return;
            int dropped = 0, notFound = 0, failed = 0;
            var applied = new List<string>();
            s_Applying = true;
            try
            {
                foreach (string record in data.Split(new[] { '\u001E' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    string[] f = record.Split('\u001F');
                    if (!((Resolve(f[0]) ?? Unfold(f[0])) is FeedbackPlayer player))
                    {
                        notFound++;
                        continue;
                    }
                    try
                    {
                        PlayModeRecord.Apply(player, f[1], out PlayModeRecord.Stats st);
                        dropped += st.Dropped;
                        applied.Add(f[0]);
                    }
                    catch (Exception e)
                    {
                        failed++;
                        Debug.LogException(e, player);
                    }
                }
            }
            finally
            {
                EndGroup();
                s_Applying = false;
            }
            if (TurnOffAfterPlay)
            {
                List<string> marks = Marks();
                marks.RemoveAll(applied.Contains);
                SessionState.SetString(MarksKey, string.Join("|", marks));
            }
            LastAlert = Alert(dropped, notFound, failed, unknown);
            if (LastAlert.Length > 0)
                Debug.LogWarning(LastAlert);
        }

        internal static string Alert(int dropped, int notFound, int failed, int unknown)
        {
            var parts = new List<string>();
            if (dropped > 0)
                parts.Add(dropped == 1 ? "1 reference to an object made in play mode was cleared" : dropped + " references to objects made in play mode were cleared");
            if (notFound > 0)
                parts.Add(notFound == 1 ? "1 player was not found in edit mode" : notFound + " players were not found in edit mode");
            if (failed > 0)
                parts.Add(failed == 1 ? "1 player could not be written" : failed + " players could not be written");
            if (unknown > 0)
                parts.Add(unknown == 1 ? "1 field of an unsupported kind was not kept" : unknown + " fields of an unsupported kind were not kept");
            return parts.Count == 0 ? "" : UndoName + " — " + string.Join(" · ", parts);
        }

        // the inspector's row: the toggle — mixed for several players — and, once on, turn off after play
        internal static VisualElement Row(Object[] targets)
        {
            var row = new VisualElement { name = RowName };
            row.AddToClassList(InspectorStyles.Row);
            FeedbackPlayer[] players = targets.OfType<FeedbackPlayer>().ToArray();
            Markable[] states = players.Select(p => State(p, out _)).ToArray();
            if (states.All(s => s == Markable.Hidden))
            {
                row.style.display = DisplayStyle.None;
                return row;
            }
            var keep = new Toggle(ToggleText) { name = ToggleName, tooltip = ToggleTooltip };
            var off = new Toggle(TurnOffText) { name = TurnOffName, tooltip = TurnOffTooltip };
            off.SetValueWithoutNotify(TurnOffAfterPlay);
            keep.showMixedValue = players.Any(IsMarked) && !players.All(IsMarked);
            keep.SetValueWithoutNotify(players.All(IsMarked));
            if (states.Any(s => s == Markable.Disabled))
            {
                keep.SetEnabled(false);
                keep.tooltip = UnsavedText;
            }
            void ShowOff() => off.style.display = keep.value && !keep.showMixedValue ? DisplayStyle.Flex : DisplayStyle.None;
            keep.RegisterValueChangedCallback(e =>
            {
                foreach (FeedbackPlayer p in players)
                    Mark(p, e.newValue);
                keep.showMixedValue = false;
                ShowOff();
            });
            off.RegisterValueChangedCallback(e => TurnOffAfterPlay = e.newValue);
            row.Add(keep);
            row.Add(off);
            ShowOff();
            return row;
        }
    }
}
