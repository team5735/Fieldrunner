namespace DriverSim.Net
{
    /// <summary>
    /// A virtual Xbox controller reading in WPILib's XboxController conventions: sticks -1..1 with forward negative,
    /// triggers 0..1. Sent to the bridge as /DriverSim/game/joystick0. Immutable.
    /// </summary>
    public readonly struct JoystickState
    {
        public const int ButtonCount = 10; // buttons an Xbox controller has
        public const int WireLength = 8; // values in the topic's array

        /// <summary>A reading with everything centered and released.</summary>
        public static readonly JoystickState Neutral = new JoystickState(0, 0, 0, 0, 0, 0, 0, -1);

        /// <summary>Creates a reading from its values; see the properties for ranges.</summary>
        public JoystickState(
            double leftX,
            double leftY,
            double leftTrigger,
            double rightTrigger,
            double rightX,
            double rightY,
            int buttonBits,
            int povDeg)
        {
            LeftX = leftX;
            LeftY = leftY;
            LeftTrigger = leftTrigger;
            RightTrigger = rightTrigger;
            RightX = rightX;
            RightY = rightY;
            ButtonBits = buttonBits;
            PovDeg = povDeg;
        }

        /// <summary>Left stick X, -1 (left) to 1 (right).</summary>
        public double LeftX { get; }

        /// <summary>Left stick Y, -1 (forward) to 1 (back).</summary>
        public double LeftY { get; }

        /// <summary>Left trigger, 0 to 1.</summary>
        public double LeftTrigger { get; }

        /// <summary>Right trigger, 0 to 1.</summary>
        public double RightTrigger { get; }

        /// <summary>Right stick X, -1 (left) to 1 (right).</summary>
        public double RightX { get; }

        /// <summary>Right stick Y, -1 (forward) to 1 (back).</summary>
        public double RightY { get; }

        /// <summary>Pressed buttons: bit 0..9 = A, B, X, Y, LB, RB, Back, Start, left stick press, right stick
        /// press.</summary>
        public int ButtonBits { get; }

        /// <summary>D-pad direction, whole degrees clockwise from up (0, 45, ... 315); -1 when released.</summary>
        public int PovDeg { get; }

        /// <summary>Whether a button bit is set.</summary>
        /// <param name="bitIndex">Button bit, 0..9.</param>
        public bool IsButtonDown(int bitIndex)
        {
            return (ButtonBits & (1 << bitIndex)) != 0;
        }

        /// <summary>Copy of this reading with some button bits cleared.</summary>
        /// <param name="bitIndex">Button bit to clear, 0..9.</param>
        public JoystickState MakeWithButtonCleared(int bitIndex)
        {
            int bits = ButtonBits & ~(1 << bitIndex);
            return new JoystickState(LeftX, LeftY, LeftTrigger, RightTrigger, RightX, RightY, bits, PovDeg);
        }

        /// <summary>The values laid out for the joystick topic.</summary>
        public double[] ConvertToWire()
        {
            return new double[] { LeftX, LeftY, LeftTrigger, RightTrigger, RightX, RightY, ButtonBits, PovDeg };
        }
    }

    /// <summary>
    /// Controller rumble the robot code asks for, from /DriverSim/robot/rumble. Immutable.
    /// </summary>
    public readonly struct RumbleState
    {
        /// <summary>No rumble.</summary>
        public static readonly RumbleState None = new RumbleState(0, 0);

        /// <summary>Creates a rumble request from its values.</summary>
        /// <param name="left">Left (low-frequency) motor, 0 to 1.</param>
        /// <param name="right">Right (high-frequency) motor, 0 to 1.</param>
        public RumbleState(float left, float right)
        {
            Left = left;
            Right = right;
        }

        /// <summary>Left (low-frequency) motor, 0 to 1.</summary>
        public float Left { get; }

        /// <summary>Right (high-frequency) motor, 0 to 1.</summary>
        public float Right { get; }
    }
}
