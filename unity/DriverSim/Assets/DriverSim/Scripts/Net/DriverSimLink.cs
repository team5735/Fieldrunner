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
    /// Rendering reads <see cref="Robot"/>, which is interpolated a little behind the newest sample so motion stays
    /// smooth despite network jitter. Override the server with the command line argument
    /// <c>--nt-host &lt;host&gt;</c>.
    /// </summary>
    public sealed class DriverSimLink : MonoBehaviour
    {
        private const int NtPort = 5810; // NT4 server port for robot code / WPILib sim
        private const double SubscribePeriodS = 0.01; // seconds between server sends
        private const double RenderDelayS = 0.05; // how far behind the newest sample we render
        private const double OffsetSmoothing = 0.05; // per-sample weight when tracking the clock offset
        private const int MaxSamples = 64; // robot-state history kept for interpolation

        [Tooltip("NetworkTables server host; the robot sim runs on this machine by default.")]
        public string ntHost = "localhost";

        private Nt4Client client; // NT4 connection
        private int commandPublisher; // /DriverSim/game/command
        private int nudgePublisher; // /DriverSim/game/poseNudge
        private long nextCommandSeq; // sequence number for the next command
        private readonly List<RobotState> samples = new List<RobotState>(MaxSamples); // newest last
        private double localMinusRobotS; // smoothed (local clock - robot clock), seconds
        private bool hasOffset; // localMinusRobotS has been initialized
        private RobotState robot; // interpolated state for this frame
        private MatchState match; // newest match state
        private string bridgeInfo = ""; // bridge protocol string
        private float lastRobotSampleTime = -1000; // Time.unscaledTime of the newest robot sample

        /// <summary>True while connected to the robot sim's NetworkTables server.</summary>
        public bool IsConnected
        {
            get { return client != null && client.IsConnected; }
        }

        /// <summary>True when robot state has arrived within the last second.</summary>
        public bool HasRobotState
        {
            get { return samples.Count > 0 && Time.unscaledTime - lastRobotSampleTime < 1f; }
        }

        /// <summary>Robot state to draw this frame (interpolated).</summary>
        public RobotState Robot
        {
            get { return robot; }
        }

        /// <summary>Newest match state.</summary>
        public MatchState Match
        {
            get { return match; }
        }

        /// <summary>Bridge protocol string, empty until received.</summary>
        public string BridgeInfo
        {
            get { return bridgeInfo; }
        }

        /// <summary>True when the bridge speaks a different protocol version than this game.</summary>
        public bool IsProtocolMismatch { get; private set; }

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
        /// <param name="dxM">Field-frame x change, meters.</param>
        /// <param name="dyM">Field-frame y change, meters.</param>
        /// <param name="dHeadingRad">Heading change, radians.</param>
        public void SendPoseNudge(double dxM, double dyM, double dHeadingRad)
        {
            if (client == null)
            {
                return;
            }
            client.SendDoubleArray(nudgePublisher, new[] { dxM, dyM, dHeadingRad });
        }

        private void Awake()
        {
            string hostArgument = CommandLine.GetValue("--nt-host");
            if (!string.IsNullOrEmpty(hostArgument))
            {
                ntHost = hostArgument;
            }

            // sequence numbers start from wall-clock milliseconds so a restarted game keeps counting upward
            nextCommandSeq = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

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
            robot = ComputeRenderState(Time.unscaledTimeAsDouble);
        }

        private void OnDestroy()
        {
            if (client != null)
            {
                client.Dispose();
                client = null;
            }
        }

        private void HandleUpdate(Nt4Value update)
        {
            if (update.Topic == Protocol.RobotStateTopic)
            {
                double[] values = ToDoubles(update.Value);
                if (RobotState.TryParse(values, out RobotState state))
                {
                    IsProtocolMismatch = (int)values[RobotStateLayout.ProtocolVersion] != Protocol.Version;
                    AddSample(state);
                }
            }
            else if (update.Topic == Protocol.MatchStateTopic)
            {
                double[] values = ToDoubles(update.Value);
                if (MatchState.TryParse(values, out MatchState state))
                {
                    match = state;
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

        private void AddSample(RobotState state)
        {
            // a robot restart sends the clock backwards: start over
            if (samples.Count > 0 && state.TimestampS < samples[samples.Count - 1].TimestampS)
            {
                samples.Clear();
                hasOffset = false;
            }

            samples.Add(state);
            if (samples.Count > MaxSamples)
            {
                samples.RemoveAt(0);
            }
            lastRobotSampleTime = Time.unscaledTime;

            // track the offset between our clock and the robot's
            double measured = Time.unscaledTimeAsDouble - state.TimestampS;
            if (!hasOffset)
            {
                localMinusRobotS = measured;
                hasOffset = true;
            }
            else
            {
                localMinusRobotS += (measured - localMinusRobotS) * OffsetSmoothing;
            }
        }

        private RobotState ComputeRenderState(double localNowS)
        {
            if (samples.Count == 0)
            {
                return robot;
            }
            RobotState newest = samples[samples.Count - 1];
            if (samples.Count == 1)
            {
                return newest;
            }

            // render time on the robot's clock, never past the newest sample
            double renderRobotS = localNowS - localMinusRobotS - RenderDelayS;
            if (renderRobotS >= newest.TimestampS)
            {
                return newest;
            }
            if (renderRobotS <= samples[0].TimestampS)
            {
                return samples[0];
            }

            // find the bracketing pair
            for (int i = samples.Count - 1; i > 0; i--)
            {
                RobotState earlier = samples[i - 1];
                RobotState later = samples[i];
                if (renderRobotS >= earlier.TimestampS)
                {
                    double span = later.TimestampS - earlier.TimestampS;
                    double t = 0;
                    if (span > 1e-9)
                    {
                        t = (renderRobotS - earlier.TimestampS) / span;
                    }
                    return RobotState.Lerp(earlier, later, t);
                }
            }
            return newest;
        }

        private static double[] ToDoubles(object value)
        {
            object[] items = value as object[];
            if (items == null)
            {
                return null;
            }
            double[] result = new double[items.Length];
            for (int i = 0; i < items.Length; i++)
            {
                result[i] = MsgPackReader.ToDouble(items[i]);
            }
            return result;
        }
    }
}
