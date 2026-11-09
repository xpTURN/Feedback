using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

namespace xpTURN.Feedback.Editor
{
    // an item's fields — the shown ones (ItemRules), the target (TargetRefField), the motion (MotionField — a change builds
    // the body again) and its parameters (a Vector4 is the target's value kind — ValueField · an Ease the thumbnails —
    // EaseField), the advanced half folded. Built when the row opens
    internal sealed class ItemBody : VisualElement
    {
        internal const string AdvancedKeySuffix = ".Advanced";
        // a target that weighs the motion's shape toward its own fields (WeightTowardTarget)
        internal const string ShapeOnlyNotice = "This target weighs the motion's shape toward the item's fields — the motion's size (amplitude · remap) is not used.";

        internal readonly List<string> Fields = new List<string>();
        internal Foldout Advanced;

        internal ItemBody()
        {
            AddToClassList(InspectorStyles.Body);
        }

        internal void Build(SerializedProperty item, string foldKey, UnityEngine.Component host)
        {
            Clear();
            Fields.Clear();
            var value = item.managedReferenceValue as FeedbackItem;
            if (value == null)
                return;
            Advanced = new Foldout { text = "Advanced", value = SessionState.GetBool(foldKey + AdvancedKeySuffix, false) };
            Advanced.RegisterValueChangedCallback(e =>
            {
                if (e.target == Advanced)
                    SessionState.SetBool(foldKey + AdvancedKeySuffix, e.newValue);
            });
            foreach (string field in FieldsOf(item, value))
            {
                if (field == "m_Motion")
                {
                    AddMotion(item, value, () => Build(item, foldKey, host));
                    continue;
                }
                SerializedProperty p = item.FindPropertyRelative(field);
                if (p == null)
                    continue;
                if (field == "m_Target")
                {
                    Add(new TargetRefField(p, host, TargetChecks.Requires(value.GetType())));
                    Fields.Add(field);
                    continue;
                }
                // a transform item's second reference
                if (p.propertyType == SerializedPropertyType.Generic && p.type == nameof(TargetRef))
                {
                    var reference = new TargetRefField(p, host, typeof(UnityEngine.Transform), ItemRules.Label(value, field) ?? p.displayName);
                    (ItemRules.Advanced(field) ? Advanced.contentContainer : this).Add(reference);
                    Fields.Add(field);
                    continue;
                }
                // a Component Enabled's component (its index with it) · a Set Layer's layer
                if (value is ComponentEnabledItem && field == "m_ComponentType")
                {
                    Add(new ComponentPickField(item, host));
                    Fields.Add(field);
                    continue;
                }
                if (value is SetLayerItem && field == "m_Layer")
                {
                    var layer = new LayerField(ItemRules.Label(value, field) ?? p.displayName);
                    layer.BindProperty(p);
                    Add(layer);
                    Fields.Add(field);
                    continue;
                }
                // an animator item's names — picked from the target's controller
                AnimatorNameKind? name = AnimatorName(value, field);
                if (name != null)
                {
                    Add(new AnimatorNameField(item, field, ItemRules.Label(value, field) ?? p.displayName, host, name.Value));
                    Fields.Add(field);
                    continue;
                }
                var f = new PropertyField(p, ItemRules.Label(value, field));
                f.BindProperty(p);
                // a Parameter's kind and pick choose its other fields · a 2D force's kind
                if ((value is AnimatorParameterItem && (field == "m_Kind" || field == "m_Pick")) || (value is Rigidbody2DForceItem && field == "m_Kind"))
                    f.TrackPropertyValue(p, _ => Build(item, foldKey, host));
                (ItemRules.Advanced(field) ? Advanced.contentContainer : this).Add(f);
                Fields.Add(field);
            }
            if (Advanced.childCount > 0)
                Add(Advanced);
        }

        static AnimatorNameKind? AnimatorName(FeedbackItem value, string field)
        {
            if (field == "m_Parameter" && (value is AnimatorParameterItem || value is AnimatorFloatItem))
                return AnimatorNameKind.Parameter;
            if (field == "m_State" && value is AnimatorStateItem)
                return AnimatorNameKind.State;
            if (field == "m_Layer" && (value is AnimatorLayerWeightItem || value is AnimatorStateItem))
                return AnimatorNameKind.Layer;
            return null;
        }

        // the motion and its parameters, one level under m_Params — after the target, before the timing
        void AddMotion(SerializedProperty item, FeedbackItem value, System.Action changed)
        {
            var target = value as TargetItem;
            if (target == null)
                return;
            Add(new MotionField(item, changed));
            SerializedProperty motion = item.FindPropertyRelative("m_Motion");
            SerializedProperty parameters = motion?.FindPropertyRelative("m_Params");
            if (target.m_Motion == null || parameters == null)
                return;
            ValueKind kind = target.Traits.ValueKind;
            bool weighs = target.Traits.ApplyMode == ApplyMode.WeightTowardTarget;
            bool told = false;
            SerializedProperty it = parameters.Copy();
            SerializedProperty end = parameters.GetEndProperty();
            bool enter = true;
            while (it.NextVisible(enter) && !SerializedProperty.EqualContents(it, end))
            {
                enter = false;
                string label = ItemRules.Label(value, it.name) ?? it.displayName;
                if (it.propertyType == SerializedPropertyType.Vector4)
                    Add(new ValueField(it, kind, label));
                else if (it.propertyType == SerializedPropertyType.Generic && it.type == EaseField.TypeName)
                    Add(new EaseField(it, label));
                else
                {
                    var f = new PropertyField(it.Copy(), label);
                    f.BindProperty(it.Copy());
                    Add(f);
                }
                Fields.Add("m_Motion.m_Params." + it.name);
                // said once, beside the first size field
                if (weighs && !told && (it.name == "m_Amplitude" || it.name == "m_RemapZero" || it.name == "m_RemapOne"))
                {
                    Add(new HelpBox(ShapeOnlyNotice, HelpBoxMessageType.Info));
                    told = true;
                }
            }
        }

        // the item's fields — timing · conditions · random · a flow item's play one level in — in order: label, target, motion,
        // timing, the rest
        internal static List<string> FieldsOf(SerializedProperty item, FeedbackItem value)
        {
            var fields = new List<string>();
            SerializedProperty it = item.Copy();
            SerializedProperty end = item.GetEndProperty();
            bool enter = true;
            while (it.NextVisible(enter) && !SerializedProperty.EqualContents(it, end))
            {
                enter = false;
                string name = it.name;
                if (name == "m_Timing" || name == "m_Conditions" || name == "m_Random" || name == "m_Play")
                {
                    SerializedProperty child = it.Copy();
                    SerializedProperty childEnd = it.GetEndProperty();
                    bool enterChild = true;
                    while (child.NextVisible(enterChild) && !SerializedProperty.EqualContents(child, childEnd))
                    {
                        enterChild = false;
                        string path = name + "." + child.name;
                        if (ItemRules.Visible(value, path))
                            fields.Add(path);
                    }
                    continue;
                }
                if (name == "m_Motion" || ItemRules.Visible(value, name))
                    fields.Add(name);
            }
            // the serialized order is the base classes' first — the body puts what the item does first
            var ordered = new List<string>(fields.Count);
            for (int rank = 0; rank < 5; rank++)
            {
                foreach (string f in fields)
                {
                    if (Rank(f) == rank)
                        ordered.Add(f);
                }
            }
            return ordered;
        }

        static int Rank(string field)
        {
            switch (field)
            {
                case "m_Label": return 0;
                case "m_Target": return 1;
                case "m_Motion": return 2;
            }
            return field.StartsWith("m_Timing", System.StringComparison.Ordinal) ? 3 : 4;
        }
    }
}
