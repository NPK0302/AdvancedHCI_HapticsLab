using System;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using Oculus.Haptics;

namespace HapticsLab
{
    /// <summary>
    /// A combination-lock dial. Reach to it, hold grip and twist your wrist.
    /// Every number is a detent you can feel; a "gate" number gives a heavy double knock.
    /// Pull the trigger while holding to enter the current number.
    ///
    /// Convention: the dial's local +Z points INTO the safe (away from the user),
    /// numbers are printed clockwise from the top.
    /// </summary>
    [RequireComponent(typeof(XRSimpleInteractable))]
    [AddComponentMenu("Haptics Lab/Safe Dial")]
    public class SafeDial : MonoBehaviour
    {
        [Min(4)] public int positions = 40;
        [Tooltip("The visual part that rotates.")]
        public Transform knob;
        [Tooltip("Degrees of dial rotation per degree of wrist twist.")]
        public float sensitivity = 1f;

        [Header("Feedback")]
        public HapticClip tickClip;
        public HapticClip gateClip;
        public AudioClip tickSound;
        public AudioClip gateSound;
        public bool audioEnabled = true;
        [Tooltip("Off: the gate makes the same sound as every other number, so only touch gives it away.")]
        public bool gateHasOwnSound = false;
        [Tooltip("Simple-pulse fallback strengths if no clips are assigned.")]
        public float tickPulse = 0.2f, gatePulse = 0.8f;

        /// <summary>Set by the safe: is this number a gate (heavier click)?</summary>
        public Func<int, bool> IsGate = _ => false;
        public event Action<int> NumberChanged;
        public event Action<Hand> EnterPressed;

        public int Current { get; private set; }
        public bool IsHeld => _holder != null;

        XRSimpleInteractable _interactable;
        IXRSelectInteractor _holder;
        Hand _hand;
        Quaternion _prevRot;
        float _angle;
        int _raw;   // detent index without wrapping, to know which numbers were crossed
        AudioSource _audio;

        void Awake()
        {
            _interactable = GetComponent<XRSimpleInteractable>();
            _audio = gameObject.AddComponent<AudioSource>();
            _audio.playOnAwake = false;
            _audio.spatialBlend = 1f;
            _audio.minDistance = 0.3f;
        }

        void OnEnable()
        {
            _interactable.selectEntered.AddListener(OnGrab);
            _interactable.selectExited.AddListener(OnRelease);
            _interactable.activated.AddListener(OnActivate);
        }

        void OnDisable()
        {
            _interactable.selectEntered.RemoveListener(OnGrab);
            _interactable.selectExited.RemoveListener(OnRelease);
            _interactable.activated.RemoveListener(OnActivate);
            _holder = null;
        }

        void OnGrab(SelectEnterEventArgs args)
        {
            _holder = args.interactorObject;
            _hand = HandUtil.FromArgs(args);
            _prevRot = _holder.GetAttachTransform(_interactable).rotation;
        }

        void OnRelease(SelectExitEventArgs args)
        {
            if (args.interactorObject == _holder) _holder = null;
        }

        void OnActivate(ActivateEventArgs args) => EnterPressed?.Invoke(HandUtil.FromArgs(args, _hand));

        void Update()
        {
            if (_holder == null) return;

            // How much did the controller twist around the dial axis since last frame?
            var rot = _holder.GetAttachTransform(_interactable).rotation;
            var delta = rot * Quaternion.Inverse(_prevRot);
            _prevRot = rot;
            delta.ToAngleAxis(out float deg, out Vector3 axis);
            if (float.IsNaN(axis.x) || axis.sqrMagnitude < 1e-6f) return;
            if (deg > 180f) deg -= 360f;
            float twist = deg * Vector3.Dot(axis.normalized, transform.forward);

            SetAngle(_angle + twist * sensitivity, true);
        }

        /// <summary>Rotate the dial (degrees). Positive = counter-clockwise as seen by the user.</summary>
        public void SetAngle(float angle, bool withFeedback)
        {
            _angle = angle;
            if (knob != null) knob.localRotation = Quaternion.AngleAxis(_angle, Vector3.forward);

            int raw = Mathf.RoundToInt(_angle / (360f / positions));
            if (raw == _raw) return;
            int from = _raw;
            _raw = raw;
            Current = Mod(raw, positions);
            if (withFeedback) Detent(CrossedGate(from, raw));
            NumberChanged?.Invoke(Current);
        }

        /// <summary>
        /// A fast twist can cross several numbers in one frame. Only one click can play per
        /// frame, so make it the heavy one if any crossed number was the gate.
        /// </summary>
        bool CrossedGate(int from, int to)
        {
            int dir = to > from ? 1 : -1;
            if (Mathf.Abs(to - from) > positions) from = to - dir * positions;
            for (int i = from + dir; i != to + dir; i += dir)
                if (IsGate(Mod(i, positions)))
                    return true;
            return false;
        }

        public void ResetDial() => SetAngle(0f, false);

        void Detent(bool gate)
        {
            var clip = gate ? gateClip : tickClip;
            if (clip != null) LabHaptics.PlayOnce(clip, _hand);
            else LabHaptics.Pulse(_hand, gate ? gatePulse : tickPulse, gate ? 0.05f : 0.012f);

            if (audioEnabled)
            {
                var s = gate && gateHasOwnSound ? gateSound : tickSound;
                // Same volume for both: the difference should be felt, not heard as "louder".
                if (s != null) _audio.PlayOneShot(s, 0.7f);
            }
        }

        static int Mod(int a, int n) => ((a % n) + n) % n;
    }
}
