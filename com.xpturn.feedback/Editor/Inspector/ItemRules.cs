namespace xpTURN.Feedback.Editor
{
    // which of an item's fields the body shows and under which name — paths relative to the item. A field is hidden where
    // nothing reads it (a field changed, the nominal total did not)
    internal static class ItemRules
    {
        internal const string Delay = "m_Timing.m_Delay";
        internal const string Duration = "m_Timing.m_Duration";
        internal const string Infinite = "m_Timing.m_Infinite";
        internal const string RepeatCount = "m_Timing.m_RepeatCount";
        internal const string RepeatForever = "m_Timing.m_RepeatForever";
        internal const string RepeatGap = "m_Timing.m_RepeatGap";
        internal const string Background = "m_Timing.m_Background";

        // the flow markers — a loop's two ends and Hold: no length of their own
        internal static bool Marker(FeedbackItem item) => item is LoopStartItem || item is LoopEndItem || item is HoldItem;

        internal static bool Visible(FeedbackItem item, string field)
        {
            if (item == null)
                return false;
            // a user's feedback's own fields are the user's — shown whatever their names
            if (UserField(item, field))
                return true;
            bool action = item is ActionItem;
            switch (field)
            {
                case "m_Active":
                case "m_Motion":
                    // the header holds the switch · the motion's parameters are drawn on their own
                    return false;
                case Delay:
                    // no timing field reaches a Loop End
                    return !(item is LoopEndItem);
                case Duration:
                case Infinite:
                    // an action item has its own length · a signal wait has its time limit
                    return !Marker(item) && !action && !(item is WaitForSignalItem);
                case RepeatCount:
                case RepeatForever:
                case RepeatGap:
                    return !Marker(item);
                case Background:
                    // a background Wait, signal wait or instant action changes nothing (a background wait on a player — FB-O07)
                    return !Marker(item) && !(item is WaitItem) && !(item is WaitForSignalItem) && !(item is UnityEventItem) && !(item is RandomUnityEventItem)
                        && !(item is SignalBroadcastItem) && !(item is DebugItem) && !(item is PlayerControlItem);
                case "m_Intensity":
                case "m_Random.m_RandomizeOutput":
                case "m_Random.m_OutputRange":
                    // nothing reads an action item's output — but a flow item that plays a player hands it on, a signal
                    // multiplies its value by it
                    return !action || PassesIntensity(item);
                case "m_Target":
                    // a global item has no target
                    return !TargetChecks.Global(item.GetType());
                case "m_DirectWrite":
                    // a global's private slot loses the first value at the close · a state, a toggle, a sheet's own slot and a
                    // clear read none · nor a spawn, a despawn · nor a text's characters — one shared slot per text
                    return item is TargetItem t && !(item is SetActiveItem) && !(item is SetParentItem) && !(item is RendererVisibilityItem) && !(item is MaterialSwapItem)
                        && !(item is SpriteSwapItem) && !(item is SpriteFlipItem) && !(item is SpriteSheetItem) && !(item is TrailEmittingItem) && !(item is TrailClearItem)
                        && !(item is ComponentEnabledItem) && !(item is SpawnItem) && !(item is DespawnItem) && !(item is SetLayerItem)
                        && !(item is ParticleControlItem) && !(item is ParticleEmitItem) && !(item is ParticleSpawnItem)
                        && !(item is AnimatorParameterItem) && !(item is AnimatorStateItem)
                        && !(item is RigidbodyForceItem) && !(item is RigidbodyExplosionItem) && !(item is RigidbodyVelocityItem)
                        && !(item is Rigidbody2DForceItem) && !(item is Rigidbody2DVelocityItem)
                        && !(item is TMPCharacterOffsetItem) && !(item is TMPCharacterScaleItem) && !(item is TMPCharacterAlphaItem)
                        && !(item is CustomEffectItem)
                        && !TargetChecks.Global(item.GetType()) && t.m_Target.m_Mode != TargetRefMode.Channel;
                case "m_ComponentIndex":
                    // drawn with the component
                    return !(item is ComponentEnabledItem);
                case "m_StopMode":
                    // read by a Stop only
                    if (item is PlayerControlItem playerControl)
                        return playerControl.m_Action == PlayerControlAction.Stop;
                    return !(item is ParticleControlItem control) || control.m_Action == ParticleAction.Stop;
                // a chain's entries wait each on their own · the time limit is a wait's
                case "m_Play.m_Wait":
                    return !(item is PlayerChainItem);
                case "m_Play.m_Timeout":
                    return item is ActionItem waiting && waiting.Opens;
                // a Parameter's kind and pick read their own (a bool's increment is its value)
                case "m_Pick":
                    return !(item is AnimatorParameterItem pick) || pick.m_Kind != AnimatorParameterKind.Trigger;
                case "m_Bool":
                    return !(item is AnimatorParameterItem b) || (b.m_Kind == AnimatorParameterKind.Bool && b.m_Pick != AnimatorValuePick.Random);
                case "m_Int":
                    return !(item is AnimatorParameterItem n) || (n.m_Kind == AnimatorParameterKind.Int && n.m_Pick == AnimatorValuePick.Value);
                case "m_Float":
                    return !(item is AnimatorParameterItem v) || (v.m_Kind == AnimatorParameterKind.Float && v.m_Pick == AnimatorValuePick.Value);
                case "m_Min":
                case "m_Max":
                    // a 2D force's torque reads its own
                    if (item is Rigidbody2DForceItem force)
                        return force.m_Kind == PhysicsForceKind.Force;
                    return !(item is AnimatorParameterItem range) || (Numeric(range) && range.m_Pick == AnimatorValuePick.Random);
                case "m_Space":
                    return !(item is Rigidbody2DForceItem space) || space.m_Kind == PhysicsForceKind.Force;
                case "m_MinTorque":
                case "m_MaxTorque":
                    return !(item is Rigidbody2DForceItem torque) || torque.m_Kind == PhysicsForceKind.Torque;
                case "m_Step":
                    return !(item is AnimatorParameterItem step) || (Numeric(step) && step.m_Pick == AnimatorValuePick.Increment);
                case "m_Interpolation":
                    // read by Tween only
                    return item is RotationItem r && r.m_Motion is TweenMotion;
            }
            return true;
        }

        static bool Numeric(AnimatorParameterItem item) => item.m_Kind == AnimatorParameterKind.Int || item.m_Kind == AnimatorParameterKind.Float;

        // a field a user's type declares (the path's first name) — not one of the package's bases
        static bool UserField(FeedbackItem item, string field)
        {
            if (!FeedbackCatalog.IsCustom(item.GetType()))
                return false;
            int dot = field.IndexOf('.');
            string name = dot < 0 ? field : field.Substring(0, dot);
            for (System.Type t = item.GetType(); t != null && t.Assembly != typeof(FeedbackItem).Assembly; t = t.BaseType)
            {
                if (t.GetField(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.DeclaredOnly) != null)
                    return true;
            }
            return false;
        }

        // a user's action reads its intensity (CustomPlay.Intensity — the play's × the random output)
        static bool PassesIntensity(FeedbackItem item)
            => item is CustomActionItem || item is PlayPlayerItem || item is RandomPlayerItem || item is PlayerChainItem || item is SignalBroadcastItem
            || (item is PlayerControlItem control && (control.m_Action == PlayerControlAction.Play || control.m_Action == PlayerControlAction.PlayBackward));

        static bool PhysicsRange(FeedbackItem item) => item is RigidbodyForceItem || item is RigidbodyVelocityItem || item is Rigidbody2DForceItem || item is Rigidbody2DVelocityItem;

        // the advanced half of the body (the gradual disclosure: random, repeat, conditions)
        internal static bool Advanced(string field)
            => field == RepeatCount || field == RepeatForever || field == RepeatGap || field == Background || field == "m_Timing.m_Clock"
            || field.StartsWith("m_Conditions", System.StringComparison.Ordinal) || field.StartsWith("m_Random", System.StringComparison.Ordinal)
            || field == "m_Intensity" || field == "m_Category" || field == "m_DirectWrite" || field == "m_PlayDirection";

        // a name other than the field's own — null keeps the field's
        internal static string Label(FeedbackItem item, string field)
        {
            switch (field)
            {
                case RepeatCount:
                    return "Extra Runs";
                case "m_LoopCount":
                    return item is LoopEndItem ? "Total Runs (0 = endless)" : null;
                case "m_Timeout":
                    return item is WaitForSignalItem ? "Time Limit (0 = none)" : null;
                case "m_Relative":
                    // a SetActive's Set flips the play's starting state · a flip's and an emitting's too
                    return item is SetActiveItem || item is SpriteFlipItem || item is TrailEmittingItem || item is ComponentEnabledItem ? "Toggle" : null;
                case "m_NewParent":
                    return item is SetParentItem ? "New Parent (none = the scene root)" : null;
                case "m_Lifetime":
                    return item is SpawnItem ? "Lifetime (0 = until a despawn or a revert)" : null;
                case "m_Pooled":
                    return item is SpawnItem || item is ParticleSpawnItem ? "Use a Pool" : null;
                case "m_StopAfter":
                    return item is ParticleSpawnItem ? "Stop After (0 = never — a looping system needs it)" : null;
                case "m_Count":
                    return item is ParticleEmitItem ? "Count (× the play's intensity)" : null;
                case "m_Transition":
                    return item is AnimatorStateItem ? "Transition (s · 0 = Play)" : null;
                case "m_Offset":
                    return item is AnimatorStateItem ? "Start (s)" : item is RigidbodyExplosionItem ? "Offset (target's local)" : null;
                case "m_Layer":
                    return item is AnimatorStateItem ? "Layer (empty = the first with the state)" : null;
                case "m_Min":
                    return PhysicsRange(item) ? "Min (random per axis × intensity)" : null;
                case "m_Max":
                    return PhysicsRange(item) ? "Max (random per axis × intensity)" : null;
                case "m_MinTorque":
                    return item is Rigidbody2DForceItem ? "Min Torque (N·m · random × intensity)" : null;
                case "m_MaxTorque":
                    return item is Rigidbody2DForceItem ? "Max Torque (N·m · random × intensity)" : null;
                case "m_MinForce":
                    return item is RigidbodyExplosionItem ? "Min Force (random × intensity)" : null;
                case "m_MaxForce":
                    return item is RigidbodyExplosionItem ? "Max Force (random × intensity)" : null;
                case "m_Upwards":
                    return item is RigidbodyExplosionItem ? "Upwards Modifier" : null;
                case "m_MaxBodies":
                    return item is RigidbodyExplosionItem ? "Max Bodies (query buffer)" : null;
                case "m_Triggers":
                    return item is RigidbodyExplosionItem ? "Include Triggers" : null;
                case "m_InvokeInPreview":
                    return item is CustomFloatEventItem ? "Invoke In Preview (listeners set to Editor And Runtime)" : null;
                case "m_Base":
                    return item is CustomFloatEventItem || item is CustomEffectItem ? "Base (offset motions)" : null;
                case "m_Play.m_Wait":
                    return "Wait For Its End";
                case "m_Play.m_Timeout":
                    return "Time Limit (0 = none)";
                case "m_Play.m_PassIntensity":
                    return "Pass The Intensity";
                case "m_Play.m_PassDirection":
                    return "Backward When Played Backward";
                case "m_Play.m_PassPosition":
                    return "Pass The Position";
                case "m_DevelopmentOnly":
                    return item is DebugItem ? "Development Builds Only" : null;
                case "m_Value":
                    return item is SignalBroadcastItem ? "Value (× intensity)" : null;
            }
            return null;
        }
    }
}
