using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;
using xpTURN.Feedback;

namespace PlayerGates
{
    // check-player-gates.sh's scripts-only build: with "-gateLinkerScene <file>" — load the package's scene, play its
    // player (the public API only), write the camera's field of view before and after (30 → 60 when Camera.Reset survived
    // the linker), quit
    public sealed class GateSceneProbe : MonoBehaviour
    {
        string m_Path;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Run()
        {
            string[] args = Environment.GetCommandLineArgs();
            int at = Array.IndexOf(args, "-gateLinkerScene");
            if (at < 0 || at + 1 >= args.Length)
                return;
            var go = new GameObject("gate scene probe");
            DontDestroyOnLoad(go);
            go.AddComponent<GateSceneProbe>().m_Path = args[at + 1];
        }

        IEnumerator Start()
        {
            yield return SceneManager.LoadSceneAsync("FeedbackLinkerScene", LoadSceneMode.Additive);
            Scene scene = SceneManager.GetSceneByName("FeedbackLinkerScene");
            GameObject root = null;
            foreach (GameObject r in scene.GetRootGameObjects())
            {
                if (r.name == "packaged linker")
                    root = r;
            }
            Camera camera = root != null ? root.GetComponentInChildren<Camera>(true) : null;
            if (camera == null)
            {
                File.WriteAllText(m_Path, "scene no player\n");
                Application.Quit();
                yield break;
            }
            float before = camera.fieldOfView;
            root.GetComponent<FeedbackPlayer>().Play();
            yield return null;
            yield return null;
            File.WriteAllText(m_Path, "scene fov before=" + before + " after=" + camera.fieldOfView + "\n");
            Application.Quit();
        }
    }
}
