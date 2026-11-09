using UnityEngine;

namespace DriverSim.Field
{
    /// <summary>
    /// Conversions between the robot code's WPILib field frame and Unity world space.
    ///
    /// WPILib field frame (wpiBlue): origin at the blue alliance wall's right corner (as seen from the blue driver
    /// stations), +x down the field's length toward red, +y to the blue drivers' left, +z up, headings CCW positive.
    ///
    /// Unity world: origin at the field center (where the AdvantageScope field model is placed), +X = +x, +Z = +y,
    /// +Y = up. Mapping x to X and y to Z swaps handedness exactly as Unity's left-handed frame needs, so positions
    /// convert with a plain offset. Verified against the model's AprilTag positions (Config.json) and WPILib's
    /// 2026-rebuilt-andymark layout.
    /// </summary>
    public static class FieldFrame
    {
        public const float FieldLengthM = 16.518f; // WPILib 2026 AndyMark layout length (robot code uses this)
        public const float FieldWidthM = 8.043f; // WPILib 2026 AndyMark layout width

        /// <summary>Converts a field-frame point to Unity world space.</summary>
        /// <param name="xM">Field x, meters.</param>
        /// <param name="yM">Field y, meters.</param>
        /// <param name="zM">Height above the carpet, meters.</param>
        public static Vector3 ToWorld(double xM, double yM, double zM)
        {
            return new Vector3((float)(xM - FieldLengthM / 2), (float)zM, (float)(yM - FieldWidthM / 2));
        }

        /// <summary>Converts a Unity world point to field-frame (x, y), meters.</summary>
        public static Vector2 ToField(Vector3 world)
        {
            return new Vector2(world.x + FieldLengthM / 2, world.z + FieldWidthM / 2);
        }

        /// <summary>Converts a field-frame vector (dx, dy) to a Unity world direction on the ground plane.</summary>
        public static Vector3 DirectionToWorld(double dxM, double dyM)
        {
            return new Vector3((float)dxM, 0, (float)dyM);
        }

        /// <summary>
        /// Converts a field-frame heading to the Unity rotation that turns local +Z (an object's forward) to face it.
        /// </summary>
        /// <param name="headingRad">Field heading, radians, CCW positive (0 faces +x, toward red).</param>
        public static Quaternion HeadingToRotation(double headingRad)
        {
            // field direction (cos h, sin h) is world (cos h, 0, sin h); Unity yaw psi faces (sin psi, 0, cos psi)
            float yawDeg = 90f - (float)(headingRad * Mathf.Rad2Deg);
            return Quaternion.Euler(0, yawDeg, 0);
        }

        /// <summary>
        /// Converts a Unity yaw (degrees about +Y) back to a field heading.
        /// </summary>
        public static double YawToHeadingRad(float yawDeg)
        {
            return (90f - yawDeg) * Mathf.Deg2Rad;
        }
    }

    /// <summary>
    /// Where each driver station is, from the AdvantageScope 2026 field config, in field frame.
    /// </summary>
    public static class DriverStations
    {
        // station y positions, field frame meters; index 0..2 = Blue 1..3, 3..5 = Red 1..3
        private static readonly float[] StationYM = { 7.101f, 5.272f, 2.193f, 0.942f, 2.771f, 5.850f };

        /// <summary>Default station when the driver station has not chosen one: Blue 2.</summary>
        public const int DefaultStation = 1;

        /// <summary>Display name, e.g. "Blue 2".</summary>
        public static string GetName(int station)
        {
            int index = Normalize(station);
            if (index >= 3)
            {
                return "Red " + (index - 2);
            }
            return "Blue " + (index + 1);
        }

        /// <summary>Short wire name for the bridge's station command, e.g. "B2".</summary>
        public static string GetWireName(int station)
        {
            int index = Normalize(station);
            if (index >= 3)
            {
                return "R" + (index - 2);
            }
            return "B" + (index + 1);
        }

        /// <summary>True for red stations.</summary>
        public static bool IsRed(int station)
        {
            return Normalize(station) >= 3;
        }

        /// <summary>Field-frame y of a station, meters.</summary>
        public static float GetYM(int station)
        {
            return StationYM[Normalize(station)];
        }

        /// <summary>Field-frame x of a station's alliance wall, meters.</summary>
        public static float GetWallXM(int station)
        {
            if (IsRed(station))
            {
                return FieldFrame.FieldLengthM;
            }
            return 0;
        }

        /// <summary>Field heading the station's drivers face (downfield), radians.</summary>
        public static double GetFacingRad(int station)
        {
            if (IsRed(station))
            {
                return Mathf.PI;
            }
            return 0;
        }

        private static int Normalize(int station)
        {
            if (station < 0 || station > 5)
            {
                return DefaultStation;
            }
            return station;
        }
    }
}
