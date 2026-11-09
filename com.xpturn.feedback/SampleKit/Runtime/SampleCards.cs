using System;
using System.Collections.Generic;
using UnityEngine;

namespace xpTURN.Feedback.Samples
{
    // the imported bundles' cards: a card is available when the sample's assembly is there (the packages it needs are
    // installed). The editor gathers the catalogs from the asset database, a player from its preloaded assets ('Build Samples')
    public static class SampleCards
    {
        public static bool Available(SampleCard card) => card != null && (string.IsNullOrEmpty(card.RequiredType) || Type.GetType(card.RequiredType) != null);

        public static void Gather(List<SampleCard> cards)
        {
            cards.Clear();
            var catalogs = new List<SampleCatalog>();
#if UNITY_EDITOR
            foreach (string guid in UnityEditor.AssetDatabase.FindAssets("t:" + nameof(SampleCatalog)))
                if (UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(UnityEditor.AssetDatabase.GUIDToAssetPath(guid)) is SampleCatalog catalog)
                    catalogs.Add(catalog);
#else
            catalogs.AddRange(Resources.FindObjectsOfTypeAll<SampleCatalog>());
#endif
            catalogs.Sort(Compare);
            foreach (SampleCatalog catalog in catalogs)
                if (catalog.Cards != null)
                    foreach (SampleCard card in catalog.Cards)
                        if (card != null && !cards.Contains(card))
                            cards.Add(card);
        }

        static int Compare(SampleCatalog a, SampleCatalog b)
        {
            int order = a.Order.CompareTo(b.Order);
            return order != 0 ? order : string.CompareOrdinal(a.Bundle, b.Bundle);
        }
    }
}
