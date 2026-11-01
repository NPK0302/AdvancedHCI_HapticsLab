using System.Collections.Generic;
using UnityEngine;

namespace HapticsLab
{
    /// <summary>
    /// Draws a .haptic clip like Haptics Studio does: amplitude envelope, frequency
    /// envelope and emphasis (transient) markers, plus a moving playhead.
    /// The graph lies in the local XY plane of this transform, size width x height.
    /// </summary>
    [AddComponentMenu("Haptics Lab/Envelope View")]
    public class EnvelopeView : MonoBehaviour
    {
        public float width = 1.1f;
        public float height = 0.4f;
        public Color amplitudeColor = new Color(1f, 0.6f, 0.2f);
        public Color frequencyColor = new Color(0.35f, 0.65f, 1f);
        public Color emphasisColor = new Color(1f, 0.95f, 0.4f);
        public Color playheadColor = Color.white;
        public Material lineMaterial;
        [Min(16)] public int samples = 200;

        LineRenderer _amp, _freq, _head;
        readonly List<Transform> _markers = new List<Transform>();
        float _duration;

        void Awake()
        {
            _amp = MakeLine("Amplitude", amplitudeColor, 0.006f);
            _freq = MakeLine("Frequency", frequencyColor, 0.004f);
            _head = MakeLine("Playhead", playheadColor, 0.003f);
            _head.positionCount = 2;
            _head.enabled = false;
        }

        LineRenderer MakeLine(string n, Color c, float w)
        {
            var go = new GameObject(n);
            go.transform.SetParent(transform, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = false;
            lr.widthMultiplier = w;
            lr.startColor = lr.endColor = c;
            lr.numCapVertices = 2;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            if (lineMaterial != null)
            {
                lr.sharedMaterial = lineMaterial;
            }
            lr.positionCount = 0;
            return lr;
        }

        Vector3 P(float t01, float v01, float z = 0f) =>
            new Vector3((t01 - 0.5f) * width, (v01 - 0.5f) * height, z);

        public void Show(HapticEnvelope env)
        {
            foreach (var m in _markers) m.gameObject.SetActive(false);
            if (env == null || env.Duration <= 0f)
            {
                _amp.positionCount = _freq.positionCount = 0;
                _duration = 0f;
                return;
            }

            _duration = env.Duration;
            _amp.positionCount = samples;
            _freq.positionCount = samples;
            for (int i = 0; i < samples; i++)
            {
                float u = i / (samples - 1f);
                float t = u * _duration;
                _amp.SetPosition(i, P(u, env.AmplitudeAt(t), -0.002f));
                _freq.SetPosition(i, P(u, env.FrequencyAt(t), -0.001f));
            }

            int k = 0;
            foreach (var p in env.amplitude)
            {
                if (!p.hasEmphasis) continue;
                var m = Marker(k++);
                m.localPosition = P(p.time / _duration, p.emphasisAmplitude, -0.003f);
                m.gameObject.SetActive(true);
            }
        }

        Transform Marker(int i)
        {
            while (_markers.Count <= i)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
                Destroy(go.GetComponent<Collider>());
                go.name = "Emphasis";
                go.transform.SetParent(transform, false);
                go.transform.localScale = Vector3.one * 0.018f;
                go.transform.localRotation = Quaternion.Euler(0, 0, 45);
                var r = go.GetComponent<Renderer>();
                if (lineMaterial != null) r.sharedMaterial = lineMaterial;
                var mpb = new MaterialPropertyBlock();
                mpb.SetColor("_BaseColor", emphasisColor);
                mpb.SetColor("_Color", emphasisColor);
                r.SetPropertyBlock(mpb);
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                _markers.Add(go.transform);
            }
            return _markers[i];
        }

        /// <summary>Show the playhead at time t (seconds), or hide it with a negative value.</summary>
        public void SetPlayhead(float t)
        {
            if (t < 0f || _duration <= 0f) { _head.enabled = false; return; }
            _head.enabled = true;
            float u = Mathf.Clamp01(t / _duration);
            _head.SetPosition(0, P(u, 0f, -0.004f));
            _head.SetPosition(1, P(u, 1f, -0.004f));
        }
    }
}
