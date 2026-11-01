#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features;

namespace HapticsLab
{
    /// <summary>
    /// Makes designed haptics (.haptic clips) work over Quest Link.
    ///
    /// When the runtime offers parametric haptics (XR_EXT_haptic_parametric), the Meta Haptics SDK
    /// streams clips that way. Over Link (tested: Quest 3S, Meta XR SDK 207) the runtime advertises
    /// parametric haptics but rejects parametric vibrations, and the Haptics SDK ignores the error,
    /// so clips are silent. PCM haptics (XR_FB_haptic_pcm) work over Link. So on Windows we stop the
    /// Meta XR Feature from requesting the parametric extension before OpenXR starts; the SDK then
    /// plays the same clips through PCM. Quest builds are not affected.
    /// Remove this file once Meta fixes parametric haptics over Link.
    /// </summary>
    static class LinkHapticsFix
    {
        const string ParametricExtension = "XR_EXT_haptic_parametric";

        static OpenXRFeature s_feature;
        static string s_original;

        // Runs before XR Plug-in Management creates the OpenXR instance (AfterAssembliesLoaded).
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void DisableParametricHaptics()
        {
            var settings = OpenXRSettings.Instance;
            var field = typeof(OpenXRFeature).GetField("openxrExtensionStrings", BindingFlags.Instance | BindingFlags.NonPublic);
            var feature = settings?.GetFeatures().FirstOrDefault(f => f != null && f.GetType().Name == "MetaXRFeature");
            if (field == null || feature == null)
            {
                Debug.LogWarning("[HapticsLab] Could not find the Meta XR Feature; designed haptics may not play over Link.");
                return;
            }

            var extensions = (string)field.GetValue(feature) ?? "";
            if (!extensions.Contains(ParametricExtension))
                return;

            s_feature = feature;
            s_original = extensions;
            field.SetValue(feature, string.Join(" ", extensions.Split(' ').Where(e => e != ParametricExtension)));
            Debug.Log("[HapticsLab] Quest Link: parametric haptics disabled, the Haptics SDK will use PCM.");

            // The settings asset is shared with the editor: put the original value back afterwards.
            Application.quitting += () => field.SetValue(s_feature, s_original);
        }
    }
}
#endif
