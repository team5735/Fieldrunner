using System.Globalization;

namespace DriverSim.Net
{
    /// <summary>
    /// What the bridge announces in /DriverSim/bridge/info: <c>"version=3 input=game"</c>. Immutable; build it with
    /// <see cref="TryParse"/>.
    /// </summary>
    public readonly struct BridgeInfo
    {
        /// <summary>Input owner name meaning the game supplies the controller values.</summary>
        public const string GameOwner = "game";

        /// <summary>Creates the info from its values; see the properties for meanings.</summary>
        public BridgeInfo(int version, string inputOwner)
        {
            Version = version;
            InputOwner = inputOwner;
        }

        /// <summary>Protocol version the bridge speaks.</summary>
        public int Version { get; }

        /// <summary>Who supplies joystick input: "game", "gui" or "selftest".</summary>
        public string InputOwner { get; }

        /// <summary>Whether the game is the one that supplies the controller values.</summary>
        public bool IsGameInput()
        {
            return InputOwner == GameOwner;
        }

        /// <summary>Decodes the bridge's info text.</summary>
        /// <param name="text">Text such as "version=3 input=game"; may be null.</param>
        /// <param name="info">Decoded info when the text is valid; default otherwise.</param>
        /// <returns>False when the text is missing or has no version.</returns>
        public static bool TryParse(string text, out BridgeInfo info)
        {
            info = default;
            if (string.IsNullOrEmpty(text))
            {
                return false;
            }
            int version = -1;
            string inputOwner = "";
            string[] parts = text.Split(' ');
            foreach (string part in parts)
            {
                string[] pair = part.Split('=');
                if (pair.Length != 2)
                {
                    continue;
                }
                if (pair[0] == "version")
                {
                    int.TryParse(pair[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out version);
                }
                else if (pair[0] == "input")
                {
                    inputOwner = pair[1];
                }
            }
            if (version < 0)
            {
                return false;
            }
            info = new BridgeInfo(version, inputOwner);
            return true;
        }
    }
}
