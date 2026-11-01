using UnityEngine;

namespace HapticsLab
{
    /// <summary>Anything a fingertip can touch: material panels, buttons, ...</summary>
    public interface ITouchable
    {
        void TouchBegin(Fingertip tip);
        void TouchStay(Fingertip tip);
        void TouchEnd(Fingertip tip);
    }

    /// <summary>
    /// A small trigger sphere at the tip of each controller. It measures its own velocity
    /// and tells <see cref="ITouchable"/> objects when it touches, rubs and leaves them.
    /// (Physics triggers are simpler than XRI poke interaction for continuous contact.)
    /// </summary>
    [RequireComponent(typeof(SphereCollider), typeof(Rigidbody))]
    [AddComponentMenu("Haptics Lab/Fingertip")]
    public class Fingertip : MonoBehaviour
    {
        public Hand hand = Hand.Right;

        /// <summary>Smoothed world-space velocity (m/s).</summary>
        public Vector3 Velocity { get; private set; }
        public Vector3 Position => transform.position;

        Vector3 _lastPos;
        ITouchable _current;
        Collider _currentCollider;

        void Awake()
        {
            var rb = GetComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;
            GetComponent<SphereCollider>().isTrigger = true;
        }

        void OnEnable()
        {
            _lastPos = transform.position;
            Velocity = Vector3.zero;
        }

        void OnDisable()
        {
            if (_current != null) _current.TouchEnd(this);
            _current = null;
            _currentCollider = null;
        }

        void Update()
        {
            var p = transform.position;
            var v = (p - _lastPos) / Mathf.Max(Time.deltaTime, 1e-4f);
            Velocity = Vector3.Lerp(Velocity, v, 0.5f);
            _lastPos = p;

            // Unity sends no exit event if the other collider is disabled.
            if (_current != null && (_currentCollider == null || !_currentCollider.enabled || !_currentCollider.gameObject.activeInHierarchy))
            {
                _current.TouchEnd(this);
                _current = null;
                _currentCollider = null;
            }
        }

        void OnTriggerEnter(Collider other)
        {
            var t = other.GetComponentInParent<ITouchable>();
            if (t == null || t == _current) return;
            if (_current != null) _current.TouchEnd(this);
            _current = t;
            _currentCollider = other;
            t.TouchBegin(this);
        }

        void OnTriggerStay(Collider other)
        {
            if (_current != null && other == _currentCollider) _current.TouchStay(this);
        }

        void OnTriggerExit(Collider other)
        {
            if (_current == null || other != _currentCollider) return;
            var t = _current;
            _current = null;
            _currentCollider = null;
            t.TouchEnd(this);
        }
    }
}
