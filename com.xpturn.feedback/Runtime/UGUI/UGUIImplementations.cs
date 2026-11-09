using UnityEngine;

namespace xpTURN.Feedback
{
    // the UGUI assembly's registration — the runtime at SubsystemRegistration (every play entry — the same factories
    // again), the editor at its load (the preview prepares them)
    static class UGUIImplementations
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Register()
        {
            FeedbackImplementations.Register<GraphicColorItem>(item => new GraphicColorBinding(item));
            FeedbackImplementations.Register<GraphicAlphaItem>(item => new GraphicAlphaBinding(item));
            FeedbackImplementations.Register<GraphicCrossFadeItem>(item => new GraphicCrossFadeBinding(item));
            FeedbackImplementations.Register<GraphicMaterialItem>(item => new GraphicMaterialBinding(item));
            FeedbackImplementations.Register<GraphicRaycastTargetItem>(item => new GraphicRaycastTargetBinding(item));
            FeedbackImplementations.Register<GraphicTextureOffsetItem>(item => new GraphicTextureOffsetBinding(item));
            FeedbackImplementations.Register<GraphicTextureScaleItem>(item => new GraphicTextureScaleBinding(item));
            FeedbackImplementations.Register<ImageFillItem>(item => new ImageFillBinding(item));
            FeedbackImplementations.Register<ImageSpriteItem>(item => new ImageSpriteBinding(item));
            FeedbackImplementations.Register<ImageSpriteSheetItem>(item => new ImageSpriteSheetBinding(item));
            FeedbackImplementations.Register<CanvasGroupAlphaItem>(item => new CanvasGroupAlphaBinding(item));
            FeedbackImplementations.Register<CanvasGroupInteractableItem>(item => new CanvasGroupInteractableBinding(item));
            FeedbackImplementations.Register<CanvasGroupBlocksRaycastsItem>(item => new CanvasGroupBlocksRaycastsBinding(item));
            FeedbackImplementations.Register<TextContentItem>(item => new TextContentBinding(item));
            FeedbackImplementations.Register<TextFontSizeItem>(item => new TextFontSizeBinding(item));
            FeedbackImplementations.Register<SelectableInteractableItem>(item => new SelectableInteractableBinding(item));
            FeedbackImplementations.Register<TMPFontSizeItem>(item => new TMPFontSizeBinding(item));
            FeedbackImplementations.Register<TMPCharacterSpacingItem>(item => new TMPCharacterSpacingBinding(item));
            FeedbackImplementations.Register<TMPWordSpacingItem>(item => new TMPWordSpacingBinding(item));
            FeedbackImplementations.Register<TMPLineSpacingItem>(item => new TMPLineSpacingBinding(item));
            FeedbackImplementations.Register<TMPParagraphSpacingItem>(item => new TMPParagraphSpacingBinding(item));
            FeedbackImplementations.Register<TMPDilateItem>(item => new TMPMaterialFloatBinding(item, TMPShader.FaceDilate, true));
            FeedbackImplementations.Register<TMPSoftnessItem>(item => new TMPMaterialFloatBinding(item, TMPShader.OutlineSoftness, true));
            FeedbackImplementations.Register<TMPOutlineWidthItem>(item => new TMPMaterialFloatBinding(item, TMPShader.OutlineWidth, true));
            FeedbackImplementations.Register<TMPOutlineColorItem>(item => new TMPMaterialColorBinding(item, TMPShader.OutlineColor));
            FeedbackImplementations.Register<TMPUnderlayColorItem>(item => new TMPMaterialColorBinding(item, TMPShader.UnderlayColor));
            FeedbackImplementations.Register<TMPUnderlayOffsetItem>(item => new TMPUnderlayOffsetBinding(item));
            FeedbackImplementations.Register<TMPTextItem>(item => new TMPTextBinding(item));
            FeedbackImplementations.Register<TMPCountToItem>(item => new TMPCountToBinding(item));
            FeedbackImplementations.Register<TMPRevealItem>(item => new TMPRevealBinding(item));
            // the characters' offset · scale · alpha — one shared slot per text
            FeedbackImplementations.Register<TMPCharacterOffsetItem>(item => new TMPCharacterBinding(item, TMPCharacterChannel.Offset));
            FeedbackImplementations.Register<TMPCharacterScaleItem>(item => new TMPCharacterBinding(item, TMPCharacterChannel.Scale));
            FeedbackImplementations.Register<TMPCharacterAlphaItem>(item => new TMPCharacterBinding(item, TMPCharacterChannel.Alpha));
            // the screen overlay's items and its extension service
            FeedbackImplementations.Register<ScreenFlashItem>(item => new ScreenFlashBinding(item));
            FeedbackImplementations.Register<ScreenFadeItem>(item => new ScreenFadeBinding(item));
            FeedbackImplementations.Register<FloatingTextItem>(item => new FloatingTextBinding(item));
            FeedbackContext.RegisterExtension(ScreenOverlayService.Key, context => new ScreenOverlayService(context));
        }

#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
        static void RegisterInEditor() => Register();
#endif
    }
}
