using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEditor;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace xpTURN.Feedback.Editor
{
    // a rule sees one thing. Its id (FB- + group letter + two digits) is never reused — a removed rule leaves its number
    // empty, a new rule takes its group's next number, a subject without a group takes a new letter from H
    internal enum RuleSeverity
    {
        Info,
        Warning,
        Error,
    }

    internal enum RuleScope
    {
        Host,
        Project,
    }

    // a fix: its name and its action — one undo group (FeedbackValidator.Apply). A fix that writes a file or reloads a scene
    // is not undoable and says so on its button
    internal sealed class RuleFix
    {
        internal const string NotUndoable = " — not undoable";

        internal readonly string Name;
        internal readonly Action Run;
        internal readonly bool Undoable;

        internal RuleFix(string name, Action run, bool undoable = true)
        {
            Name = name;
            Run = run;
            Undoable = undoable;
        }

        internal string Label => Undoable ? Name : Name + NotUndoable;
    }

    internal sealed class RuleResult
    {
        internal FeedbackRule Rule;
        internal string Id;
        internal RuleSeverity Severity;
        internal Object Host;
        internal int Item = -1;
        internal string Message;
        internal readonly List<RuleFix> Fixes = new List<RuleFix>();

        public override string ToString() => "[" + Id + "] " + Message;
    }

    // one host's check: the host, its player (null for another host), its list and the list's path, the target resolution's
    // context (the context null), the compiled list and its nominal resolve (built once, when a rule asks)
    internal sealed class RuleContext
    {
        internal Component Host;
        internal FeedbackPlayer Player;
        // the list's owner — the host, or the linked preset, or the preset host
        internal Object Owner;
        internal const string LinkedWords = " — changes the linked preset";
        internal bool Linked => Host != null && Owner != null && !ReferenceEquals(Owner, Host);
        internal Object For(string path) => path != null && path.StartsWith(ListPath, StringComparison.Ordinal) ? Owner : Host;
        internal string Words(string name, string path) => Linked && path != null && path.StartsWith(ListPath, StringComparison.Ordinal) ? name + LinkedWords : name;
        internal RuleFix ItemFix(string name, string path, Action<SerializedProperty> write, bool undoable = true)
        {
            Object to = For(path);
            return new RuleFix(Words(name, path), () => RuleFixes.Write(to, path, write), undoable);
        }
        internal List<FeedbackItem> Items;
        internal string ListPath;
        // a MotionDriver's one item — its path is the field itself
        internal bool Single;
        internal PrepareContext Prepare;
        internal bool InPrefabAsset;
        SerializedObject m_Serialized;
        List<int> m_ListIndex;
        TimelineProgram m_Program;
        ResolvedTimeline m_Resolved;

        internal SerializedObject Serialized => m_Serialized ??= new SerializedObject(Owner != null ? Owner : Host);
        SerializedObject m_HostSerialized;
        internal SerializedObject HostSerialized => m_HostSerialized ??= new SerializedObject(Host);

        internal string ItemPath(int i, string relative = null)
            => (Single ? ListPath : ListPath + ".Array.data[" + i + "]") + (relative != null ? "." + relative : "");

        readonly Dictionary<int, Object> m_Targets = new Dictionary<int, Object>();
        Dictionary<AnimationClip, EditorCurveBinding[]> m_Curves;
        Dictionary<Scene, HashSet<Transform>> m_Sources;
        int m_FlashLimit = -1;

        // an item's resolved target, once a check (null when it does not resolve — A01's — and for a global item: it has none)
        internal Object Target(int i)
        {
            if (!m_Targets.TryGetValue(i, out Object o))
            {
                TargetFailure failure = TargetFailure.None;
                o = Items[i] is TargetItem t && !TargetChecks.Global(t.GetType()) ? RuleFixes.Resolve(t, in Prepare, out failure) : null;
                // a prefab asset's own object (the menu's prefab scan): the runtime answers NotInScene — the rules take it (A02's rule)
                if (o == null && failure == TargetFailure.NotInScene && Items[i] is TargetItem d && d.m_Target.m_Mode == TargetRefMode.Direct
                    && d.m_Target.m_Object != null && TargetChecks.Check(Host, d.m_Target.m_Object) == TargetRefusal.None)
                    o = Coerce(d.m_Target.m_Object, RuleFixes.Requires(d));
                m_Targets[i] = o;
            }
            return o;
        }

        static Object Coerce(Object o, Type requires)
        {
            GameObject go = o is GameObject g ? g : o is Component c ? c.gameObject : null;
            if (go == null || requires == null)
                return o;
            if (requires == typeof(GameObject))
                return go;
            return requires.IsInstanceOfType(o) ? o : go.GetComponent(requires);
        }

        // a clip's curves, once a check
        internal EditorCurveBinding[] Curves(AnimationClip clip)
        {
            m_Curves ??= new Dictionary<AnimationClip, EditorCurveBinding[]>();
            if (!m_Curves.TryGetValue(clip, out EditorCurveBinding[] b))
                m_Curves[clip] = b = AnimationUtility.GetCurveBindings(clip);
            return b;
        }

        // the constraint sources of the target's scene (a prefab asset: its root's), once a check
        internal HashSet<Transform> ConstraintSources(Transform t)
        {
            m_Sources ??= new Dictionary<Scene, HashSet<Transform>>();
            Scene scene = t.gameObject.scene;
            if (m_Sources.TryGetValue(scene, out HashSet<Transform> set))
                return set;
            set = new HashSet<Transform>();
            IEnumerable<GameObject> roots = scene.IsValid() && scene.isLoaded ? scene.GetRootGameObjects() : new[] { t.root.gameObject };
            foreach (GameObject root in roots)
            {
                foreach (IConstraint k in root.GetComponentsInChildren<IConstraint>(true))
                {
                    for (int s = 0; s < k.sourceCount; s++)
                    {
                        Transform src = k.GetSource(s).sourceTransform;
                        if (src != null)
                            set.Add(src);
                    }
                }
            }
            m_Sources[scene] = set;
            return set;
        }

        // the runtime's flash limit (the settings the runtime loads)
        internal int FlashLimit => m_FlashLimit >= 0 ? m_FlashLimit : m_FlashLimit = SettingsValues.Load(ContextKind.Runtime).FlashLimit;

        internal TimelineProgram Program
        {
            get
            {
                if (m_Program == null)
                {
                    var descs = new List<TimelineItemDesc>(Items.Count);
                    m_ListIndex = new List<int>(Items.Count);
                    TimelineConversion.DescribeList(Items, descs, m_ListIndex);
                    m_Program = TimelineProgram.Compile(descs.ToArray());
                }
                return m_Program;
            }
        }

        // a program item's list slot (inactive flow items are left out of the program)
        internal List<int> ListIndex
        {
            get
            {
                _ = Program;
                return m_ListIndex;
            }
        }

        // the nominal resolve — the inspector strip's (TimelineModel)
        internal ResolvedTimeline Resolved
        {
            get
            {
                if (m_Resolved == null)
                {
                    m_Resolved = new ResolvedTimeline(Program);
                    float factor = Player != null ? PlayerRuntime.Factor(Player.m_DurationMultiplier) : 1f;
                    float delay = Player != null ? PlayerRuntime.Offset(Player.m_StartDelay) : 0f;
                    Program.Resolve(default, default, factor, delay, m_Resolved);
                }
                return m_Resolved;
            }
        }
    }

    internal abstract class FeedbackRule
    {
        internal abstract string Id { get; }
        internal abstract RuleSeverity Severity { get; }
        internal virtual RuleScope Scope => RuleScope.Host;
        internal virtual bool Applies(Component host) => host is FeedbackPlayer;
        // sees the list alone — runs on a preset host; a linked player gives it an empty list
        internal virtual bool ListOnly => false;

        // a rule on one item each — a player's and a MotionDriver's
        protected static bool OnItems(Component host) => host is FeedbackPlayer || host is MotionDriver;

        internal virtual void Check(RuleContext c, List<RuleResult> into)
        {
        }

        internal virtual void CheckProject(List<RuleResult> into)
        {
        }

        protected RuleResult Result(RuleContext c, int item, string message)
            => new RuleResult { Rule = this, Id = Id, Severity = Severity, Host = c.Host != null ? c.Host : c.Owner, Item = item, Message = message };

        protected RuleResult Result(Object host, string message)
            => new RuleResult { Rule = this, Id = Id, Severity = Severity, Host = host, Item = -1, Message = message };

        // a value in a message
        protected static string Text(float value) => value.ToString("0.###", CultureInfo.InvariantCulture);
    }
}
