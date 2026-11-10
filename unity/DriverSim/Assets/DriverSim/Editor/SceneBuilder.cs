using System.Collections.Generic;
using DriverSim.Field;
using DriverSim.Game;
using DriverSim.Net;
using DriverSim.Robot;
using DriverSim.Util;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UIElements;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace DriverSim.Editor
{
    /// <summary>
    /// Generates the game scene from code so it is reproducible and reviewable: lighting, the AdvantageScope field,
    /// the driver camera and the game objects with their references wired. Run from the menu (Fieldrunner > Rebuild
    /// Scene) or in batch mode with <c>-executeMethod DriverSim.Editor.SceneBuilder.BuildScene</c>.
    /// Also applies the player settings the trainer needs (runs in the background, windowed).
    /// </summary>
    public static class SceneBuilder
    {
        public const string ScenePath = "Assets/Scenes/DriverSim.unity";
        private const string FieldModelPath = "Assets/ThirdParty/AdvantageScope/Field2026/Field2026.glb";
        private const string RobotMaterialPath = "Assets/DriverSim/Materials/RobotBase.mat";
        private const string BumpPartNumber = "GE-26100"; // field model nodes of the four bumps start with this
        private const float MinShadowCasterSizeM = 0.25f; // field parts smaller than this (largest side) cast no shadow
        private const string MenuLayoutPath = "Assets/DriverSim/UI/DriverSim.uxml"; // menu screens
        private const string PanelSettingsPath = "Assets/DriverSim/UI/PanelSettings.asset"; // menu scaling
        private const string SampleScenePath = "Assets/Scenes/SampleScene.unity";

        /// <summary>Builds and saves the scene, registers it as the only build scene, and sets player
        /// settings.</summary>
        [MenuItem("Fieldrunner/Rebuild Scene")]
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
            Collider floorCollider = arenaFloor.GetComponent<Collider>();
            Object.DestroyImmediate(floorCollider);
            arenaFloor.transform.position = new Vector3(0, -0.02f, 0);
            arenaFloor.transform.localScale = new Vector3(8, 1, 6);
            Material floorMaterial = new Material(robotMaterial);
            floorMaterial.SetColor("_BaseColor", new Color(0.12f, 0.12f, 0.13f));
            AssetDatabase.CreateAsset(floorMaterial, "Assets/DriverSim/Materials/ArenaFloor.mat");
            arenaFloor.GetComponent<Renderer>().sharedMaterial = floorMaterial;

            // rendering cost: small field parts don't cast shadows
            int shadowlessCount = TurnOffSmallShadowCasters(field.transform);
            Debug.Log("[Driver-Sim] field: " + shadowlessCount + " small parts without shadows");

            // collision: obstacle colliders from the layout table, and bump surfaces from the field model
            BuildObstacles();
            int bumpColliderCount = AddBumpColliders(field.transform);
            Debug.Log("[Driver-Sim] added " + bumpColliderCount + " bump mesh colliders");

            // game objects
            GameObject root = new GameObject("DriverSim");
            DriverSimLink link = root.AddComponent<DriverSimLink>();
            FieldSetup fieldSetup = root.AddComponent<FieldSetup>();
            fieldSetup.SetFieldModel(field.transform);
            GameObject robotObject = new GameObject("Robot");
            robotObject.transform.SetParent(root.transform, false);
            RobotModel robot = robotObject.AddComponent<RobotModel>();
            Material labelMaterial = LoadOrCreateLabelMaterial();
            RobotCollision robotCollision = robotObject.AddComponent<RobotCollision>();
            robotCollision.SetReferences(link);
            robot.SetReferences(link, robotMaterial, labelMaterial, robotCollision);
            CollisionSelfTest collisionSelfTest = robotObject.AddComponent<CollisionSelfTest>();
            collisionSelfTest.SetReferences(robotCollision);

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
            driverCamera.SetReferences(link, fieldSetup);

            // overlay and controls
            FrameRateLog frameRateLog = root.AddComponent<FrameRateLog>();
            RenderScaleCap renderScaleCap = root.AddComponent<RenderScaleCap>();
            DebugHud hud = root.AddComponent<DebugHud>();
            hud.SetReferences(link, driverCamera, robotCollision, robot, frameRateLog, renderScaleCap);
            ColliderDebugView colliderView = root.AddComponent<ColliderDebugView>();
            Material solidDebugMaterial = LoadOrCreateDebugMaterial("ColliderSolid", new Color(0f, 0.9f, 1f, 0.6f));
            Material lowDebugMaterial = LoadOrCreateDebugMaterial("ColliderLowCeiling", new Color(1f, 0.9f, 0f, 0.6f));
            colliderView.SetReferences(solidDebugMaterial, lowDebugMaterial);
            root.AddComponent<AutoScreenshot>();

            // input, menus and game flow
            ControllerInput controllerInput = root.AddComponent<ControllerInput>();
            InputActionAsset controlsAsset = DriverControlsBuilder.BuildAsset();
            controllerInput.SetActionsAsset(controlsAsset);
            GameOptions gameOptions = root.AddComponent<GameOptions>();
            gameOptions.SetReferences(driverCamera, hud, colliderView);
            MenuView menuView = BuildMenuUi(controllerInput, gameOptions);
            GameFlow flow = root.AddComponent<GameFlow>();
            flow.SetReferences(link, controllerInput, menuView, hud);
            JoystickSender joystickSender = root.AddComponent<JoystickSender>();
            joystickSender.SetReferences(link, controllerInput, flow);
            GameControls controls = root.AddComponent<GameControls>();
            controls.SetReferences(flow, controllerInput, gameOptions);
            BuildEventSystem();

            // save and register
            UnityEngine.SceneManagement.Scene scene = EditorSceneManager.GetActiveScene();
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(SampleScenePath) != null)
            {
                AssetDatabase.DeleteAsset(SampleScenePath);
            }
            AssetDatabase.SaveAssets();
            Debug.Log("[Driver-Sim] scene built: " + ScenePath);
        }

        /// <summary>
        /// Creates the UI object: its UIDocument (menu layout and panel settings) and the <see cref="MenuView"/>.
        /// </summary>
        /// <param name="controllerInput">Controller input, for the Controls screen.</param>
        /// <param name="gameOptions">Settings, for the Options screen.</param>
        private static MenuView BuildMenuUi(ControllerInput controllerInput, GameOptions gameOptions)
        {
            VisualTreeAsset layout = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(MenuLayoutPath);
            if (layout == null)
            {
                throw new System.InvalidOperationException("menu layout not imported: " + MenuLayoutPath);
            }
            PanelSettings panelSettings = LoadOrCreatePanelSettings();
            GameObject uiObject = new GameObject("MenuUi");
            UIDocument document = uiObject.AddComponent<UIDocument>();
            document.panelSettings = panelSettings;
            document.visualTreeAsset = layout;
            MenuView menuView = uiObject.AddComponent<MenuView>();
            menuView.SetReferences(document, controllerInput, gameOptions);
            return menuView;
        }

        /// <summary>
        /// Loads the menu's panel settings (scale with screen size from a 1920x1080 reference), creating them on first
        /// use.
        /// </summary>
        private static PanelSettings LoadOrCreatePanelSettings()
        {
            PanelSettings settings = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath);
            if (settings == null)
            {
                settings = ScriptableObject.CreateInstance<PanelSettings>();
                AssetDatabase.CreateAsset(settings, PanelSettingsPath);
            }
            settings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            settings.referenceResolution = new Vector2Int(1920, 1080);
            settings.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            settings.match = 0.5f;
            settings.sortingOrder = 100;
            string[] themeGuids = AssetDatabase.FindAssets("t:ThemeStyleSheet");
            if (settings.themeStyleSheet == null && themeGuids.Length > 0)
            {
                string themePath = AssetDatabase.GUIDToAssetPath(themeGuids[0]);
                settings.themeStyleSheet = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(themePath);
            }
            EditorUtility.SetDirty(settings);
            return settings;
        }

        /// <summary>
        /// Adds the event system with the Input System UI module, so menus take mouse, keyboard and controller.
        /// </summary>
        private static void BuildEventSystem()
        {
            GameObject eventSystemObject = new GameObject("EventSystem");
            eventSystemObject.AddComponent<EventSystem>();
            InputSystemUIInputModule module = eventSystemObject.AddComponent<InputSystemUIInputModule>();
            module.AssignDefaultActions();
        }

        /// <summary>
        /// Creates one box collider per obstacle in <see cref="FieldObstacleLayout"/> under a FieldObstacles root, each
        /// with a <see cref="FieldObstacle"/> carrying its kind. No renderers: the colliders are invisible.
        /// </summary>
        private static void BuildObstacles()
        {
            GameObject obstaclesRoot = new GameObject("FieldObstacles");
            List<ObstacleSpec> specs = FieldObstacleLayout.GetAll();
            foreach (ObstacleSpec spec in specs)
            {
                GameObject obstacleObject = new GameObject(spec.Name);
                obstacleObject.transform.SetParent(obstaclesRoot.transform, false);

                // box from the carpet up, positioned from the field-frame rectangle
                float heightM = FieldObstacleLayout.ColliderHeightM;
                obstacleObject.transform.position = FieldFrame.ConvertFieldToWorld(spec.GetCenterXFieldM(),
                    spec.GetCenterYFieldM(), heightM / 2);
                BoxCollider boxCollider = obstacleObject.AddComponent<BoxCollider>();
                boxCollider.size = new Vector3(spec.GetSizeXFieldM(), heightM, spec.GetSizeYFieldM());
                FieldObstacle obstacle = obstacleObject.AddComponent<FieldObstacle>();
                obstacle.SetKind(spec.Kind);
            }
        }

        /// <summary>
        /// Adds a mesh collider and a <see cref="BumpSurface"/> marker to every mesh under the field model's bump
        /// nodes, so the drawn robot can ride over them. Done in edit mode so Unity cooks the colliders at build time.
        /// </summary>
        /// <param name="node">Field model node to search, with its children.</param>
        /// <returns>Number of mesh colliders added.</returns>
        private static int AddBumpColliders(Transform node)
        {
            int added = 0;
            if (node.name.StartsWith(BumpPartNumber))
            {
                MeshFilter[] filters = node.GetComponentsInChildren<MeshFilter>();
                foreach (MeshFilter filter in filters)
                {
                    MeshCollider meshCollider = filter.gameObject.AddComponent<MeshCollider>();
                    meshCollider.sharedMesh = filter.sharedMesh;
                    filter.gameObject.AddComponent<BumpSurface>();
                    added++;
                }
                return added;
            }
            for (int i = 0; i < node.childCount; i++)
            {
                added += AddBumpColliders(node.GetChild(i));
            }
            return added;
        }

        /// <summary>Loads or creates a translucent unlit material for the collider overlay.</summary>
        /// <param name="materialName">Asset name without extension.</param>
        /// <param name="color">Base color; alpha is the opacity.</param>
        private static Material LoadOrCreateDebugMaterial(string materialName, Color color)
        {
            string path = "Assets/DriverSim/Materials/" + materialName + ".mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                AssetDatabase.CreateAsset(material, path);
            }

            // transparent surface with alpha blending, drawn without writing depth
            material.SetFloat("_Surface", 1);
            material.SetFloat("_Blend", 0);
            material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_ZWrite", 0);
            material.SetOverrideTag("RenderType", "Transparent");
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.renderQueue = (int)RenderQueue.Transparent;
            material.SetColor("_BaseColor", color);
            EditorUtility.SetDirty(material);
            return material;
        }

        /// <summary>Adds arena lighting, ambient light and the post-processing volume to the open scene.</summary>
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

        /// <summary>Loads the robot's base URP Lit material, creating it on first use.</summary>
        private static Material LoadOrCreateRobotMaterial()
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(RobotMaterialPath);
            if (material != null)
            {
                return material;
            }
            Shader lit = Shader.Find("Universal Render Pipeline/Lit");
            material = new Material(lit);
            material.SetColor("_BaseColor", Color.gray);
            AssetDatabase.CreateAsset(material, RobotMaterialPath);
            return material;
        }

        /// <summary>Loads the bumper-number material, creating it on first use.</summary>
        private static Material LoadOrCreateLabelMaterial()
        {
            const string path = "Assets/DriverSim/Materials/RobotLabel.mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null)
            {
                return material;
            }
            Shader text = Shader.Find("DriverSim/DepthTestedText");
            material = new Material(text);
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        /// <summary>
        /// Stops field parts smaller than <see cref="MinShadowCasterSizeM"/> (largest side) from casting shadows.
        /// </summary>
        /// <param name="field">Root of the field model.</param>
        /// <returns>Number of renderers changed.</returns>
        private static int TurnOffSmallShadowCasters(Transform field)
        {
            int changed = 0;
            Renderer[] renderers = field.GetComponentsInChildren<Renderer>(true);
            foreach (Renderer partRenderer in renderers)
            {
                Vector3 sizeM = partRenderer.bounds.size;
                float largestSideM = Mathf.Max(sizeM.x, sizeM.y, sizeM.z);
                if (largestSideM < MinShadowCasterSizeM)
                {
                    partRenderer.shadowCastingMode = ShadowCastingMode.Off;
                    changed++;
                }
            }
            return changed;
        }

        /// <summary>Sets the player settings the trainer needs.</summary>
        private static void ApplyPlayerSettings()
        {
            PlayerSettings.companyName = "FRC 5735";
            PlayerSettings.productName = "Fieldrunner";
            PlayerSettings.runInBackground = true; // keep running while the sim GUI has focus
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.defaultScreenWidth = 1600;
            PlayerSettings.defaultScreenHeight = 900;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.visibleInBackground = true;
            PlayerSettings.enableFrameTimingStats = true; // FrameRateLog reports cpu and gpu time per frame

            // Linear color space: physically based lighting for URP
            if (PlayerSettings.colorSpace != ColorSpace.Linear)
            {
                PlayerSettings.colorSpace = ColorSpace.Linear;
            }
        }
    }
}
