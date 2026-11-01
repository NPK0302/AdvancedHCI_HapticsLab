using System;
using System.Collections.Generic;
using UnityEngine;

namespace HapticsLab
{
    /// <summary>
    /// A read-only view of the data inside a .haptic file (spec 1.0).
    ///
    /// The Meta Haptics SDK plays .haptic files for you, so you never *need* this.
    /// The lab uses it to (1) draw clips on the Design Bench, (2) know a clip's
    /// duration, and (3) approximate a clip with simple pulses on platforms where
    /// the Meta SDK is unavailable (e.g. the Unity editor on macOS).
    ///
    /// File layout:  signals.continuous.envelopes.{amplitude[], frequency[]}
    ///   amplitude point: { time, amplitude, emphasis? { amplitude, frequency } }
    ///   frequency point: { time, frequency }
    /// All values are normalised 0..1; the runtime maps them onto the actuator.
    /// </summary>
    public sealed class HapticEnvelope
    {
        public struct AmplitudePoint
        {
            public float time;
            public float amplitude;
            public bool hasEmphasis;
            public float emphasisAmplitude;
            public float emphasisFrequency;
        }

        public struct FrequencyPoint
        {
            public float time;
            public float frequency;
        }

        public readonly AmplitudePoint[] amplitude;
        public readonly FrequencyPoint[] frequency;
        public readonly string description;

        public float Duration => amplitude.Length == 0 ? 0f : amplitude[amplitude.Length - 1].time;
        public int EmphasisCount { get; }

        HapticEnvelope(AmplitudePoint[] amp, FrequencyPoint[] freq, string desc)
        {
            amplitude = amp;
            frequency = freq;
            description = desc;
            foreach (var p in amp)
                if (p.hasEmphasis)
                    EmphasisCount++;
        }

        /// <summary>Linear interpolation of the amplitude envelope at time t.</summary>
        public float AmplitudeAt(float t)
        {
            if (amplitude.Length == 0)
                return 0f;
            if (t <= amplitude[0].time)
                return amplitude[0].amplitude;
            for (int i = 1; i < amplitude.Length; i++)
            {
                if (t <= amplitude[i].time)
                {
                    var a = amplitude[i - 1];
                    var b = amplitude[i];
                    float span = Mathf.Max(1e-5f, b.time - a.time);
                    return Mathf.Lerp(a.amplitude, b.amplitude, (t - a.time) / span);
                }
            }
            return amplitude[amplitude.Length - 1].amplitude;
        }

        /// <summary>Frequency at time t (0.5 if the clip has no frequency envelope).</summary>
        public float FrequencyAt(float t)
        {
            if (frequency.Length == 0)
                return 0.5f;
            if (t <= frequency[0].time)
                return frequency[0].frequency;
            for (int i = 1; i < frequency.Length; i++)
            {
                if (t <= frequency[i].time)
                {
                    var a = frequency[i - 1];
                    var b = frequency[i];
                    float span = Mathf.Max(1e-5f, b.time - a.time);
                    return Mathf.Lerp(a.frequency, b.frequency, (t - a.time) / span);
                }
            }
            return frequency[frequency.Length - 1].frequency;
        }

        // ---------------------------------------------------------------
        // Parsing (JsonUtility needs plain serialisable classes)
        // ---------------------------------------------------------------
        [Serializable] class FileJson { public MetaJson metadata; public SignalsJson signals; }
        [Serializable] class MetaJson { public string description; }
        [Serializable] class SignalsJson { public ContinuousJson continuous; }
        [Serializable] class ContinuousJson { public EnvelopesJson envelopes; }
        [Serializable] class EnvelopesJson { public AmpJson[] amplitude; public FreqJson[] frequency; }
        [Serializable] class AmpJson { public float time; public float amplitude; public EmphasisJson emphasis; }
        [Serializable] class EmphasisJson { public float amplitude = -1f; public float frequency; }
        [Serializable] class FreqJson { public float time; public float frequency; }

        static readonly Dictionary<string, HapticEnvelope> s_cache = new Dictionary<string, HapticEnvelope>();

        /// <summary>Parses .haptic JSON. Returns null if the text is not a valid clip.</summary>
        public static HapticEnvelope Parse(string json)
        {
            if (string.IsNullOrEmpty(json))
                return null;

            FileJson file;
            try
            {
                file = JsonUtility.FromJson<FileJson>(json);
            }
            catch (Exception)
            {
                return null;
            }

            var env = file?.signals?.continuous?.envelopes;
            if (env?.amplitude == null || env.amplitude.Length < 2)
                return null;

            var amp = new AmplitudePoint[env.amplitude.Length];
            for (int i = 0; i < amp.Length; i++)
            {
                var src = env.amplitude[i];
                // JsonUtility creates an EmphasisJson even if the key is missing, so we
                // use the -1 sentinel to tell "no emphasis" apart from "emphasis 0".
                bool has = src.emphasis != null && src.emphasis.amplitude >= 0f;
                amp[i] = new AmplitudePoint
                {
                    time = src.time,
                    amplitude = Mathf.Clamp01(src.amplitude),
                    hasEmphasis = has,
                    emphasisAmplitude = has ? Mathf.Clamp01(src.emphasis.amplitude) : 0f,
                    emphasisFrequency = has ? Mathf.Clamp01(src.emphasis.frequency) : 0f,
                };
            }

            var freq = new FrequencyPoint[env.frequency?.Length ?? 0];
            for (int i = 0; i < freq.Length; i++)
                freq[i] = new FrequencyPoint { time = env.frequency[i].time, frequency = Mathf.Clamp01(env.frequency[i].frequency) };

            return new HapticEnvelope(amp, freq, file.metadata?.description ?? "");
        }

        /// <summary>Parse with a small cache (clip JSON never changes at runtime).</summary>
        public static HapticEnvelope ParseCached(string json)
        {
            if (string.IsNullOrEmpty(json))
                return null;
            if (s_cache.TryGetValue(json, out var env))
                return env;
            env = Parse(json);
            if (s_cache.Count > 256)
                s_cache.Clear();
            s_cache[json] = env;
            return env;
        }
    }
}
