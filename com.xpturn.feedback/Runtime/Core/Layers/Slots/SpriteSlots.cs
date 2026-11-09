using System;
using System.Collections.Generic;
using UnityEngine;

namespace xpTURN.Feedback
{
    // the sprite renderer's color — not clamped, read back bit for bit. Sprite Alpha shares it
    [RecordPaths(typeof(SpriteRenderer), "m_Color.r", "m_Color.g", "m_Color.b", "m_Color.a")]
    internal sealed class SpriteColorSlot : ValueSlot<Color, ColorOps>
    {
        readonly SpriteRenderer m_Target;

        internal SpriteColorSlot(SpriteRenderer target)
            : base(ConsumptionStage.LateApply, Composition.Additive, false)
        {
            m_Target = target;
        }

        internal override bool IsAlive => m_Target != null;

        protected override Color Read() => m_Target.color;

        protected override void Write(in Color value) => m_Target.color = value;
    }

    // the sprite — a state, Persist only (Sprite Swap). The engine sizes m_Size at a renderer's first sprite only and the
    // setter's null clears that mark
    [RecordPaths(typeof(SpriteRenderer), "m_Sprite", "m_Size.x", "m_Size.y")]
    internal sealed class SpriteSlot : LayerSlot
    {
        readonly SpriteRenderer m_Target;
        int m_Reserved;

        internal SpriteSlot(SpriteRenderer target)
            : base(ConsumptionStage.Update)
        {
            m_Target = target;
        }

        internal override bool IsAlive => m_Target != null;

        internal override bool Pending => false;

        internal override int LiveContributions => 0;

        internal override int Reserved => m_Reserved;

        internal override Vector4 BaseVector => Vector4.zero;

        internal void Reserve(int count) => m_Reserved += count;

        internal override void Unreserve(int count) => m_Reserved -= Math.Min(count, m_Reserved);

        internal override void Revert()
        {
        }

        internal override void Apply()
        {
        }

        internal override void Close()
        {
        }

        internal override void EndAll()
        {
        }

        internal Sprite Read() => m_Target.sprite;

        // Persist — the door
        internal void WriteSprite(Sprite sprite)
        {
            RecordOnce();
            m_Target.sprite = sprite;
        }
    }

    // a sheet binding's own slot — the place 0 ~ 1 in its frames (rounded down, 1 is the last; an empty frame keeps the one
    // shown). The sprite is written only when the frame changes. Two values are no place: the sprite before the first frame
    // (the first value's door) and another's sprite (the latest seen — taken again at the first write after it)
    [RecordPaths(typeof(SpriteRenderer), "m_Sprite", "m_Size.x", "m_Size.y")]
    internal sealed class SpriteSheetSlot : ValueSlot<float, FloatOps>
    {
        internal const float Original = -1e9f;
        internal const float Other = -2e9f;
        readonly SpriteRenderer m_Target;
        readonly List<Sprite> m_Frames;
        Sprite m_First;
        bool m_HasFirst;
        Sprite m_Before;
        Sprite m_Shown;
        float m_Place;
        bool m_Showing;

        internal SpriteSheetSlot(SpriteRenderer target, List<Sprite> frames)
            : base(ConsumptionStage.Update, Composition.Additive, false)
        {
            m_Target = target;
            m_Frames = frames;
        }

        internal override bool IsAlive => m_Target != null;

        // observation — the sprite writes
        internal int Writes { get; private set; }

        protected override float Read()
        {
            Sprite now = m_Target.sprite;
            if (m_Showing && now == m_Shown)
                return m_Place;
            return !m_HasFirst || now == m_First ? Original : Other;
        }

        protected override void Write(in float value)
        {
            Sprite now = m_Target.sprite;
            if (!m_HasFirst)
            {
                m_First = now;
                m_HasFirst = true;
            }
            if (!(m_Showing && now == m_Shown))
                m_Before = now;
            if (value == Original || value == Other)
            {
                Put(value == Original ? m_First : m_Before);
                m_Showing = false;
                return;
            }
            int n = m_Frames != null ? m_Frames.Count : 0;
            if (n == 0)
                return;
            float p = value > 0f ? (value < 1f ? value : 1f) : 0f;
            Sprite frame = m_Frames[Mathf.Min((int)(p * n), n - 1)];
            if (frame == null)
            {
                if (m_Showing && now == m_Shown)
                    m_Place = value;
                return;
            }
            Put(frame);
            m_Shown = frame;
            m_Place = value;
            m_Showing = true;
        }

        void Put(Sprite sprite)
        {
            if (m_Target.sprite == sprite)
                return;
            m_Target.sprite = sprite;
            Writes++;
        }
    }

    // an axis's flip — a toggle
    [RecordPaths(typeof(SpriteRenderer), "m_FlipX", "m_FlipY")]
    internal sealed class SpriteFlipSlot : ToggleSlot
    {
        readonly SpriteRenderer m_Target;
        readonly SpriteAxis m_Axis;

        internal SpriteFlipSlot(SpriteRenderer target, SpriteAxis axis)
        {
            m_Target = target;
            m_Axis = axis;
        }

        internal override bool IsAlive => m_Target != null;

        protected override bool Read() => m_Axis == SpriteAxis.Y ? m_Target.flipY : m_Target.flipX;

        protected override void Write(bool value)
        {
            if (m_Axis == SpriteAxis.Y)
                m_Target.flipY = value;
            else
                m_Target.flipX = value;
        }
    }

    // the width multiplier — the curve's shape stays; nothing below 0 is written (the engine keeps a negative one)
    [RecordPaths(typeof(LineRenderer), "m_Parameters.widthMultiplier")]
    internal sealed class LineWidthSlot : ValueSlot<float, FloatOps>
    {
        readonly LineRenderer m_Target;

        internal LineWidthSlot(LineRenderer target)
            : base(ConsumptionStage.LateApply, Composition.Additive, false)
        {
            m_Target = target;
        }

        internal override bool IsAlive => m_Target != null;

        protected override float Read() => m_Target.widthMultiplier;

        protected override void Write(in float value) => m_Target.widthMultiplier = Mathf.Max(0f, value);
    }

    // an end's color — the gradient's first or last color and alpha keys, stored as 8 bits in 0 ~ 1. The judgment on the
    // value read back takes that in. Not recordable: the preview's save state puts it back, and the color the slot left tells
    // a user's edit
    [NotRecorded("a gradient key is not recordable — the preview's save state puts it back")]
    internal sealed class LineColorSlot : ValueSlot<Color, ColorOps>
    {
        readonly LineRenderer m_Target;
        readonly LineEnd m_End;
        Color m_Left;
        bool m_HasLeft;

        internal LineColorSlot(LineRenderer target, LineEnd end)
            : base(ConsumptionStage.LateApply, Composition.Additive, false)
        {
            m_Target = target;
            m_End = end;
        }

        internal override bool IsAlive => m_Target != null;

        // the color the slot left, read back
        internal bool Left(out Color left)
        {
            left = m_Left;
            return m_HasLeft;
        }

        protected override Color Read() => m_End == LineEnd.End ? m_Target.endColor : m_Target.startColor;

        protected override void Write(in Color value)
        {
            if (m_End == LineEnd.End)
                m_Target.endColor = value;
            else
                m_Target.startColor = value;
            m_Left = Read();
            m_HasLeft = true;
        }
    }

    // the trail's time and width — nothing below 0 is written
    [RecordPaths(typeof(TrailRenderer), "m_Time")]
    internal sealed class TrailTimeSlot : ValueSlot<float, FloatOps>
    {
        readonly TrailRenderer m_Target;

        internal TrailTimeSlot(TrailRenderer target)
            : base(ConsumptionStage.LateApply, Composition.Additive, false)
        {
            m_Target = target;
        }

        internal override bool IsAlive => m_Target != null;

        protected override float Read() => m_Target.time;

        protected override void Write(in float value) => m_Target.time = Mathf.Max(0f, value);
    }

    [RecordPaths(typeof(TrailRenderer), "m_Parameters.widthMultiplier")]
    internal sealed class TrailWidthSlot : ValueSlot<float, FloatOps>
    {
        readonly TrailRenderer m_Target;

        internal TrailWidthSlot(TrailRenderer target)
            : base(ConsumptionStage.LateApply, Composition.Additive, false)
        {
            m_Target = target;
        }

        internal override bool IsAlive => m_Target != null;

        protected override float Read() => m_Target.widthMultiplier;

        protected override void Write(in float value) => m_Target.widthMultiplier = Mathf.Max(0f, value);
    }

    [RecordPaths(typeof(TrailRenderer), "m_Emitting")]
    internal sealed class TrailEmittingSlot : ToggleSlot
    {
        readonly TrailRenderer m_Target;

        internal TrailEmittingSlot(TrailRenderer target)
        {
            m_Target = target;
        }

        internal override bool IsAlive => m_Target != null;

        protected override bool Read() => m_Target.emitting;

        protected override void Write(bool value) => m_Target.emitting = value;
    }
}
