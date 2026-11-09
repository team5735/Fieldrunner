# Phase 3c spec: Controllers screen (who drives)

Status: approved 2026-11-09, ready to implement. Read `CLAUDE.md` (project rules) and `../FRC/STYLE.md` (code style; C# mapping in CLAUDE.md)
first. Once approved, everything here is decided; if the code contradicts this spec or a step turns out impossible,
stop and report rather than improvising.

## Goal

A game-style "who's playing" screen. Every detected controller and the keyboard sit as cards in the middle; one large
**Driver** slot is on the left. Whoever presses **left** on their device moves it into the Driver slot and drives the
robot; the device that was there drops back into the middle. Pressing **right** on the driver device moves it back
out, leaving the slot empty: an empty slot shows a ghost keyboard placeholder, because the keyboard drives if nobody
takes the slot before the screen closes. Nobody has to visit this screen: the automatic choice the game makes today
fills the Driver slot by default.

## The driver slot

The game always has exactly one **driver device**: the keyboard or one controller. Only the driver device drives the
robot; every device still works in the menus.

- **Default incumbent** (no change from today's rule): at startup the first connected gamepad (or a joystick with
  saved bindings), otherwise the keyboard. A controller connecting while the keyboard is the driver takes the slot
  (with today's "Controller connected" toast). A controller connecting while another controller is the driver joins
  the middle instead, with the toast "Controller connected: <name> - open Controllers to drive with it".
- **Takeover**: on the Controllers screen, pressing left on any device in the middle makes it the driver; the
  incumbent returns to the middle.
- **Stepping out**: pressing right on the driver device moves it back to the middle and empties the slot. The empty
  slot shows a **ghost keyboard placeholder** (dimmed keyboard card, "Keyboard - drives if no one takes the slot").
  If the screen closes with the slot empty, the keyboard becomes the driver. Right on a middle card does nothing.
  The keyboard itself steps out the same way (Right arrow or D); its card then sits in the middle while the ghost
  shows in the slot, which still means the keyboard will drive unless someone takes over.
- Outside this screen there is always a driver device (the ghost resolves to the keyboard on leaving).
- **Driver disconnects**: today's behavior: the game pauses with "Controller disconnected" and the keyboard or a
  reconnect resumes. The message adds "or open Controllers to pick another".
- Not remembered across restarts (devices change between sessions); each start begins with the default incumbent.

What changes underneath: today the Controller scheme reads every gamepad at once, so two plugged-in controllers both
drive. From now on the robot reads only the driver device (restrict the action asset's devices to it, keyboard or
one controller).

## Controllers screen

Opened from **Controllers** on the title screen and the pause menu (new item, between Controls and Options).

```
+------------------------------------------------------------------------------+
|                                  Controllers                                 |
|  +----------------------+     +------------------+   +------------------+    |
|  |       DRIVER         |     | Xbox Wireless    |   | Keyboard         |    |
|  |                      |     | Controller       |   |                  |    |
|  |  Microsoft Xbox One  | <-- |  [lights up]     |   |                  |    |
|  |  2015                |     +------------------+   +------------------+    |
|  |                      |     +------------------+                           |
|  |  (lights up on input)|     | DualSense        |                           |
|  +----------------------+     +------------------+                           |
|                                                                              |
|   Press LEFT on your controller (Left arrow or A on the keyboard) to drive,  |
|              RIGHT to step out.   B / Esc: back                              |
+------------------------------------------------------------------------------+
```

- **Cards**: one per detected gamepad and joystick, plus one for the keyboard. Each shows the device's display name
  and its family (Xbox / PlayStation / Switch / generic joystick / keyboard). A card briefly lights up whenever its
  device has any input, so people can tell which card is theirs.
- **Left** means: gamepad D-pad left or left stick pushed left past half way; joystick: its stick pushed left;
  keyboard: Left arrow or A. **Right** is the mirror (D-pad / stick right, Right arrow or D). Both act on the press
  (edge), not while held.
- A generic joystick with no saved bindings moved into the slot opens the existing "New controller: bind its inputs"
  flow on the Controls screen; it becomes the driver once bound.
- Cards appear and disappear as devices connect and disconnect. If the driver device disconnects while this screen
  is open, the slot empties to the ghost keyboard (no pause; the pause rule applies only during play). With no
  controllers, the keyboard card alone sits in the slot and the hint says "No controllers detected - the keyboard drives." (plus, on Linux, "If one is plugged
  in, check you can read /dev/input.").
- On this screen directional input is not used for menu navigation (left would move focus); B / Esc / the mouse
  "Back" button leave. Mouse users can click a card to make it the driver, and click the driver card to step it out.
- The robot is disabled while this screen is open (it opens from the title or pause only).

## Elsewhere

- Title screen line "Input: <name>" becomes "Driver: <name>".
- The Controls screen labels follow the driver device's family (as they follow the active device today).
- Rumble goes to the driver device only (when it is a gamepad).
- Log: `[Driver-Sim] input devices: <name> (<family>[, driver]); ...` at startup and on every connect, disconnect and
  takeover.
- `--screenshot-menu controllers` opens the screen for captures.

## Implementation notes

- `ControllerInput` keeps the default-incumbent rule (it is today's active-device logic) and gains
  `SetDriverDevice(InputDevice)`, `GetDriverDevice()`, and a list of candidate devices. "Keyboard" is the
  `Keyboard.current` device. Restrict reading to the driver with `actions.devices` (plus the existing binding mask for
  the scheme).
- Detecting "left" and card activity on non-driver devices must read the devices directly (e.g. `Gamepad.dpad.left`,
  `leftStick.x`, `Keyboard.leftArrowKey`/`aKey`, `Joystick.stick.x`), not through the action asset, which only sees
  the driver.
- New `Game/ControllersView.cs` for the screen (like `ControlsView`), UXML/USS additions in `DriverSim.uxml/.uss`,
  `MenuScreen.Controllers`, a `MenuButton` for the new menu item; `GameFlow` routes to it from title and pause.
- No bridge or protocol changes.

## Acceptance (report actual output for each)

1. Build clean; `testDriverSim` passes; `selfTestDriverSim` 12/12; `selfTestDriverSimWithGame` 18/18 (unchanged).
2. Screenshots: Controllers screen with the keyboard only; with the Xbox controller connected (driver slot showing
   it, keyboard in the middle); with the slot emptied (ghost keyboard placeholder). Title line reads "Driver: ...".
3. Log shows the device list at startup.
4. Manual (the user, with one or more controllers): left on a middle card takes the slot and bumps the incumbent;
   right on the driver empties the slot to the ghost keyboard, and leaving the screen then drives with the keyboard;
   only the driver drives; a second controller connecting mid-session joins the middle with the toast; unplugging the
   driver pauses as before.

## Out of scope

Operator slot (deferred; the layout leaves room for a slot on the right later), remembering assignments across
restarts, per-player bindings.
