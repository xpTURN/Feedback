using System;
using System.Collections.Generic;
using Object = UnityEngine.Object;

namespace xpTURN.Feedback.Editor
{
    internal enum PreviewRecordResult
    {
        Unknown,    // no rows for the slot type — the recorder falls back
        Record,
        Skip,       // nothing serialized to record: a block (MPB) — the layer reverts it
    }

    internal readonly struct PreviewRecordPath
    {
        internal readonly Object Owner;
        internal readonly string Path;

        internal PreviewRecordPath(Object owner, string path)
        {
            Owner = owner;
            Path = path;
        }
    }

    internal interface IPreviewRecordSource
    {
        // adds the (object, path) pairs the key's slot writes
        PreviewRecordResult Get(in LayerKey key, List<PreviewRecordPath> paths);

        // whether the slot type has rows — a Skip is a row. The preflight asks it before anything is recorded and Get gives the
        // same answer
        bool Knows(Type slotType);
    }

    // the one place a key's (object, path) pairs come from. The source is swapped — the slot types' rows are
    // PreviewRecordPathTable's, a test puts its own. Without a source every key is unknown
    internal static class PreviewRecordPaths
    {
        internal static IPreviewRecordSource Source;

        internal static PreviewRecordResult Get(in LayerKey key, List<PreviewRecordPath> paths)
            => Source != null ? Source.Get(in key, paths) : PreviewRecordResult.Unknown;

        internal static bool Knows(Type slotType) => Source != null && Source.Knows(slotType);
    }
}
