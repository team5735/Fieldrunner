package driversim.game;

/**
 * Runs a 2026 REBUILT match the way the field system would: 20 s of autonomous, a short disabled gap, then 2:20 of
 * teleop, with the hub-shift schedule driven by which alliance won autonomous.
 *
 * <p>
 * Outside a match the controller is in {@link Phase#FREE_PLAY} and leaves the driver station alone, so drivers can
 * enable from the sim GUI as they like. It is pure timing logic: callers pass the current time in, read
 * {@link #getStatus} and apply it to the simulated driver station.
 */
public final class MatchController {
    public static final double AUTO_S = 20;           // seconds of autonomous
    public static final double AUTO_TO_TELEOP_S = 3;  // seconds disabled between autonomous and teleop
    public static final double TELEOP_S = 140;        // seconds of teleop (2:20)

    // teleop hub shifts, as match time remaining in seconds (the robot code's MatchState uses the same boundaries)
    public static final double TRANSITION_END_S = 130;  // both hubs active above this
    public static final double SHIFT_1_END_S = 105;     // shift 1: the autonomous loser's hub is active
    public static final double SHIFT_2_END_S = 80;      // shift 2: the autonomous winner's hub is active
    public static final double SHIFT_3_END_S = 55;      // shift 3: loser's hub
    public static final double ENDGAME_START_S = 30;    // shift 4 (winner's hub) until here, then both hubs active

    /** Where the match is. */
    public enum Phase { FREE_PLAY, AUTO, AUTO_TO_TELEOP, TELEOP, POST_MATCH }

    private final double slackS;  // seconds: half the update period, so a boundary never waits an extra update
    private Phase phase;          // current phase
    private double phaseStartS;   // time the current phase began, seconds
    private char autoWinner;      // 'R', 'B', or ' ' when not yet decided

    /**
     * Creates a controller in free play.
     *
     * @param updatePeriodS Seconds between calls to {@link #update}; phase boundaries allow half of it as slack.
     */
    public MatchController(double updatePeriodS) {
        this.slackS = 0.5 * updatePeriodS;
        this.phase = Phase.FREE_PLAY;
        this.phaseStartS = 0;
        this.autoWinner = ' ';
    }

    /**
     * Starts a new match at autonomous. Ignored while a match is already running.
     *
     * @param nowS Current time, seconds.
     */
    public void start(double nowS) {
        if (phase != Phase.FREE_PLAY && phase != Phase.POST_MATCH) {
            return;
        }
        phase = Phase.AUTO;
        phaseStartS = nowS;
        autoWinner = ' ';
    }

    /**
     * Ends any match immediately and returns to free play.
     *
     * @param nowS Current time, seconds.
     */
    public void abort(double nowS) {
        phase = Phase.FREE_PLAY;
        phaseStartS = nowS;
    }

    /**
     * Records which alliance won autonomous; decides the teleop hub shifts. Only accepted before teleop starts.
     *
     * @param winner 'R' for red, 'B' for blue; anything else is ignored.
     */
    public void setAutoWinner(char winner) {
        boolean isValid = winner == 'R' || winner == 'B';
        boolean isBeforeTeleop = phase == Phase.AUTO || phase == Phase.AUTO_TO_TELEOP;
        if (isValid && isBeforeTeleop) {
            autoWinner = winner;
        }
    }

    /**
     * Moves to the next phase when the current one has run its length.
     *
     * @param nowS Current time, seconds.
     */
    public void update(double nowS) {
        double elapsedS = nowS - phaseStartS;
        if (phase == Phase.AUTO && elapsedS >= AUTO_S - slackS) {
            phase = Phase.AUTO_TO_TELEOP;
            phaseStartS = phaseStartS + AUTO_S;
        } else if (phase == Phase.AUTO_TO_TELEOP && elapsedS >= AUTO_TO_TELEOP_S - slackS) {
            phase = Phase.TELEOP;
            phaseStartS = phaseStartS + AUTO_TO_TELEOP_S;
            if (autoWinner == ' ') {
                autoWinner = 'R';  // nobody reported a winner: treat it as a tie the field broke for red
            }
        } else if (phase == Phase.TELEOP && elapsedS >= TELEOP_S - slackS) {
            phase = Phase.POST_MATCH;
            phaseStartS = phaseStartS + TELEOP_S;
        }
    }

    /**
     * Reports what the driver station should show right now.
     *
     * @param nowS Current time, seconds.
     * @return Match status; {@link MatchStatus#isFieldControlled()} is false in free play.
     */
    public MatchStatus getStatus(double nowS) {
        double elapsedS = nowS - phaseStartS;
        String gameMessage = "";
        if (phase == Phase.TELEOP || phase == Phase.POST_MATCH) {
            gameMessage = String.valueOf(autoWinner);
        }

        // match time counts down within auto and teleop; -1 means "no match time" like an unattached DS
        double matchTimeS = -1;
        if (phase == Phase.AUTO) {
            matchTimeS = Math.max(0, AUTO_S - elapsedS);
        } else if (phase == Phase.AUTO_TO_TELEOP) {
            matchTimeS = TELEOP_S;
        } else if (phase == Phase.TELEOP) {
            matchTimeS = Math.max(0, TELEOP_S - elapsedS);
        } else if (phase == Phase.POST_MATCH) {
            matchTimeS = 0;
        }

        boolean isEnabled = phase == Phase.AUTO || phase == Phase.TELEOP;
        boolean isAutonomous = phase == Phase.AUTO;
        boolean isFieldControlled = phase != Phase.FREE_PLAY;
        boolean isBlueHubActive = computeHubActive(phase, matchTimeS, autoWinner, 'B');
        boolean isRedHubActive = computeHubActive(phase, matchTimeS, autoWinner, 'R');
        return new MatchStatus(
            phase, isFieldControlled, isEnabled, isAutonomous, matchTimeS, gameMessage, isBlueHubActive, isRedHubActive
        );
    }

    // ---- low-level helpers (static, no side effects)

    /**
     * Whether an alliance's hub accepts fuel for points, following the REBUILT shift schedule.
     *
     * @param phase      Current match phase.
     * @param matchTimeS Match time remaining, seconds.
     * @param autoWinner 'R' or 'B' once known, ' ' before.
     * @param alliance   'R' or 'B': the alliance whose hub to check.
     * @return True when that hub scores; always true in free play and autonomous.
     */
    private static boolean computeHubActive(Phase phase, double matchTimeS, char autoWinner, char alliance) {
        if (phase == Phase.FREE_PLAY || phase == Phase.AUTO) {
            return true;
        }
        if (phase != Phase.TELEOP) {
            return false;
        }

        // the alliance that lost autonomous gets shift 1
        boolean isShift1Active = autoWinner != alliance;
        if (matchTimeS > TRANSITION_END_S) {
            return true;
        }
        if (matchTimeS > SHIFT_1_END_S) {
            return isShift1Active;
        }
        if (matchTimeS > SHIFT_2_END_S) {
            return !isShift1Active;
        }
        if (matchTimeS > SHIFT_3_END_S) {
            return isShift1Active;
        }
        if (matchTimeS > ENDGAME_START_S) {
            return !isShift1Active;
        }
        return true;
    }
}
