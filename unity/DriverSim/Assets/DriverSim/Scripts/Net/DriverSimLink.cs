using System;
using System.Collections.Generic;
using DriverSim.Util;
using UnityEngine;
using UnityEngine.Rendering;

namespace DriverSim.Net
{
    /// <summary>
    /// The game's connection to the Fieldrunner bridge running inside the robot sim. Owns the NT4 client, decodes
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
        private const double TeleportJumpM = 0.5; // a position jump between two samples bigger than this is a reset
        private const int BatchModeFrameRate = 60; // frames per second when running without a window

        [Tooltip("NetworkTables server host; the robot sim runs on this machine by default.")]
        [SerializeField] private string ntHost = "localhost";

        private readonly List<RobotState> samples = new List<RobotState>(MaxSamples); // newest last
        private Nt4Client client; // NT4 connection; created in Awake
        private int commandPublisher; // /DriverSim/game/command publisher id
        private int correctionPublisher; // /DriverSim/game/poseCorrection publisher id
        private int joystickPublisher; // /DriverSim/game/joystick0 publisher id
        private long nextCommandSeq; // sequence number for the next command
        private double localMinusRobotS; // smoothed (local clock - robot clock), seconds
        private bool hasOffset; // localMinusRobotS has been initialized
        private RobotState robotState; // interpolated state for this frame
        private MatchState matchState; // newest match state
        private bool hasMatchState; // a match state has arrived on the current run
        private string bridgeInfo = ""; // bridge info text, e.g. "version=3 input=game"
        private BridgeInfo parsedBridgeInfo; // bridgeInfo decoded; default until it arrives
        private bool hasBridgeInfo; // bridgeInfo has arrived and parsed
        private RumbleState rumble = RumbleState.None; // newest rumble request from the robot code
        private float lastSampleTimeS = -1000; // Time.unscaledTime of the newest robot sample, seconds
        private bool isProtocolMismatch; // bridge speaks a different protocol version
        private int robotStateVersion = Protocol.Version; // protocol version carried by the newest robot state
        private bool hasSaidHello; // hello was sent on the current connection
        private int correctionCount; // pose corrections sent so far
        private double lastCorrectionTimestampS = double.NegativeInfinity; // robot clock of the last correction sent

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

        /// <summary>Number of pose corrections sent to the bridge so far.</summary>
        public int GetCorrectionCount()
        {
            return correctionCount;
        }

        /// <summary>Newest match state from the bridge.</summary>
        public MatchState GetMatchState()
        {
            return matchState;
        }

        /// <summary>Whether a match state has arrived from the bridge.</summary>
        public bool HasMatchState()
        {
            return hasMatchState;
        }

        /// <summary>Bridge info text, e.g. "version=3 input=game"; empty until received.</summary>
        public string GetBridgeInfo()
        {
            return bridgeInfo;
        }

        /// <summary>Whether the bridge has announced itself and speaks this game's protocol version.</summary>
        public bool IsBridgeReady()
        {
            if (!IsConnected() || !hasBridgeInfo)
            {
                return false;
            }
            return !isProtocolMismatch;
        }

        /// <summary>Whether the game supplies the controller values (the bridge's input owner is "game").</summary>
        public bool IsGameInput()
        {
            if (!hasBridgeInfo)
            {
                return false;
            }
            return parsedBridgeInfo.IsGameInput();
        }

        /// <summary>The bridge's input owner: "game", "gui" or "selftest"; empty until the info arrives.</summary>
        public string GetInputOwner()
        {
            if (!hasBridgeInfo)
            {
                return "";
            }
            return parsedBridgeInfo.InputOwner;
        }

        /// <summary>The robot code's newest controller rumble request.</summary>
        public RumbleState GetRumble()
        {
            return rumble;
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

        /// <summary>Sends the virtual controller reading to the bridge. Dropped while disconnected.</summary>
        /// <param name="state">Controller reading in WPILib Xbox conventions.</param>
        public void SendJoystick(JoystickState state)
        {
            if (client == null)
            {
                return;
            }
            double[] wire = state.ConvertToWire();
            client.SendDoubleArray(joystickPublisher, wire);
        }

        /// <summary>
        /// Tells the robot code where the robot should have been at a past instant, after the game resolved collisions.
        /// The bridge moves the robot code's pose to match. Dropped while disconnected; repeats of the same timestamp
        /// are not sent.
        /// </summary>
        /// <param name="timestampS">Robot-clock time of the reported state that was resolved, seconds.</param>
        /// <param name="xFieldM">Constrained robot x, field frame, meters.</param>
        /// <param name="yFieldM">Constrained robot y, field frame, meters.</param>
        public void SendPoseCorrection(double timestampS, double xFieldM, double yFieldM)
        {
            if (client == null || timestampS <= lastCorrectionTimestampS)
            {
                return;
            }
            lastCorrectionTimestampS = timestampS;
            correctionCount++;
            double[] correction = { timestampS, xFieldM, yFieldM };
            client.SendDoubleArray(correctionPublisher, correction);
        }

        // ---- Unity messages

        private void Awake()
        {
            // which GPU and graphics API the player picked, for checking that a discrete GPU is used
            string graphicsDevice = SystemInfo.graphicsDeviceName;
            GraphicsDeviceType graphicsApi = SystemInfo.graphicsDeviceType;
            Debug.Log("[Driver-Sim] graphics: " + graphicsDevice + " (" + graphicsApi + ")");

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
            correctionPublisher = client.Publish(Protocol.PoseCorrectionTopic, "double[]");
            joystickPublisher = client.Publish(Protocol.JoystickTopic, "double[]");
            client.Start();

            // without a window nothing limits the frame rate
            if (Application.isBatchMode)
            {
                Application.targetFrameRate = BatchModeFrameRate;
            }
        }

        private void Update()
        {
            // greet the bridge once per connection, after the publications have been announced
            UpdateHello();

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

        /// <summary>Sends hello once per connection, as soon as it is up (publications are announced by
        /// then).</summary>
        private void UpdateHello()
        {
            if (!client.IsConnected())
            {
                hasSaidHello = false;
                return;
            }
            if (!hasSaidHello)
            {
                SendCommand("hello");
                hasSaidHello = true;
            }
        }

        /// <summary>Routes one received value to the state it updates.</summary>
        private void HandleUpdate(Nt4Value update)
        {
            if (update.Topic == Protocol.RobotStateTopic)
            {
                double[] values = ConvertToDoubles(update.Value);
                if (RobotState.TryParse(values, out RobotState state))
                {
                    robotStateVersion = (int)values[RobotStateLayout.ProtocolVersion];
                    UpdateProtocolMismatch();
                    AddSample(state);
                }
            }
            else if (update.Topic == Protocol.MatchStateTopic)
            {
                double[] values = ConvertToDoubles(update.Value);
                if (MatchState.TryParse(values, out MatchState state))
                {
                    matchState = state;
                    hasMatchState = true;
                }
            }
            else if (update.Topic == Protocol.BridgeInfoTopic)
            {
                string info = update.Value as string;
                if (info != null)
                {
                    bridgeInfo = info;
                    hasBridgeInfo = BridgeInfo.TryParse(info, out parsedBridgeInfo);
                    UpdateProtocolMismatch();
                }
            }
            else if (update.Topic == Protocol.RumbleTopic)
            {
                double[] values = ConvertToDoubles(update.Value);
                if (values != null && values.Length >= 2)
                {
                    rumble = new RumbleState((float)values[0], (float)values[1]);
                }
            }
        }

        /// <summary>Flags a mismatch when the bridge's info or its robot state carries another protocol
        /// version.</summary>
        private void UpdateProtocolMismatch()
        {
            bool isMismatch = robotStateVersion != Protocol.Version;
            if (hasBridgeInfo && parsedBridgeInfo.Version != Protocol.Version)
            {
                isMismatch = true;
            }
            isProtocolMismatch = isMismatch;
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
                lastCorrectionTimestampS = double.NegativeInfinity;
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

        /// <summary>Robot state to draw at a local time: interpolated from history, never past the newest
        /// sample.</summary>
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
                    // a reset (the robot was placed somewhere else) is not motion: never draw the blend
                    double jumpXM = later.XFieldM - earlier.XFieldM;
                    double jumpYM = later.YFieldM - earlier.YFieldM;
                    double jumpM = System.Math.Sqrt(jumpXM * jumpXM + jumpYM * jumpYM);
                    if (jumpM > TeleportJumpM)
                    {
                        return later;
                    }
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
