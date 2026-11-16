using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Serialization;
using xpTURN.Feedback.Editor;
using Object = UnityEngine.Object;

namespace xpTURN.Feedback.Tests.Editor
{
    // the edit mode preview of a player's Variants — the bar plays the shown list, the masks and the notices are that list's
    public sealed class PreviewVariantTests
    {
        const string Dir = "Assets/PreviewVariantTests";
        const string ScenePath = Dir + "/PreviewVariantTests.unity";
        const double UpdateStep = 1.0 / 120.0;

        static double s_Now;

        IPreviewRecordSource m_Source;
        Func<double> m_Clock;
        Scene m_Scene;
        Scene m_Active;
        readonly List<FeedbackPlayer> m_Players = new List<FeedbackPlayer>();

        [SetUp]
        public void SetUp()
        {
            m_Source = PreviewRecordPaths.Source;
            m_Clock = PreviewClock.Source;
            PreviewRecordPaths.Source = new PreviewRecordPathTable();
            s_Now = 0;
            PreviewClock.Source = () => s_Now;
            m_Active = SceneManager.GetActiveScene();
            if (!AssetDatabase.IsValidFolder(Dir))
                AssetDatabase.CreateFolder("Assets", "PreviewVariantTests");
            File.WriteAllText(Path.GetFullPath(ScenePath), "%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n");
            AssetDatabase.ImportAsset(ScenePath, ImportAssetOptions.ForceUpdate);
            m_Scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            SceneManager.SetActiveScene(m_Scene);
            m_Players.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            PreviewSession.Current?.Stop();
            PreviewRecovery.Run();
            foreach (FeedbackPlayer p in m_Players)
            {
                if (p != null)
                {
                    PreviewMasks.Clear(p);
                    VariantShow.Set(p, null);
                }
            }
            PreviewRecordPaths.Source = m_Source;
            PreviewClock.Source = m_Clock;
            if (m_Active.IsValid())
                SceneManager.SetActiveScene(m_Active);
            if (m_Scene.IsValid())
                EditorSceneManager.CloseScene(m_Scene, true);
            AssetDatabase.DeleteAsset(Dir);
        }

        static void Seconds(double seconds)
        {
            for (int i = 0; i < (int)Math.Round(seconds / UpdateStep); i++)
            {
                s_Now += UpdateStep;
                PreviewSession.Current?.Update();
            }
        }

        static PreviewSession S => PreviewSession.Current;

        static PlayArgs Free => PlayArgs.Default.WithUnconditional();

        static TargetRef Anchor(string name) => new TargetRef { m_Mode = TargetRefMode.Anchor, m_Anchor = name, m_Path = "" };

        static PositionItem Moving(string anchor, float to, float duration)
        {
            var item = new PositionItem { m_Motion = new TweenMotion { m_Params = new TweenParams { m_To = new Vector4(to, 0f, 0f, 0f), m_Loops = 1 } } };
            item.m_Target = Anchor(anchor);
            item.m_Timing.m_Duration = duration;
            return item;
        }

        static FeedbackPreset Preset(string name, params FeedbackItem[] items)
        {
            var p = ScriptableObject.CreateInstance<FeedbackPreset>();
            p.m_Items.AddRange(items);
            AssetDatabase.CreateAsset(p, Dir + "/" + name + ".asset");
            AssetDatabase.SaveAssets();
            return p;
        }

        static FeedbackPreset Copy(FeedbackPreset from, string name)
        {
            string path = Dir + "/" + name + ".asset";
            Assert.That(AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(from), path), Is.True);
            return AssetDatabase.LoadAssetAtPath<FeedbackPreset>(path);
        }

        FeedbackPlayer Player(string name, params (string Name, Transform At)[] anchors)
        {
            var p = new GameObject(name).AddComponent<FeedbackPlayer>();
            foreach ((string n, Transform at) in anchors)
                p.m_Anchors.Add(new PlayerAnchor { m_Name = n, m_Object = at });
            m_Players.Add(p);
            return p;
        }

        static Transform Go(string name) => new GameObject(name).transform;

        // the bar plays the shown variant — its items, its length; Stop takes it back; an item's ▶ is that variant's item;
        // another Show ends the session (the values back); the player's list is not played meanwhile
        [Test]
        public void VS21_TheBarPlaysTheShownVariant()
        {
            Transform a = Go("A"), b = Go("B"), c = Go("C");
            FeedbackPreset v = Preset("VS21V", Moving("B", 1f, 1f), Moving("C", 1f, 1f));
            FeedbackPlayer p = Player("VS21", ("A", a), ("B", b), ("C", c));
            p.m_Items.Add(Moving("A", 1f, 0.5f));
            p.m_Variants = new List<FeedbackPreset> { v };
            var bar = new PreviewBar(p);
            bar.Unconditional.value = true;

            VariantShow.Set(p, v);
            bar.OnPlay();
            Assert.That(S, Is.Not.Null);
            Seconds(0.3);
            Assert.That(a.position.x, Is.EqualTo(0f), "the player's list is not played");
            Assert.That(b.position.x, Is.GreaterThan(0f), "the variant's items");
            Assert.That(c.position.x, Is.GreaterThan(0f));
            Assert.That(p.MaskOwner, Is.SameAs(v), "the variant's masks");
            Assert.That(S.ScrubState(out _, out double high, out _), Is.EqualTo(SeekRefusal.None));
            Assert.That(high, Is.EqualTo(1.0).Within(1e-4), "the variant's length");
            bar.OnStop();
            Assert.That(b.position.x, Is.EqualTo(0f), "Stop takes it back");
            Assert.That(p.m_PreviewMaskOwner, Is.Null, "the session's end lets the mask owner go");

            bar.PlayItem(1);
            Seconds(0.3);
            Assert.That(b.position.x, Is.EqualTo(0f), "the item alone");
            Assert.That(c.position.x, Is.GreaterThan(0f), "the variant's item 1");
            Assert.That(a.position.x, Is.EqualTo(0f));

            VariantShow.Set(p, null);
            Assert.That(S, Is.Null, "another Show ends the session");
            Assert.That(c.position.x, Is.EqualTo(0f), "the values back");

            bar.OnPlay();
            Seconds(0.3);
            Assert.That(a.position.x, Is.GreaterThan(0f), "Base: the player's list");
            Assert.That(b.position.x, Is.EqualTo(0f));
        }

        // the masks are the shown list's: two lists of the same ids (copies) do not share them — the linked list, a variant
        // copied from it, a copy of that; Solo and an item's ▶ of a variant; Clear takes every list's
        [Test]
        public void VS28b_TheMasksBelongToTheShownList()
        {
            Transform a = Go("A"), b = Go("B");
            FeedbackPreset linked = Preset("VS28L", Moving("A", 1f, 1f), Moving("B", 1f, 1f));
            FeedbackPreset v = Copy(linked, "VS28V");
            FeedbackPreset w = Copy(v, "VS28W");
            Assert.That(ManagedReferenceUtility.GetManagedReferenceIdForObject(v, v.m_Items[0]), Is.EqualTo(ManagedReferenceUtility.GetManagedReferenceIdForObject(linked, linked.m_Items[0])), "the copies share the ids");
            Assert.That(ManagedReferenceUtility.GetManagedReferenceIdForObject(w, w.m_Items[1]), Is.EqualTo(ManagedReferenceUtility.GetManagedReferenceIdForObject(linked, linked.m_Items[1])));
            FeedbackPlayer p = Player("VS28b", ("A", a), ("B", b));
            p.m_Preset = linked;
            p.m_Variants = new List<FeedbackPreset> { v, w };

            void Run(FeedbackPreset preset, bool aMoves, bool bMoves, string what)
            {
                Assert.That(PreviewSession.Start(p, preset != null ? Free.WithPreset(preset) : Free), Is.EqualTo(PreviewRefusal.None));
                Seconds(0.3);
                Assert.That(a.position.x > 0f, Is.EqualTo(aMoves), what + ": A");
                Assert.That(b.position.x > 0f, Is.EqualTo(bMoves), what + ": B");
                S.Stop();
            }

            PreviewMasks.Set(p, linked, 0, PreviewMask.Mute);
            Run(null, false, true, "the linked list's mask");
            Run(v, true, true, "the variant does not read the linked list's mask");
            PreviewMasks.Set(p, v, 1, PreviewMask.Mute);
            Assert.That(PreviewMasks.Get(p, v, 1), Is.EqualTo(PreviewMask.Mute));
            Assert.That(PreviewMasks.Get(p, linked, 1), Is.EqualTo(PreviewMask.None));
            Assert.That(PreviewMasks.Get(p, w, 1), Is.EqualTo(PreviewMask.None));
            Run(v, true, false, "the variant's own mask");
            Run(w, true, true, "a copy of the same ids has its own");
            Run(null, false, true, "the linked list keeps its own");
            PreviewMasks.Set(p, w, 0, PreviewMask.Solo);
            Run(w, true, false, "a variant's Solo");

            Assert.That(PreviewSession.Start(p, Free.WithPreset(v)), Is.EqualTo(PreviewRefusal.None));
            Assert.That(p.MaskOwner, Is.SameAs(v));
            S.PlayItem(1, true);
            Seconds(0.2);
            Assert.That(b.position.x, Is.GreaterThan(0f), "the variant's item 1 alone — its ▶ over its mask");
            Assert.That(a.position.x, Is.EqualTo(0f));
            S.Stop();
            Assert.That(p.m_PreviewMaskOwner, Is.Null);

            Assert.That(new ItemListView(new SerializedObject(v), ItemListEdits.ItemsPath, p, null, true).FoldHost, Is.SameAs(v), "a variant's folds are the preset's");
            Assert.That(new ItemListView(new SerializedObject(linked), ItemListEdits.ItemsPath, p, null, true).FoldHost, Is.SameAs(p), "the player's list: the player's");

            PreviewMasks.Clear(p);
            Assert.That(PreviewMasks.Get(p, linked, 0), Is.EqualTo(PreviewMask.None));
            Assert.That(PreviewMasks.Get(p, v, 1), Is.EqualTo(PreviewMask.None), "Clear takes the variants' masks");
            Assert.That(PreviewMasks.Get(p, w, 0), Is.EqualTo(PreviewMask.None));
        }

        // a variant's Set Parent of a prefab instance's child: left out and told in that variant's preview — not in the player's
        [Test]
        public void VS26_TheNoticesReadTheShownList()
        {
            string prefabPath = Dir + "/VS26.prefab";
            var root = new GameObject("PrefabRoot");
            new GameObject("PrefabChild").transform.SetParent(root.transform, false);
            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            Object.DestroyImmediate(root);
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath), m_Scene);
            Transform child = instance.transform.Find("PrefabChild");
            Transform other = Go("Other");
            var set = new SetParentItem { m_Motion = new SetMotion() };
            set.m_Target = Anchor("C");
            set.m_NewParent = Anchor("O");
            FeedbackPreset v = Preset("VS26V", set, Moving("O", 4f, 1f));
            FeedbackPlayer p = Player("VS26", ("C", child), ("O", other));
            p.m_Items.Add(Moving("O", 1f, 1f));
            p.m_Variants = new List<FeedbackPreset> { v };

            Assert.That(PreviewSession.Start(p, PlayArgs.Default.WithPreset(v)), Is.EqualTo(PreviewRefusal.None));
            Seconds(0.27);
            Assert.That(child.parent, Is.SameAs(instance.transform), "left out");
            var notices = new List<PreviewNotice>();
            PreviewNotices.Collect(S, notices);
            Assert.That(notices.Count(n => n.Kind == PreviewNoticeKind.PrefabChild), Is.EqualTo(1), "told");
            Assert.That(PreviewMasks.Get(p, v, 0), Is.EqualTo(PreviewMask.None), "the user's mask untouched");
            S.Stop();

            Assert.That(PreviewSession.Start(p, PlayArgs.Default), Is.EqualTo(PreviewRefusal.None));
            Seconds(0.1);
            notices.Clear();
            PreviewNotices.Collect(S, notices);
            Assert.That(notices.Count(n => n.Kind == PreviewNoticeKind.PrefabChild), Is.EqualTo(0), "not the player's list's");
            S.Stop();
        }
    }
}
