using System.Collections.Generic;
using System.IO;
using System.Linq;
using Oculus.Haptics;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Feedback;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace HapticsLab.Editor
{
    /// <summary>
    /// "Haptics Lab > 2. Build Lab Scene" builds the whole demo from code, so the scene can
    /// always be regenerated and every object is plain Unity primitives you can inspect.
    ///
    /// Layout (user stands at the origin):
    ///   front  - 1 Material Wall      left - 2 Safe
    ///   right  - 3 Design Bench       back - 4 Blind Test
    ///
    /// Convention: in each zone's local space +Z points AWAY from the user, so panels,
    /// text and buttons face the user along -Z.
    /// </summary>
    public static class HapticsLabSceneBuilder
    {
        const string Root = "Assets/HapticsLab";
        const string ScenePath = Root + "/Scenes/HapticsLab.unity";
        const string GeneratedPath = Root + "/Generated";
        const float ZoneRadius = 1.35f;

        static readonly Dictionary<string, Material> s_mats = new Dictionary<string, Material>();
        static HapticClip s_uiPress;
        static AudioClip s_uiPressSound;
        static TMP_FontAsset s_font;

        [MenuItem("Haptics Lab/2. Build Lab Scene", priority = 2)]
        public static void Build()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            var rigPrefab = FindRigPrefab();
            if (rigPrefab == null)
            {
                EditorUtility.DisplayDialog("Haptics Lab", "Could not find the XRI 'XR Origin (XR Rig)' prefab.\n\nRun Haptics Lab > 1. Set Up Project first (it imports XRI Starter Assets).", "OK");
                return;
            }

            s_font = TMP_Settings.defaultFontAsset;
            if (s_font == null)
            {
                EditorUtility.DisplayDialog("Haptics Lab", "TextMesh Pro essentials are missing.\n\nRun Haptics Lab > 1. Set Up Project (or Window > TextMeshPro > Import TMP Essential Resources), then build again.", "OK");
                return;
            }

            s_mats.Clear();
            Directory.CreateDirectory(GeneratedPath);
            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
            s_uiPress = Load<HapticClip>("Haptics/UI_Press.haptic");
            s_uiPressSound = Load<AudioClip>("Audio/UI_Press.wav");

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            BuildEnvironment();
            var rig = (GameObject)PrefabUtility.InstantiatePrefab(rigPrefab);
            rig.transform.position = Vector3.zero;
            // XRI's starter rig buzzes on every hover and grab. Turn that off so the
            // controllers only play the lab's own feedback.
            foreach (var f in rig.GetComponentsInChildren<SimpleHapticFeedback>(true))
                f.enabled = false;
            ConfigureRigForTouch(rig);
            var labRig = rig.AddComponent<LabRig>();
            labRig.fingertipMaterial = Mat("Fingertip");
            new GameObject("XR Interaction Manager").AddComponent<XRInteractionManager>();

            var wall = BuildMaterialWall(Zone("1 Material Wall", 0f));
            BuildSafe(Zone("2 Safe", -90f));
            BuildBench(Zone("3 Design Bench", 90f));
            BuildBlindTest(Zone("4 Blind Test", 180f), wall);

            EditorSceneManager.SaveScene(scene, ScenePath);
            AddSceneToBuild(ScenePath);
            AssetDatabase.SaveAssets();
            Debug.Log("<b>[HapticsLab]</b> Scene built and saved to " + ScenePath + ". Put on the headset (Quest Link) and press Play.");
        }

        /// <summary>
        /// Everything in the lab is touched or grabbed within reach, so the starter rig's pointers
        /// only get in the way: no far ray, no XRI poke (the lab has its own fingertips) and no
        /// teleport. Stick movement and snap turn stay, for people testing seated.
        /// </summary>
        static void ConfigureRigForTouch(GameObject rig)
        {
            foreach (var nearFar in rig.GetComponentsInChildren<UnityEngine.XR.Interaction.Toolkit.Interactors.NearFarInteractor>(true))
                nearFar.enableFarCasting = false;
            foreach (var curve in rig.GetComponentsInChildren<UnityEngine.XR.Interaction.Toolkit.Interactors.Visuals.CurveVisualController>(true))
            {
                if (curve.lineRenderer != null) curve.lineRenderer.enabled = false;
                curve.enabled = false;
            }
            foreach (var poke in rig.GetComponentsInChildren<UnityEngine.XR.Interaction.Toolkit.Interactors.XRPokeInteractor>(true))
                poke.gameObject.SetActive(false);

            // The starter rig's ControllerInputActionManager turns the teleport ray on when the stick
            // is pushed forward. Without a teleport action it never does; move and turn are separate.
            foreach (var manager in rig.GetComponentsInChildren<MonoBehaviour>(true).Where(m => m != null && m.GetType().Name == "ControllerInputActionManager"))
            {
                var so = new SerializedObject(manager);
                foreach (var name in new[] { "m_TeleportMode", "m_TeleportModeCancel" })
                {
                    var p = so.FindProperty(name);
                    if (p != null) p.objectReferenceValue = null;
                }
                so.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        // ==================================================================
        // Zones
        // ==================================================================
        static Transform Zone(string name, float yaw)
        {
            var go = new GameObject(name);
            var rot = Quaternion.Euler(0f, yaw, 0f);
            go.transform.SetPositionAndRotation(rot * new Vector3(0f, 0f, ZoneRadius), rot);
            return go.transform;
        }

        static MaterialWall BuildMaterialWall(Transform z)
        {
            Box(z, "Backboard", new Vector3(0f, 1.2f, 0.05f), new Vector3(1.3f, 1.0f, 0.04f), Mat("Board"), false);
            Text(z, "Title", "<b>1  MATERIAL WALL</b>", new Vector3(0f, 1.64f, 0.02f), 0.55f, 1.2f);
            Text(z, "Subtitle", "Tap and rub the panels with your fingertip. Switch sound and haptics on or off, and choose the fidelity below.", new Vector3(0f, 1.56f, 0.02f), 0.22f, 1.2f);

            var wall = z.gameObject.AddComponent<MaterialWall>();
            string[] names = { "Wood", "Metal", "Glass", "Gravel" };
            // Level 3 texture: distance between ridges/grains (m) and how irregular they are.
            // Level 1 is deliberately the same for every material (the HapticSurface defaults).
            var grains = new[]
            {
                new Vector2(0.008f, 0.1f),  // wood: regular grain ridges, ~8 mm apart
                new Vector2(0.003f, 0.2f),  // metal: very fine brushed scratches
                new Vector2(0.04f, 0.5f),   // glass: slick, only a rare squeak
                new Vector2(0.006f, 0.7f),  // gravel: irregular stones
            };
            for (int i = 0; i < 4; i++)
            {
                float x = -0.45f + i * 0.3f;
                var panel = Box(z, names[i] + " Panel", new Vector3(x, 1.23f, 0f), new Vector3(0.26f, 0.36f, 0.03f), Mat(names[i]), true);
                var s = panel.AddComponent<HapticSurface>();
                s.materialName = names[i];
                s.tapClip = Load<HapticClip>($"Haptics/{names[i]}_Tap.haptic");
                s.textureClip = Load<HapticClip>($"Haptics/{names[i]}_Texture.haptic");
                s.tapSound = Load<AudioClip>($"Audio/{names[i]}_Tap.wav");
                s.scrapeLoop = Load<AudioClip>($"Audio/{names[i]}_Scrape.wav");
                s.grainClip = Load<HapticClip>($"Haptics/{names[i]}_Grain.haptic");
                s.grainSpacing = grains[i].x;
                s.grainJitter = grains[i].y;
                s.wall = wall;
                Text(z, names[i] + " Label", "<b>" + names[i] + "</b>", new Vector3(x, 1.0f, 0.02f), 0.3f, 0.28f);
            }

            // One console, two groups: on/off switches on the left, the haptic fidelity level on the right.
            var console = Group(z, "Wall Console", new Vector3(0f, 0.88f, -0.12f), Quaternion.Euler(35f, 0f, 0f));
            Box(console, "Console Plate", new Vector3(0f, 0f, 0.025f), new Vector3(1.26f, 0.22f, 0.02f), Mat("Dark"), false);

            const float headingY = 0.085f, switchesX = -0.47f, levelsX = 0.17f;
            Text(console, "On Off Heading", "<b>ON / OFF</b>", new Vector3(switchesX, headingY, 0.01f), 0.17f, 0.3f);
            Text(console, "Fidelity Heading", "<b>HAPTIC FIDELITY</b>", new Vector3(levelsX, headingY, 0.01f), 0.17f, 0.8f);
            Box(console, "Divider", new Vector3(-0.31f, -0.01f, 0.012f), new Vector3(0.004f, 0.18f, 0.006f), Mat("White"), false);

            var switchSize = new Vector2(0.22f, 0.07f);
            wall.soundButton = Button(console, "Sound", new Vector3(switchesX, 0.035f, 0f), switchSize);
            UnityEventTools.AddPersistentListener(wall.soundButton.onPress, wall.ToggleSound);
            wall.hapticsButton = Button(console, "Haptics", new Vector3(switchesX, -0.045f, 0f), switchSize);
            UnityEventTools.AddPersistentListener(wall.hapticsButton.onPress, wall.ToggleHaptics);

            for (int i = 0; i < 3; i++)
            {
                int level = i + 1;
                var b = Button(console, FidelityInfo.Label((Fidelity)level), new Vector3(levelsX + (i - 1) * 0.25f, -0.008f, 0f), new Vector2(0.235f, 0.08f));
                UnityEventTools.AddIntPersistentListener(b.onPress, wall.SetFidelity, level);
                wall.levelButtons[i] = b;
            }

            wall.description = Text(z, "Description", "", new Vector3(0f, 0.72f, -0.22f), 0.2f, 1.2f, Quaternion.Euler(35f, 0f, 0f));
            wall.sdkStatus = Text(z, "SDK Status", "", new Vector3(0f, 0.665f, -0.255f), 0.14f, 1.2f, Quaternion.Euler(35f, 0f, 0f));

            // Welcome sign above the wall
            Text(z, "Welcome", "<b>HAPTICS LAB</b>  <size=55%>Advanced HCI | VR & Haptics</size>", new Vector3(0f, 1.92f, 0.04f), 0.8f, 1.6f);
            Text(z, "Map", "Turn around:  <b>left</b> 2 Safe   <b>right</b> 3 Design Bench   <b>behind</b> 4 Blind Test", new Vector3(0f, 1.8f, 0.04f), 0.2f, 1.6f);
            return wall;
        }

        static void BuildSafe(Transform z)
        {
            const float w = 0.6f, h = 0.6f, d = 0.45f, cy = 1.05f;
            var body = Group(z, "Safe Body", new Vector3(0f, cy, d * 0.5f), Quaternion.identity);
            var metal = Mat("SafeBody");
            Box(body, "Back", new Vector3(0, 0, d / 2), new Vector3(w, h, 0.03f), metal, true);
            Box(body, "Left", new Vector3(-w / 2, 0, 0), new Vector3(0.03f, h, d), metal, true);
            Box(body, "Right", new Vector3(w / 2, 0, 0), new Vector3(0.03f, h, d), metal, true);
            Box(body, "Top", new Vector3(0, h / 2, 0), new Vector3(w, 0.03f, d), metal, true);
            Box(body, "Bottom", new Vector3(0, -h / 2, 0), new Vector3(w, 0.03f, d), metal, true);
            Box(z, "Plinth", new Vector3(0f, (cy - h / 2) * 0.5f, d * 0.5f), new Vector3(w + 0.05f, cy - h / 2, d + 0.05f), Mat("Dark"), true);

            // Door on a hinge at the left front edge.
            var hinge = Group(z, "Door Hinge", new Vector3(-w / 2, cy, -0.01f), Quaternion.identity);
            Box(hinge, "Door", new Vector3(w / 2, 0f, 0f), new Vector3(w - 0.01f, h - 0.01f, 0.035f), Mat("SafeDoor"), true);
            Box(hinge, "Handle", new Vector3(w - 0.07f, 0f, -0.035f), new Vector3(0.03f, 0.14f, 0.03f), Mat("Metal"), false);

            // Dial (on the door, so it swings with it)
            var dialRoot = Group(hinge, "Dial", new Vector3(w / 2, 0.03f, -0.035f), Quaternion.identity);
            var dialCol = dialRoot.gameObject.AddComponent<BoxCollider>();
            dialCol.size = new Vector3(0.2f, 0.2f, 0.06f);
            dialRoot.gameObject.AddComponent<XRSimpleInteractable>();
            var dial = dialRoot.gameObject.AddComponent<SafeDial>();

            var knob = Group(dialRoot, "Knob", Vector3.zero, Quaternion.identity);
            var cyl = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            cyl.name = "Knob Mesh";
            Object.DestroyImmediate(cyl.GetComponent<Collider>());
            cyl.transform.SetParent(knob, false);
            cyl.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            cyl.transform.localScale = new Vector3(0.17f, 0.012f, 0.17f);
            cyl.GetComponent<Renderer>().sharedMaterial = Mat("Metal");
            var grip = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            grip.name = "Grip";
            Object.DestroyImmediate(grip.GetComponent<Collider>());
            grip.transform.SetParent(knob, false);
            grip.transform.localPosition = new Vector3(0, 0, -0.025f);
            grip.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            grip.transform.localScale = new Vector3(0.07f, 0.02f, 0.07f);
            grip.GetComponent<Renderer>().sharedMaterial = Mat("Dark");

            var numbers = Group(knob, "Numbers", new Vector3(0, 0, -0.014f), Quaternion.identity);
            // 20 detents (18 degrees each): every number is easy to feel and reach without re-gripping.
            const int positions = 20;
            for (int i = 0; i < positions; i++)
            {
                float a = i * 360f / positions;
                var dir = new Vector3(Mathf.Sin(a * Mathf.Deg2Rad), Mathf.Cos(a * Mathf.Deg2Rad), 0f);
                bool major = i % 5 == 0;
                var tick = Box(numbers, "Tick " + i, dir * 0.078f, new Vector3(0.002f, major ? 0.012f : 0.006f, 0.002f), Mat("White"), false);
                tick.transform.localRotation = Quaternion.Euler(0, 0, -a);
                if (major)
                {
                    var t = Text(numbers, "N" + i, i.ToString(), dir * 0.06f, 0.11f, 0.04f, Quaternion.Euler(0, 0, -a));
                    t.transform.localPosition = dir * 0.058f;
                }
            }
            Box(hinge, "Indicator", new Vector3(w / 2, 0.03f + 0.1f, -0.03f), new Vector3(0.008f, 0.02f, 0.008f), Mat("Accent"), false);

            dial.positions = positions;
            dial.sensitivity = 2f; // a half turn of the wrist sweeps the whole dial
            dial.knob = knob;
            dial.tickClip = Load<HapticClip>("Haptics/Safe_Tick.haptic");
            dial.gateClip = Load<HapticClip>("Haptics/Safe_Gate.haptic");
            dial.tickSound = Load<AudioClip>("Audio/Safe_Tick.wav");
            dial.gateSound = Load<AudioClip>("Audio/Safe_Gate.wav");
            dial.audioEnabled = false; // touch first; the Sound button turns it on

            // Reward inside the safe
            var bar = Box(z, "Gold Bar", new Vector3(0f, cy - h / 2 + 0.05f, d * 0.5f), new Vector3(0.16f, 0.045f, 0.07f), Mat("Gold"), true);
            var rb = bar.AddComponent<Rigidbody>();
            rb.mass = 1.0f;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            // Velocity tracking keeps the bar a physics object while held. With the default
            // (instantaneous) movement it turns kinematic and never reports collisions.
            bar.AddComponent<XRGrabInteractable>().movementType = XRBaseInteractable.MovementType.VelocityTracking;
            var fb = bar.AddComponent<HapticAudioFeedback>();
            fb.hapticClip = Load<HapticClip>("Haptics/GoldBar_Grab.haptic");
            fb.sounds = new[] { Load<AudioClip>("Audio/GoldBar_Grab.wav") };
            fb.onSelectEnter = true;
            var clink = bar.AddComponent<HapticAudioFeedback>();
            clink.hapticClip = Load<HapticClip>("Haptics/GoldBar_Clink.haptic");
            clink.sounds = new[] { Load<AudioClip>("Audio/GoldBar_Clink.wav") };
            clink.onSelectEnter = false;
            clink.onCollision = true;
            clink.priority = 64;

            // Controller + text
            var ctrl = z.gameObject.AddComponent<SafeController>();
            ctrl.dial = dial;
            ctrl.doorHinge = hinge;
            ctrl.openAngle = 105f; // positive yaw swings the door towards the user
            ctrl.numberLabels = numbers.gameObject;
            ctrl.reward = bar;
            ctrl.confirmClip = Load<HapticClip>("Haptics/Safe_Confirm.haptic");
            ctrl.errorClip = Load<HapticClip>("Haptics/Safe_Error.haptic");
            ctrl.openClip = Load<HapticClip>("Haptics/Safe_Open.haptic");
            ctrl.confirmSound = Load<AudioClip>("Audio/Safe_Confirm.wav");
            ctrl.errorSound = Load<AudioClip>("Audio/Safe_Error.wav");
            ctrl.openSound = Load<AudioClip>("Audio/Safe_Open.wav");

            Text(z, "Title", "<b>2  THE SAFE</b>", new Vector3(0f, 1.66f, 0.05f), 0.55f, 1.2f);
            ctrl.instructions = Text(z, "Instructions", "", new Vector3(0f, 1.53f, 0.05f), 0.2f, 1.0f);
            // Same depth as the title and instructions, high enough that the safe's top (1.365 m) never hides it.
            ctrl.progress = Text(z, "Progress", "", new Vector3(0f, 1.42f, 0.05f), 0.35f, 0.6f);
            ctrl.readout = Text(z, "Readout", "", new Vector3(0f, cy - h / 2 - 0.05f, -0.06f), 0.22f, 0.6f);
            ctrl.status = Text(z, "Status", "", new Vector3(0f, cy - h / 2 - 0.11f, -0.06f), 0.2f, 1.0f);

            // Turned towards the user, who stands at the centre of the lab (about 20 degrees from here).
            var panel = Group(z, "Safe Buttons", new Vector3(0.48f, 1.05f, 0.0f), Quaternion.Euler(0f, 20f, 0f));
            Box(panel, "Plate", new Vector3(0f, 0f, 0.02f), new Vector3(0.24f, 0.5f, 0.02f), Mat("Dark"), false);
            var size = new Vector2(0.2f, 0.08f);
            var bNew = Button(panel, "New combination", new Vector3(0, 0.18f, 0), size);
            ctrl.soundButton = Button(panel, "Sound", new Vector3(0, 0.06f, 0), size);
            ctrl.numbersButton = Button(panel, "Numbers", new Vector3(0, -0.06f, 0), size);
            ctrl.hintButton = Button(panel, "Visual hint", new Vector3(0, -0.18f, 0), size);
            UnityEventTools.AddPersistentListener(bNew.onPress, ctrl.NewCombination);
            UnityEventTools.AddPersistentListener(ctrl.soundButton.onPress, ctrl.ToggleSound);
            UnityEventTools.AddPersistentListener(ctrl.numbersButton.onPress, ctrl.ToggleNumbers);
            UnityEventTools.AddPersistentListener(ctrl.hintButton.onPress, ctrl.ToggleHint);

            Text(z, "Discuss", "<i>Try: sound OFF + numbers OFF. Can touch alone carry the information?</i>",
                new Vector3(0f, 0.52f, -0.06f), 0.16f, 1.0f);
        }

        static void BuildBench(Transform z)
        {
            Box(z, "Table", new Vector3(0f, 0.85f, 0.05f), new Vector3(1.1f, 0.04f, 0.5f), Mat("Wood"), true);
            Box(z, "Pedestal", new Vector3(0f, 0.415f, 0.08f), new Vector3(1.0f, 0.83f, 0.4f), Mat("Dark"), true);
            Box(z, "Board", new Vector3(0f, 1.38f, 0.3f), new Vector3(1.3f, 0.82f, 0.03f), Mat("Board"), false);
            Text(z, "Zone Title", "<b>3  DESIGN BENCH</b>", new Vector3(0f, 1.74f, 0.28f), 0.5f, 1.2f);

            var bench = z.gameObject.AddComponent<DesignBench>();
            var lib = z.gameObject.AddComponent<LiveClipLibrary>();
            string[] builtIns = { "Bench_Heartbeat", "Bench_Ramp", "Bench_Engine", "Bench_Rain", "Bench_Laser", "Metal_Tap", "Gravel_Texture", "Safe_Open" };
            // Load clip and audio as pairs, so a missing clip can't shift the audio onto the wrong clip.
            var pairs = builtIns
                .Select(n => (clip: Load<HapticClip>($"Haptics/{n}.haptic"), audio: Load<AudioClip>($"Audio/{n.Replace("_Texture", "_Scrape")}.wav")))
                .Where(p => p.clip != null)
                .ToArray();
            lib.builtInClips = pairs.Select(p => p.clip).ToArray();
            lib.builtInAudio = pairs.Select(p => p.audio).ToArray();
            bench.library = lib;

            bench.title = Text(z, "Clip Title", "", new Vector3(0f, 1.65f, 0.28f), 0.3f, 1.2f);
            Box(z, "Graph Background", new Vector3(0f, 1.42f, 0.28f), new Vector3(1.16f, 0.4f, 0.005f), Mat("Graph"), false);
            var envGo = new GameObject("Envelope View");
            envGo.transform.SetParent(z, false);
            envGo.transform.localPosition = new Vector3(0f, 1.42f, 0.275f);
            var env = envGo.AddComponent<EnvelopeView>();
            env.width = 1.1f;
            env.height = 0.34f;
            env.lineMaterial = Mat("Line");
            bench.envelope = env;
            Text(z, "Legend", "<color=#fa4>- amplitude</color>    <color=#6af>- frequency</color>    <color=#ff6>* emphasis</color>", new Vector3(0f, 1.19f, 0.28f), 0.16f, 1.2f);
            bench.info = Text(z, "Info", "", new Vector3(0f, 1.1f, 0.28f), 0.15f, 1.2f);
            bench.folderText = Text(z, "Folder", "", new Vector3(0f, 1.0f, 0.28f), 0.13f, 1.2f);

            // Buttons on a slanted console at the front of the table
            var console = Group(z, "Bench Console", new Vector3(0f, 0.99f, -0.14f), Quaternion.Euler(35f, 0f, 0f));
            Box(console, "Plate", new Vector3(0f, 0f, 0.02f), new Vector3(1.08f, 0.2f, 0.02f), Mat("Dark"), false);
            var s = new Vector2(0.19f, 0.07f);
            void B(string label, float x, float y, UnityAction a)
            {
                var b = Button(console, label, new Vector3(x, y, 0f), s);
                UnityEventTools.AddPersistentListener(b.onPress, a);
                if (label.StartsWith("Loop")) bench.loopButton = b;
            }
            B("< Prev", -0.42f, 0.045f, bench.Previous);
            B("Next >", -0.21f, 0.045f, bench.Next);
            B("Loop", 0.0f, 0.045f, bench.ToggleLoop);
            B("Play L", 0.21f, 0.045f, bench.PlayLeft);
            B("Play R", 0.42f, 0.045f, bench.PlayRight);
            B("Amp -", -0.42f, -0.045f, bench.AmplitudeDown);
            B("Amp +", -0.21f, -0.045f, bench.AmplitudeUp);
            B("Reset", 0.0f, -0.045f, bench.ResetModulation);
            B("Freq -", 0.21f, -0.045f, bench.FrequencyDown);
            B("Freq +", 0.42f, -0.045f, bench.FrequencyUp);
        }

        static void BuildBlindTest(Transform z, MaterialWall wall)
        {
            Box(z, "Backboard", new Vector3(0f, 1.2f, 0.05f), new Vector3(1.3f, 1.0f, 0.04f), Mat("Board"), false);
            Text(z, "Title", "<b>4  BLIND TEST</b>", new Vector3(0f, 1.64f, 0.02f), 0.55f, 1.2f);

            var test = z.gameObject.AddComponent<BlindTest>();
            test.prompt = Text(z, "Prompt", "", new Vector3(0f, 1.535f, 0.02f), 0.2f, 1.2f);

            HapticSurface Panel(string label, float x)
            {
                var p = Box(z, "Panel " + label, new Vector3(x, 1.2f, 0f), new Vector3(0.3f, 0.36f, 0.03f), Mat("Neutral"), true);
                Text(z, "Label " + label, "<b>" + label + "</b>", new Vector3(x, 1.43f, 0.02f), 0.5f, 0.3f);
                var s = p.AddComponent<HapticSurface>();
                s.overrideFidelity = true;
                return s;
            }
            test.panelA = Panel("A", -0.25f);
            test.panelB = Panel("B", 0.25f);
            test.materials = wall.GetComponentsInChildren<HapticSurface>();
            if (test.materials.Length > 0) { test.panelA.CopyMaterialFrom(test.materials[0]); test.panelB.CopyMaterialFrom(test.materials[0]); }

            test.result = Text(z, "Result", "", new Vector3(0f, 0.98f, 0.02f), 0.18f, 1.2f);

            var console = Group(z, "Vote Console", new Vector3(0f, 0.86f, -0.12f), Quaternion.Euler(35f, 0f, 0f));
            Box(console, "Plate", new Vector3(0f, 0f, 0.02f), new Vector3(1.1f, 0.12f, 0.02f), Mat("Dark"), false);
            var s2 = new Vector2(0.24f, 0.08f);
            var a = Button(console, "A felt more real", new Vector3(-0.39f, 0, 0), s2);
            var b = Button(console, "B felt more real", new Vector3(-0.13f, 0, 0), s2);
            var n = Button(console, "Next round", new Vector3(0.13f, 0, 0), s2);
            var r = Button(console, "Reset score", new Vector3(0.39f, 0, 0), s2);
            UnityEventTools.AddPersistentListener(a.onPress, test.VoteA);
            UnityEventTools.AddPersistentListener(b.onPress, test.VoteB);
            UnityEventTools.AddPersistentListener(n.onPress, test.NewTrial);
            UnityEventTools.AddPersistentListener(r.onPress, test.ResetScore);
            test.score = Text(z, "Score", "", new Vector3(0f, 0.74f, -0.22f), 0.17f, 1.2f, Quaternion.Euler(35f, 0f, 0f));
        }

        // ==================================================================
        // Environment
        // ==================================================================
        static void BuildEnvironment()
        {
            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "Floor";
            floor.transform.localScale = new Vector3(0.8f, 1f, 0.8f);
            floor.GetComponent<Renderer>().sharedMaterial = Mat("Floor");
            floor.isStatic = true;

            var sunGo = new GameObject("Sun");
            var sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 1.1f;
            sun.color = new Color(1f, 0.96f, 0.9f);
            sun.shadows = LightShadows.Soft;
            sunGo.transform.rotation = Quaternion.Euler(55f, -35f, 0f);

            var fillGo = new GameObject("Fill");
            var fill = fillGo.AddComponent<Light>();
            fill.type = LightType.Directional;
            fill.intensity = 0.35f;
            fill.color = new Color(0.75f, 0.85f, 1f);
            fill.shadows = LightShadows.None;
            fillGo.transform.rotation = Quaternion.Euler(30f, 150f, 0f);

            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.45f, 0.5f, 0.6f);
            RenderSettings.ambientEquatorColor = new Color(0.32f, 0.32f, 0.36f);
            RenderSettings.ambientGroundColor = new Color(0.14f, 0.13f, 0.13f);
            RenderSettings.skybox = null;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = new Color(0.09f, 0.1f, 0.13f);
            RenderSettings.fogStartDistance = 4f;
            RenderSettings.fogEndDistance = 14f;
        }

        // ==================================================================
        // Helpers
        // ==================================================================
        static GameObject FindRigPrefab()
        {
            foreach (var guid in AssetDatabase.FindAssets("XR Origin (XR Rig) t:Prefab"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (Path.GetFileNameWithoutExtension(path) == "XR Origin (XR Rig)")
                    return AssetDatabase.LoadAssetAtPath<GameObject>(path);
            }
            return null;
        }

        static T Load<T>(string relative) where T : Object
        {
            var a = AssetDatabase.LoadAssetAtPath<T>(Root + "/" + relative);
            if (a == null) Debug.LogWarning("[HapticsLab] Missing asset: " + Root + "/" + relative);
            return a;
        }

        static Transform Group(Transform parent, string name, Vector3 localPos, Quaternion localRot)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = localRot;
            return go.transform;
        }

        static GameObject Box(Transform parent, string name, Vector3 localPos, Vector3 size, Material mat, bool collider)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = size;
            go.GetComponent<Renderer>().sharedMaterial = mat;
            if (!collider) Object.DestroyImmediate(go.GetComponent<Collider>());
            return go;
        }

        /// <param name="letterHeight">roughly the cap height in decimetres (0.2 = ~2 cm)</param>
        static TextMeshPro Text(Transform parent, string name, string text, Vector3 localPos, float letterHeight, float width, Quaternion? rot = null)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = rot ?? Quaternion.identity;
            var t = go.AddComponent<TextMeshPro>();
            t.font = s_font;
            t.text = text;
            t.fontSize = letterHeight;
            t.alignment = TextAlignmentOptions.Center;
            t.textWrappingMode = TextWrappingModes.Normal;
            t.color = new Color(0.93f, 0.94f, 0.96f);
            t.rectTransform.sizeDelta = new Vector2(width, Mathf.Max(0.05f, letterHeight * 0.6f));
            t.overflowMode = TextOverflowModes.Overflow;
            return t;
        }

        static LabButton Button(Transform parent, string label, Vector3 localPos, Vector2 size)
        {
            var root = new GameObject("Button " + label);
            root.transform.SetParent(parent, false);
            root.transform.localPosition = localPos;
            var col = root.AddComponent<BoxCollider>();
            col.size = new Vector3(size.x, size.y, 0.03f);
            root.AddComponent<XRSimpleInteractable>();
            var b = root.AddComponent<LabButton>();

            var cap = Box(root.transform, "Cap", new Vector3(0f, 0f, -0.01f), new Vector3(size.x, size.y, 0.02f), Mat("ButtonCap"), false);
            b.cap = cap.GetComponent<Renderer>();
            b.label = Text(root.transform, "Label", label, new Vector3(0f, 0f, -0.0215f), Mathf.Min(0.17f, size.y * 2.2f), size.x * 0.95f);
            // No hover clip: pointing at a button would vibrate while users test a station.
            b.pressClip = s_uiPress;
            b.pressSound = s_uiPressSound;
            return b;
        }

        static Material Mat(string key)
        {
            if (s_mats.TryGetValue(key, out var m) && m != null) return m;
            string path = $"{GeneratedPath}/{key}.mat";
            m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = CreateMaterial(key);
                AssetDatabase.CreateAsset(m, path);
            }
            s_mats[key] = m;
            return m;
        }

        static Material CreateMaterial(string key)
        {
            var lit = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var unlit = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
            Material M(Shader s, Color c, float metal = 0f, float smooth = 0.4f, string tex = null, float tiling = 1f)
            {
                var mat = new Material(s);
                mat.SetColor("_BaseColor", c);
                mat.SetColor("_Color", c);
                if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", metal);
                if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", smooth);
                if (mat.HasProperty("_Glossiness")) mat.SetFloat("_Glossiness", smooth);
                if (tex != null)
                {
                    var t = AssetDatabase.LoadAssetAtPath<Texture2D>($"{Root}/Textures/{tex}.png");
                    if (t != null)
                    {
                        mat.SetTexture("_BaseMap", t);
                        mat.SetTexture("_MainTex", t);
                        mat.SetTextureScale("_BaseMap", Vector2.one * tiling);
                        mat.SetTextureScale("_MainTex", Vector2.one * tiling);
                    }
                }
                return mat;
            }

            switch (key)
            {
                case "Wood": return M(lit, Color.white, 0f, 0.35f, "Wood");
                case "Metal": return M(lit, Color.white, 0.9f, 0.55f, "Metal");
                case "Glass": return M(lit, new Color(0.62f, 0.8f, 0.9f), 0.1f, 0.97f);
                case "Gravel": return M(lit, Color.white, 0f, 0.1f, "Gravel");
                case "Neutral": return M(lit, Color.white, 0.2f, 0.4f, "Neutral");
                case "Floor": return M(lit, Color.white, 0f, 0.2f, "Floor", 6f);
                case "Board": return M(lit, new Color(0.13f, 0.14f, 0.17f), 0f, 0.3f);
                case "Dark": return M(lit, new Color(0.08f, 0.085f, 0.1f), 0.2f, 0.4f);
                case "Graph": return M(unlit, new Color(0.04f, 0.045f, 0.06f));
                case "SafeBody": return M(lit, new Color(0.16f, 0.24f, 0.2f), 0.7f, 0.45f);
                case "SafeDoor": return M(lit, new Color(0.2f, 0.3f, 0.25f), 0.75f, 0.55f);
                case "Gold": return M(lit, new Color(1f, 0.78f, 0.3f), 1f, 0.75f);
                case "Accent": return M(lit, new Color(0.95f, 0.55f, 0.2f), 0.1f, 0.6f);
                case "White": return M(unlit, new Color(0.92f, 0.92f, 0.92f));
                case "ButtonCap": return M(lit, new Color(0.22f, 0.24f, 0.3f), 0.1f, 0.5f);
                case "Fingertip": return M(unlit, new Color(0.3f, 0.95f, 1f));
                case "Line":
                    return new Material(Shader.Find("Sprites/Default") ?? unlit);
                default: return M(lit, Color.magenta);
            }
        }

        static void AddSceneToBuild(string path)
        {
            var scenes = EditorBuildSettings.scenes.Where(s => s.path != path).ToList();
            scenes.Insert(0, new EditorBuildSettingsScene(path, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }
    }
}
