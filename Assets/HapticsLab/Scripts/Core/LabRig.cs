using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Haptics;

namespace HapticsLab
{
    /// <summary>
    /// Lives on the XR Origin. Registers each controller's XRI HapticImpulsePlayer with
    /// <see cref="LabHaptics"/> and adds a <see cref="Fingertip"/> to each controller.
    /// </summary>
    [AddComponentMenu("Haptics Lab/Lab Rig")]
    public class LabRig : MonoBehaviour
    {
        public Transform leftController;
        public Transform rightController;

        [Header("Fingertips")]
        public bool createFingertips = true;
        [Tooltip("Fingertip position relative to the controller (metres).")]
        public Vector3 fingertipOffset = new Vector3(0f, -0.02f, 0.07f);
        [Min(0.005f)] public float fingertipRadius = 0.012f;
        public Material fingertipMaterial;

        [Header("Audio")]
        [Tooltip("Volume of all lab sounds. Sound easily dominates touch, so keep it well below 1.")]
        [Range(0f, 1f)] public float masterVolume = 0.15f;

        void OnValidate() { if (Application.isPlaying) AudioListener.volume = masterVolume; }

        void Awake()
        {
            AudioListener.volume = masterVolume;

            if (leftController == null) leftController = FindChild(transform, "left controller");
            if (rightController == null) rightController = FindChild(transform, "right controller");

            var l = leftController ? leftController.GetComponentInChildren<HapticImpulsePlayer>(true) : null;
            var r = rightController ? rightController.GetComponentInChildren<HapticImpulsePlayer>(true) : null;
            if (l != null || r != null)
                LabHaptics.RegisterImpulsePlayers(l, r);
            else
                Debug.LogWarning("[HapticsLab] No HapticImpulsePlayer found on the controllers - using the generic XR input API for pulses.");

            if (createFingertips)
            {
                if (leftController) EnsureFingertip(leftController, Hand.Left);
                if (rightController) EnsureFingertip(rightController, Hand.Right);
            }
        }

        void EnsureFingertip(Transform controller, Hand hand)
        {
            if (controller.GetComponentInChildren<Fingertip>(true) != null) return;

            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = hand + " Fingertip";
            go.transform.SetParent(controller, false);
            go.transform.localPosition = fingertipOffset;
            go.transform.localScale = Vector3.one * (fingertipRadius * 2f);
            var r = go.GetComponent<Renderer>();
            if (fingertipMaterial) r.sharedMaterial = fingertipMaterial;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            go.AddComponent<Rigidbody>();
            var tip = go.AddComponent<Fingertip>();
            tip.hand = hand;
        }

        static Transform FindChild(Transform root, string lowerName)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.name.ToLowerInvariant() == lowerName)
                    return t;
            return null;
        }
    }
}
