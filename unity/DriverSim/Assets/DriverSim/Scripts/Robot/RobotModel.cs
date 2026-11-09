using DriverSim.Field;
using DriverSim.Net;
using UnityEngine;

namespace DriverSim.Robot
{
    /// <summary>
    /// Stylized 5735 2026 robot built from primitives at startup and posed every frame from the bridge's robot state:
    /// chassis position and heading, turret yaw, hood angle, intake arm, intake roller spin and climber extension.
    /// Bumpers take the alliance color of the selected driver station.
    ///
    /// Local frame: +Z = robot front (robot x), +X = robot right (robot -y), +Y = up. Part sizes and positions are given
    /// in inches, from CompbotConstants where known (frame 29.5 x 25 in, 3.5 in bumpers, turret at x -4.245 / y 6.5 in)
    /// and game-feel estimates otherwise, kept under the 22.25 in trench clearance.
    /// </summary>
    public sealed class RobotModel : MonoBehaviour
    {
        private const float InchToMeter = 0.0254f; // meters per inch
        private const float FrameLengthIn = 29.5f; // inches, along robot x
        private const float FrameWidthIn = 25f; // inches, along robot y
        private const float BumperThicknessIn = 3.5f; // inches
        private const float BumperHeightIn = 5f; // inches
        private const float BumperBottomIn = 1.5f; // inches above the carpet
        private const float IntakeVisualOffsetDeg = 30f; // robot-code arm angle minus drawn angle (15 deg = on carpet)
        private const float RollerSpinDegPerVoltS = 120f; // drawn roller spin, degrees per volt per second
        private const float ClimberTravelIn = 16f; // inches the drawn inner tube slides at full extension
        private const float LabelOffsetIn = 0.05f; // inches the team number floats off the bumper face

        [Tooltip("Link to the bridge; robot state comes from here.")]
        [SerializeField] private DriverSimLink link;

        [Tooltip("URP Lit material to clone for every robot part.")]
        [SerializeField] private Material baseMaterial;

        [Tooltip("DriverSim/DepthTestedText material for the bumper numbers.")]
        [SerializeField] private Material labelMaterial;

        private Transform body; // root of the drawn robot, moved to the robot pose
        private Transform turret; // yaws with the turret angle
        private Transform hood; // pitches with the hood angle
        private Transform intakeArm; // pitches with the intake angle
        private Transform intakeRoller; // spins with roller voltage
        private Transform climberInner; // slides with climber extension
        private Renderer[] bumperRenderers; // recolored with the alliance
        private Material blueBumper; // bumper material, blue alliance
        private Material redBumper; // bumper material, red alliance
        private bool isShowingRed; // which bumper color is applied

        /// <summary>Sets the references the model needs; call before the scene starts.</summary>
        /// <param name="driverSimLink">Link to the bridge.</param>
        /// <param name="partMaterial">URP Lit material cloned for every part.</param>
        /// <param name="textMaterial">DriverSim/DepthTestedText material for the bumper numbers.</param>
        public void SetReferences(DriverSimLink driverSimLink, Material partMaterial, Material textMaterial)
        {
            link = driverSimLink;
            baseMaterial = partMaterial;
            labelMaterial = textMaterial;
        }

        /// <summary>Root transform of the drawn robot, at the robot pose.</summary>
        public Transform GetBody()
        {
            return body;
        }

        // ---- Unity messages

        private void Awake()
        {
            BuildRobot();
        }

        private void LateUpdate()
        {
            // hide the robot until the bridge sends state
            bool hasState = false;
            if (link != null)
            {
                hasState = link.HasRobotState();
            }
            body.gameObject.SetActive(hasState);
            if (!hasState)
            {
                return;
            }
            RobotState state = link.GetRobotState();

            // chassis pose
            body.position = FieldFrame.ConvertFieldToWorld(state.XFieldM, state.YFieldM, 0);
            body.rotation = FieldFrame.ConvertHeadingToRotation(state.HeadingFieldRad);

            // mechanisms (robot angles are CCW positive; Unity yaws clockwise)
            float turretDeg = (float)(state.TurretRobotRelRad * Mathf.Rad2Deg);
            turret.localRotation = Quaternion.Euler(0, -turretDeg, 0);
            hood.localRotation = Quaternion.Euler(-(float)state.HoodDeg, 0, 0);
            float armDeg = (float)state.IntakeArmDeg - IntakeVisualOffsetDeg;
            intakeArm.localRotation = Quaternion.Euler(-armDeg, 0, 0);
            float rollerStepDeg = (float)state.IntakeRollerVolts * RollerSpinDegPerVoltS * Time.deltaTime;
            intakeRoller.Rotate(Vector3.up, rollerStepDeg, Space.Self);
            float climberRiseIn = (float)state.ClimberExtension * ClimberTravelIn;
            climberInner.localPosition = new Vector3(0, 9 + climberRiseIn, 0) * InchToMeter;

            // bumper color follows the selected station's alliance
            MatchState match = link.GetMatchState();
            bool isRed = match.IsRed();
            if (isRed != isShowingRed)
            {
                ApplyBumperColor(isRed);
            }
        }

        // ---- private stateful helpers

        /// <summary>Builds every robot part under a fresh body transform.</summary>
        private void BuildRobot()
        {
            body = new GameObject("RobotBody").transform;
            body.SetParent(transform, false);

            // materials
            Material frameMat = MakeMaterial(new Color(0.18f, 0.18f, 0.2f), 0.6f);
            Material metalMat = MakeMaterial(new Color(0.62f, 0.64f, 0.68f), 0.8f);
            Material accentMat = MakeMaterial(new Color(1f, 0.78f, 0.1f), 0.3f);
            Material wheelMat = MakeMaterial(new Color(0.08f, 0.08f, 0.08f), 0.2f);
            Material rollerMat = MakeMaterial(new Color(0.15f, 0.55f, 0.2f), 0.3f);
            blueBumper = MakeMaterial(new Color(0.08f, 0.25f, 0.85f), 0.2f);
            redBumper = MakeMaterial(new Color(0.85f, 0.08f, 0.08f), 0.2f);

            // drivetrain frame and swerve wheels
            AddBox(body, "Frame", new Vector3(FrameWidthIn, 3, FrameLengthIn), new Vector3(0, 4.5f, 0), frameMat);
            float wheelXIn = FrameWidthIn / 2 - 2.5f;
            float wheelZIn = FrameLengthIn / 2 - 2.5f;
            AddWheel(new Vector3(wheelXIn, 2, wheelZIn), wheelMat);
            AddWheel(new Vector3(-wheelXIn, 2, wheelZIn), wheelMat);
            AddWheel(new Vector3(wheelXIn, 2, -wheelZIn), wheelMat);
            AddWheel(new Vector3(-wheelXIn, 2, -wheelZIn), wheelMat);

            // bumpers with team number
            BuildBumpers();

            // hopper / superstructure, with a marker on the front
            AddBox(body, "Hopper", new Vector3(21, 9, 22), new Vector3(0, 10.5f, -1), metalMat);
            Vector3 markerPositionIn = new Vector3(0, 6.5f, FrameLengthIn / 2 - 0.5f);
            AddBox(body, "FrontMarker", new Vector3(8, 1, 1), markerPositionIn, accentMat);

            // turret, shooter and hood (turret center from CompbotConstants: x -4.245 in, y 6.5 in)
            turret = AddPivot(body, "Turret", new Vector3(-6.5f, 15, -4.245f));
            AddCylinder(turret, "TurretRing", new Vector3(11, 1.25f, 11), new Vector3(0, 1.25f, 0), frameMat);
            AddBox(turret, "Shooter", new Vector3(6, 4, 9), new Vector3(0, 4.5f, 1), metalMat);
            AddBox(turret, "Muzzle", new Vector3(4, 2.5f, 1), new Vector3(0, 5, 5.5f), accentMat);
            hood = AddPivot(turret, "Hood", new Vector3(0, 6.5f, -3));
            AddBox(hood, "HoodPlate", new Vector3(6.5f, 0.5f, 8), new Vector3(0, 0, 4), accentMat);

            // intake: arm hinged at the frame's front edge, roller across the front
            intakeArm = AddPivot(body, "IntakeArm", new Vector3(0, 6, FrameLengthIn / 2));
            AddBox(intakeArm, "ArmLeft", new Vector3(1, 2, 12), new Vector3(11.5f, 0, 6), frameMat);
            AddBox(intakeArm, "ArmRight", new Vector3(1, 2, 12), new Vector3(-11.5f, 0, 6), frameMat);
            intakeRoller = AddPivot(intakeArm, "Roller", new Vector3(0, 0, 12));
            intakeRoller.localRotation = Quaternion.Euler(0, 0, 90);
            AddCylinder(intakeRoller, "RollerTube", new Vector3(2.5f, 11.5f, 2.5f), Vector3.zero, rollerMat);
            AddBox(intakeRoller, "RollerStripe", new Vector3(2.7f, 22, 0.4f), Vector3.zero, accentMat);

            // climber: outer tube at the back, inner tube slides up
            Transform climber = AddPivot(body, "Climber", new Vector3(0, 6, -FrameLengthIn / 2 + 2));
            AddBox(climber, "Outer", new Vector3(2, 14, 2), new Vector3(0, 7, 0), metalMat);
            climberInner = AddPivot(climber, "Inner", Vector3.zero);
            AddBox(climberInner, "InnerTube", new Vector3(1.5f, 12, 1.5f), Vector3.zero, accentMat);
            AddBox(climberInner, "Hook", new Vector3(4, 1, 2), new Vector3(0, 6, 0.5f), frameMat);
        }

        /// <summary>Builds the four bumpers and the team number on each.</summary>
        private void BuildBumpers()
        {
            // bumper geometry, inches
            float outerLengthIn = FrameLengthIn + 2 * BumperThicknessIn;
            float outerWidthIn = FrameWidthIn + 2 * BumperThicknessIn;
            float centerYIn = BumperBottomIn + BumperHeightIn / 2;
            float sideXIn = FrameWidthIn / 2 + BumperThicknessIn / 2;
            float endZIn = FrameLengthIn / 2 + BumperThicknessIn / 2;
            Vector3 endSizeIn = new Vector3(outerWidthIn, BumperHeightIn, BumperThicknessIn);
            Vector3 sideSizeIn = new Vector3(BumperThicknessIn, BumperHeightIn, FrameLengthIn);

            // bumpers
            bumperRenderers = new Renderer[4];
            bumperRenderers[0] = AddBox(body, "BumperFront", endSizeIn, new Vector3(0, centerYIn, endZIn), blueBumper);
            bumperRenderers[1] = AddBox(body, "BumperBack", endSizeIn, new Vector3(0, centerYIn, -endZIn), blueBumper);
            bumperRenderers[2] = AddBox(body, "BumperLeft", sideSizeIn, new Vector3(-sideXIn, centerYIn, 0), blueBumper);
            bumperRenderers[3] = AddBox(body, "BumperRight", sideSizeIn, new Vector3(sideXIn, centerYIn, 0), blueBumper);
            isShowingRed = false;

            // team number on all four sides, just outside the bumper faces
            float frontZIn = outerLengthIn / 2 + LabelOffsetIn;
            float sideFaceXIn = outerWidthIn / 2 + LabelOffsetIn;
            AddLabel(new Vector3(0, centerYIn, frontZIn), Quaternion.Euler(0, 180, 0));
            AddLabel(new Vector3(0, centerYIn, -frontZIn), Quaternion.identity);
            AddLabel(new Vector3(-sideFaceXIn, centerYIn, 0), Quaternion.Euler(0, 90, 0));
            AddLabel(new Vector3(sideFaceXIn, centerYIn, 0), Quaternion.Euler(0, -90, 0));
        }

        /// <summary>Adds a "5735" label to the body.</summary>
        /// <param name="positionIn">Local position, inches.</param>
        /// <param name="localRotation">Local rotation; the text reads correctly looking along its +Z.</param>
        private void AddLabel(Vector3 positionIn, Quaternion localRotation)
        {
            GameObject label = new GameObject("TeamNumber");
            label.transform.SetParent(body, false);
            label.transform.localPosition = positionIn * InchToMeter;
            label.transform.localRotation = localRotation;

            // text with a depth-tested material using the font's glyph atlas
            TextMesh text = label.AddComponent<TextMesh>();
            text.text = "5735";
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            Material textMaterial = new Material(labelMaterial);
            textMaterial.mainTexture = text.font.material.mainTexture;
            text.GetComponent<MeshRenderer>().sharedMaterial = textMaterial;
            text.fontSize = 64;
            text.characterSize = 0.0125f;
            text.anchor = TextAnchor.MiddleCenter;
            text.alignment = TextAlignment.Center;
            text.color = Color.white;
        }

        /// <summary>Puts the blue or red bumper material on every bumper.</summary>
        private void ApplyBumperColor(bool isRed)
        {
            Material bumper = blueBumper;
            if (isRed)
            {
                bumper = redBumper;
            }
            foreach (Renderer bumperRenderer in bumperRenderers)
            {
                bumperRenderer.sharedMaterial = bumper;
            }
            isShowingRed = isRed;
        }

        /// <summary>Adds a swerve wheel (a short cylinder on its side) to the body.</summary>
        /// <param name="positionIn">Local position, inches.</param>
        /// <param name="material">Wheel material.</param>
        private void AddWheel(Vector3 positionIn, Material material)
        {
            Renderer wheel = AddCylinder(body, "Wheel", new Vector3(4, 0.75f, 4), positionIn, material);
            wheel.transform.localRotation = Quaternion.Euler(0, 0, 90);
        }

        /// <summary>Makes a material for one robot color from the base material.</summary>
        private Material MakeMaterial(Color color, float smoothness)
        {
            Material mat = new Material(baseMaterial);
            mat.SetColor("_BaseColor", color);
            mat.SetFloat("_Smoothness", smoothness);
            return mat;
        }

        // ---- low-level helpers (static; each creates and returns a new scene object)

        /// <summary>Adds an empty transform to rotate or slide a group of parts.</summary>
        /// <param name="parent">Parent transform.</param>
        /// <param name="pivotName">Object name.</param>
        /// <param name="positionIn">Local position, inches.</param>
        private static Transform AddPivot(Transform parent, string pivotName, Vector3 positionIn)
        {
            Transform pivot = new GameObject(pivotName).transform;
            pivot.SetParent(parent, false);
            pivot.localPosition = positionIn * InchToMeter;
            return pivot;
        }

        /// <summary>Adds a box part.</summary>
        /// <param name="parent">Parent transform.</param>
        /// <param name="partName">Object name.</param>
        /// <param name="sizeIn">Box size, inches.</param>
        /// <param name="positionIn">Local position of its center, inches.</param>
        /// <param name="mat">Material.</param>
        private static Renderer AddBox(Transform parent, string partName, Vector3 sizeIn, Vector3 positionIn,
            Material mat)
        {
            return AddPrimitive(PrimitiveType.Cube, parent, partName, sizeIn, positionIn, mat);
        }

        /// <summary>Adds a cylinder part (axis along local Y; Y size is half the height).</summary>
        /// <param name="parent">Parent transform.</param>
        /// <param name="partName">Object name.</param>
        /// <param name="scaleIn">Diameter, half-height, diameter, inches.</param>
        /// <param name="positionIn">Local position of its center, inches.</param>
        /// <param name="mat">Material.</param>
        private static Renderer AddCylinder(Transform parent, string partName, Vector3 scaleIn, Vector3 positionIn,
            Material mat)
        {
            return AddPrimitive(PrimitiveType.Cylinder, parent, partName, scaleIn, positionIn, mat);
        }

        /// <summary>Adds a primitive part without a collider (collisions use separate, simpler shapes).</summary>
        private static Renderer AddPrimitive(PrimitiveType type, Transform parent, string partName, Vector3 scaleIn,
            Vector3 positionIn, Material mat)
        {
            GameObject part = GameObject.CreatePrimitive(type);
            part.name = partName;
            Collider partCollider = part.GetComponent<Collider>();
            Destroy(partCollider);
            part.transform.SetParent(parent, false);
            part.transform.localPosition = positionIn * InchToMeter;
            part.transform.localScale = scaleIn * InchToMeter;
            Renderer partRenderer = part.GetComponent<Renderer>();
            partRenderer.sharedMaterial = mat;
            return partRenderer;
        }
    }
}
