using System.Collections.Generic;
using UnityEngine;

namespace DriverSim.Field
{
    /// <summary>
    /// Prepares the AdvantageScope field model at startup: hides the model's staged fuel (the game spawns its own fuel)
    /// and records where those balls were, and finds the driver station acrylic so the camera can hide the panels it
    /// looks through.
    /// </summary>
    public sealed class FieldSetup : MonoBehaviour
    {
        private const string StagedFuelPartNumber = "GE-26900"; // staged fuel nodes are named "GE-26900: Fuel"
        private const string StagedFuelWord = "Fuel"; // second half of the staged fuel node name
        private const string DriverStationAcrylicPartNumber = "FE-00012"; // "FE-00012: Driver Station Acrylic"

        [Tooltip("The instantiated Field2026 model.")]
        [SerializeField] private Transform fieldModel;

        private readonly List<Vector3> stagedFuelPositions = new List<Vector3>(); // world positions, meters
        private readonly List<GameObject> driverStationAcrylic = new List<GameObject>(); // panels on both walls

        /// <summary>Sets the field model to prepare; call before the scene starts.</summary>
        /// <param name="model">Root of the instantiated Field2026 model.</param>
        public void SetFieldModel(Transform model)
        {
            fieldModel = model;
        }

        /// <summary>World positions where the field model stages fuel at match start.</summary>
        /// <returns>Ball centers, meters, world space.</returns>
        public IReadOnlyList<Vector3> GetStagedFuelPositions()
        {
            return stagedFuelPositions;
        }

        /// <summary>
        /// Hides the driver station acrylic on the wall nearest a viewpoint and shows it elsewhere. The model draws the
        /// acrylic as 20%-opaque light gray, which looks like frosted glass across the whole view when the camera stands
        /// behind it; real polycarbonate is close to clear.
        /// </summary>
        /// <param name="viewWorldX">Unity world X of the viewpoint, meters; panels on the same side of the field are
        /// hidden. Pass 0 to show all panels.</param>
        public void HideAcrylicNear(float viewWorldX)
        {
            float viewSide = Mathf.Sign(viewWorldX);
            foreach (GameObject panel in driverStationAcrylic)
            {
                float panelSide = Mathf.Sign(panel.transform.position.x);
                bool isSameSide = viewWorldX != 0 && panelSide == viewSide;
                Renderer[] renderers = panel.GetComponentsInChildren<Renderer>(true);
                foreach (Renderer panelRenderer in renderers)
                {
                    panelRenderer.enabled = !isSameSide;
                }
            }
        }

        // ---- Unity messages

        private void Awake()
        {
            if (fieldModel == null)
            {
                Debug.LogWarning("[Driver-Sim] FieldSetup has no field model assigned");
                return;
            }
            SortFieldParts(fieldModel);
            Debug.Log("[Driver-Sim] hid " + stagedFuelPositions.Count + " staged fuel balls from the field model");
        }

        // ---- private stateful helpers

        /// <summary>
        /// Walks the field model: hides staged fuel (remembering where it was) and collects the driver station acrylic.
        /// </summary>
        /// <param name="node">Model node to examine, with its children.</param>
        private void SortFieldParts(Transform node)
        {
            // staged fuel: remember the ball's center, then hide it
            bool hasFuelPartNumber = node.name.StartsWith(StagedFuelPartNumber);
            bool hasFuelWord = node.name.Contains(StagedFuelWord);
            bool isStagedFuel = hasFuelPartNumber && hasFuelWord;
            if (isStagedFuel)
            {
                Renderer[] renderers = node.GetComponentsInChildren<Renderer>();
                if (renderers.Length > 0)
                {
                    stagedFuelPositions.Add(renderers[0].bounds.center);
                }
                node.gameObject.SetActive(false);
                return;
            }

            // driver station acrylic: shown or hidden per view by HideAcrylicNear
            if (node.name.StartsWith(DriverStationAcrylicPartNumber))
            {
                driverStationAcrylic.Add(node.gameObject);
                return;
            }

            // everything else: look inside
            for (int i = 0; i < node.childCount; i++)
            {
                Transform child = node.GetChild(i);
                SortFieldParts(child);
            }
        }
    }
}
