using System.Collections.Generic;
using UnityEngine;

namespace xpTURN.Feedback.Tests
{
    // a script on the player's object, before the player, that calls the player's entry from its own callbacks — what a
    // user's script does with Play
    public sealed class PlayerEarlyCaller : MonoBehaviour
    {
        internal FeedbackPlayer Player;
        internal readonly List<string> Seen = new List<string>();

        string Call(string where)
        {
            bool before = Player.Runtime != null;
            bool result = Player.EnsureRuntime();
            return $"{where} {(before ? "prepared" : "none")}→ensure {result}";
        }

        void Awake() => Seen.Add(Call("Awake"));
        void OnEnable() => Seen.Add(Call("OnEnable"));
        void Start() => Seen.Add(Call("Start"));
    }
}
