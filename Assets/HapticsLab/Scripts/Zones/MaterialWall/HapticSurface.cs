using System;
using System.Collections.Generic;
using UnityEngine;
using Oculus.Haptics;

namespace HapticsLab
{
    /// <summary>
    /// A surface you can tap and rub with a fingertip. It renders touch at one of four
    /// <see cref="Fidelity"/> levels so they can be compared directly:
    ///
    ///   Tap  -> impact feedback (pulse / tap clip / tap clip scaled by impact speed)
    ///   Rub  -> texture feedback (buzz / looping texture clip / one grain per ridge crossed)
    ///
    /// The audio is the same at every level (and can be switched off at the wall).
    /// </summary>
    [AddComponentMenu("Haptics Lab/Haptic Surface")]
    public class HapticSurface : MonoBehaviour, ITouchable
    {
        public string materialName = "Wood";

        [Header("Designed haptics (Haptics Studio .haptic)")]
        public HapticClip tapClip;
        [Tooltip("A short, seamlessly looping texture clip (level 2).")]
        public HapticClip textureClip;
        [Tooltip("One ridge / grain of the material (level 3): played each time the finger travels grainSpacing.")]
        public HapticClip grainClip;

        [Header("Audio (same at every fidelity level)")]
        public AudioClip tapSound;
        public AudioClip scrapeLoop;
        [Range(0f, 1f)] public float scrapeVolume = 0.6f;

        [Header("Level 1: simple pulse settings")]
        [Tooltip("XRI's SimpleHapticFeedback default: what most apps ship.")]
        [Range(0f, 1f)] public float pulseAmplitude = 0.5f;
        public float pulseDuration = 0.1f;
        [Range(0f, 1f)] public float buzzAmplitude = 0.35f;

        [Header("Level 3: dynamics")]
        [Tooltip("Tap speeds (m/s) mapped to clip strength (minDynamicStrength..1)")]
        public Vector2 tapSpeedRange = new Vector2(0.05f, 1.2f);
        [Tooltip("Weakest tap/texture strength at level 3. Keep it at or above the pulse amplitude, " +
                 "so 'designed + dynamics' is never weaker than the simple pulse.")]
        [Range(0f, 1f)] public float minDynamicStrength = 0.6f;
        [Tooltip("Sliding speeds (m/s) mapped to texture strength 0..1")]
        public Vector2 slideSpeedRange = new Vector2(0.02f, 0.5f);
        [Tooltip("Distance (m) between two ridges/grains of the material.")]
        [Min(0.001f)] public float grainSpacing = 0.008f;
        [Tooltip("Randomness of the spacing (0 = regular ridges, 1 = very irregular, like gravel).")]
        [Range(0f, 1f)] public float grainJitter = 0.1f;
        [Tooltip("Frequency shift at slow / fast sliding (used when there is no grain clip)")]
        public Vector2 frequencyShiftRange = new Vector2(-0.25f, 0.25f);

        [Header("Contact")]
        [Tooltip("Leaving and touching again within this time (s) counts as one continuous contact: " +
                 "a rubbing finger drifts in and out of the panel, and that should not knock.")]
        [Min(0f)] public float reentryGrace = 0.2f;

        [Header("Fidelity")]
        [Tooltip("If set, the wall decides the fidelity level.")]
        public MaterialWall wall;
        public bool overrideFidelity;
        public Fidelity fidelity = Fidelity.DesignedDynamic;

        [Tooltip("Outward normal of the surface in local space.")]
        public Vector3 localNormal = Vector3.back;

        public Fidelity CurrentFidelity => overrideFidelity || wall == null ? fidelity : wall.fidelity;
        bool SoundOn => wall == null || wall.soundEnabled;

        /// <summary>Raised when a fingertip starts touching (used by the blind test).</summary>
        public event Action<HapticSurface> Touched;

        class Contact
        {
            public Fingertip tip;
            public ClipVoice texture;
            public float nextBuzz;
            public float lastMove;
            public float smoothed;
            public float nextUpdate;
            public float resumeAt = -1f;
            public Vector3 lastPos;
            public float travelled;   // distance slid since the last grain
            public float nextGrain;   // distance at which the next grain plays
        }

        readonly Dictionary<Fingertip, Contact> _contacts = new Dictionary<Fingertip, Contact>();
        readonly Dictionary<Hand, ClipVoice> _tapVoices = new Dictionary<Hand, ClipVoice>();
        readonly Dictionary<Hand, ClipVoice> _grainVoices = new Dictionary<Hand, ClipVoice>();
        AudioSource _oneShot, _loop;
        float _loopTarget;
        float _loopPitch = 1f;
        readonly Dictionary<Fingertip, float> _endedAt = new Dictionary<Fingertip, float>();

        void Awake()
        {
            _oneShot = gameObject.AddComponent<AudioSource>();
            _oneShot.playOnAwake = false;
            _oneShot.spatialBlend = 1f;
            // Full volume only within 0.2 m: a tap sounds like it comes from the panel, not from inside your head.
            _oneShot.minDistance = 0.2f;

            _loop = gameObject.AddComponent<AudioSource>();
            _loop.playOnAwake = false;
            _loop.loop = true;
            _loop.spatialBlend = 1f;
            _loop.minDistance = 0.4f;
            _loop.volume = 0f;
        }

        /// <summary>Copy the "material" (clips, sounds, tuning) from another surface.</summary>
        public void CopyMaterialFrom(HapticSurface other)
        {
            StopAll();
            materialName = other.materialName;
            tapClip = other.tapClip;
            textureClip = other.textureClip;
            grainClip = other.grainClip;
            grainSpacing = other.grainSpacing;
            grainJitter = other.grainJitter;
            minDynamicStrength = other.minDynamicStrength;
            tapSound = other.tapSound;
            scrapeLoop = other.scrapeLoop;
            scrapeVolume = other.scrapeVolume;
            pulseAmplitude = other.pulseAmplitude;
            pulseDuration = other.pulseDuration;
            buzzAmplitude = other.buzzAmplitude;
            tapSpeedRange = other.tapSpeedRange;
            slideSpeedRange = other.slideSpeedRange;
            frequencyShiftRange = other.frequencyShiftRange;
            foreach (var v in _tapVoices.Values) v.SetClip(tapClip);
            foreach (var v in _grainVoices.Values) v.SetClip(grainClip);
        }

        // ------------------------------------------------------------------
        // Touch
        // ------------------------------------------------------------------
        public void TouchBegin(Fingertip tip)
        {
            if (_contacts.ContainsKey(tip)) return;
            var c = new Contact { tip = tip, lastMove = Time.time, lastPos = tip.transform.position, nextGrain = NextGrainDistance() };
            _contacts[tip] = c;

            var normal = transform.TransformDirection(localNormal).normalized;
            float into = Mathf.Max(0f, -Vector3.Dot(tip.Velocity, normal));
            float speed = Mathf.Max(into, tip.Velocity.magnitude * 0.5f);
            float k = Mathf.InverseLerp(tapSpeedRange.x, tapSpeedRange.y, speed);

            // A tap needs a real movement into the panel, and not a finger that just slipped
            // out and back in while rubbing. Otherwise rubbing turns into random knocks.
            bool reentry = _endedAt.TryGetValue(tip, out float endedAt) && Time.time - endedAt < reentryGrace;
            bool tap = !reentry && into >= tapSpeedRange.x;
            if (!tap)
            {
                Touched?.Invoke(this);
                return;
            }

            // Sound: identical for every fidelity level.
            if (tapSound != null && SoundOn) _oneShot.PlayOneShot(tapSound, Mathf.Lerp(0.3f, 1f, k));

            switch (CurrentFidelity)
            {
                case Fidelity.SimplePulse:
                    LabHaptics.Pulse(tip.hand, pulseAmplitude, pulseDuration);
                    break;
                case Fidelity.DesignedClip:
                    PlayTap(tip.hand, 1f);
                    break;
                case Fidelity.DesignedDynamic:
                    PlayTap(tip.hand, Mathf.Lerp(minDynamicStrength, 1f, k));
                    break;
            }

            Touched?.Invoke(this);
        }

        public void TouchStay(Fingertip tip)
        {
            if (!_contacts.TryGetValue(tip, out var c)) return;

            var normal = transform.TransformDirection(localNormal).normalized;
            var v = tip.Velocity;
            float slide = (v - Vector3.Dot(v, normal) * normal).magnitude;
            float s = Mathf.InverseLerp(slideSpeedRange.x, slideSpeedRange.y, slide);
            c.smoothed = Mathf.Lerp(c.smoothed, s, 1f - Mathf.Exp(-Time.deltaTime * 8f));
            bool moving = slide > slideSpeedRange.x;
            if (moving) c.lastMove = Time.time;

            // Distance slid along the surface since last frame (the part across it, not into it).
            var pos = tip.transform.position;
            var d = pos - c.lastPos;
            c.lastPos = pos;
            c.travelled += (d - Vector3.Dot(d, normal) * normal).magnitude;

            _loopTarget = Mathf.Max(_loopTarget, c.smoothed);

            switch (CurrentFidelity)
            {
                case Fidelity.Off:
                    StopTexture(c);
                    break;

                case Fidelity.SimplePulse:
                    StopTexture(c);
                    // One fixed buzz while moving, whatever the material or speed.
                    if (moving && Time.time >= c.nextBuzz)
                    {
                        LabHaptics.Pulse(tip.hand, buzzAmplitude, 0.06f);
                        c.nextBuzz = Time.time + 0.05f;
                    }
                    break;

                case Fidelity.DesignedClip:
                    // The texture clip, played as designed, while moving.
                    if (moving) StartTexture(c, 1f, 0f);
                    else if (Time.time - c.lastMove > 0.12f) StopTexture(c);
                    break;

                case Fidelity.DesignedDynamic:
                    if (grainClip != null)
                    {
                        // The texture follows the finger: one grain per ridge crossed. Slow = separate
                        // ridges, fast = a dense buzz, still = nothing.
                        StopTexture(c);
                        if (c.travelled >= c.nextGrain)
                        {
                            c.travelled = 0f;
                            c.nextGrain = NextGrainDistance();
                            PlayGrain(c, Mathf.Lerp(minDynamicStrength, 1f, c.smoothed));
                        }
                    }
                    else if (moving)
                    {
                        // No grain clip: strength and "pitch" of the loop follow sliding speed.
                        StartTexture(c, Mathf.Lerp(minDynamicStrength, 1f, c.smoothed),
                            Mathf.Lerp(frequencyShiftRange.x, frequencyShiftRange.y, c.smoothed));
                    }
                    else if (Time.time - c.lastMove > 0.12f) StopTexture(c);
                    break;
            }
        }

        float NextGrainDistance() => grainSpacing * (1f + grainJitter * UnityEngine.Random.Range(-1f, 1f));

        void PlayGrain(Contact c, float strength)
        {
            var hand = c.tip.hand;
            // A tap (higher priority) is playing on this hand: let it finish.
            if (c.resumeAt >= 0f && Time.time < c.resumeAt) return;
            if (!_grainVoices.TryGetValue(hand, out var voice))
            {
                voice = LabHaptics.CreateVoice(grainClip, 100);
                _grainVoices[hand] = voice;
            }
            voice.Amplitude = strength;
            voice.Play(hand);
        }

        public void TouchEnd(Fingertip tip)
        {
            if (!_contacts.TryGetValue(tip, out var c)) return;
            StopTexture(c);
            c.texture?.Dispose();
            _contacts.Remove(tip);
            _endedAt[tip] = Time.time;
        }

        void PlayTap(Hand hand, float strength)
        {
            if (tapClip == null) { LabHaptics.Pulse(hand, pulseAmplitude * strength, pulseDuration); return; }
            if (!_tapVoices.TryGetValue(hand, out var voice))
            {
                // Priority 20 (high): a tap briefly overrides the looping texture (priority 100).
                voice = LabHaptics.CreateVoice(tapClip, 20);
                _tapVoices[hand] = voice;
            }
            voice.Amplitude = strength;
            voice.Play(hand);

            // The tap interrupts the texture loop on that controller, and the SDK does not
            // promise to resume it. Restart the loop ourselves once the tap is over.
            foreach (var c in _contacts.Values)
                if (c.tip.hand == hand || hand == Hand.Both)
                    c.resumeAt = Time.time + voice.Duration + 0.02f;
        }

        void StartTexture(Contact c, float amplitude, float shift)
        {
            if (textureClip == null) return;
            if (c.texture == null)
            {
                c.texture = LabHaptics.CreateVoice(textureClip, 100);
                c.texture.Loop = true;
            }
            if (c.texture.Clip != textureClip) { c.texture.SetClip(textureClip); c.texture.Loop = true; }

            // Updating the SDK ~30x per second is plenty (changes apply within tens of ms).
            if (Time.time >= c.nextUpdate)
            {
                c.texture.Amplitude = amplitude;
                c.texture.FrequencyShift = shift;
                c.nextUpdate = Time.time + 0.033f;
            }
            bool tapOver = c.resumeAt >= 0f && Time.time >= c.resumeAt;
            if (!c.texture.IsPlaying || tapOver)
            {
                c.texture.Play(c.tip.hand);
                // Started during a tap? Then keep resumeAt, so it is restarted after the tap.
                if (tapOver) c.resumeAt = -1f;
            }
        }

        static void StopTexture(Contact c)
        {
            if (c.texture != null && c.texture.IsPlaying) c.texture.Stop();
        }

        void StopAll()
        {
            foreach (var c in _contacts.Values) { StopTexture(c); c.texture?.Dispose(); }
            _contacts.Clear();
        }

        void Update()
        {
            // Scrape audio follows sliding speed (same at every level).
            if (scrapeLoop != null)
            {
                if (_loop.clip != scrapeLoop) { _loop.clip = scrapeLoop; }
                float target = _contacts.Count > 0 && SoundOn ? _loopTarget * scrapeVolume : 0f;
                _loop.volume = Mathf.MoveTowards(_loop.volume, target, Time.deltaTime * 4f);
                // Pitch follows speed slowly and within a narrow range, so it never flutters.
                _loopPitch = Mathf.MoveTowards(_loopPitch, Mathf.Lerp(0.96f, 1.08f, _loopTarget), Time.deltaTime * 0.5f);
                _loop.pitch = _loopPitch;
                if (_loop.volume > 0.001f && !_loop.isPlaying) _loop.Play();
                else if (_loop.volume <= 0.001f && _loop.isPlaying) _loop.Stop();
            }
            _loopTarget = Mathf.MoveTowards(_loopTarget, 0f, Time.deltaTime * 3f);
        }

        void OnDisable() => StopAll();

        void OnDestroy()
        {
            StopAll();
            foreach (var v in _tapVoices.Values) v.Dispose();
            _tapVoices.Clear();
            foreach (var v in _grainVoices.Values) v.Dispose();
            _grainVoices.Clear();
        }
    }
}
