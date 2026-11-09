package driversim.game;

/**
 * What the field system says at one instant.
 *
 * @param phase             Match phase.
 * @param isFieldControlled True while a match is running or just finished; false in free play.
 * @param isEnabled         Whether the robot should be enabled; in free play, whether free practice is on.
 * @param isAutonomous      Whether the robot should be in autonomous mode.
 * @param matchTimeS        Match time remaining in the current period, seconds; -1 when there is no match.
 * @param gameMessage       Game-specific message: "R" or "B" (autonomous winner) during teleop, empty otherwise.
 * @param isBlueHubActive   Whether the blue hub currently scores.
 * @param isRedHubActive    Whether the red hub currently scores.
 * @param isPaused          Whether the session is paused: the match clock is held and the robot is disabled.
 */
public record MatchStatus(
    MatchController.Phase phase, boolean isFieldControlled, boolean isEnabled, boolean isAutonomous, double matchTimeS,
    String gameMessage, boolean isBlueHubActive, boolean isRedHubActive, boolean isPaused
) {}
