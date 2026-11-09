using UnityEngine;

namespace DriverSim.Field
{
    /// <summary>
    /// Marks a field-model mesh collider as a bump surface: the robot rides over it (the drawn robot rises and tilts)
    /// but it never blocks the robot, so it carries no <see cref="FieldObstacle"/>.
    /// </summary>
    public sealed class BumpSurface : MonoBehaviour
    {
    }
}
