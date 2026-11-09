namespace xpTURN.Feedback
{
    // the preview recorder — called once per slot right before its first write. The key's slot type picks how the editor
    // records (AnimationMode property paths · m_IsActive · m_Materials)
    internal interface ILayerRecorder
    {
        void Record(in LayerKey key);
    }
}
