using System.Globalization;

namespace DriverSim.Net
{
    /// <summary>
    /// A request to put one fuel ball on the field, from the <c>spawnFuel</c> command on the command topic. Immutable;
    /// build it with <see cref="TryParse"/>.
    /// </summary>
    public readonly struct FuelSpawnRequest
    {
        private const string Verb = "spawnFuel"; // command verb

        /// <summary>Creates a request from its values; see the properties for units and frames.</summary>
        public FuelSpawnRequest(double xFieldM, double yFieldM, double heightM)
        {
            XFieldM = xFieldM;
            YFieldM = yFieldM;
            HeightM = heightM;
        }

        /// <summary>Where the ball goes, field x, meters.</summary>
        public double XFieldM { get; }

        /// <summary>Where the ball goes, field y, meters.</summary>
        public double YFieldM { get; }

        /// <summary>Release height above the carpet, meters; 0 puts the ball on the carpet.</summary>
        public double HeightM { get; }

        /// <summary>Decodes a command text.</summary>
        /// <param name="wire">Command text, <c>"&lt;seq&gt; spawnFuel &lt;x&gt; &lt;y&gt; [height]"</c>; may be null.</param>
        /// <param name="request">Decoded request when the text is a valid spawnFuel command; default otherwise.</param>
        /// <returns>False when the text is some other command or malformed.</returns>
        public static bool TryParse(string wire, out FuelSpawnRequest request)
        {
            request = default;
            if (string.IsNullOrEmpty(wire))
            {
                return false;
            }
            string[] parts = wire.Trim().Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries);
            bool hasShape = parts.Length == 4 || parts.Length == 5;
            if (!hasShape || parts[1] != Verb)
            {
                return false;
            }
            double xFieldM = 0;
            double yFieldM = 0;
            double heightM = 0;
            NumberStyles style = NumberStyles.Float;
            CultureInfo culture = CultureInfo.InvariantCulture;
            if (!double.TryParse(parts[2], style, culture, out xFieldM))
            {
                return false;
            }
            if (!double.TryParse(parts[3], style, culture, out yFieldM))
            {
                return false;
            }
            if (parts.Length == 5 && !double.TryParse(parts[4], style, culture, out heightM))
            {
                return false;
            }
            if (heightM < 0)
            {
                return false;
            }
            request = new FuelSpawnRequest(xFieldM, yFieldM, heightM);
            return true;
        }
    }
}
