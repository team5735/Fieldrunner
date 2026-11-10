package driversim.game;

import edu.wpi.first.math.geometry.Rotation2d;
import edu.wpi.first.math.geometry.Translation2d;

/**
 * How far to move and turn the true pose so it matches the game's decision.
 *
 * @param translationField Movement, field frame, meters.
 * @param rotation         Turn, counter-clockwise positive.
 */
public record PoseDelta(Translation2d translationField, Rotation2d rotation) {
    /** No movement and no turn. */
    public static final PoseDelta NONE = new PoseDelta(Translation2d.kZero, Rotation2d.kZero);

    /**
     * Adds another delta to this one.
     *
     * @param other Delta to add.
     * @return The combined movement and turn.
     */
    public PoseDelta plus(PoseDelta other) {
        Translation2d totalTranslationField = translationField.plus(other.translationField());
        Rotation2d totalRotation = rotation.plus(other.rotation());
        return new PoseDelta(totalTranslationField, totalRotation);
    }

    /**
     * Reports whether this delta moves or turns anything.
     *
     * @return True when the translation or the rotation is not zero.
     */
    public boolean isMoving() { return translationField.getNorm() > 0 || rotation.getRadians() != 0; }
}
