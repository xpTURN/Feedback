using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace xpTURN.Feedback.Editor
{
    // the simulated targets of a session — the particle systems and the animators — learnt by the key's target type at the
    // recorder's call (the editor names the module type, not the optional assembly's slot type) and, for the systems, from the
    // preview pools' live ones (a spawn has no slot). Driven between the scheduler's Update and LateApply: a held (paused)
    // system and a stopped one are not driven (the engine's simulate ignores a pause and moves a stopped one's clock); a child
    // under a driven ancestor goes with it; an animator runs on its own clock while active. At the end each goes back to its
    // start (a system: simulate 0 from the start, stop · an animator: its speed and events written back, Rebind — the stop
    // brings its recorded bindings back)
    internal sealed class PreviewSimulation
    {
        readonly PropertyLayerRegistry m_Layers;
        readonly List<ParticleSystem> m_Systems = new List<ParticleSystem>();
        readonly List<LayerKey> m_Keys = new List<LayerKey>();
        readonly List<bool> m_HasKey = new List<bool>();
        readonly HashSet<GameObject> m_Scanned = new HashSet<GameObject>();
        readonly List<ParticleSystem> m_Found = new List<ParticleSystem>();
        // the animators — a second list with their start speed and events
        readonly PreviewRecorder m_Recorder;
        readonly List<Animator> m_Animators = new List<Animator>();
        readonly List<float> m_Speeds = new List<float>();
        readonly List<bool> m_Fires = new List<bool>();
        readonly HashSet<AnimationClip> m_Clips = new HashSet<AnimationClip>();
        readonly List<Transform> m_Transforms = new List<Transform>();
        static readonly string[] s_Trs = { "m_LocalPosition.x", "m_LocalPosition.y", "m_LocalPosition.z", "m_LocalRotation.x", "m_LocalRotation.y", "m_LocalRotation.z", "m_LocalRotation.w" };
        static readonly string[] s_Position = { "m_LocalPosition.x", "m_LocalPosition.y", "m_LocalPosition.z" };
        static readonly string[] s_Rotation = { "m_LocalRotation.x", "m_LocalRotation.y", "m_LocalRotation.z", "m_LocalRotation.w" };
        static readonly string[] s_Scale = { "m_LocalScale.x", "m_LocalScale.y", "m_LocalScale.z" };
        bool m_Logged;

        internal PreviewSimulation(PropertyLayerRegistry layers, PreviewRecorder recorder)
        {
            m_Layers = layers;
            m_Recorder = recorder;
        }

        internal bool Any => m_Systems.Count > 0 || m_Animators.Count > 0;

        internal int Count => m_Systems.Count + m_Animators.Count;

        internal void OnRecord(in LayerKey key)
        {
            if (key.Target is ParticleSystem system)
                Add(system, key, true);
            else if (key.Target is Animator animator)
                AddAnimator(animator);
        }

        // the first record: the bindings the animator writes (the clips' · the root under root motion · every transform of a
        // humanoid) recorded, its speed and events taken, the events off (no user code), then Rebind
        void AddAnimator(Animator a)
        {
            if (m_Animators.Contains(a))
                return;
            m_Animators.Add(a);
            m_Speeds.Add(a.speed);
            m_Fires.Add(a.fireEvents);
            if (a.runtimeAnimatorController != null)
            {
                m_Clips.Clear();
                foreach (AnimationClip clip in a.runtimeAnimatorController.animationClips)
                {
                    if (clip == null || !m_Clips.Add(clip))
                        continue;
                    foreach (EditorCurveBinding b in AnimationUtility.GetCurveBindings(clip))
                    {
                        if (b.type == typeof(Animator))
                            continue;
                        Object t = AnimationUtility.GetAnimatedObject(a.gameObject, b);
                        if (t == null)
                            continue;
                        // a transform's curve binds its whole vector — the other components take the bind's defaults
                        string[] group = t is Transform ? TransformGroup(b.propertyName) : null;
                        if (group == null)
                            m_Recorder.RecordPath(t, b.propertyName);
                        else
                            foreach (string path in group)
                                m_Recorder.RecordPath(t, path);
                    }
                    foreach (EditorCurveBinding b in AnimationUtility.GetObjectReferenceCurveBindings(clip))
                    {
                        Object t = AnimationUtility.GetAnimatedObject(a.gameObject, b);
                        if (t != null)
                            m_Recorder.RecordPath(t, b.propertyName);
                    }
                }
            }
            bool human = a.avatar != null && a.avatar.isHuman;
            if (human || a.applyRootMotion)
            {
                m_Transforms.Clear();
                if (human)
                    a.GetComponentsInChildren(true, m_Transforms);
                else
                    m_Transforms.Add(a.transform);
                foreach (Transform t in m_Transforms)
                    foreach (string path in s_Trs)
                        m_Recorder.RecordPath(t, path);
            }
            a.fireEvents = false;
            a.Rebind();
            if (!m_Logged && a.isInitialized && a.GetBehaviours<StateMachineBehaviour>().Length > 0)
            {
                m_Logged = true;
                Debug.Log("Feedback preview: the state machine behaviours of " + a.name + " run in the preview (the editor's animator update calls them).");
            }
        }

        static string[] TransformGroup(string property)
            => property.StartsWith("m_LocalPosition", System.StringComparison.Ordinal) ? s_Position
                : property.StartsWith("m_LocalRotation", System.StringComparison.Ordinal) || property.StartsWith("localEulerAngles", System.StringComparison.Ordinal) ? s_Rotation
                : property.StartsWith("m_LocalScale", System.StringComparison.Ordinal) ? s_Scale : null;

        void Add(ParticleSystem system, LayerKey key, bool hasKey)
        {
            int at = m_Systems.IndexOf(system);
            if (at >= 0)
            {
                if (hasKey && !m_HasKey[at] && typeof(ISimulationState).IsAssignableFrom(key.SlotType))
                {
                    m_Keys[at] = key;
                    m_HasKey[at] = true;
                }
                return;
            }
            m_Systems.Add(system);
            bool state = hasKey && typeof(ISimulationState).IsAssignableFrom(key.SlotType);
            m_Keys.Add(state ? key : default);
            m_HasKey.Add(state);
            // a play-on-awake system plays from the first record — the runtime's system that already runs
            if (system.main.playOnAwake && system.isStopped)
                system.Play(true);
        }

        ISimulationState StateOf(int i)
            => m_HasKey[i] && m_Layers.TryFind(m_Keys[i], out LayerSlot slot) ? slot as ISimulationState : null;

        bool UnderAnother(ParticleSystem system)
        {
            for (Transform t = system.transform.parent; t != null; t = t.parent)
            {
                if (t.TryGetComponent(out ParticleSystem up) && m_Systems.Contains(up))
                    return true;
            }
            return false;
        }

        internal void Step(FeedbackContext context, float scaled, float unscaled)
        {
            PoolService pools = context.PoolsIfMade;
            if (pools != null)
            {
                for (int i = 0; i < pools.LiveCount; i++)
                {
                    GameObject g = pools.LiveAt(i);
                    if (g == null || !m_Scanned.Add(g))
                        continue;
                    g.GetComponentsInChildren(true, m_Found);
                    foreach (ParticleSystem s in m_Found)
                        Add(s, default, false);
                }
            }
            for (int i = 0; i < m_Systems.Count; i++)
            {
                ParticleSystem s = m_Systems[i];
                if (s == null || !s.gameObject.activeInHierarchy || UnderAnother(s) || s.isStopped)
                    continue;
                ISimulationState state = StateOf(i);
                if (state != null && state.Held)
                    continue;
                s.Simulate(s.main.useUnscaledTime ? unscaled : scaled, true, false, false);
            }
            for (int i = 0; i < m_Animators.Count; i++)
            {
                Animator a = m_Animators[i];
                if (a == null || !a.isActiveAndEnabled || a.runtimeAnimatorController == null)
                    continue;
                a.Update(a.updateMode == AnimatorUpdateMode.UnscaledTime ? unscaled : scaled);
            }
        }

        // the automatic end waits while a driven system shows something — its particles, or a play that goes on (a looping
        // one until the stop · a one-shot within its duration)
        internal bool Alive
        {
            get
            {
                for (int i = 0; i < m_Systems.Count; i++)
                {
                    ParticleSystem s = m_Systems[i];
                    if (s == null || !s.gameObject.activeInHierarchy || UnderAnother(s))
                        continue;
                    ISimulationState state = StateOf(i);
                    if (state != null && state.Held)
                        continue;
                    if (s.particleCount > 0)
                        return true;
                    bool emits = state != null ? state.Emits : !s.isStopped;
                    if (emits && !s.isStopped && (s.main.loop || s.time < s.main.duration))
                        return true;
                }
                return false;
            }
        }

        internal void End()
        {
            foreach (ParticleSystem s in m_Systems)
            {
                if (s == null)
                    continue;
                s.Simulate(0f, true, true, false);
                s.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }
            for (int i = 0; i < m_Animators.Count; i++)
            {
                Animator a = m_Animators[i];
                if (a == null)
                    continue;
                a.speed = m_Speeds[i];
                a.fireEvents = m_Fires[i];
                if (a.isInitialized)
                    a.Rebind();
            }
            m_Animators.Clear();
            m_Speeds.Clear();
            m_Fires.Clear();
            m_Logged = false;
            m_Systems.Clear();
            m_Keys.Clear();
            m_HasKey.Clear();
            m_Scanned.Clear();
        }
    }
}
