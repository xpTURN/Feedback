using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.InputSystem.UI;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace xpTURN.Feedback.Tests
{
    // the trigger through a real EventSystem and the Input System's UI module, driven by a virtual mouse and touchscreen: on a
    // Button's object (hover, the Select a press brings, click — the Button's On Click too), below a Button (the trigger takes
    // the click; disabled it does not), in a ScrollRect (the drag reaches the ScrollRect; an EventTrigger keeps it), moving into
    // a child (no second Enter), a touch's order. Batch play mode has no focused Game view: the two input settings route the
    // devices to the player — with the defaults no event arrives, so each case asserts its events came. The editor only
    [UnityPlatform(RuntimePlatform.OSXEditor, RuntimePlatform.WindowsEditor, RuntimePlatform.LinuxEditor)]
    public sealed class UITriggerPlayTests
    {
        static readonly List<string> s_Played = new List<string>();
        static int s_Clicks;
        readonly List<Object> m_Made = new List<Object>();
        Mouse m_Mouse;
        Touchscreen m_Touch;
        Canvas m_Canvas;
        InputSettings.EditorInputBehaviorInPlayMode m_Behavior;
        InputSettings.BackgroundBehavior m_Background;

        static void OnPlayed(FeedbackPlayer player, PlaybackHandle handle) => s_Played.Add(player.name);

        static void OnClick() => s_Clicks++;

        static readonly Vector2 Outside = new Vector2(3f, 3f);

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            s_Played.Clear();
            s_Clicks = 0;
            m_Behavior = InputSystem.settings.editorInputBehaviorInPlayMode;
            m_Background = InputSystem.settings.backgroundBehavior;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            m_Mouse = InputSystem.AddDevice<Mouse>();
            m_Made.Add(new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule)));
            var canvas = new GameObject("Canvas", typeof(Canvas), typeof(GraphicRaycaster));
            m_Made.Add(canvas);
            m_Canvas = canvas.GetComponent<Canvas>();
            m_Canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            yield return null;
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
            if (m_Mouse != null)
                InputSystem.RemoveDevice(m_Mouse);
            if (m_Touch != null)
                InputSystem.RemoveDevice(m_Touch);
            m_Mouse = null;
            m_Touch = null;
            InputSystem.settings.editorInputBehaviorInPlayMode = m_Behavior;
            InputSystem.settings.backgroundBehavior = m_Background;
            yield return null;
        }

        RectTransform Box(string name, Transform parent, Vector2 center, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = size;
            rt.anchoredPosition = center;
            return rt;
        }

        // a trigger whose row for each event plays its own player, named after the event
        FeedbackUITrigger Watch(GameObject go)
        {
            var trigger = go.AddComponent<FeedbackUITrigger>();
            foreach (UITriggerEvent kind in System.Enum.GetValues(typeof(UITriggerEvent)))
            {
                var holder = new GameObject(kind.ToString());
                m_Made.Add(holder);
                var player = holder.AddComponent<FeedbackPlayer>();
                player.m_Policy = PlayPolicy.Overlap;
                player.m_Items.Add(new WaitItem());
                player.Played += OnPlayed;
                trigger.m_Entries.Add(new UITriggerEntry { m_Event = kind, m_Action = UITriggerAction.Play, m_Player = player });
            }
            return trigger;
        }

        static Vector2 At(RectTransform rt, float dx = 0f) => (Vector2)rt.position + new Vector2(dx, 0f);

        IEnumerator Pointer(Vector2 position, bool pressed)
        {
            InputSystem.QueueStateEvent(m_Mouse, new MouseState { position = position }.WithButton(MouseButton.Left, pressed));
            yield return null;
            yield return null;
        }

        static string Take()
        {
            string text = string.Join(" ", s_Played);
            s_Played.Clear();
            return text;
        }

        static string Selected() => EventSystem.current != null && EventSystem.current.currentSelectedGameObject != null ? EventSystem.current.currentSelectedGameObject.name : "none";

        // on the Button's object: Enter, the Select its press brings (a pointer's) and Down, Up and Click, Exit — the Button
        // clicks too and holds the selection
        [UnityTest]
        public IEnumerator PT01_OnTheButtonsObject()
        {
            RectTransform b = Box("Buy", m_Canvas.transform, Vector2.zero, new Vector2(200f, 100f));
            b.gameObject.AddComponent<Button>().onClick.AddListener(OnClick);
            Watch(b.gameObject);
            yield return Pointer(Outside, false);
            Take();
            var steps = new List<string>();
            yield return Pointer(At(b), false);
            steps.Add(Take());
            yield return Pointer(At(b), true);
            steps.Add(Take());
            yield return Pointer(At(b), false);
            steps.Add(Take());
            yield return Pointer(Outside, false);
            steps.Add(Take());
            Assert.That(steps[0], Is.Not.Empty, "the events came");
            Assert.That(steps, Is.EqualTo(new[] { "PointerEnter", "Select PointerDown", "PointerUp PointerClick", "PointerExit" }));
            Assert.That((s_Clicks, Selected()), Is.EqualTo((1, "Buy")));
        }

        // below the Button: the trigger takes the press and the click — no On Click, no selection; disabled, it takes nothing
        [UnityTest]
        public IEnumerator PT02_BelowTheButton()
        {
            RectTransform b = Box("Buy", m_Canvas.transform, Vector2.zero, new Vector2(200f, 100f));
            b.gameObject.AddComponent<Button>().onClick.AddListener(OnClick);
            RectTransform label = Box("label", b, Vector2.zero, new Vector2(100f, 50f));
            FeedbackUITrigger trigger = Watch(label.gameObject);
            var rounds = new List<string>();
            foreach (bool enabled in new[] { true, false })
            {
                trigger.enabled = enabled;
                s_Clicks = 0;
                yield return Pointer(Outside, false);
                Take();
                yield return Pointer(At(label), false);
                yield return Pointer(At(label), true);
                yield return Pointer(At(label), false);
                string selected = Selected();
                yield return Pointer(Outside, false);
                rounds.Add("clicks " + s_Clicks + " · selected " + selected + " · " + Take());
                EventSystem.current.SetSelectedGameObject(null);
            }
            Assert.That(rounds, Is.EqualTo(new[]
            {
                "clicks 0 · selected none · PointerEnter PointerDown PointerUp PointerClick PointerExit",
                "clicks 1 · selected Buy · ",
            }));
        }

        // a drag across an item in a ScrollRect moves the content past the drag threshold — with the trigger on the item too;
        // an EventTrigger on the item keeps the drag
        [UnityTest]
        public IEnumerator PT03_TheScrollRectKeepsItsDrag()
        {
            var moved = new List<string>();
            foreach (string kind in new[] { "plain", "trigger", "event trigger" })
            {
                RectTransform view = Box("view", m_Canvas.transform, Vector2.zero, new Vector2(300f, 200f));
                var content = new GameObject("content", typeof(RectTransform)).GetComponent<RectTransform>();
                content.SetParent(view, false);
                content.anchorMin = content.anchorMax = new Vector2(0.5f, 0.5f);
                content.sizeDelta = new Vector2(600f, 200f);
                var scroll = view.gameObject.AddComponent<ScrollRect>();
                scroll.content = content;
                scroll.viewport = view;
                scroll.vertical = false;
                scroll.movementType = ScrollRect.MovementType.Unrestricted;
                scroll.inertia = false;
                RectTransform item = Box("item", content, Vector2.zero, new Vector2(100f, 100f));
                if (kind == "trigger")
                    Watch(item.gameObject);
                if (kind == "event trigger")
                    item.gameObject.AddComponent<EventTrigger>();
                yield return null;
                Vector2 start = At(item);
                yield return Pointer(Outside, false);
                yield return Pointer(start, false);
                yield return Pointer(start, true);
                foreach (float dx in new[] { 5f, 15f, 30f, 60f })
                    yield return Pointer(start + new Vector2(dx, 0f), true);
                yield return Pointer(start + new Vector2(60f, 0f), false);
                yield return Pointer(Outside, false);
                moved.Add(kind + " " + content.anchoredPosition.x);
                Object.Destroy(view.gameObject);
                yield return null;
            }
            Assert.That(Take(), Does.Contain("PointerEnter"), "the events came");
            Assert.That(moved, Is.EqualTo(new[] { "plain 45", "trigger 45", "event trigger 0" }));
        }

        // the pointer moving into a child and back sends the trigger no second Enter or Exit
        [UnityTest]
        public IEnumerator PT04_IntoAChild()
        {
            RectTransform parent = Box("parent", m_Canvas.transform, Vector2.zero, new Vector2(300f, 200f));
            Watch(parent.gameObject);
            RectTransform child = Box("child", parent, new Vector2(80f, 0f), new Vector2(80f, 80f));
            yield return Pointer(Outside, false);
            Take();
            var steps = new List<string>();
            yield return Pointer(At(parent, -100f), false);
            steps.Add(Take());
            yield return Pointer(At(child), false);
            steps.Add(Take());
            yield return Pointer(At(parent, -100f), false);
            steps.Add(Take());
            yield return Pointer(Outside, false);
            steps.Add(Take());
            Assert.That(steps, Is.EqualTo(new[] { "PointerEnter", "", "", "PointerExit" }));
        }

        // a touch: Enter, the Select and Down as it begins; Up, Click and Exit as it ends
        [UnityTest]
        public IEnumerator PT05_ATouch()
        {
            RectTransform b = Box("Buy", m_Canvas.transform, Vector2.zero, new Vector2(200f, 100f));
            b.gameObject.AddComponent<Button>().onClick.AddListener(OnClick);
            Watch(b.gameObject);
            m_Touch = InputSystem.AddDevice<Touchscreen>();
            yield return null;
            Take();
            var steps = new List<string>();
            InputSystem.QueueStateEvent(m_Touch, new TouchState { touchId = 1, phase = UnityEngine.InputSystem.TouchPhase.Began, position = At(b) });
            yield return null;
            yield return null;
            steps.Add(Take());
            InputSystem.QueueStateEvent(m_Touch, new TouchState { touchId = 1, phase = UnityEngine.InputSystem.TouchPhase.Ended, position = At(b) });
            yield return null;
            yield return null;
            steps.Add(Take());
            Assert.That(steps, Is.EqualTo(new[] { "PointerEnter Select PointerDown", "PointerUp PointerClick PointerExit" }));
            Assert.That(s_Clicks, Is.EqualTo(1));
        }
    }
}
