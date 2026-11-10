using DriverSim.Game;
using DriverSim.Net;
using UnityEngine;

namespace DriverSim.Robot
{
    /// <summary>
    /// A kinematic box the size of the bumpers that follows the drawn (collision-constrained) robot pose and shoves
    /// fuel around. It lives on the RobotPusher layer, which collides with fuel only. Moves in <c>FixedUpdate</c> with
    /// <c>Rigidbody.MovePosition</c> / <c>MoveRotation</c> so balls feel the robot's velocity; a jump of the drawn
    /// robot (a placement) teleports the box instead.
    /// </summary>
    public sealed class RobotPusher : MonoBehaviour
    {
        private const float BoxHeightM = 0.2f; // meters above the carpet the box reaches
        private const float TeleportJumpM = 0.5f; // a move longer than this is a placement, not driving
        private const float ParkedHeightM = -50f; // meters; where the box waits while there is no robot

        [Tooltip("Link to the bridge, to know whether there is a robot.")]
        [SerializeField] private DriverSimLink link;

        [Tooltip("Drawn robot; the box follows its body.")]
        [SerializeField] private RobotModel robotModel;

        private Rigidbody pusherBody; // kinematic body of the box
        private BoxCollider pusherCollider; // the box
        private bool hasPose; // the box has been placed at least once

        /// <summary>Sets the references the component needs; call before the scene starts.</summary>
        /// <param name="driverSimLink">Link to the bridge.</param>
        /// <param name="model">Drawn robot.</param>
        public void SetReferences(DriverSimLink driverSimLink, RobotModel model)
        {
            link = driverSimLink;
            robotModel = model;
        }

        // ---- Unity messages

        private void Awake()
        {
            GameObject boxObject = new GameObject("RobotPusherBox");
            boxObject.layer = GameLayers.RobotPusher;
            boxObject.transform.SetParent(transform, false);
            pusherBody = boxObject.AddComponent<Rigidbody>();
            pusherBody.isKinematic = true;
            pusherBody.useGravity = false;
            pusherCollider = boxObject.AddComponent<BoxCollider>();
            pusherCollider.size = new Vector3(RobotCollision.FootprintWidthM, BoxHeightM,
                RobotCollision.FootprintLengthM);
            pusherCollider.center = new Vector3(0, BoxHeightM / 2, 0);
            pusherCollider.enabled = false;
        }

        private void FixedUpdate()
        {
            // no robot drawn: park the box away from the fuel
            bool hasRobot = link != null && link.HasRobotState();
            pusherCollider.enabled = hasRobot;
            if (!hasRobot)
            {
                hasPose = false;
                pusherBody.position = new Vector3(0, ParkedHeightM, 0);
                return;
            }

            // follow the drawn body; a placement jumps there
            Transform body = robotModel.GetBody();
            Vector3 target = body.position;
            Quaternion rotation = body.rotation;
            float jumpM = Vector3.Distance(pusherBody.position, target);
            if (!hasPose || jumpM > TeleportJumpM)
            {
                pusherBody.position = target;
                pusherBody.rotation = rotation;
                hasPose = true;
                return;
            }
            pusherBody.MovePosition(target);
            pusherBody.MoveRotation(rotation);
        }
    }
}
