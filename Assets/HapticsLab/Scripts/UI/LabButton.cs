using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using Oculus.Haptics;

namespace HapticsLab
{
    /// <summary>
    /// A physical 3D button. Press it by pushing into it with a fingertip (any XRI
    /// interactor that selects it works too). Gives UI haptics: an optional tick on
    /// hover and a firm click on press.
    /// </summary>
    [RequireComponent(typeof(XRSimpleInteractable))]
    [AddComponentMenu("Haptics Lab/Lab Button")]
    public class LabButton : MonoBehaviour, ITouchable
    {
        public UnityEvent onPress = new UnityEvent();

        [Header("Visuals")]
        public TMP_Text label;
        public Renderer cap;
        public Color idleColor = new Color(0.22f, 0.24f, 0.3f);
        public Color activeColor = new Color(0.95f, 0.55f, 0.2f);
        public Color pressedColor = Color.white;
        [Tooltip("How far the cap moves when pressed (local Z).")]
        public float travel = 0.008f;

        [Header("Feedback")]
        public HapticClip hoverClip;
        public HapticClip pressClip;
        public AudioClip pressSound;
        [Min(0f)] public float cooldown = 0.3f;

        [Header("Fingertip press")]
        [Tooltip("Minimum fingertip speed (m/s) into the button. Stops a brushing finger from pressing it.")]
        [Min(0f)] public float minPressSpeed = 0.03f;
        [Tooltip("How much of the fingertip's motion must point into the button (0 = any direction, 1 = straight in).")]
        [Range(0f, 1f)] public float minPressDirectness = 0.35f;

        bool _active;
        float _next;
        float _pressAnim;
        Vector3 _capRest;
        AudioSource _audio;
        XRSimpleInteractable _interactable;
        MaterialPropertyBlock _mpb;

        public bool Active => _active;

        void Awake()
        {
            _interactable = GetComponent<XRSimpleInteractable>();
            if (cap != null) _capRest = cap.transform.localPosition;
            _mpb = new MaterialPropertyBlock();
            ApplyColor(_active ? activeColor : idleColor);
        }

        void OnEnable()
        {
            _interactable.hoverEntered.AddListener(OnHover);
            _interactable.selectEntered.AddListener(OnSelect);
        }

        void OnDisable()
        {
            _interactable.hoverEntered.RemoveListener(OnHover);
            _interactable.selectEntered.RemoveListener(OnSelect);
        }

        void OnHover(HoverEnterEventArgs args)
        {
            if (hoverClip != null) LabHaptics.PlayOnce(hoverClip, HandUtil.FromArgs(args));
        }

        void OnSelect(SelectEnterEventArgs args) => Press(HandUtil.FromArgs(args));

        public void TouchBegin(Fingertip tip)
        {
            // Local +Z points into the button. Only a push counts, not a finger sliding past
            // on its way to a panel.
            float into = Vector3.Dot(tip.Velocity, transform.forward);
            if (into >= minPressSpeed && into >= minPressDirectness * tip.Velocity.magnitude)
                Press(tip.hand);
        }

        public void TouchStay(Fingertip tip) { }
        public void TouchEnd(Fingertip tip) { }

        public void Press(Hand hand)
        {
            if (Time.time < _next) return;
            _next = Time.time + cooldown;
            _pressAnim = 1f;

            if (pressClip != null) LabHaptics.PlayOnce(pressClip, hand);
            else LabHaptics.Pulse(hand, 0.4f, 0.03f);

            if (pressSound != null)
            {
                if (_audio == null)
                {
                    _audio = gameObject.AddComponent<AudioSource>();
                    _audio.playOnAwake = false;
                    _audio.spatialBlend = 1f;
                    _audio.minDistance = 0.3f;
                }
                _audio.PlayOneShot(pressSound, 0.8f);
            }

            onPress.Invoke();
        }

        /// <summary>Highlight this button (e.g. the currently selected option).</summary>
        public void SetActive(bool active)
        {
            _active = active;
            ApplyColor(active ? activeColor : idleColor);
        }

        public void SetLabel(string text)
        {
            if (label != null) label.text = text;
        }

        /// <summary>Grey out the label, e.g. for options that have no effect right now (the button still works).</summary>
        public void SetDimmed(bool dimmed)
        {
            if (label != null) label.alpha = dimmed ? 0.35f : 1f;
        }

        void Update()
        {
            if (_pressAnim <= 0f || cap == null) return;
            _pressAnim = Mathf.Max(0f, _pressAnim - Time.deltaTime * 6f);
            float k = Mathf.Sin(_pressAnim * Mathf.PI);
            cap.transform.localPosition = _capRest + new Vector3(0f, 0f, travel * k);
            ApplyColor(Color.Lerp(_active ? activeColor : idleColor, pressedColor, k * 0.6f));
        }

        void ApplyColor(Color c)
        {
            if (cap == null) return;
            cap.GetPropertyBlock(_mpb);
            _mpb.SetColor("_BaseColor", c);
            _mpb.SetColor("_Color", c);
            cap.SetPropertyBlock(_mpb);
        }
    }
}
