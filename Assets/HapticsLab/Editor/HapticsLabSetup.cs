using System;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.PackageManager.UI;
using UnityEngine;

namespace HapticsLab.Editor
{
    /// <summary>
    /// "Haptics Lab > 1. Set Up Project": imports XRI Starter Assets, assigns the OpenXR
    /// loader, enables the Quest controller profiles + Meta features and sets Android
    /// player settings. Everything is done defensively (reflection where package APIs
    /// differ between versions) and reported in the Console, so a failed step tells you
    /// exactly what to click by hand.
    /// </summary>
    public static class HapticsLabSetup
    {
        const string XriPackage = "com.unity.xr.interaction.toolkit";

        [MenuItem("Haptics Lab/1. Set Up Project", priority = 1)]
        public static void SetUpProject()
        {
            var log = new StringBuilder("<b>[HapticsLab] Project setup</b>\n");
            bool ok = true;
            ok &= Step(log, "Import XRI Starter Assets", ImportStarterAssets);
            ok &= Step(log, "Import TextMesh Pro essentials", ImportTmpEssentials);
            ok &= Step(log, "Assign OpenXR loader (Windows + Android)", AssignOpenXRLoaders);
            ok &= Step(log, "Enable OpenXR features (Touch profiles, Meta Quest, Meta XR)", EnableOpenXRFeatures);
            ok &= Step(log, "Player settings (linear colour, Android ARM64/IL2CPP/API 32)", ConfigurePlayer);
            AssetDatabase.SaveAssets();
            log.AppendLine(ok
                ? "Done. Next: <b>Edit > Project Settings > XR Plug-in Management > Project Validation > Fix All</b>, then open <b>Assets/HapticsLab/Scenes/HapticsLab.unity</b>."
                : "Some steps need attention (see above). Fix them, then run this again.");
            Debug.Log(log.ToString());
            EditorUtility.DisplayDialog("Haptics Lab",
                ok ? "Project set up.\n\nNext:\n1. Edit > Project Settings > XR Plug-in Management > Project Validation > Fix All\n2. Open Assets/HapticsLab/Scenes/HapticsLab.unity and press Play"
                   : "Some steps need attention - see the Console for details.", "OK");
        }

        static bool Step(StringBuilder log, string name, Func<string> action)
        {
            try
            {
                var result = action();
                bool ok = result == null || !result.StartsWith("!");
                log.AppendLine((ok ? "  ✔ " : "  ✖ ") + name + (string.IsNullOrEmpty(result) ? "" : ": " + result.TrimStart('!')));
                return ok;
            }
            catch (Exception e)
            {
                log.AppendLine("  ✖ " + name + ": " + e.GetType().Name + " - " + e.Message);
                return false;
            }
        }

        // ------------------------------------------------------------------
        static string ImportStarterAssets()
        {
            var sample = Sample.FindByPackage(XriPackage, string.Empty)
                .FirstOrDefault(s => s.displayName == "Starter Assets");
            if (string.IsNullOrEmpty(sample.displayName))
                return "!XR Interaction Toolkit not found. Is it in Packages/manifest.json?";
            if (sample.isImported)
                return "already imported";
            sample.Import(Sample.ImportOptions.HideImportWindow | Sample.ImportOptions.OverridePreviousImports);
            return "imported";
        }

        static string ImportTmpEssentials()
        {
            if (Resources.Load("TMP Settings") != null)
                return "already imported";
            TMPro.TMP_PackageResourceImporter.ImportResources(true, false, false);
            return "imported";
        }

        /// <summary>Windows always; Android only if the Android Build Support module is installed.</summary>
        static BuildTargetGroup[] TargetGroups()
        {
            return BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Android, BuildTarget.Android)
                ? new[] { BuildTargetGroup.Standalone, BuildTargetGroup.Android }
                : new[] { BuildTargetGroup.Standalone };
        }

        static string AndroidNote() =>
            BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Android, BuildTarget.Android)
                ? ""
                : "  (Android skipped: install 'Android Build Support' in Unity Hub for APK builds, then run this again)";

        static string AssignOpenXRLoaders()
        {
            var sb = new StringBuilder();
            foreach (var group in TargetGroups())
            {
                var settings = UnityEditor.XR.Management.XRGeneralSettingsPerBuildTarget.XRGeneralSettingsForBuildTarget(group);
                if (settings == null || settings.Manager == null)
                {
                    SettingsService.OpenProjectSettings("Project/XR Plug-in Management");
                    return "!Settings did not exist yet. The XR Plug-in Management page is now open " +
                           "(this creates them). Tick OpenXR on the Windows and Android tabs, or run this menu again.";
                }
                bool assigned = UnityEditor.XR.Management.Metadata.XRPackageMetadataStore.AssignLoader(
                    settings.Manager, "UnityEngine.XR.OpenXR.OpenXRLoader", group);
                settings.InitManagerOnStart = true;
                EditorUtility.SetDirty(settings);
                EditorUtility.SetDirty(settings.Manager);
                sb.Append(group).Append(assigned ? " ok  " : " (already)  ");
            }
            return sb + AndroidNote();
        }

        static readonly string[] s_featuresToEnable =
        {
            "OculusTouchControllerProfile",
            "MetaQuestTouchPlusControllerProfile",
            "MetaQuestTouchProControllerProfile",
            "MetaQuestFeature",   // Unity OpenXR: Meta Quest support (Android)
            "MetaXRFeature",      // Meta XR Core SDK (needed by the Haptics SDK)
        };

        static string EnableOpenXRFeatures()
        {
            var sb = new StringBuilder();
            foreach (var group in TargetGroups())
            {
                var settings = UnityEngine.XR.OpenXR.OpenXRSettings.GetSettingsForBuildTargetGroup(group);
                if (settings == null)
                    return "!No OpenXR settings for " + group + " - run step 'Assign OpenXR loader' first.";
                int count = 0;
                foreach (var f in settings.GetFeatures())
                {
                    if (f == null) continue;
                    if (s_featuresToEnable.Contains(f.GetType().Name) && !f.enabled)
                    {
                        f.enabled = true;
                        EditorUtility.SetDirty(f);
                        count++;
                    }
                }
                EditorUtility.SetDirty(settings);
                sb.Append(group).Append(": +").Append(count).Append("  ");
            }
            return sb + AndroidNote();
        }

        static string ConfigurePlayer()
        {
            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            if ((int)PlayerSettings.Android.minSdkVersion < 32)
                PlayerSettings.Android.minSdkVersion = (AndroidSdkVersions)32;
            if (PlayerSettings.productName == "AdvancedHCI_HapticsLab" || string.IsNullOrEmpty(PlayerSettings.productName))
                PlayerSettings.productName = "Haptics Lab";
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, "dk.aau.advancedhci.hapticslab");
            return null;
        }

        // ------------------------------------------------------------------
        [MenuItem("Haptics Lab/Open Live Folder", priority = 20)]
        static void OpenLiveFolder()
        {
            var path = System.IO.Path.GetFullPath("Assets/HapticsLab/Haptics/Live");
            System.IO.Directory.CreateDirectory(path);
            EditorUtility.RevealInFinder(path + "/");
        }

        [MenuItem("Haptics Lab/Open Haptics Studio on GitHub", priority = 21)]
        static void OpenStudio() => Application.OpenURL("https://github.com/facebook/haptics-studio");
    }
}
