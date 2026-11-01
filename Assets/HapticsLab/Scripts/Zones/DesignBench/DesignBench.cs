using TMPro;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace HapticsLab
{
    /// <summary>
    /// Zone 3: design your own haptics.
    ///  1. In Haptics Studio: import an audio file, shape the clip, export as .haptic
    ///     into the Live folder (shown on the board).
    ///  2. The bench picks it up within a second and selects it.
    ///  3. It plays on the hand you last used. Play L / Play R play it again.
    /// Buttons change clip, loop, amplitude and frequency shift (the HapticClipPlayer API).
    /// The optional puck plays the clip while held (and with the trigger).
    /// </summary>
    [AddComponentMenu("Haptics Lab/Design Bench")]
    public class DesignBench : MonoBehaviour
    {
        public LiveClipLibrary library;
        public EnvelopeView envelope;
        [Tooltip("Optional: a grabbable object that plays the clip while held.")]
        public XRGrabInteractable puck;

        [Header("Text")]
        public TMP_Text title;
        public TMP_Text info;
        public TMP_Text folderText;
        public LabButton loopButton;

        int _index;
        bool _loop;
        float _amplitude = 1f;
        float _shift;
        ClipVoice _voice;
        AudioSource _audio;
        Hand _hand = Hand.Right;
        bool _held;
        Vector3 _puckHome;
        Quaternion _puckHomeRot;

        void Awake()
        {
            _audio = puck != null ? puck.gameObject.AddComponent<AudioSource>() : gameObject.AddComponent<AudioSource>();
            _audio.playOnAwake = false;
            _audio.spatialBlend = 1f;
            _audio.minDistance = 0.3f;
            if (puck != null) { _puckHome = puck.transform.position; _puckHomeRot = puck.transform.rotation; }
        }

        void OnEnable()
        {
            if (library != null) { library.ClipUpdated += OnLiveClip; library.ListChanged += Refresh; }
            if (puck != null)
            {
                puck.selectEntered.AddListener(OnGrab);
                puck.selectExited.AddListener(OnRelease);
                puck.activated.AddListener(OnTrigger);
            }
        }

        void OnDisable()
        {
            if (library != null) { library.ClipUpdated -= OnLiveClip; library.ListChanged -= Refresh; }
            if (puck != null)
            {
                puck.selectEntered.RemoveListener(OnGrab);
                puck.selectExited.RemoveListener(OnRelease);
                puck.activated.RemoveListener(OnTrigger);
            }
            _voice?.Stop();
        }

        void Start()
        {
            if (folderText != null && library != null)
                folderText.text = "<b>Live folder</b> (export .haptic + same-name .wav here):\n<size=70%>" + library.FolderPath + "</size>";
            Refresh();
        }

        void OnDestroy() => _voice?.Dispose();

        // Without a puck, a new or changed clip plays straight away on the last-used hand.
        bool AutoPlay => puck == null || _held;

        LiveClipLibrary.Entry Current =>
            library != null && library.Entries.Count > 0 ? library.Entries[Mathf.Clamp(_index, 0, library.Entries.Count - 1)] : null;

        void OnLiveClip(LiveClipLibrary.Entry e)
        {
            // Newly exported clip: select it and play it right away.
            _index = Mathf.Max(0, library.Entries.IndexOf(e));
            Refresh();
            if (AutoPlay) Play();
        }

        void OnGrab(SelectEnterEventArgs args)
        {
            _held = true;
            _hand = HandUtil.FromArgs(args);
            Play();
        }

        void OnRelease(SelectExitEventArgs args)
        {
            _held = false;
            if (!_loop) return;
            _voice?.Stop();
            _audio.Stop();
        }

        void OnTrigger(ActivateEventArgs args)
        {
            _hand = HandUtil.FromArgs(args, _hand);
            Play();
        }

        // ---- Buttons --------------------------------------------------------
        public void Next() { Step(+1); }
        public void Previous() { Step(-1); }

        void Step(int d)
        {
            if (library == null || library.Entries.Count == 0) return;
            _index = (_index + d + library.Entries.Count) % library.Entries.Count;
            Refresh();
            if (AutoPlay) Play();
        }

        public void ToggleLoop()
        {
            _loop = !_loop;
            if (_voice != null) _voice.Loop = _loop;
            if (!_loop) { _voice?.Stop(); _audio.Stop(); }
            Refresh();
        }

        public void AmplitudeUp() { _amplitude = Mathf.Min(1.5f, _amplitude + 0.25f); ApplyModulation(); }
        public void AmplitudeDown() { _amplitude = Mathf.Max(0f, _amplitude - 0.25f); ApplyModulation(); }
        public void FrequencyUp() { _shift = Mathf.Min(1f, _shift + 0.25f); ApplyModulation(); }
        public void FrequencyDown() { _shift = Mathf.Max(-1f, _shift - 0.25f); ApplyModulation(); }
        public void ResetModulation() { _amplitude = 1f; _shift = 0f; ApplyModulation(); }

        public void PlayLeft() { _hand = Hand.Left; Play(); }
        public void PlayRight() { _hand = Hand.Right; Play(); }

        public void ResetPuck()
        {
            if (puck == null) return;
            var rb = puck.GetComponent<Rigidbody>();
            if (rb != null && !rb.isKinematic) { rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero; }
            puck.transform.SetPositionAndRotation(_puckHome, _puckHomeRot);
        }

        void ApplyModulation()
        {
            if (_voice != null) { _voice.Amplitude = _amplitude; _voice.FrequencyShift = _shift; }
            Refresh();
        }

        public void Play()
        {
            var e = Current;
            if (e == null || e.clip == null) return;
            if (_voice == null) _voice = LabHaptics.CreateVoice(e.clip, 50);
            else if (_voice.Clip != e.clip) _voice.SetClip(e.clip);
            _voice.Loop = _loop;
            _voice.Amplitude = _amplitude;
            _voice.FrequencyShift = _shift;
            _voice.Play(_hand);

            _audio.Stop();
            if (e.audio != null)
            {
                _audio.clip = e.audio;
                _audio.loop = _loop;
                _audio.Play();
            }
        }

        void Refresh()
        {
            var e = Current;
            if (envelope != null) envelope.Show(e != null && e.clip != null ? HapticEnvelope.ParseCached(e.clip.json) : null);

            if (title != null)
            {
                int n = library != null ? library.Entries.Count : 0;
                title.text = e == null ? "No clips" : $"{(e.isLive ? "<color=#8f8>LIVE</color>  " : "")}<b>{e.name}</b>   <size=70%>({_index + 1}/{n})</size>";
            }

            if (info != null)
            {
                var env = e?.clip != null ? HapticEnvelope.ParseCached(e.clip.json) : null;
                info.text = env == null
                    ? ""
                    : $"{env.description}\n" +
                      $"Duration {env.Duration:0.00}s   Points {env.amplitude.Length}   Emphasis {env.EmphasisCount}   Audio {(e.audio ? "yes" : "no")}\n" +
                      $"<color=#fa4>amplitude</color> x{_amplitude:0.00}   <color=#6af>frequencyShift</color> {_shift:+0.00;-0.00;0.00}   loop {(_loop ? "on" : "off")}";
            }

            if (loopButton != null) { loopButton.SetActive(_loop); loopButton.SetLabel(_loop ? "Loop: ON" : "Loop: OFF"); }
        }

        void Update()
        {
            if (envelope != null) envelope.SetPlayhead(_voice != null && _voice.IsPlaying ? _voice.PlaybackTime : -1f);

            // Bring the puck back if it falls off the table.
            if (puck != null && !_held && puck.transform.position.y < -1f) ResetPuck();
        }
    }
}
