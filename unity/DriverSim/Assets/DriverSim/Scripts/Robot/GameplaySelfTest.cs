using System.Collections;
using DriverSim.Field;
using DriverSim.Game;
using DriverSim.Util;
using UnityEngine;

namespace DriverSim.Robot
{
    /// <summary>
    /// Checks the fuel game without a robot sim: every row of the robot code's score table scores from its distance, a
    /// ferry shot does not, the intake takes a ball only with the arm down and the roller in, a full hopper refuses,
    /// the launcher's rate and gates, a shot into a hub's body bounces, scored fuel returns, and free practice counts
    /// only the player's own hub. Logs one <c>[Driver-Sim gameplay-selftest] PASS|FAIL name</c> line per case to the
    /// player log, then quits with exit code 0 when every case passed and 1 otherwise. Active only with the command
    /// line flag <c>--gameplay-selftest</c>.
    /// </summary>
    public sealed class GameplaySelfTest : MonoBehaviour
    {
        private const string Prefix = "[Driver-Sim gameplay-selftest] "; // every log line starts with this
        private const float FerryHoodDeg = 45; // the robot code's ferry hood angle, degrees
        private const float FerryRpm = 4000; // the robot code's ferry flywheel speed, RPM
        private const float FerryRangeM = 3; // meters from the hub the ferry shot starts
        private const float DownArmDeg = 15; // intake arm angle when deployed, robot-code degrees
        private const float UpArmDeg = 100; // intake arm angle when stowed, robot-code degrees
        private const float RollerInVolts = 8; // intake roller voltage when pulling in
        private const float FeedVolts = -6; // spindex feed voltage while feeding the launcher
        private const float TestRpm = 3000; // flywheel speed for the launcher cases, RPM
        private const float TestHoodDeg = 20; // hood angle for the launcher cases, degrees
        private const float LowRpm = 400; // flywheel speed below the launch gate, RPM
        private const float FrameS = 1f / 60; // seconds one simulated frame stands for in the launcher cases
        private const float FlightWaitS = 6; // seconds a ball may take to finish its flight in these cases
        private const float DropHeightM = 3; // meters above the carpet a dropped ball is released
        private const float BounceSpeedMps = 5; // meters/s a ball flies into the hub's body at
        private const float BounceStartHeightM = 1.4f; // meters above the carpet the hub-body shot starts
        private const float ReturnSlackS = 0.3f; // seconds of slack on the return delay
        private const float ReturnPlaceM = 0.3f; // meters a returned ball may be from the hub's exit

        [Tooltip("The fuel on the field.")]
        [SerializeField] private FuelField field;

        [Tooltip("Hopper and launcher under test.")]
        [SerializeField] private RobotFuel robotFuel;

        [Tooltip("Scores under test.")]
        [SerializeField] private Scoreboard scoreboard;

        private int failureCount; // cases that failed so far
        private int scoredEventCount; // balls that scored since the test began
        private float lastScoredTimeS; // Time.time of the last score event

        /// <summary>Sets the objects to test; call before the scene starts.</summary>
        /// <param name="fuelField">The fuel on the field.</param>
        /// <param name="fuel">Hopper and launcher.</param>
        /// <param name="scores">Scores.</param>
        public void SetReferences(FuelField fuelField, RobotFuel fuel, Scoreboard scores)
        {
            field = fuelField;
            robotFuel = fuel;
            scoreboard = scores;
        }

        // ---- Unity messages

        private IEnumerator Start()
        {
            if (!CommandLine.HasFlag("--gameplay-selftest"))
            {
                yield break;
            }

            // let the physics world and the field register
            yield return new WaitForFixedUpdate();
            yield return null;
            field.FuelScored += CountScore;
            field.ClearAll();

            RunTableCases();
            yield return RunFerryCase();
            yield return RunIntakeCases();
            RunLauncherCases();
            yield return RunHubBounceCase();
            yield return RunReturnCase();
            yield return RunScoringCases();

            int exitCode = 0;
            if (failureCount > 0)
            {
                exitCode = 1;
            }
            Debug.Log(Prefix + "finished, failures: " + failureCount);
            Application.Quit(exitCode);
        }

        // ---- private stateful helpers

        /// <summary>Counts a score event and notes when it happened.</summary>
        private void CountScore(bool isRedHub)
        {
            scoredEventCount++;
            lastScoredTimeS = Time.time;
        }

        /// <summary>Every row of the robot code's score table lands in the hub opening from its distance.</summary>
        private void RunTableCases()
        {
            HubFootprint[] hubs = field.GetHubs();
            Vector3 hubCenter = hubs[0].OpeningCenterWorld;
            ShotTableRow[] rows = ShotModel.MakeScoreTable();
            foreach (ShotTableRow row in rows)
            {
                // from the table's distance straight at the hub, robot standing still
                Vector3 start = hubCenter - new Vector3((float)row.DistanceM, 0, 0);
                start.y = ShotModel.LaunchHeightM;
                Vector3 velocity = ShotModel.ComputeLaunchVelocity(row.FlywheelRpm, row.HoodDeg, 0, 0, 0);
                FlightPlan plan = ShotModel.MakeFlightPlan(start, velocity, hubs);

                // where the free arc crosses the opening, without the aim assist
                float missM = plan.AssistOffsetWorldM.magnitude;
                bool isOnTarget = plan.AssistedHubIndex == 0 && missM <= ShotModel.OpeningRadiusM;
                FlightStep end = RunFlight(plan, hubs);
                bool isScored = end.Result == FlightResult.Scored && end.HubIndex == 0;
                string detail = string.Format("{0:F2} m at {1:F0} RPM, hood {2:F1} deg: {3:F2} m from the center",
                    row.DistanceM, row.FlywheelRpm, row.HoodDeg, missM);
                string distanceText = row.DistanceM.ToString("F2");
                Report("table row scores from " + distanceText + " m", isOnTarget && isScored, detail);
            }
        }

        /// <summary>A ferry shot (45 degree hood, 4000 RPM) from 3 m does not score.</summary>
        private IEnumerator RunFerryCase()
        {
            HubFootprint[] hubs = field.GetHubs();
            Vector3 start = hubs[0].OpeningCenterWorld - new Vector3(FerryRangeM, 0, 0);
            start.y = ShotModel.LaunchHeightM;
            Vector3 velocity = ShotModel.ComputeLaunchVelocity(FerryRpm, FerryHoodDeg, 0, 0, 0);
            int scoredBefore = scoredEventCount;
            FuelBall ball = field.Launch(start, velocity);
            yield return WaitForFlightEnd(ball);
            bool isResting = ball.GetState() == FuelState.Resting;
            Report("ferry shot from 3 m does not score", isResting && scoredEventCount == scoredBefore);
            field.ClearAll();
        }

        /// <summary>The intake takes a ball only with the arm down and the roller in; a full hopper refuses.</summary>
        private IEnumerator RunIntakeCases()
        {
            // a robot at the field center facing world +Z, a ball resting in the intake zone
            Vector3 robotPosition = Vector3.zero;
            Quaternion robotRotation = Quaternion.identity;
            Vector3 ballPosition = new Vector3(0, ShotModel.BallRadiusM, 0.59f);
            robotFuel.SetHopperCount(0);
            FuelBall ball = field.SpawnResting(ballPosition);
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();

            // arm up: nothing
            int armUp = robotFuel.RunIntake(robotPosition, robotRotation, UpArmDeg, RollerInVolts);
            Report("intake takes nothing with the arm up", armUp == 0 && ball.GetState() == FuelState.Resting);

            // roller off: nothing
            int rollerOff = robotFuel.RunIntake(robotPosition, robotRotation, DownArmDeg, 0);
            Report("intake takes nothing with the roller off", rollerOff == 0 && ball.GetState() == FuelState.Resting);

            // arm down and roller in: the ball goes into the hopper
            int taken = robotFuel.RunIntake(robotPosition, robotRotation, DownArmDeg, RollerInVolts);
            bool isInHopper = robotFuel.GetHopperCount() == 1 && ball.GetState() == FuelState.Hidden;
            Report("intake takes a ball with the arm down and the roller in", taken == 1 && isInHopper);
            field.ClearAll();

            // a full hopper leaves the ball where it is
            robotFuel.SetHopperCount(RobotFuel.Capacity);
            FuelBall refused = field.SpawnResting(ballPosition);
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
            int fullTaken = robotFuel.RunIntake(robotPosition, robotRotation, DownArmDeg, RollerInVolts);
            bool isStillThere = refused.GetState() == FuelState.Resting;
            bool isStillFull = robotFuel.GetHopperCount() == RobotFuel.Capacity;
            Report("full hopper refuses a ball", fullTaken == 0 && isStillThere && isStillFull);
            field.ClearAll();
        }

        /// <summary>The launcher fires 8 balls a second while feeding, and not below the RPM gate or without feed.</summary>
        private void RunLauncherCases()
        {
            Vector3 muzzle = new Vector3(0, ShotModel.LaunchHeightM, 0);

            // feeding at speed for one second
            robotFuel.SetHopperCount(RobotFuel.Capacity);
            int launched = 0;
            for (int frame = 0; frame < 60; frame++)
            {
                launched += robotFuel.RunLauncher(FrameS, FeedVolts, TestRpm, TestHoodDeg, muzzle, 0, 0, 0);
            }
            bool isRateRight = launched >= 7 && launched <= 9;
            bool isHopperDown = robotFuel.GetHopperCount() == RobotFuel.Capacity - launched;
            Report("launcher fires 8 balls a second", isRateRight && isHopperDown, launched + " balls in 1 s");
            field.ClearAll();

            // flywheel below the gate: jammed
            robotFuel.SetHopperCount(RobotFuel.Capacity);
            int jammed = 0;
            for (int frame = 0; frame < 30; frame++)
            {
                jammed += robotFuel.RunLauncher(FrameS, FeedVolts, LowRpm, TestHoodDeg, muzzle, 0, 0, 0);
            }
            Report("launcher does not fire below 500 RPM", jammed == 0);

            // spindex not feeding
            int idle = 0;
            for (int frame = 0; frame < 30; frame++)
            {
                idle += robotFuel.RunLauncher(FrameS, 0, TestRpm, TestHoodDeg, muzzle, 0, 0, 0);
            }
            Report("launcher does not fire without feed", idle == 0);

            // empty hopper
            robotFuel.SetHopperCount(0);
            int empty = 0;
            for (int frame = 0; frame < 30; frame++)
            {
                empty += robotFuel.RunLauncher(FrameS, FeedVolts, TestRpm, TestHoodDeg, muzzle, 0, 0, 0);
            }
            Report("launcher does not fire with an empty hopper", empty == 0);
        }

        /// <summary>A ball flying into a hub's body becomes a physics ball outside it, moving back.</summary>
        private IEnumerator RunHubBounceCase()
        {
            HubFootprint[] hubs = field.GetHubs();
            HubFootprint hub = hubs[0];
            Vector3 start = new Vector3(hub.MinXWorldM - 1.5f, BounceStartHeightM, hub.OpeningCenterWorld.z);
            Vector3 velocity = new Vector3(BounceSpeedMps, 0.5f, 0);
            int scoredBefore = scoredEventCount;
            FuelBall ball = field.Launch(start, velocity);
            yield return WaitForFlightEnd(ball);

            // read straight after the hit: the physics ball is outside the hub's face and moving away from it
            Vector3 position = ball.transform.position;
            Vector3 speed = ball.GetBody().linearVelocity;
            bool isOutside = !hub.IsOverBody(position) && position.x < hub.MinXWorldM;
            bool isMovingBack = speed.x < 0 && speed.x > -BounceSpeedMps;
            bool isResting = ball.GetState() == FuelState.Resting;
            string detail = string.Format("at x {0:F2}, moving {1:F2} m/s", position.x, speed.x);
            Report("shot into the hub body bounces", isResting && isOutside && isMovingBack
                && scoredEventCount == scoredBefore, detail);
            field.ClearAll();
        }

        /// <summary>A scored ball waits, then reappears at the hub's exit facing the field center.</summary>
        private IEnumerator RunReturnCase()
        {
            HubFootprint[] hubs = field.GetHubs();
            Vector3 center = hubs[0].OpeningCenterWorld;
            Vector3 start = center + new Vector3(0, DropHeightM, 0);
            int scoredBefore = scoredEventCount;
            FuelBall ball = field.Launch(start, Vector3.zero);
            float deadlineS = Time.time + FlightWaitS;
            while (scoredEventCount == scoredBefore && Time.time < deadlineS)
            {
                yield return null;
            }
            float scoredAtS = lastScoredTimeS;
            bool isHidden = ball.GetState() == FuelState.Hidden && field.GetReturningCount() == 1;

            // wait for the return, then check when and where it came back
            deadlineS = Time.time + FlightWaitS;
            while (ball.GetState() == FuelState.Hidden && Time.time < deadlineS)
            {
                yield return null;
            }
            float returnedAfterS = Time.time - scoredAtS;
            Vector3 exit = center + new Vector3(0.8f, 0, 0);
            float exitM = ShotModel.MeasureHorizontalDistance(ball.transform.position, exit);
            bool isBack = ball.GetState() == FuelState.Resting && field.GetReturningCount() == 0;
            bool isOnTime = Mathf.Abs(returnedAfterS - FuelField.ReturnDelayS) < ReturnSlackS;
            string detail = string.Format("back after {0:F2} s, {1:F2} m from the exit", returnedAfterS, exitM);
            Report("scored fuel returns from the hub", isHidden && isBack && isOnTime && exitM < ReturnPlaceM, detail);
            field.ClearAll();
        }

        /// <summary>Free practice counts a ball into the player's own hub and nothing into the other one.</summary>
        private IEnumerator RunScoringCases()
        {
            HubFootprint[] hubs = field.GetHubs();
            scoreboard.ResetScores();

            // the red hub first: no change
            Vector3 redDrop = hubs[1].OpeningCenterWorld + new Vector3(0, DropHeightM, 0);
            int scoredBefore = scoredEventCount;
            field.Launch(redDrop, Vector3.zero);
            float deadlineS = Time.time + FlightWaitS;
            while (scoredEventCount == scoredBefore && Time.time < deadlineS)
            {
                yield return null;
            }
            bool didRedScore = scoredEventCount > scoredBefore;
            Report("free practice counts nothing in the other hub", didRedScore && scoreboard.GetOurScore() == 0);

            // then ours: one point
            Vector3 blueDrop = hubs[0].OpeningCenterWorld + new Vector3(0, DropHeightM, 0);
            scoredBefore = scoredEventCount;
            field.Launch(blueDrop, Vector3.zero);
            deadlineS = Time.time + FlightWaitS;
            while (scoredEventCount == scoredBefore && Time.time < deadlineS)
            {
                yield return null;
            }
            Report("free practice counts a ball in our hub", scoreboard.GetOurScore() == 1);
            field.ClearAll();
        }

        /// <summary>Waits until a launched ball stops flying.</summary>
        private IEnumerator WaitForFlightEnd(FuelBall ball)
        {
            float deadlineS = Time.time + FlightWaitS;
            while (ball.GetState() == FuelState.Flying && Time.time < deadlineS)
            {
                yield return null;
            }
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

        /// <summary>Steps a flight in small increments until it ends or runs out of time.</summary>
        /// <param name="plan">The flight.</param>
        /// <param name="hubs">Both hubs.</param>
        /// <returns>The step that ended the flight.</returns>
        private static FlightStep RunFlight(FlightPlan plan, HubFootprint[] hubs)
        {
            const float stepS = 0.005f;
            const float maxS = 10;
            FlightStep step = new FlightStep(FlightResult.Flying, plan.StartWorld, plan.VelocityWorldMps, -1);
            float timeS = 0;
            while (step.Result == FlightResult.Flying && timeS < maxS)
            {
                step = ShotModel.StepFlight(plan, timeS, timeS + stepS, hubs);
                timeS += stepS;
            }
            return step;
        }
    }
}
