package driversim;

import com.revrobotics.util.StatusLogger;

import com.ctre.phoenix6.SignalLogger;

import edu.wpi.first.wpilibj.RobotBase;

import frc.robot.Robot;
import frc.robot.constants.Constants;
import frc.robot.constants.robot.CompbotTunerConstants;
import frc.robot.constants.robot.DevbotTunerConstants;
import frc.robot.subsystems.DrivetrainSubsystem;

/**
 * Entry point for {@code ./gradlew simulateDriverSim}. Builds the robot exactly as {@code frc.robot.Main} does for the
 * full-robot configurations, then attaches the {@link DriverSimBridge} to the robot's main loop. Set the environment
 * variable {@code DRIVERSIM_SELFTEST=1} to run the scripted {@link SelfTest} and exit with its result, or
 * {@code DRIVERSIM_SELFTEST=with-game} to also check collisions against a running Unity game.
 */
public final class DriverSimMain {
    private DriverSimMain() { throw new UnsupportedOperationException("This is a utility class!"); }

    /**
     * Starts the robot with the Driver-Sim bridge attached.
     *
     * @param args Ignored.
     */
    public static void main(String... args) {
        SignalLogger.enableAutoLogging(false);
        StatusLogger.disableAutoLogging();
        RobotBase.startRobot(DriverSimMain::makeRobot);
    }

    /**
     * Builds the robot for the current configuration and attaches the bridge (and self-test, when requested).
     *
     * @return The robot to run.
     * @throws IllegalStateException When {@code Constants.CURRENT_ROBOT} is not a full-robot configuration.
     */
    private static Robot makeRobot() {
        // the same robot frc.robot.Main builds
        Robot robot;
        switch (Constants.CURRENT_ROBOT) {
        case FULL_DEVBOT -> {
            DrivetrainSubsystem drivetrain = DevbotTunerConstants.createDrivetrain();
            robot = new Robot(drivetrain, false);
        }
        case FULL_COMPBOT -> {
            DrivetrainSubsystem drivetrain = CompbotTunerConstants.createDrivetrain();
            robot = new Robot(drivetrain, false);
        }
        case DEMO -> {
            DrivetrainSubsystem drivetrain = CompbotTunerConstants.createDrivetrain();
            robot = new Robot(drivetrain, true);
        }
        default ->
            throw new IllegalStateException(
                "Driver-Sim needs a full robot; Constants.CURRENT_ROBOT is " + Constants.CURRENT_ROBOT
            );
        }

        // bridge runs on the robot's loop thread, between robot loops
        DriverSimBridge bridge = new DriverSimBridge(robot);
        robot.addPeriodic(bridge::update, DriverSimBridge.PERIOD_S);

        // optional scripted self-test
        String selfTestFlag = System.getenv("DRIVERSIM_SELFTEST");
        boolean isSelfTest = false;
        boolean isWithGame = false;
        if (selfTestFlag != null && selfTestFlag.equals("1")) {
            isSelfTest = true;
        }
        if (selfTestFlag != null && selfTestFlag.equals("with-game")) {
            isSelfTest = true;
            isWithGame = true;
        }
        if (isSelfTest) {
            SelfTest selfTest = new SelfTest(robot, bridge, isWithGame);
            selfTest.schedule();
        }
        return robot;
    }
}
