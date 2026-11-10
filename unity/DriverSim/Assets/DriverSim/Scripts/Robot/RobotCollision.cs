using DriverSim.Field;
using DriverSim.Net;
using UnityEngine;

namespace DriverSim.Robot
{
    /// <summary>
    /// Outcome of resolving a robot pose against the field's obstacles. Immutable.
    /// </summary>
    public readonly struct CollisionResult
    {
        /// <summary>Creates a result; see the properties for units and frames.</summary>
        public CollisionResult(double xFieldM, double yFieldM, double headingFieldRad, double pushedM,
            bool isTrenchBlocked)
        {
            XFieldM = xFieldM;
            YFieldM = yFieldM;
            HeadingFieldRad = headingFieldRad;
            PushedM = pushedM;
            IsTrenchBlocked = isTrenchBlocked;
        }

        /// <summary>Constrained robot x, field frame, meters.</summary>
        public double XFieldM { get; }

        /// <summary>Constrained robot y, field frame, meters.</summary>
        public double YFieldM { get; }

        /// <summary>Constrained robot heading, field frame, radians, CCW positive.</summary>
        public double HeadingFieldRad { get; }

        /// <summary>How far the pose was moved to clear every obstacle (slip included), meters.</summary>
        public double PushedM { get; }

        /// <summary>Whether a trench bar moved the robot (it was too tall to pass under).</summary>
        public bool IsTrenchBlocked { get; }
    }

    /// <summary>
    /// Keeps the robot out of the field's obstacles. Each frame it takes the bridge's reported robot pose, pushes it out
    /// of every <see cref="FieldObstacle"/> collider, and exposes the constrained pose for the drawn robot. When the
    /// constrained pose differs from the reported one it sends the bridge a pose correction, so the simulated robot
    /// follows within about a network round trip. A push also turns the robot about the contact point, and the robot
    /// loses some speed along the obstacle it slides on (wall friction).
    ///
    /// A LowCeiling obstacle (a trench bar) blocks only while the hood is above <see cref="TrenchMaxHoodDeg"/>.
    /// </summary>
    [DefaultExecutionOrder(-10)]
    public sealed class RobotCollision : MonoBehaviour
    {
        public const float FootprintLengthM = 0.927f; // meters along robot x, bumpers included (36.5 in, CompbotConstants)
        public const float FootprintWidthM = 0.813f; // meters along robot y, bumpers included (32 in, CompbotConstants)
        public const double TrenchMaxHoodDeg = 12; // hood angle above this is too tall for a trench bar: the robot code's
                                                   // HoodConstants.LOWEST_ANGLE_DEGREES (8) plus 4, a training rule

        public const float CollisionTurnGain = 1.0f; // turn per push, dimensionless: radians per (meters pushed x
                                                     // lever fraction); 0 turns nothing
        public const float MaxTurnRadPerFrame = 0.05f; // most the robot can be turned by one frame's pushes, radians
        public const float WallFriction = 0.5f; // meters of sliding stopped per meter pushed into the obstacle

        private const int MaxIterations = 4; // push-out passes per frame
        private const float ContactTieM = 0.01f; // corners this close to the deepest one share the contact, meters
        private const double MinPushM = 0.001; // pushes shorter than this are ignored, meters
        private const float QueryHeightM = 0.5f; // height of the box used to find obstacles, meters
        private const float ProxyHeightM = 8f; // height of the box used to measure penetration, meters (so it is
                                               // always cheapest to push out sideways, never up or down)
        private const int MaxOverlaps = 32; // obstacle colliders examined per query

        [Tooltip("Link to the bridge; the reported robot state and corrections go through here.")]
        [SerializeField] private DriverSimLink link;

        private readonly Collider[] overlaps = new Collider[MaxOverlaps]; // scratch space for overlap queries
        private BoxCollider proxy; // robot-shaped collider used only to measure penetration; never moves
        private CollisionResult latest; // result of the most recent frame's resolution
        private bool hasLatest; // latest holds a real result

        /// <summary>Sets the references the component needs; call before the scene starts.</summary>
        /// <param name="driverSimLink">Link to the bridge.</param>
        public void SetReferences(DriverSimLink driverSimLink)
        {
            link = driverSimLink;
        }

        /// <summary>
        /// Whether the most recent frame moved the robot out of an obstacle. False until a robot state has been
        /// resolved.
        /// </summary>
        public bool IsColliding()
        {
            if (!hasLatest)
            {
                return false;
            }
            return latest.PushedM >= MinPushM;
        }

        /// <summary>Whether the most recent frame was blocked by a trench bar with the hood up.</summary>
        public bool IsTrenchBlocked()
        {
            if (!hasLatest)
            {
                return false;
            }
            return latest.IsTrenchBlocked;
        }

        /// <summary>Whether a robot state has been resolved yet (the constrained pose is meaningful).</summary>
        public bool HasConstrainedPose()
        {
            return hasLatest;
        }

        /// <summary>Constrained robot x from the most recent frame, field frame, meters.</summary>
        public double GetConstrainedXFieldM()
        {
            return latest.XFieldM;
        }

        /// <summary>Constrained robot y from the most recent frame, field frame, meters.</summary>
        public double GetConstrainedYFieldM()
        {
            return latest.YFieldM;
        }

        /// <summary>Constrained robot heading from the most recent frame, field frame, radians.</summary>
        public double GetConstrainedHeadingFieldRad()
        {
            return latest.HeadingFieldRad;
        }

        /// <summary>
        /// Pushes a robot pose out of every obstacle that applies to it, turning it where it pushes at a corner and
        /// slowing its slide along the obstacle.
        /// </summary>
        /// <param name="xFieldM">Robot x, field frame, meters.</param>
        /// <param name="yFieldM">Robot y, field frame, meters.</param>
        /// <param name="headingFieldRad">Robot heading, field frame, radians, CCW positive.</param>
        /// <param name="hoodDeg">Hood angle, degrees; above <see cref="TrenchMaxHoodDeg"/> the robot is too tall for a
        /// trench bar.</param>
        /// <param name="velocityXFieldMps">Robot velocity along field x, meters/s; used for wall friction.</param>
        /// <param name="velocityYFieldMps">Robot velocity along field y, meters/s.</param>
        /// <param name="dtS">Seconds the correction stands for (the frame time); limits how much slide one frame stops.
        /// </param>
        /// <returns>The constrained position and heading, and what moved them.</returns>
        public CollisionResult Resolve(double xFieldM, double yFieldM, double headingFieldRad, double hoodDeg,
            double velocityXFieldMps, double velocityYFieldMps, double dtS)
        {
            EnsureProxy();
            bool isTall = hoodDeg > TrenchMaxHoodDeg;
            double candidateXFieldM = xFieldM;
            double candidateYFieldM = yFieldM;
            double candidateHeadingRad = headingFieldRad;
            Quaternion rotation = FieldFrame.ConvertHeadingToRotation(candidateHeadingRad);
            double turnedRad = 0;
            double pushXM = 0;
            double pushYM = 0;
            bool isTrenchBlocked = false;

            // push out of what overlaps, a few passes so a push into one obstacle can't leave the robot in another
            for (int iteration = 0; iteration < MaxIterations; iteration++)
            {
                Vector3 queryCenter = FieldFrame.ConvertFieldToWorld(candidateXFieldM, candidateYFieldM,
                    QueryHeightM / 2);
                Vector3 queryHalfExtents = new Vector3(FootprintWidthM / 2, QueryHeightM / 2, FootprintLengthM / 2);
                int count = Physics.OverlapBoxNonAlloc(queryCenter, queryHalfExtents, overlaps, rotation, ~0,
                    QueryTriggerInteraction.Ignore);

                // measure and apply each push against the proxy at the current candidate
                bool hasMoved = false;
                Vector3 proxyPosition = FieldFrame.ConvertFieldToWorld(candidateXFieldM, candidateYFieldM, 0);
                for (int i = 0; i < count; i++)
                {
                    Collider other = overlaps[i];
                    FieldObstacle obstacle = other.GetComponent<FieldObstacle>();
                    if (obstacle == null)
                    {
                        continue;
                    }
                    bool isLowCeiling = obstacle.GetKind() == ObstacleKind.LowCeiling;
                    if (isLowCeiling && !isTall)
                    {
                        continue;
                    }
                    Vector3 pushDirection;
                    float pushDistance;
                    Transform otherTransform = other.transform;
                    bool isPenetrating = Physics.ComputePenetration(proxy, proxyPosition, rotation, other,
                        otherTransform.position, otherTransform.rotation, out pushDirection, out pushDistance);
                    if (!isPenetrating)
                    {
                        continue;
                    }

                    // horizontal push only
                    pushDirection.y = 0;
                    if (pushDistance < MinPushM || pushDirection.sqrMagnitude < 1e-8f)
                    {
                        continue;
                    }
                    Vector3 push = pushDirection.normalized * pushDistance;

                    // the push acts at the robot's deepest corner, which turns the robot about its center
                    float turnRad = ComputeTurn(candidateHeadingRad, push.x, push.z);
                    float turnLowRad = -MaxTurnRadPerFrame - (float)turnedRad;
                    float turnHighRad = MaxTurnRadPerFrame - (float)turnedRad;
                    turnRad = Mathf.Clamp(turnRad, turnLowRad, turnHighRad);
                    turnedRad += turnRad;
                    candidateHeadingRad += turnRad;
                    rotation = FieldFrame.ConvertHeadingToRotation(candidateHeadingRad);

                    candidateXFieldM += push.x;
                    candidateYFieldM += push.z;
                    pushXM += push.x;
                    pushYM += push.z;
                    proxyPosition = FieldFrame.ConvertFieldToWorld(candidateXFieldM, candidateYFieldM, 0);
                    hasMoved = true;
                    if (isLowCeiling)
                    {
                        isTrenchBlocked = true;
                    }
                }
                if (!hasMoved)
                {
                    break;
                }
            }

            double dxM = candidateXFieldM - xFieldM;
            double dyM = candidateYFieldM - yFieldM;
            double pushedM = System.Math.Sqrt(dxM * dxM + dyM * dyM);
            if (pushedM < MinPushM)
            {
                return new CollisionResult(xFieldM, yFieldM, headingFieldRad, 0, false);
            }

            // wall friction: the robot's slide along what it pushes against is slowed, the wheels keep turning
            Vector2 slipM = ComputeSlip(pushXM, pushYM, velocityXFieldMps, velocityYFieldMps, dtS);
            candidateXFieldM += slipM.x;
            candidateYFieldM += slipM.y;
            double finalDxM = candidateXFieldM - xFieldM;
            double finalDyM = candidateYFieldM - yFieldM;
            double movedM = System.Math.Sqrt(finalDxM * finalDxM + finalDyM * finalDyM);
            return new CollisionResult(candidateXFieldM, candidateYFieldM, candidateHeadingRad, movedM,
                isTrenchBlocked);
        }

        /// <summary>
        /// Deepest penetration left between a pose and the obstacles that apply to it; zero when the pose is clear.
        /// </summary>
        /// <param name="xFieldM">Robot x, field frame, meters.</param>
        /// <param name="yFieldM">Robot y, field frame, meters.</param>
        /// <param name="headingFieldRad">Robot heading, field frame, radians, CCW positive.</param>
        /// <param name="hoodDeg">Hood angle, degrees.</param>
        /// <returns>Penetration depth, meters.</returns>
        public double MeasureOverlap(double xFieldM, double yFieldM, double headingFieldRad, double hoodDeg)
        {
            EnsureProxy();
            bool isTall = hoodDeg > TrenchMaxHoodDeg;
            Quaternion rotation = FieldFrame.ConvertHeadingToRotation(headingFieldRad);
            Vector3 queryCenter = FieldFrame.ConvertFieldToWorld(xFieldM, yFieldM, QueryHeightM / 2);
            Vector3 queryHalfExtents = new Vector3(FootprintWidthM / 2, QueryHeightM / 2, FootprintLengthM / 2);
            int count = Physics.OverlapBoxNonAlloc(queryCenter, queryHalfExtents, overlaps, rotation, ~0,
                QueryTriggerInteraction.Ignore);
            Vector3 proxyPosition = FieldFrame.ConvertFieldToWorld(xFieldM, yFieldM, 0);
            double deepestM = 0;
            for (int i = 0; i < count; i++)
            {
                FieldObstacle obstacle = overlaps[i].GetComponent<FieldObstacle>();
                if (obstacle == null)
                {
                    continue;
                }
                if (obstacle.GetKind() == ObstacleKind.LowCeiling && !isTall)
                {
                    continue;
                }
                Transform otherTransform = overlaps[i].transform;
                Vector3 pushDirection;
                float pushDistance;
                bool isPenetrating = Physics.ComputePenetration(proxy, proxyPosition, rotation, overlaps[i],
                    otherTransform.position, otherTransform.rotation, out pushDirection, out pushDistance);
                if (isPenetrating && pushDistance > deepestM)
                {
                    deepestM = pushDistance;
                }
            }
            return deepestM;
        }

        // ---- Unity messages

        private void LateUpdate()
        {
            // nothing to resolve until the bridge sends state
            hasLatest = false;
            if (link == null || !link.HasRobotState())
            {
                return;
            }
            RobotState state = link.GetRobotState();

            // constrain the reported pose
            CollisionResult result = Resolve(state.XFieldM, state.YFieldM, state.HeadingFieldRad, state.HoodDeg,
                state.VelocityXFieldMps, state.VelocityYFieldMps, Time.unscaledDeltaTime);
            latest = result;
            hasLatest = true;

            // tell the simulated robot when the game moved it
            if (result.PushedM >= MinPushM)
            {
                link.SendPoseCorrection(state.TimestampS, result.XFieldM, result.YFieldM, result.HeadingFieldRad);
            }
        }

        // ---- private stateful helpers

        /// <summary>Creates the penetration-measuring proxy on first use.</summary>
        private void EnsureProxy()
        {
            if (proxy != null)
            {
                return;
            }

            // a robot-shaped box on its own object, away from the drawn robot so the robot never hits itself;
            // it is only ever passed to ComputePenetration with explicit positions
            GameObject proxyObject = new GameObject("RobotCollisionProxy");
            proxyObject.transform.SetParent(transform, false);
            proxy = proxyObject.AddComponent<BoxCollider>();
            proxy.isTrigger = true;
            proxy.size = new Vector3(FootprintWidthM, ProxyHeightM, FootprintLengthM);
            proxy.center = Vector3.zero;
        }

        // ---- low-level helpers (static, no side effects)

        /// <summary>
        /// How far one push turns the robot. The push acts at the robot's deepest corner (the middle of the deepest
        /// corners when two are about equally deep, as in a flush hit), so a robot driven corner-first swings flush
        /// and one driven flat does not turn.
        /// </summary>
        /// <param name="headingFieldRad">Robot heading, field frame, radians.</param>
        /// <param name="pushXM">Push along field x, meters.</param>
        /// <param name="pushYM">Push along field y, meters.</param>
        /// <returns>Turn, radians, CCW positive; unclamped.</returns>
        private static float ComputeTurn(double headingFieldRad, float pushXM, float pushYM)
        {
            float pushLengthM = Mathf.Sqrt(pushXM * pushXM + pushYM * pushYM);
            if (pushLengthM < 1e-6f)
            {
                return 0;
            }
            float pushDirX = pushXM / pushLengthM;
            float pushDirY = pushYM / pushLengthM;

            // each corner's offset from the robot center (field frame) and how deep it sits against the push
            float cosHeading = Mathf.Cos((float)headingFieldRad);
            float sinHeading = Mathf.Sin((float)headingFieldRad);
            float halfLengthM = FootprintLengthM / 2;
            float halfWidthM = FootprintWidthM / 2;
            float[] cornerSignsX = { 1f, 1f, -1f, -1f };
            float[] cornerSignsY = { 1f, -1f, 1f, -1f };
            float[] offsetsX = new float[4];
            float[] offsetsY = new float[4];
            float[] depthsM = new float[4];
            float deepestM = float.MinValue;
            for (int i = 0; i < 4; i++)
            {
                float robotX = cornerSignsX[i] * halfLengthM;
                float robotY = cornerSignsY[i] * halfWidthM;
                offsetsX[i] = robotX * cosHeading - robotY * sinHeading;
                offsetsY[i] = robotX * sinHeading + robotY * cosHeading;
                depthsM[i] = -(offsetsX[i] * pushDirX + offsetsY[i] * pushDirY);
                if (depthsM[i] > deepestM)
                {
                    deepestM = depthsM[i];
                }
            }

            // contact: the middle of the corners that are about as deep as the deepest
            float contactX = 0;
            float contactY = 0;
            int contactCount = 0;
            for (int i = 0; i < 4; i++)
            {
                if (depthsM[i] >= deepestM - ContactTieM)
                {
                    contactX += offsetsX[i];
                    contactY += offsetsY[i];
                    contactCount++;
                }
            }
            contactX /= contactCount;
            contactY /= contactCount;

            // turn = gain x (lever x push) / lever^2, CCW positive in the field frame
            float leverSquaredM2 = contactX * contactX + contactY * contactY;
            if (leverSquaredM2 < 1e-6f)
            {
                return 0;
            }
            float torqueM2 = contactX * pushYM - contactY * pushXM;
            return CollisionTurnGain * torqueM2 / leverSquaredM2;
        }

        /// <summary>
        /// How far to pull the robot back along the obstacle it slides on: the slide it would make this frame, capped
        /// by <see cref="WallFriction"/> times how far it was pushed.
        /// </summary>
        /// <param name="pushXM">Total push this frame along field x, meters.</param>
        /// <param name="pushYM">Total push this frame along field y, meters.</param>
        /// <param name="velocityXFieldMps">Robot velocity along field x, meters/s.</param>
        /// <param name="velocityYFieldMps">Robot velocity along field y, meters/s.</param>
        /// <param name="dtS">Seconds the frame stands for.</param>
        /// <returns>Movement to add, field frame, meters (opposite the sliding velocity).</returns>
        private static Vector2 ComputeSlip(double pushXM, double pushYM, double velocityXFieldMps,
            double velocityYFieldMps, double dtS)
        {
            double pushedM = System.Math.Sqrt(pushXM * pushXM + pushYM * pushYM);
            if (pushedM < MinPushM || dtS <= 0)
            {
                return Vector2.zero;
            }

            // velocity along the obstacle: what is left after removing the part into it
            double normalX = pushXM / pushedM;
            double normalY = pushYM / pushedM;
            double intoWallMps = velocityXFieldMps * normalX + velocityYFieldMps * normalY;
            double tangentXMps = velocityXFieldMps - intoWallMps * normalX;
            double tangentYMps = velocityYFieldMps - intoWallMps * normalY;
            double tangentMps = System.Math.Sqrt(tangentXMps * tangentXMps + tangentYMps * tangentYMps);
            if (tangentMps < 1e-6)
            {
                return Vector2.zero;
            }

            // stop up to the slide this frame, but no more than friction allows
            double slideM = tangentMps * dtS;
            double frictionM = WallFriction * pushedM;
            double lossM = System.Math.Min(slideM, frictionM);
            float slipX = (float)(-tangentXMps / tangentMps * lossM);
            float slipY = (float)(-tangentYMps / tangentMps * lossM);
            return new Vector2(slipX, slipY);
        }
    }
}
