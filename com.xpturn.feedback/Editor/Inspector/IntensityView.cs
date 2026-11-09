using System.Text;
using UnityEngine;
using UnityEngine.UIElements;

namespace xpTURN.Feedback.Editor
{
    // the intensity — the static factor (a Play's default argument 1 through the remap, × the player's intensity) with the
    // settings' scales of the categories the list uses, and the last play's value: the record of the highest Serial, ended
    // or not (gone at a Rebuild). That value is the play's — an item's output randomness, intensity response and category
    // scale are not in it. Seen again every 0.25 s
    internal sealed class IntensityView : VisualElement
    {
        internal const string LastNote = "the play's intensity — argument → remap → × the player's intensity × the distance falloff; an item's output randomness, intensity response and category scale come after";

        readonly FeedbackPlayer m_Player;
        internal readonly Label Static = new Label();
        internal readonly Label Last = new Label();

        internal IntensityView(FeedbackPlayer player)
        {
            m_Player = player;
            AddToClassList(InspectorStyles.Intensity);
            Last.tooltip = LastNote;
            Add(Static);
            Add(Last);
            Refresh();
            schedule.Execute(Refresh).Every(250);
        }

        internal void Refresh()
        {
            if (m_Player == null)
                return;
            Static.text = StaticText(m_Player, SettingsValues.Load(ContextKind.Runtime));
            Last.text = LastIntensity(m_Player, out float last) ? "Last play: " + last.ToString("0.###") : "Last play: —";
        }

        // a Play's default argument (1) through the remap, × the player's intensity
        internal static float StaticFactor(FeedbackPlayer player)
        {
            float intensity = 1f;
            if (player.m_RemapIntensity && player.m_IntensityRemap != null)
            {
                float remapped = player.m_IntensityRemap.Evaluate(1f);
                intensity = remapped >= 0f && remapped < float.PositiveInfinity ? remapped : 0f;
            }
            return intensity * PlayerRuntime.Factor(player.m_Intensity);
        }

        internal static string StaticText(FeedbackPlayer player, SettingsValues settings)
        {
            var sb = new StringBuilder("Intensity: ");
            sb.Append(StaticFactor(player).ToString("0.###"));
            bool[] seen = new bool[SettingsValues.CategoryCount];
            foreach (FeedbackItem item in player.Items)
            {
                if (!(item is EffectItem e))
                    continue;
                int c = (int)e.EffectiveCategory;
                if (c <= 0 || c >= SettingsValues.CategoryCount || seen[c])
                    continue;
                seen[c] = true;
                if (!settings.Enabled[c])
                    sb.Append(" · ").Append(e.EffectiveCategory).Append(" off");
                else if (settings.Scale[c] != 1f)
                    sb.Append(" · ").Append(e.EffectiveCategory).Append(" ×").Append(settings.Scale[c].ToString("0.###"));
            }
            return sb.ToString();
        }

        // the last play — the record of the highest Serial, ended or not
        internal static bool LastIntensity(FeedbackPlayer player, out float intensity)
        {
            intensity = 0f;
            PlayerRuntime runtime = player != null ? player.Runtime : null;
            if (runtime == null || runtime.IsReleased)
                return false;
            long serial = 0;
            for (int r = 0; r < runtime.RecordCount; r++)
            {
                PlaybackRecord record = runtime.Record(r);
                if (record.Serial > serial)
                {
                    serial = record.Serial;
                    intensity = record.Intensity;
                }
            }
            return serial > 0;
        }
    }
}
