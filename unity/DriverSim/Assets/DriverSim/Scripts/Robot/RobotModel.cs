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
    /// Local frame: +Z = robot front (robot x), +X = robot right (robot -y), +Y = up. Dimensions are inches, from
    /// CompbotConstants where known (frame 29.5 x 25 in, 3.5 in bumpers, turret at x -4.245 / y 6.5 in) and
    /// game-feel estimates otherwise, kept under the 22.25 in trench clearance.
    /// </summary>
    public sealed class RobotModel : MonoBehaviour
    {
        private const float InchToMeter = 0.0254f;
        private const float FrameLengthIn = 29.5f; // along robot x
        private const float FrameWidthIn = 25f; // along robot y
        private const float BumperThicknessIn = 3.5f;
        private const float BumperHeightIn = 5f;
        private const float BumperBottomIn = 1.5f;
        private const float IntakeVisualOffsetDeg = 30f; // robot-code arm angle minus drawn angle (15 deg = on carpet)
        private const float RollerSpinDegPerVoltS = 120f; // drawn roller spin per volt
        private const float ClimberTravelIn = 16f; // drawn inner-tube travel at full extension

        [Tooltip("Link to the bridge; robot state comes from here.")]
        public DriverSimLink link;

        [Tooltip("URP Lit material to clone for every robot part.")]
        public Material baseMaterial;

        [Tooltip("DriverSim/DepthTestedText material for the bumper numbers.")]
        public Material labelMaterial;

        private Transform body; // root of the drawn robot, moved to the robot pose
        private Transform turret; // yaws with the turret angle
        private Transform hood; // pitches with the hood angle
        private Transform intakeArm; // pitches with the intake angle
        private Transform intakeRoller; // spins with roller voltage
        private Transform climberInner; // slides with climber extension
        private Renderer[] bumperRenderers; // recolored with the alliance
        private TextMesh[] bumperLabels; // team number on the bumpers
        private Material blueBumper; // bumper material, blue alliance
        private Material redBumper; // bumper material, red alliance
        private bool isShowingRed; // which bumper color is applied

        /// <summary>Root transform of the drawn robot (at the robot pose).</summary>
        public Transform Body
        {
            get { return body; }
        }

        private void Awake()
        {
            BuildRobot();
        }

        private void LateUpdate()
        {
            bool hasState = link != null && link.HasRobotState;
            body.gameObject.SetActive(hasState);
            if (!hasState)
            {
                return;
            }
            RobotState state = link.Robot;

            // chassis pose
            body.position = FieldFrame.ToWorld(state.XM, state.YM, 0);
            body.rotation = FieldFrame.HeadingToRotation(state.HeadingRad);

            // mechanisms
            float turretDeg = (float)(state.TurretRobotRelRad * Mathf.Rad2Deg);
            turret.localRotation = Quaternion.Euler(0, -turretDeg, 0); // CCW-positive robot angle, Unity yaws CW
            hood.localRotation = Quaternion.Euler(-(float)state.HoodDeg, 0, 0);
            float armDeg = (float)state.IntakeArmDeg - IntakeVisualOffsetDeg;
            intakeArm.localRotation = Quaternion.Euler(-armDeg, 0, 0);
            float rollerStepDeg = (float)state.IntakeRollerV * RollerSpinDegPerVoltS * Time.deltaTime;
            intakeRoller.Rotate(Vector3.up, rollerStepDeg, Space.Self);
            float climberY = (float)state.ClimberExtension * ClimberTravelIn;
            climberInner.localPosition = Inches(0, 9 + climberY, 0);

            // bumper color follows the selected station's alliance
            bool isRed = link.Match.IsRed;
            if (isRed != isShowingRed)
            {
                ApplyBumperColor(isRed);
            }
        }

        // ---- construction

        private void BuildRobot()
        {
            body = new GameObject("RobotBody").transform;
            body.SetParent(transform, false);

            Material frameMat = MakeMaterial(new Color(0.18f, 0.18f, 0.2f), 0.6f);
            Material metalMat = MakeMaterial(new Color(0.62f, 0.64f, 0.68f), 0.8f);
            Material accentMat = MakeMaterial(new Color(1f, 0.78f, 0.1f), 0.3f);
            Material wheelMat = MakeMaterial(new Color(0.08f, 0.08f, 0.08f), 0.2f);
            Material rollerMat = MakeMaterial(new Color(0.15f, 0.55f, 0.2f), 0.3f);
            blueBumper = MakeMaterial(new Color(0.08f, 0.25f, 0.85f), 0.2f);
            redBumper = MakeMaterial(new Color(0.85f, 0.08f, 0.08f), 0.2f);

            // drivetrain frame and swerve wheels
            AddBox(body, "Frame", Inches(FrameWidthIn, 3, FrameLengthIn), Inches(0, 4.5f, 0), frameMat);
            float wheelX = FrameWidthIn / 2 - 2.5f;
            float wheelZ = FrameLengthIn / 2 - 2.5f;
            AddWheel(body, Inches(wheelX, 2, wheelZ), wheelMat);
            AddWheel(body, Inches(-wheelX, 2, wheelZ), wheelMat);
            AddWheel(body, Inches(wheelX, 2, -wheelZ), wheelMat);
            AddWheel(body, Inches(-wheelX, 2, -wheelZ), wheelMat);

            // bumpers with team number
            BuildBumpers();

            // hopper / superstructure
            AddBox(body, "Hopper", Inches(21, 9, 22), Inches(0, 10.5f, -1), metalMat);
            AddBox(body, "FrontMarker", Inches(8, 1, 1), Inches(0, 6.5f, FrameLengthIn / 2 - 0.5f), accentMat);

            // turret, shooter and hood (turret center from CompbotConstants: x -4.245 in, y 6.5 in)
            turret = new GameObject("Turret").transform;
            turret.SetParent(body, false);
            turret.localPosition = Inches(-6.5f, 15, -4.245f);
            AddCylinder(turret, "TurretRing", Inches(11, 1.25f, 11), Inches(0, 1.25f, 0), frameMat); // y scale = half height
            AddBox(turret, "Shooter", Inches(6, 4, 9), Inches(0, 4.5f, 1), metalMat);
            AddBox(turret, "Muzzle", Inches(4, 2.5f, 1), Inches(0, 5, 5.5f), accentMat);
            hood = new GameObject("Hood").transform;
            hood.SetParent(turret, false);
            hood.localPosition = Inches(0, 6.5f, -3);
            AddBox(hood, "HoodPlate", Inches(6.5f, 0.5f, 8), Inches(0, 0, 4), accentMat);

            // intake: arm hinged at the frame's front edge, roller across the front
            intakeArm = new GameObject("IntakeArm").transform;
            intakeArm.SetParent(body, false);
            intakeArm.localPosition = Inches(0, 6, FrameLengthIn / 2);
            AddBox(intakeArm, "ArmLeft", Inches(1, 2, 12), Inches(11.5f, 0, 6), frameMat);
            AddBox(intakeArm, "ArmRight", Inches(1, 2, 12), Inches(-11.5f, 0, 6), frameMat);
            intakeRoller = new GameObject("Roller").transform;
            intakeRoller.SetParent(intakeArm, false);
            intakeRoller.localPosition = Inches(0, 0, 12);
            intakeRoller.localRotation = Quaternion.Euler(0, 0, 90);
            AddCylinder(intakeRoller, "RollerTube", Inches(2.5f, 11.5f, 2.5f), Vector3.zero, rollerMat);
            AddBox(intakeRoller, "RollerStripe", Inches(2.7f, 22, 0.4f), Vector3.zero, accentMat);

            // climber: outer tube at the back, inner tube slides up
            Transform climber = new GameObject("Climber").transform;
            climber.SetParent(body, false);
            climber.localPosition = Inches(0, 6, -FrameLengthIn / 2 + 2);
            AddBox(climber, "Outer", Inches(2, 14, 2), Inches(0, 7, 0), metalMat);
            climberInner = new GameObject("Inner").transform;
            climberInner.SetParent(climber, false);
            AddBox(climberInner, "InnerTube", Inches(1.5f, 12, 1.5f), Vector3.zero, accentMat);
            AddBox(climberInner, "Hook", Inches(4, 1, 2), Inches(0, 6, 0.5f), frameMat);
        }

        private void BuildBumpers()
        {
            float outerLength = FrameLengthIn + 2 * BumperThicknessIn;
            float outerWidth = FrameWidthIn + 2 * BumperThicknessIn;
            float y = BumperBottomIn + BumperHeightIn / 2;
            float sideX = FrameWidthIn / 2 + BumperThicknessIn / 2;
            float endZ = FrameLengthIn / 2 + BumperThicknessIn / 2;

            bumperRenderers = new Renderer[4];
            bumperRenderers[0] = AddBox(body, "BumperFront", Inches(outerWidth, BumperHeightIn, BumperThicknessIn),
                Inches(0, y, endZ), blueBumper);
            bumperRenderers[1] = AddBox(body, "BumperBack", Inches(outerWidth, BumperHeightIn, BumperThicknessIn),
                Inches(0, y, -endZ), blueBumper);
            bumperRenderers[2] = AddBox(body, "BumperLeft", Inches(BumperThicknessIn, BumperHeightIn, FrameLengthIn),
                Inches(-sideX, y, 0), blueBumper);
            bumperRenderers[3] = AddBox(body, "BumperRight", Inches(BumperThicknessIn, BumperHeightIn, FrameLengthIn),
                Inches(sideX, y, 0), blueBumper);

            // team number on all four sides, just outside the bumper faces
            bumperLabels = new TextMesh[4];
            bumperLabels[0] = AddLabel(Inches(0, y, outerLength / 2 + 0.05f), Quaternion.Euler(0, 180, 0));
            bumperLabels[1] = AddLabel(Inches(0, y, -outerLength / 2 - 0.05f), Quaternion.identity);
            bumperLabels[2] = AddLabel(Inches(-outerWidth / 2 - 0.05f, y, 0), Quaternion.Euler(0, 90, 0));
            bumperLabels[3] = AddLabel(Inches(outerWidth / 2 + 0.05f, y, 0), Quaternion.Euler(0, -90, 0));
            isShowingRed = false;
        }

        private TextMesh AddLabel(Vector3 localPosition, Quaternion localRotation)
        {
            GameObject label = new GameObject("TeamNumber");
            label.transform.SetParent(body, false);
            label.transform.localPosition = localPosition;
            label.transform.localRotation = localRotation;
            TextMesh text = label.AddComponent<TextMesh>();
            text.text = "5735";
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            Material textMaterial = new Material(labelMaterial);
            textMaterial.mainTexture = text.font.material.mainTexture; // the font's glyph atlas
            text.GetComponent<MeshRenderer>().sharedMaterial = textMaterial;
            text.fontSize = 64;
            text.characterSize = 0.0125f;
            text.anchor = TextAnchor.MiddleCenter;
            text.alignment = TextAlignment.Center;
            text.color = Color.white;
            return text;
        }

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

        private void AddWheel(Transform parent, Vector3 localPosition, Material material)
        {
            Renderer wheel = AddCylinder(parent, "Wheel", Inches(4, 0.75f, 4), localPosition, material);
            wheel.transform.localRotation = Quaternion.Euler(0, 0, 90);
        }

        private Renderer AddBox(Transform parent, string partName, Vector3 size, Vector3 localPosition, Material mat)
        {
            return AddPrimitive(PrimitiveType.Cube, parent, partName, size, localPosition, mat);
        }

        private Renderer AddCylinder(Transform parent, string partName, Vector3 size, Vector3 localPosition,
            Material mat)
        {
            return AddPrimitive(PrimitiveType.Cylinder, parent, partName, size, localPosition, mat);
        }

        private static Renderer AddPrimitive(PrimitiveType type, Transform parent, string partName, Vector3 size,
            Vector3 localPosition, Material mat)
        {
            GameObject part = GameObject.CreatePrimitive(type);
            part.name = partName;
            Destroy(part.GetComponent<Collider>()); // collisions use separate, simpler shapes
            part.transform.SetParent(parent, false);
            part.transform.localPosition = localPosition;
            part.transform.localScale = size;
            Renderer partRenderer = part.GetComponent<Renderer>();
            partRenderer.sharedMaterial = mat;
            return partRenderer;
        }

        private Material MakeMaterial(Color color, float smoothness)
        {
            Material mat = new Material(baseMaterial);
            mat.SetColor("_BaseColor", color);
            mat.SetFloat("_Smoothness", smoothness);
            return mat;
        }

        private static Vector3 Inches(float x, float y, float z)
        {
            return new Vector3(x, y, z) * InchToMeter;
        }
    }
}
