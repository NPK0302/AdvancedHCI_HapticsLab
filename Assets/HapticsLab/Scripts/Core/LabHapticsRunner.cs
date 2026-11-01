using UnityEngine;

namespace HapticsLab
{
    /// <summary>Hidden helper object that runs coroutines for non-MonoBehaviour code.</summary>
    [AddComponentMenu("")]
    public sealed class LabHapticsRunner : MonoBehaviour
    {
        static LabHapticsRunner s_instance;
        public static bool Exists => s_instance != null;

        public static LabHapticsRunner Instance
        {
            get
            {
                if (s_instance == null)
                {
                    var go = new GameObject("[HapticsLab Runner]") { hideFlags = HideFlags.HideInHierarchy };
                    if (Application.isPlaying)
                        DontDestroyOnLoad(go);
                    s_instance = go.AddComponent<LabHapticsRunner>();
                }
                return s_instance;
            }
        }
    }
}
