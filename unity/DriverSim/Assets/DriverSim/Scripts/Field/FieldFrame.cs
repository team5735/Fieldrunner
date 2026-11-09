using System.Globalization;
using UnityEngine;

namespace DriverSim.Field
{
    /// <summary>
    /// Conversions between the robot code's WPILib field frame and Unity world space. The only place that knows the
    /// mapping.
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
        public const float FieldLengthM = 16.518f; // meters, WPILib 2026 AndyMark layout length (robot code uses it)
        public const float FieldWidthM = 8.043f; // meters, WPILib 2026 AndyMark layout width

        /// <summary>Converts a field-frame point to Unity world space.</summary>
        /// <param name="xFieldM">Field x, meters.</param>
        /// <param name="yFieldM">Field y, meters.</param>
        /// <param name="heightM">Height above the carpet, meters.</param>
        /// <returns>World position, meters.</returns>
        public static Vector3 ConvertFieldToWorld(double xFieldM, double yFieldM, double heightM)
        {
            float worldX = (float)(xFieldM - FieldLengthM / 2);
            float worldZ = (float)(yFieldM - FieldWidthM / 2);
            return new Vector3(worldX, (float)heightM, worldZ);
        }

        /// <summary>Converts a Unity world point to field-frame (x, y).</summary>
        /// <param name="world">World position, meters.</param>
        /// <returns>Field (x, y), meters.</returns>
        public static Vector2 ConvertWorldToField(Vector3 world)
        {
            return new Vector2(world.x + FieldLengthM / 2, world.z + FieldWidthM / 2);
        }

        /// <summary>
        /// Converts a field-frame heading to the Unity rotation that turns local +Z (an object's forward) to face it.
        /// </summary>
        /// <param name="headingFieldRad">Field heading, radians, CCW positive (0 faces +x, toward red).</param>
        /// <returns>World rotation about +Y.</returns>
        public static Quaternion ConvertHeadingToRotation(double headingFieldRad)
        {
            // field direction (cos h, sin h) is world (cos h, 0, sin h); Unity yaw psi faces (sin psi, 0, cos psi)
            float headingDeg = (float)(headingFieldRad * Mathf.Rad2Deg);
            float yawDeg = 90f - headingDeg;
            return Quaternion.Euler(0, yawDeg, 0);
        }

        /// <summary>Converts a Unity yaw back to a field heading.</summary>
        /// <param name="yawDeg">Rotation about world +Y, degrees.</param>
        /// <returns>Field heading, radians, CCW positive.</returns>
        public static double ConvertYawToHeadingRad(float yawDeg)
        {
            return (90f - yawDeg) * Mathf.Deg2Rad;
        }
    }

    /// <summary>
    /// Where each driver station is, from the AdvantageScope 2026 field config, in field frame. Stations are indexed
    /// 0..2 = Blue 1..3 and 3..5 = Red 1..3; anything else means the default station.
    /// </summary>
    public static class DriverStations
    {
        public const int DefaultStation = 1; // Blue 2, used when the driver station has not chosen one
        private const float StartLineXM = 3.56f; // meters from the own wall to the robot center behind the start line

        // station y positions, field frame meters, by station index
        private static readonly float[] StationYFieldM = { 7.101f, 5.272f, 2.193f, 0.942f, 2.771f, 5.850f };

        /// <summary>Display name, e.g. "Blue 2".</summary>
        /// <param name="station">Station index.</param>
        public static string GetName(int station)
        {
            int index = ConvertToValidStation(station);
            if (index >= 3)
            {
                return "Red " + (index - 2);
            }
            return "Blue " + (index + 1);
        }

        /// <summary>Wire name for the bridge's station command, e.g. "B2".</summary>
        /// <param name="station">Station index.</param>
        public static string GetWireName(int station)
        {
            int index = ConvertToValidStation(station);
            if (index >= 3)
            {
                return "R" + (index - 2);
            }
            return "B" + (index + 1);
        }

        /// <summary>Whether a station is on the red alliance.</summary>
        /// <param name="station">Station index.</param>
        public static bool IsRed(int station)
        {
            int index = ConvertToValidStation(station);
            return index >= 3;
        }

        /// <summary>Field-frame y of a station, meters.</summary>
        /// <param name="station">Station index.</param>
        public static float GetYFieldM(int station)
        {
            int index = ConvertToValidStation(station);
            return StationYFieldM[index];
        }

        /// <summary>Field-frame x of a station's alliance wall, meters.</summary>
        /// <param name="station">Station index.</param>
        public static float GetWallXFieldM(int station)
        {
            if (IsRed(station))
            {
                return FieldFrame.FieldLengthM;
            }
            return 0;
        }

        /// <summary>Field heading pointing downfield from a station (straight along x), radians.</summary>
        /// <param name="station">Station index.</param>
        public static double GetDownfieldHeadingRad(int station)
        {
            if (IsRed(station))
            {
                return Mathf.PI;
            }
            return 0;
        }

        /// <summary>
        /// The bridge command that puts the robot behind its alliance's starting line, in line with a station, facing
        /// downfield.
        /// </summary>
        /// <param name="station">Station index.</param>
        /// <returns>A resetPose command without its sequence number.</returns>
        public static string MakeStartPoseCommand(int station)
        {
            double xFieldM = StartLineXM;
            if (IsRed(station))
            {
                xFieldM = FieldFrame.FieldLengthM - StartLineXM;
            }
            double yFieldM = GetYFieldM(station);
            double headingFieldRad = GetDownfieldHeadingRad(station);
            return string.Format(CultureInfo.InvariantCulture, "resetPose {0:F3} {1:F3} {2:F4}", xFieldM, yFieldM,
                headingFieldRad);
        }

        /// <summary>A usable station index: the given one, or the default when it is out of range.</summary>
        private static int ConvertToValidStation(int station)
        {
            if (station < 0 || station > 5)
            {
                return DefaultStation;
            }
            return station;
        }
    }
}
