using System;
using UnityEngine;

namespace xpTURN.Feedback
{
    // one row of the query — for the debugger, never on the playback path. The base only of a live target. Direct: a private
    // slot of a direct write
    internal readonly struct LayerSlotInfo
    {
        internal readonly UnityEngine.Object Target;
        internal readonly Type SlotType;
        internal readonly int Aux;
        internal readonly int Aux2;
        internal readonly ConsumptionStage Stage;
        internal readonly int LiveContributions;
        internal readonly bool Pending;
        internal readonly bool IsAlive;
        internal readonly Vector4 Base;
        internal readonly bool Direct;

        internal LayerSlotInfo(UnityEngine.Object target, Type slotType, int aux, int aux2, ConsumptionStage stage, int liveContributions, bool pending, bool isAlive, Vector4 baseValue, bool direct)
        {
            Target = target;
            SlotType = slotType;
            Aux = aux;
            Aux2 = aux2;
            Stage = stage;
            LiveContributions = liveContributions;
            Pending = pending;
            IsAlive = isAlive;
            Base = baseValue;
            Direct = direct;
        }
    }
}
