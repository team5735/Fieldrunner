using System;

namespace DriverSim.Util
{
    /// <summary>Reads the game's command-line arguments.</summary>
    public static class CommandLine
    {
        /// <summary>
        /// Value following a flag, e.g. GetValue("--nt-host") for "--nt-host 10.57.35.2".
        /// </summary>
        /// <param name="flag">Flag to look for, dashes included.</param>
        /// <returns>The value, or null when the flag is absent or has no value.</returns>
        public static string GetValue(string flag)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == flag)
                {
                    return args[i + 1];
                }
            }
            return null;
        }

        /// <summary>Whether a flag is present.</summary>
        /// <param name="flag">Flag to look for, dashes included.</param>
        public static bool HasFlag(string flag)
        {
            string[] args = Environment.GetCommandLineArgs();
            foreach (string arg in args)
            {
                if (arg == flag)
                {
                    return true;
                }
            }
            return false;
        }
    }
}
