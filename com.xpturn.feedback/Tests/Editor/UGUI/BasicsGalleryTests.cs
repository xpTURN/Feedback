using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
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
    // coverage (the registered types by TypeCache, not from the gallery) · the players' rules · every entry pressed in play
    // (Console · one target on the stage · the resets · nothing left playing). The gallery is a sample's type — reached by name
    public sealed class BasicsGalleryTests
    {
        static readonly string[] Excluded = { "Cinemachine/", "Light 2D/", "Video/" };
        const BindingFlags All = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        // a static test leaves no sample scene open — a later test's camera would draw the gallery behind what it reads
        [TearDown]
        public void LeaveTheScene()
        {
            if (!EditorApplication.isPlaying && SceneManager.GetActiveScene().path.Contains("/Basics/"))
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }


        static string ScenePath()
        {
            string path = AssetDatabase.FindAssets("t:Scene").Select(AssetDatabase.GUIDToAssetPath).FirstOrDefault(p => System.IO.Path.GetFileNameWithoutExtension(p) == "FeedbackGallery");
            if (path == null)
                Assert.Ignore("no Basics bundle in this project");
            return path;
        }

        static Component Gallery() => Resources.FindObjectsOfTypeAll<MonoBehaviour>().FirstOrDefault(m => m != null && m.GetType().Name == "FeedbackGallery" && m.gameObject.scene.isLoaded);

        static IList Entries(Component g) => (IList)g.GetType().GetField("m_Entries", All).GetValue(g);

        static T F<T>(object o, string name) => (T)o.GetType().GetField(name, All).GetValue(o);

        static List<FeedbackPlayer> Players(Component g)
        {
            var players = Entries(g).Cast<object>().Select(e => F<FeedbackPlayer>(e, "Player")).Where(p => p != null).ToList();
            var prefab = F<GameObject>(g, "m_TmpStagePrefab");
            if (prefab != null)
                players.AddRange(prefab.GetComponentsInChildren<FeedbackPlayer>(true));
            return players;
        }

        [Test]
        public void BQ04a_EveryTypeAndPreset()
        {
            EditorSceneManager.OpenScene(ScenePath(), OpenSceneMode.Single);
            Component g = Gallery();
            var shown = new HashSet<Type>(Players(g).SelectMany(p => p.m_Items).Where(i => i != null).Select(i => i.GetType()));
            string[] missing = TypeCache.GetTypesWithAttribute<FeedbackTargetAttribute>()
                .Where(t => !t.IsAbstract && typeof(FeedbackItem).IsAssignableFrom(t))
                .Select(t => (t, a: (FeedbackTargetAttribute)Attribute.GetCustomAttribute(t, typeof(FeedbackTargetAttribute), false)))
                .Where(x => !Excluded.Any(e => x.a.Path.StartsWith(e, StringComparison.Ordinal)) && !shown.Contains(x.t))
                .Select(x => x.a.Path).ToArray();
            string[] presets = AssetDatabase.FindAssets("t:FeedbackPreset", new[] { "Packages/com.xpturn.feedback/Presets" })
                .Select(guid => System.IO.Path.GetFileNameWithoutExtension(AssetDatabase.GUIDToAssetPath(guid))).OrderBy(x => x, StringComparer.Ordinal).ToArray();
            string[] tab = Entries(g).Cast<object>().Where(e => F<string>(e, "Tab") == "Presets").Select(e => F<string>(e, "Label")).OrderBy(x => x, StringComparer.Ordinal).ToArray();
            Assert.That(missing, Is.Empty, "types without a gallery entry");
            Assert.That(tab, Is.EqualTo(presets), "the Presets tab = the package's presets");
        }

        [Test]
        public void BQ05a_ThePlayersHoldNoWarning()
        {
            EditorSceneManager.OpenScene(ScenePath(), OpenSceneMode.Single);
            var problems = new List<string>();
            foreach (FeedbackPlayer p in Players(Gallery()))
            {
                foreach (RuleResult r in FeedbackValidator.Run(p).Where(r => r.Severity != RuleSeverity.Info))
                    problems.Add($"{p.name}: {r.Id}");
            }
            Assert.That(problems, Is.Empty);
        }

        // an Image Fill entry's image has a sprite (a Filled image without one ignores fillAmount)
        [Test]
        public void BQ05b_TheImageFillHasASprite()
        {
            EditorSceneManager.OpenScene(ScenePath(), OpenSceneMode.Single);
            var problems = new List<string>();
            int n = 0;
            foreach (FeedbackPlayer p in Players(Gallery()))
                foreach (FeedbackItem item in p.m_Items.Where(i => i is ImageFillItem))
                {
                    n++;
                    var image = (((TargetItem)item).m_Target.m_Mode == TargetRefMode.Direct ? ((TargetItem)item).m_Target.m_Object as UnityEngine.UI.Image : p.GetComponent<UnityEngine.UI.Image>());
                    if (image == null || image.sprite == null)
                        problems.Add(p.name);
                }
            Assert.That(n, Is.GreaterThan(0), "an Image Fill entry");
            Assert.That(problems, Is.Empty);
        }

        // a particle on the stage is a soft round dot (a texture with alpha on a transparent material), not a flat square
        [Test]
        public void BQ05c_TheParticlesHaveATexture()
        {
            Scene scene = EditorSceneManager.OpenScene(ScenePath(), OpenSceneMode.Single);
            var problems = new List<string>();
            int n = 0;
            foreach (ParticleSystemRenderer r in scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<ParticleSystemRenderer>(true)))
            {
                n++;
                Material m = r.sharedMaterial;
                string at = r.transform.parent != null ? r.transform.parent.name + "/" + r.name : r.name;
                if (m == null)
                    problems.Add($"{at}: no material");
                else if (m.mainTexture == null || m.renderQueue < (int)UnityEngine.Rendering.RenderQueue.Transparent)
                    problems.Add($"{at}: {m.name} · texture {m.mainTexture} · queue {m.renderQueue}");
            }
            TestContext.WriteLine($"particle renderers {n} · without a texture or not transparent {problems.Count}");
            Assert.That(n, Is.GreaterThan(0), "a particle system on the stage");
            Assert.That(problems, Is.Empty);
        }

        // the Bloom stage has something brighter than the item's threshold (Bloom spreads only from the pixels above it; the grey
        // stage under the gallery's light has none)
        [Test]
        public void BQ05d_TheBloomHasSomethingBright()
        {
            EditorSceneManager.OpenScene(ScenePath(), OpenSceneMode.Single);
            object e = Entry(Gallery(), "PostFX", "Bloom · Punch");
            Assert.That(e, Is.Not.Null, "no Bloom entry");
            var item = (PostFXBloomItem)F<FeedbackPlayer>(e, "Player").m_Items.First(i => i is PostFXBloomItem);
            float threshold = item.m_Threshold.m_Value, brightest = 0f;
            int emissive = 0;
            foreach (Renderer r in F<GameObject>(e, "Target").GetComponentsInChildren<Renderer>(true))
                foreach (Material m in r.sharedMaterials)
                    if (m != null && m.IsKeywordEnabled("_EMISSION") && m.HasProperty("_EmissionColor"))
                    {
                        emissive++;
                        brightest = Mathf.Max(brightest, m.GetColor("_EmissionColor").maxColorComponent);
                    }
            TestContext.WriteLine($"Bloom — emissive {emissive} · brightest {brightest:F2} · threshold {threshold:F2}");
            Assert.That(emissive, Is.GreaterThan(0), "nothing emissive on the stage");
            Assert.That(brightest, Is.GreaterThan(threshold), "nothing above the threshold");
        }

        static int s_Warn;
        static readonly List<string> s_Msgs = new List<string>();

        static void Count(string c, string st, LogType t)
        {
            if (t == LogType.Log)
                return;
            s_Warn++;
            if (s_Msgs.Count < 3)
                s_Msgs.Add(c.Split('\n')[0]);
        }

        // a static helper: a lambda in a play-mode iterator must not capture a local (the reload drops the closure)
        static int Shown(Component g, MethodInfo targetOf)
        {
            var seen = new HashSet<GameObject>();
            foreach (object x in Entries(g))
            {
                var t = (GameObject)targetOf.Invoke(g, new[] { x });
                if (t != null && t.activeInHierarchy)
                    seen.Add(t);
            }
            return seen.Count;
        }

        [UnityTest, Timeout(900000)]
        public IEnumerator BQ06a_PressEveryEntry()
        {
            yield return new EnterPlayMode();
            EditorSceneManager.LoadSceneInPlayMode(ScenePath(), new LoadSceneParameters(LoadSceneMode.Single));
            for (int i = 0; i < 10; i++)
                yield return null;
            Component g = Gallery();
            MethodInfo press = g.GetType().GetMethod("Press");
            MethodInfo playerOf = g.GetType().GetMethod("PlayerOf");
            MethodInfo targetOf = g.GetType().GetMethod("TargetOf");
            var problems = new List<string>();
            ScreenOverlayService overlay = ScreenOverlayService.Of(FeedbackScheduler.Default.Context);
            Color ambient = RenderSettings.ambientLight;
            float near = Camera.main.nearClipPlane, far = Camera.main.farClipPlane;
            Application.logMessageReceived += Count;
            foreach (object e in Entries(g))
            {
                var player = (FeedbackPlayer)playerOf.Invoke(g, new[] { e });
                if (player == null)
                    continue;
                string key = F<string>(e, "Tab") + "/" + F<string>(e, "Label");
                s_Warn = 0;
                s_Msgs.Clear();
                Transform pressed = player.transform.Find("Subject");
                press.Invoke(g, new[] { e });
                float until = Time.realtimeSinceStartup + 3.2f;
                yield return null;
                while (Time.realtimeSinceStartup < until && player != null && player.IsPlaying)
                    yield return null;
                // a Clear's cover and a Set's Respawn come later (the gallery holds a Set's end 1.6 s)
                string later = F<object>(e, "Reset").ToString();
                float settle = Time.realtimeSinceStartup + (later == "Clear" || later == "Respawn" ? 1.8f : 0.5f);
                while (Time.realtimeSinceStartup < settle)
                    yield return null;
                if (s_Warn > 0)
                    problems.Add($"{key}: {string.Join(" / ", s_Msgs)}");
                int shown = Shown(g, targetOf);
                if (shown > 1)
                    problems.Add($"{key}: {shown} targets on the stage");
                if (player != null && player.IsPlaying)
                    problems.Add($"{key}: still playing");
                var reset = F<object>(e, "Reset").ToString();
                if (reset == "Respawn")
                {
                    Transform subject = player != null ? player.transform.Find("Subject") : null;
                    Transform template = player != null ? player.transform.Find("Template") : null;
                    // a Trail entry's subject circles from the template's place — it stays the orbit's radius off the centre
                    Component orbit = subject != null ? subject.GetComponent("GalleryOrbit") : null;
                    float radius = orbit != null ? F<float>(orbit, "m_Radius") : 0f;
                    if (subject == null || template == null || ReferenceEquals(subject, pressed)
                        || Mathf.Abs((subject.localPosition - (template.localPosition - new Vector3(radius, 0f, 0f))).magnitude - radius) > 1e-2f)
                        problems.Add($"{key}: the subject is not made again from the template");
                }
                if (reset == "Globals" && (RenderSettings.ambientLight != ambient || Camera.main.nearClipPlane != near || Camera.main.farClipPlane != far))
                    problems.Add($"{key}: the scene's values did not come back");
                if (reset == "Clear" && overlay != null && overlay.Fade > 1e-3f)
                    problems.Add($"{key}: the cover stays ({overlay.Fade})");
            }
            Application.logMessageReceived -= Count;
            yield return new ExitPlayMode();
            Assert.That(problems, Is.Empty);
        }

        // the Animation tab shows what its items drive: the subject (the Speed entry's own cube) moves on the channel the item
        // feeds — Blend leans it, the Overlay layer swells it, the Jump trigger hops it, the Bob state bobs it, the speed acts on
        // the idle sway. A fixed window from the press, not the play's length
        static readonly string[] AnimationLabels = { "Float Parameter · Tween", "Layer Weight · Tween", "Parameter · Set", "Play State · Set", "Speed · Punch" };
        static readonly string[] AnimationChannels = { "lean", "swell", "hop", "bob", "sway" };
        static readonly float[] AnimationAtLeast = { 20f, 1.3f, 0.4f, 0.3f, 20f };
        const float AnimationSpeedAtLeast = 1.5f;

        static float s_Lean, s_Swell, s_Height, s_Sway, s_Speed, s_LastYaw;

        static object Entry(Component g, string tab, string label)
        {
            foreach (object e in Entries(g))
                if (F<string>(e, "Tab") == tab && F<string>(e, "Label") == label)
                    return e;
            return null;
        }

        // one frame of the watched object: the roll (degrees off 0), the scale, the height, the yaw travelled (Mathf.DeltaAngle —
        // Quaternion.Angle reads 0 under ~0.16°, more than a batch frame turns), the animator's speed
        static void Sample(Transform t, bool first)
        {
            Vector3 euler = t.localEulerAngles;
            s_Lean = Mathf.Max(s_Lean, Mathf.Abs(Mathf.DeltaAngle(0f, euler.z)));
            s_Swell = Mathf.Max(s_Swell, t.localScale.x);
            s_Height = Mathf.Max(s_Height, t.localPosition.y);
            if (!first)
                s_Sway += Mathf.Abs(Mathf.DeltaAngle(s_LastYaw, euler.y));
            s_LastYaw = euler.y;
            if (t.TryGetComponent(out Animator animator))
                s_Speed = Mathf.Max(s_Speed, animator.speed);
        }

        [UnityTest, Timeout(300000)]
        public IEnumerator BQ06b_TheAnimationTabMoves()
        {
            yield return new EnterPlayMode();
            EditorSceneManager.LoadSceneInPlayMode(ScenePath(), new LoadSceneParameters(LoadSceneMode.Single));
            for (int i = 0; i < 10; i++)
                yield return null;
            Component g = Gallery();
            MethodInfo press = g.GetType().GetMethod("Press");
            MethodInfo playerOf = g.GetType().GetMethod("PlayerOf");
            var problems = new List<string>();
            for (int k = 0; k < AnimationLabels.Length; k++)
            {
                object e = Entry(g, "Animation", AnimationLabels[k]);
                var player = e != null ? (FeedbackPlayer)playerOf.Invoke(g, new[] { e }) : null;
                if (player == null)
                {
                    problems.Add($"{AnimationLabels[k]}: no entry");
                    continue;
                }
                bool subject = F<object>(e, "Reset").ToString() == "Respawn";
                s_Lean = s_Swell = s_Height = s_Sway = s_Speed = 0f;
                press.Invoke(g, new[] { e });
                bool first = true;
                float until = Time.realtimeSinceStartup + 2.5f;
                while (Time.realtimeSinceStartup < until)
                {
                    yield return null;
                    Transform t = subject ? player.transform.Find("Subject") : player.transform;
                    if (t != null)
                    {
                        Sample(t, first);
                        first = false;
                    }
                }
                float[] got = { s_Lean, s_Swell, s_Height, s_Height, s_Sway };
                TestContext.WriteLine($"{AnimationLabels[k]} — {AnimationChannels[k]} {got[k]:F2} (at least {AnimationAtLeast[k]}) · speed {s_Speed:F2}");
                if (got[k] < AnimationAtLeast[k])
                    problems.Add($"{AnimationLabels[k]}: {AnimationChannels[k]} {got[k]:F2} < {AnimationAtLeast[k]}");
                if (k == 4 && s_Speed < AnimationSpeedAtLeast)
                    problems.Add($"{AnimationLabels[k]}: speed {s_Speed:F2} < {AnimationSpeedAtLeast}");
            }
            yield return new ExitPlayMode();
            Assert.That(problems, Is.Empty);
        }

        // a Camera entry's stage sets the main camera up to show its item (the gallery draws a sky with a perspective camera):
        // Background Color clears to a solid color while it flashes white, Orthographic Size turns orthographic while its size
        // punches (and comes back to where it started); the next entry finds the camera as it was
        [UnityTest, Timeout(300000)]
        public IEnumerator BQ06c_TheCameraStagesShowTheirItem()
        {
            yield return new EnterPlayMode();
            EditorSceneManager.LoadSceneInPlayMode(ScenePath(), new LoadSceneParameters(LoadSceneMode.Single));
            for (int i = 0; i < 10; i++)
                yield return null;
            Component g = Gallery();
            MethodInfo press = g.GetType().GetMethod("Press");
            Camera camera = Camera.main;
            CameraClearFlags flags = camera.clearFlags;
            Color background = camera.backgroundColor;
            bool orthographic = camera.orthographic;
            float size = camera.orthographicSize;
            var problems = new List<string>();

            press.Invoke(g, new[] { Entry(g, "Camera", "Background Color · Flash") });
            bool solid = true;
            float white = 0f;
            float until = Time.realtimeSinceStartup + 1.5f;
            while (Time.realtimeSinceStartup < until)
            {
                yield return null;
                solid &= camera.clearFlags == CameraClearFlags.SolidColor;
                Color c = camera.backgroundColor;
                white = Mathf.Max(white, Mathf.Min(c.r, Mathf.Min(c.g, c.b)));
            }
            TestContext.WriteLine($"Background Color — solid {solid} · whitest {white:F2}");
            if (!solid || white < 0.9f)
                problems.Add($"Background Color: solid {solid} · whitest {white:F2}");

            press.Invoke(g, new[] { Entry(g, "Camera", "Orthographic Size · Punch") });
            if (camera.clearFlags != flags || ((Vector4)(camera.backgroundColor - background)).sqrMagnitude > 1e-6f)
                problems.Add($"Background Color: the camera is not put back ({camera.clearFlags} · {camera.backgroundColor})");
            // the size the stage set, before the punch writes (the scheduler has not run since the press)
            bool ortho = camera.orthographic;
            float start = camera.orthographicSize, low = float.MaxValue, high = float.MinValue;
            until = Time.realtimeSinceStartup + 1.5f;
            while (Time.realtimeSinceStartup < until)
            {
                yield return null;
                ortho &= camera.orthographic;
                low = Mathf.Min(low, camera.orthographicSize);
                high = Mathf.Max(high, camera.orthographicSize);
            }
            float settled = camera.orthographicSize;
            TestContext.WriteLine($"Orthographic Size — orthographic {ortho} · size {start:F2} → {low:F2} ~ {high:F2} → {settled:F2}");
            if (!ortho || high - low < 0.5f || Mathf.Abs(settled - start) > 0.05f)
                problems.Add($"Orthographic Size: orthographic {ortho} · size {start:F2} → {low:F2} ~ {high:F2} → {settled:F2}");

            press.Invoke(g, new[] { Entry(g, "Camera", "Field of View · Punch") });
            if (camera.orthographic != orthographic || Mathf.Abs(camera.orthographicSize - size) > 1e-4f)
                problems.Add($"Orthographic Size: the camera is not put back ({camera.orthographic} · {camera.orthographicSize:F2})");
            for (int i = 0; i < 30; i++)
                yield return null;
            yield return new ExitPlayMode();
            Assert.That(problems, Is.Empty);
        }

        // a Custom entry's sample component draws the value it takes: Color Value its cube's color (flashed white), Float Value
        // its height, Vector3 Value its place, Float Event's listener a bar's height; each comes back where it started. A fixed
        // window from the press
        static readonly string[] CustomLabels = { "Color Value · Flash", "Float Value · Punch", "Vector3 Value · Punch", "Float Event · Punch" };
        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");

        // what the stage draws: the block's base color (its darkest channel — white is 1), the height, the place's height, the bar's height
        static float Drawn(int k, Transform t, Renderer renderer, MaterialPropertyBlock block)
        {
            if (k == 0)
            {
                if (renderer == null)
                    return 0f;
                renderer.GetPropertyBlock(block);
                Color c = block.GetColor(BaseColor);
                return Mathf.Min(c.r, Mathf.Min(c.g, c.b));
            }
            return k == 2 ? t.localPosition.y : t.localScale.y;
        }

        [UnityTest, Timeout(300000)]
        public IEnumerator BQ06d_TheCustomTabDrawsItsValue()
        {
            yield return new EnterPlayMode();
            EditorSceneManager.LoadSceneInPlayMode(ScenePath(), new LoadSceneParameters(LoadSceneMode.Single));
            for (int i = 0; i < 10; i++)
                yield return null;
            Component g = Gallery();
            MethodInfo press = g.GetType().GetMethod("Press");
            MethodInfo playerOf = g.GetType().GetMethod("PlayerOf");
            var block = new MaterialPropertyBlock();
            var problems = new List<string>();
            for (int k = 0; k < CustomLabels.Length; k++)
            {
                object e = Entry(g, "Custom", CustomLabels[k]);
                var player = e != null ? (FeedbackPlayer)playerOf.Invoke(g, new[] { e }) : null;
                if (player == null)
                {
                    problems.Add($"{CustomLabels[k]}: no entry");
                    continue;
                }
                Transform t = player.transform;
                bool drawn = t.TryGetComponent(out Renderer renderer);
                press.Invoke(g, new[] { e });
                // the stage as it shows, before the item writes (the scheduler has not run since the press)
                float start = Drawn(k, t, renderer, block), low = start, high = start;
                float until = Time.realtimeSinceStartup + 1.5f;
                while (Time.realtimeSinceStartup < until)
                {
                    yield return null;
                    float v = Drawn(k, t, renderer, block);
                    low = Mathf.Min(low, v);
                    high = Mathf.Max(high, v);
                }
                float settled = Drawn(k, t, renderer, block);
                TestContext.WriteLine($"{CustomLabels[k]} — drawn {drawn} · {start:F2} → {low:F2} ~ {high:F2} → {settled:F2}");
                if (!drawn || high - low < 0.5f || Mathf.Abs(settled - start) > 0.05f)
                    problems.Add($"{CustomLabels[k]}: drawn {drawn} · {start:F2} → {low:F2} ~ {high:F2} → {settled:F2}");
            }
            yield return new ExitPlayMode();
            Assert.That(problems, Is.Empty);
        }

        // the Shadow Strength stage has something in the light's range that casts a shadow (the light alone casts none on the
        // floor); the strength fades out and back, with no jump from frame to frame (the Respawn finds it where it started). A
        // fixed window from the press
        [UnityTest, Timeout(300000)]
        public IEnumerator BQ06e_TheShadowStrengthHasAShadow()
        {
            yield return new EnterPlayMode();
            EditorSceneManager.LoadSceneInPlayMode(ScenePath(), new LoadSceneParameters(LoadSceneMode.Single));
            for (int i = 0; i < 10; i++)
                yield return null;
            Component g = Gallery();
            MethodInfo press = g.GetType().GetMethod("Press");
            MethodInfo playerOf = g.GetType().GetMethod("PlayerOf");
            object e = Entry(g, "Light", "Shadow Strength · Tween");
            var player = e != null ? (FeedbackPlayer)playerOf.Invoke(g, new[] { e }) : null;
            Assert.That(player, Is.Not.Null, "no Shadow Strength entry");
            press.Invoke(g, new[] { e });
            Light light = player.transform.Find("Subject").GetComponent<Light>();
            int casters = 0;
            foreach (Renderer r in player.GetComponentsInChildren<Renderer>())
                if (r.shadowCastingMode != UnityEngine.Rendering.ShadowCastingMode.Off && Vector3.Distance(r.bounds.center, light.transform.position) < light.range)
                    casters++;
            float start = light.shadowStrength, low = start, high = start, last = start, step = 0f;
            float until = Time.realtimeSinceStartup + 2.5f;
            while (Time.realtimeSinceStartup < until)
            {
                yield return null;
                Transform subject = player.transform.Find("Subject");
                if (subject == null || !subject.TryGetComponent(out light))
                    continue;
                float v = light.shadowStrength;
                low = Mathf.Min(low, v);
                high = Mathf.Max(high, v);
                step = Mathf.Max(step, Mathf.Abs(v - last));
                last = v;
            }
            TestContext.WriteLine($"Shadow Strength — casters {casters} · {start:F2} → {low:F2} ~ {high:F2} → {last:F2} · largest step {step:F2}");
            yield return new ExitPlayMode();
            Assert.That(casters, Is.GreaterThan(0), "nothing in the light's range casts a shadow");
            Assert.That(high - low, Is.GreaterThanOrEqualTo(0.8f), "the strength does not fade out");
            Assert.That(step, Is.LessThanOrEqualTo(0.5f), "the strength jumps");
            Assert.That(Mathf.Abs(last - start), Is.LessThanOrEqualTo(0.05f), "the strength does not come back");
        }

        // the Emission Rate and Simulation Speed stages' systems flow while their stage shows (a stopped system shows nothing
        // whatever its rate or speed): Emission Rate's flash pours more particles than its stream holds, Simulation Speed's speeds
        // the flow up and back. Fixed windows from the presses
        [UnityTest, Timeout(300000)]
        public IEnumerator BQ06f_TheParticleStagesFlow()
        {
            yield return new EnterPlayMode();
            EditorSceneManager.LoadSceneInPlayMode(ScenePath(), new LoadSceneParameters(LoadSceneMode.Single));
            for (int i = 0; i < 10; i++)
                yield return null;
            Component g = Gallery();
            MethodInfo press = g.GetType().GetMethod("Press");
            MethodInfo playerOf = g.GetType().GetMethod("PlayerOf");
            var problems = new List<string>();

            // the stream once the first flash is over, then a second flash on top of it
            object rate = Entry(g, "Particles", "Emission Rate · Flash");
            var rateSystem = ((FeedbackPlayer)playerOf.Invoke(g, new[] { rate })).GetComponent<ParticleSystem>();
            press.Invoke(g, new[] { rate });
            bool flowing = true;
            int stream = 0, poured = 0;
            float until = Time.realtimeSinceStartup + 3f;
            while (Time.realtimeSinceStartup < until)
            {
                yield return null;
                flowing &= rateSystem.isPlaying;
                if (Time.realtimeSinceStartup > until - 0.6f)
                    stream = Mathf.Max(stream, rateSystem.particleCount);
            }
            press.Invoke(g, new[] { rate });
            until = Time.realtimeSinceStartup + 1.5f;
            while (Time.realtimeSinceStartup < until)
            {
                yield return null;
                flowing &= rateSystem.isPlaying;
                poured = Mathf.Max(poured, rateSystem.particleCount);
            }
            TestContext.WriteLine($"Emission Rate — playing {flowing} · stream {stream} · poured {poured}");
            if (!flowing || stream == 0 || poured < stream + 15)
                problems.Add($"Emission Rate: playing {flowing} · stream {stream} · poured {poured}");

            object speed = Entry(g, "Particles", "Simulation Speed · Flash");
            var speedSystem = ((FeedbackPlayer)playerOf.Invoke(g, new[] { speed })).GetComponent<ParticleSystem>();
            press.Invoke(g, new[] { speed });
            flowing = true;
            int alive = 0;
            float start = speedSystem.main.simulationSpeed, low = start, high = start;
            until = Time.realtimeSinceStartup + 1.5f;
            while (Time.realtimeSinceStartup < until)
            {
                yield return null;
                flowing &= speedSystem.isPlaying;
                alive = Mathf.Max(alive, speedSystem.particleCount);
                low = Mathf.Min(low, speedSystem.main.simulationSpeed);
                high = Mathf.Max(high, speedSystem.main.simulationSpeed);
            }
            float settled = speedSystem.main.simulationSpeed;
            TestContext.WriteLine($"Simulation Speed — playing {flowing} · alive {alive} · speed {start:F2} → {low:F2} ~ {high:F2} → {settled:F2}");
            if (!flowing || alive == 0 || high - low < 1.5f || Mathf.Abs(settled - start) > 0.05f)
                problems.Add($"Simulation Speed: playing {flowing} · alive {alive} · speed {start:F2} → {low:F2} ~ {high:F2} → {settled:F2}");
            yield return new ExitPlayMode();
            Assert.That(problems, Is.Empty);
        }

        // the Explosion stage has bodies around its centre for the blast to push: most of them scatter more than 0.5 m and none
        // leaves the view (4.5 m from the centre, across the floor) before the Respawn puts them back (the gallery holds a Set's
        // end 1.6 s). A fixed window from the press
        [UnityTest, Timeout(300000)]
        public IEnumerator BQ06g_TheExplosionScattersBodies()
        {
            yield return new EnterPlayMode();
            EditorSceneManager.LoadSceneInPlayMode(ScenePath(), new LoadSceneParameters(LoadSceneMode.Single));
            for (int i = 0; i < 10; i++)
                yield return null;
            Component g = Gallery();
            MethodInfo press = g.GetType().GetMethod("Press");
            MethodInfo playerOf = g.GetType().GetMethod("PlayerOf");
            object e = Entry(g, "Physics", "Explosion · Set");
            var player = e != null ? (FeedbackPlayer)playerOf.Invoke(g, new[] { e }) : null;
            Assert.That(player, Is.Not.Null, "no Explosion entry");
            press.Invoke(g, new[] { e });
            Transform subject = player.transform.Find("Subject");
            Rigidbody[] bodies = subject.GetComponentsInChildren<Rigidbody>();
            var from = new Vector3[bodies.Length];
            var moved = new float[bodies.Length];
            for (int i = 0; i < bodies.Length; i++)
                from[i] = bodies[i].position;
            Vector3 centre = subject.position;
            float farthest = 0f;
            float until = Time.realtimeSinceStartup + 1.4f;
            while (Time.realtimeSinceStartup < until)
            {
                yield return null;
                for (int i = 0; i < bodies.Length; i++)
                {
                    if (bodies[i] == null)
                        continue;
                    Vector3 at = bodies[i].position;
                    moved[i] = Mathf.Max(moved[i], Vector3.Distance(at, from[i]));
                    farthest = Mathf.Max(farthest, new Vector2(at.x - centre.x, at.z - centre.z).magnitude);
                }
            }
            int scattered = 0;
            float most = 0f;
            for (int i = 0; i < bodies.Length; i++)
            {
                if (moved[i] >= 0.5f)
                    scattered++;
                most = Mathf.Max(most, moved[i]);
            }
            TestContext.WriteLine($"Explosion — bodies {bodies.Length} · scattered {scattered} · most {most:F2} m · farthest {farthest:F2} m");
            yield return new ExitPlayMode();
            Assert.That(bodies.Length, Is.GreaterThan(0), "no bodies around the centre");
            Assert.That(scattered * 2, Is.GreaterThan(bodies.Length), "most bodies do not scatter");
            Assert.That(farthest, Is.LessThanOrEqualTo(4.5f), "a body leaves the view");
        }

        const int ViewWidth = 960, ViewHeight = 540;

        // the main camera's view (post-processing included — the gallery's UI is an overlay, not in it) at half the reference size
        static Color32[] View()
        {
            Camera cam = Camera.main;
            RenderTexture rt = RenderTexture.GetTemporary(ViewWidth, ViewHeight, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            cam.targetTexture = rt;
            cam.Render();
            cam.targetTexture = null;
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(ViewWidth, ViewHeight, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, ViewWidth, ViewHeight), 0, 0);
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(rt);
            Color32[] pixels = tex.GetPixels32();
            Object.DestroyImmediate(tex);
            return pixels;
        }

        // the share of pixels whose channels moved by more than `over` (of 255)
        static float Changed(Color32[] a, Color32[] b, int over)
        {
            int n = 0;
            for (int i = 0; i < a.Length; i++)
                if (Math.Max(Math.Abs(a[i].r - b[i].r), Math.Max(Math.Abs(a[i].g - b[i].g), Math.Abs(a[i].b - b[i].b))) > over)
                    n++;
            return (float)n / a.Length;
        }

        // the Chromatic Aberration press splits the colors on its stage: at some frame the view changes by more than 0.15 in at
        // least 1 % of its pixels against the stage before the press (the bare stage — the floor's thin lines — peaks at 0.5 %, the
        // pillars at 3 %). Measured on a second press: the first one's first frame is long (the preparation · the effect's first
        // render) and takes most of a short play. A fixed window from the press
        [UnityTest, Timeout(300000)]
        public IEnumerator BQ06h_TheChromaticAberrationSplitsColors()
        {
            yield return new EnterPlayMode();
            EditorSceneManager.LoadSceneInPlayMode(ScenePath(), new LoadSceneParameters(LoadSceneMode.Single));
            for (int i = 0; i < 10; i++)
                yield return null;
            Component g = Gallery();
            MethodInfo press = g.GetType().GetMethod("Press");
            MethodInfo playerOf = g.GetType().GetMethod("PlayerOf");
            object e = Entry(g, "PostFX", "Chromatic Aberration · Punch");
            var player = e != null ? (FeedbackPlayer)playerOf.Invoke(g, new[] { e }) : null;
            Assert.That(player, Is.Not.Null, "no Chromatic Aberration entry");
            press.Invoke(g, new[] { e });
            float warm = Time.realtimeSinceStartup + 3f;
            while (player.IsPlaying && Time.realtimeSinceStartup < warm)
            {
                yield return null;
                View();
            }
            for (int i = 0; i < 5; i++)
                yield return null;
            Color32[] from = View();
            press.Invoke(g, new[] { e });
            float peak = 0f, over = 0f, longest = 0f, last = Time.realtimeSinceStartup;
            float until = last + 1.4f;
            while (Time.realtimeSinceStartup < until)
            {
                yield return null;
                float share = Changed(from, View(), 38);
                peak = Mathf.Max(peak, share);
                longest = Mathf.Max(longest, Time.deltaTime);
                if (share >= 0.005f)
                    over += Time.realtimeSinceStartup - last;
                last = Time.realtimeSinceStartup;
            }
            TestContext.WriteLine($"Chromatic Aberration — peak {peak * 100f:F2} % · over 0.5 % for {over:F2} s · longest frame {longest:F3} s");
            yield return new ExitPlayMode();
            Assert.That(peak, Is.GreaterThanOrEqualTo(0.01f), "the colors do not split");
        }

        // the Film Grain press shows its grain on the bright stage: at some frame the view changes by more than 0.05 in at least
        // 1 % of its pixels against the stage before the press (the default Response 0.8 leaves bright pixels nearly bare — none
        // moved; Response 0.1 · 1.35 s — 3 %). Measured on a second press as the Chromatic Aberration's. A fixed window from the
        // press
        [UnityTest, Timeout(300000)]
        public IEnumerator BQ06i_TheFilmGrainShowsItsGrain()
        {
            yield return new EnterPlayMode();
            EditorSceneManager.LoadSceneInPlayMode(ScenePath(), new LoadSceneParameters(LoadSceneMode.Single));
            for (int i = 0; i < 10; i++)
                yield return null;
            Component g = Gallery();
            MethodInfo press = g.GetType().GetMethod("Press");
            MethodInfo playerOf = g.GetType().GetMethod("PlayerOf");
            object e = Entry(g, "PostFX", "Film Grain · Flash");
            var player = e != null ? (FeedbackPlayer)playerOf.Invoke(g, new[] { e }) : null;
            Assert.That(player, Is.Not.Null, "no Film Grain entry");
            press.Invoke(g, new[] { e });
            float warm = Time.realtimeSinceStartup + 3f;
            while (player.IsPlaying && Time.realtimeSinceStartup < warm)
            {
                yield return null;
                View();
            }
            for (int i = 0; i < 5; i++)
                yield return null;
            Color32[] from = View();
            press.Invoke(g, new[] { e });
            float peak = 0f, over = 0f, longest = 0f, last = Time.realtimeSinceStartup;
            float until = last + 2f;
            while (Time.realtimeSinceStartup < until)
            {
                yield return null;
                float share = Changed(from, View(), 13);
                peak = Mathf.Max(peak, share);
                longest = Mathf.Max(longest, Time.deltaTime);
                if (share >= 0.005f)
                    over += Time.realtimeSinceStartup - last;
                last = Time.realtimeSinceStartup;
            }
            TestContext.WriteLine($"Film Grain — peak {peak * 100f:F2} % · over 0.5 % for {over:F2} s · longest frame {longest:F3} s");
            yield return new ExitPlayMode();
            Assert.That(peak, Is.GreaterThanOrEqualTo(0.01f), "no grain shows");
        }

        // the PostFX overlay's volumes that are on — the service's, in DontDestroyOnLoad (the gallery's own volumes are in its scene)
        static List<Behaviour> OverlayVolumes()
        {
            var on = new List<Behaviour>();
            foreach (Behaviour b in Resources.FindObjectsOfTypeAll<Behaviour>())
                if (b != null && b.enabled && b.GetType().Name == "Volume" && b.gameObject.scene.name == "DontDestroyOnLoad")
                    on.Add(b);
            return on;
        }

        // the Motion Blur press blurs: the camera moves (URP's Motion Blur is Camera Only), so at some frame the view differs by
        // more than 0.05 in at least 1 % of its pixels from the same frame rendered with the overlay's volumes off (a still camera
        // — none; the swing — 4 %). Measured on a second press as the Chromatic Aberration's. A fixed window from the press
        [UnityTest, Timeout(300000)]
        public IEnumerator BQ06j_TheMotionBlurBlurs()
        {
            yield return new EnterPlayMode();
            EditorSceneManager.LoadSceneInPlayMode(ScenePath(), new LoadSceneParameters(LoadSceneMode.Single));
            for (int i = 0; i < 10; i++)
                yield return null;
            Component g = Gallery();
            MethodInfo press = g.GetType().GetMethod("Press");
            MethodInfo playerOf = g.GetType().GetMethod("PlayerOf");
            object e = Entry(g, "PostFX", "Motion Blur · Flash");
            var player = e != null ? (FeedbackPlayer)playerOf.Invoke(g, new[] { e }) : null;
            Assert.That(player, Is.Not.Null, "no Motion Blur entry");
            press.Invoke(g, new[] { e });
            float warm = Time.realtimeSinceStartup + 3f;
            while (player.IsPlaying && Time.realtimeSinceStartup < warm)
            {
                yield return null;
                View();
            }
            for (int i = 0; i < 5; i++)
                yield return null;
            press.Invoke(g, new[] { e });
            float peak = 0f, over = 0f, longest = 0f, last = Time.realtimeSinceStartup;
            float until = last + 1.4f;
            while (Time.realtimeSinceStartup < until)
            {
                yield return null;
                Color32[] shown = View();
                List<Behaviour> on = OverlayVolumes();
                foreach (Behaviour v in on)
                    v.enabled = false;
                Color32[] bare = View();
                foreach (Behaviour v in on)
                    v.enabled = true;
                float share = Changed(bare, shown, 13);
                peak = Mathf.Max(peak, share);
                longest = Mathf.Max(longest, Time.deltaTime);
                if (share >= 0.005f)
                    over += Time.realtimeSinceStartup - last;
                last = Time.realtimeSinceStartup;
            }
            TestContext.WriteLine($"Motion Blur — peak {peak * 100f:F2} % · over 0.5 % for {over:F2} s · longest frame {longest:F3} s");
            yield return new ExitPlayMode();
            Assert.That(peak, Is.GreaterThanOrEqualTo(0.01f), "nothing blurs");
        }
    }
}
