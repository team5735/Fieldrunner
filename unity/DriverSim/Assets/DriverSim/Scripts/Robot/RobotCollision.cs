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
        public CollisionResult(double xFieldM, double yFieldM, double pushedM, bool isTrenchBlocked)
        {
            XFieldM = xFieldM;
            YFieldM = yFieldM;
            PushedM = pushedM;
            IsTrenchBlocked = isTrenchBlocked;
        }

        /// <summary>Constrained robot x, field frame, meters.</summary>
        public double XFieldM { get; }

        /// <summary>Constrained robot y, field frame, meters.</summary>
        public double YFieldM { get; }

        /// <summary>How far the pose was moved to clear every obstacle, meters.</summary>
        public double PushedM { get; }

        /// <summary>Whether a trench bar moved the robot (it was too tall to pass under).</summary>
        public bool IsTrenchBlocked { get; }
    }

    /// <summary>
    /// Keeps the robot out of the field's obstacles. Each frame it takes the bridge's reported robot pose, pushes it out
    /// of every <see cref="FieldObstacle"/> collider, and exposes the constrained pose for the drawn robot. When the
    /// constrained pose differs from the reported one it sends the bridge a pose correction, so the robot code's own
    /// pose follows within about a network round trip. Heading is never changed.
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

        private const int MaxIterations = 4; // push-out passes per frame
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

        /// <summary>
        /// Pushes a robot pose out of every obstacle that applies to it. Reads the physics world only; sends nothing.
        /// </summary>
        /// <param name="xFieldM">Robot x, field frame, meters.</param>
        /// <param name="yFieldM">Robot y, field frame, meters.</param>
        /// <param name="headingFieldRad">Robot heading, field frame, radians, CCW positive.</param>
        /// <param name="hoodDeg">Hood angle, degrees; above <see cref="TrenchMaxHoodDeg"/> the robot is too tall for a
        /// trench bar.</param>
        /// <returns>The constrained position and what moved it.</returns>
        public CollisionResult Resolve(double xFieldM, double yFieldM, double headingFieldRad, double hoodDeg)
        {
            EnsureProxy();
            bool isTall = hoodDeg > TrenchMaxHoodDeg;
            Quaternion rotation = FieldFrame.ConvertHeadingToRotation(headingFieldRad);
            double candidateXFieldM = xFieldM;
            double candidateYFieldM = yFieldM;
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
                    candidateXFieldM += push.x;
                    candidateYFieldM += push.z;
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
                return new CollisionResult(xFieldM, yFieldM, 0, false);
            }
            return new CollisionResult(candidateXFieldM, candidateYFieldM, pushedM, isTrenchBlocked);
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
            CollisionResult result = Resolve(state.XFieldM, state.YFieldM, state.HeadingFieldRad, state.HoodDeg);
            latest = result;
            hasLatest = true;

            // tell the robot code when the game moved the robot
            if (result.PushedM >= MinPushM)
            {
                link.SendPoseCorrection(state.TimestampS, result.XFieldM, result.YFieldM);
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
    }
}
