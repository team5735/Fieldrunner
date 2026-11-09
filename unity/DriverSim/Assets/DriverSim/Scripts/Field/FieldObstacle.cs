using UnityEngine;

namespace DriverSim.Field
{
    /// <summary>
    /// Marks a collider as something the robot cannot drive through. Only colliders with this component take part in
    /// robot collisions, so moving obstacles (fuel, other robots) can join later by adding it.
    /// </summary>
    public sealed class FieldObstacle : MonoBehaviour
    {
        [Tooltip("Whether the obstacle always blocks, or only a robot with its hood raised.")]
        [SerializeField] private ObstacleKind kind = ObstacleKind.Solid;

        /// <summary>Sets how the obstacle blocks the robot; call when building the scene.</summary>
        /// <param name="obstacleKind">Blocking behavior.</param>
        public void SetKind(ObstacleKind obstacleKind)
        {
            kind = obstacleKind;
        }

        /// <summary>How the obstacle blocks the robot.</summary>
        public ObstacleKind GetKind()
        {
            return kind;
        }
    }
}
