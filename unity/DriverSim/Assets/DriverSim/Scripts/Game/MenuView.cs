using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace DriverSim.Game
{
    /// <summary>Which full-screen menu is showing; None means the game itself.</summary>
    public enum MenuScreen
    {
        None,
        Title,
        StationPicker,
        Pause,
        Controls,
        Controllers,
        Options,
        ControllerLost,
    }

    /// <summary>A button the player can press on the title, station, pause, controls, controllers and options
    /// screens.</summary>
    public enum MenuButton
    {
        StartMatch,
        FreePractice,
        Controls,
        Controllers,
        Options,
        Quit,
        StationBack,
        Resume,
        RestartMatch,
        ResetRobot,
        QuitToTitle,
        Back,
        ControllersBack,
    }

    /// <summary>
    /// The menu screens, drawn with UI Toolkit from DriverSim.uxml and DriverSim.uss: shows one screen at a time,
    /// focuses its first control so a controller or the keyboard can drive it, and reports button presses. It holds
    /// no game logic; <see cref="GameFlow"/> decides what each button does.
    /// </summary>
    public sealed class MenuView : MonoBehaviour
    {
        private const long FocusRetryMs = 50; // milliseconds between attempts to focus a new screen's first control

        [Tooltip("Document holding DriverSim.uxml.")]
        [SerializeField] private UIDocument document;

        [Tooltip("Controller input, for the Controls screen.")]
        [SerializeField] private ControllerInput input;

        [Tooltip("Display and camera settings, for the Options screen.")]
        [SerializeField] private GameOptions options;

        private readonly Dictionary<MenuScreen, VisualElement> screens =
            new Dictionary<MenuScreen, VisualElement>(); // screen element for each menu screen
        private readonly List<Button> stationButtons = new List<Button>(); // the six station buttons, by index
        private VisualElement root; // the document's root element
        private Button startMatchButton; // title: Start Match
        private Button freePracticeButton; // title: Free Practice
        private Button restartMatchButton; // pause: Restart Match
        private Label titleStatus; // title: robot sim connection line
        private Label titleDevice; // title: active input device line
        private Label titleNotice; // title: input-owner notice
        private Label toast; // bottom-of-screen message
        private ControlsView controlsView; // Controls screen logic
        private ControllersView controllersView; // Controllers screen logic
        private OptionsView optionsView; // Options screen logic
        private MenuScreen shownScreen = MenuScreen.None; // screen currently showing
        private int focusRequestId; // counts focus requests, so an older retry loop stops when a newer one starts
        private float toastEndS; // Time.unscaledTime when the toast disappears

        /// <summary>Raised when a menu button is pressed.</summary>
        public event Action<MenuButton> ButtonPressed;

        /// <summary>Raised with the station index (0..5) when a station is chosen.</summary>
        public event Action<int> StationChosen;

        /// <summary>Sets the objects the screens use; call before the scene starts.</summary>
        /// <param name="uiDocument">Document holding the menu layout.</param>
        /// <param name="controllerInput">Controller input, for the Controls screen.</param>
        /// <param name="gameOptions">Settings, for the Options screen.</param>
        public void SetReferences(UIDocument uiDocument, ControllerInput controllerInput, GameOptions gameOptions)
        {
            document = uiDocument;
            input = controllerInput;
            options = gameOptions;
        }

        /// <summary>The screen currently showing.</summary>
        public MenuScreen GetScreen()
        {
            return shownScreen;
        }

        /// <summary>Shows one screen (None shows the game) and focuses its first control.</summary>
        /// <param name="screen">Screen to show.</param>
        /// <param name="preferredStation">Station button to focus on the station picker, 0..5.</param>
        public void ShowScreen(MenuScreen screen, int preferredStation)
        {
            EnsureBuilt();
            shownScreen = screen;
            foreach (KeyValuePair<MenuScreen, VisualElement> pair in screens)
            {
                bool isShown = pair.Key == screen;
                pair.Value.style.display = ConvertToDisplay(isShown);
            }
            if (screen == MenuScreen.Controls)
            {
                controlsView.Refresh();
            }
            if (screen == MenuScreen.Controllers)
            {
                controllersView.Refresh();
            }
            if (screen == MenuScreen.Options)
            {
                optionsView.Refresh();
            }

            // focus once the screen has been laid out: retry until the control has taken it
            focusRequestId++;
            int requestId = focusRequestId;
            VisualElement target = FindFocusTarget(screen, preferredStation);
            if (target != null)
            {
                IVisualElementScheduledItem focusItem = target.schedule.Execute(() => target.Focus());
                focusItem.Every(FocusRetryMs).Until(() => IsFocusSettled(target, requestId));
            }
            else
            {
                BlurFocused();
            }
        }

        /// <summary>Updates the title screen's status lines and enables its game buttons when the bridge is
        /// ready.</summary>
        /// <param name="isReady">Whether the robot sim's bridge is connected and compatible.</param>
        /// <param name="statusText">Connection line.</param>
        /// <param name="deviceText">Driver device line.</param>
        /// <param name="noticeText">Extra notice; empty for none.</param>
        public void SetTitleState(bool isReady, string statusText, string deviceText, string noticeText)
        {
            EnsureBuilt();
            startMatchButton.SetEnabled(isReady);
            freePracticeButton.SetEnabled(isReady);
            titleStatus.text = statusText;
            titleDevice.text = deviceText;
            titleNotice.text = noticeText;
        }

        /// <summary>Shows or hides the pause menu's Restart Match button (matches only).</summary>
        /// <param name="isMatch">True during a match.</param>
        public void SetPauseMode(bool isMatch)
        {
            EnsureBuilt();
            restartMatchButton.style.display = ConvertToDisplay(isMatch);
        }

        /// <summary>Shows a line near the bottom of the screen for a while.</summary>
        /// <param name="text">Message.</param>
        /// <param name="durationS">Seconds to keep it up.</param>
        public void ShowToast(string text, float durationS)
        {
            EnsureBuilt();
            toast.text = text;
            toast.AddToClassList("visible");
            toastEndS = Time.unscaledTime + durationS;
        }

        /// <summary>Shows a line above the Controls screen's schematic; empty hides it.</summary>
        /// <param name="text">Line to show.</param>
        public void SetControlsBanner(string text)
        {
            EnsureBuilt();
            controlsView.SetBanner(text);
        }

        // ---- Unity messages

        private void OnEnable()
        {
            EnsureBuilt();
        }

        private void Update()
        {
            if (root == null)
            {
                return;
            }
            if (toast.ClassListContains("visible") && Time.unscaledTime >= toastEndS)
            {
                toast.RemoveFromClassList("visible");
            }
            if (shownScreen == MenuScreen.Controls)
            {
                controlsView.UpdateView();
            }
            if (shownScreen == MenuScreen.Controllers)
            {
                controllersView.UpdateView();
            }
        }

        // ---- private stateful helpers

        /// <summary>Finds the elements and wires the buttons the first time the document is available.</summary>
        private void EnsureBuilt()
        {
            if (root != null || document == null)
            {
                return;
            }
            root = document.rootVisualElement;
            if (root == null)
            {
                return;
            }

            // screens
            screens[MenuScreen.Title] = root.Q<VisualElement>("screen-title");
            screens[MenuScreen.StationPicker] = root.Q<VisualElement>("screen-station");
            screens[MenuScreen.Pause] = root.Q<VisualElement>("screen-pause");
            screens[MenuScreen.Controls] = root.Q<VisualElement>("screen-controls");
            screens[MenuScreen.Controllers] = root.Q<VisualElement>("screen-controllers");
            screens[MenuScreen.Options] = root.Q<VisualElement>("screen-options");
            screens[MenuScreen.ControllerLost] = root.Q<VisualElement>("screen-disconnected");

            // title
            startMatchButton = Wire("btn-start-match", MenuButton.StartMatch);
            freePracticeButton = Wire("btn-free-practice", MenuButton.FreePractice);
            Wire("btn-controls", MenuButton.Controls);
            Wire("btn-controllers", MenuButton.Controllers);
            Wire("btn-options", MenuButton.Options);
            Wire("btn-quit", MenuButton.Quit);
            titleStatus = root.Q<Label>("title-status");
            titleDevice = root.Q<Label>("title-device");
            titleNotice = root.Q<Label>("title-notice");

            // station picker
            for (int station = 0; station < 6; station++)
            {
                Button stationButton = root.Q<Button>("btn-station-" + station);
                int chosen = station;
                stationButton.clicked += () => HandleStationClicked(chosen);
                stationButtons.Add(stationButton);
            }
            Wire("btn-station-back", MenuButton.StationBack);

            // pause
            Wire("btn-resume", MenuButton.Resume);
            restartMatchButton = Wire("btn-restart-match", MenuButton.RestartMatch);
            Wire("btn-reset-robot", MenuButton.ResetRobot);
            Wire("btn-pause-controls", MenuButton.Controls);
            Wire("btn-pause-controllers", MenuButton.Controllers);
            Wire("btn-pause-options", MenuButton.Options);
            Wire("btn-quit-to-title", MenuButton.QuitToTitle);

            // controls, controllers and options
            Button resetAll = root.Q<Button>("btn-controls-reset-all");
            resetAll.clicked += HandleResetAllClicked;
            Wire("btn-controls-back", MenuButton.Back);
            Wire("btn-controllers-back", MenuButton.ControllersBack);
            Wire("btn-options-back", MenuButton.Back);
            controlsView = new ControlsView(screens[MenuScreen.Controls], input);
            controllersView = new ControllersView(screens[MenuScreen.Controllers], input);
            optionsView = new OptionsView(screens[MenuScreen.Options], options);

            toast = root.Q<Label>("toast");
            ShowScreen(MenuScreen.None, 0);
        }

        /// <summary>Connects a named button to the <see cref="ButtonPressed"/> event.</summary>
        private Button Wire(string name, MenuButton which)
        {
            Button button = root.Q<Button>(name);
            button.clicked += () => HandleButtonClicked(which);
            return button;
        }

        /// <summary>Reports a button press.</summary>
        private void HandleButtonClicked(MenuButton which)
        {
            if (ButtonPressed != null)
            {
                ButtonPressed(which);
            }
        }

        /// <summary>Reports a station choice.</summary>
        private void HandleStationClicked(int station)
        {
            if (StationChosen != null)
            {
                StationChosen(station);
            }
        }

        /// <summary>Puts every binding back to its default and redraws the Controls screen.</summary>
        private void HandleResetAllClicked()
        {
            ControlRebinder rebinder = input.GetRebinder();
            rebinder.ResetAll();
            input.SetLeftStickYInverted(false);
            controlsView.Refresh();
        }

        /// <summary>Whether a focus attempt is over: the control has focus, or a newer request replaced it.</summary>
        private bool IsFocusSettled(VisualElement target, int requestId)
        {
            Focusable focused = root.focusController.focusedElement;
            bool hasFocus = focused == target;
            bool isReplaced = requestId != focusRequestId;
            if (hasFocus)
            {
                return true;
            }
            return isReplaced;
        }

        /// <summary>Takes keyboard focus away from whatever has it.</summary>
        private void BlurFocused()
        {
            Focusable focused = root.focusController.focusedElement;
            if (focused != null)
            {
                focused.Blur();
            }
        }

        /// <summary>The control to focus when a screen opens.</summary>
        private VisualElement FindFocusTarget(MenuScreen screen, int preferredStation)
        {
            switch (screen)
            {
                case MenuScreen.Title:
                    if (startMatchButton.enabledSelf)
                    {
                        return startMatchButton;
                    }
                    return root.Q<Button>("btn-controls");
                case MenuScreen.StationPicker:
                    int stationIndex = Mathf.Clamp(preferredStation, 0, 5);
                    return stationButtons[stationIndex];
                case MenuScreen.Pause:
                    return root.Q<Button>("btn-resume");
                case MenuScreen.Controls:
                    return controlsView.GetFirstFocusable();
                case MenuScreen.Controllers:
                    return root.Q<Button>("btn-controllers-back");
                case MenuScreen.Options:
                    return optionsView.GetFirstFocusable();
            }
            return null;
        }

        // ---- low-level helpers (static, no side effects)

        /// <summary>The display style for a shown or hidden element.</summary>
        private static StyleEnum<DisplayStyle> ConvertToDisplay(bool isShown)
        {
            if (isShown)
            {
                return DisplayStyle.Flex;
            }
            return DisplayStyle.None;
        }
    }
}
