using DriverSim.Field;
using DriverSim.Game;
using DriverSim.Net;
using DriverSim.Robot;
using DriverSim.Util;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace DriverSim.Editor
{
    /// <summary>
    /// Generates the game scene from code so it is reproducible and reviewable: lighting, the AdvantageScope field,
    /// the driver camera and the game objects with their references wired. Run from the menu (Driver-Sim > Rebuild
    /// Scene) or in batch mode with <c>-executeMethod DriverSim.Editor.SceneBuilder.BuildScene</c>.
    /// Also applies the player settings the trainer needs (runs in the background, windowed).
    /// </summary>
    public static class SceneBuilder
    {
        public const string ScenePath = "Assets/Scenes/DriverSim.unity";
        private const string FieldModelPath = "Assets/ThirdParty/AdvantageScope/Field2026/Field2026.glb";
        private const string RobotMaterialPath = "Assets/DriverSim/Materials/RobotBase.mat";
        private const string SampleScenePath = "Assets/Scenes/SampleScene.unity";

        /// <summary>Builds and saves the scene, registers it as the only build scene, and sets player settings.</summary>
        [MenuItem("Driver-Sim/Rebuild Scene")]
        public static void BuildScene()
        {
            ApplyPlayerSettings();
            Material robotMaterial = LoadOrCreateRobotMaterial();
            GameObject fieldAsset = AssetDatabase.LoadAssetAtPath<GameObject>(FieldModelPath);
            if (fieldAsset == null)
            {
                throw new System.InvalidOperationException("field model not imported: " + FieldModelPath);
            }

            // empty scene with arena lighting
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            BuildLighting();

            // field
            GameObject field = (GameObject)PrefabUtility.InstantiatePrefab(fieldAsset);
            field.name = "Field2026";
            field.transform.position = Vector3.zero;
            GameObject arenaFloor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            arenaFloor.name = "ArenaFloor";
            Object.DestroyImmediate(arenaFloor.GetComponent<Collider>());
            arenaFloor.transform.position = new Vector3(0, -0.02f, 0);
            arenaFloor.transform.localScale = new Vector3(8, 1, 6);
            Material floorMaterial = new Material(robotMaterial);
            floorMaterial.SetColor("_BaseColor", new Color(0.12f, 0.12f, 0.13f));
            AssetDatabase.CreateAsset(floorMaterial, "Assets/DriverSim/Materials/ArenaFloor.mat");
            arenaFloor.GetComponent<Renderer>().sharedMaterial = floorMaterial;

            // game objects
            GameObject root = new GameObject("DriverSim");
            DriverSimLink link = root.AddComponent<DriverSimLink>();
            FieldSetup fieldSetup = root.AddComponent<FieldSetup>();
            fieldSetup.fieldModel = field.transform;
            GameObject robotObject = new GameObject("Robot");
            robotObject.transform.SetParent(root.transform, false);
            RobotModel robot = robotObject.AddComponent<RobotModel>();
            robot.link = link;
            robot.baseMaterial = robotMaterial;
            robot.labelMaterial = LoadOrCreateLabelMaterial();

            // driver camera
            GameObject cameraObject = new GameObject("DriverCamera");
            cameraObject.tag = "MainCamera";
            Camera cam = cameraObject.AddComponent<Camera>();
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 200f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.07f, 0.08f, 0.1f);
            cameraObject.AddComponent<AudioListener>();
            UniversalAdditionalCameraData cameraData = cameraObject.AddComponent<UniversalAdditionalCameraData>();
            cameraData.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            cameraData.antialiasingQuality = AntialiasingQuality.High;
            cameraData.renderPostProcessing = true;
            DriverStationCamera driverCamera = cameraObject.AddComponent<DriverStationCamera>();
            driverCamera.link = link;

            // overlay and controls
            DebugHud hud = root.AddComponent<DebugHud>();
            hud.link = link;
            hud.driverCamera = driverCamera;
            GameControls controls = root.AddComponent<GameControls>();
            controls.link = link;
            controls.driverCamera = driverCamera;
            controls.hud = hud;
            root.AddComponent<AutoScreenshot>();

            // save and register
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(SampleScenePath) != null)
            {
                AssetDatabase.DeleteAsset(SampleScenePath);
            }
            AssetDatabase.SaveAssets();
            Debug.Log("[Driver-Sim] scene built: " + ScenePath);
        }

        private static void BuildLighting()
        {
            // key light: high, angled like arena lighting, with soft shadows
            GameObject sun = new GameObject("KeyLight");
            Light key = sun.AddComponent<Light>();
            key.type = LightType.Directional;
            key.intensity = 1.6f;
            key.color = new Color(1f, 0.97f, 0.92f);
            key.shadows = LightShadows.Soft;
            sun.transform.rotation = Quaternion.Euler(62, -30, 0);

            // fill light from the other side, no shadows
            GameObject fillObject = new GameObject("FillLight");
            Light fill = fillObject.AddComponent<Light>();
            fill.type = LightType.Directional;
            fill.intensity = 0.45f;
            fill.color = new Color(0.85f, 0.9f, 1f);
            fill.shadows = LightShadows.None;
            fillObject.transform.rotation = Quaternion.Euler(50, 150, 0);

            RenderSettings.skybox = null;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.4f, 0.42f, 0.48f);
            RenderSettings.ambientEquatorColor = new Color(0.3f, 0.3f, 0.33f);
            RenderSettings.ambientGroundColor = new Color(0.14f, 0.14f, 0.14f);

            // post-processing: filmic tone mapping and a little extra contrast so the gray field doesn't look hazy
            VolumeProfile profile = ScriptableObject.CreateInstance<VolumeProfile>();
            Tonemapping tonemapping = profile.Add<Tonemapping>(true);
            tonemapping.mode.Override(TonemappingMode.ACES);
            ColorAdjustments colorAdjustments = profile.Add<ColorAdjustments>(true);
            colorAdjustments.postExposure.Override(0.35f);
            colorAdjustments.contrast.Override(18f);
            colorAdjustments.saturation.Override(12f);
            AssetDatabase.CreateAsset(profile, "Assets/DriverSim/Materials/ArenaPost.asset");
            GameObject volumeObject = new GameObject("PostProcessing");
            Volume volume = volumeObject.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.sharedProfile = profile;
        }

        private static Material LoadOrCreateRobotMaterial()
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(RobotMaterialPath);
            if (material != null)
            {
                return material;
            }
            material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            material.SetColor("_BaseColor", Color.gray);
            AssetDatabase.CreateAsset(material, RobotMaterialPath);
            return material;
        }

        private static Material LoadOrCreateLabelMaterial()
        {
            const string path = "Assets/DriverSim/Materials/RobotLabel.mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null)
            {
                return material;
            }
            material = new Material(Shader.Find("DriverSim/DepthTestedText"));
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        private static void ApplyPlayerSettings()
        {
            PlayerSettings.companyName = "FRC 5735";
            PlayerSettings.productName = "DriverSim";
            PlayerSettings.runInBackground = true; // keep running while the sim GUI has focus
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.defaultScreenWidth = 1600;
            PlayerSettings.defaultScreenHeight = 900;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.visibleInBackground = true;
        }
    }
}
