package driversim.game;

/**
 * What the field system says at one instant.
 *
 * @param phase             Match phase.
 * @param isFieldControlled True while a match is running or just finished; false in free play, where the driver
 *                          station is left to the sim GUI.
 * @param isEnabled         Whether the robot should be enabled.
 * @param isAutonomous      Whether the robot should be in autonomous mode.
 * @param matchTimeS        Match time remaining in the current period, seconds; -1 when there is no match.
 * @param gameMessage       Game-specific message: "R" or "B" (autonomous winner) during teleop, empty otherwise.
 * @param isBlueHubActive   Whether the blue hub currently scores.
 * @param isRedHubActive    Whether the red hub currently scores.
 */
public record MatchStatus(
    MatchController.Phase phase,
    boolean isFieldControlled,
    boolean isEnabled,
    boolean isAutonomous,
    double matchTimeS,
    String gameMessage,
    boolean isBlueHubActive,
    boolean isRedHubActive
) {}
