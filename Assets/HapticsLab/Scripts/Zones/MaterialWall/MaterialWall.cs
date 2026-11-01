using TMPro;
using UnityEngine;

namespace HapticsLab
{
    /// <summary>
    /// Zone 1. Four material panels share one setting, chosen on one console with two groups:
    ///   On / off:        Sound and Haptics switches
    ///   Haptic fidelity: 1 Simple pulse, 2 Designed clip, 3 Clip + dynamics
    /// so students can feel how much each step adds, with and without sound.
    /// </summary>
    [AddComponentMenu("Haptics Lab/Material Wall")]
    public class MaterialWall : MonoBehaviour
    {
        [Tooltip("What the panels play right now: Off while haptics are switched off, otherwise the chosen level.")]
        public Fidelity fidelity = Fidelity.SimplePulse;

        [Header("On / off")]
        public bool soundEnabled = true;
        public bool hapticsEnabled = true;
        public LabButton soundButton;
        public LabButton hapticsButton;

        [Header("Haptic fidelity")]
        [Tooltip("One button per level, in order 1..3 (simple pulse, designed clip, clip + dynamics).")]
        public LabButton[] levelButtons = new LabButton[3];

        [Header("Text")]
        public TMP_Text description;
        public TMP_Text sdkStatus;

        // Start with the simple pulse: students then step up to designed and dynamic haptics.
        Fidelity _level = Fidelity.SimplePulse;

        void Start()
        {
            if (fidelity != Fidelity.Off) _level = fidelity;
            Refresh();
            UpdateStatus(null);
            LabHaptics.StatusChanged += UpdateStatus;
        }

        void OnDestroy() => LabHaptics.StatusChanged -= UpdateStatus;

        float _nextStatus;

        void Update()
        {
            if (Time.time < _nextStatus) return;
            _nextStatus = Time.time + 1f;
            UpdateStatus(null);
        }

        /// <summary>Hook this to the level buttons (int argument 1..3). 0 switches haptics off.</summary>
        public void SetFidelity(int level)
        {
            if (level <= 0)
            {
                hapticsEnabled = false;
            }
            else
            {
                // Choosing a level also switches haptics back on.
                _level = (Fidelity)Mathf.Clamp(level, 1, 3);
                hapticsEnabled = true;
            }
            Refresh();
        }

        /// <summary>Hook this to the Haptics button. Switching back on returns to the chosen level.</summary>
        public void ToggleHaptics()
        {
            hapticsEnabled = !hapticsEnabled;
            Refresh();
        }

        /// <summary>Hook this to the Sound button.</summary>
        public void ToggleSound()
        {
            soundEnabled = !soundEnabled;
            Refresh();
        }

        void Refresh()
        {
            fidelity = hapticsEnabled ? _level : Fidelity.Off;

            if (soundButton != null)
            {
                soundButton.SetActive(soundEnabled);
                soundButton.SetLabel(soundEnabled ? "Sound: ON" : "Sound: OFF");
            }
            if (hapticsButton != null)
            {
                hapticsButton.SetActive(hapticsEnabled);
                hapticsButton.SetLabel(hapticsEnabled ? "Haptics: ON" : "Haptics: OFF");
            }
            for (int i = 0; i < levelButtons.Length; i++)
            {
                if (levelButtons[i] == null) continue;
                levelButtons[i].SetActive(hapticsEnabled && (int)_level == i + 1);
                levelButtons[i].SetDimmed(!hapticsEnabled);
            }
            if (description != null)
                description.text = FidelityInfo.Description(fidelity);
        }

        void UpdateStatus(string _)
        {
            if (sdkStatus == null) return;
            sdkStatus.text = LabHaptics.MetaAvailable
                ? "<color=#8f8>Meta Haptics SDK active</color>"
                : "<color=#fb6>Meta Haptics SDK unavailable: clips approximated with pulses</color>";
        }
    }
}
