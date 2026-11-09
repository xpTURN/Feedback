using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace xpTURN.Feedback.Editor
{
    // one style sheet from the package path, and the class names the views and the tests share
    internal static class InspectorStyles
    {
        internal const string SheetPath = "Packages/com.xpturn.feedback/Editor/Styles/xpTURN.Feedback.Inspector.uss";

        internal const string Root = "xpturn-feedback-inspector";
        internal const string Status = "xpturn-feedback-status";
        internal const string Settings = "xpturn-feedback-settings";
        internal const string OneAtATime = "xpturn-feedback-one-at-a-time";
        internal const string List = "xpturn-feedback-list";
        internal const string Item = "xpturn-feedback-item";
        internal const string ItemOff = "xpturn-feedback-item--off";
        internal const string Header = "xpturn-feedback-item-header";
        internal const string Summary = "xpturn-feedback-item-summary";
        internal const string Body = "xpturn-feedback-item-body";
        internal const string Category = "xpturn-feedback-category";
        internal const string Row = "xpturn-feedback-row";
        internal const string Grow = "xpturn-feedback-grow";
        internal const string Dimmed = "xpturn-feedback-dimmed";
        internal const string Bottom = "xpturn-feedback-bottom";
        internal const string AddButton = "xpturn-feedback-add";
        internal const string PasteButton = "xpturn-feedback-paste";
        internal const string Events = "xpturn-feedback-events";
        internal const string Hint = "xpturn-feedback-hint";
        internal const string Intensity = "xpturn-feedback-intensity";
        internal const string AddMenu = "xpturn-feedback-add-menu";
        internal const string AddMenuSearch = "xpturn-feedback-add-menu-search";
        internal const string AddMenuStar = "xpturn-feedback-add-menu-star";
        internal const string EaseLabel = "xpturn-feedback-ease-label";
        internal const string EaseThumb = "xpturn-feedback-ease-thumb";
        internal const string EaseGrid = "xpturn-feedback-ease-grid";
        internal const string EaseCell = "xpturn-feedback-ease-cell";
        internal const string Strip = "xpturn-feedback-strip";
        internal const string StripCanvas = "xpturn-feedback-strip-canvas";
        internal const string StripBand = "xpturn-feedback-strip-band";
        // the validation's slot, panels and rows
        internal const string ValidationWarning = "xpturn-feedback-validation-warning";
        internal const string ValidationResults = "xpturn-feedback-validation-results";
        internal const string ValidationSummary = "xpturn-feedback-validation-summary";
        internal const string ValidationHost = "xpturn-feedback-validation-host";
        internal const string ValidationRow = "xpturn-feedback-validation-row";
        internal const string ValidationIcon = "xpturn-feedback-validation-icon";
        internal const string ValidationText = "xpturn-feedback-validation-text";
        internal const string ValidationFix = "xpturn-feedback-validation-fix";
        internal const string ValidationPick = "xpturn-feedback-validation-pick";
        internal const string Severity = "xpturn-feedback-severity";
        // the debugger's lists: a line above and below, a space after
        internal const string DebuggerList = "xpturn-feedback-debugger-list";
#if UNITY_6000_3_OR_NEWER
        internal const string MenuArrowIcon = "ToolsGroupArrow";
#else
        internal const string MenuArrowIcon = "icon dropdown";
#endif

        static StyleSheet s_Sheet;

        internal static void Attach(VisualElement element)
        {
            if (s_Sheet == null)
                s_Sheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(SheetPath);
            if (s_Sheet != null && !element.styleSheets.Contains(s_Sheet))
                element.styleSheets.Add(s_Sheet);
        }

        // a category's colour is a class — the sheet holds the colours
        internal static string CategoryClass(FeedbackCategory category) => Category + "--" + category.ToString().ToLowerInvariant();

        // the same colours for the strip's painter (the sheet's category classes)
        internal static Color CategoryColor(FeedbackCategory category)
        {
            switch (category)
            {
                case FeedbackCategory.Motion: return new Color32(77, 153, 255, 255);
                case FeedbackCategory.Camera: return new Color32(153, 102, 255, 255);
                case FeedbackCategory.Flash: return new Color32(255, 204, 51, 255);
                case FeedbackCategory.PostFX: return new Color32(255, 102, 179, 255);
                case FeedbackCategory.Time: return new Color32(255, 128, 51, 255);
                case FeedbackCategory.Particles: return new Color32(102, 204, 102, 255);
                case FeedbackCategory.UI: return new Color32(51, 204, 204, 255);
                case FeedbackCategory.Haptics: return new Color32(204, 153, 102, 255);
                default: return new Color32(128, 128, 128, 255);
            }
        }
    }
}
