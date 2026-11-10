using DriverSim.Net;
using DriverSim.Robot;
using UnityEngine;

namespace DriverSim.Game
{
    /// <summary>
    /// The robot's side of the fuel game: the hopper count, the intake and the launcher. A ball touching the intake zone
    /// (a box across the front bumper) is taken into the hopper while the arm is down and the roller pulls in; while the
    /// spindex feeds, the hopper has fuel and the flywheel is spinning, one ball leaves the turret every
    /// <see cref="LaunchPeriodS"/> and the bridge is told so the flywheel loses speed.
    /// </summary>
    public sealed class RobotFuel : MonoBehaviour
    {
        public const int Capacity = 20; // balls the hopper holds
        public const int PreloadCount = 8; // balls the robot starts a session with
        public const float LaunchPeriodS = 0.125f; // seconds between balls while feeding (8 balls/s)

        private const float IntakeWidthM = 0.635f; // meters, the frame's width (25 in)
        private const float IntakeDepthM = 0.25f; // meters in front of the front bumper
        private const float IntakeHeightM = 0.2f; // meters above the carpet
        private const double IntakeArmMaxDeg = 40; // the arm must be below this angle to take fuel
        private const double IntakeRollerMinVolts = 2; // the roller must pull in harder than this
        private const double FeedMaxVolts = -1; // the spindex feeds the launcher below this voltage
        private const int MaxIntakeHits = 16; // balls examined per intake query

        [Tooltip("Link to the bridge; robot state comes from here.")]
        [SerializeField] private DriverSimLink link;

        [Tooltip("Drawn robot: the intake zone and the turret follow it.")]
        [SerializeField] private RobotModel robotModel;

        [Tooltip("The fuel on the field.")]
        [SerializeField] private FuelField field;

        private readonly Collider[] intakeHits = new Collider[MaxIntakeHits]; // scratch space for the intake query
        private int hopperCount; // balls in the hopper
        private float launchTimerS = LaunchPeriodS; // seconds since the last launch while feeding

        /// <summary>Sets the references the component needs; call before the scene starts.</summary>
        /// <param name="driverSimLink">Link to the bridge.</param>
        /// <param name="model">Drawn robot.</param>
        /// <param name="fuelField">The fuel on the field.</param>
        public void SetReferences(DriverSimLink driverSimLink, RobotModel model, FuelField fuelField)
        {
            link = driverSimLink;
            robotModel = model;
            field = fuelField;
        }

        /// <summary>Balls in the hopper.</summary>
        public int GetHopperCount()
        {
            return hopperCount;
        }

        /// <summary>Sets the number of balls in the hopper, within zero and <see cref="Capacity"/>.</summary>
        /// <param name="count">Balls in the hopper.</param>
        public void SetHopperCount(int count)
        {
            hopperCount = Mathf.Clamp(count, 0, Capacity);
        }

        /// <summary>Fills the hopper with the session's starting balls.</summary>
        public void Preload()
        {
            hopperCount = PreloadCount;
            launchTimerS = LaunchPeriodS;
        }

        /// <summary>
        /// Takes into the hopper every ball touching the intake zone, while the arm is down, the roller pulls in and
        /// there is room.
        /// </summary>
        /// <param name="robotPositionWorld">Drawn robot origin (on the carpet, at its center), world space, meters.</param>
        /// <param name="robotRotationWorld">Drawn robot rotation, world space.</param>
        /// <param name="intakeArmDeg">Intake arm angle above horizontal, robot-code degrees (about 15 down, 105 up).</param>
        /// <param name="rollerVolts">Roller voltage; positive pulls balls in.</param>
        /// <returns>Number of balls taken.</returns>
        public int RunIntake(Vector3 robotPositionWorld, Quaternion robotRotationWorld, double intakeArmDeg,
            double rollerVolts)
        {
            bool isArmDown = intakeArmDeg < IntakeArmMaxDeg;
            bool isRollerIn = rollerVolts > IntakeRollerMinVolts;
            if (!isArmDown || !isRollerIn || hopperCount >= Capacity)
            {
                return 0;
            }

            // the box in front of the front bumper
            float frontM = RobotCollision.FootprintLengthM / 2;
            Vector3 centerLocal = new Vector3(0, IntakeHeightM / 2, frontM + IntakeDepthM / 2);
            Vector3 centerWorld = robotPositionWorld + robotRotationWorld * centerLocal;
            Vector3 halfExtents = new Vector3(IntakeWidthM / 2, IntakeHeightM / 2, IntakeDepthM / 2);
            int count = Physics.OverlapBoxNonAlloc(centerWorld, halfExtents, intakeHits, robotRotationWorld,
                GameLayers.GetFuelMask(), QueryTriggerInteraction.Ignore);

            // take each resting ball touched, until the hopper is full
            int taken = 0;
            for (int i = 0; i < count && hopperCount < Capacity; i++)
            {
                FuelBall ball = intakeHits[i].GetComponent<FuelBall>();
                if (ball == null || ball.GetState() != FuelState.Resting)
                {
                    continue;
                }
                field.Remove(ball);
                hopperCount++;
                taken++;
            }
            return taken;
        }

        /// <summary>
        /// Launches balls while the spindex feeds, the hopper has fuel and the flywheel is above
        /// <see cref="ShotModel.MinLaunchRpm"/>: one ball per <see cref="LaunchPeriodS"/>. Each launch tells the bridge.
        /// </summary>
        /// <param name="dtS">Seconds since the last call.</param>
        /// <param name="feedVolts">Spindex feed voltage; negative feeds the launcher.</param>
        /// <param name="flywheelRpm">Flywheel speed, RPM.</param>
        /// <param name="hoodDeg">Hood angle, degrees.</param>
        /// <param name="muzzleWorld">Where balls leave the turret, world space, meters.</param>
        /// <param name="shotHeadingFieldRad">Robot heading plus turret angle, field frame, radians, CCW positive.</param>
        /// <param name="robotVelocityXFieldMps">Robot velocity along field x, meters/s.</param>
        /// <param name="robotVelocityYFieldMps">Robot velocity along field y, meters/s.</param>
        /// <returns>Number of balls launched.</returns>
        public int RunLauncher(float dtS, double feedVolts, double flywheelRpm, double hoodDeg, Vector3 muzzleWorld,
            double shotHeadingFieldRad, double robotVelocityXFieldMps, double robotVelocityYFieldMps)
        {
            bool isFeeding = feedVolts < FeedMaxVolts;
            bool isSpinning = flywheelRpm > ShotModel.MinLaunchRpm;
            if (!isFeeding || !isSpinning || hopperCount <= 0)
            {
                launchTimerS = LaunchPeriodS;
                return 0;
            }
            launchTimerS += dtS;
            int launched = 0;
            while (launchTimerS >= LaunchPeriodS && hopperCount > 0)
            {
                launchTimerS -= LaunchPeriodS;
                Vector3 velocity = ShotModel.ComputeLaunchVelocity(flywheelRpm, hoodDeg, shotHeadingFieldRad,
                    robotVelocityXFieldMps, robotVelocityYFieldMps);
                field.Launch(muzzleWorld, velocity);
                hopperCount--;
                launched++;
                if (link != null)
                {
                    link.SendCommand("ballLaunched");
                }
            }
            return launched;
        }

        // ---- Unity messages

        private void Start()
        {
            Preload();
        }

        private void FixedUpdate()
        {
            if (link == null || !link.HasRobotState())
            {
                return;
            }
            RobotState state = link.GetRobotState();
            Transform body = robotModel.GetBody();
            RunIntake(body.position, body.rotation, state.IntakeArmDeg, state.IntakeRollerVolts);
        }

        private void Update()
        {
            if (link == null || !link.HasRobotState())
            {
                return;
            }
            RobotState state = link.GetRobotState();

            // the turret's drawn direction is the robot's heading plus the turret angle
            Transform turret = robotModel.GetTurret();
            Vector3 muzzle = turret.position;
            muzzle.y = ShotModel.LaunchHeightM;
            Vector3 aim = turret.forward;
            double shotHeadingFieldRad = System.Math.Atan2(aim.z, aim.x);
            RunLauncher(Time.deltaTime, state.SpindexFeedVolts, state.FlywheelRpm, state.HoodDeg, muzzle,
                shotHeadingFieldRad, state.VelocityXFieldMps, state.VelocityYFieldMps);
        }
    }
}
