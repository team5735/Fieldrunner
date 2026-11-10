using System.Collections.Generic;
using DriverSim.Field;
using UnityEngine;

namespace DriverSim.Game
{
    /// <summary>How one step of a ball's flight ended.</summary>
    public enum FlightResult
    {
        Flying, // still in the air
        Scored, // crossed a hub opening going down
        HitHub, // entered a hub's body
        Landed, // dropped to the carpet
    }

    /// <summary>
    /// Where a hub is, in Unity world space: its opening and its body's footprint. Immutable.
    /// </summary>
    public readonly struct HubFootprint
    {
        /// <summary>Creates a hub from its values; see the properties for units and frames.</summary>
        public HubFootprint(bool isRed, Vector3 openingCenterWorld, float minXWorldM, float maxXWorldM,
            float minZWorldM, float maxZWorldM)
        {
            IsRed = isRed;
            OpeningCenterWorld = openingCenterWorld;
            MinXWorldM = minXWorldM;
            MaxXWorldM = maxXWorldM;
            MinZWorldM = minZWorldM;
            MaxZWorldM = maxZWorldM;
        }

        /// <summary>Whether this is the red alliance's hub.</summary>
        public bool IsRed { get; }

        /// <summary>Center of the opening, world space, at carpet height, meters.</summary>
        public Vector3 OpeningCenterWorld { get; }

        /// <summary>Smallest world X of the hub's body, meters.</summary>
        public float MinXWorldM { get; }

        /// <summary>Largest world X of the hub's body, meters.</summary>
        public float MaxXWorldM { get; }

        /// <summary>Smallest world Z of the hub's body, meters.</summary>
        public float MinZWorldM { get; }

        /// <summary>Largest world Z of the hub's body, meters.</summary>
        public float MaxZWorldM { get; }

        /// <summary>Whether a point is over the hub's body, ignoring height.</summary>
        /// <param name="pointWorld">Point, world space, meters.</param>
        public bool IsOverBody(Vector3 pointWorld)
        {
            bool isInX = pointWorld.x >= MinXWorldM && pointWorld.x <= MaxXWorldM;
            bool isInZ = pointWorld.z >= MinZWorldM && pointWorld.z <= MaxZWorldM;
            return isInX && isInZ;
        }
    }

    /// <summary>
    /// A ball's whole flight, fixed at launch: the free-flight arc, and which hub (if any) the light aim assist bends
    /// the end of the arc toward. Immutable; build it with <see cref="ShotModel.MakeFlightPlan"/>.
    /// </summary>
    public readonly struct FlightPlan
    {
        /// <summary>Creates a plan from its values; see the properties for units and frames.</summary>
        public FlightPlan(Vector3 startWorld, Vector3 velocityWorldMps, int assistedHubIndex, float crossingTimeS,
            Vector2 assistOffsetWorldM)
        {
            StartWorld = startWorld;
            VelocityWorldMps = velocityWorldMps;
            AssistedHubIndex = assistedHubIndex;
            CrossingTimeS = crossingTimeS;
            AssistOffsetWorldM = assistOffsetWorldM;
        }

        /// <summary>Where the ball left, world space, meters.</summary>
        public Vector3 StartWorld { get; }

        /// <summary>Launch velocity, world space, meters/s.</summary>
        public Vector3 VelocityWorldMps { get; }

        /// <summary>Index of the hub whose opening the arc crosses within reach, or -1 for none.</summary>
        public int AssistedHubIndex { get; }

        /// <summary>Time after launch when the arc crosses the opening height going down, seconds.</summary>
        public float CrossingTimeS { get; }

        /// <summary>Horizontal (X, Z) shift that brings the crossing point onto the hub's center, meters.</summary>
        public Vector2 AssistOffsetWorldM { get; }
    }

    /// <summary>
    /// The state of a flight after one step. Immutable.
    /// </summary>
    public readonly struct FlightStep
    {
        /// <summary>Creates a step result from its values; see the properties for units and frames.</summary>
        public FlightStep(FlightResult result, Vector3 positionWorld, Vector3 velocityWorldMps, int hubIndex)
        {
            Result = result;
            PositionWorld = positionWorld;
            VelocityWorldMps = velocityWorldMps;
            HubIndex = hubIndex;
        }

        /// <summary>How the step ended.</summary>
        public FlightResult Result { get; }

        /// <summary>Where the ball is at the end of the step (for a score, where it crossed), world space, meters.</summary>
        public Vector3 PositionWorld { get; }

        /// <summary>The ball's velocity over the step, world space, meters/s.</summary>
        public Vector3 VelocityWorldMps { get; }

        /// <summary>The hub that was scored in or hit; -1 otherwise.</summary>
        public int HubIndex { get; }
    }

    /// <summary>A ball pushed out of a body: where it ends up and how fast it moves. Immutable.</summary>
    public readonly struct BodyBounce
    {
        /// <summary>Creates a bounce from its values; see the properties for units and frames.</summary>
        public BodyBounce(Vector3 positionWorld, Vector3 velocityWorldMps)
        {
            PositionWorld = positionWorld;
            VelocityWorldMps = velocityWorldMps;
        }

        /// <summary>Where the ball is placed, world space, meters.</summary>
        public Vector3 PositionWorld { get; }

        /// <summary>The ball's velocity after the bounce, world space, meters/s.</summary>
        public Vector3 VelocityWorldMps { get; }
    }

    /// <summary>One distance of the robot code's score shot table. Immutable.</summary>
    public readonly struct ShotTableRow
    {
        /// <summary>Creates a row from its values; see the properties for units.</summary>
        public ShotTableRow(double distanceM, double flywheelRpm, double hoodDeg)
        {
            DistanceM = distanceM;
            FlywheelRpm = flywheelRpm;
            HoodDeg = hoodDeg;
        }

        /// <summary>Distance from the turret to the hub center, meters.</summary>
        public double DistanceM { get; }

        /// <summary>Flywheel speed the robot code commands at that distance, RPM.</summary>
        public double FlywheelRpm { get; }

        /// <summary>Hood angle the robot code commands at that distance, degrees.</summary>
        public double HoodDeg { get; }
    }

    /// <summary>
    /// The shot model: what a launch does and how a flight ends. Pure functions; <see cref="FuelField"/> moves the
    /// balls with them and the gameplay self-test checks them.
    ///
    /// A shot leaves the turret at <c>RPM x ExitMpsPerRpm</c>, elevated <c>90 deg - (hood + HoodElevationOffsetDeg)</c>
    /// (a low hood is a steep shot), along the robot's heading plus the turret angle, with the robot's own velocity
    /// added. Flight is a drag-free arc. The two fitted constants are chosen so that every row of the robot code's
    /// score table lands in the hub opening from its distance; the table below is a copy of that table.
    /// </summary>
    public static class ShotModel
    {
        // fitted once against the score table below: every row lands within 0.43 m of the hub center (the opening
        // is 0.53 m in radius); a shot at the table's RPM and hood reaches the hub from the table's distance
        public const double ExitMpsPerRpm = 0.0021; // meters/s of exit speed per RPM of flywheel
        public const double HoodElevationOffsetDeg = 2.0; // degrees added to the hood angle before it is taken from 90

        public const float GravityMps2 = 9.80665f; // meters/s^2
        public const float LaunchHeightM = 0.5f; // meters above the carpet where a ball leaves the turret
        public const float OpeningHeightM = 1.83f; // meters above the carpet, the hub opening's height
        public const float OpeningRadiusM = 0.53f; // meters from the hub center to the edge of the opening
        public const float AssistMarginM = 0.15f; // meters beyond the opening that still score (light aim assist)
        public const float AssistBendS = 0.2f; // seconds before the crossing during which the arc bends to the center
        public const float HubBounceScale = 0.3f; // fraction of its speed a ball keeps after hitting a hub
        public const float LandingHeightM = 0.6f; // meters; a ball coming down below this is on the field
        public const float BallRadiusM = 0.075f; // meters, a fuel ball is 0.15 m across
        public const double MinLaunchRpm = 500; // flywheel speeds at or below this do not launch (jammed)
        public const float BlueHubCenterXFieldM = 4.6116f; // meters, blue hub center, field frame
        public const float BlueHubCenterYFieldM = 4.0213f; // meters, blue hub center, field frame

        // the robot code's score table: LaunchCalculator.scoreSpeedMap and scoreHoodMap (FRC-2026,
        // src/main/java/frc/robot/commands/LaunchCalculator.java); keys in inches, speeds before rpmMult, hood
        // angles before hoodTweakOffset
        private const double InchToMeter = 0.0254; // meters per inch
        private const double TableRpmMultiplier = 1.12; // rpmMult in LaunchCalculator
        private const double TableHoodTweakDeg = 1.0; // hoodTweakOffset in LaunchCalculator
        private static readonly double[] TableSpeedKeysIn =
            { 56, 71.75, 87.8, 102.2, 117, 132, 147, 163, 177, 192, 207, 216 };
        private static readonly double[] TableSpeedRpm =
            { 2600, 2650, 2675, 2800, 2900, 3050, 3200, 3350, 3450, 3650, 3850, 4000 };
        private static readonly double[] TableHoodKeysIn = { 56, 71.75, 87.8, 102.2, 216 };
        private static readonly double[] TableHoodDeg = { 8, 15, 15, 20, 20 };

        /// <summary>
        /// Both hubs' openings and bodies, taken from the obstacle layout's hub rectangles; index 0 is blue, 1 red.
        /// </summary>
        /// <returns>New array of two hubs.</returns>
        public static HubFootprint[] MakeHubs()
        {
            List<ObstacleSpec> specs = FieldObstacleLayout.GetAll();
            ObstacleSpec blueBody = FindSpec(specs, "BlueHub");
            ObstacleSpec redBody = FindSpec(specs, "RedHub");
            float redCenterXFieldM = FieldFrame.FieldLengthM - BlueHubCenterXFieldM;
            float redCenterYFieldM = FieldFrame.FieldWidthM - BlueHubCenterYFieldM;
            HubFootprint[] hubs = new HubFootprint[2];
            hubs[0] = MakeHub(false, BlueHubCenterXFieldM, BlueHubCenterYFieldM, blueBody);
            hubs[1] = MakeHub(true, redCenterXFieldM, redCenterYFieldM, redBody);
            return hubs;
        }

        /// <summary>The rows of the robot code's score table, one per speed key.</summary>
        /// <returns>New array of rows, nearest distance first.</returns>
        public static ShotTableRow[] MakeScoreTable()
        {
            ShotTableRow[] rows = new ShotTableRow[TableSpeedKeysIn.Length];
            for (int i = 0; i < rows.Length; i++)
            {
                double keyIn = TableSpeedKeysIn[i];
                double flywheelRpm = TableSpeedRpm[i] * TableRpmMultiplier;
                double hoodBaseDeg = Interpolate(TableHoodKeysIn, TableHoodDeg, keyIn);
                double hoodDeg = hoodBaseDeg + TableHoodTweakDeg;
                rows[i] = new ShotTableRow(keyIn * InchToMeter, flywheelRpm, hoodDeg);
            }
            return rows;
        }

        /// <summary>Exit speed of a ball for a flywheel speed.</summary>
        /// <param name="flywheelRpm">Flywheel speed, RPM.</param>
        /// <returns>Exit speed, meters/s.</returns>
        public static double ComputeExitSpeedMps(double flywheelRpm)
        {
            return flywheelRpm * ExitMpsPerRpm;
        }

        /// <summary>Velocity of a ball leaving the turret.</summary>
        /// <param name="flywheelRpm">Flywheel speed, RPM.</param>
        /// <param name="hoodDeg">Hood angle, degrees; a low hood is a steep shot.</param>
        /// <param name="shotHeadingFieldRad">Direction of the shot over the carpet (robot heading plus turret angle),
        /// field frame, radians, CCW positive.</param>
        /// <param name="robotVelocityXFieldMps">Robot velocity along field x, meters/s; added to the ball.</param>
        /// <param name="robotVelocityYFieldMps">Robot velocity along field y, meters/s; added to the ball.</param>
        /// <returns>Launch velocity, world space, meters/s.</returns>
        public static Vector3 ComputeLaunchVelocity(double flywheelRpm, double hoodDeg, double shotHeadingFieldRad,
            double robotVelocityXFieldMps, double robotVelocityYFieldMps)
        {
            double speedMps = ComputeExitSpeedMps(flywheelRpm);
            double elevationDeg = 90 - (hoodDeg + HoodElevationOffsetDeg);
            double elevationRad = elevationDeg * Mathf.Deg2Rad;
            double alongCarpetMps = speedMps * System.Math.Cos(elevationRad);
            double upMps = speedMps * System.Math.Sin(elevationRad);

            // field x is world X and field y is world Z
            double velocityXMps = alongCarpetMps * System.Math.Cos(shotHeadingFieldRad) + robotVelocityXFieldMps;
            double velocityZMps = alongCarpetMps * System.Math.Sin(shotHeadingFieldRad) + robotVelocityYFieldMps;
            return new Vector3((float)velocityXMps, (float)upMps, (float)velocityZMps);
        }

        /// <summary>
        /// Fixes a flight at launch: finds where the free arc crosses the opening height going down and, when that is
        /// within reach of a hub's opening, which hub the aim assist bends the arc toward.
        /// </summary>
        /// <param name="startWorld">Launch point, world space, meters.</param>
        /// <param name="velocityWorldMps">Launch velocity, world space, meters/s.</param>
        /// <param name="hubs">Both hubs, from <see cref="MakeHubs"/>.</param>
        /// <returns>The flight's plan.</returns>
        public static FlightPlan MakeFlightPlan(Vector3 startWorld, Vector3 velocityWorldMps, HubFootprint[] hubs)
        {
            float crossingTimeS = ComputeOpeningCrossingTimeS(startWorld.y, velocityWorldMps.y);
            if (crossingTimeS < 0)
            {
                return new FlightPlan(startWorld, velocityWorldMps, -1, -1, Vector2.zero);
            }

            // where the free arc crosses, and the hub it crosses nearest
            Vector3 crossingWorld = ComputeFreePosition(startWorld, velocityWorldMps, crossingTimeS);
            int nearestIndex = -1;
            float nearestM = AssistReachM();
            for (int i = 0; i < hubs.Length; i++)
            {
                float distanceM = MeasureHorizontalDistance(crossingWorld, hubs[i].OpeningCenterWorld);
                if (distanceM <= nearestM)
                {
                    nearestM = distanceM;
                    nearestIndex = i;
                }
            }
            if (nearestIndex < 0)
            {
                return new FlightPlan(startWorld, velocityWorldMps, -1, crossingTimeS, Vector2.zero);
            }
            Vector3 center = hubs[nearestIndex].OpeningCenterWorld;
            Vector2 offset = new Vector2(center.x - crossingWorld.x, center.z - crossingWorld.z);
            return new FlightPlan(startWorld, velocityWorldMps, nearestIndex, crossingTimeS, offset);
        }

        /// <summary>Where a flight's ball is a time after launch, aim assist included.</summary>
        /// <param name="plan">The flight.</param>
        /// <param name="timeS">Seconds since launch.</param>
        /// <returns>Ball center, world space, meters.</returns>
        public static Vector3 ComputePosition(FlightPlan plan, float timeS)
        {
            Vector3 position = ComputeFreePosition(plan.StartWorld, plan.VelocityWorldMps, timeS);
            if (plan.AssistedHubIndex < 0)
            {
                return position;
            }

            // over the last AssistBendS before the crossing, ease the ball sideways onto the hub's center
            float bendProgress = (timeS - (plan.CrossingTimeS - AssistBendS)) / AssistBendS;
            float clamped = Mathf.Clamp01(bendProgress);
            float eased = clamped * clamped * (3 - 2 * clamped);
            position.x += plan.AssistOffsetWorldM.x * eased;
            position.z += plan.AssistOffsetWorldM.y * eased;
            return position;
        }

        /// <summary>Moves a flight forward and says how the step ended.</summary>
        /// <param name="plan">The flight.</param>
        /// <param name="fromS">Seconds since launch at the start of the step.</param>
        /// <param name="toS">Seconds since launch at the end of the step; greater than <paramref name="fromS"/>.</param>
        /// <param name="hubs">Both hubs, from <see cref="MakeHubs"/>.</param>
        /// <returns>The ball's state at the end of the step.</returns>
        public static FlightStep StepFlight(FlightPlan plan, float fromS, float toS, HubFootprint[] hubs)
        {
            Vector3 from = ComputePosition(plan, fromS);
            Vector3 to = ComputePosition(plan, toS);
            Vector3 velocity = (to - from) / (toS - fromS);

            // scored: crossed the opening height going down within reach of a hub's center
            for (int i = 0; i < hubs.Length; i++)
            {
                bool isCrossing = from.y >= OpeningHeightM && to.y < OpeningHeightM;
                if (!isCrossing)
                {
                    continue;
                }
                float fraction = (from.y - OpeningHeightM) / (from.y - to.y);
                Vector3 crossing = Vector3.Lerp(from, to, fraction);
                float distanceM = MeasureHorizontalDistance(crossing, hubs[i].OpeningCenterWorld);
                if (distanceM <= AssistReachM())
                {
                    return new FlightStep(FlightResult.Scored, crossing, velocity, i);
                }
            }

            // hit a hub: inside its body below the opening height
            for (int i = 0; i < hubs.Length; i++)
            {
                if (to.y < OpeningHeightM && hubs[i].IsOverBody(to))
                {
                    return new FlightStep(FlightResult.HitHub, to, velocity, i);
                }
            }

            // landed: coming down below the field's obstacle height
            if (velocity.y < 0 && to.y < LandingHeightM)
            {
                return new FlightStep(FlightResult.Landed, to, velocity, -1);
            }
            return new FlightStep(FlightResult.Flying, to, velocity, -1);
        }

        /// <summary>
        /// Moves a ball that is inside a hub's body out through the nearest face and reflects its velocity off that
        /// face, keeping <see cref="HubBounceScale"/> of its speed.
        /// </summary>
        /// <param name="hub">The hub that was hit.</param>
        /// <param name="positionWorld">Ball center inside the body, world space, meters.</param>
        /// <param name="velocityWorldMps">Ball velocity, world space, meters/s.</param>
        /// <returns>Where to put the ball and how fast it moves.</returns>
        public static BodyBounce ComputeHubBounce(HubFootprint hub, Vector3 positionWorld, Vector3 velocityWorldMps)
        {
            Vector3 normal;
            Vector3 outside = PushOutOfRectangle(positionWorld, hub.MinXWorldM, hub.MaxXWorldM, hub.MinZWorldM,
                hub.MaxZWorldM, out normal);
            Vector3 reflected = velocityWorldMps;
            float intoFaceMps = Vector3.Dot(velocityWorldMps, normal);
            if (intoFaceMps < 0)
            {
                reflected = velocityWorldMps - 2 * intoFaceMps * normal;
            }
            return new BodyBounce(outside, reflected * HubBounceScale);
        }

        /// <summary>
        /// Moves a point that is inside a rectangle (a box seen from above) out through its nearest face, clear by
        /// the ball's radius.
        /// </summary>
        /// <param name="positionWorld">Ball center, world space, meters.</param>
        /// <param name="minXWorldM">Rectangle's smallest world X, meters.</param>
        /// <param name="maxXWorldM">Rectangle's largest world X, meters.</param>
        /// <param name="minZWorldM">Rectangle's smallest world Z, meters.</param>
        /// <param name="maxZWorldM">Rectangle's largest world Z, meters.</param>
        /// <param name="outwardNormal">Direction of the face the point left through, horizontal and unit length.</param>
        /// <returns>The moved point; the input unchanged when it is outside the rectangle.</returns>
        public static Vector3 PushOutOfRectangle(Vector3 positionWorld, float minXWorldM, float maxXWorldM,
            float minZWorldM, float maxZWorldM, out Vector3 outwardNormal)
        {
            outwardNormal = Vector3.right;
            bool isInside = positionWorld.x > minXWorldM && positionWorld.x < maxXWorldM
                && positionWorld.z > minZWorldM && positionWorld.z < maxZWorldM;
            if (!isInside)
            {
                return positionWorld;
            }

            // the face with the least way out
            float toMinX = positionWorld.x - minXWorldM;
            float toMaxX = maxXWorldM - positionWorld.x;
            float toMinZ = positionWorld.z - minZWorldM;
            float toMaxZ = maxZWorldM - positionWorld.z;
            float leastX = Mathf.Min(toMinX, toMaxX);
            float leastZ = Mathf.Min(toMinZ, toMaxZ);
            float least = Mathf.Min(leastX, leastZ);
            float clearM = BallRadiusM + 0.01f;
            Vector3 moved = positionWorld;
            if (least == toMinX)
            {
                outwardNormal = Vector3.left;
                moved.x = minXWorldM - clearM;
            }
            else if (least == toMaxX)
            {
                outwardNormal = Vector3.right;
                moved.x = maxXWorldM + clearM;
            }
            else if (least == toMinZ)
            {
                outwardNormal = Vector3.back;
                moved.z = minZWorldM - clearM;
            }
            else
            {
                outwardNormal = Vector3.forward;
                moved.z = maxZWorldM + clearM;
            }
            return moved;
        }

        /// <summary>Distance over the carpet between two points, ignoring height.</summary>
        /// <param name="aWorld">First point, world space, meters.</param>
        /// <param name="bWorld">Second point, world space, meters.</param>
        /// <returns>Distance, meters.</returns>
        public static float MeasureHorizontalDistance(Vector3 aWorld, Vector3 bWorld)
        {
            float dxM = aWorld.x - bWorld.x;
            float dzM = aWorld.z - bWorld.z;
            return Mathf.Sqrt(dxM * dxM + dzM * dzM);
        }

        /// <summary>How far from a hub's center a ball can cross the opening height and still score.</summary>
        /// <returns>Distance, meters: the opening's radius plus the aim assist's margin.</returns>
        public static float AssistReachM()
        {
            return OpeningRadiusM + AssistMarginM;
        }

        // ---- low-level helpers (static, no side effects)

        /// <summary>The ball's place on a drag-free arc.</summary>
        private static Vector3 ComputeFreePosition(Vector3 startWorld, Vector3 velocityWorldMps, float timeS)
        {
            Vector3 position = startWorld + velocityWorldMps * timeS;
            position.y -= 0.5f * GravityMps2 * timeS * timeS;
            return position;
        }

        /// <summary>When an arc crosses the opening height going down.</summary>
        /// <param name="startHeightM">Launch height, meters.</param>
        /// <param name="upMps">Launch velocity upward, meters/s.</param>
        /// <returns>Seconds after launch; -1 when the arc never reaches that height.</returns>
        private static float ComputeOpeningCrossingTimeS(float startHeightM, float upMps)
        {
            float riseM = OpeningHeightM - startHeightM;
            float discriminant = upMps * upMps - 2 * GravityMps2 * riseM;
            if (discriminant < 0)
            {
                return -1;
            }
            return (upMps + Mathf.Sqrt(discriminant)) / GravityMps2;
        }

        /// <summary>Builds one hub from its opening center and its body's obstacle rectangle.</summary>
        private static HubFootprint MakeHub(bool isRed, float centerXFieldM, float centerYFieldM, ObstacleSpec body)
        {
            Vector3 center = FieldFrame.ConvertFieldToWorld(centerXFieldM, centerYFieldM, 0);
            Vector3 minCorner = FieldFrame.ConvertFieldToWorld(body.MinXFieldM, body.MinYFieldM, 0);
            Vector3 maxCorner = FieldFrame.ConvertFieldToWorld(body.MaxXFieldM, body.MaxYFieldM, 0);
            return new HubFootprint(isRed, center, minCorner.x, maxCorner.x, minCorner.z, maxCorner.z);
        }

        /// <summary>The obstacle with a given name.</summary>
        private static ObstacleSpec FindSpec(List<ObstacleSpec> specs, string specName)
        {
            foreach (ObstacleSpec spec in specs)
            {
                if (spec.Name == specName)
                {
                    return spec;
                }
            }
            throw new System.InvalidOperationException("obstacle layout has no " + specName);
        }

        /// <summary>Piecewise linear lookup that holds the end values beyond the keys.</summary>
        private static double Interpolate(double[] keys, double[] values, double key)
        {
            if (key <= keys[0])
            {
                return values[0];
            }
            for (int i = 1; i < keys.Length; i++)
            {
                if (key <= keys[i])
                {
                    double fraction = (key - keys[i - 1]) / (keys[i] - keys[i - 1]);
                    return values[i - 1] + fraction * (values[i] - values[i - 1]);
                }
            }
            return values[values.Length - 1];
        }
    }
}
