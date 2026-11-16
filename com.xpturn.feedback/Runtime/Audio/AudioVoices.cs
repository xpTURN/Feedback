using System.Collections.Generic;
using UnityEngine;

namespace xpTURN.Feedback
{
    // a context's Play At Point voices. A template (one Audio Source) is the pools' key — the pools make, keep and grow the
    // instances; this service keeps the live ones: their end, Stop After and the fade out on their clock (a paused voice does
    // not count), the player's pause, their owner's stops. A voice goes back from here — stopped, then to the pools
    internal sealed class AudioVoiceService : FeedbackService
    {
        internal static readonly ServiceKey Key = new ServiceKey();
        internal const string TemplateName = "AudioVoice";

        internal static AudioVoiceService Of(FeedbackContext context) => context.GetExtension(Key) as AudioVoiceService;

        // the pools' end of a voice: never — this service gives it back. A live voice keeps the preview's automatic end waiting
        internal static readonly System.Func<GameObject, bool> Held = _ => false;

        struct Voice
        {
            internal GameObject Instance;
            internal AudioSource Source;
            internal ItemBinding Owner;
            internal int Record;
            internal long Serial;
            internal bool Unscaled;
            internal bool Paused;
            // Stop After's seconds of sound left — +∞ without one
            internal float Left;
            internal float Fade;
            // the fade out's seconds left — below 0 while not fading
            internal float FadeLeft;
            internal float Volume;
        }

        Voice[] m_Live = new Voice[4];
        int m_LiveCount;
        int m_Reserved;
        GameObject m_Template;

        internal AudioVoiceService(FeedbackContext context)
            : base(context)
        {
        }

        bool IsPreview => Context.Kind == ContextKind.Preview;

        // ---- observation
        internal int LiveCount => m_LiveCount;

        internal GameObject LiveAt(int i) => m_Live[i].Instance;

        internal bool PausedAt(int i) => m_Live[i].Paused;

        internal GameObject TemplateIfMade => m_Template;

        // made under the pools' inactive container, itself on: an instance is made off and wakes when the take moves it out.
        // A preview's is hidden
        internal GameObject Template
        {
            get
            {
                if (m_Template == null)
                {
                    var go = new GameObject((IsPreview ? PropertyLayerRegistry.PreviewPrefix : PropertyLayerRegistry.RuntimePrefix) + TemplateName);
                    if (IsPreview)
                        go.hideFlags = HideFlags.HideAndDontSave;
                    go.transform.SetParent(Context.Pools.Container, false);
                    AudioSource source = go.AddComponent<AudioSource>();
                    source.playOnAwake = false;
                    source.loop = false;
                    m_Template = go;
                }
                return m_Template;
            }
        }

        // the live list holds every voice the preparations reserved — a take does not grow it (Pools' form)
        internal void ReserveLive(int count)
        {
            m_Reserved += count;
            if (m_Live.Length < m_Reserved)
                System.Array.Resize(ref m_Live, m_Reserved);
        }

        internal void Launch(GameObject instance, AudioSource source, ItemBinding owner, int record, long serial, bool unscaled, float volume, float stopAfter, float fadeOut)
        {
            if (m_LiveCount == m_Live.Length)
                System.Array.Resize(ref m_Live, m_Live.Length * 2);
            m_Live[m_LiveCount++] = new Voice
            {
                Instance = instance,
                Source = source,
                Owner = owner,
                Record = record,
                Serial = serial,
                Unscaled = unscaled,
                Left = stopAfter > 0f ? stopAfter : float.PositiveInfinity,
                Fade = fadeOut > 0f ? fadeOut : 0f,
                FadeLeft = -1f,
                Volume = volume,
            };
        }

        // RunUpdate's services step: a destroyed voice let go (a scene's unload — the pools let it go too) · an ended one given
        // back — not playing and held by neither our pause nor the listener's · Stop After and the fade out counted while it
        // plays
        internal override void Update(in FrameTime time)
        {
            for (int i = m_LiveCount - 1; i >= 0; i--)
            {
                ref Voice v = ref m_Live[i];
                if (v.Instance == null || v.Source == null)
                {
                    RemoveAt(i);
                    continue;
                }
                AudioSource s = v.Source;
                if (!s.isPlaying)
                {
                    if (!v.Paused && (s.ignoreListenerPause || !AudioListener.pause))
                        ReturnAt(i);
                    continue;
                }
                if (float.IsPositiveInfinity(v.Left))
                    continue;
                float dt = v.Unscaled ? time.UnscaledDelta : time.ScaledDelta;
                if (v.FadeLeft < 0f)
                {
                    v.Left -= dt;
                    if (v.Left > 0f)
                        continue;
                    v.FadeLeft = v.Fade + v.Left;
                }
                else
                    v.FadeLeft -= dt;
                if (v.FadeLeft <= 0f)
                {
                    ReturnAt(i);
                    continue;
                }
                s.volume = v.Volume * (v.FadeLeft / v.Fade);
            }
        }

        void RemoveAt(int i)
        {
            m_Live[i] = m_Live[--m_LiveCount];
            m_Live[m_LiveCount] = default;
        }

        void ReturnAt(int i)
        {
            Voice v = m_Live[i];
            RemoveAt(i);
            if (v.Source != null)
                v.Source.Stop();
            Context.PoolsIfMade?.TryReturn(v.Instance);
        }

        bool Owns(int i, ItemBinding owner, int record, long serial)
            => ReferenceEquals(m_Live[i].Owner, owner) && (record < 0 || m_Live[i].Record == record) && (serial < 0 || m_Live[i].Serial == serial);

        // the owner's voices of a record (−1: every record) of a play (−1: every play) stopped and given back — the paused
        // ones only, or all
        internal void Stop(ItemBinding owner, int record, long serial, bool pausedOnly)
        {
            for (int i = m_LiveCount - 1; i >= 0; i--)
            {
                if (Owns(i, owner, record, serial) && (!pausedOnly || m_Live[i].Paused))
                    ReturnAt(i);
            }
        }

        // the player's pause and resume — the engine does not count pauses, so the voice keeps its own mark. A voice held by
        // the listener's pause is paused too: it stays paused when the listener goes on
        internal void Suspend(ItemBinding owner, int record, long serial, bool paused)
        {
            for (int i = 0; i < m_LiveCount; i++)
            {
                if (!Owns(i, owner, record, serial) || m_Live[i].Source == null)
                    continue;
                if (paused)
                {
                    if (m_Live[i].Paused)
                        continue;
                    m_Live[i].Source.Pause();
                    m_Live[i].Paused = true;
                }
                else if (m_Live[i].Paused)
                {
                    m_Live[i].Paused = false;
                    m_Live[i].Source.UnPause();
                }
            }
        }

        // the owner's live voices of a record and a play, and how many of them the player's pause holds — the debugger's note
        internal int VoicesOf(ItemBinding owner, int record, long serial, out int paused)
        {
            int n = 0;
            paused = 0;
            for (int i = 0; i < m_LiveCount; i++)
            {
                if (!Owns(i, owner, record, serial))
                    continue;
                n++;
                if (m_Live[i].Paused)
                    paused++;
            }
            return n;
        }

        // a Rebuild: the new binding takes the old one's
        internal void Reown(ItemBinding from, ItemBinding to)
        {
            for (int i = 0; i < m_LiveCount; i++)
            {
                if (ReferenceEquals(m_Live[i].Owner, from))
                    m_Live[i].Owner = to;
            }
        }

        // a binding let go: its voices ring to their end, no owner
        internal void Disown(ItemBinding owner)
        {
            for (int i = 0; i < m_LiveCount; i++)
            {
                if (ReferenceEquals(m_Live[i].Owner, owner))
                    m_Live[i].Owner = null;
            }
        }

        internal override string DebugName => "Audio voices";

        internal override void Describe(List<ServiceRow> into)
        {
            into.Clear();
            int paused = 0, fading = 0;
            for (int i = 0; i < m_LiveCount; i++)
            {
                if (m_Live[i].Paused)
                    paused++;
                if (m_Live[i].FadeLeft >= 0f)
                    fading++;
            }
            into.Add(new ServiceRow("voices live", m_LiveCount));
            into.Add(new ServiceRow("paused", paused));
            into.Add(new ServiceRow("fading", fading));
            into.Add(new ServiceRow("template", m_Template != null ? 1 : 0));
        }

        // the instances are the pools' (their close destroys them and the template under their container)
        internal override void Shutdown()
        {
            for (int i = 0; i < m_LiveCount; i++)
                m_Live[i] = default;
            m_LiveCount = 0;
            m_Template = null;
        }
    }
}
