package driversim.game;

/**
 * Where the game's world says the robot should have been at one instant: the game resolved the robot's reported pose
 * against walls and obstacles and sends the result back.
 *
 * @param robotTimestampS Robot clock time of the reported state the game resolved, seconds.
 * @param xFieldM         Constrained robot x, field frame, meters.
 * @param yFieldM         Constrained robot y, field frame, meters.
 */
public record PoseCorrection(double robotTimestampS, double xFieldM, double yFieldM) {}
