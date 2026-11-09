using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace xpTURN.Feedback.Tests
{
    // the uGUI items' fixtures — an Image with a group and a button (no transition — the tint's allocation is uGUI's) · a Text
    // on its own (a GameObject takes one Graphic). Registered at the runtime's load: the test assemblies without uGUI can't
    // name them
    static class UGUIFixtures
    {
        static Sprite s_A;
        static Sprite s_B;
        static Material s_Material;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Register()
        {
            ModuleFixtures.Add("ugui", Leaf, new[]
            {
                typeof(GraphicColorItem), typeof(GraphicAlphaItem), typeof(GraphicCrossFadeItem), typeof(GraphicMaterialItem), typeof(GraphicRaycastTargetItem),
                typeof(GraphicTextureOffsetItem), typeof(GraphicTextureScaleItem), typeof(ImageFillItem), typeof(ImageSpriteItem), typeof(ImageSpriteSheetItem),
                typeof(CanvasGroupAlphaItem), typeof(CanvasGroupInteractableItem), typeof(CanvasGroupBlocksRaycastsItem), typeof(SelectableInteractableItem),
            }, Fields);
            ModuleFixtures.Add("ugui-text", TextLeaf, new[] { typeof(TextContentItem), typeof(TextFontSizeItem) }, Fields);
            // a TMP text (the project's Essential Resources — its font material)
            ModuleFixtures.Add("tmp", TMPLeaf, new[]
            {
                typeof(TMPFontSizeItem), typeof(TMPCharacterSpacingItem), typeof(TMPWordSpacingItem), typeof(TMPLineSpacingItem), typeof(TMPParagraphSpacingItem),
                typeof(TMPDilateItem), typeof(TMPSoftnessItem), typeof(TMPOutlineWidthItem), typeof(TMPOutlineColorItem), typeof(TMPUnderlayColorItem),
                typeof(TMPUnderlayOffsetItem), typeof(TMPTextItem), typeof(TMPRevealItem), typeof(TMPCountToItem),
            }, Fields);
            // a UGUI text under no canvas has no mesh — the characters' leaf carries its canvas
            ModuleFixtures.Add("tmp-characters", CharacterLeaf, new[] { typeof(TMPCharacterOffsetItem), typeof(TMPCharacterScaleItem), typeof(TMPCharacterAlphaItem) }, Fields);
            ModuleFixtures.EditorAllocates(typeof(TMPCountToItem), "the editor's SetText(char[]) makes TMP's m_text string (#if UNITY_EDITOR)");
            // the screen overlay — the globals take no target, the text its leaf
            ModuleFixtures.Add("screen", _ => { }, new[] { typeof(ScreenFlashItem), typeof(ScreenFadeItem), typeof(FloatingTextItem) }, Fields);
        }

        internal static Sprite MakeSprite(Color c)
        {
            var t = new Texture2D(4, 4) { name = "G1-sprite" };
            var px = new Color[16];
            for (int i = 0; i < px.Length; i++)
                px[i] = c;
            t.SetPixels(px);
            t.Apply();
            return Sprite.Create(t, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 4);
        }

        static Sprite A => s_A != null ? s_A : s_A = MakeSprite(Color.red);

        static Sprite B => s_B != null ? s_B : s_B = MakeSprite(Color.green);

        static void Leaf(GameObject leaf)
        {
            var image = leaf.AddComponent<Image>();
            image.sprite = A;
            image.type = Image.Type.Filled;
            leaf.AddComponent<CanvasGroup>();
            var button = leaf.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.targetGraphic = image;
        }

        static void TextLeaf(GameObject leaf)
        {
            var text = leaf.AddComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.text = "G1";
        }

        static void TMPLeaf(GameObject leaf) => leaf.AddComponent<TextMeshProUGUI>().text = "G1";

        static void CharacterLeaf(GameObject leaf)
        {
            leaf.AddComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            leaf.AddComponent<TextMeshProUGUI>().text = "G1";
        }

        static void Fields(TargetItem item)
        {
            switch (item)
            {
                case ImageSpriteItem s:
                    s.m_Sprite = B;
                    break;
                case ImageSpriteSheetItem s:
                    s.m_Frames.Clear();
                    s.m_Frames.Add(A);
                    s.m_Frames.Add(B);
                    break;
                case GraphicMaterialItem m:
                    if (s_Material == null)
                        s_Material = new Material(Canvas.GetDefaultCanvasMaterial()) { name = "G1-ui-swap" };
                    m.m_Material = s_Material;
                    break;
                case TextContentItem t:
                    t.m_Text = "G1-other";
                    break;
                case TMPTextItem t:
                    t.m_Text = "G1-other";
                    break;
                case FloatingTextItem f:
                    f.m_Text = "G1";
                    f.m_PoolSize = 2;
                    break;
            }
        }
    }
}
