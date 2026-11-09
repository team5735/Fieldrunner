using System.Collections.Generic;
using UnityEngine;

namespace DriverSim.Field
{
    /// <summary>
    /// Prepares the AdvantageScope field model at startup: hides the model's staged fuel (the game spawns its own fuel)
    /// and records where those balls were, so gameplay can stage fuel in the same spots.
    /// </summary>
    public sealed class FieldSetup : MonoBehaviour
    {
        private const string StagedFuelPartNumber = "GE-26900"; // staged fuel nodes are named "GE-26900: Fuel"
        private const string StagedFuelWord = "Fuel";

        [Tooltip("The instantiated Field2026 model.")]
        public Transform fieldModel;

        private const string DriverStationAcrylicPartNumber = "FE-00012"; // "FE-00012: Driver Station Acrylic"

        private readonly List<Vector3> stagedFuelPositions = new List<Vector3>(); // world positions, meters
        private readonly List<GameObject> driverStationAcrylic = new List<GameObject>(); // panels on both walls

        /// <summary>World positions where the field model stages fuel at match start.</summary>
        public IReadOnlyList<Vector3> StagedFuelPositions
        {
            get { return stagedFuelPositions; }
        }

        /// <summary>
        /// Hides the driver station acrylic on the wall nearest a viewpoint and shows it elsewhere. The model draws the
        /// acrylic as 20%-opaque light gray, which looks like frosted glass across the whole view when the camera stands
        /// behind it; real polycarbonate is close to clear.
        /// </summary>
        /// <param name="viewWorldX">Unity world X of the viewpoint; panels on the same side of the field are hidden.
        /// Pass 0 to show all panels.</param>
        public void HideAcrylicNear(float viewWorldX)
        {
            foreach (GameObject panel in driverStationAcrylic)
            {
                bool isSameSide = viewWorldX != 0 && Mathf.Sign(panel.transform.position.x) == Mathf.Sign(viewWorldX);
                Renderer[] renderers = panel.GetComponentsInChildren<Renderer>(true);
                foreach (Renderer panelRenderer in renderers)
                {
                    panelRenderer.enabled = !isSameSide;
                }
            }
        }

        private void Awake()
        {
            if (fieldModel == null)
            {
                Debug.LogWarning("[Driver-Sim] FieldSetup has no field model assigned");
                return;
            }
            HideStagedFuel(fieldModel);
            Debug.Log("[Driver-Sim] hid " + stagedFuelPositions.Count + " staged fuel balls from the field model");
        }

        private void HideStagedFuel(Transform node)
        {
            bool isStagedFuel = node.name.StartsWith(StagedFuelPartNumber) && node.name.Contains(StagedFuelWord);
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
            if (node.name.StartsWith(DriverStationAcrylicPartNumber))
            {
                driverStationAcrylic.Add(node.gameObject);
                return;
            }
            for (int i = 0; i < node.childCount; i++)
            {
                HideStagedFuel(node.GetChild(i));
            }
        }
    }
}
