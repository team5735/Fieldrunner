using DriverSim.Field;
using DriverSim.Util;
using UnityEngine;

namespace DriverSim.Game
{
    /// <summary>
    /// Debug overlay that draws every obstacle collider as a translucent box (cyan for Solid, yellow for LowCeiling) so
    /// collider placement can be checked against the field model. Toggled with F2 or in Options, or shown from the
    /// start with the command-line flag <c>--show-colliders</c>.
    /// </summary>
    public sealed class ColliderDebugView : MonoBehaviour
    {
        [Tooltip("Translucent material for Solid obstacles.")]
        [SerializeField] private Material solidMaterial;

        [Tooltip("Translucent material for LowCeiling obstacles.")]
        [SerializeField] private Material lowCeilingMaterial;

        private GameObject[] boxes = new GameObject[0]; // one drawn box per obstacle collider
        private bool isShown; // boxes are visible

        /// <summary>Sets the materials the boxes use; call before the scene starts.</summary>
        /// <param name="solid">Material for Solid obstacles.</param>
        /// <param name="lowCeiling">Material for LowCeiling obstacles.</param>
        public void SetReferences(Material solid, Material lowCeiling)
        {
            solidMaterial = solid;
            lowCeilingMaterial = lowCeiling;
        }

        /// <summary>Shows or hides every box.</summary>
        /// <param name="shown">True to show.</param>
        public void SetShown(bool shown)
        {
            isShown = shown;
            foreach (GameObject box in boxes)
            {
                if (box != null)
                {
                    box.SetActive(shown);
                }
            }
        }

        /// <summary>Whether the boxes are visible.</summary>
        public bool IsShown()
        {
            return isShown;
        }

        // ---- Unity messages

        private void Start()
        {
            BuildBoxes();
            SetShown(CommandLine.HasFlag("--show-colliders"));
        }

        // ---- private stateful helpers

        /// <summary>Creates a drawn box as a child of every obstacle collider's object.</summary>
        private void BuildBoxes()
        {
            FieldObstacle[] obstacles = FindObjectsByType<FieldObstacle>();
            boxes = new GameObject[obstacles.Length];
            for (int i = 0; i < obstacles.Length; i++)
            {
                FieldObstacle obstacle = obstacles[i];
                BoxCollider obstacleCollider = obstacle.GetComponent<BoxCollider>();
                if (obstacleCollider == null)
                {
                    continue;
                }

                // a cube the size of the collider, without a collider of its own
                GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
                box.name = "ColliderDebugBox";
                Collider boxCollider = box.GetComponent<Collider>();
                Destroy(boxCollider);
                box.transform.SetParent(obstacle.transform, false);
                box.transform.localPosition = obstacleCollider.center;
                box.transform.localScale = obstacleCollider.size;
                Material material = solidMaterial;
                if (obstacle.GetKind() == ObstacleKind.LowCeiling)
                {
                    material = lowCeilingMaterial;
                }
                box.GetComponent<Renderer>().sharedMaterial = material;
                boxes[i] = box;
            }
        }
    }
}
