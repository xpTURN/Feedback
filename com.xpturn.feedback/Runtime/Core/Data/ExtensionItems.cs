using System;
using System.Collections.Generic;
using UnityEngine;

namespace xpTURN.Feedback
{
    /// <summary>Whether the inspector's preview runs a custom feedback's code.</summary>
    public enum CustomPreview
    {
        /// <summary>The preview does not call it; the preview bar counts the plays it skipped.</summary>
        Skip = 0,
        /// <summary>The preview calls it with InPreview true; what it changes outside the package is not put back.</summary>
        Run = 1,
    }

    // read by the editor (the Add menu, the target field) and, in a preview, by the binding (Preview) — a player reads none of it
    /// <summary>Names a custom feedback in the inspector's Add menu and tells the editor about its target and its preview.</summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class CustomFeedbackAttribute : Attribute
    {
        /// <summary>Puts the feedback at <paramref name="path"/> in the Add menu, groups separated by '/'.</summary>
        public CustomFeedbackAttribute(string path)
        {
            Path = path;
        }

        /// <summary>The Add menu path; an effect's leaves are this path and each motion it takes.</summary>
        public string Path { get; }

        /// <summary>Search words for the Add menu, space separated.</summary>
        public string Aliases { get; set; } = "";

        /// <summary>True when the feedback has no target: the inspector hides the target field.</summary>
        public bool Global { get; set; }

        /// <summary>What a Direct target or a dropped object must be, a component type or an interface; null takes any object.</summary>
        public Type Requires { get; set; }

        /// <summary>Whether the inspector's preview calls the feedback's code; Skip unless set.</summary>
        public CustomPreview Preview { get; set; }
    }

    // a user's action derives from this; its binding is the package's adapter. The internal abstract members are sealed here,
    // so the closed item layer opens only through the two extension bases
    /// <summary>The base of a custom action: an instant or a timed act whose code is the CustomAction it makes.</summary>
    [Serializable]
    public abstract class CustomActionItem : ActionItem
    {
        [SerializeField] internal TargetRef m_Target = TargetRef.Default;

        /// <summary>Makes the item; the inspector and the serializer call a derived type's public parameterless constructor.</summary>
        protected CustomActionItem()
        {
        }

        /// <summary>The act's length in seconds on the item's clock, 0 for an instant; read before a player prepares, so compute it from serialized fields.</summary>
        public virtual float Duration => 0f;

        /// <summary>The category the action counts in, read once for its type, so return a constant; Flash reads as None.</summary>
        public virtual FeedbackCategory Category => FeedbackCategory.None;

        /// <summary>Makes the code object for one preparation; a player calls it each time it prepares the item.</summary>
        protected internal abstract CustomAction CreateAction();

        internal sealed override FeedbackCategory DeclaredCategory => ExtensionTraits.CategoryOf(this);

        internal sealed override float NaturalDuration
        {
            get
            {
                float d = Duration;
                return d > 0f ? d : 0f;
            }
        }

        // a timed act runs backward on its own (Begin with Reverse, T from 1 to 0) — an instant's reverse is its Undo
        internal sealed override bool ActsBackward => NaturalDuration > 0f;

        internal sealed override bool Opens => false;

        internal sealed override bool Flashes => false;
    }

    /// <summary>The base of a custom effect: a motion's value handed to the CustomEffect it makes, every frame the play runs.</summary>
    [Serializable]
    public abstract class CustomEffectItem : TargetItem
    {
        [SerializeField] internal float m_Base;

        /// <summary>Makes the item; the inspector and the serializer call a derived type's public parameterless constructor.</summary>
        protected CustomEffectItem()
        {
        }

        /// <summary>The category the effect counts in, read once for its type, so return a constant; Flash reads as None.</summary>
        public virtual FeedbackCategory Category => FeedbackCategory.None;

        /// <summary>Makes the code object for one preparation; a player calls it each time it prepares the item.</summary>
        protected internal abstract CustomEffect CreateEffect();

        internal sealed override TargetTraits Traits => ExtensionTraits.Of(this);
    }

    // per type, made on the first read (a preparation or the editor) — the play path reads the binding's copy
    internal static class ExtensionTraits
    {
        // the motions without a slot — a Custom Float Event's
        internal const MotionMask Motions = MotionMask.Tween | MotionMask.Punch | MotionMask.Shake | MotionMask.Curve | MotionMask.Oscillate;

        static readonly Dictionary<Type, TargetTraits> s_Effects = new Dictionary<Type, TargetTraits>();
        static readonly Dictionary<Type, FeedbackCategory> s_Actions = new Dictionary<Type, FeedbackCategory>();

        // a declared Flash would not count against the flash limit — it reads as None
        static FeedbackCategory Declared(FeedbackCategory category) => category == FeedbackCategory.Flash ? FeedbackCategory.None : category;

        internal static TargetTraits Of(CustomEffectItem item)
        {
            Type type = item.GetType();
            if (!s_Effects.TryGetValue(type, out TargetTraits traits))
                s_Effects[type] = traits = new TargetTraits(ValueKind.Float, Motions, Declared(item.Category), false, ApplyMode.Offset);
            return traits;
        }

        internal static FeedbackCategory CategoryOf(CustomActionItem item)
        {
            Type type = item.GetType();
            if (!s_Actions.TryGetValue(type, out FeedbackCategory category))
                s_Actions[type] = category = Declared(item.Category);
            return category;
        }

        // the attribute's Preview — read only by a preview's preparation
        internal static CustomPreview Preview(Type type)
            => ((CustomFeedbackAttribute)Attribute.GetCustomAttribute(type, typeof(CustomFeedbackAttribute), false))?.Preview ?? CustomPreview.Skip;
    }
}
