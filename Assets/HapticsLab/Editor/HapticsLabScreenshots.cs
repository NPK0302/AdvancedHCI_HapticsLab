using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace HapticsLab.Editor
{
    /// <summary>
    /// "Haptics Lab > Capture Zone Screenshots": renders each zone from standing head height
    /// into Screenshots/ (next to Assets). Handy for slides, reports and checking the layout
    /// without a headset.
    ///
    /// It briefly enters Play mode first, so every station shows its texts and button states
    /// as students see them (they are set when the scene starts).
    /// From the command line: Unity -batchmode -projectPath . -executeMethod
    /// HapticsLab.Editor.HapticsLabScreenshots.CaptureFromCommandLine   (no -quit, no -nographics)
    /// </summary>
    public static class HapticsLabScreenshots
    {
        const string ScenePath = "Assets/HapticsLab/Scenes/HapticsLab.unity";
        const string PendingKey = "HapticsLab.Screenshots.Pending";
        const string QuitKey = "HapticsLab.Screenshots.QuitAfter";
        const int FramesToWait = 30; // let every Start() run and the texts settle

        [MenuItem("Haptics Lab/Capture Zone Screenshots", priority = 22)]
        public static void Capture()
        {
            if (EditorApplication.isPlaying)
            {
                CaptureNow();
                return;
            }
            SessionState.SetBool(PendingKey, true);
            EditorApplication.EnterPlaymode();
        }

        public static void CaptureFromCommandLine()
        {
            EditorSceneManager.OpenScene(ScenePath);
            SessionState.SetBool(QuitKey, true);
            Capture();
        }

        [InitializeOnLoadMethod]
        static void Hook()
        {
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(PendingKey, false))
                {
                    SessionState.SetBool(PendingKey, false);
                    int frames = 0;
                    EditorApplication.CallbackFunction tick = null;
                    tick = () =>
                    {
                        if (++frames < FramesToWait) return;
                        EditorApplication.update -= tick;
                        CaptureNow();
                        EditorApplication.ExitPlaymode();
                    };
                    EditorApplication.update += tick;
                }
                else if (state == PlayModeStateChange.EnteredEditMode && SessionState.GetBool(QuitKey, false))
                {
                    SessionState.SetBool(QuitKey, false);
                    // One update later, so other packages finish their own play-mode cleanup first.
                    EditorApplication.delayCall += () => EditorApplication.Exit(0);
                }
            };
        }

        static void CaptureNow()
        {
            string dir = Path.GetFullPath("Screenshots");
            Directory.CreateDirectory(dir);
            var go = new GameObject("[Screenshot Camera]") { hideFlags = HideFlags.HideAndDontSave };
            var cam = go.AddComponent<Camera>();
            cam.fieldOfView = 70f;
            cam.nearClipPlane = 0.02f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = RenderSettings.fogColor;
            var rt = new RenderTexture(1920, 1080, 24) { antiAliasing = 4 };
            cam.targetTexture = rt;
            var tex = new Texture2D(1920, 1080, TextureFormat.RGB24, false);

            var shots = new (string name, float yaw, Vector3 offset, float pitch)[]
            {
                ("1_MaterialWall", 0f, new Vector3(0f, 1.55f, 0.15f), 12f),
                ("2_Safe", -90f, new Vector3(0f, 1.55f, 0.15f), 18f),
                ("3_DesignBench", 90f, new Vector3(0f, 1.6f, 0.15f), 15f),
                ("4_BlindTest", 180f, new Vector3(0f, 1.55f, 0.15f), 12f),
                ("0_Overview", 0f, new Vector3(0f, 5.6f, -3.0f), 55f),
            };

            foreach (var s in shots)
            {
                var rot = Quaternion.Euler(0f, s.yaw, 0f);
                go.transform.SetPositionAndRotation(rot * s.offset, rot * Quaternion.Euler(s.pitch, 0f, 0f));
                cam.Render();
                RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
                tex.Apply();
                File.WriteAllBytes(Path.Combine(dir, s.name + ".png"), tex.EncodeToPNG());
            }

            RenderTexture.active = null;
            cam.targetTexture = null;
            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(tex);
            Object.DestroyImmediate(go);
            Debug.Log("<b>[HapticsLab]</b> Screenshots saved to " + dir);
        }
    }
}
