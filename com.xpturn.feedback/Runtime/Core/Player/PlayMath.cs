using UnityEngine;
using UnityEngine.Serialization;

namespace xpTURN.Feedback
{
    // a play's random parts, skips, turns and intensities — the runtime, the Motion Driver, the bindings and the editor's
    // graph take them from here. Values only, no allocation
    internal static class PlayMath
    {
        // what an item's seed hashes: its managed reference id under the list's owner, folded, else its list index
        internal static uint SeedKey(Object owner, FeedbackItem item, int listIndex, out bool isId)
        {
            long id = item != null ? ManagedReferenceUtility.GetManagedReferenceIdForObject(owner, item) : ManagedReferenceUtility.RefIdUnknown;
            isId = id >= 0;
            return isId ? Hash32.Fold(id) : (uint)listIndex;
        }

        internal static uint ItemSeed(uint seed, uint key, bool isId) => isId ? Hash32.Tagged(seed, Hash32.Item, key) : Hash32.Of(seed, key);

        // an occurrence's evaluation seed — each repeat of an item draws anew
        internal static uint OccurrenceSeed(uint itemSeed, long occurrence) => Hash32.Tagged(itemSeed, Hash32.Occurrence, Hash32.Fold(occurrence));

        // a range's draw by the hash: finite and not negative, else 1
        internal static float Range(Vector2 range, uint hash) => PlayerRuntime.Factor(Mathf.Lerp(range.x, range.y, Hash32.ToUnit(hash)));

        internal static float Output(in ItemRandomness random, uint itemSeed)
            => random.m_RandomizeOutput ? Range(random.m_OutputRange, Hash32.Tagged(itemSeed, Hash32.Output, 0u)) : 1f;

        // the least and the most an item's random output can be
        internal static void OutputBounds(in ItemRandomness random, out float low, out float high)
        {
            if (!random.m_RandomizeOutput)
            {
                low = 1f;
                high = 1f;
                return;
            }
            float x = random.m_OutputRange.x, y = random.m_OutputRange.y;
            float a = PlayerRuntime.Factor(x), b = PlayerRuntime.Factor(y);
            low = a < b ? a : b;
            high = a < b ? b : a;
            // a range across 0: the draws near 0 and the negative ones, which count as 1
            if ((x < 0f) != (y < 0f))
            {
                low = 0f;
                if (high < 1f)
                    high = 1f;
            }
        }

        internal static float DurationScale(in ItemRandomness random, uint itemSeed)
            => random.m_RandomizeDuration ? Range(random.m_DurationRange, Hash32.Tagged(itemSeed, Hash32.Duration, 0u)) : 1f;

        // the whole list's length multiplier: the player's, times its random length unless the play is free
        internal static float PlayerDurationScale(FeedbackPlayer player, uint seed, bool free)
        {
            float global = PlayerRuntime.Factor(player.m_DurationMultiplier);
            if (!free && player.m_RandomizeDuration)
                global *= Range(player.m_DurationRange, Hash32.Tagged(seed, Hash32.Duration, 0u));
            return global;
        }

        internal static bool ChanceSkips(in ItemConditions conditions, uint itemSeed)
            => conditions.m_Chance < 100f && Hash32.ToUnit(Hash32.Tagged(itemSeed, Hash32.Chance, 0u)) * 100f >= conditions.m_Chance;

        // the direction's condition and the intensity range — the play's inputs
        internal static bool ConditionSkips(in ItemConditions conditions, bool reverse, float playIntensity)
        {
            if (conditions.m_Direction == DirectionCondition.ReverseOnly ? !reverse : conditions.m_Direction == DirectionCondition.ForwardOnly && reverse)
                return true;
            return conditions.m_UseIntensityRange && (playIntensity < conditions.m_IntensityRange.x || playIntensity > conditions.m_IntensityRange.y);
        }

        // an item's skips that the seed and the inputs decide — a category turned off first, then the chance (not in a free
        // play), then the conditions. The cooldown and the count of plays are a play's history: not here
        internal static bool SkipsBySeed(EffectItem effect, uint itemSeed, float playIntensity, bool reverse, bool categoryOn, bool free)
        {
            if (!categoryOn)
                return true;
            if (!free && ChanceSkips(in effect.m_Conditions, itemSeed))
                return true;
            return ConditionSkips(in effect.m_Conditions, reverse, playIntensity);
        }

        // the item runs against the play's way — never an item with a direction condition, an open one or an endless one
        internal static bool Turns(EffectItem effect, bool reverse, bool open, bool infinite)
        {
            if (effect == null || effect.m_Conditions.m_Direction != DirectionCondition.Always || open || infinite)
                return false;
            switch (effect.m_PlayDirection)
            {
                case ItemPlayDirection.Forward: return reverse;
                case ItemPlayDirection.Backward: return !reverse;
                case ItemPlayDirection.Opposite: return true;
                default: return false;
            }
        }

        // a play's intensity: the argument through the remap (finite and not negative, else 0) × the player's intensity ×
        // the falloff
        internal static float PlayIntensity(FeedbackPlayer player, float input, float falloff)
        {
            float intensity = input;
            if (player.m_RemapIntensity && player.m_IntensityRemap != null)
                intensity = PlayerRuntime.Factor0(player.m_IntensityRemap.Evaluate(input));
            intensity *= PlayerRuntime.Factor(player.m_Intensity) * falloff;
            return intensity;
        }

        // an item's begin intensity: Ignore takes the random output alone
        internal static float ItemIntensity(IntensityResponse response, float playIntensity, float output)
            => response == IntensityResponse.Ignore ? output : playIntensity * output;

        // a Motion Driver's Loop: its own intensity (Ignore: 1) × the random output of its seed
        internal static float DriverIntensity(EffectItem item, float driverIntensity, uint seed)
        {
            float intensity = item.m_Intensity == IntensityResponse.Ignore ? 1f : PlayerRuntime.Factor(driverIntensity);
            if (item.m_Random.m_RandomizeOutput)
                intensity *= Output(in item.m_Random, seed);
            return intensity;
        }
    }
}
