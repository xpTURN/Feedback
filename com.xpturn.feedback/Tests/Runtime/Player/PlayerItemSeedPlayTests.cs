using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace xpTURN.Feedback.Tests
{
    // a saved list's item seeds where it plays — a Resources prefab made before the build with fixed managed reference ids
    // (a player with its own list and one linking a Resources preset): the ids read are the ones the editor wrote, and each
    // item's seed is Hash32(the play's seed, the item tag, the folded id)
    [PrebuildSetup("xpTURN.Feedback.Tests.Editor.FeedbackItemSeedAssets")]
    [PostBuildCleanup("xpTURN.Feedback.Tests.Editor.FeedbackItemSeedAssets")]
    public sealed class PlayerItemSeedPlayTests
    {
        internal const string PrefabName = "FeedbackItemSeedProbe";
        internal const string PresetName = "FeedbackItemSeedPreset";
        internal const long OwnFirst = 0x0123456700000011L;
        internal const long PresetFirst = 0x0765432100000021L;
        internal const int OwnCount = 3;
        internal const int PresetCount = 2;

        readonly List<Object> m_Made = new List<Object>();

        [SetUp]
        public void SetUp()
        {
            PlayerTestBinding.Reset();
            PlayerTestBinding.Register();
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            foreach (Object o in m_Made)
            {
                if (o != null)
                    Object.Destroy(o);
            }
            m_Made.Clear();
            yield return null;
            PlayerTestBinding.Reset();
        }

        [UnityTest]
        public IEnumerator PS01_ASavedListsSeedsWhereItPlays()
        {
            yield return null;
            var prefab = Resources.Load<GameObject>(PrefabName);
            Assert.That(prefab, Is.Not.Null, "the Resources prefab");
            GameObject go = Object.Instantiate(prefab);
            m_Made.Add(go);
            var fail = new List<string>();
            FeedbackPlayer[] players = go.GetComponentsInChildren<FeedbackPlayer>();
            int linked = 0;
            foreach (FeedbackPlayer p in players)
            {
                bool link = p.m_Preset != null;
                linked += link ? 1 : 0;
                long first = link ? PresetFirst : OwnFirst;
                List<FeedbackItem> items = p.Items;
                if (items.Count != (link ? PresetCount : OwnCount))
                    fail.Add(p.name + " — items " + items.Count);
                for (int i = 0; i < items.Count; i++)
                {
                    long id = ManagedReferenceUtility.GetManagedReferenceIdForObject(p.ItemsOwner, items[i]);
                    if (id != first + i)
                        fail.Add(p.name + " — item " + i + " id " + id.ToString("X16"));
                }
                PlaybackHandle h = p.Play(PlayArgs.Default.WithSeed(42u));
                if (h == default)
                {
                    fail.Add(p.name + " — the play refused");
                    continue;
                }
                int seen = 0;
                for (int k = 0; k < p.Runtime.ItemCount; k++)
                {
                    if (!(p.Runtime.Binding(k) is PlayerTestBinding b))
                        continue;
                    seen++;
                    int i = items.IndexOf(b.Item);
                    uint want = Hash32.Tagged(42u, 0xA00u, Hash32.Fold(first + i));
                    if (b.LastBegin.ItemSeed != want)
                        fail.Add(p.name + " — item " + i + " seed " + b.LastBegin.ItemSeed.ToString("X8") + " not " + want.ToString("X8"));
                }
                if (seen != items.Count)
                    fail.Add(p.name + " — began " + seen);
                p.Stop();
            }
            Assert.That((players.Length, linked), Is.EqualTo((2, 1)), "the prefab's players");
            Assert.That(fail, Is.Empty);
        }
    }
}
