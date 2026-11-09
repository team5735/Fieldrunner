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
            CollisionResult clear = collision.Resolve(8.25, 4.0, 0, ClearHoodDeg);
            bool isClearUnchanged = IsAt(clear, 8.25, 4.0) && clear.PushedM == 0;
            Report("clear field is unchanged", isClearUnchanged);

            // blue hub, from the -x side: stops flush against the face
            CollisionResult hub = collision.Resolve(HubFaceXM - HalfLengthM + 0.1, 4.02, 0, ClearHoodDeg);
            Report("blue hub from -x stops at the face", IsAt(hub, HubFaceXM - HalfLengthM, 4.02));

            // blue hub corner at 45 degrees: nothing overlaps afterwards
            double cornerHeadingRad = Mathf.PI / 4;
            CollisionResult corner = collision.Resolve(3.72, 3.17, cornerHeadingRad, ClearHoodDeg);
            double cornerLeftM = collision.MeasureOverlap(corner.XFieldM, corner.YFieldM, cornerHeadingRad,
                ClearHoodDeg);
            string cornerDetail = "pushed " + corner.PushedM + " m, overlap left " + cornerLeftM + " m";
            Report("45 deg hub corner leaves no overlap", corner.PushedM > 0 && cornerLeftM < PositionToleranceM,
                cornerDetail);

            // alliance wall
            CollisionResult wall = collision.Resolve(-0.2, 3.0, 0, ClearHoodDeg);
            Report("past x = 0 wall is pushed inside", IsAt(wall, HalfLengthM, 3.0));

            // corner between two walls
            double halfWidthM = RobotCollision.FootprintWidthM / 2;
            CollisionResult wallCorner = collision.Resolve(-0.2, -0.2, 0, ClearHoodDeg);
            Report("corner is pushed out of both walls", IsAt(wallCorner, HalfLengthM, halfWidthM));

            // trench bar, hood down: passes under; hood up: pushed out and flagged
            CollisionResult underBar = collision.Resolve(4.60, 0.634, 0, ClearHoodDeg);
            bool isPassing = IsAt(underBar, 4.60, 0.634) && !underBar.IsTrenchBlocked;
            Report("trench bar with hood down is passable", isPassing);
            CollisionResult blockedByBar = collision.Resolve(4.60, 0.634, 0, RaisedHoodDeg);
            bool isBlocked = IsAt(blockedByBar, TrenchBarNearXM - HalfLengthM, 0.634) && blockedByBar.IsTrenchBlocked;
            Report("trench bar with hood up blocks", isBlocked);

            // red hub, from the +x side: the point-reflected twin of the blue hub
            CollisionResult redHub = collision.Resolve(RedHubFarFaceXM + HalfLengthM - 0.1, 4.02, 0, ClearHoodDeg);
            Report("red hub from +x stops at the face", IsAt(redHub, RedHubFarFaceXM + HalfLengthM, 4.02));
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
