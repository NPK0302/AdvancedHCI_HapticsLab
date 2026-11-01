using System.Collections;
using TMPro;
using UnityEngine;
using Oculus.Haptics;

namespace HapticsLab
{
    /// <summary>
    /// Zone 2: haptics as an INFORMATION channel. The only cue for the right number
    /// is how a detent feels (and, if sound is on, how it sounds). Turn the sound off
    /// and hide the numbers to make touch the only channel.
    /// </summary>
    [AddComponentMenu("Haptics Lab/Safe Controller")]
    public class SafeController : MonoBehaviour
    {
        public SafeDial dial;
        [Tooltip("Rotates around its local Y axis to open.")]
        public Transform doorHinge;
        public float openAngle = 105f;
        public GameObject numberLabels;
        public GameObject reward;

        [Header("Text")]
        public TMP_Text instructions;
        public TMP_Text progress;
        public TMP_Text readout;
        [Tooltip("Says what happened after each trigger pull.")]
        public TMP_Text status;

        [Header("Feedback")]
        public HapticClip confirmClip, errorClip, openClip;
        public AudioClip confirmSound, errorSound, openSound;

        [Header("Toggles (buttons)")]
        public LabButton soundButton;
        public LabButton numbersButton;
        public LabButton hintButton;

        public const int Length = 3;
        readonly int[] _combo = new int[Length];
        int _step;
        bool _open;
        bool _hint;
        AudioSource _audio;
        Coroutine _doorRoutine;

        void Awake()
        {
            _audio = gameObject.AddComponent<AudioSource>();
            _audio.playOnAwake = false;
            _audio.spatialBlend = 1f;
        }

        void Start()
        {
            dial.IsGate = i => !_open && _step < Length && i == _combo[_step];
            dial.EnterPressed += OnEnter;
            dial.NumberChanged += _ => UpdateReadout();
            NewCombination();
            RefreshToggles();
        }

        void OnDestroy()
        {
            if (dial != null) dial.EnterPressed -= OnEnter;
        }

        public void NewCombination()
        {
            int positions = dial.positions;
            for (int i = 0; i < Length; i++)
            {
                int n;
                int guard = 0;
                do { n = Random.Range(1, positions); }
                while (guard++ < 50 && (i > 0 && Mathf.Abs(n - _combo[i - 1]) < positions / 5));
                _combo[i] = n;
            }
            _step = 0;
            _open = false;
            dial.ResetDial();
            if (reward) reward.SetActive(false);
            MoveDoor(0f);
            if (instructions)
                instructions.text = "<b>Crack the safe</b>  (three numbers)\n" +
                                    "1  Reach to the dial and hold <b>grip</b>\n" +
                                    "2  Twist your wrist: feel for the <b>heavy double knock</b>\n" +
                                    "3  Keep holding and pull the <b>trigger</b> to enter it";
            SetStatus("Find number 1");
            UpdateProgress();
            UpdateReadout();
        }

        void OnEnter(Hand hand)
        {
            if (_open) return;
            if (dial.Current == _combo[_step])
            {
                _step++;
                Feedback(confirmClip, confirmSound, hand);
                if (_step >= Length)
                {
                    _open = true;
                    SetStatus($"<color=#7f7>{dial.Current:00} correct - the safe opens!</color>");
                    StartCoroutine(OpenRoutine(hand));
                }
                else
                {
                    SetStatus($"<color=#7f7>{dial.Current:00} correct!</color>  Now find number {_step + 1}");
                }
            }
            else
            {
                // Start over, so guessing every number is slower than feeling for the knock.
                bool hadProgress = _step > 0;
                _step = 0;
                Feedback(errorClip, errorSound, hand);
                SetStatus(hadProgress
                    ? $"<color=#f77>{dial.Current:00} is wrong - start again from number 1.</color>"
                    : $"<color=#f77>{dial.Current:00} is wrong.</color>  Keep turning, feel for the heavy knock");
            }
            UpdateProgress();
            UpdateReadout();
        }

        IEnumerator OpenRoutine(Hand hand)
        {
            yield return new WaitForSeconds(0.25f);
            Feedback(openClip, openSound, hand);
            if (reward) reward.SetActive(true);
            MoveDoor(openAngle);
            if (instructions)
                instructions.text = "<b>Open!</b>\nYou found it by touch.\nGrab the gold bar - and knock it against things.";
        }

        void Feedback(HapticClip clip, AudioClip sound, Hand hand)
        {
            if (clip != null) LabHaptics.PlayOnce(clip, hand);
            else LabHaptics.Pulse(hand, 0.6f, 0.1f);
            if (sound != null && dial.audioEnabled) _audio.PlayOneShot(sound);
        }

        void MoveDoor(float target)
        {
            if (doorHinge == null) return;
            if (_doorRoutine != null) StopCoroutine(_doorRoutine);
            _doorRoutine = StartCoroutine(DoorRoutine(target));
        }

        IEnumerator DoorRoutine(float target)
        {
            float start = doorHinge.localEulerAngles.y;
            if (start > 180f) start -= 360f;
            for (float t = 0f; t < 1f; t += Time.deltaTime / 1.2f)
            {
                float k = 1f - Mathf.Pow(1f - t, 3f);
                doorHinge.localRotation = Quaternion.Euler(0f, Mathf.Lerp(start, target, k), 0f);
                yield return null;
            }
            doorHinge.localRotation = Quaternion.Euler(0f, target, 0f);
        }

        void SetStatus(string text)
        {
            if (status) status.text = text;
        }

        void UpdateProgress()
        {
            if (!progress) return;
            var s = "";
            for (int i = 0; i < Length; i++)
                s += i < _step ? $"<color=#7f7>[{i + 1}]</color> " : $"<color=#777>[{i + 1}]</color> ";
            progress.text = s;
        }

        void UpdateReadout()
        {
            if (!readout) return;
            readout.text = _hint && !_open && _step < Length
                ? $"Dial: {dial.Current:00}    (hint: next is {_combo[_step]:00})"
                : $"Dial: {dial.Current:00}";
        }

        // ---- Toggle buttons ------------------------------------------------
        public void ToggleSound() { dial.audioEnabled = !dial.audioEnabled; RefreshToggles(); }

        public void ToggleNumbers()
        {
            if (numberLabels) numberLabels.SetActive(!numberLabels.activeSelf);
            if (readout) readout.gameObject.SetActive(numberLabels == null || numberLabels.activeSelf);
            RefreshToggles();
        }

        public void ToggleHint() { _hint = !_hint; UpdateReadout(); RefreshToggles(); }

        void RefreshToggles()
        {
            if (soundButton) { soundButton.SetActive(dial.audioEnabled); soundButton.SetLabel(dial.audioEnabled ? "Sound: ON" : "Sound: OFF"); }
            bool numbersOn = numberLabels == null || numberLabels.activeSelf;
            if (numbersButton) { numbersButton.SetActive(numbersOn); numbersButton.SetLabel(numbersOn ? "Numbers: ON" : "Numbers: OFF"); }
            if (hintButton) { hintButton.SetActive(_hint); hintButton.SetLabel(_hint ? "Visual hint: ON" : "Visual hint: OFF"); }
        }
    }
}
