using System.Collections;
using DriverSim.Field;
using DriverSim.Util;
using UnityEngine;

namespace DriverSim.Robot
{
    /// <summary>
    /// Checks the obstacle layout and <see cref="RobotCollision"/> without a robot sim: feeds synthetic poses straight
    /// into the collision resolver, logs one <c>[Driver-Sim collision-selftest] PASS|FAIL name</c> line per case to the
    /// player log, then quits with exit code 0 when every case passed and 1 otherwise. Active only with the command
    /// line flag <c>--collision-selftest</c>.
    /// </summary>
    public sealed class CollisionSelfTest : MonoBehaviour
    {
        private const double PositionToleranceM = 0.002; // meters of slack on expected positions
        private const double ClearHoodDeg = 8; // hood angle that fits under a trench bar (the lowest it goes)
        private const double RaisedHoodDeg = 30; // hood angle too tall for a trench bar
        private const double HalfLengthM = RobotCollision.FootprintLengthM / 2; // robot half length along its x, meters
        private const double HubFaceXM = 4.02; // field x of the blue hub's near face, meters
        private const double RedHubFarFaceXM = FieldFrame.FieldLengthM - HubFaceXM; // field x of the red hub's far
                                                                                      // face: the blue near face reflected
        private const double TrenchBarNearXM = 4.54; // field x of the blue trench bar's near face, meters
        private const double FrameS = 1.0 / 60; // seconds one frame stands for in the friction cases
        private const double SlidingMps = 3.0; // meters/s the robot slides along the wall in the friction cases
        private const double TurnAngleRad = Mathf.PI / 6; // 30 degrees, for the collision rotation cases
        private const string Prefix = "[Driver-Sim collision-selftest] "; // every log line starts with this

        [Tooltip("Collision resolver under test.")]
        [SerializeField] private RobotCollision collision;

        private int failureCount; // cases that failed so far

        /// <summary>Sets the resolver to test; call before the scene starts.</summary>
        /// <param name="robotCollision">Collision resolver.</param>
        public void SetReferences(RobotCollision robotCollision)
        {
            collision = robotCollision;
        }

        // ---- Unity messages

        private IEnumerator Start()
        {
            if (!CommandLine.HasFlag("--collision-selftest"))
            {
                yield break;
            }

            // let the physics world register the scene's colliders
            yield return new WaitForFixedUpdate();
            yield return null;

            RunCases();
            int exitCode = 0;
            if (failureCount > 0)
            {
                exitCode = 1;
            }
            Debug.Log(Prefix + "finished, failures: " + failureCount);
            Application.Quit(exitCode);
        }

        // ---- private stateful helpers

        /// <summary>Runs every case and logs its verdict.</summary>
        private void RunCases()
        {
            // clear of everything
            CollisionResult clear = ResolveStill(8.25, 4.0, 0, ClearHoodDeg);
            bool isClearUnchanged = IsAt(clear, 8.25, 4.0) && clear.PushedM == 0;
            Report("clear field is unchanged", isClearUnchanged);

            // blue hub, from the -x side: stops flush against the face
            CollisionResult hub = ResolveStill(HubFaceXM - HalfLengthM + 0.1, 4.02, 0, ClearHoodDeg);
            Report("blue hub from -x stops at the face", IsAt(hub, HubFaceXM - HalfLengthM, 4.02));

            // blue hub corner at 45 degrees: nothing overlaps afterwards
            double cornerHeadingRad = Mathf.PI / 4;
            CollisionResult corner = ResolveStill(3.72, 3.17, cornerHeadingRad, ClearHoodDeg);
            double cornerLeftM = collision.MeasureOverlap(corner.XFieldM, corner.YFieldM, cornerHeadingRad,
                ClearHoodDeg);
            string cornerDetail = "pushed " + corner.PushedM + " m, overlap left " + cornerLeftM + " m";
            Report("45 deg hub corner leaves no overlap", corner.PushedM > 0 && cornerLeftM < PositionToleranceM,
                cornerDetail);

            // alliance wall
            CollisionResult wall = ResolveStill(-0.2, 3.0, 0, ClearHoodDeg);
            Report("past x = 0 wall is pushed inside", IsAt(wall, HalfLengthM, 3.0));

            // corner between two walls
            double halfWidthM = RobotCollision.FootprintWidthM / 2;
            CollisionResult wallCorner = ResolveStill(-0.2, -0.2, 0, ClearHoodDeg);
            Report("corner is pushed out of both walls", IsAt(wallCorner, HalfLengthM, halfWidthM));

            // trench bar, hood down: passes under; hood up: pushed out and flagged
            CollisionResult underBar = ResolveStill(4.60, 0.634, 0, ClearHoodDeg);
            bool isPassing = IsAt(underBar, 4.60, 0.634) && !underBar.IsTrenchBlocked;
            Report("trench bar with hood down is passable", isPassing);
            CollisionResult blockedByBar = ResolveStill(4.60, 0.634, 0, RaisedHoodDeg);
            bool isBlocked = IsAt(blockedByBar, TrenchBarNearXM - HalfLengthM, 0.634) && blockedByBar.IsTrenchBlocked;
            Report("trench bar with hood up blocks", isBlocked);

            // red hub, from the +x side: the point-reflected twin of the blue hub
            CollisionResult redHub = ResolveStill(RedHubFarFaceXM + HalfLengthM - 0.1, 4.02, 0, ClearHoodDeg);
            Report("red hub from +x stops at the face", IsAt(redHub, RedHubFarFaceXM + HalfLengthM, 4.02));

            RunTurnCases();
            RunFrictionCases();
        }

        /// <summary>Collision rotation: a corner pushed into a wall swings flush, a flat hit does not turn.</summary>
        private void RunTurnCases()
        {
            // robot at +30 degrees pushed into the x = 0 wall: its heading must end nearer 0, not farther
            CollisionResult turnedLeft = ResolveStill(0.3, 3.0, TurnAngleRad, ClearHoodDeg);
            double leftDetail = turnedLeft.HeadingFieldRad;
            bool isNearerLeft = turnedLeft.HeadingFieldRad < TurnAngleRad - 0.001 && turnedLeft.HeadingFieldRad > 0;
            Report("+30 deg into the blue wall turns toward flush", isNearerLeft, "heading " + leftDetail + " rad");

            // the mirror image turns the other way
            CollisionResult turnedRight = ResolveStill(0.3, 3.0, -TurnAngleRad, ClearHoodDeg);
            double rightDetail = turnedRight.HeadingFieldRad;
            bool isNearerRight = turnedRight.HeadingFieldRad > -TurnAngleRad + 0.001 && turnedRight.HeadingFieldRad < 0;
            Report("-30 deg into the blue wall turns toward flush", isNearerRight, "heading " + rightDetail + " rad");

            // the turn is limited per frame
            double turnedRad = System.Math.Abs(turnedLeft.HeadingFieldRad - TurnAngleRad);
            Report("turn per frame is limited", turnedRad <= RobotCollision.MaxTurnRadPerFrame + 1e-6,
                "turned " + turnedRad + " rad");

            // a flat hit pushes at the middle of the bumper: no turn
            CollisionResult flat = ResolveStill(0.3, 3.0, 0, ClearHoodDeg);
            Report("flat hit into the wall does not turn", System.Math.Abs(flat.HeadingFieldRad) < 1e-4,
                "heading " + flat.HeadingFieldRad + " rad");

            // an unobstructed robot keeps its heading
            CollisionResult free = ResolveStill(8.25, 4.0, TurnAngleRad, ClearHoodDeg);
            Report("clear field keeps the heading", System.Math.Abs(free.HeadingFieldRad - TurnAngleRad) < 1e-9);
        }

        /// <summary>Wall friction: sliding along a wall it is pushed into loses speed, free sliding does not.</summary>
        private void RunFrictionCases()
        {
            // sliding along +y while pushed into the x = 0 wall: pulled back by what one frame of sliding covers
            double pushedM = HalfLengthM + 0.2;
            CollisionResult sliding = collision.Resolve(-0.2, 3.0, 0, ClearHoodDeg, 0, SlidingMps, FrameS);
            double expectedY = 3.0 - System.Math.Min(SlidingMps * FrameS, RobotCollision.WallFriction * pushedM);
            bool isSlowed = sliding.YFieldM < 3.0 - 0.01 && IsAt(sliding, HalfLengthM, expectedY);
            Report("sliding along a wall loses speed", isSlowed,
                "y " + sliding.YFieldM + " m, expected " + expectedY + " m");

            // friction is limited by how hard the robot is pushed into the wall
            double lightPushM = 0.01;
            double lightX = HalfLengthM - lightPushM;
            CollisionResult light = collision.Resolve(lightX, 3.0, 0, ClearHoodDeg, 0, SlidingMps, 1.0);
            double lightExpectedY = 3.0 - RobotCollision.WallFriction * lightPushM;
            Report("wall friction is limited by the push", IsAt(light, HalfLengthM, lightExpectedY),
                "y " + light.YFieldM + " m, expected " + lightExpectedY + " m");

            // driving straight into the wall has no sliding to stop
            CollisionResult straight = collision.Resolve(-0.2, 3.0, 0, ClearHoodDeg, -SlidingMps, 0, FrameS);
            Report("driving straight into a wall adds no slip", IsAt(straight, HalfLengthM, 3.0));

            // away from walls nothing slips
            CollisionResult open = collision.Resolve(8.25, 4.0, 0, ClearHoodDeg, 0, SlidingMps, FrameS);
            Report("free sliding is untouched", IsAt(open, 8.25, 4.0) && open.PushedM == 0);
        }

        /// <summary>Resolves a pose for a robot that is not moving (no wall friction).</summary>
        private CollisionResult ResolveStill(double xFieldM, double yFieldM, double headingFieldRad, double hoodDeg)
        {
            return collision.Resolve(xFieldM, yFieldM, headingFieldRad, hoodDeg, 0, 0, FrameS);
        }

        /// <summary>Logs one case's verdict and counts failures.</summary>
        /// <param name="caseName">Case description.</param>
        /// <param name="didPass">Whether the case passed.</param>
        /// <param name="detail">Extra text for the log line; may be empty.</param>
        private void Report(string caseName, bool didPass, string detail = "")
        {
            string verdict = "PASS";
            if (!didPass)
            {
                verdict = "FAIL";
                failureCount++;
            }
            string line = Prefix + verdict + " " + caseName;
            if (detail != "")
            {
                line = line + " (" + detail + ")";
            }
            Debug.Log(line);
        }

        // ---- low-level helpers (static, no side effects)

        /// <summary>Whether a result's position is within tolerance of a point.</summary>
        /// <param name="result">Resolved pose.</param>
        /// <param name="xFieldM">Expected x, field frame, meters.</param>
        /// <param name="yFieldM">Expected y, field frame, meters.</param>
        private static bool IsAt(CollisionResult result, double xFieldM, double yFieldM)
        {
            double dxM = System.Math.Abs(result.XFieldM - xFieldM);
            double dyM = System.Math.Abs(result.YFieldM - yFieldM);
            return dxM <= PositionToleranceM && dyM <= PositionToleranceM;
        }
    }
}
