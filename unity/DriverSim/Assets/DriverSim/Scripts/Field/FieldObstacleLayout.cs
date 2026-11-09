using System.Collections.Generic;

namespace DriverSim.Field
{
    /// <summary>How an obstacle blocks the robot.</summary>
    public enum ObstacleKind
    {
        Solid, // always blocks
        LowCeiling, // blocks only while the robot is too tall to pass under it (hood raised)
    }

    /// <summary>
    /// One axis-aligned rectangular obstacle in the field frame (wpiBlue origin), meters. Immutable.
    /// </summary>
    public readonly struct ObstacleSpec
    {
        /// <summary>Creates an obstacle from its extents; see the properties for units and frames.</summary>
        public ObstacleSpec(string name, float minXFieldM, float maxXFieldM, float minYFieldM, float maxYFieldM,
            ObstacleKind kind)
        {
            Name = name;
            MinXFieldM = minXFieldM;
            MaxXFieldM = maxXFieldM;
            MinYFieldM = minYFieldM;
            MaxYFieldM = maxYFieldM;
            Kind = kind;
        }

        /// <summary>Unique name, used for the collider's GameObject.</summary>
        public string Name { get; }

        /// <summary>Smallest field x, meters.</summary>
        public float MinXFieldM { get; }

        /// <summary>Largest field x, meters.</summary>
        public float MaxXFieldM { get; }

        /// <summary>Smallest field y, meters.</summary>
        public float MinYFieldM { get; }

        /// <summary>Largest field y, meters.</summary>
        public float MaxYFieldM { get; }

        /// <summary>How the obstacle blocks the robot.</summary>
        public ObstacleKind Kind { get; }

        /// <summary>Center field x, meters.</summary>
        public float GetCenterXFieldM()
        {
            return (MinXFieldM + MaxXFieldM) / 2;
        }

        /// <summary>Center field y, meters.</summary>
        public float GetCenterYFieldM()
        {
            return (MinYFieldM + MaxYFieldM) / 2;
        }

        /// <summary>Extent along field x, meters.</summary>
        public float GetSizeXFieldM()
        {
            return MaxXFieldM - MinXFieldM;
        }

        /// <summary>Extent along field y, meters.</summary>
        public float GetSizeYFieldM()
        {
            return MaxYFieldM - MinYFieldM;
        }
    }

    /// <summary>
    /// Every obstacle the robot can run into, as rectangles in the field frame: the perimeter, and the hub, trench
    /// blocks, trench bars and tower uprights on both alliance sides. Extracted from the Field2026 model's part bounds
    /// and cross-checked against the robot code's FieldConstants. This table is the only obstacle source; the scene
    /// builder turns it into colliders.
    /// </summary>
    public static class FieldObstacleLayout
    {
        public const float ColliderHeightM = 0.6f; // meters, obstacle collider height above the carpet
        public const float PerimeterThicknessM = 1f; // meters, thickness of each perimeter box outside the field

        // blue side; the trench's left half and the whole red side are derived from these
        private static readonly ObstacleSpec BlueHub = new ObstacleSpec("BlueHub", 4.02f, 5.21f, 3.42f, 4.62f,
            ObstacleKind.Solid); // whole hub base
        private static readonly ObstacleSpec BlueRightTrenchBlock = new ObstacleSpec("BlueRightTrenchBlock", 4.02f,
            5.21f, 1.27f, 1.57f, ObstacleKind.Solid); // wall between the trench lane and the bump
        private static readonly ObstacleSpec BlueRightTrenchBar = new ObstacleSpec("BlueRightTrenchBar", 4.54f, 4.69f,
            0.00f, 1.27f, ObstacleKind.LowCeiling); // bar across the lane, underside 0.57 m
        private static readonly ObstacleSpec BlueTowerUprightA = new ObstacleSpec("BlueTowerUprightA", 1.01f, 1.10f,
            3.28f, 3.32f, ObstacleKind.Solid); // rungs and braces are above robot height
        private static readonly ObstacleSpec BlueTowerUprightB = new ObstacleSpec("BlueTowerUprightB", 1.01f, 1.10f,
            4.14f, 4.18f, ObstacleKind.Solid);

        /// <summary>
        /// Every obstacle: blue parts, the y-mirrored left trench parts, red twins of all of those by point reflection
        /// through the field center, and the four perimeter boxes.
        /// </summary>
        /// <returns>New list of obstacles, field frame.</returns>
        public static List<ObstacleSpec> GetAll()
        {
            // blue side, including the left trench mirrored in y
            List<ObstacleSpec> blue = new List<ObstacleSpec>();
            blue.Add(BlueHub);
            blue.Add(BlueRightTrenchBlock);
            blue.Add(BlueRightTrenchBar);
            blue.Add(BlueTowerUprightA);
            blue.Add(BlueTowerUprightB);
            blue.Add(MakeLeftMirror(BlueRightTrenchBlock, "BlueLeftTrenchBlock"));
            blue.Add(MakeLeftMirror(BlueRightTrenchBar, "BlueLeftTrenchBar"));

            // red side: point reflection of every blue obstacle
            List<ObstacleSpec> all = new List<ObstacleSpec>(blue);
            foreach (ObstacleSpec blueObstacle in blue)
            {
                all.Add(MakeRedTwin(blueObstacle));
            }

            // perimeter: four boxes just outside the field, overlapping at the corners
            float length = FieldFrame.FieldLengthM;
            float width = FieldFrame.FieldWidthM;
            float thick = PerimeterThicknessM;
            all.Add(new ObstacleSpec("WallBlue", -thick, 0, -thick, width + thick, ObstacleKind.Solid));
            all.Add(new ObstacleSpec("WallRed", length, length + thick, -thick, width + thick, ObstacleKind.Solid));
            all.Add(new ObstacleSpec("GuardrailRight", -thick, length + thick, -thick, 0, ObstacleKind.Solid));
            all.Add(new ObstacleSpec("GuardrailLeft", -thick, length + thick, width, width + thick,
                ObstacleKind.Solid));
            return all;
        }

        // ---- low-level helpers (static, no side effects)

        /// <summary>The same obstacle mirrored across the field's long axis (y to width - y).</summary>
        /// <param name="spec">Obstacle to mirror.</param>
        /// <param name="mirrorName">Name of the mirrored obstacle.</param>
        private static ObstacleSpec MakeLeftMirror(ObstacleSpec spec, string mirrorName)
        {
            float mirroredMinY = FieldFrame.FieldWidthM - spec.MaxYFieldM;
            float mirroredMaxY = FieldFrame.FieldWidthM - spec.MinYFieldM;
            return new ObstacleSpec(mirrorName, spec.MinXFieldM, spec.MaxXFieldM, mirroredMinY, mirroredMaxY,
                spec.Kind);
        }

        /// <summary>
        /// The red twin of a blue obstacle: point reflection through the field center, x to length - x and y to
        /// width - y, the same mapping as the robot code's FieldConstants.redElement.
        /// </summary>
        /// <param name="spec">Blue obstacle.</param>
        private static ObstacleSpec MakeRedTwin(ObstacleSpec spec)
        {
            float length = FieldFrame.FieldLengthM;
            float width = FieldFrame.FieldWidthM;
            string redName = spec.Name.Replace("Blue", "Red");
            return new ObstacleSpec(redName, length - spec.MaxXFieldM, length - spec.MinXFieldM,
                width - spec.MaxYFieldM, width - spec.MinYFieldM, spec.Kind);
        }
    }
}
