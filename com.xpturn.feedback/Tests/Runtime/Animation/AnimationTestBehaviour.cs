using UnityEngine;

namespace xpTURN.Feedback.Tests
{
    // a state machine behaviour for the tests' controllers — the editor's animator update calls it (the simulation's log). A
    // runtime script in its own file: a controller finds a behaviour by its MonoScript (an editor assembly's is not found). The
    // editor's tests name it by its assembly-qualified name (the minimal project has none)
    public sealed class AnimationTestBehaviour : StateMachineBehaviour
    {
    }
}
