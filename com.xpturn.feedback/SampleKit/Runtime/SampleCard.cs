using UnityEngine;

namespace xpTURN.Feedback.Samples
{
    // a sample's card — the hub's entry, the HUD's controls and the README are this data. The scene is kept by its GUID
    // (an import keeps it) and its name (a player loads by name)
    public sealed class SampleCard : ScriptableObject
    {
        public string Title;
        [TextArea] public string Try;
        [TextArea] public string See;
        [TextArea] public string Controls;
        public string SceneGuid;
        public string SceneName;
        // a type of the sample's own assembly ('Type, Assembly') — absent when a package it needs is not installed
        public string RequiredType;
        public string RequiresText;
        // an edit-mode sample: the hub adds 'Open in Editor' — the editor leaves play, opens the scene and selects this card
        public bool OpenInEditor;
    }
}
