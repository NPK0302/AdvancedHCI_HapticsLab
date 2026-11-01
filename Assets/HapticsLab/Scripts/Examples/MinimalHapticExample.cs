using Oculus.Haptics;                                   // Meta Haptics SDK
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Haptics; // XRI simple haptics
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

/// <summary>
/// The smallest possible example of both kinds of haptics, without any lab code.
/// Add it to an object with an XRGrabInteractable and assign a .haptic clip.
///
///   Simple:   HapticImpulsePlayer.SendHapticImpulse(amplitude, duration)
///   Designed: new HapticClipPlayer(clip).Play(Controller.Right)
/// </summary>
[RequireComponent(typeof(XRGrabInteractable))]
public class MinimalHapticExample : MonoBehaviour
{
    [Tooltip("Exported from Haptics Studio (drag the .haptic file into Unity).")]
    public HapticClip clip;
    public bool useDesignedClip = true;

    HapticClipPlayer _player;

    void Awake()
    {
        if (clip != null) _player = new HapticClipPlayer(clip);
        GetComponent<XRGrabInteractable>().selectEntered.AddListener(OnGrab);
    }

    void OnGrab(SelectEnterEventArgs args)
    {
        bool left = args.interactorObject.handedness == InteractorHandedness.Left;

        if (useDesignedClip && _player != null)
        {
            // Designed haptics: amplitude + frequency envelopes and emphasis from the clip.
            _player.amplitude = 1f;      // scale strength (0..)
            _player.frequencyShift = 0f; // shift "pitch" (-1..1)
            _player.Play(left ? Controller.Left : Controller.Right);
        }
        else
        {
            // Simple haptics: one amplitude for a fixed time.
            var impulse = (args.interactorObject as Component)?.GetComponentInParent<HapticImpulsePlayer>();
            impulse?.SendHapticImpulse(0.6f, 0.1f);
        }
    }

    void OnDestroy() => _player?.Dispose();
}
