using System.IO;
using UnityEditor;
using UnityEngine;

namespace xpTURN.Feedback.Samples.Editor
{
    // the README a card makes — a sample's README.md next to its card is this text (a test compares)
    public static class SampleReadme
    {
        public static string Text(SampleCard card)
            => "# " + card.Title + "\n\n## Try\n\n" + card.Try + "\n\n## See\n\n" + card.See + "\n\n## Controls\n\n" + card.Controls + "\n"
               + "\n## HUD\n\n" + Hud + "\n"
               + (string.IsNullOrEmpty(card.RequiresText) ? "" : "\n## Requires\n\n" + card.RequiresText + "\n");

        // the HUD's keys (SampleHud shows the same on screen)
        internal const string Hud = "Tab · R3 — feedback on/off (Before/After)\n[ ] · LB RB — intensity\n, . · LT RT — time scale\nEsc · Start · Select — back to the hub\nH · L3 — fold the HUD";

        public static string PathOf(SampleCard card) => Path.Combine(Path.GetDirectoryName(AssetDatabase.GetAssetPath(card)), "README.md");

        [MenuItem("Tools/xpTURN/Feedback/Samples/Write Sample READMEs")]
        static void WriteAll()
        {
            int n = 0;
            foreach (SampleCatalog catalog in SampleBuild.Catalogs())
                foreach (SampleCard card in catalog.Cards)
                    if (card != null)
                    {
                        File.WriteAllText(PathOf(card), Text(card));
                        n++;
                    }
            AssetDatabase.Refresh();
            Debug.Log("Wrote " + n + " sample READMEs.");
        }
    }
}
