using System;
using System.Collections;
using UnityEngine;
using Oculus.Haptics;

namespace HapticsLab
{
    /// <summary>
    /// Plays one .haptic clip. A thin, safe wrapper around the Meta Haptics SDK's
    /// <see cref="HapticClipPlayer"/>:
    ///
    ///   var voice = LabHaptics.CreateVoice(clip);
    ///   voice.Amplitude = 0.8f;        // scale the whole clip (0..1+)
    ///   voice.FrequencyShift = 0.2f;   // shift all frequencies (-1..1)
    ///   voice.Loop = true;
    ///   voice.Play(Hand.Right);
    ///
    /// When the Meta SDK is not available it renders an approximation with pulses.
    /// </summary>
    public sealed class ClipVoice : IDisposable
    {
        HapticClip _clip;
        HapticEnvelope _envelope;
        HapticClipPlayer _player;
        float _amplitude = 1f;
        float _frequencyShift;
        bool _loop;
        readonly uint _priority;
        float _startTime = -1f;
        Coroutine _fallback;

        public bool IsDisposed { get; private set; }
        public HapticClip Clip => _clip;
        public HapticEnvelope Envelope => _envelope;
        public float Duration => _envelope?.Duration ?? 0f;

        /// <summary>True while the clip is (probably) playing. The SDK has no query for this, so it's timed.</summary>
        public bool IsPlaying => _startTime >= 0f && (_loop || Time.time < _startTime + Duration);

        /// <summary>Seconds since Play() (wrapped for looping clips) - used to draw a playhead.</summary>
        public float PlaybackTime
        {
            get
            {
                if (_startTime < 0f) return 0f;
                float t = Time.time - _startTime;
                return _loop && Duration > 0f ? t % Duration : Mathf.Min(t, Duration);
            }
        }

        internal ClipVoice(HapticClip clip, uint priority)
        {
            _priority = (uint)Mathf.Clamp((int)priority, 0, 255);
            SetClip(clip);
        }

        public void SetClip(HapticClip clip)
        {
            if (IsDisposed)
                return;
            Stop();
            _clip = clip;
            _envelope = clip != null ? HapticEnvelope.ParseCached(clip.json) : null;
            if (_player != null && clip != null)
            {
                try { _player.clip = clip; ApplySettings(); }
                catch (Exception e) { LabHaptics.MarkMetaFailed(e); _player = null; }
            }
            else
            {
                EnsurePlayer();
            }
        }

        /// <summary>Creates the SDK player if possible (it may fail until the headset is ready).</summary>
        bool EnsurePlayer()
        {
            if (_player != null) return true;
            if (_clip == null || !LabHaptics.MetaAvailable) return false;
            try
            {
                _player = new HapticClipPlayer(_clip);
                _player.priority = _priority;
                ApplySettings();
                return _player != null;
            }
            catch (Exception e)
            {
                LabHaptics.MarkMetaFailed(e);
                _player = null;
                return false;
            }
        }

        /// <summary>Strength multiplier for the clip's amplitudes (0 = silent, 1 = as designed).</summary>
        public float Amplitude
        {
            get => _amplitude;
            set
            {
                value = Mathf.Max(0f, value);
                if (Mathf.Approximately(value, _amplitude)) return;
                _amplitude = value;
                ApplySettings();
            }
        }

        /// <summary>Shifts every frequency in the clip up or down (-1..1). 0 = as designed.</summary>
        public float FrequencyShift
        {
            get => _frequencyShift;
            set
            {
                value = Mathf.Clamp(value, -1f, 1f);
                if (Mathf.Approximately(value, _frequencyShift)) return;
                _frequencyShift = value;
                ApplySettings();
            }
        }

        public bool Loop
        {
            get => _loop;
            set
            {
                if (value == _loop) return;
                _loop = value;
                ApplySettings();
            }
        }

        void ApplySettings()
        {
            if (_player == null) return;
            try
            {
                _player.amplitude = _amplitude * LabHaptics.MasterGain;
                _player.frequencyShift = _frequencyShift;
                _player.isLooping = _loop;
            }
            catch (Exception e)
            {
                LabHaptics.MarkMetaFailed(e);
                _player = null;
            }
        }

        public void Play(Hand hand)
        {
            if (IsDisposed || _clip == null)
                return;
            _startTime = Time.time;

            if (EnsurePlayer())
            {
                ApplySettings(); // picks up a changed LabHaptics.MasterGain
                try
                {
                    if (_player != null)
                    {
                        _player.Play(HandUtil.ToMeta(hand));
                        return;
                    }
                }
                catch (Exception e)
                {
                    LabHaptics.MarkMetaFailed(e);
                    _player = null;
                }
            }

            // Fallback: approximate the envelope with short impulses.
            var runner = LabHapticsRunner.Instance;
            if (_fallback != null) runner.StopCoroutine(_fallback);
            _fallback = runner.StartCoroutine(RunFallback(hand));
        }

        public void Stop()
        {
            _startTime = -1f;
            if (_player != null)
            {
                try { _player.Stop(); }
                catch (Exception e) { LabHaptics.MarkMetaFailed(e); _player = null; }
            }
            if (_fallback != null && LabHapticsRunner.Exists)
                LabHapticsRunner.Instance.StopCoroutine(_fallback);
            _fallback = null;
        }

        IEnumerator RunFallback(Hand hand)
        {
            const float step = 0.02f;
            if (_envelope == null || Duration <= 0f)
            {
                LabHaptics.Pulse(hand, 0.5f * _amplitude, 0.05f);
                yield break;
            }
            do
            {
                float t = 0f;
                int nextEmphasis = 0;
                var pts = _envelope.amplitude;
                while (t < Duration)
                {
                    float amp = _envelope.AmplitudeAt(t);
                    // Transients ("emphasis") become a short, stronger kick.
                    while (nextEmphasis < pts.Length && pts[nextEmphasis].time <= t + step)
                    {
                        if (pts[nextEmphasis].hasEmphasis)
                            amp = Mathf.Max(amp, pts[nextEmphasis].emphasisAmplitude);
                        nextEmphasis++;
                    }
                    LabHaptics.Pulse(hand, amp * _amplitude, step * 1.5f);
                    yield return new WaitForSeconds(step);
                    t += step;
                }
            } while (_loop && _startTime >= 0f);
            _fallback = null;
        }

        public void Dispose()
        {
            if (IsDisposed) return;
            Stop();
            try { _player?.Dispose(); }
            catch (Exception) { /* SDK already shut down */ }
            _player = null;
            IsDisposed = true;
        }
    }
}
