using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using NUnit.Framework;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.Serialization;
using xpTURN.Feedback.Editor;
using Object = UnityEngine.Object;

namespace xpTURN.Feedback.Tests.Editor
{
    // an item's seed: Hash32(the play's seed, the item tag, the item's folded managed reference id) when its list's owner has
    // given the item an id — it keeps its random parts when items are added, removed or moved — else Hash32(the play's seed,
    // its list index). The fixtures give fixed ids (a high half that is not 0) to lists no one has serialized yet, so the
    // goldens are the formula on literals; the edits go through the editor's own list edits
    public sealed class PlayerItemSeedTests
    {
        const string Folder = "Assets/FeedbackItemSeedTests";
        const long First = 0x0123456700000011L;
        const uint ItemTag = 0xA00u;

        readonly List<Object> m_Made = new List<Object>();
        readonly List<FeedbackPlayer> m_Players = new List<FeedbackPlayer>();
        readonly List<FeedbackScheduler> m_Schedulers = new List<FeedbackScheduler>();
        readonly List<string> m_Fail = new List<string>();

        [SetUp]
        public void SetUp()
        {
            m_Fail.Clear();
            PlayerTestBinding.Reset();
            PlayerTestBinding.Register();
            PlayerRuntime.ResetSession();
            ItemClipboard.Instance.m_Items.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (FeedbackPlayer player in m_Players)
            {
                if (player != null)
                    player.ReleaseRuntime();
            }
            m_Players.Clear();
            foreach (Object o in m_Made)
            {
                if (o != null)
                    Object.DestroyImmediate(o);
            }
            m_Made.Clear();
            foreach (FeedbackScheduler scheduler in m_Schedulers)
            {
                if (!scheduler.IsShutdown)
                    scheduler.Shutdown();
            }
            m_Schedulers.Clear();
            ItemClipboard.Instance.m_Items.Clear();
            PlayerTestBinding.Reset();
            AssetDatabase.DeleteAsset(Folder);
        }

        void Check(bool ok, string what)
        {
            if (!ok)
                m_Fail.Add(what);
        }

        void End() => Assert.That(m_Fail, Is.Empty);

        FeedbackScheduler Sched()
        {
            var scheduler = new FeedbackScheduler(ContextKind.Test);
            m_Schedulers.Add(scheduler);
            return scheduler;
        }

        FeedbackPlayer Player(FeedbackScheduler scheduler, params FeedbackItem[] items)
        {
            var go = new GameObject("item seed");
            m_Made.Add(go);
            var player = go.AddComponent<FeedbackPlayer>();
            m_Players.Add(player);
            player.m_Items.AddRange(items);
            player.Bind(scheduler);
            return player;
        }

        FeedbackPlayer Own(FeedbackScheduler scheduler, FeedbackPlayer player)
        {
            m_Players.Add(player);
            player.Bind(scheduler);
            return player;
        }

        static PlayerTestAction Act(string label)
        {
            var action = new PlayerTestAction { m_Label = label, m_Length = 0.5f };
            action.m_Random.m_RandomizeOutput = true;
            action.m_Random.m_OutputRange = new Vector2(0.25f, 1f);
            return action;
        }

        static long Id(Object owner, object item) => ManagedReferenceUtility.GetManagedReferenceIdForObject(owner, item);

        // fixed ids on a list no one has serialized yet
        static void Fix(Object owner, IList<FeedbackItem> items, long first)
        {
            for (int i = 0; i < items.Count; i++)
                Assert.That(ManagedReferenceUtility.SetManagedReferenceIdForObject(owner, items[i], first + i), Is.True, "the fixed id of item " + i);
        }

        static uint ById(uint seed, long id) => Hash32.Tagged(seed, ItemTag, Hash32.Fold(id));

        // what one play of `seed` gave each test item — its seed (where its binding began) and its random output — by the
        // item's label, in the list's order
        static Dictionary<string, (uint Seed, float Output)> Seeds(FeedbackPlayer p, uint seed)
        {
            Assert.That(p.Rebuild(), Is.True, "the rebuild");
            Assert.That(p.Play(PlayArgs.Default.WithSeed(seed)), Is.Not.EqualTo(default(PlaybackHandle)), "the play");
            PlaybackRecord record = p.Runtime.Record(0);
            var seen = new Dictionary<string, (uint, float)>();
            for (int i = 0; i < p.Runtime.ItemCount; i++)
            {
                if (p.Runtime.Binding(i) is PlayerTestBinding b)
                    seen[b.Item.m_Label] = (b.LastBegin.ItemSeed, record.Output[i]);
            }
            p.Stop();
            return seen;
        }

        static string Text(Dictionary<string, (uint Seed, float Output)> seeds)
            => string.Join(" ", seeds.Select(kv => kv.Key + ":" + kv.Value.Seed.ToString("X8") + "x" + kv.Value.Output.ToString("0.####", CultureInfo.InvariantCulture)));

        void Same(Dictionary<string, (uint Seed, float Output)> was, Dictionary<string, (uint Seed, float Output)> now, string what, params string[] labels)
        {
            foreach (string label in labels)
                Check(now.ContainsKey(label) && was[label].Seed == now[label].Seed && was[label].Output == now[label].Output, what + " — " + label + ": " + Text(was) + " / " + Text(now));
        }

        // inserted at 0, the first removed, two swapped: each saved item keeps its seed and its random output — the editor's
        // own list edits; the formula on the fixed ids; the edits keep the ids
        [Test]
        public void IS01_AnItemKeepsItsSeedWhenTheListChanges()
        {
            FeedbackScheduler s = Sched();
            FeedbackPlayer p = Player(s, Act("a"), Act("b"), Act("c"));
            Fix(p, p.m_Items, First);
            Dictionary<string, (uint Seed, float Output)> was = Seeds(p, 42u);
            Check(was["a"].Seed == ById(42u, First) && was["b"].Seed == ById(42u, First + 1) && was["c"].Seed == ById(42u, First + 2),
                "the formula on the ids: " + Text(was));
            Check(was["a"].Seed != was["b"].Seed && was["b"].Seed != was["c"].Seed, "the items' seeds differ: " + Text(was));
            FeedbackItem a = p.m_Items[0];
            FeedbackItem b = p.m_Items[1];
            FeedbackItem c = p.m_Items[2];
            ItemListEdits.Insert(new SerializedObject(p), ItemListEdits.ItemsPath, 0, new FeedbackItem[] { Act("new") }, "Add Feedback");
            Check(p.m_Items.Count == 4 && Id(p, a) == First && Id(p, b) == First + 1 && Id(p, c) == First + 2, "an insert keeps the ids");
            Check(Id(p, p.m_Items[0]) >= 0, "the inserted item has an id at once");
            Same(was, Seeds(p, 42u), "inserted at 0", "a", "b", "c");
            ItemListEdits.Delete(new SerializedObject(p), ItemListEdits.ItemsPath, new[] { 1 });
            Check(p.m_Items.Count == 3 && !p.m_Items.Contains(a), "the delete");
            Same(was, Seeds(p, 42u), "the first saved item removed", "b", "c");
            ItemListEdits.Move(new SerializedObject(p), ItemListEdits.ItemsPath, 1, 2);
            Check(p.m_Items.IndexOf(c) == 1 && p.m_Items.IndexOf(b) == 2 && Id(p, b) == First + 1 && Id(p, c) == First + 2, "the move keeps the ids");
            Same(was, Seeds(p, 42u), "two swapped", "b", "c");
            End();
        }

        // a list no one has serialized — the list index: Hash32(the seed, the index); an inactive flow item ahead keeps its
        // place
        [Test]
        public void IS02_AnItemWithoutAnIdTakesItsPlace()
        {
            FeedbackScheduler s = Sched();
            var wait = new WaitItem();
            wait.m_Timing.m_Duration = 0f;
            FeedbackPlayer p = Player(s, wait, Act("a"), Act("b"));
            Check(p.m_Items.All(x => Id(p, x) == ManagedReferenceUtility.RefIdUnknown), "no ids yet");
            Dictionary<string, (uint Seed, float Output)> was = Seeds(p, 42u);
            Check(was["a"].Seed == Hash32.Of(42u, 1u) && was["b"].Seed == Hash32.Of(42u, 2u), "the list index: " + Text(was));
            wait.m_Active = false;
            Same(was, Seeds(p, 42u), "an inactive flow item ahead", "a", "b");
            Check(p.m_Items.All(x => Id(p, x) == ManagedReferenceUtility.RefIdUnknown), "still no ids");
            End();
        }

        // two players linking one preset take the preset's ids (the player's id of the preset's item is −1); an insert into
        // the preset keeps the others
        [Test]
        public void IS03_ALinkedPlayerTakesThePresetsIds()
        {
            FeedbackScheduler s = Sched();
            var preset = ScriptableObject.CreateInstance<FeedbackPreset>();
            m_Made.Add(preset);
            preset.m_Items.AddRange(new FeedbackItem[] { Act("a"), Act("b") });
            Fix(preset, preset.m_Items, First);
            FeedbackPlayer one = Player(s);
            FeedbackPlayer two = Player(s);
            one.m_Preset = preset;
            two.m_Preset = preset;
            Check(Id(one, preset.m_Items[0]) == ManagedReferenceUtility.RefIdUnknown && one.ItemsOwner == preset, "the preset owns the list");
            Dictionary<string, (uint Seed, float Output)> was = Seeds(one, 42u);
            Check(was["a"].Seed == ById(42u, First) && was["b"].Seed == ById(42u, First + 1), "the preset's ids: " + Text(was));
            Same(was, Seeds(two, 42u), "the other player", "a", "b");
            ItemListEdits.Insert(new SerializedObject(preset), ItemListEdits.ItemsPath, 0, new FeedbackItem[] { Act("new") }, "Add Feedback");
            Same(was, Seeds(one, 42u), "inserted at the preset's 0", "a", "b");
            End();
        }

        // a copy made by Instantiate keeps the ids and the seeds; Instantiate gives a list no one had serialized its ids — the
        // original's too, the same as the copy's
        [Test]
        public void IS04_InstantiateKeepsTheIds()
        {
            FeedbackScheduler s = Sched();
            FeedbackPlayer p = Player(s, Act("a"), Act("b"));
            Fix(p, p.m_Items, First);
            Dictionary<string, (uint Seed, float Output)> was = Seeds(p, 42u);
            GameObject copy = Object.Instantiate(p.gameObject);
            m_Made.Add(copy);
            FeedbackPlayer q = Own(s, copy.GetComponent<FeedbackPlayer>());
            Check(Id(q, q.m_Items[0]) == First && Id(q, q.m_Items[1]) == First + 1, "the copy's ids");
            Same(was, Seeds(q, 42u), "the copy", "a", "b");
            FeedbackPlayer r = Player(s, Act("c"), Act("d"));
            Check(r.m_Items.All(x => Id(r, x) == ManagedReferenceUtility.RefIdUnknown), "no ids yet");
            GameObject rc = Object.Instantiate(r.gameObject);
            m_Made.Add(rc);
            FeedbackPlayer rq = Own(s, rc.GetComponent<FeedbackPlayer>());
            long c0 = Id(r, r.m_Items[0]);
            Check(c0 >= 0 && c0 == Id(rq, rq.m_Items[0]) && Id(r, r.m_Items[1]) == Id(rq, rq.m_Items[1]), "the original's ids are the copy's");
            Dictionary<string, (uint Seed, float Output)> original = Seeds(r, 42u);
            Check(original["c"].Seed == ById(42u, c0), "the original takes its new id: " + Text(original));
            Same(original, Seeds(rq, 42u), "the copy of a list no one had serialized", "c", "d");
            End();
        }

        // a base prefab inserts at 0 — the variant's items (a field override on one) and their instances keep their seeds
        [Test]
        public void IS05_AVariantKeepsItsSeedsWhenTheBaseInserts()
        {
            FeedbackScheduler s = Sched();
            AssetDatabase.DeleteAsset(Folder);
            AssetDatabase.CreateFolder("Assets", "FeedbackItemSeedTests");
            string basePath = Folder + "/Base.prefab";
            var go = new GameObject("base");
            go.AddComponent<FeedbackPlayer>().m_Items.AddRange(new FeedbackItem[] { Act("a"), Act("b") });
            GameObject baseAsset = PrefabUtility.SaveAsPrefabAsset(go, basePath);
            Object.DestroyImmediate(go);
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(baseAsset);
            FeedbackPlayer ip = instance.GetComponent<FeedbackPlayer>();
            ((PlayerTestAction)ip.m_Items[1]).m_Length = 0.75f;
            PrefabUtility.RecordPrefabInstancePropertyModifications(ip);
            GameObject variantAsset = PrefabUtility.SaveAsPrefabAsset(instance, Folder + "/Variant.prefab");
            Object.DestroyImmediate(instance);
            var before = (GameObject)PrefabUtility.InstantiatePrefab(variantAsset);
            m_Made.Add(before);
            Dictionary<string, (uint Seed, float Output)> was = Seeds(Own(s, before.GetComponent<FeedbackPlayer>()), 42u);
            using (var scope = new PrefabUtility.EditPrefabContentsScope(basePath))
                scope.prefabContentsRoot.GetComponent<FeedbackPlayer>().m_Items.Insert(0, Act("new"));
            var after = (GameObject)PrefabUtility.InstantiatePrefab(variantAsset);
            m_Made.Add(after);
            FeedbackPlayer vp = Own(s, after.GetComponent<FeedbackPlayer>());
            Check(vp.m_Items.Count == 3 && ((PlayerTestAction)vp.m_Items[0]).m_Label == "new" && ((PlayerTestAction)vp.m_Items[2]).m_Length == 0.75f,
                "the variant took the base's insert and kept its override");
            Same(was, Seeds(vp, 42u), "the base inserted at 0", "a", "b");
            End();
        }

        // a Random action's pick and the child's seed follow the Random item — an item inserted ahead changes no pick
        [Test]
        public void IS06_ARandomPickFollowsItsItem()
        {
            FeedbackScheduler s = Sched();
            var children = new List<FeedbackPlayer>();
            var random = new RandomPlayerItem();
            for (int i = 0; i < 3; i++)
            {
                FeedbackPlayer c = Player(s, Act("r" + i));
                children.Add(c);
                random.m_Entries.Add(new WeightedPlayer { m_Player = c, m_Weight = 1f });
            }
            random.m_Play.m_Wait = false;
            FeedbackPlayer p = Player(s, Act("x"), random);
            Fix(p, p.m_Items, First);
            string Picks()
            {
                Assert.That(p.Rebuild(), Is.True);
                var sb = new StringBuilder();
                for (uint seed = 100; seed < 120; seed++)
                {
                    p.Play(PlayArgs.Default.WithSeed(seed));
                    int k = children.FindIndex(c => c.IsPlaying);
                    sb.Append(k).Append(k >= 0 ? ":" + children[k].Runtime.Record(0).Seed.ToString("X8") : "").Append(' ');
                    foreach (FeedbackPlayer c in children)
                        c.Stop();
                    p.Stop();
                }
                return sb.ToString();
            }
            string was = Picks();
            Check(was.Split(' ').Select(x => x.Split(':')[0]).Where(x => x.Length > 0).Distinct().Count() > 1, "the picks vary: " + was);
            ItemListEdits.Insert(new SerializedObject(p), ItemListEdits.ItemsPath, 0, new FeedbackItem[] { Act("new") }, "Add Feedback");
            string now = Picks();
            Check(now == was, "an item ahead changes no pick: " + was + " / " + now);
            End();
        }

        // the editor's copies: a Duplicate and a Paste are new items with new seeds; an undone delete brings the item back
        // with its seed; Paste Component Values gives the other player the same seeds
        [Test]
        public void IS07_TheCopies()
        {
            FeedbackScheduler s = Sched();
            FeedbackPlayer p = Player(s, Act("a"), Act("b"));
            Fix(p, p.m_Items, First);
            Dictionary<string, (uint Seed, float Output)> was = Seeds(p, 42u);
            ItemListEdits.Duplicate(new SerializedObject(p), ItemListEdits.ItemsPath, new[] { 1 });
            ((PlayerTestAction)p.m_Items[2]).m_Label = "dup";
            Dictionary<string, (uint Seed, float Output)> dup = Seeds(p, 42u);
            Same(was, dup, "a Duplicate", "a", "b");
            Check(dup["dup"].Seed != was["b"].Seed, "a Duplicate takes a new seed: " + Text(dup));
            ItemListEdits.CopyToClipboard(new SerializedObject(p), ItemListEdits.ItemsPath, new[] { 0 });
            ItemListEdits.Paste(new SerializedObject(p), ItemListEdits.ItemsPath, -1, out _);
            ((PlayerTestAction)p.m_Items[3]).m_Label = "pasted";
            Dictionary<string, (uint Seed, float Output)> pasted = Seeds(p, 42u);
            Check(pasted["pasted"].Seed != was["a"].Seed, "a Paste takes a new seed: " + Text(pasted));
            ItemListEdits.Delete(new SerializedObject(p), ItemListEdits.ItemsPath, new[] { 0 });
            Undo.PerformUndo();
            Check(p.m_Items.Count == 4 && Id(p, p.m_Items[0]) == First, "the undone delete's id");
            Same(was, Seeds(p, 42u), "an undone delete", "a", "b");
            FeedbackPlayer q = Player(s, Act("z"));
            ComponentUtility.CopyComponent(p);
            ComponentUtility.PasteComponentValues(q);
            Check(q.m_Items.Count == 4 && Id(q, q.m_Items[0]) == First && Id(q, q.m_Items[1]) == First + 1, "Paste Component Values' ids");
            Same(was, Seeds(q, 42u), "Paste Component Values", "a", "b");
            End();
        }

        // a list no one had serialized, serialized: the ids come and the seeds take them; two plays of one seed in this
        // process are the same
        [Test]
        public void IS08_ASerializedListTakesItsIds()
        {
            FeedbackScheduler s = Sched();
            FeedbackPlayer p = Player(s, Act("a"), Act("b"));
            Check(p.m_Items.All(x => Id(p, x) == ManagedReferenceUtility.RefIdUnknown), "no ids yet");
            using (new SerializedObject(p))
            {
            }
            long a = Id(p, p.m_Items[0]);
            Check(a >= 0 && Id(p, p.m_Items[1]) >= 0, "the ids came");
            Dictionary<string, (uint Seed, float Output)> once = Seeds(p, 42u);
            Check(once["a"].Seed == ById(42u, a), "the id's formula: " + Text(once));
            Same(once, Seeds(p, 42u), "again", "a", "b");
            End();
        }
    }
}
