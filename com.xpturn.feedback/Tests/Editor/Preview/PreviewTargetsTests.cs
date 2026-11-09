using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using xpTURN.Feedback.Editor;
using Object = UnityEngine.Object;

namespace xpTURN.Feedback.Tests.Editor
{
    // the blocks around the stop, the record paths' table and its judgment, the derived fields' finder, each base target
    // kind's save and stop, the block and the instance item. The source is the slot types' table; the session runs on the
    // test's clock — an update is 1/120 s, a tick 1/60 s. A save's check needs the recorded values off their start at the
    // save (a shake is 0 at its half periods)
    public sealed class PreviewTargetsTests
    {
        const string ScenePath = "Assets/PreviewTargetsTests.unity";
        const string PrefabPath = "Assets/PreviewTargetsTests.prefab";
        const string ControllerPath = "Assets/PreviewTargetsTests.controller";
        const double UpdateStep = 1.0 / 120.0;
        static readonly Vector3 s_Home = new Vector3(1f, 2f, 3f);

        static double s_Now;

        IPreviewRecordSource m_Source;
        Func<double> m_Clock;
        Scene m_Scene;
        Scene m_Active;

        [SetUp]
        public void SetUp()
        {
            m_Source = PreviewRecordPaths.Source;
            m_Clock = PreviewClock.Source;
            PreviewRecordPaths.Source = new PreviewRecordPathTable();
            s_Now = 0;
            PreviewClock.Source = () => s_Now;
            m_Active = SceneManager.GetActiveScene();
            File.WriteAllText(Path.GetFullPath(ScenePath), "%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n");
            AssetDatabase.ImportAsset(ScenePath, ImportAssetOptions.ForceUpdate);
            m_Scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            SceneManager.SetActiveScene(m_Scene);
        }

        [TearDown]
        public void TearDown()
        {
            PreviewSession.Current?.Stop();
            PreviewRecovery.Run();
            PreviewRecordPaths.Source = m_Source;
            PreviewClock.Source = m_Clock;
            if (StageUtility.GetCurrentStage() is PrefabStage)
                StageUtility.GoToMainStage();
            if (m_Active.IsValid())
                SceneManager.SetActiveScene(m_Active);
            if (m_Scene.IsValid())
                EditorSceneManager.CloseScene(m_Scene, true);
            AssetDatabase.DeleteAsset(ScenePath);
            AssetDatabase.DeleteAsset(PrefabPath);
            AssetDatabase.DeleteAsset(ControllerPath);
        }

        static void Pump(int updates)
        {
            for (int i = 0; i < updates; i++)
            {
                s_Now += UpdateStep;
                PreviewSession.Current?.Update();
            }
        }

        static void Seconds(double seconds) => Pump((int)Math.Round(seconds / UpdateStep));

        static void Save(Scene scene) => Assert.That(EditorSceneManager.SaveScene(scene), Is.True);

        static T Item<T>(FeedbackPlayer player, FeedbackMotion motion, float duration) where T : TargetItem, new()
        {
            var item = new T();
            item.m_Target.m_Mode = TargetRefMode.Self;
            item.m_Motion = motion;
            item.m_Timing.m_Duration = duration;
            player.m_Items.Add(item);
            return item;
        }

        static T ItemOn<T>(FeedbackPlayer player, Object target, FeedbackMotion motion, float duration) where T : TargetItem, new()
        {
            T item = Item<T>(player, motion, duration);
            item.m_Target.m_Mode = TargetRefMode.Direct;
            item.m_Target.m_Object = target;
            return item;
        }

        static TargetRef Direct(Object o) => new TargetRef { m_Mode = TargetRefMode.Direct, m_Object = o, m_Path = "", m_Anchor = "" };

        static TweenMotion Tween(Vector4 to)
        {
            var tween = new TweenMotion();
            tween.m_Params.m_To = to;
            return tween;
        }

        // a flash toward red: the default target is white, and on the white default material it writes nothing (no block)
        static FlashMotion RedFlash()
        {
            var flash = new FlashMotion();
            flash.m_Params.m_Target = new Vector4(1f, 0f, 0f, 1f);
            return flash;
        }

        static MeshRenderer Renderer(string name, int materials = 1)
        {
            var go = new GameObject(name);
            go.AddComponent<MeshFilter>();
            var renderer = go.AddComponent<MeshRenderer>();
            var material = AssetDatabase.GetBuiltinExtraResource<Material>("Default-Material.mat");
            renderer.sharedMaterials = Enumerable.Repeat(material, materials).ToArray();
            return renderer;
        }

        static int MainColor(Renderer renderer)
        {
            Assert.That(MaterialColorBinding.MainColor(renderer.sharedMaterial.shader, out int id), Is.True);
            return id;
        }

        static void SetUserBlock(Renderer renderer, int id, Color color)
        {
            var block = new MaterialPropertyBlock();
            block.SetColor(id, color);
            renderer.SetPropertyBlock(block);
        }

        // the renderer's whole block color of that property — null without a block or the color
        static Color? BlockColor(Renderer renderer, int id)
        {
            if (!renderer.HasPropertyBlock())
                return null;
            var block = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(block);
            return block.HasColor(id) ? block.GetColor(id) : (Color?)null;
        }

        static int Copies() => Resources.FindObjectsOfTypeAll<Material>().Count(m => m != null && m.name.EndsWith("(Feedback)", StringComparison.Ordinal));

        static void AssertFree()
        {
            Assert.That(PreviewSession.Current, Is.Null, "the session ended");
            List<string> left = GlobalStateCheck.Left();
            Assert.That(left, Is.Empty, string.Join(" · ", left));
        }

        // the properties a snapshot saw change — their paths (a camera's hold spaces)
        static List<string> ChangedPaths(SerializedSnapshot before, params Object[] objects) => before.ChangedPaths(SerializedSnapshot.Take(objects));

        // a changed path is covered by a row, or by a recorded reference's child (the PPtr's .m_FileID)
        static List<string> Uncovered(List<string> changed, List<PreviewRecordPath> rows)
            => changed.Where(c => !rows.Any(p => c == p.Path || c.StartsWith(p.Path + ".", StringComparison.Ordinal))).ToList();

        static PreviewRecordPathTable Table => (PreviewRecordPathTable)PreviewRecordPaths.Source;

        // ---- a block's Persist goes with the preview: a session that kept it would leave the tween's block

        [Test]
        public void PX18_BlockPersistGoesWithThePreview([Values(false, true)] bool userBlock, [Values(false, true)] bool midway)
        {
            MeshRenderer renderer = Renderer("Tinted");
            int id = MainColor(renderer);
            if (userBlock)
                SetUserBlock(renderer, id, Color.blue);
            var player = renderer.gameObject.AddComponent<FeedbackPlayer>();
            Item<MaterialColorItem>(player, Tween(new Vector4(1f, 0f, 0f, 1f)), 0.3f);

            Assert.That(PreviewSession.Start(player, default), Is.EqualTo(PreviewRefusal.None));
            Seconds(0.15);
            Assert.That(BlockColor(renderer, id), Is.Not.EqualTo(userBlock ? Color.blue : (Color?)null), "the tween wrote its block");
            if (midway)
                PreviewSession.Current.Stop();
            else
            {
                Seconds(0.4);
                Assert.That(PreviewSession.LastEnd, Is.EqualTo(PreviewEndReason.AutoEnd));
            }
            if (userBlock)
                Assert.That(BlockColor(renderer, id), Is.EqualTo((Color?)Color.blue), "the user's block as it was");
            else
                Assert.That(renderer.HasPropertyBlock(), Is.False, "no block left");
            AssertFree();
        }

        // ---- the stop clears the blocks of a renderer whose property was recorded: they come back

        [Test]
        public void PX19_InstanceItemKeepsTheUsersBlock([Values("", "_EmissionColor")] string property)
        {
            MeshRenderer renderer = Renderer("Instanced");
            int id = MainColor(renderer);
            SetUserBlock(renderer, id, Color.blue);
            var player = renderer.gameObject.AddComponent<FeedbackPlayer>();
            var item = Item<MaterialColorItem>(player, Tween(new Vector4(1f, 0f, 0f, 1f)), 1f);
            item.m_WriteMode = MaterialWriteMode.Instance;
            item.m_PropertyName = property;

            PreviewSession.Start(player, default);
            Seconds(0.1);
            Assert.That(AnimationMode.IsPropertyAnimated(renderer, "m_Materials.Array.data[0]"), Is.True, "the renderer was recorded");
            // a save during it leaves the blocks alone
            Save(m_Scene);
            Assert.That(BlockColor(renderer, id), Is.EqualTo((Color?)Color.blue), "kept through the save");
            PreviewSession.Current.Stop();
            Assert.That(BlockColor(renderer, id), Is.EqualTo((Color?)Color.blue), "the user's block after the stop");
            AssertFree();
        }

        // the fallback records the renderer too (the block slot's row forgotten)
        sealed class ForgetfulSource : IPreviewRecordSource
        {
            readonly IPreviewRecordSource m_Inner;
            readonly Type m_Forgotten;

            internal ForgetfulSource(IPreviewRecordSource inner, Type forgotten)
            {
                m_Inner = inner;
                m_Forgotten = forgotten;
            }

            public bool Knows(Type slotType) => slotType != m_Forgotten && m_Inner.Knows(slotType);

            public PreviewRecordResult Get(in LayerKey key, List<PreviewRecordPath> paths)
                => key.SlotType == m_Forgotten ? PreviewRecordResult.Unknown : m_Inner.Get(in key, paths);
        }

        [Test]
        public void PX19_FallbackKeepsTheUsersBlock()
        {
            PreviewRecordPaths.Source = new ForgetfulSource(new PreviewRecordPathTable(), typeof(MaterialBlockColorSlot));
            MeshRenderer renderer = Renderer("Fallback");
            int id = MainColor(renderer);
            SetUserBlock(renderer, id, Color.blue);
            var player = renderer.gameObject.AddComponent<FeedbackPlayer>();
            Item<MaterialColorItem>(player, RedFlash(), 1f);
            LogAssert.Expect(LogType.Warning, string.Format(PreviewRecorder.FallbackWarning, nameof(MaterialBlockColorSlot)));

            PreviewSession.Start(player, default);
            Seconds(0.1);
            Assert.That(AnimationMode.IsPropertyAnimated(renderer, "m_Materials.Array.data[0]"), Is.True, "the fallback recorded the renderer");
            PreviewSession.Current.Stop();
            Assert.That(BlockColor(renderer, id), Is.EqualTo((Color?)Color.blue), "the user's block after the stop");
            AssertFree();
        }

        // a renderer first recorded after our block's write — a Rebuild's new instance item: its first record took that write,
        // and the blocks are taken again right before the stop
        [Test]
        public void PX19_RendererRecordedAfterTheBlocksWrite([Values(false, true)] bool userBlock, [Values("flash", "tween")] string motion)
        {
            MeshRenderer renderer = Renderer("Late");
            int id = MainColor(renderer);
            if (userBlock)
                SetUserBlock(renderer, id, Color.blue);
            var player = renderer.gameObject.AddComponent<FeedbackPlayer>();
            Item<MaterialColorItem>(player, motion == "flash" ? (FeedbackMotion)RedFlash() : Tween(new Vector4(1f, 0f, 0f, 1f)), 1f);

            PreviewSession.Start(player, default);
            Seconds(0.1);
            Assert.That(BlockColor(renderer, id), Is.Not.EqualTo(userBlock ? Color.blue : (Color?)null), "the item wrote its block");
            var late = Item<MaterialColorItem>(player, Tween(new Vector4(0f, 1f, 0f, 1f)), 1f);
            late.m_WriteMode = MaterialWriteMode.Instance;
            late.m_PropertyName = "_EmissionColor";
            Assert.That(player.Rebuild(), Is.True);
            Assert.That(AnimationMode.IsPropertyAnimated(renderer, "m_Materials.Array.data[0]"), Is.True, "the renderer recorded after the write");
            // the Rebuild alone plays nothing: the next tick ends the session
            Seconds(0.05);
            Assert.That(PreviewSession.LastEnd, Is.EqualTo(PreviewEndReason.AutoEnd));
            if (userBlock)
                Assert.That(BlockColor(renderer, id), Is.EqualTo((Color?)Color.blue), "the user's block as it was");
            else
                Assert.That(renderer.HasPropertyBlock(), Is.False, "no block left");
            AssertFree();
        }

        // ---- the table

        static List<Type> CoreSlotTypes()
            => typeof(LayerSlot).Assembly.GetTypes().Where(t => typeof(LayerSlot).IsAssignableFrom(t) && !t.IsAbstract).ToList();

        [Test]
        public void PX01a_EveryCoreSlotHasRowsOrNotRecorded()
        {
            List<Type> slots = CoreSlotTypes();
            Assert.That(slots, Has.Count.EqualTo(54), "the base slots, the lights' and cameras' eight, the parent slot, the materials' nine, the render globals' ten, the sprites' nine, the GameObject's three, time · custom's four and the RectTransform's three");
            Assert.That(slots.Where(t => !Table.Knows(t)).Select(t => t.Name), Is.Empty);
            // an optional assembly's slots where it compiles — rows or not recorded too (the minimal project has none)
            List<Type> optional = TypeCache.GetTypesDerivedFrom<LayerSlot>().Where(t => !t.IsAbstract && t.Assembly != typeof(LayerSlot).Assembly
                && t.Assembly.GetName().Name.StartsWith("xpTURN.Feedback.", StringComparison.Ordinal) && !t.Assembly.GetName().Name.StartsWith("xpTURN.Feedback.Tests", StringComparison.Ordinal)).ToList();
            Assert.That(optional.Where(t => !Table.Knows(t)).Select(t => t.Name), Is.Empty);
            // and their count per assembly
            Assert.That(optional.Count, Is.EqualTo((OptionalAssemblyTests.Implemented(typeof(ParticleControlItem)) ? 3 : 0) + (OptionalAssemblyTests.Implemented(typeof(AnimatorSpeedItem)) ? 6 : 0)
                + (OptionalAssemblyTests.Implemented(typeof(Light2DIntensityItem)) ? 4 : 0) + (OptionalAssemblyTests.Implemented(typeof(GraphicColorItem)) ? 15 : 0)
                + (OptionalAssemblyTests.Implemented(typeof(VideoPlayerControlItem)) ? 1 : 0) + (OptionalAssemblyTests.Implemented(typeof(TMPFontSizeItem)) ? 14 : 0)
                + (OptionalAssemblyTests.Implemented(typeof(ScreenFlashItem)) ? 2 : 0) + (OptionalAssemblyTests.Implemented(typeof(PostFXBloomItem)) ? 2 : 0)
                + (OptionalAssemblyTests.Implemented(typeof(CinemachineLensFieldOfViewItem)) ? 7 : 0) + (OptionalAssemblyTests.Implemented(typeof(GamepadRumbleItem)) ? 1 : 0)),
                "the particles' three, the animators' six, the 2D lights' four, uGUI's fifteen, the video's one, TMP's fourteen where their modules · packages are on");
        }

        [Test]
        public void PX01b_RowsOnSlotTypesOnly()
        {
            Assert.That(Table.RowTypes.Concat(Table.SkipTypes).Where(t => !typeof(LayerSlot).IsAssignableFrom(t)).Select(t => t.Name), Is.Empty);
            Assert.That(Table.RowTypes.Intersect(Table.SkipTypes), Is.Empty, "a type is either");
        }

        [Test]
        public void PX01c_IndexTemplateOpensWithTheAux()
        {
            MeshRenderer renderer = Renderer("Two", 2);
            string[] rows = Table.RowsOf(typeof(MaterialSwapSlot)).Paths;
            var all = new List<PreviewRecordPath>();
            PreviewRecordPathTable.Expand(renderer, rows, -1, all);
            var one = new List<PreviewRecordPath>();
            PreviewRecordPathTable.Expand(renderer, rows, 1, one);
            Assert.That(all.Select(p => p.Path), Is.EqualTo(new[] { "m_Materials.Array.data[0]", "m_Materials.Array.data[1]" }));
            Assert.That(one.Select(p => p.Path), Is.EqualTo(new[] { "m_Materials.Array.data[1]" }));
        }

        // a RectTransform's own rows (ParentSlot): a plain Transform has none, the recorder skips them
        static readonly string[] RectOnly =
        {
            "m_AnchorMin.x", "m_AnchorMin.y", "m_AnchorMax.x", "m_AnchorMax.y", "m_Pivot.x", "m_Pivot.y",
            "m_AnchoredPosition.x", "m_AnchoredPosition.y", "m_SizeDelta.x", "m_SizeDelta.y",
        };

        [Test]
        public void PX01d_EveryRowPassesTheRecordersJudgment()
        {
            var plain = new GameObject("Plain");
            var canvas = new GameObject("Canvas", typeof(RectTransform), typeof(Canvas));
            var ui = new GameObject("UI", typeof(RectTransform));
            ui.transform.SetParent(canvas.transform, false);
            Light light = new GameObject("Light").AddComponent<Light>();
            Camera camera = new GameObject("Camera").AddComponent<Camera>();
            MeshRenderer renderer = Renderer("Two", 2);
            SpriteRenderer sprite = new GameObject("Sprite").AddComponent<SpriteRenderer>();
            LineRenderer line = new GameObject("Line").AddComponent<LineRenderer>();
            TrailRenderer trail = new GameObject("Trail").AddComponent<TrailRenderer>();
            BoxCollider collider = new GameObject("Collider").AddComponent<BoxCollider>();
            ParticleSystem particles = new GameObject("Particles").AddComponent<ParticleSystem>();
            var failed = new List<string>();
            foreach (Type slot in Table.RowTypes)
            {
                RecordPathsAttribute rows = Table.RowsOf(slot);
                var owners = new List<Object>();
                if (rows.Owner == typeof(Transform))
                    owners.AddRange(new Object[] { plain.transform, ui.transform });
                else if (rows.Owner == typeof(Light))
                    owners.Add(light);
                else if (rows.Owner == typeof(GameObject))
                    owners.Add(plain);
                else if (rows.Owner == typeof(Renderer))
                    owners.Add(renderer);
                else if (rows.Owner == typeof(Camera))
                    owners.Add(camera);
                // the sprite · line · trail renderers
                else if (rows.Owner == typeof(SpriteRenderer))
                    owners.Add(sprite);
                else if (rows.Owner == typeof(LineRenderer))
                    owners.Add(line);
                else if (rows.Owner == typeof(TrailRenderer))
                    owners.Add(trail);
                // a Behaviour's enabled (a light) · a collider's (the row's owner is Component)
                else if (rows.Owner == typeof(Behaviour))
                    owners.Add(light);
                else if (rows.Owner == typeof(Component))
                    owners.Add(collider);
                // a particle system's speed · rate
                else if (rows.Owner == typeof(ParticleSystem))
                    owners.Add(particles);
                // URP's 2D light — a package type this assembly can't name
                else if (rows.Owner.FullName == "UnityEngine.Rendering.Universal.Light2D")
                    owners.Add(new GameObject("Light2D").AddComponent(rows.Owner));
                // Core RP's volume
                else if (rows.Owner.FullName == "UnityEngine.Rendering.Volume")
                    owners.Add(new GameObject("Volume").AddComponent(rows.Owner));
                // Cinemachine's camera · noise
                else if (rows.Owner.FullName == "Unity.Cinemachine.CinemachineCamera" || rows.Owner.FullName == "Unity.Cinemachine.CinemachineBasicMultiChannelPerlin")
                    owners.Add(new GameObject(rows.Owner.Name).AddComponent(rows.Owner));
                // uGUI's — package types this assembly can't name (an abstract Graphic takes an Image, a Selectable a Button) ·
                // the UI module's group · a RectTransform
                else if (rows.Owner.FullName == "UnityEngine.UI.Graphic")
                    owners.Add(new GameObject("Graphic", typeof(RectTransform)).AddComponent(Type.GetType("UnityEngine.UI.Image, UnityEngine.UI")));
                else if (rows.Owner.FullName == "UnityEngine.UI.Selectable")
                    owners.Add(new GameObject("Selectable", typeof(RectTransform)).AddComponent(Type.GetType("UnityEngine.UI.Button, UnityEngine.UI")));
                else if (rows.Owner.FullName == "UnityEngine.UI.Image" || rows.Owner.FullName == "UnityEngine.UI.Text")
                    owners.Add(new GameObject(rows.Owner.Name, typeof(RectTransform)).AddComponent(rows.Owner));
                // an abstract TMP text takes a UGUI one
                else if (rows.Owner.FullName == "TMPro.TMP_Text")
                    owners.Add(new GameObject("TMP", typeof(RectTransform)).AddComponent(Type.GetType("TMPro.TextMeshProUGUI, Unity.TextMeshPro")));
                else if (rows.Owner == typeof(CanvasGroup))
                    owners.Add(new GameObject("CanvasGroup").AddComponent<CanvasGroup>());
                else if (rows.Owner == typeof(RectTransform))
                    owners.Add(new GameObject("Rect", typeof(RectTransform)).transform);
                Assert.That(owners, Is.Not.Empty, slot.Name + " has a fixture");
                foreach (Object owner in owners)
                {
                    var expanded = new List<PreviewRecordPath>();
                    PreviewRecordPathTable.Expand(owner, rows.Paths, -1, expanded);
                    if (expanded.Count == 0)
                        failed.Add(slot.Name + " on " + owner.GetType().Name + ": no path");
                    failed.AddRange(expanded.Where(p => !(p.Owner is RectTransform) && RectOnly.Contains(p.Path) ? PreviewRecorder.CanRecord(p.Owner, p.Path) : !PreviewRecorder.CanRecord(p.Owner, p.Path))
                        .Select(p => slot.Name + " on " + owner.GetType().Name + ": " + p.Path));
                }
            }
            Assert.That(failed, Is.Empty);
        }

        [Test]
        public void PX01e_JudgmentRefusesWhatCannotRecord()
        {
            var plain = new GameObject("Plain");
            Assert.That(PreviewRecorder.CanRecord(plain.transform, "m_LocalEulerAnglesHint.x"), Is.False, "found but not bound");
            Assert.That(PreviewRecorder.CanRecord(plain.transform, "m_LocalPositon.x"), Is.False, "a typo");
            Assert.That(PreviewRecorder.CanRecord(plain, "m_IsActiv"), Is.False, "a GameObject's typo");
            Assert.That(PreviewRecorder.CanRecord(plain.transform, "m_LocalPosition.x"), Is.True, "the positive");
        }

        [Test]
        public void PX01f_IndexPastTheArrayOpensNothing()
        {
            MeshRenderer renderer = Renderer("Two", 2);
            var past = new List<PreviewRecordPath>();
            PreviewRecordPathTable.Expand(renderer, Table.RowsOf(typeof(MaterialSwapSlot)).Paths, 2, past);
            Assert.That(past, Is.Empty);
        }

        [Test]
        public void PX01g_KnowsAnswersAsGet()
        {
            var plain = new GameObject("Plain");
            Light light = plain.AddComponent<Light>();
            MeshRenderer renderer = Renderer("R");
            foreach (Type slot in Table.RowTypes.Concat(Table.SkipTypes))
            {
                Object target = slot == typeof(GameObjectActiveSlot) ? plain
                    : slot == typeof(LightIntensitySlot) ? light
                    : slot == typeof(MaterialBlockColorSlot) || slot == typeof(MaterialInstanceColorSlot) ? renderer
                    : (Object)plain.transform;
                var key = new LayerKey(target, slot, 0, -1);
                Assert.That(Table.Get(in key, new List<PreviewRecordPath>()) != PreviewRecordResult.Unknown, Is.EqualTo(Table.Knows(slot)), slot.Name);
            }
            Assert.That(Table.Knows(typeof(PreviewTestSlot)), Is.False, "a type without rows");
        }

        // ---- the derived fields' finder: a slot's write changes only its rows (a recorded reference's child too)

        string Finder(Type slotType, Object owner, Object[] of, Action write, int index = -1)
        {
            RecordPathsAttribute rows = Table.RowsOf(slotType);
            var expanded = new List<PreviewRecordPath>();
            if (rows != null)
                PreviewRecordPathTable.Expand(owner, rows.Paths, index, expanded);
            SerializedSnapshot before = SerializedSnapshot.Take(of);
            write();
            List<string> changed = ChangedPaths(before, of);
            if (rows == null)
                return changed.Count == 0 ? "empty" : "changed: " + string.Join(", ", changed);
            List<string> uncovered = Uncovered(changed, expanded);
            return changed.Count == 0 ? "vacuous" : uncovered.Count == 0 ? "covered" : "uncovered: " + string.Join(", ", uncovered);
        }

        // values off the current one in every component (a component left alone would let its row go missing)
        static readonly Vector3 s_Delta = new Vector3(1.25f, -2.5f, 3.75f);

        [Test]
        public void PX02a_WritesStayInTheRows([Values("position", "rotation", "scale", "light", "active", "instance",
            "range", "color", "spot", "shadow", "fov", "ortho", "clipping", "background",
            "instance texture", "block float", "block texture", "swap", "visible")] string slot)
        {
            var s = new FeedbackScheduler(ContextKind.Test);
            PropertyLayerRegistry layers = s.Context.Layers;
            try
            {
                var go = new GameObject("T");
                go.transform.localRotation = Quaternion.Euler(10f, 20f, 30f);
                Transform t = go.transform;
                string result;
                switch (slot)
                {
                    case "position":
                        result = Finder(typeof(LocalPositionSlot), t, new Object[] { t }, () => layers.GetOrCreate(t, 0, 0, () => new LocalPositionSlot(t)).WriteValue(t.localPosition + s_Delta));
                        break;
                    case "rotation":
                        result = Finder(typeof(LocalRotationSlot), t, new Object[] { t }, () => layers.GetOrCreate(t, 0, 0, () => new LocalRotationSlot(t)).WriteValue(Quaternion.Euler(11f, -23f, 37f) * t.localRotation));
                        break;
                    case "scale":
                        result = Finder(typeof(LocalScaleSlot), t, new Object[] { t }, () => layers.GetOrCreate(t, 0, 0, () => new LocalScaleSlot(t)).WriteValue(new Vector3(1.5f, 0.75f, 2.25f)));
                        break;
                    case "light":
                        Light light = go.AddComponent<Light>();
                        light.intensity = 1.25f;
                        result = Finder(typeof(LightIntensitySlot), light, new Object[] { light }, () => layers.GetOrCreate(light, 0, 0, () => new LightIntensitySlot(light)).WriteValue(2.75f));
                        break;
                    case "active":
                        result = Finder(typeof(GameObjectActiveSlot), go, new Object[] { go }, () => layers.GetOrCreate(go, 0, 0, () => new GameObjectActiveSlot(go)).WriteValue(false));
                        break;
                    // the spot's outer below its inner and a physical camera — the derived rows
                    case "range":
                    case "color":
                    case "spot":
                    case "shadow":
                        Light lamp = go.AddComponent<Light>();
                        lamp.type = LightType.Spot;
                        lamp.spotAngle = 60f;
                        lamp.innerSpotAngle = 30f;
                        lamp.shadows = LightShadows.Soft;
                        result = slot == "range" ? Finder(typeof(LightRangeSlot), lamp, new Object[] { lamp }, () => layers.GetOrCreate(lamp, 0, 0, () => new LightRangeSlot(lamp)).WriteValue(30f))
                            : slot == "color" ? Finder(typeof(LightColorSlot), lamp, new Object[] { lamp }, () => layers.GetOrCreate(lamp, 0, 0, () => new LightColorSlot(lamp)).WriteValue(new Color(2f, 0.1f, 0.2f, 0.5f)))
                            : slot == "spot" ? Finder(typeof(LightSpotAngleSlot), lamp, new Object[] { lamp }, () => layers.GetOrCreate(lamp, 0, 0, () => new LightSpotAngleSlot(lamp)).WriteValue(20f))
                            : Finder(typeof(LightShadowStrengthSlot), lamp, new Object[] { lamp }, () => layers.GetOrCreate(lamp, 0, 0, () => new LightShadowStrengthSlot(lamp)).WriteValue(0.25f));
                        break;
                    case "fov":
                    case "ortho":
                    case "clipping":
                    case "background":
                        Camera camera = go.AddComponent<Camera>();
                        camera.usePhysicalProperties = slot == "fov";
                        result = slot == "fov" ? Finder(typeof(CameraFieldOfViewSlot), camera, new Object[] { camera }, () => layers.GetOrCreate(camera, 0, 0, () => new CameraFieldOfViewSlot(camera)).WriteValue(30f))
                            : slot == "ortho" ? Finder(typeof(CameraOrthographicSizeSlot), camera, new Object[] { camera }, () => layers.GetOrCreate(camera, 0, 0, () => new CameraOrthographicSizeSlot(camera)).WriteValue(3f))
                            : slot == "clipping" ? Finder(typeof(CameraClippingPlanesSlot), camera, new Object[] { camera }, () => layers.GetOrCreate(camera, 0, 0, () => new CameraClippingPlanesSlot(camera)).WriteValue(new Vector2(2f, 50f)))
                            : Finder(typeof(CameraBackgroundColorSlot), camera, new Object[] { camera }, () => layers.GetOrCreate(camera, 0, 0, () => new CameraBackgroundColorSlot(camera)).WriteValue(new Color(1f, 0f, 0f, 1f)));
                        break;
                    // the instances' swap is the renderer's copies slot's rows · a block is not serialized (empty)
                    case "instance texture":
                    case "block float":
                    case "block texture":
                    case "swap":
                    case "visible":
                        MeshRenderer target = Renderer("R", 2);
                        int st = Shader.PropertyToID("_MainTex_ST");
                        int texture = Shader.PropertyToID("_MainTex");
                        int cutoff = Shader.PropertyToID("_Cutoff");
                        Material other = new Material(target.sharedMaterial);
                        result = slot == "instance texture" ? Finder(typeof(MaterialCopiesSlot), target, new Object[] { target }, () => layers.GetOrCreate(target, st, -1, () => new MaterialInstanceTextureSlot(target, st, -1, texture)).WriteValue(new Vector4(2f, 3f, 0.5f, 0.25f)))
                            : slot == "block float" ? Finder(typeof(MaterialBlockFloatSlot), target, new Object[] { target }, () => layers.GetOrCreate(target, cutoff, 1, () => new MaterialBlockFloatSlot(target, cutoff, 1)).WriteValue(0.75f))
                            : slot == "block texture" ? Finder(typeof(MaterialBlockTextureSlot), target, new Object[] { target }, () => layers.GetOrCreate(target, st, -1, () => new MaterialBlockTextureSlot(target, st, -1)).WriteValue(new Vector4(2f, 3f, 0.5f, 0.25f)))
                            : slot == "swap" ? Finder(typeof(MaterialSwapSlot), target, new Object[] { target }, () => layers.GetOrCreate(target, 0, 1, () => new MaterialSwapSlot(target, 1)).WriteMaterial(other), 1)
                            : Finder(typeof(RendererEnabledSlot), target, new Object[] { target }, () => layers.GetOrCreate(target, 0, 0, () => new RendererEnabledSlot(target)).WriteValue(false));
                        Object.DestroyImmediate(other);
                        break;
                    default:
                        MeshRenderer renderer = Renderer("R");
                        int id = MainColor(renderer);
                        result = Finder(typeof(MaterialCopiesSlot), renderer, new Object[] { renderer }, () => layers.GetOrCreate(renderer, id, -1, () => new MaterialInstanceColorSlot(renderer, id, -1)).WriteValue(Color.red));
                        break;
                }
                Assert.That(result, Is.EqualTo(slot.StartsWith("block", StringComparison.Ordinal) ? "empty" : "covered"));
            }
            finally
            {
                s.Shutdown();
            }
        }

        // the negative: the same check on a row short of a path is red
        [Test]
        public void PX02b_ARowShortOfAPathIsRed()
        {
            var s = new FeedbackScheduler(ContextKind.Test);
            try
            {
                Transform t = new GameObject("T").transform;
                var shortRow = new List<PreviewRecordPath> { new PreviewRecordPath(t, "m_LocalPosition.x"), new PreviewRecordPath(t, "m_LocalPosition.y") };
                SerializedSnapshot before = SerializedSnapshot.Take(t);
                s.Context.Layers.GetOrCreate(t, 0, 0, () => new LocalPositionSlot(t)).WriteValue(t.localPosition + s_Delta);
                Assert.That(Uncovered(ChangedPaths(before, t), shortRow), Is.EqualTo(new[] { "m_LocalPosition.z" }));
            }
            finally
            {
                s.Shutdown();
            }
        }

        // the derived rows are asked for — the focal length a physical camera's field of view writes, the inner angle a spot
        // angle below it pushes. A camera's paths hold spaces
        [Test]
        public void PX02e_TheDerivedRowsAreAsked()
        {
            var s = new FeedbackScheduler(ContextKind.Test);
            try
            {
                Camera camera = new GameObject("Physical").AddComponent<Camera>();
                camera.usePhysicalProperties = true;
                var fovRow = new List<PreviewRecordPath> { new PreviewRecordPath(camera, "field of view") };
                SerializedSnapshot before = SerializedSnapshot.Take(camera);
                s.Context.Layers.GetOrCreate(camera, 0, 0, () => new CameraFieldOfViewSlot(camera)).WriteValue(30f);
                Assert.That(Uncovered(ChangedPaths(before, camera), fovRow), Is.EqualTo(new[] { "m_FocalLength" }));

                Light light = new GameObject("Spot").AddComponent<Light>();
                light.type = LightType.Spot;
                light.spotAngle = 60f;
                light.innerSpotAngle = 30f;
                var spotRow = new List<PreviewRecordPath> { new PreviewRecordPath(light, "m_SpotAngle") };
                before = SerializedSnapshot.Take(light);
                s.Context.Layers.GetOrCreate(light, 0, 0, () => new LightSpotAngleSlot(light)).WriteValue(20f);
                Assert.That(Uncovered(ChangedPaths(before, light), spotRow), Is.EqualTo(new[] { "m_InnerSpotAngle" }));
            }
            finally
            {
                s.Shutdown();
            }
        }

        // the block is not recorded: its write changes nothing serialized — and wrote a block (the positive)
        [Test]
        public void PX02c_BlockChangesNothingSerialized()
        {
            var s = new FeedbackScheduler(ContextKind.Test);
            try
            {
                MeshRenderer renderer = Renderer("R");
                int id = MainColor(renderer);
                string result = Finder(typeof(MaterialBlockColorSlot), renderer, new Object[] { renderer }, () => s.Context.Layers.GetOrCreate(renderer, id, -1, () => new MaterialBlockColorSlot(renderer, id, -1)).WriteValue(Color.green));
                Assert.That(result, Is.EqualTo("empty"));
                Assert.That(renderer.HasPropertyBlock(), Is.True);
            }
            finally
            {
                s.Shutdown();
            }
        }

        // a RectTransform takes Transform's rows
        [Test]
        public void PX02d_RectTransformPosition([Values(false, true)] bool stretched)
        {
            var s = new FeedbackScheduler(ContextKind.Test);
            try
            {
                var canvas = new GameObject("Canvas", typeof(RectTransform), typeof(Canvas));
                var ui = new GameObject("UI", typeof(RectTransform));
                ui.transform.SetParent(canvas.transform, false);
                var rt = (RectTransform)ui.transform;
                if (stretched)
                {
                    rt.anchorMin = new Vector2(0.1f, 0.2f);
                    rt.anchorMax = new Vector2(0.7f, 0.9f);
                }
                rt.anchoredPosition = new Vector2(10f, 20f);
                string result = Finder(typeof(LocalPositionSlot), rt, new Object[] { rt }, () => s.Context.Layers.GetOrCreate(rt, 0, 0, () => new LocalPositionSlot(rt)).WriteValue(rt.localPosition + s_Delta));
                Assert.That(result, Is.EqualTo("covered"));
            }
            finally
            {
                s.Shutdown();
            }
        }

        // ---- each base target kind: the save during the preview is the baseline, the snapshot after the stop is the one
        // before, nothing left

        void RoundTrip(FeedbackPlayer player, Object[] snapshotOf, bool serializedChange = true, Func<bool> during = null)
        {
            Save(m_Scene);
            SceneFileCheck file = SceneFileCheck.ForScene(m_Scene);
            SerializedSnapshot before = SerializedSnapshot.Take(snapshotOf);
            Assert.That(PreviewSession.Start(player, default), Is.EqualTo(PreviewRefusal.None));
            Seconds(0.27);
            if (serializedChange)
                Assert.That(before.Differences(SerializedSnapshot.Take(snapshotOf)), Is.Not.Empty, "the preview's values at the save — the positive");
            if (during != null)
                Assert.That(during(), Is.True, "the preview at the save — the positive");
            Assert.That(file.SaveAndCompare(m_Scene), Is.Empty, "the save during the preview");
            PreviewSession.Current.Stop();
            List<string> after = before.Differences(SerializedSnapshot.Take(snapshotOf));
            Assert.That(after, Is.Empty, string.Join(" · ", after));
            AssertFree();
        }

        [Test]
        public void PX03a_Position()
        {
            var go = new GameObject("Shaken");
            go.transform.localPosition = s_Home;
            var player = go.AddComponent<FeedbackPlayer>();
            Item<PositionItem>(player, new ShakeMotion(), 1f);
            RoundTrip(player, new Object[] { go.transform });
        }

        [Test]
        public void PX03b_RectTransformPosition()
        {
            var canvas = new GameObject("Canvas", typeof(RectTransform), typeof(Canvas));
            var ui = new GameObject("UI", typeof(RectTransform));
            ui.transform.SetParent(canvas.transform, false);
            ((RectTransform)ui.transform).anchoredPosition = new Vector2(10f, 20f);
            var player = ui.AddComponent<FeedbackPlayer>();
            Item<PositionItem>(player, new ShakeMotion(), 1f);
            RoundTrip(player, new Object[] { ui.transform });
        }

        [Test]
        public void PX03c_LightIntensity()
        {
            var go = new GameObject("Lit");
            Light light = go.AddComponent<Light>();
            light.intensity = 1.25f;
            var player = go.AddComponent<FeedbackPlayer>();
            Item<LightIntensityItem>(player, Tween(new Vector4(3f, 0f, 0f, 0f)), 1f);
            RoundTrip(player, new Object[] { light });
        }

        // Set's and Blink's value is on by default: 0 turns it off
        [Test]
        public void PX03d_GameObjectActive()
        {
            var go = new GameObject("Blinker");
            var target = new GameObject("Blinked");
            var player = go.AddComponent<FeedbackPlayer>();
            var blink = new BlinkMotion();
            blink.m_Params.m_Value = Vector4.zero;
            ItemOn<SetActiveItem>(player, target, blink, 1f);
            RoundTrip(player, new Object[] { target });
        }

        [Test]
        public void PX03e_Block()
        {
            MeshRenderer renderer = Renderer("Flashed");
            var player = renderer.gameObject.AddComponent<FeedbackPlayer>();
            Item<MaterialColorItem>(player, RedFlash(), 1f);
            RoundTrip(player, new Object[] { renderer }, false, renderer.HasPropertyBlock);
            Assert.That(renderer.HasPropertyBlock(), Is.False, "the block went with the end");
        }

        // the probe's camera is the main camera — the editor's untitled scene can hold one tagged MainCamera too
        [Test]
        public void PX03f_MainCameraTransform()
        {
            var untagged = new List<GameObject>();
            foreach (Camera other in Camera.allCameras)
            {
                if (other.CompareTag("MainCamera") && other.gameObject.scene != m_Scene)
                {
                    other.gameObject.tag = "Untagged";
                    untagged.Add(other.gameObject);
                }
            }
            try
            {
                var cam = new GameObject("Cam");
                cam.tag = "MainCamera";
                cam.AddComponent<Camera>();
                cam.transform.position = new Vector3(0f, 1f, -10f);
                Assert.That(Camera.main, Is.SameAs(cam.GetComponent<Camera>()));
                var player = new GameObject("Shaker").AddComponent<FeedbackPlayer>();
                player.m_Items.Add(new CameraShakeItem { m_Motion = new ShakeMotion(), m_Timing = new ItemTiming { m_Duration = 1f } });
                RoundTrip(player, new Object[] { cam.transform });
            }
            finally
            {
                foreach (GameObject o in untagged)
                {
                    if (o != null)
                        o.tag = "MainCamera";
                }
            }
        }

        // a spot light's four — the outer angle goes below the inner one, which comes back with the stop only through its row
        [Test]
        public void PX03i_LightValues()
        {
            var go = new GameObject("Spot");
            Light light = go.AddComponent<Light>();
            light.type = LightType.Spot;
            light.spotAngle = 60f;
            light.innerSpotAngle = 30f;
            light.shadows = LightShadows.Soft;
            var player = go.AddComponent<FeedbackPlayer>();
            Item<LightRangeItem>(player, Tween(new Vector4(30f, 0f, 0f, 0f)), 1f);
            Item<LightColorItem>(player, Tween(new Vector4(2f, 0.1f, -0.5f, 1f)), 1f);
            Item<LightSpotAngleItem>(player, Tween(new Vector4(10f, 0f, 0f, 0f)), 0.2f);
            Item<LightShadowStrengthItem>(player, Tween(new Vector4(0.25f, 0f, 0f, 0f)), 1f);
            RoundTrip(player, new Object[] { light }, true, () => light.innerSpotAngle < 30f);
        }

        // a physical camera's field of view (its focal length moves), the clipping planes and the background; a zoom on an
        // orthographic camera
        [Test]
        public void PX03j_CameraValues()
        {
            Camera physical = new GameObject("Physical").AddComponent<Camera>();
            physical.usePhysicalProperties = true;
            float focal = physical.focalLength;
            Camera orthographic = new GameObject("Orthographic").AddComponent<Camera>();
            orthographic.orthographic = true;
            var player = new GameObject("Player").AddComponent<FeedbackPlayer>();
            ItemOn<CameraFieldOfViewItem>(player, physical, Tween(new Vector4(30f, 0f, 0f, 0f)), 1f);
            ItemOn<CameraClippingPlanesItem>(player, physical, Tween(new Vector4(2f, 50f, 0f, 0f)), 1f);
            ItemOn<CameraBackgroundColorItem>(player, physical, Tween(new Vector4(2f, 0f, 0f, 1f)), 1f);
            ItemOn<CameraZoomItem>(player, orthographic, Tween(new Vector4(1f, 0f, 0f, 0f)), 1f);
            RoundTrip(player, new Object[] { physical, orthographic }, true, () => physical.focalLength != focal);
        }

        // the scene stays clean after the preview, and the user's edit before it is undone by one step
        [UnityTest]
        public IEnumerator PX03g_SceneCleanAndUndoKept([Values("position", "light", "active", "block", "instance", "camera", "parent",
            "swap", "visibility", "copies", "fog", "skybox", "shader global", "sprite", "line", "trail", "component", "layer", "spawn", "despawn",
            "particles", "particle spawn", "animator", "physics", "time custom")] string kind)
        {
            var other = new GameObject("Other");
            FeedbackPlayer player;
            switch (kind)
            {
                case "position":
                    player = new GameObject("Shaken").AddComponent<FeedbackPlayer>();
                    Item<PositionItem>(player, new ShakeMotion(), 1f);
                    break;
                case "light":
                    var lit = new GameObject("Lit");
                    lit.AddComponent<Light>().intensity = 1.25f;
                    player = lit.AddComponent<FeedbackPlayer>();
                    Item<LightIntensityItem>(player, Tween(new Vector4(3f, 0f, 0f, 0f)), 1f);
                    break;
                case "active":
                    player = new GameObject("Blinker").AddComponent<FeedbackPlayer>();
                    var blink = new BlinkMotion();
                    blink.m_Params.m_Value = Vector4.zero;
                    ItemOn<SetActiveItem>(player, new GameObject("Blinked"), blink, 1f);
                    break;
                case "block":
                    player = Renderer("Flashed").gameObject.AddComponent<FeedbackPlayer>();
                    Item<MaterialColorItem>(player, Tween(new Vector4(1f, 0f, 0f, 1f)), 1f);
                    break;
                // the hierarchy kind — a Set Parent, a tween keeps the session
                case "parent":
                    player = new GameObject("Parenting").AddComponent<FeedbackPlayer>();
                    var moved = new GameObject("Moved");
                    moved.transform.SetParent(new GameObject("From").transform, false);
                    ItemOn<SetParentItem>(player, moved, new SetMotion(), 0f).m_NewParent = Direct(new GameObject("To").transform);
                    ItemOn<PositionItem>(player, new GameObject("Keeper"), Tween(new Vector4(0f, 4f, 0f, 0f)), 1f);
                    break;
                case "camera":
                    var lens = new GameObject("Lens");
                    lens.AddComponent<Camera>().usePhysicalProperties = true;
                    player = lens.AddComponent<FeedbackPlayer>();
                    Item<CameraFieldOfViewItem>(player, Tween(new Vector4(30f, 0f, 0f, 0f)), 1f);
                    Item<CameraClippingPlanesItem>(player, Tween(new Vector4(2f, 50f, 0f, 0f)), 1f);
                    Item<CameraBackgroundColorItem>(player, Tween(new Vector4(1f, 0f, 0f, 1f)), 1f);
                    break;
                // the scene's RenderSettings (the fog · its skybox's copy) · a shader global
                case "fog":
                case "skybox":
                case "shader global":
                    player = new GameObject("Global").AddComponent<FeedbackPlayer>();
                    if (kind == "fog")
                    {
                        Item<FogColorItem>(player, Tween(new Vector4(1f, 0f, 0f, 1f)), 1f);
                        Item<FogDensityItem>(player, Tween(new Vector4(0.2f, 0f, 0f, 0f)), 1f);
                    }
                    else if (kind == "skybox")
                    {
                        RenderSettings.skybox = AssetDatabase.GetBuiltinExtraResource<Material>("Default-Skybox.mat");
                        Item<SkyboxExposureItem>(player, Tween(new Vector4(3f, 0f, 0f, 0f)), 1f);
                    }
                    else
                        Item<ShaderGlobalFloatItem>(player, Tween(new Vector4(3f, 0f, 0f, 0f)), 1f).m_PropertyName = "_PX03gGlobal";
                    ItemOn<PositionItem>(player, new GameObject("Keeper"), Tween(new Vector4(0f, 4f, 0f, 0f)), 1f);
                    break;
                // a material swapped · a renderer turned off · the copies of two properties
                case "swap":
                case "visibility":
                case "copies":
                    MeshRenderer target = Renderer("Rendered", 2);
                    player = target.gameObject.AddComponent<FeedbackPlayer>();
                    if (kind == "swap")
                        Item<MaterialSwapItem>(player, new SetMotion(), 0f).m_Material = AssetDatabase.GetBuiltinExtraResource<Material>("Sprites-Default.mat");
                    else if (kind == "visibility")
                        Item<RendererVisibilityItem>(player, new SetMotion(), 0f);
                    else
                    {
                        Item<MaterialColorItem>(player, Tween(new Vector4(1f, 0f, 0f, 1f)), 1f).m_WriteMode = MaterialWriteMode.Instance;
                        MaterialFloatItem cutoff = Item<MaterialFloatItem>(player, Tween(new Vector4(0.9f, 0f, 0f, 0f)), 1f);
                        cutoff.m_PropertyName = "_Cutoff";
                        cutoff.m_WriteMode = MaterialWriteMode.Instance;
                    }
                    ItemOn<PositionItem>(player, new GameObject("Keeper"), Tween(new Vector4(0f, 4f, 0f, 0f)), 1f);
                    break;
                // a sprite's color · sprite · flip, a line's color (the save state) · width, a trail's time · emitting · clear
                case "sprite":
                    var drawn = new GameObject("Sprite").AddComponent<SpriteRenderer>();
                    drawn.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
                    player = drawn.gameObject.AddComponent<FeedbackPlayer>();
                    Item<SpriteColorItem>(player, Tween(new Vector4(1f, 0f, 0f, 1f)), 1f);
                    Item<SpriteSwapItem>(player, new SetMotion(), 0f).m_Sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Background.psd");
                    Item<SpriteFlipItem>(player, new SetMotion { m_Params = new SetParams { m_Value = Vector4.one } }, 0f);
                    ItemOn<PositionItem>(player, new GameObject("Keeper"), Tween(new Vector4(0f, 4f, 0f, 0f)), 1f);
                    break;
                case "line":
                    player = new GameObject("Line").AddComponent<LineRenderer>().gameObject.AddComponent<FeedbackPlayer>();
                    Item<LineColorItem>(player, Tween(new Vector4(1f, 0f, 0f, 1f)), 1f);
                    Item<LineWidthItem>(player, Tween(new Vector4(2f, 0f, 0f, 0f)), 1f);
                    break;
                case "trail":
                    player = new GameObject("Trail").AddComponent<TrailRenderer>().gameObject.AddComponent<FeedbackPlayer>();
                    Item<TrailTimeItem>(player, Tween(new Vector4(2f, 0f, 0f, 0f)), 1f);
                    Item<TrailEmittingItem>(player, new SetMotion(), 0f);
                    Item<TrailClearItem>(player, new SetMotion(), 0f);
                    break;
                // a component turned off · a layer set · spawns (a parent Target's in the scene) · a despawn's hide
                case "component":
                    player = new GameObject("Turned").AddComponent<AudioSource>().gameObject.AddComponent<FeedbackPlayer>();
                    Item<ComponentEnabledItem>(player, new SetMotion(), 0f).m_ComponentType = "UnityEngine.AudioSource";
                    ItemOn<PositionItem>(player, new GameObject("Keeper"), Tween(new Vector4(0f, 4f, 0f, 0f)), 1f);
                    break;
                case "layer":
                    player = new GameObject("Layered").AddComponent<FeedbackPlayer>();
                    Item<SetLayerItem>(player, new SetMotion(), 0f).m_Layer = 5;
                    ItemOn<PositionItem>(player, new GameObject("Keeper"), Tween(new Vector4(0f, 4f, 0f, 0f)), 1f);
                    break;
                case "spawn":
                    player = new GameObject("Spawner").AddComponent<FeedbackPlayer>();
                    var template = new GameObject("Template");
                    template.AddComponent<MeshFilter>();
                    Item<SpawnItem>(player, new SetMotion(), 0f).m_Prefab = template;
                    SpawnItem under = Item<SpawnItem>(player, new SetMotion(), 0f);
                    under.m_Prefab = template;
                    under.m_Parent = SpawnParent.Target;
                    ItemOn<PositionItem>(player, new GameObject("Keeper"), Tween(new Vector4(0f, 4f, 0f, 0f)), 1f);
                    break;
                case "despawn":
                    player = new GameObject("Despawner").AddComponent<FeedbackPlayer>();
                    ItemOn<DespawnItem>(player, new GameObject("Despawned"), new SetMotion(), 0f);
                    ItemOn<PositionItem>(player, new GameObject("Keeper"), Tween(new Vector4(0f, 4f, 0f, 0f)), 1f);
                    break;
                // the simulated kinds — a system's play state, a burst, its speed and rate · a particle spawn (the minimal project
                // skips them — no implementation)
                case "particles":
                {
                    var system = new GameObject("System");
                    ParticleSystem.MainModule main = system.AddComponent<ParticleSystem>().main;
                    main.playOnAwake = false;
                    player = new GameObject("Simulator").AddComponent<FeedbackPlayer>();
                    ItemOn<ParticleControlItem>(player, system.GetComponent<ParticleSystem>(), new SetMotion(), 0f);
                    ItemOn<ParticleEmitItem>(player, system.GetComponent<ParticleSystem>(), new SetMotion(), 0f);
                    ItemOn<ParticleSimulationSpeedItem>(player, system.GetComponent<ParticleSystem>(), Tween(new Vector4(2f, 0f, 0f, 0f)), 1f);
                    ItemOn<ParticleEmissionRateItem>(player, system.GetComponent<ParticleSystem>(), Tween(new Vector4(40f, 0f, 0f, 0f)), 1f);
                    break;
                }
                // the animator's five — a parameter, a float, its speed, a layer's weight, a state; the clips' transforms recorded
                // (the minimal project skips them — no implementation)
                case "animator":
                {
                    var rig = new GameObject("Animated");
                    new GameObject("Child").transform.SetParent(rig.transform, false);
                    var animator = rig.AddComponent<Animator>();
                    animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                    animator.runtimeAnimatorController = FeedbackTestAnimator.Make(ControllerPath);
                    player = new GameObject("Animator").AddComponent<FeedbackPlayer>();
                    AnimatorParameterItem on = ItemOn<AnimatorParameterItem>(player, animator, new SetMotion(), 0f);
                    on.m_Parameter = "On";
                    on.m_Kind = AnimatorParameterKind.Bool;
                    ItemOn<AnimatorFloatItem>(player, animator, Tween(new Vector4(1f, 0f, 0f, 0f)), 1f).m_Parameter = "Speed";
                    ItemOn<AnimatorSpeedItem>(player, animator, Tween(new Vector4(2f, 0f, 0f, 0f)), 1f);
                    ItemOn<AnimatorLayerWeightItem>(player, animator, Tween(new Vector4(1f, 0f, 0f, 0f)), 1f).m_Layer = "Upper";
                    ItemOn<AnimatorStateItem>(player, animator, new SetMotion(), 0f).m_State = "Other";
                    break;
                }
                // the five display-only items mark and write nothing — a body, a 2D body, an explosion around a body
                case "physics":
                {
                    var body = new GameObject("Body");
                    body.AddComponent<SphereCollider>();
                    Rigidbody rb = body.AddComponent<Rigidbody>();
                    rb.useGravity = false;
                    var body2 = new GameObject("Body2D");
                    Rigidbody2D rb2 = body2.AddComponent<Rigidbody2D>();
                    rb2.gravityScale = 0f;
                    player = new GameObject("Physics").AddComponent<FeedbackPlayer>();
                    ItemOn<RigidbodyForceItem>(player, rb, new SetMotion(), 0f);
                    ItemOn<RigidbodyVelocityItem>(player, rb, new SetMotion(), 0f);
                    ItemOn<RigidbodyExplosionItem>(player, body.transform, new SetMotion(), 0f);
                    ItemOn<Rigidbody2DForceItem>(player, rb2, new SetMotion(), 0f);
                    ItemOn<Rigidbody2DVelocityItem>(player, rb2, new SetMotion(), 0f);
                    // the instants alone end the session at once where the modules are off
                    ItemOn<PositionItem>(player, new GameObject("Keeper"), Tween(new Vector4(0f, 4f, 0f, 0f)), 1f);
                    break;
                }
                // the time scale writes no time value · a user target's tween and punch come back (the save state) · a float event
                // is skipped
                case "time custom":
                {
                    var user = new GameObject("Custom").AddComponent<FeedbackTestTarget>();
                    user.m_Float = 2f;
                    player = new GameObject("TimeCustom").AddComponent<FeedbackPlayer>();
                    Item<TimeScaleItem>(player, Tween(new Vector4(0.5f, 0f, 0f, 0f)), 1f);
                    ItemOn<CustomFloatItem>(player, user, Tween(new Vector4(5f, 0f, 0f, 0f)), 1f);
                    ItemOn<CustomVector3Item>(player, user, new PunchMotion(), 1f);
                    Item<CustomFloatEventItem>(player, Tween(Vector4.one), 1f);
                    break;
                }
                case "particle spawn":
                {
                    player = new GameObject("ParticleSpawner").AddComponent<FeedbackPlayer>();
                    var burst = new GameObject("Burst");
                    ParticleSystem.MainModule main = burst.AddComponent<ParticleSystem>().main;
                    main.playOnAwake = false;
                    main.loop = false;
                    burst.SetActive(false);
                    Item<ParticleSpawnItem>(player, new SetMotion(), 0f).m_Prefab = burst;
                    ItemOn<PositionItem>(player, new GameObject("Keeper"), Tween(new Vector4(0f, 4f, 0f, 0f)), 1f);
                    break;
                }
                default:
                    player = Renderer("Instanced").gameObject.AddComponent<FeedbackPlayer>();
                    Item<MaterialColorItem>(player, Tween(new Vector4(1f, 0f, 0f, 1f)), 1f).m_WriteMode = MaterialWriteMode.Instance;
                    break;
            }
            UndoCheck undo = UndoCheck.Edit(other.transform, "m_LocalPosition.x", p => p.floatValue = 7f);
            // an edit applied with Undo marks its scene two updates later
            yield return null;
            yield return null;
            Save(m_Scene);
            Assert.That(DirtyCheck.Dirty(m_Scene), Is.Empty, "clean before");

            Assert.That(PreviewSession.Start(player, default), Is.EqualTo(PreviewRefusal.None));
            Seconds(0.27);
            PreviewSession.Current.Stop();
            yield return null;
            yield return null;
            Assert.That(DirtyCheck.Dirty(m_Scene), Is.Empty, "clean after the preview");
            Assert.That(undo.GroupKept, Is.True, "no undo group moved");
            Undo.PerformUndo();
            Assert.That(other.transform.localPosition.x, Is.EqualTo(0f), "one undo takes the user's edit back");
            AssertFree();
        }

        // a prefab stage's auto save while the preview runs: the file differs by the user's own edit only
        [UnityTest]
        public IEnumerator PX03h_PrefabStageAutoSave()
        {
            var root = new GameObject("Root");
            var lit = new GameObject("Lit");
            lit.transform.SetParent(root.transform, false);
            lit.AddComponent<Light>().intensity = 1.25f;
            Type urpLight = Type.GetType("UnityEngine.Rendering.Universal.UniversalAdditionalLightData, Unity.RenderPipelines.Universal.Runtime");
            if (urpLight != null)
                lit.AddComponent(urpLight);
            MeshRenderer flashed = Renderer("Flashed");
            flashed.transform.SetParent(root.transform, false);
            MeshRenderer instanced = Renderer("Instanced");
            instanced.transform.SetParent(root.transform, false);
            var shaken = new GameObject("Shaken");
            shaken.transform.SetParent(root.transform, false);
            var other = new GameObject("Other");
            other.transform.SetParent(root.transform, false);
            var player = root.AddComponent<FeedbackPlayer>();
            ItemOn<LightIntensityItem>(player, lit.GetComponent<Light>(), Tween(new Vector4(3f, 0f, 0f, 0f)), 1f);
            ItemOn<MaterialColorItem>(player, flashed, RedFlash(), 1f);
            ItemOn<MaterialColorItem>(player, instanced, Tween(new Vector4(1f, 0f, 0f, 1f)), 1f).m_WriteMode = MaterialWriteMode.Instance;
            ItemOn<PositionItem>(player, shaken.transform, new ShakeMotion(), 1f);
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);

            PrefabStage stage = PrefabStageUtility.OpenPrefab(PrefabPath);
            bool autoSave = SceneFileCheck.AutoSave(stage, true);
            try
            {
                yield return SceneFileCheck.WaitForAutoSave();
                SceneFileCheck file = SceneFileCheck.ForPrefab(PrefabPath);
                FeedbackPlayer staged = stage.prefabContentsRoot.GetComponent<FeedbackPlayer>();
                Assert.That(PreviewSession.Start(staged, default), Is.EqualTo(PreviewRefusal.None));
                Seconds(0.27);
                Transform stagedOther = stage.prefabContentsRoot.transform.Find("Other");
                UserEdit.Inspector(stagedOther, "m_LocalScale.x", p => p.floatValue = 2f);
                yield return SceneFileCheck.WaitForAutoSave(1.0);
                List<string> differences = file.Differences();
                Assert.That(PreviewSession.Current, Is.Not.Null, "the preview goes on through the auto save");
                Assert.That(differences, Has.Count.EqualTo(1), string.Join(" | ", differences));
                Assert.That(differences[0], Does.Contain("m_LocalScale: {x: 2,"));
                PreviewSession.Current.Stop();
                Assert.That(Copies(), Is.EqualTo(0), "no copy left");
                AssertFree();
            }
            finally
            {
                if (StageUtility.GetCurrentStage() is PrefabStage open)
                {
                    SceneFileCheck.AutoSave(open, autoSave);
                    StageUtility.GoToMainStage();
                }
            }
        }

        // ---- the block (an Offset — the layer puts it back; a Persist's block goes with the preview)

        [Test]
        public void PX04a_FlashLeavesNoBlock()
        {
            MeshRenderer renderer = Renderer("Flashed");
            var player = renderer.gameObject.AddComponent<FeedbackPlayer>();
            Item<MaterialColorItem>(player, RedFlash(), 0.3f);
            PreviewSession.Start(player, default);
            // a flash has its off phases — the block is written while it is on
            bool any = false;
            for (int i = 0; i < 18; i++)
            {
                Pump(1);
                any |= renderer.HasPropertyBlock();
            }
            Assert.That(any, Is.True, "the flash's block");
            PreviewSession.Current.Stop();
            Assert.That(renderer.HasPropertyBlock(), Is.False);
            AssertFree();
        }

        [Test]
        public void PX04b_FlashKeepsTheUsersBlock()
        {
            MeshRenderer renderer = Renderer("Flashed");
            int id = MainColor(renderer);
            SetUserBlock(renderer, id, Color.blue);
            var player = renderer.gameObject.AddComponent<FeedbackPlayer>();
            Item<MaterialColorItem>(player, RedFlash(), 0.3f);
            PreviewSession.Start(player, default);
            Seconds(0.15);
            PreviewSession.Current.Stop();
            Assert.That(BlockColor(renderer, id), Is.EqualTo((Color?)Color.blue));
            AssertFree();
        }

        // ---- the instance item: the slot as it is, its copies hidden in a preview

        [Test]
        public void PX05a_InstanceCopiesHiddenAndNotSaved()
        {
            MeshRenderer renderer = Renderer("Instanced");
            var player = renderer.gameObject.AddComponent<FeedbackPlayer>();
            Item<MaterialColorItem>(player, Tween(new Vector4(1f, 0f, 0f, 1f)), 1f).m_WriteMode = MaterialWriteMode.Instance;
            Save(m_Scene);
            SceneFileCheck file = SceneFileCheck.ForScene(m_Scene);
            PreviewSession.Start(player, default);
            Seconds(0.1);
            Assert.That(renderer.sharedMaterial.name, Does.EndWith("(Feedback)"), "the copy is in");
            Assert.That(renderer.sharedMaterial.hideFlags, Is.EqualTo(HideFlags.HideAndDontSave));
            Assert.That(file.SaveAndCompare(m_Scene), Is.Empty, "the save during the preview");
        }

        [Test]
        public void PX05b_InstanceOriginalAfterTheStop()
        {
            MeshRenderer renderer = Renderer("Instanced");
            Material original = renderer.sharedMaterial;
            var player = renderer.gameObject.AddComponent<FeedbackPlayer>();
            Item<MaterialColorItem>(player, Tween(new Vector4(1f, 0f, 0f, 1f)), 1f).m_WriteMode = MaterialWriteMode.Instance;
            PreviewSession.Start(player, default);
            Seconds(0.1);
            PreviewSession.Current.Stop();
            Assert.That(renderer.sharedMaterial, Is.SameAs(original));
            Assert.That(Copies(), Is.EqualTo(0));
            AssertFree();
        }

        // the branch is the preview's only: a direct tick of the Test kind keeps the copy as the runtime does
        [Test]
        public void PX05c_TestKindCopiesNotHidden()
        {
            MeshRenderer renderer = Renderer("Instanced");
            var player = renderer.gameObject.AddComponent<FeedbackPlayer>();
            Item<MaterialColorItem>(player, Tween(new Vector4(1f, 0f, 0f, 1f)), 1f).m_WriteMode = MaterialWriteMode.Instance;
            var s = new FeedbackScheduler(ContextKind.Test);
            player.Bind(s);
            try
            {
                player.Play();
                s.Tick(0.05f);
                Assert.That(renderer.sharedMaterial.name, Does.EndWith("(Feedback)"));
                Assert.That(renderer.sharedMaterial.hideFlags, Is.EqualTo(HideFlags.None));
            }
            finally
            {
                s.Shutdown();
                player.ReleaseRuntime();
                player.Bind(null);
            }
        }

        // ---- the transform targets — the four ride the transform slots' rows; Set Parent's slot records the local values and
        // its save state keeps the parent

        // a parent write changes its rows and the paths the state keeps: the target's m_Father, the parents' m_Children
        [Test]
        public void PX02f_TheParentRowsAndTheStatesPaths([Values(false, true)] bool rect)
        {
            var s = new FeedbackScheduler(ContextKind.Test);
            try
            {
                var p1 = new GameObject("P1", typeof(RectTransform));
                p1.transform.SetPositionAndRotation(new Vector3(5f, 0f, 0f), Quaternion.Euler(0f, 30f, 0f));
                var p2 = new GameObject("P2", typeof(RectTransform));
                p2.transform.localScale = new Vector3(2f, 2f, 2f);
                GameObject go = rect ? new GameObject("T", typeof(RectTransform)) : new GameObject("T");
                Transform t = go.transform;
                t.SetParent(p1.transform, false);
                if (t is RectTransform r)
                {
                    r.anchorMin = new Vector2(0.1f, 0.2f);
                    r.anchorMax = new Vector2(0.7f, 0.9f);
                    r.sizeDelta = new Vector2(-40f, 25f);
                }
                t.localPosition = new Vector3(1f, 2f, 3f);
                var rows = new List<PreviewRecordPath>();
                PreviewRecordPathTable.Expand(t, Table.RowsOf(typeof(ParentSlot)).Paths, -1, rows);
                SerializedSnapshot before = SerializedSnapshot.Take(t);
                SerializedSnapshot parents = SerializedSnapshot.Take(p1.transform, p2.transform);
                s.Context.Layers.GetOrCreate(t, 0, 0, () => new ParentSlot(t)).WriteParent(p2.transform, true);
                List<string> changed = ChangedPaths(before, t);
                Assert.That(changed.Count, Is.GreaterThan(1), "the local values moved — the positive");
                // the reference and its children (the PPtr's .m_FileID · .m_PathID)
                List<string> uncovered = Uncovered(changed, rows);
                Assert.That(uncovered, Does.Contain("m_Father"), "the state's own");
                Assert.That(uncovered.Where(p => !p.StartsWith("m_Father.", StringComparison.Ordinal) && p != "m_Father"), Is.Empty, "the state's own only");
                // a child moved changes the parents' layout — the snapshot stops at the first difference, the children's
                Assert.That(parents.Differences(SerializedSnapshot.Take(p1.transform, p2.transform)).Where(l => !l.Contains("m_Children")), Is.Empty, "the parents' children only");
            }
            finally
            {
                s.Shutdown();
            }
        }

        // the four's Persist and contributions over the transform rows — the save is the baseline, the stop gives the start
        [Test]
        public void PX03k_TransformTargets()
        {
            var squashed = new GameObject("Squashed");
            var looking = new GameObject("Looking");
            var orbiting = new GameObject("Orbiting");
            orbiting.transform.position = new Vector3(2f, 0f, 0f);
            var matching = new GameObject("Matching");
            var other = new GameObject("Other");
            other.transform.SetPositionAndRotation(new Vector3(3f, 1f, 2f), Quaternion.Euler(0f, 90f, 0f));
            other.transform.localScale = new Vector3(2f, 2f, 2f);
            var player = new GameObject("Player").AddComponent<FeedbackPlayer>();
            ItemOn<SquashAndStretchItem>(player, squashed, Tween(new Vector4(1f, 0f, 0f, 0f)), 1f);
            ItemOn<LookAtItem>(player, looking, Tween(Vector4.one), 1f).m_LookTarget = Direct(other.transform);
            ItemOn<RotateAroundItem>(player, orbiting, Tween(new Vector4(90f, 0f, 0f, 0f)), 1f).m_Pivot = Direct(other.transform);
            ItemOn<MatchTransformItem>(player, matching, Tween(Vector4.one), 1f).m_Destination = Direct(other.transform);
            RoundTrip(player, new Object[] { squashed.transform, looking.transform, orbiting.transform, matching.transform, other.transform });
        }

        // a swap · a renderer turned off · two instance properties (one copy) · a block float's Persist · a texture's two
        // halves (one _ST slot): the save is the baseline, the stop gives the start, no copy and no block left
        [Test]
        public void PX03p_MaterialTargets([Values("swap", "visibility", "instance", "block float", "texture")] string kind)
        {
            MeshRenderer target = Renderer("Rendered", 2);
            var player = new GameObject("Player").AddComponent<FeedbackPlayer>();
            switch (kind)
            {
                case "swap":
                    ItemOn<MaterialSwapItem>(player, target, new SetMotion(), 0f).m_Material = AssetDatabase.GetBuiltinExtraResource<Material>("Sprites-Default.mat");
                    break;
                case "visibility":
                    ItemOn<RendererVisibilityItem>(player, target, new SetMotion(), 0f);
                    break;
                case "instance":
                    ItemOn<MaterialColorItem>(player, target, Tween(new Vector4(1f, 0f, 0f, 1f)), 1f).m_WriteMode = MaterialWriteMode.Instance;
                    MaterialFloatItem cutoff = ItemOn<MaterialFloatItem>(player, target, Tween(new Vector4(0.9f, 0f, 0f, 0f)), 1f);
                    cutoff.m_PropertyName = "_Cutoff";
                    cutoff.m_WriteMode = MaterialWriteMode.Instance;
                    break;
                case "block float":
                    ItemOn<MaterialFloatItem>(player, target, Tween(new Vector4(0.9f, 0f, 0f, 0f)), 0.1f).m_PropertyName = "_Cutoff";
                    break;
                default:
                    ItemOn<TextureOffsetItem>(player, target, Tween(new Vector4(0.5f, 0f, 0f, 0f)), 0.1f);
                    ItemOn<TextureScaleItem>(player, target, Tween(new Vector4(2f, 2f, 0f, 0f)), 1f);
                    break;
            }
            ItemOn<PositionItem>(player, player.gameObject, Tween(new Vector4(0f, 3f, 0f, 0f)), 1f);
            bool block = kind == "block float" || kind == "texture";
            RoundTrip(player, new Object[] { target }, !block, block ? () => target.HasPropertyBlock() : (Func<bool>)null);
            Assert.That(target.HasPropertyBlock(), Is.False, "no block left");
        }

        // Set Parent with and without a position item on the same object (AnimationMode keeps the first value it records), a
        // RectTransform: the save during the preview is the baseline, the stop gives the place back, clean
        [Test]
        public void PX03l_SetParentSaveAndStop([Values("alone", "position", "rect")] string kind)
        {
            // a RectTransform's under a canvas, placed by its anchored position — a write through the Transform leaves the
            // serialized anchored position behind, which a reparent (the engine) and the place's revert bring up to date
            bool rect = kind == "rect";
            Transform canvas = rect ? new GameObject("Canvas", typeof(RectTransform), typeof(Canvas)).transform : null;
            GameObject Made(string name, Vector3 local)
            {
                GameObject g = rect ? new GameObject(name, typeof(RectTransform)) : new GameObject(name);
                g.transform.SetParent(canvas, false);
                if (g.transform is RectTransform r)
                    r.anchoredPosition3D = local;
                else
                    g.transform.localPosition = local;
                return g;
            }
            GameObject p1 = Made("P1", new Vector3(5f, 0f, 0f));
            p1.transform.localRotation = Quaternion.Euler(0f, 0f, 30f);
            GameObject p2 = Made("P2", new Vector3(-3f, 1f, 0f));
            p2.transform.localScale = new Vector3(2f, 2f, 2f);
            GameObject first = Made("First", Vector3.zero);
            GameObject t = Made("T", Vector3.zero);
            GameObject last = Made("Last", Vector3.zero);
            foreach (GameObject g in new[] { first, t, last })
                g.transform.SetParent(p1.transform, false);
            if (t.transform is RectTransform r)
            {
                r.anchorMin = new Vector2(0.1f, 0.2f);
                r.anchorMax = new Vector2(0.7f, 0.9f);
                r.sizeDelta = new Vector2(-40f, 25f);
            }
            if (t.transform is RectTransform placed)
                placed.anchoredPosition3D = new Vector3(1f, 2f, 3f);
            else
                t.transform.localPosition = new Vector3(1f, 2f, 3f);
            var player = new GameObject("Player").AddComponent<FeedbackPlayer>();
            ItemOn<SetParentItem>(player, t, new SetMotion(), 0f).m_NewParent = Direct(p2.transform);
            ItemOn<PositionItem>(player, kind == "position" ? t : last, Tween(new Vector4(0f, 4f, 0f, 0f)), 1f);
            int states = PreviewSaveState.Count;
            RoundTrip(player, new Object[] { t.transform, p1.transform, p2.transform }, true, () => t.transform.parent == p2.transform && PreviewSaveState.Count == states + 1);
            Assert.That(t.transform.GetSiblingIndex(), Is.EqualTo(1), "the sibling index");
            Assert.That(DirtyCheck.Dirty(m_Scene), Is.Empty, "clean");
        }

        // a root to another scene's parent — both saves are their baselines, the root comes back at its index
        [Test]
        public void PX03m_SetParentIntoAnotherScene()
        {
            const string pathB = "Assets/PreviewTargetsTestsB.unity";
            File.WriteAllText(Path.GetFullPath(pathB), "%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n");
            AssetDatabase.ImportAsset(pathB, ImportAssetOptions.ForceUpdate);
            Scene b = EditorSceneManager.OpenScene(pathB, OpenSceneMode.Additive);
            try
            {
                new GameObject("First");
                var t = new GameObject("Root");
                new GameObject("Last");
                var player = new GameObject("Player").AddComponent<FeedbackPlayer>();
                SceneManager.SetActiveScene(b);
                var parent = new GameObject("InB");
                SceneManager.SetActiveScene(m_Scene);
                ItemOn<SetParentItem>(player, t, new SetMotion(), 0f).m_NewParent = Direct(parent.transform);
                ItemOn<PositionItem>(player, parent, Tween(new Vector4(0f, 4f, 0f, 0f)), 1f);
                SceneFileCheck fileA = SceneFileCheck.ForScene(m_Scene);
                SceneFileCheck fileB = SceneFileCheck.ForScene(b);
                Assert.That(PreviewSession.Start(player, default), Is.EqualTo(PreviewRefusal.None));
                Seconds(0.27);
                Assert.That(t.scene, Is.EqualTo(b), "in the other scene — the positive");
                Assert.That(fileA.SaveAndCompare(m_Scene), Is.Empty, "its scene's save");
                Assert.That(fileB.SaveAndCompare(b), Is.Empty, "the other scene's save");
                PreviewSession.Current.Stop();
                Assert.That(t.scene, Is.EqualTo(m_Scene), "back in its scene");
                Assert.That(m_Scene.GetRootGameObjects().Select(g => g.name).Take(3), Is.EqualTo(new[] { "First", "Root", "Last" }), "at its root index");
                AssertFree();
            }
            finally
            {
                PreviewSession.Current?.Stop();
                EditorSceneManager.CloseScene(b, true);
                AssetDatabase.DeleteAsset(pathB);
            }
        }

        // a prefab instance's own child: the preview leaves the Set Parent out — no error, a notice; the user's masks untouched
        [Test]
        public void PX03n_SetParentOfAPrefabChildIsLeftOut()
        {
            var root = new GameObject("PrefabRoot");
            new GameObject("PrefabChild").transform.SetParent(root.transform, false);
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath), m_Scene);
            Transform child = instance.transform.Find("PrefabChild");
            var other = new GameObject("Other");
            var player = new GameObject("Player").AddComponent<FeedbackPlayer>();
            ItemOn<SetParentItem>(player, child, new SetMotion(), 0f).m_NewParent = Direct(other.transform);
            ItemOn<PositionItem>(player, other, Tween(new Vector4(0f, 4f, 0f, 0f)), 1f);
            Assert.That(PreviewSession.Start(player, default), Is.EqualTo(PreviewRefusal.None));
            Seconds(0.27);
            Assert.That(child.parent, Is.SameAs(instance.transform), "left out");
            var notices = new List<PreviewNotice>();
            PreviewNotices.Collect(PreviewSession.Current, notices);
            Assert.That(notices.Count(n => n.Kind == PreviewNoticeKind.PrefabChild), Is.EqualTo(1), "told");
            Assert.That(PreviewMasks.Get(player, 0), Is.EqualTo(PreviewMask.None), "the user's mask untouched");
            PreviewSession.Current.Stop();
            AssertFree();
        }

        // the user's reparent (Undo) during the preview ends it — the user's place stays, parent and world place
        [UnityTest]
        public IEnumerator PX03o_TheUsersReparentStays()
        {
            var p1 = new GameObject("P1");
            p1.transform.SetPositionAndRotation(new Vector3(5f, 0f, 0f), Quaternion.Euler(0f, 30f, 0f));
            var p2 = new GameObject("P2");
            var p3 = new GameObject("P3");
            p3.transform.SetPositionAndRotation(new Vector3(0f, 2f, 0f), Quaternion.Euler(0f, 0f, 45f));
            var t = new GameObject("T");
            t.transform.SetParent(p1.transform, false);
            t.transform.localPosition = new Vector3(1f, 2f, 3f);
            var player = new GameObject("Player").AddComponent<FeedbackPlayer>();
            ItemOn<SetParentItem>(player, t, new SetMotion(), 0f).m_NewParent = Direct(p2.transform);
            ItemOn<PositionItem>(player, new GameObject("Keeper"), Tween(new Vector4(0f, 4f, 0f, 0f)), 5f);
            Assert.That(PreviewSession.Start(player, default), Is.EqualTo(PreviewRefusal.None));
            Seconds(0.1);
            Assert.That(t.transform.parent, Is.SameAs(p2.transform), "the preview's parent");
            Undo.SetTransformParent(t.transform, p3.transform, "PX03o");
            Vector3 world = t.transform.position;
            for (int i = 0; i < 4; i++)
            {
                yield return null;
                Pump(1);
            }
            Assert.That(PreviewSession.Current, Is.Null, "the session ended");
            Assert.That(PreviewSession.LastEnd, Is.EqualTo(PreviewEndReason.UserEdit));
            Assert.That(t.transform.parent, Is.SameAs(p3.transform), "the user's parent");
            Assert.That((t.transform.position - world).magnitude, Is.LessThan(1e-4f), "the user's world place");
            AssertFree();
        }

        // the slot-state table makes a state in a session only, for the parent slot only
        [Test]
        public void PX06a_TheSlotStatesInASessionOnly()
        {
            var p1 = new GameObject("P1");
            var p2 = new GameObject("P2");
            var t = new GameObject("T");
            t.transform.SetParent(p1.transform, false);
            var played = new GameObject("Played").AddComponent<FeedbackPlayer>();
            ItemOn<SetParentItem>(played, t, new SetMotion(), 0f).m_NewParent = Direct(p2.transform);
            var s = new FeedbackScheduler(ContextKind.Test);
            played.Bind(s);
            try
            {
                played.Play();
                s.Tick(0.05f);
                Assert.That(t.transform.parent, Is.SameAs(p2.transform), "played — the positive");
                Assert.That(PreviewSaveState.Count, Is.EqualTo(0), "no state outside a session");
            }
            finally
            {
                s.Shutdown();
                played.ReleaseRuntime();
                played.Bind(null);
            }
            var lit = new GameObject("Lit");
            lit.AddComponent<Light>();
            var others = lit.AddComponent<FeedbackPlayer>();
            Item<PositionItem>(others, Tween(new Vector4(0f, 4f, 0f, 0f)), 1f);
            Item<LightIntensityItem>(others, Tween(new Vector4(3f, 0f, 0f, 0f)), 1f);
            Item<SquashAndStretchItem>(others, Tween(new Vector4(1f, 0f, 0f, 0f)), 1f);
            Assert.That(PreviewSession.Start(others, default), Is.EqualTo(PreviewRefusal.None));
            Seconds(0.1);
            Assert.That(PreviewSaveState.Count, Is.EqualTo(0), "no state for the other slot types");
            PreviewSession.Current.Stop();
            AssertFree();
        }
    }
}
