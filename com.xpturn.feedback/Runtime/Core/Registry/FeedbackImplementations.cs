using System;
using System.Collections.Generic;

namespace xpTURN.Feedback
{
    // item type → binding factory, looked up only when a player prepares. The key is the exact type and registering again
    // overwrites. Never cleared — with domain reload off, optional assemblies register the same again
    internal static class FeedbackImplementations
    {
        static readonly Dictionary<Type, Func<EffectItem, ItemBinding>> s_Factories = new Dictionary<Type, Func<EffectItem, ItemBinding>>();

        // Core first — an optional assembly's Register that touches the class first runs this before its own entry
        static FeedbackImplementations() => CoreImplementations.RegisterAll();

        internal static void Register<TItem>(Func<TItem, ItemBinding> factory) where TItem : EffectItem
            => s_Factories[typeof(TItem)] = item => factory((TItem)item);

        // null when none is registered — the caller skips the item and warns. A type without its own entry under the two
        // extension bases gets the package's adapter
        internal static ItemBinding Create(EffectItem item)
        {
            if (item == null)
                return null;
            if (s_Factories.TryGetValue(item.GetType(), out var factory))
                return factory(item);
            if (item is CustomActionItem action)
                return new CustomActionBinding(action);
            if (item is CustomEffectItem effect)
                return new CustomEffectBinding(effect);
            return null;
        }

        // what Create makes a binding for — the editor's 'not installed' and the menu's dimming read it, not RegisteredTypes
        internal static bool IsImplemented(Type type)
            => type != null && (s_Factories.ContainsKey(type) || typeof(CustomActionItem).IsAssignableFrom(type) || typeof(CustomEffectItem).IsAssignableFrom(type));

        // read by the tests (the overlap check)
        internal static IReadOnlyCollection<Type> RegisteredTypes => s_Factories.Keys;
    }
}
