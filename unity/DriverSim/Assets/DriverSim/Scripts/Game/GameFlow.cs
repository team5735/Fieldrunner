using DriverSim.Field;
using DriverSim.Net;
using DriverSim.Util;
using UnityEngine;
using UnityEngine.InputSystem;

namespace DriverSim.Game
{
    /// <summary>
    /// The game's state machine: title, station picker, playing, pause, controls, controllers, options and the
    /// controller-lost screen. It turns menu choices into bridge commands (<c>station</c>, <c>resetPose</c>,
    /// <c>start</c>, <c>practice</c>, <c>pause</c>, <c>resume</c>, <c>abort</c>, <c>disable</c>), keeps in step with
    /// the bridge when the game connects mid-session, pauses when the driver controller disconnects, and handles the
    /// back key (Esc, or B on a controller) in menus. Starting a session respawns the fuel and clears the scores.
    /// Command line: <c>--start-in title|practice|match</c> (default title), <c>--station &lt;B1..R3&gt;</c>, and
    /// <c>--screenshot-menu title|station|controls|controllers|options|pause|disconnected|toast</c> to open a screen
    /// for captures.
    /// </summary>
    public sealed class GameFlow : MonoBehaviour
    {
        private const string StationKey = "DriverSim.station"; // PlayerPrefs: last chosen station index
        private const float SyncQuietS = 1f; // seconds after leaving a session that the bridge's old state is ignored
        private const float ToastSeconds = 3f; // how long an ordinary toast stays up
        private const float CaptureToastSeconds = 60f; // how long the capture toast stays up

        /// <summary>What the player is doing in a session.</summary>
        private enum SessionKind
        {
            None,
            Match,
            Practice,
        }

        [Tooltip("Link to the bridge.")]
        [SerializeField] private DriverSimLink link;

        [Tooltip("Controller and keyboard input.")]
        [SerializeField] private ControllerInput input;

        [Tooltip("Menu screens.")]
        [SerializeField] private MenuView view;

        [Tooltip("Overlay hidden while a menu is open.")]
        [SerializeField] private DebugHud hud;

        [Tooltip("The fuel on the field, respawned when a session starts.")]
        [SerializeField] private FuelField fuelField;

        [Tooltip("Hopper count, refilled when a session starts.")]
        [SerializeField] private RobotFuel robotFuel;

        [Tooltip("Scores, cleared when a session starts.")]
        [SerializeField] private Scoreboard scoreboard;

        private MenuScreen screen = MenuScreen.Title; // where the player is
        private MenuScreen returnScreen = MenuScreen.Title; // screen the Controls and Options screens go back to
        private MenuScreen controllersReturnScreen = MenuScreen.Title; // screen the Controllers screen goes back to
        private SessionKind session = SessionKind.None; // what the current play session is
        private SessionKind pendingPick = SessionKind.None; // what the station picker is picking for
        private int station; // the player's driver station, 0..5
        private bool hasSynced; // the first bridge match state has been acted on
        private bool hasRunStartIn; // the --start-in option has been carried out
        private bool hasOpenedCaptureScreen; // the --screenshot-menu screen has been opened
        private bool wasReady; // the bridge was ready at the last frame
        private float ignoreBridgeUntilS; // Time.unscaledTime until which the bridge's phase is not trusted

        /// <summary>Sets the objects the flow drives; call before the scene starts.</summary>
        /// <param name="driverSimLink">Link to the bridge.</param>
        /// <param name="controllerInput">Controller and keyboard input.</param>
        /// <param name="menuView">Menu screens.</param>
        /// <param name="debugHud">Overlay hidden while a menu is open.</param>
        public void SetReferences(
            DriverSimLink driverSimLink, ControllerInput controllerInput, MenuView menuView, DebugHud debugHud)
        {
            link = driverSimLink;
            input = controllerInput;
            view = menuView;
            hud = debugHud;
        }

        /// <summary>Sets the fuel game objects a session start resets; call before the scene starts.</summary>
        /// <param name="field">The fuel on the field.</param>
        /// <param name="fuel">Hopper count.</param>
        /// <param name="scores">Scores.</param>
        public void SetGameplayReferences(FuelField field, RobotFuel fuel, Scoreboard scores)
        {
            fuelField = field;
            robotFuel = fuel;
            scoreboard = scores;
        }

        /// <summary>Whether the current session is free practice.</summary>
        public bool IsPractice()
        {
            return session == SessionKind.Practice;
        }

        /// <summary>Whether the player is in the game itself (no menu open).</summary>
        public bool IsPlaying()
        {
            return screen == MenuScreen.None;
        }

        /// <summary>The player's driver station, 0..5.</summary>
        public int GetStation()
        {
            return station;
        }

        /// <summary>Opens the pause menu and tells the bridge to pause; ignored unless playing.</summary>
        public void OpenPause()
        {
            if (screen != MenuScreen.None)
            {
                return;
            }
            link.SendCommand("pause");
            input.StopRumble();
            ShowScreen(MenuScreen.Pause);
        }

        /// <summary>Puts every fuel ball back at its staged position (free practice only).</summary>
        public void RespawnFuel()
        {
            if (session == SessionKind.Practice)
            {
                fuelField.RespawnAll();
            }
        }

        /// <summary>Puts the robot back at its starting spot for the player's station.</summary>
        public void ResetRobot()
        {
            string startPoseCommand = DriverStations.MakeStartPoseCommand(station);
            link.SendCommand(startPoseCommand);
        }

        // ---- Unity messages

        private void Start()
        {
            station = PlayerPrefs.GetInt(StationKey, 0);
            string stationArgument = CommandLine.GetValue("--station");
            if (stationArgument != null)
            {
                station = ConvertToStation(stationArgument, station);
            }

            // input and menu events
            input.ControllerConnected += HandleControllerConnected;
            input.ActiveControllerLost += HandleControllerLost;
            input.UnknownJoystickFound += HandleUnknownJoystick;
            view.ButtonPressed += HandleButtonPressed;
            view.StationChosen += HandleStationChosen;

            // the title screen, or straight into the game when asked
            string startIn = CommandLine.GetValue("--start-in");
            if (startIn == null || startIn == "title")
            {
                ShowScreen(MenuScreen.Title);
                hasRunStartIn = true;
            }
            else
            {
                ShowScreen(MenuScreen.None);
            }
        }

        private void Update()
        {
            UpdateBridgeSync();
            UpdateStartIn();
            UpdateCaptureScreen();
            UpdateTitle();
            UpdateControllerLost();
            UpdateBackKey();
            UpdateRumble();
        }

        private void OnDestroy()
        {
            if (input != null)
            {
                input.ControllerConnected -= HandleControllerConnected;
                input.ActiveControllerLost -= HandleControllerLost;
                input.UnknownJoystickFound -= HandleUnknownJoystick;
            }
            if (view != null)
            {
                view.ButtonPressed -= HandleButtonPressed;
                view.StationChosen -= HandleStationChosen;
            }
        }

        // ---- private stateful helpers

        /// <summary>Shows a screen, and hides the overlay whenever a menu is open.</summary>
        /// <param name="newScreen">Screen to show.</param>
        private void ShowScreen(MenuScreen newScreen)
        {
            screen = newScreen;
            hud.SetVisible(newScreen == MenuScreen.None);
            view.SetPauseMode(session == SessionKind.Match);
            view.ShowScreen(newScreen, station);
        }

        /// <summary>
        /// Follows the bridge: the first match state decides whether to open the pause menu (the bridge is paused,
        /// for instance because this game was closed mid-match) or go straight into a running match; later, a match
        /// started from outside while on the title or station picker puts the player in the game.
        /// </summary>
        private void UpdateBridgeSync()
        {
            if (!link.HasMatchState())
            {
                return;
            }
            MatchState match = link.GetMatchState();
            bool isRunning = false;
            if (match.Phase == MatchPhase.Auto || match.Phase == MatchPhase.AutoToTeleop
                || match.Phase == MatchPhase.Teleop)
            {
                isRunning = true;
            }
            if (!hasSynced)
            {
                hasSynced = true;
                if (match.IsPaused)
                {
                    session = SessionKind.Practice;
                    if (match.Phase != MatchPhase.FreePlay)
                    {
                        session = SessionKind.Match;
                    }
                    hasRunStartIn = true;
                    ShowScreen(MenuScreen.Pause);
                }
                else if (isRunning)
                {
                    session = SessionKind.Match;
                    hasRunStartIn = true;
                    ShowScreen(MenuScreen.None);
                }
                return;
            }

            // a match started from outside (the probe tool, the sim GUI's field controls)
            bool isOnTitle = false;
            if (screen == MenuScreen.Title || screen == MenuScreen.StationPicker)
            {
                isOnTitle = true;
            }
            bool isTrusted = Time.unscaledTime >= ignoreBridgeUntilS;
            if (isOnTitle && isTrusted && isRunning && !match.IsPaused)
            {
                session = SessionKind.Match;
                StartGameplay();
                ShowScreen(MenuScreen.None);
            }
        }

        /// <summary>Carries out <c>--start-in practice|match</c> once the bridge is ready.</summary>
        private void UpdateStartIn()
        {
            if (hasRunStartIn || !hasSynced || !link.IsBridgeReady())
            {
                return;
            }
            hasRunStartIn = true;
            string startIn = CommandLine.GetValue("--start-in");
            if (startIn == "match")
            {
                BeginSession(SessionKind.Match);
            }
            else if (startIn == "practice")
            {
                // without the game owning input, the sim GUI or the self-test enables the robot itself
                if (link.IsGameInput())
                {
                    BeginSession(SessionKind.Practice);
                }
                else
                {
                    session = SessionKind.Practice;
                }
            }
        }

        /// <summary>Opens the screen named by <c>--screenshot-menu</c> once, when it can be shown.</summary>
        private void UpdateCaptureScreen()
        {
            if (hasOpenedCaptureScreen)
            {
                return;
            }
            string requested = CommandLine.GetValue("--screenshot-menu");
            if (requested == null)
            {
                hasOpenedCaptureScreen = true;
                return;
            }
            bool isSessionReady = false;
            if (screen == MenuScreen.None && hasRunStartIn)
            {
                isSessionReady = true;
            }
            if (requested == "title")
            {
                hasOpenedCaptureScreen = true;
            }
            else if (requested == "station")
            {
                hasOpenedCaptureScreen = true;
                pendingPick = SessionKind.Practice;
                ShowScreen(MenuScreen.StationPicker);
            }
            else if (requested == "disconnected")
            {
                hasOpenedCaptureScreen = true;
                ShowScreen(MenuScreen.ControllerLost);
            }
            else if (requested == "controllers")
            {
                hasOpenedCaptureScreen = true;
                OpenControllers();
            }
            else if (requested == "controls" || requested == "options")
            {
                hasOpenedCaptureScreen = true;
                returnScreen = screen;
                ShowScreen(MenuScreen.Controls);
                if (requested == "options")
                {
                    ShowScreen(MenuScreen.Options);
                }
            }
            else if (requested == "pause" && isSessionReady)
            {
                hasOpenedCaptureScreen = true;
                OpenPause();
            }
            else if (requested == "toast" && isSessionReady)
            {
                hasOpenedCaptureScreen = true;
                view.ShowToast("Controller connected: Xbox Wireless Controller", CaptureToastSeconds);
            }
        }

        /// <summary>Keeps the title screen's status lines current.</summary>
        private void UpdateTitle()
        {
            bool isReady = link.IsBridgeReady();
            if (screen != MenuScreen.Title)
            {
                wasReady = isReady;
                return;
            }

            // connection line
            string statusText = "waiting for robot sim...";
            if (link.IsProtocolMismatch())
            {
                statusText = "robot sim speaks another protocol version: " + link.GetBridgeInfo();
            }
            else if (isReady)
            {
                statusText = "robot sim connected";
            }
            string deviceText = "Driver: " + input.GetDriverDeviceName();

            // notice when the sim GUI owns the controller
            string noticeText = "";
            if (isReady && link.GetInputOwner() == "gui")
            {
                noticeText = "Controller input comes from the WPILib sim GUI (-PdriverSimGui)";
            }
            view.SetTitleState(isReady, statusText, deviceText, noticeText);

            // focus Start Match as soon as it becomes available
            if (isReady && !wasReady)
            {
                view.ShowScreen(MenuScreen.Title, station);
            }
            wasReady = isReady;
        }

        /// <summary>
        /// While the controller-lost screen shows: a reconnected controller or a key press returns to the pause menu,
        /// with the controller or the keyboard active respectively.
        /// </summary>
        private void UpdateControllerLost()
        {
            if (screen != MenuScreen.ControllerLost)
            {
                return;
            }
            if (input.IsDriverController())
            {
                ShowScreen(MenuScreen.Pause);
                return;
            }
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && keyboard.anyKey.wasPressedThisFrame)
            {
                ShowScreen(MenuScreen.Pause);
            }
        }

        /// <summary>Handles Esc (and B on a controller): pause when playing, go back one screen in a menu.</summary>
        private void UpdateBackKey()
        {
            ControlRebinder rebinder = input.GetRebinder();
            if (rebinder.IsRebinding() || rebinder.HasFinishedThisFrame())
            {
                return;
            }
            bool isEscape = false;
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null)
            {
                isEscape = keyboard.escapeKey.wasPressedThisFrame;
            }
            bool isGamepadBack = false;
            Gamepad gamepad = Gamepad.current;
            if (gamepad != null && screen != MenuScreen.None)
            {
                isGamepadBack = gamepad.buttonEast.wasPressedThisFrame;
            }
            if (!isEscape && !isGamepadBack)
            {
                return;
            }

            // playing: Esc pauses; menus: back one screen
            if (screen == MenuScreen.None)
            {
                if (isEscape)
                {
                    OpenPause();
                }
            }
            else if (screen == MenuScreen.StationPicker)
            {
                ShowScreen(MenuScreen.Title);
            }
            else if (screen == MenuScreen.Pause)
            {
                Resume();
            }
            else if (screen == MenuScreen.Controls || screen == MenuScreen.Options)
            {
                view.SetControlsBanner("");
                ShowScreen(returnScreen);
            }
            else if (screen == MenuScreen.Controllers)
            {
                CloseControllers();
            }
        }

        /// <summary>Runs the controller's rumble motors from the robot code while playing; stops them
        /// otherwise.</summary>
        private void UpdateRumble()
        {
            if (screen == MenuScreen.None && Application.isFocused && link.IsGameInput())
            {
                RumbleState rumble = link.GetRumble();
                input.SetRumble(rumble);
            }
            else
            {
                input.StopRumble();
            }
        }

        /// <summary>Starts a session in the player's station: station, start spot, then the match or free
        /// practice.</summary>
        /// <param name="kind">Match or free practice.</param>
        private void BeginSession(SessionKind kind)
        {
            string stationName = DriverStations.GetWireName(station);
            link.SendCommand("station " + stationName);
            string startPoseCommand = DriverStations.MakeStartPoseCommand(station);
            link.SendCommand(startPoseCommand);
            if (kind == SessionKind.Match)
            {
                link.SendCommand("start");
            }
            else
            {
                link.SendCommand("practice");
            }
            session = kind;
            StartGameplay();
            ShowScreen(MenuScreen.None);
        }

        /// <summary>Starts a session's fuel game: all fuel back on the field, the hopper preloaded, scores cleared.</summary>
        private void StartGameplay()
        {
            fuelField.RespawnAll();
            robotFuel.Preload();
            scoreboard.ResetScores();
        }

        /// <summary>Continues the paused session.</summary>
        private void Resume()
        {
            link.SendCommand("resume");
            ShowScreen(MenuScreen.None);
        }

        /// <summary>Ends the session and returns to the title screen.</summary>
        private void QuitToTitle()
        {
            if (session == SessionKind.Match)
            {
                link.SendCommand("abort");
            }
            else
            {
                link.SendCommand("disable");
            }
            session = SessionKind.None;
            ignoreBridgeUntilS = Time.unscaledTime + SyncQuietS;
            ShowScreen(MenuScreen.Title);
        }

        /// <summary>Opens the Controls or Options screen and remembers where to go back to.</summary>
        /// <param name="target">Controls or Options.</param>
        private void OpenFromMenu(MenuScreen target)
        {
            returnScreen = screen;
            ShowScreen(target);
        }

        /// <summary>Opens the Controllers screen and remembers where to go back to.</summary>
        private void OpenControllers()
        {
            controllersReturnScreen = screen;
            ShowScreen(MenuScreen.Controllers);
        }

        /// <summary>Leaves the Controllers screen; an empty driver slot is taken by the keyboard.</summary>
        private void CloseControllers()
        {
            input.CloseDriverSlot();
            ShowScreen(controllersReturnScreen);
        }

        /// <summary>Does what a menu button says.</summary>
        private void HandleButtonPressed(MenuButton button)
        {
            switch (button)
            {
                case MenuButton.StartMatch:
                    pendingPick = SessionKind.Match;
                    ShowScreen(MenuScreen.StationPicker);
                    break;
                case MenuButton.FreePractice:
                    pendingPick = SessionKind.Practice;
                    ShowScreen(MenuScreen.StationPicker);
                    break;
                case MenuButton.Controls:
                    OpenFromMenu(MenuScreen.Controls);
                    break;
                case MenuButton.Controllers:
                    OpenControllers();
                    break;
                case MenuButton.Options:
                    OpenFromMenu(MenuScreen.Options);
                    break;
                case MenuButton.Quit:
                    Application.Quit();
                    break;
                case MenuButton.StationBack:
                    ShowScreen(MenuScreen.Title);
                    break;
                case MenuButton.Resume:
                    Resume();
                    break;
                case MenuButton.RestartMatch:
                    link.SendCommand("abort");
                    BeginSession(SessionKind.Match);
                    break;
                case MenuButton.ResetRobot:
                    ResetRobot();
                    break;
                case MenuButton.QuitToTitle:
                    QuitToTitle();
                    break;
                case MenuButton.Back:
                    view.SetControlsBanner("");
                    ShowScreen(returnScreen);
                    break;
                case MenuButton.ControllersBack:
                    CloseControllers();
                    break;
            }
        }

        /// <summary>Starts the session the picker was opened for, in the chosen station.</summary>
        private void HandleStationChosen(int chosenStation)
        {
            station = chosenStation;
            PlayerPrefs.SetInt(StationKey, station);
            BeginSession(pendingPick);
        }

        /// <summary>Welcomes a controller with a toast; one that did not take the driver slot says how to drive with
        /// it.</summary>
        private void HandleControllerConnected(string name, bool isDriver)
        {
            string text = "Controller connected: " + name;
            if (!isDriver)
            {
                text = text + " - open Controllers to drive with it";
            }
            view.ShowToast(text, ToastSeconds);
        }

        /// <summary>Pauses at once when the driver controller is unplugged during play; on the Controllers screen
        /// the slot just stays empty.</summary>
        private void HandleControllerLost()
        {
            if (screen == MenuScreen.Controllers)
            {
                return;
            }
            input.CloseDriverSlot();
            if (screen == MenuScreen.None)
            {
                link.SendCommand("pause");
                ShowScreen(MenuScreen.ControllerLost);
            }
            else
            {
                view.ShowToast("Controller disconnected", ToastSeconds);
            }
        }

        /// <summary>Opens the Controls screen so the player can bind a controller the game does not
        /// recognize.</summary>
        private void HandleUnknownJoystick(string name)
        {
            if (screen == MenuScreen.None)
            {
                OpenPause();
            }
            if (screen == MenuScreen.Title || screen == MenuScreen.Pause || screen == MenuScreen.Controllers)
            {
                view.SetControlsBanner("New controller: bind its inputs (" + name + ")");
                OpenFromMenu(MenuScreen.Controls);
            }
        }

        // ---- low-level helpers (static, no side effects)

        /// <summary>A station index from a command-line value: "B1".."B3", "R1".."R3", or 0..5.</summary>
        /// <param name="text">Value given on the command line.</param>
        /// <param name="fallback">Station to use when the text is not one.</param>
        private static int ConvertToStation(string text, int fallback)
        {
            string wantedName = text.ToUpperInvariant();
            for (int index = 0; index < 6; index++)
            {
                string wireName = DriverStations.GetWireName(index);
                if (wireName == wantedName)
                {
                    return index;
                }
            }
            if (int.TryParse(text, out int parsed) && parsed >= 0 && parsed <= 5)
            {
                return parsed;
            }
            return fallback;
        }
    }
}
