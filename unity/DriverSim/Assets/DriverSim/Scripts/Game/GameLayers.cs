using UnityEngine;

namespace DriverSim.Game
{
    /// <summary>
    /// The physics layers fuel needs and the masks built from them. The scene builder registers the names at these
    /// indices; <see cref="FuelField"/> applies the collision matrix at startup.
    ///
    /// Fuel collides with the carpet, solid obstacles, hubs, bump surfaces, other fuel and the robot pusher; it
    /// ignores the LowCeiling layer (trench bars). The robot pusher collides with fuel only.
    /// </summary>
    public static class GameLayers
    {
        public const int Fuel = 8; // layer index of the fuel balls
        public const int RobotPusher = 9; // layer index of the kinematic box that shoves fuel
        public const int LowCeiling = 10; // layer index of the trench bar colliders
        public const string FuelName = "Fuel"; // name registered at <see cref="Fuel"/>
        public const string RobotPusherName = "RobotPusher"; // name registered at <see cref="RobotPusher"/>
        public const string LowCeilingName = "LowCeiling"; // name registered at <see cref="LowCeiling"/>

        /// <summary>Mask of the fuel layer, for queries that look for fuel.</summary>
        public static int GetFuelMask()
        {
            return 1 << Fuel;
        }

        /// <summary>Mask of every layer except fuel and the pusher, for the robot's own obstacle queries.</summary>
        public static int GetRobotQueryMask()
        {
            int excluded = (1 << Fuel) | (1 << RobotPusher);
            return ~excluded;
        }

        /// <summary>Sets which layers collide: fuel ignores the trench bars; the pusher meets only fuel.</summary>
        public static void ApplyCollisionMatrix()
        {
            Physics.IgnoreLayerCollision(Fuel, LowCeiling, true);
            for (int layer = 0; layer < 32; layer++)
            {
                if (layer != Fuel)
                {
                    Physics.IgnoreLayerCollision(RobotPusher, layer, true);
                }
            }
            Physics.IgnoreLayerCollision(RobotPusher, Fuel, false);
        }
    }
}
