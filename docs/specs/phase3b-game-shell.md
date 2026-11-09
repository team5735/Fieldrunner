# Phase 3b spec: game shell and controller input (no sim GUI)

Status: approved 2026-11-09, ready to implement. Read `CLAUDE.md` (project rules) and `../FRC/STYLE.md` (code style, applies to Java and
C#; C# mapping in CLAUDE.md) first. Once approved, everything here is decided; if something in the code contradicts
this spec or a step turns out impossible, stop and report rather than improvising.

## Goal

The trainer runs like a normal video game. `./gradlew simulateDriverSim` opens one window, the game, at a title
screen. The player picks Start Match or Free Practice and drives with whatever controller is plugged in, or with the
keyboard when none is. A Controls screen shows what every button does and lets the player rebind it; an Options
screen holds the camera and display settings. Esc or Back + Start pauses. Nobody touches the WPILib sim GUI: by
default it doesn't open at all.

No robot code changes. The robot code still reads an Xbox controller on driver station port 0; the controller values
now come from the game instead of the sim GUI.

## Architecture: the bridge is the driver station

In WPILib simulation the robot code reads driver station state (enabled, mode, alliance, joysticks) from the
simulated HAL. The sim GUI is one optional extension that edits that state; the bridge already edits it during
matches (as the field system) and in the self-test. In this phase the bridge takes over all of it:

```
controller / keyboard -> Unity Input System -> virtual Xbox state --NT /DriverSim/game/joystick0--> bridge
    -> DriverStationSim port 0 -> robot code (CommandXboxController)
robot code setRumble -> DriverStationSim rumble -> bridge --NT /DriverSim/robot/rumble--> game -> controller motors
```

### Input owner

The bridge has one input owner, fixed at startup:

| Owner | When | Joysticks and enable come from |
|---|---|---|
| `game` | default `simulateDriverSim` | the game (this spec) |
| `gui` | `simulateDriverSim -PdriverSimGui` | the sim GUI, as today; the bridge ignores the game's joystick and enable commands, still runs matches |
| `selftest` | `selfTestDriverSim*` | the self-test script; game joystick topic ignored except in the self-test's own joystick step |

Gradle passes the owner to the bridge in the environment variable `DRIVERSIM_INPUT` (`game` / `gui` / `selftest`).
In `game` mode Gradle disables the sim GUI extension the same way the self-test already does; `-PdriverSimGui` keeps
it. The `addDriverstation()` (DS socket) extension stays disabled in every mode.

The bridge publishes the owner so the game can show it: `/DriverSim/bridge/info` becomes `"version=3 input=game"`
(was the bare version number). In `gui` mode the game shows a one-line notice on the title screen: "Controller input
comes from the WPILib sim GUI (-PdriverSimGui)" and doesn't send joystick values.

## Protocol (bump `GameLink.PROTOCOL_VERSION` and C# `Protocol.Version` to 3)

| Topic | Type | Direction | Contents |
|---|---|---|---|
| `game/joystick0` (new) | double[8] | game -> bridge | `{leftX, leftY, leftTrigger, rightTrigger, rightX, rightY, buttonBits, povDeg}` in WPILib Xbox conventions (below); sent every frame, at most 100 Hz, also when neutral |
| `robot/rumble` (new) | double[2] | bridge -> game | `{left, right}`, 0..1, from `DriverStationSim.getJoystickRumble(0, 0/1) / 65535`; published when it changes |
| `bridge/info` | string | bridge -> game | `"version=3 input=<owner>"` |
| `match/state` | double[] | bridge -> game | adds `IS_PAUSED = 6` (1 while paused, by the game or by the bridge's staleness rule); `LENGTH` 6 -> 7 |
| `game/command` | string | game -> bridge | new verbs below |

WPILib Xbox conventions (what `XboxController` expects): axes 0 leftX, 1 leftY, 2 leftTrigger, 3 rightTrigger,
4 rightX, 5 rightY; sticks -1..1 with **stick pushed forward = negative Y** (Unity's gamepad is +1 up, so the game
negates both Ys); triggers 0..1. Buttons bit 0..9 = A, B, X, Y, LB, RB, Back, Start, left stick press, right stick
press (bit i = WPILib button i+1). POV in degrees clockwise from up (0, 45, ... 315), -1 when released.

New command verbs (handled only in `game` mode; ignored with a log line otherwise):

| Verb | Effect |
|---|---|
| `practice` | free practice: teleop, enabled (only outside a match) |
| `disable` | free practice: disabled (only outside a match) |
| `pause` | match: hold the match clock and disable; free practice: disable |
| `resume` | match: re-enable in the phase it was paused in and restart the clock; free practice: enable |

### Bridge (Java) work

- `driversim.game.JoystickLink` (pure apart from one apply method; unit-tested): decodes `joystick0` samples,
  validates length, clamps values, and applies them to `DriverStationSim` port 0 each bridge update
  (`setJoystickAxis` x6, `setJoystickButtons`, `setJoystickPOV`, then `notifyNewData`). On startup in `game` mode it
  declares port 0 as an Xbox controller (`setJoystickIsXbox`, `setJoystickType`, name "Xbox Controller (Driver-Sim)",
  axis count 6, button count 10, POV count 1) and sets `setDsAttached(true)`.
- Staleness: no sample for 0.25 s -> neutral sticks/buttons (robot stops; covers a network hiccup); none for 2 s ->
  the game is gone (crashed or killed): the bridge pauses exactly as for the `pause` verb (match clock frozen, robot
  disabled) and logs it. Nothing is aborted. A game that connects later sees `IS_PAUSED` and opens the pause menu, so
  the player can resume.
- Rumble: each update, read both rumble values for port 0 and publish when either changed by more than 1/100.
- `MatchController`: add `pause(nowS)` / `resume(nowS)`. Pausing remembers the phase and elapsed time; while paused
  `update` keeps the robot disabled and the match clock (and the `match/state` time) frozen; resume shifts
  `phaseStartS` by the paused duration. Pause/resume outside a match only toggle free-practice enable.
- Free practice enable in `game` mode: the bridge owns enable outside matches (today it leaves it to the GUI): starts
  disabled; `practice` / `resume` enable teleop; `disable` / `pause` disable.
- Unit tests: joystick decode (good, short, out-of-range values), staleness timing (neutral at 0.25 s, pause at
  2 s, no abort), rumble scaling, match pause / resume keeps the remaining time exact across phase boundaries.

## Unity work

### Screens

Built with UI Toolkit: layout in UXML, style in USS, under `Assets/DriverSim/UI/`. `SceneBuilder` adds the
`UIDocument`, its `PanelSettings` (created by code if missing; scale with screen size, reference 1920x1080) and an
`EventSystem` with `InputSystemUIInputModule` so menus work with mouse, keyboard and any controller. No image art:
panels, text and simple drawn shapes. Style: dark translucent panels over the live field view, large legible text,
the selected item highlighted; readable from 1080p to 4K.

- **Title**: Start Match - Free Practice - Controls - Options - Quit. Shows the bridge connection state
  ("waiting for robot sim..." until `hello` is acknowledged; Start/Practice disabled until then) and the active input
  device. Start Match and Free Practice first show a station picker (Blue 1-3 / Red 1-3, last choice preselected),
  which sends `station` then `start` (match) or `resetPose` to the station's start spot then `practice` (practice).
- **In game**: the existing HUD (match timer, F1 details). A small toast at the bottom when a controller connects
  ("Controller connected: Xbox Wireless Controller").
- **Controller disconnected**: when the active controller disconnects during play (batteries, cable), the game
  pauses at once (sends `pause`) and shows "Controller disconnected. Reconnect it, or press any key to continue with
  the keyboard." Reconnecting returns to the pause menu with that controller active; a key press switches to the
  keyboard scheme and returns to the pause menu. Either way the player resumes explicitly.
- **Pause** (Esc, hold Start then Back for 0.5 s, a controller disconnect, or `IS_PAUSED` from the bridge when the
  game connects): Resume - Restart Match (match only) - Reset Robot - Controls -
  Options - Quit to Title. Opening sends `pause`; Resume sends `resume`; Quit to Title sends `abort` (match) or
  `disable`. B / Esc goes back one screen.
- **Controls**: a schematic Xbox controller drawn with UI shapes, each button and stick labelled with its robot
  action (table below), plus a column with the keyboard key for each. Labels follow the active device (Xbox A/B/X/Y;
  PlayStation Cross/Circle/Square/Triangle; generic: "South/East/West/North button"). Select an action to rebind:
  "Press a button for <action>... (Esc to cancel)". Per-binding Reset and Reset All. A line at the bottom: "Pause:
  hold Start, then Back".
- **Options**: camera view (driver station / overhead), field of view, show details panel, show colliders,
  fullscreen, resolution, graphics quality. Saved in `PlayerPrefs`; command-line arguments override saved values.

### Robot controls table

`Game/RobotControlsLayout.cs`: a static table mirroring `Robot.setupDriverBindings()` and the default drive command
in `FRC-2026/src/main/java/frc/robot/Robot.java` (non-demo bindings). It is the Controls screen's source of labels.
Its doc comment says it must be updated when the robot code's bindings change.

| Xbox input | Robot action (robot code) | Keyboard default |
|---|---|---|
| Left stick | drive (field-centric, 3 m/s) | W A S D |
| Left / right trigger | rotate left / right | Q / E |
| A | face hub (hold) | R |
| B | launch: score (hold); unclog on release | Space |
| X | launch: ferry + intake (hold) | F |
| Y | slow mode (hold) | C |
| Start | turbo, 5 m/s (hold) | Left Alt |
| Back | reset field-centric heading | Tab |
| Right bumper | intake in (hold) | Left Shift |
| Left bumper | intake out (hold) | Left Ctrl |
| D-pad down / up | intake slapdown / lift (hold) | Down / Up arrow |
| D-pad left | spindex backwards (hold) | Left arrow |
| Right stick, stick presses, D-pad right | unused | - |

### Input

`Game/ControllerInput.cs` builds the virtual Xbox state each frame from an `InputActionAsset`
(`Assets/DriverSim/Input/DriverControls.inputactions`): one action per virtual control (2 stick vectors, 2 trigger
axes, 10 buttons, 4 D-pad directions), with two control schemes:

- **Controller**: defaults on `<Gamepad>` paths (left stick, triggers, buttonSouth/East/West/North, shoulders,
  select/start, stick presses, D-pad), which Unity maps for Xbox, PlayStation and Switch Pro controllers alike.
  A `<Joystick>` device (flight stick, unrecognized HID) has no defaults: the first time one is the active device,
  the game opens the Controls screen with "New controller: bind its inputs".
- **Keyboard**: the table's defaults. WASD gives -1/0/+1 per axis, normalized so diagonals stay at length 1.

Active device: the controller scheme whenever a gamepad (or a joystick with saved bindings) is connected, otherwise
the keyboard. Connecting a controller switches to it (toast); the active controller disconnecting pauses the game
(Screens, above) rather than switching silently. With no controller at startup the keyboard is active. The keyboard
always works in menus.

Rebinding uses `PerformInteractiveRebinding`; overrides are saved with `SaveBindingOverridesAsJson` to
`<persistentDataPath>/bindings.json` and loaded at startup. Stick rebinding offers an invert toggle.

What the game sends: while a menu is open, or the game window is unfocused, `joystick0` carries the neutral state
(and the robot is disabled anyway). While Start is held, Back is withheld from the robot (so the pause chord never
resets the field-centric heading); Back pressed alone goes through at once. Rumble from `robot/rumble` drives
`Gamepad.SetMotorSpeeds` on the active gamepad and stops when paused or unfocused.

### Game hotkeys move off the robot keys

The keyboard is now also a robot controller, so game-only keys move: Esc pause, F1 details, F2 colliders, F3 camera
view, F4 / F5 field of view -/+, F6 reset robot, F10 quit. Enter, Backspace, 1-6, R, V, [ and ] stop being game
hotkeys (start, abort and station live in the menus). `GameControls` shrinks to these and the pause chord.

### Command line and automation

- `--start-in title|practice|match` (default `title`); `practice` and `match` skip the menus using the saved or
  `--station` station. `scripts/capture-screenshots.sh` passes `--start-in practice` unless its args say otherwise.
- The with-game self-test passes `--start-in practice` and runs with owner `selftest`, so the game's joystick
  doesn't fight the script.
- `--screenshot-menu title|controls|options|pause`: open that screen before the auto screenshots, for acceptance
  captures.

## Self-test

- `selfTestDriverSim` gains one step, "game joystick drives robot": the self-test switches `JoystickLink` on,
  publishes a `joystick0` sample with leftY = -1 on the game's topic through the local NT instance, checks the robot
  moves downfield, then publishes neutral and switches it off. Step count 11 -> 12.
- With game: 17 -> 18 steps.

## Docs

- README: running section (one window; `-PdriverSimGui` for debugging; standalone Glass/AdvantageScope can attach to
  NT), controller section rewritten (no GUI joystick panel), keys, protocol table v3, phase table (3b row).
- CLAUDE.md: input owner modes; the robot controls table must follow `Robot.java`.
- Screenshots: title, station picker, controls, options, pause, in-game toast, numbered per the usual rule.

## Acceptance (report actual output for each)

1. `./gradlew testDriverSim` passes, including the new unit tests.
2. `selfTestDriverSim` prints 12/12; `selfTestDriverSimWithGame` prints 18/18.
3. `simulateDriverSim` opens only the game window, at the title screen; the log shows `input=game` and no sim GUI.
4. Keyboard only (no controller): Free Practice -> Blue 1 -> WASD drives, Q/E rotate, Space launches; Esc pauses and
   the robot stops; Resume continues.
5. Killing the game during a match: within 2 s the bridge log shows the pause, the match clock stops, the robot is
   disabled; starting the game again opens the pause menu and Resume continues with the same time left.
6. Screenshots of title, station picker, controls, options, pause.
7. `simulateDriverSim -PdriverSimGui` opens the sim GUI too; the game shows the GUI-input notice; the GUI's joystick
   panel drives the robot as before.
8. Manual (the user, with a real controller): plug-in toast, drive, unplug (game pauses with the disconnect message), rebind a button and see it persist after a
   restart, pause with Start then Back, rumble when the hub goes inactive.

## Implementation notes (from reading the current code)

Bridge (Java):
- `DriverSimBridge.update` runs every 5 ms (`PERIOD_S`), on the robot loop thread. Read new topics there, never in
  callbacks.
- `bridge/info` today is `"DriverSim bridge protocol 2"`; replace it with `"version=3 input=<game|gui|selftest>"`.
  Update the game's parsing and its protocol-mismatch check accordingly.
- Put pause and free-practice enable in `MatchController` (pure, unit-tested): `pause(nowS)`, `resume(nowS)`,
  `setPracticeEnabled(boolean)`. `start` and `abort` clear any pause. While paused, `update` does nothing and
  `getStatus` reports the time frozen at the pause instant, `isEnabled` false. In free play
  `isEnabled = practiceEnabled && !isPaused`. Add `isPaused` to the `MatchStatus` record.
- `InputOwner` enum (`GAME`, `GUI`, `SELFTEST`) parsed from `DRIVERSIM_INPUT`; missing or unknown -> `GUI` (today's
  behavior) with a log line.
- `JoystickLink` (pure): `accept(sample, nowS)`, `getSampleToApply(nowS)` (neutral after 0.25 s or before any sample),
  `checkGameGone(nowS)` (true exactly once per silence longer than 2 s, and only after at least one sample).
  A `JoystickSample` record holds 6 axes, button bits and POV, with a static `parse(double[])` returning null for bad
  input. Rumble change detection in a small pure class too. The `DriverStationSim` calls stay in the bridge.
- In `game` mode the bridge writes the driver station every update, in free play too (attached, not FMS, teleop,
  enabled from the status, match time -1). In `gui` mode keep today's behavior exactly.
- The game-joystick subscriber needs `keepDuplicates(true)` and `sendAll(true)`: neutral samples repeat identical
  values and must still count as "alive".
- Self-test joystick step: `DriverSimBridge` gets a package-private switch so, in `selftest` mode, the self-test can
  turn game-joystick application on for its one step (no staleness pause in that mode). The self-test publishes with
  a publisher on `NetworkTableInstance.getDefault()`; local publish reaches the bridge's subscriber.
- `bridge/src/main/java/driversim/SelfTest.java` and `DriveFeelMeasurement.java` have uncommitted work (the drive-feel
  measurement); keep it.
- Check that `quit` still ends the robot program cleanly with no sim GUI loaded (`robot.endCompetition()`).

Gradle (`gradle/driversim.gradle`):
- `-PdriverSimGui` (project property). Sim GUI extension disabled unless it is set; always disabled for self-tests
  (already done there; extend the same `halExtensions.configureEach` pattern).
- Set `DRIVERSIM_INPUT` on the simulation task: `selftest` for self-tests, `gui` with `-PdriverSimGui`, else `game`.
- The with-game self-test adds `--start-in practice` to the game command.
- `scripts/capture-screenshots.sh`: add `--start-in practice` unless the caller's game args contain `--start-in`.

Game (Unity):
- The game sends `joystick0` only when `bridge/info` says `input=game` (in `gui` and `selftest` modes it sends none,
  so the self-test's own publisher is the only one on the topic).
- `DebugHud` is IMGUI (`OnGUI`), which draws over UI Toolkit: give it a visibility setter and hide it whenever a
  menu screen is open. Its footer key line becomes the new hotkeys.
- `DriverStationCamera` needs `SetView(View)`, `GetFieldOfViewDeg()`, `SetFieldOfViewDeg(float)` for Options.
- `GameControls.PlaceRobotAtStart` (station start spot) is reused by the station picker: move it where both can
  call it (e.g. a static helper next to `DriverStations`) rather than duplicating it.
- The UI needs the `UnityEngine.UI` assembly reference (EventSystem) in `DriverSim.asmdef`; UI Toolkit and the
  Input System module are already available. `com.unity.ugui` 2.6.0 and `com.unity.inputsystem` 1.20.1 are
  installed; `activeInputHandler` is the new Input System.
- `SceneBuilder` generates the scene (never hand-edit `DriverSim.unity`): add the UI objects there. UXML, USS,
  `.inputactions` and `PanelSettings` are assets under `Assets/DriverSim/UI/` and `Assets/DriverSim/Input/`.
- Game state follows the bridge too: on the first match state after connecting, `IS_PAUSED` opens the pause menu
  and a running match phase goes to in-game; later, a match started from outside (probe `start`) while on the title
  or station picker goes to in-game. The pause menu closes only by the player's Resume / Quit.
- Keep `--collision-selftest` working with no robot sim and no UI interaction.

## Constraints

- No robot code changes (the existing `DRIVE_INERTIA` exception stays the only one).
- Style rules from CLAUDE.md for all new Java and C#; Java through `scripts/format.java.sh`.
- The bridge still runs only on the robot loop thread; NT callbacks only queue.
- Keep `--collision-selftest`, `--screenshot*`, `--view`, `--nt-host`, `--stop-sim-on-exit` working.

## Out of scope

Operator (second) controller (designed for: a later `joystick1` topic and a second column on the Controls screen),
audio, controller button artwork, per-player profiles, gameplay (phase 4).
