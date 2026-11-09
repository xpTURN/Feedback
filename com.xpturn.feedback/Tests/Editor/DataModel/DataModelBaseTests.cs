using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace xpTURN.Feedback.Tests.Editor
{
    [Serializable]
    public sealed class DataModelTestTarget : TargetItem
    {
        static readonly TargetTraits s_Traits = new TargetTraits(valueKind: ValueKind.Vector3, supportedMotions: MotionMask.Tween | MotionMask.Punch,
            category: FeedbackCategory.Motion, flashSensitive: false, applyMode: ApplyMode.Offset);

        internal override TargetTraits Traits => s_Traits;
    }

    [Serializable]
    public sealed class DataModelTestMotion : FeedbackMotion
    {
        internal override MotionKind Kind => MotionKind.Set;
    }

    // the serialization contract
    public sealed class DataModelBaseTests
    {
        const string AssetPath = "Assets/DataModelBaseTests.asset";

        // the values are saved: add members at the end and rows here
        static readonly (Type Enum, string Member, int Value)[] Golden =
        {
            (typeof(StartRule), "WithPrevious", 0), (typeof(StartRule), "AfterPrevious", 1), (typeof(StartRule), "AfterAll", 2),
            (typeof(ItemClock), "Player", 0), (typeof(ItemClock), "Scaled", 1), (typeof(ItemClock), "Unscaled", 2),
            (typeof(DirectionCondition), "Always", 0), (typeof(DirectionCondition), "ForwardOnly", 1), (typeof(DirectionCondition), "ReverseOnly", 2),
            (typeof(ItemPlayDirection), "FollowPlayer", 0), (typeof(ItemPlayDirection), "Forward", 1), (typeof(ItemPlayDirection), "Backward", 2), (typeof(ItemPlayDirection), "Opposite", 3),
            (typeof(IntensityResponse), "Receive", 0), (typeof(IntensityResponse), "Ignore", 1),
            (typeof(FeedbackCategory), "None", 0), (typeof(FeedbackCategory), "Motion", 1), (typeof(FeedbackCategory), "Camera", 2),
            (typeof(FeedbackCategory), "Flash", 3), (typeof(FeedbackCategory), "PostFX", 4), (typeof(FeedbackCategory), "Time", 5),
            (typeof(FeedbackCategory), "Particles", 6), (typeof(FeedbackCategory), "UI", 7), (typeof(FeedbackCategory), "Haptics", 8),
            (typeof(TargetRefMode), "Direct", 0), (typeof(TargetRefMode), "Self", 1), (typeof(TargetRefMode), "Child", 2),
            (typeof(TargetRefMode), "Parent", 3), (typeof(TargetRefMode), "Anchor", 4), (typeof(TargetRefMode), "MainCamera", 5),
            (typeof(TargetRefMode), "Channel", 6),
            (typeof(ChildLookup), "Path", 0), (typeof(ChildLookup), "Index", 1), (typeof(ChildLookup), "FirstOfType", 2),
            (typeof(ChannelRefMode), "Int", 0), (typeof(ChannelRefMode), "Asset", 1),
            (typeof(MotionKind), "Set", 0), (typeof(MotionKind), "Tween", 1), (typeof(MotionKind), "Punch", 2), (typeof(MotionKind), "Shake", 3),
            (typeof(MotionKind), "Curve", 4), (typeof(MotionKind), "Flash", 5), (typeof(MotionKind), "Gradient", 6), (typeof(MotionKind), "Oscillate", 7),
            (typeof(MotionKind), "Blink", 8), (typeof(MotionKind), "Wiggle", 9), (typeof(MotionKind), "Spring", 10),
            (typeof(PositionSpace), "World", 0), (typeof(PositionSpace), "Local", 1), (typeof(PositionSpace), "Anchored", 2),
            (typeof(TransformSpace), "World", 0), (typeof(TransformSpace), "Local", 1),
            (typeof(RotationInterpolation), "Euler", 0), (typeof(RotationInterpolation), "Shortest", 1),
            (typeof(MaterialWriteMode), "PropertyBlock", 0), (typeof(MaterialWriteMode), "Instance", 1),
            (typeof(SpriteAxis), "X", 0), (typeof(SpriteAxis), "Y", 1), (typeof(LineEnd), "Start", 0), (typeof(LineEnd), "End", 1),
            (typeof(SpawnPlace), "Target", 0), (typeof(SpawnPlace), "World", 1), (typeof(SpawnParent), "None", 0), (typeof(SpawnParent), "Target", 1),
            (typeof(ParticleAction), "Play", 0), (typeof(ParticleAction), "Stop", 1), (typeof(ParticleAction), "Pause", 2), (typeof(ParticleAction), "Clear", 3),
            (typeof(VideoAction), "Play", 0), (typeof(VideoAction), "Pause", 1), (typeof(VideoAction), "Seek", 2),
            (typeof(TMPRevealUnit), "Character", 0), (typeof(TMPRevealUnit), "Word", 1), (typeof(TMPRevealUnit), "Line", 2),
            (typeof(TMPCharacterOrder), "Forward", 0), (typeof(TMPCharacterOrder), "Backward", 1),
            (typeof(ScreenFadeShape), "Plain", 0), (typeof(ScreenFadeShape), "Wipe", 1), (typeof(ScreenFadeShape), "Iris", 2),
            (typeof(ScreenSide), "Left", 0), (typeof(ScreenSide), "Right", 1), (typeof(ScreenSide), "Bottom", 2), (typeof(ScreenSide), "Top", 3),
            (typeof(ScreenFadeCenter), "Screen", 0), (typeof(ScreenFadeCenter), "PlayPosition", 1),
            (typeof(FloatingTextSource), "Text", 0), (typeof(FloatingTextSource), "Intensity", 1),
            (typeof(PostFXDepthOfFieldMode), "Off", 0), (typeof(PostFXDepthOfFieldMode), "Gaussian", 1), (typeof(PostFXDepthOfFieldMode), "Bokeh", 2),
            (typeof(CinemachineImpulseShape), "Custom", 0), (typeof(CinemachineImpulseShape), "Recoil", 1), (typeof(CinemachineImpulseShape), "Bump", 2), (typeof(CinemachineImpulseShape), "Explosion", 3), (typeof(CinemachineImpulseShape), "Rumble", 4),
            (typeof(CinemachineImpulseSpread), "Uniform", 0), (typeof(CinemachineImpulseSpread), "Dissipating", 1), (typeof(CinemachineImpulseSpread), "Propagating", 2),
            (typeof(CinemachineImpulseMode), "Definition", 0), (typeof(CinemachineImpulseMode), "Source", 1),
            (typeof(CinemachineBlendStyle), "Cut", 0), (typeof(CinemachineBlendStyle), "EaseInOut", 1), (typeof(CinemachineBlendStyle), "EaseIn", 2), (typeof(CinemachineBlendStyle), "EaseOut", 3),
            (typeof(CinemachineBlendStyle), "HardIn", 4), (typeof(CinemachineBlendStyle), "HardOut", 5), (typeof(CinemachineBlendStyle), "Linear", 6), (typeof(CinemachineBlendStyle), "Custom", 7),
            (typeof(GamepadRumbleDevice), "Current", 0), (typeof(GamepadRumbleDevice), "All", 1), (typeof(GamepadRumbleDevice), "Player", 2),
            (typeof(ParticleStopMode), "StopEmitting", 0), (typeof(ParticleStopMode), "StopEmittingAndClear", 1),
            (typeof(AnimatorParameterKind), "Trigger", 0), (typeof(AnimatorParameterKind), "Bool", 1), (typeof(AnimatorParameterKind), "Int", 2), (typeof(AnimatorParameterKind), "Float", 3),
            (typeof(AnimatorValuePick), "Value", 0), (typeof(AnimatorValuePick), "Random", 1), (typeof(AnimatorValuePick), "Increment", 2),
            // the engine's ForceMode has other values: the implementation maps them
            (typeof(PhysicsForceMode), "Force", 0), (typeof(PhysicsForceMode), "Acceleration", 1), (typeof(PhysicsForceMode), "Impulse", 2), (typeof(PhysicsForceMode), "VelocityChange", 3),
            (typeof(PhysicsForceMode2D), "Force", 0), (typeof(PhysicsForceMode2D), "Impulse", 1),
            (typeof(PhysicsForceKind), "Force", 0), (typeof(PhysicsForceKind), "Torque", 1),
            (typeof(VelocityWrite), "Set", 0), (typeof(VelocityWrite), "Add", 1),
            // Player Control's stop mode is the first saved StopMode
            (typeof(PlayerControlAction), "Play", 0), (typeof(PlayerControlAction), "PlayBackward", 1), (typeof(PlayerControlAction), "Stop", 2), (typeof(PlayerControlAction), "Pause", 3),
            (typeof(PlayerControlAction), "Resume", 4), (typeof(PlayerControlAction), "Skip", 5), (typeof(PlayerControlAction), "Restore", 6), (typeof(PlayerControlAction), "Signal", 7),
            (typeof(DebugAction), "Log", 0), (typeof(DebugAction), "Warning", 1), (typeof(DebugAction), "Error", 2), (typeof(DebugAction), "Break", 3), (typeof(DebugAction), "Note", 4),
            (typeof(StopMode), "Hold", 0), (typeof(StopMode), "Revert", 1), (typeof(StopMode), "Complete", 2),
            (typeof(SquashAxis), "XtoYZ", 0), (typeof(SquashAxis), "XtoY", 1), (typeof(SquashAxis), "XtoZ", 2), (typeof(SquashAxis), "YtoXZ", 3),
            (typeof(SquashAxis), "YtoX", 4), (typeof(SquashAxis), "YtoZ", 5), (typeof(SquashAxis), "ZtoXY", 6), (typeof(SquashAxis), "ZtoX", 7),
            (typeof(SquashAxis), "ZtoY", 8),
            (typeof(LookAtMode), "Transform", 0), (typeof(LookAtMode), "Position", 1), (typeof(LookAtMode), "Direction", 2),
            (typeof(LookAtUp), "Up", 0), (typeof(LookAtUp), "Forward", 1), (typeof(LookAtUp), "Right", 2),
            // the Math enums Core's motion fields save, and the four parameter enums
            (typeof(EaseType), "Linear", 0), (typeof(EaseType), "SineIn", 1), (typeof(EaseType), "SineOut", 2), (typeof(EaseType), "SineInOut", 3),
            (typeof(EaseType), "QuadIn", 4), (typeof(EaseType), "QuadOut", 5), (typeof(EaseType), "QuadInOut", 6), (typeof(EaseType), "CubicIn", 7),
            (typeof(EaseType), "CubicOut", 8), (typeof(EaseType), "CubicInOut", 9), (typeof(EaseType), "QuartIn", 10), (typeof(EaseType), "QuartOut", 11),
            (typeof(EaseType), "QuartInOut", 12), (typeof(EaseType), "QuintIn", 13), (typeof(EaseType), "QuintOut", 14), (typeof(EaseType), "QuintInOut", 15),
            (typeof(EaseType), "ExpoIn", 16), (typeof(EaseType), "ExpoOut", 17), (typeof(EaseType), "ExpoInOut", 18), (typeof(EaseType), "CircIn", 19),
            (typeof(EaseType), "CircOut", 20), (typeof(EaseType), "CircInOut", 21), (typeof(EaseType), "BackIn", 22), (typeof(EaseType), "BackOut", 23),
            (typeof(EaseType), "BackInOut", 24), (typeof(EaseType), "ElasticIn", 25), (typeof(EaseType), "ElasticOut", 26), (typeof(EaseType), "ElasticInOut", 27),
            (typeof(EaseType), "BounceIn", 28), (typeof(EaseType), "BounceOut", 29), (typeof(EaseType), "BounceInOut", 30), (typeof(EaseType), "Custom", 31),
            (typeof(Waveform), "Sine", 0), (typeof(Waveform), "Triangle", 1), (typeof(Waveform), "Square", 2), (typeof(Waveform), "Saw", 3), (typeof(Waveform), "PingPong", 4),
            (typeof(TweenMode), "To", 0), (typeof(TweenMode), "From", 1), (typeof(TweenMode), "FromTo", 2), (typeof(TweenMode), "By", 3),
            (typeof(ShakeNoise), "Smooth", 0), (typeof(ShakeNoise), "Jitter", 1),
            (typeof(GradientWrite), "Override", 0), (typeof(GradientWrite), "Persist", 1),
            (typeof(SpringMode), "Bump", 0), (typeof(SpringMode), "MoveTo", 1),
            // the defaults are Start · Restart, not 0
            (typeof(PlayerInitialization), "Awake", 0), (typeof(PlayerInitialization), "Start", 1), (typeof(PlayerInitialization), "Lazy", 2),
            (typeof(PlayPolicy), "Ignore", 0), (typeof(PlayPolicy), "Restart", 1), (typeof(PlayPolicy), "Overlap", 2),
            (typeof(PlayerAutoPlay), "None", 0), (typeof(PlayerAutoPlay), "OnStart", 1), (typeof(PlayerAutoPlay), "OnEnable", 2),
            (typeof(PlayerTimeMode), "Scaled", 0), (typeof(PlayerTimeMode), "Unscaled", 1),
            (typeof(DistanceReference), "MainCamera", 0), (typeof(DistanceReference), "Anchor", 1), (typeof(DistanceReference), "PlayPosition", 2),
            (typeof(DistanceReference), "Global", 3),
            (typeof(PlayerEventKind), "Played", 0), (typeof(PlayerEventKind), "Completed", 1), (typeof(PlayerEventKind), "Stopped", 2),
            (typeof(PlayerEventKind), "Paused", 3), (typeof(PlayerEventKind), "Resumed", 4), (typeof(PlayerEventKind), "Looped", 5),
            (typeof(PlayerDirection), "Forward", 0), (typeof(PlayerDirection), "Backward", 1), (typeof(PlayerDirection), "Toggle", 2),
            (typeof(PlayerDisableBehavior), "Continue", 0), (typeof(PlayerDisableBehavior), "StopHold", 1), (typeof(PlayerDisableBehavior), "StopRevert", 2),
            // Global ends DistanceReference (the saved value's end)
            (typeof(FeedbackCommandKind), "Play", 0), (typeof(FeedbackCommandKind), "Stop", 1), (typeof(FeedbackCommandKind), "Pause", 2), (typeof(FeedbackCommandKind), "Resume", 3),
            (typeof(FeedbackCommandKind), "Seek", 4), (typeof(FeedbackCommandKind), "Skip", 5), (typeof(FeedbackCommandKind), "Restore", 6), (typeof(FeedbackCommandKind), "Signal", 7),
            // not saved, but public: a user's switch reads the values
            (typeof(PlaybackEnd), "Completed", 0), (typeof(PlaybackEnd), "Stopped", 1), (typeof(PlaybackEnd), "Faulted", 2), (typeof(PlaybackEnd), "Destroyed", 3),
            (typeof(PlaybackEnd), "ContextShutdown", 4),
            // PlayAsync's own
            (typeof(PlaybackEnd), "Refused", 5),
            (typeof(MotionDriverMode), "Loop", 0), (typeof(MotionDriverMode), "Follow", 1),
            // public, not saved
            (typeof(FeedbackTargetStage), "Update", 0), (typeof(FeedbackTargetStage), "LateUpdate", 1),
            // the extension bases' — public, not saved
            (typeof(CustomEnd), "Completed", 0), (typeof(CustomEnd), "Hold", 1), (typeof(CustomEnd), "Revert", 2), (typeof(CustomEnd), "Complete", 3),
            (typeof(CustomEnd), "Aborted", 4),
            (typeof(CustomPreview), "Skip", 0), (typeof(CustomPreview), "Run", 1),
        };

        [TearDown]
        public void DeleteAsset() => AssetDatabase.DeleteAsset(AssetPath);

        [Test]
        public void EnumValuesMatchTheGolden()
        {
            foreach (var (type, member, value) in Golden)
                Assert.That(Convert.ToInt32(Enum.Parse(type, member)), Is.EqualTo(value), $"{type.Name}.{member}");
            foreach (var type in Golden.Select(g => g.Enum).Distinct())
                Assert.That(Enum.GetNames(type).Length, Is.EqualTo(Golden.Count(g => g.Enum == type)), $"{type.Name} has a member without a row");
        }

        // the table covers every package enum a saved field uses — one way: an enum only in the table (MotionKind) is fine
        [Test]
        public void EverySavedEnumHasGoldenRows()
        {
            var package = new[] { typeof(FeedbackItem).Assembly, typeof(StartRule).Assembly };
            var pinned = new HashSet<Type>(Golden.Select(g => g.Enum));
            var missing = typeof(FeedbackItem).Assembly.GetTypes()
                .Where(DataModelTraitsTests.IsSavedType)
                .SelectMany(DataModelTraitsTests.SavedFieldInfos)
                .Select(f => OptionalAssemblyTests.ElementType(f.FieldType))
                .Where(t => t.IsEnum && package.Contains(t.Assembly) && !pinned.Contains(t))
                .Select(t => t.Name)
                .Distinct();
            Assert.That(missing, Is.Empty, "a saved enum without rows — its values are not pinned");
        }

        // and every public enum of Core and Math has rows — public values are only added at the end too
        [Test]
        public void EveryPublicEnumHasGoldenRows()
        {
            var pinned = new HashSet<Type>(Golden.Select(g => g.Enum));
            var missing = new[] { typeof(FeedbackItem).Assembly, typeof(StartRule).Assembly }.Distinct()
                .SelectMany(a => a.GetExportedTypes())
                .Where(t => t.IsEnum && !pinned.Contains(t))
                .Select(t => t.Name);
            Assert.That(missing, Is.Empty, "a public enum without rows — its values are not pinned");
        }

        // a struct made on its own is zero; the defaults come from the owner
        [Test]
        public void NewItemsCarryTheDefaults()
        {
            var item = new DataModelTestTarget();
            Assert.That(item.m_Active, Is.True);
            Assert.That(item.m_Label, Is.EqualTo(""));

            var t = item.m_Timing;
            Assert.That((t.m_Rule, t.m_Delay, t.m_Duration, t.m_Infinite, t.m_Clock), Is.EqualTo((StartRule.WithPrevious, 0f, 0.2f, false, ItemClock.Player)));
            Assert.That((t.m_RepeatCount, t.m_RepeatForever, t.m_RepeatGap, t.m_Background), Is.EqualTo((0, false, 0f, false)));

            var c = item.m_Conditions;
            Assert.That((c.m_Chance, c.m_Cooldown, c.m_MaxPlays, c.m_Direction), Is.EqualTo((100f, 0f, 0, DirectionCondition.Always)));
            Assert.That((c.m_UseIntensityRange, c.m_IntensityRange), Is.EqualTo((false, new Vector2(0f, 1f))));

            var r = item.m_Random;
            Assert.That((r.m_RandomizeOutput, r.m_OutputRange, r.m_RandomizeDuration, r.m_DurationRange),
                Is.EqualTo((false, new Vector2(0.8f, 1f), false, new Vector2(0.5f, 2f))));

            Assert.That(item.m_Intensity, Is.EqualTo(IntensityResponse.Receive));
            Assert.That((item.m_Category.m_Override, item.m_Category.m_Category), Is.EqualTo((false, FeedbackCategory.None)));

            var target = item.m_Target;
            Assert.That((target.m_Mode, target.m_Object, target.m_Path, target.m_Index, target.m_ChildLookup, target.m_Anchor),
                Is.EqualTo((TargetRefMode.Direct, (UnityEngine.Object)null, "", 0, ChildLookup.Path, "")));
            Assert.That((target.m_Channel.m_Mode, target.m_Channel.m_Id, target.m_Channel.m_Asset), Is.EqualTo((ChannelRefMode.Int, 0, (FeedbackChannel)null)));
            Assert.That(item.m_Motion, Is.Null);
            Assert.That(item.m_DirectWrite, Is.False);

            Assert.That(default(ItemConditions).m_Chance, Is.Zero, "a struct made on its own has no defaults — start from ItemConditions.Default");
        }

        // the same mode and the same value only; a missing asset matches nothing
        [Test]
        public void ChannelRefMatchesTheSameModeAndValueOnly()
        {
            var asset = ScriptableObject.CreateInstance<FeedbackChannel>();
            try
            {
                ChannelRef Int(int id) => new ChannelRef { m_Mode = ChannelRefMode.Int, m_Id = id };
                ChannelRef Asset(FeedbackChannel a) => new ChannelRef { m_Mode = ChannelRefMode.Asset, m_Asset = a };

                Assert.That(Int(3).Matches(Int(3)), Is.True);
                Assert.That(Int(3).Matches(Int(4)), Is.False);
                Assert.That(Asset(asset).Matches(Asset(asset)), Is.True);
                Assert.That(Asset(asset).Matches(Int(0)), Is.False, "different modes");
                Assert.That(Asset(null).Matches(Asset(null)), Is.False, "a missing asset matches nothing");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(asset);
            }
        }

        // a field missing from saved data keeps its initial value; the others keep the saved values
        [Test]
        public void FieldsMissingFromSavedDataTakeTheInitialValues()
        {
            var holder = ScriptableObject.CreateInstance<DataModelTestHolder>();
            var item = new DataModelTestTarget { m_Active = false, m_Motion = new DataModelTestMotion() };
            item.m_Timing.m_Duration = 5f;
            item.m_Timing.m_Delay = 1.5f;
            item.m_Conditions.m_Chance = 30f;
            holder.items.Add(item);
            AssetDatabase.CreateAsset(holder, AssetPath);
            AssetDatabase.SaveAssets();

            string file = Path.GetFullPath(AssetPath);
            string text = File.ReadAllText(file);
            var line = new Regex(@"^[ \t]*(m_Active|m_Duration|m_Chance): .*\r?\n", RegexOptions.Multiline);
            Assert.That(line.Matches(text).Count, Is.EqualTo(3), text);
            File.WriteAllText(file, line.Replace(text, ""));

            Resources.UnloadAsset(holder);
            AssetDatabase.ImportAsset(AssetPath, ImportAssetOptions.ForceUpdate);
            var loaded = AssetDatabase.LoadAssetAtPath<DataModelTestHolder>(AssetPath);
            var back = (DataModelTestTarget)loaded.items[0];

            Assert.That(back.m_Active, Is.True);
            Assert.That(back.m_Timing.m_Duration, Is.EqualTo(0.2f));
            Assert.That(back.m_Conditions.m_Chance, Is.EqualTo(100f));
            Assert.That(back.m_Timing.m_Delay, Is.EqualTo(1.5f), "a field still in the data keeps the saved value");
            Assert.That(back.m_Motion, Is.InstanceOf<DataModelTestMotion>());
        }
    }
}
