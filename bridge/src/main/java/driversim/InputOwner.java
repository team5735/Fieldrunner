package driversim;

import java.util.Locale;

/**
 * Who supplies the simulated driver station's joystick values and enable state. Fixed when the bridge starts and
 * published to the game in {@code /DriverSim/bridge/info}.
 */
public enum InputOwner {
    /** The Unity game: its controller or keyboard input and its menus drive the robot. */
    GAME,

    /** The WPILib sim GUI, as in plain WPILib simulation; the game's joystick and enable commands are ignored. */
    GUI,

    /** The scripted self-test; the game's joystick topic is ignored except in the self-test's own joystick step. */
    SELFTEST;

    /**
     * Names the owner as it appears on the wire.
     *
     * @return Lower-case name: "game", "gui" or "selftest".
     */
    public String getWireName() { return name().toLowerCase(Locale.ROOT); }

    /**
     * Reads an owner from its wire name.
     *
     * @param text Name such as "game"; may be null.
     * @return The matching owner, or null when the text is null or unknown.
     */
    public static InputOwner parse(String text) {
        if (text == null) {
            return null;
        }
        String trimmed = text.trim();
        for (InputOwner owner : values()) {
            if (owner.getWireName().equals(trimmed)) {
                return owner;
            }
        }
        return null;
    }
}
