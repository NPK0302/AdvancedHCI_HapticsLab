using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Haptics;
using Oculus.Haptics;

namespace HapticsLab
{
    /// <summary>
    /// One small entry point for all haptics in the lab.
    ///
    ///  * <see cref="Pulse"/>        - "simple" haptics: one amplitude for a duration.
    ///                                 Uses XRI's HapticImpulsePlayer (works on any OpenXR controller).
    ///  * <see cref="CreateVoice"/>  - "designed" haptics: plays a .haptic clip made in Haptics Studio
    ///                                 through the Meta Haptics SDK (HapticClipPlayer).
    ///
    /// If the Meta SDK cannot run (unsupported platform, no headset yet), clips fall back to
    /// a pulse-based approximation so the demo keeps working everywhere.
    /// </summary>
    public static class LabHaptics
    {
        static HapticImpulsePlayer s_left, s_right;
        static bool s_searchedForPlayers;
        static bool s_metaFailed;
        static float s_metaFailedAt;
        const float MetaRetryDelay = 5f;
        static readonly Dictionary<HapticClip, ClipVoice> s_shared = new Dictionary<HapticClip, ClipVoice>();

        /// <summary>Global strength multiplier (0..1), e.g. for an accessibility slider. Applies from the next Play.</summary>
        public static float MasterGain = 1f;

        /// <summary>True on Quest builds and in the Windows editor/player (Quest Link).</summary>
        public static bool MetaSupportedPlatform
        {
            get
            {
#if (UNITY_ANDROID && !UNITY_EDITOR) || UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
                return true;
#else
                return false;
#endif
            }
        }

        /// <summary>Whether .haptic clips are currently rendered by the Meta Haptics SDK.</summary>
        public static bool MetaAvailable
        {
            get
            {
                // A failure is often temporary (headset not ready yet), so retry after a few seconds.
                if (s_metaFailed && Time.realtimeSinceStartup - s_metaFailedAt > MetaRetryDelay)
                    s_metaFailed = false;
                return MetaSupportedPlatform && !s_metaFailed;
            }
        }

        public static event Action<string> StatusChanged;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            // Keeps things correct when "Enter Play Mode Options" disables domain reload.
            s_left = s_right = null;
            s_searchedForPlayers = false;
            s_metaFailed = false;
            s_metaFailedAt = 0f;
            s_shared.Clear();
            MasterGain = 1f;
        }

        internal static void MarkMetaFailed(Exception e)
        {
            bool first = s_metaFailedAt <= 0f;
            s_metaFailed = true;
            s_metaFailedAt = Time.realtimeSinceStartup;
            if (!first)
                return;
            var msg = "Meta Haptics SDK not ready - clips fall back to simple pulses for now. (" + e.Message + ")";
            Debug.LogWarning("[HapticsLab] " + msg);
            StatusChanged?.Invoke(msg);
        }

        // -----------------------------------------------------------------
        // Simple haptics
        // -----------------------------------------------------------------

        /// <summary>Tell the lab which XRI impulse players belong to which hand.</summary>
        public static void RegisterImpulsePlayers(HapticImpulsePlayer left, HapticImpulsePlayer right)
        {
            s_left = left;
            s_right = right;
            s_searchedForPlayers = true;
        }

        /// <summary>
        /// Simple haptics: vibrate at one amplitude (0..1) for a duration in seconds.
        /// This is the same as XRI's <c>HapticImpulsePlayer.SendHapticImpulse</c>.
        /// </summary>
        public static void Pulse(Hand hand, float amplitude, float duration, float frequency = 0f)
        {
            amplitude = Mathf.Clamp01(amplitude * MasterGain);
            if (amplitude <= 0f || duration <= 0f)
                return;
            if (hand != Hand.Right)
                SendImpulse(XRNode.LeftHand, amplitude, duration, frequency);
            if (hand != Hand.Left)
                SendImpulse(XRNode.RightHand, amplitude, duration, frequency);
        }

        static void SendImpulse(XRNode node, float amplitude, float duration, float frequency)
        {
            FindPlayersIfNeeded();
            var player = node == XRNode.LeftHand ? s_left : s_right;
            if (player != null && player.isActiveAndEnabled)
            {
                if (frequency > 0f)
                    player.SendHapticImpulse(amplitude, duration, frequency);
                else
                    player.SendHapticImpulse(amplitude, duration);
                return;
            }

            // Fallback without XRI components: Unity's generic XR input API.
            var device = InputDevices.GetDeviceAtXRNode(node);
            if (device.isValid)
                device.SendHapticImpulse(0, amplitude, duration);
        }

        static void FindPlayersIfNeeded()
        {
            if (s_searchedForPlayers && (s_left != null || s_right != null))
                return;
            s_searchedForPlayers = true;
            foreach (var p in UnityEngine.Object.FindObjectsByType<HapticImpulsePlayer>())
            {
                for (var t = p.transform; t != null; t = t.parent)
                {
                    var n = t.name.ToLowerInvariant();
                    if (n.Contains("left")) { s_left ??= p; break; }
                    if (n.Contains("right")) { s_right ??= p; break; }
                }
            }
        }

        // -----------------------------------------------------------------
        // Designed haptics (.haptic clips)
        // -----------------------------------------------------------------

        /// <summary>
        /// Creates a player for a .haptic clip. Keep it and reuse it; call Dispose when done.
        /// Priority: 0 = most important, 255 = least (the SDK plays only the most important
        /// clip on each controller at a time - clips are not mixed).
        /// </summary>
        public static ClipVoice CreateVoice(HapticClip clip, uint priority = 128)
        {
            return new ClipVoice(clip, priority);
        }

        /// <summary>A shared, fire-and-forget player per clip (handy for UI clicks and ticks).</summary>
        public static ClipVoice Shared(HapticClip clip, uint priority = 64)
        {
            if (clip == null)
                return null;
            if (!s_shared.TryGetValue(clip, out var voice) || voice.IsDisposed)
            {
                voice = new ClipVoice(clip, priority);
                s_shared[clip] = voice;
            }
            return voice;
        }

        /// <summary>Play a clip once on a hand at a given strength (0..1).</summary>
        public static void PlayOnce(HapticClip clip, Hand hand, float strength = 1f)
        {
            var v = Shared(clip);
            if (v == null)
                return;
            v.Amplitude = strength;
            v.Play(hand);
        }
    }
}
