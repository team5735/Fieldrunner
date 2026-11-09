using System;
using System.Collections.Generic;
using DriverSim.Util;
using UnityEngine;

namespace DriverSim.Net
{
    /// <summary>
    /// The game's connection to the Driver-Sim bridge running inside the robot sim. Owns the NT4 client, decodes
    /// robot and match state, smooths the robot state for rendering, and sends game commands.
    ///
    /// Rendering reads <see cref="GetRobotState"/>, which is interpolated a little behind the newest sample so motion
    /// stays smooth despite network jitter. Override the server with the command line argument
    /// <c>--nt-host &lt;host&gt;</c>. With <c>--stop-sim-on-exit</c> (passed by simulateDriverSim), closing the game
    /// tells the robot sim to exit too.
    /// </summary>
    public sealed class DriverSimLink : MonoBehaviour
    {
        private const int NtPort = 5810; // NT4 server port for robot code / WPILib sim
        private const double SubscribePeriodS = 0.01; // seconds between server sends
        private const double RenderDelayS = 0.05; // seconds the rendered state trails the newest sample
        private const double OffsetSmoothing = 0.05; // per-sample weight when tracking the clock offset
        private const int MaxSamples = 64; // robot-state history kept for interpolation
        private const float StaleAfterS = 1f; // seconds without a sample before the robot state counts as stale
        private const int QuitSendWaitMs = 300; // milliseconds to let the quit command go out before exiting

        [Tooltip("NetworkTables server host; the robot sim runs on this machine by default.")]
        [SerializeField] private string ntHost = "localhost";

        private readonly List<RobotState> samples = new List<RobotState>(MaxSamples); // newest last
        private Nt4Client client; // NT4 connection; created in Awake
        private int commandPublisher; // /DriverSim/game/command publisher id
        private int nudgePublisher; // /DriverSim/game/poseNudge publisher id
        private long nextCommandSeq; // sequence number for the next command
        private double localMinusRobotS; // smoothed (local clock - robot clock), seconds
        private bool hasOffset; // localMinusRobotS has been initialized
        private RobotState robotState; // interpolated state for this frame
        private MatchState matchState; // newest match state
        private string bridgeInfo = ""; // bridge protocol string
        private float lastSampleTimeS = -1000; // Time.unscaledTime of the newest robot sample, seconds
        private bool isProtocolMismatch; // bridge speaks a different protocol version

        /// <summary>Whether the game is connected to the robot sim's NetworkTables server.</summary>
        public bool IsConnected()
        {
            if (client == null)
            {
                return false;
            }
            return client.IsConnected();
        }

        /// <summary>Whether robot state has arrived within the last second.</summary>
        public bool HasRobotState()
        {
            float ageS = Time.unscaledTime - lastSampleTimeS;
            return samples.Count > 0 && ageS < StaleAfterS;
        }

        /// <summary>Robot state to draw this frame (interpolated).</summary>
        public RobotState GetRobotState()
        {
            return robotState;
        }

        /// <summary>Newest match state from the bridge.</summary>
        public MatchState GetMatchState()
        {
            return matchState;
        }

        /// <summary>Bridge protocol string; empty until received.</summary>
        public string GetBridgeInfo()
        {
            return bridgeInfo;
        }

        /// <summary>NetworkTables server host the game connects to.</summary>
        public string GetNtHost()
        {
            return ntHost;
        }

        /// <summary>Whether the bridge speaks a different protocol version than this game.</summary>
        public bool IsProtocolMismatch()
        {
            return isProtocolMismatch;
        }

        /// <summary>
        /// Sends one command to the bridge, e.g. "start", "station R2", "resetPose 2 4 0". Dropped while disconnected.
        /// </summary>
        /// <param name="verbAndArgs">Command text without its sequence number.</param>
        public void SendCommand(string verbAndArgs)
        {
            if (client == null)
            {
                return;
            }
            string wire = nextCommandSeq + " " + verbAndArgs;
            nextCommandSeq++;
            client.SendString(commandPublisher, wire);
        }

        /// <summary>
        /// Pushes the robot by a small amount, e.g. out of a wall it drove into. Added to the robot's current pose.
        /// </summary>
        /// <param name="dxFieldM">Field-frame x change, meters.</param>
        /// <param name="dyFieldM">Field-frame y change, meters.</param>
        /// <param name="dHeadingRad">Heading change, radians, counter-clockwise positive.</param>
        public void SendPoseNudge(double dxFieldM, double dyFieldM, double dHeadingRad)
        {
            if (client == null)
            {
                return;
            }
            double[] nudge = { dxFieldM, dyFieldM, dHeadingRad };
            client.SendDoubleArray(nudgePublisher, nudge);
        }

        // ---- Unity messages

        private void Awake()
        {
            // server host from the command line
            string hostArgument = CommandLine.GetValue("--nt-host");
            if (!string.IsNullOrEmpty(hostArgument))
            {
                ntHost = hostArgument;
            }

            // sequence numbers start from wall-clock milliseconds so a restarted game keeps counting upward
            DateTimeOffset now = DateTimeOffset.UtcNow;
            nextCommandSeq = now.ToUnixTimeMilliseconds();

            // connect
            client = new Nt4Client(ntHost, NtPort, "DriverSim-game");
            client.SubscribePrefix(Protocol.Prefix, SubscribePeriodS);
            commandPublisher = client.Publish(Protocol.CommandTopic, "string");
            nudgePublisher = client.Publish(Protocol.PoseNudgeTopic, "double[]");
            client.Start();
        }

        private void Update()
        {
            // take everything that arrived since last frame
            while (client.TryDequeue(out Nt4Value update))
            {
                HandleUpdate(update);
            }

            // interpolate the robot state for this frame
            double localNowS = Time.unscaledTimeAsDouble;
            robotState = ComputeRenderState(localNowS);
        }

        private void OnApplicationQuit()
        {
            // launched by ./gradlew simulateDriverSim: closing the game ends the whole session cleanly
            bool shouldStopSim = CommandLine.HasFlag("--stop-sim-on-exit");
            if (shouldStopSim && IsConnected())
            {
                SendCommand("quit");
                System.Threading.Thread.Sleep(QuitSendWaitMs);
            }
        }

        private void OnDestroy()
        {
            if (client != null)
            {
                client.Dispose();
                client = null;
            }
        }

        // ---- private stateful helpers

        /// <summary>Routes one received value to the state it updates.</summary>
        private void HandleUpdate(Nt4Value update)
        {
            if (update.Topic == Protocol.RobotStateTopic)
            {
                double[] values = ConvertToDoubles(update.Value);
                if (RobotState.TryParse(values, out RobotState state))
                {
                    int version = (int)values[RobotStateLayout.ProtocolVersion];
                    isProtocolMismatch = version != Protocol.Version;
                    AddSample(state);
                }
            }
            else if (update.Topic == Protocol.MatchStateTopic)
            {
                double[] values = ConvertToDoubles(update.Value);
                if (MatchState.TryParse(values, out MatchState state))
                {
                    matchState = state;
                }
            }
            else if (update.Topic == Protocol.BridgeInfoTopic)
            {
                string info = update.Value as string;
                if (info != null)
                {
                    bridgeInfo = info;
                }
            }
        }

        /// <summary>Adds a robot-state sample to the history and updates the clock offset estimate.</summary>
        private void AddSample(RobotState state)
        {
            // a robot restart sends the clock backwards: start over
            bool hasSamples = samples.Count > 0;
            if (hasSamples && state.TimestampS < samples[samples.Count - 1].TimestampS)
            {
                samples.Clear();
                hasOffset = false;
            }

            // keep a bounded history
            samples.Add(state);
            if (samples.Count > MaxSamples)
            {
                samples.RemoveAt(0);
            }
            lastSampleTimeS = Time.unscaledTime;

            // track the offset between our clock and the robot's
            double measuredOffsetS = Time.unscaledTimeAsDouble - state.TimestampS;
            if (!hasOffset)
            {
                localMinusRobotS = measuredOffsetS;
                hasOffset = true;
            }
            else
            {
                localMinusRobotS += (measuredOffsetS - localMinusRobotS) * OffsetSmoothing;
            }
        }

        /// <summary>Robot state to draw at a local time: interpolated from history, never past the newest sample.</summary>
        private RobotState ComputeRenderState(double localNowS)
        {
            if (samples.Count == 0)
            {
                return robotState;
            }
            RobotState newest = samples[samples.Count - 1];
            RobotState oldest = samples[0];
            if (samples.Count == 1)
            {
                return newest;
            }

            // render time on the robot's clock
            double renderRobotS = localNowS - localMinusRobotS - RenderDelayS;
            if (renderRobotS >= newest.TimestampS)
            {
                return newest;
            }
            if (renderRobotS <= oldest.TimestampS)
            {
                return oldest;
            }

            // blend the bracketing pair
            for (int i = samples.Count - 1; i > 0; i--)
            {
                RobotState earlier = samples[i - 1];
                RobotState later = samples[i];
                if (renderRobotS >= earlier.TimestampS)
                {
                    double spanS = later.TimestampS - earlier.TimestampS;
                    double t = 0;
                    if (spanS > 1e-9)
                    {
                        t = (renderRobotS - earlier.TimestampS) / spanS;
                    }
                    return RobotState.Interpolate(earlier, later, t);
                }
            }
            return newest;
        }

        // ---- low-level helpers (static, no side effects)

        /// <summary>Converts a decoded NT array to doubles.</summary>
        /// <returns>The numbers, or null when the value is not an array.</returns>
        private static double[] ConvertToDoubles(object value)
        {
            object[] items = value as object[];
            if (items == null)
            {
                return null;
            }
            double[] result = new double[items.Length];
            for (int i = 0; i < items.Length; i++)
            {
                result[i] = MsgPackReader.ConvertToDouble(items[i]);
            }
            return result;
        }
    }
}
