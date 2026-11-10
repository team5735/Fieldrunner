using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using UnityEngine.Rendering;
using Debug = UnityEngine.Debug;

namespace DriverSim.Field
{
    /// <summary>
    /// Prepares the AdvantageScope field model at startup: hides the model's staged fuel (the game spawns its own fuel)
    /// and records where those balls were, and finds the driver station acrylic so the camera can hide the panels it
    /// looks through. Then merges the remaining field meshes into one mesh per material per region of the field, so
    /// the field costs a few dozen draw calls instead of thousands.
    /// </summary>
    public sealed class FieldSetup : MonoBehaviour
    {
        private const string StagedFuelPartNumber = "GE-26900"; // staged fuel nodes are named "GE-26900: Fuel"
        private const string StagedFuelWord = "Fuel"; // second half of the staged fuel node name
        private const string DriverStationAcrylicPartNumber = "FE-00012"; // "FE-00012: Driver Station Acrylic"
        private const string BumpPartNumber = "GE-26100"; // bump nodes keep their own renderers and colliders
        private const int GridColumns = 4; // merge regions along the field's length
        private const int GridRows = 2; // merge regions across the field's width
        private const int TransparentRenderQueue = 2500; // materials at or above this draw sorted back to front
        private const int ShadowModeCount = 4; // values of ShadowCastingMode, for packing a group key

        [Tooltip("The instantiated Field2026 model.")]
        [SerializeField] private Transform fieldModel;

        private readonly List<Vector3> stagedFuelPositions = new List<Vector3>(); // world positions, meters
        private readonly List<GameObject> driverStationAcrylic = new List<GameObject>(); // panels on both walls
        private int rendererCount; // enabled field renderers after the merge
        private long triangleCount; // triangles those renderers draw

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

        /// <summary>Number of enabled renderers in the field model and the merged field meshes.</summary>
        /// <returns>Renderer count after the merge.</returns>
        public int GetRendererCount()
        {
            return rendererCount;
        }

        /// <summary>Number of triangles those renderers draw.</summary>
        /// <returns>Triangle count after the merge.</returns>
        public long GetTriangleCount()
        {
            return triangleCount;
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
            MergeFieldMeshes();
        }

        // ---- private stateful helpers

        /// <summary>
        /// Combines the field's mesh renderers into one mesh per material, shadow mode and grid cell, and disables the
        /// originals. Bumps, driver station acrylic and parts with transparent materials keep their own renderers. Logs the renderer and triangle
        /// counts before and after, and the time taken.
        /// </summary>
        private void MergeFieldMeshes()
        {
            Stopwatch timer = Stopwatch.StartNew();

            // gather the parts to merge, grouped by material, then by shadow mode and grid cell
            MeshRenderer[] renderers = fieldModel.GetComponentsInChildren<MeshRenderer>(false);
            int beforeCount = 0;
            long beforeTriangles = 0;
            long keptTriangles = 0;
            Dictionary<Material, Dictionary<int, List<CombineInstance>>> groups =
                new Dictionary<Material, Dictionary<int, List<CombineInstance>>>();
            List<MeshRenderer> merged = new List<MeshRenderer>();
            foreach (MeshRenderer partRenderer in renderers)
            {
                if (!partRenderer.enabled)
                {
                    continue;
                }
                MeshFilter filter = partRenderer.GetComponent<MeshFilter>();
                if (filter == null || filter.sharedMesh == null)
                {
                    continue;
                }
                beforeCount++;
                long triangles = CountTriangles(filter.sharedMesh);
                beforeTriangles += triangles;
                bool isKept = IsKeptWhole(partRenderer.transform) || HasTransparentMaterial(partRenderer);
                if (isKept)
                {
                    keptTriangles += triangles;
                    continue;
                }
                AddPartToGroups(groups, partRenderer, filter.sharedMesh);
                merged.Add(partRenderer);
            }

            // build one mesh per group
            Transform mergedRoot = new GameObject("FieldMerged").transform;
            mergedRoot.SetParent(fieldModel.parent, false);
            int mergedCount = 0;
            long mergedTriangles = 0;
            foreach (KeyValuePair<Material, Dictionary<int, List<CombineInstance>>> materialGroup in groups)
            {
                foreach (KeyValuePair<int, List<CombineInstance>> cellGroup in materialGroup.Value)
                {
                    Mesh combined = MakeCombinedMesh(cellGroup.Value);
                    MakeMergedObject(mergedRoot, materialGroup.Key, cellGroup.Key, combined);
                    mergedCount++;
                    mergedTriangles += CountTriangles(combined);
                }
            }

            // hide the originals
            foreach (MeshRenderer partRenderer in merged)
            {
                partRenderer.enabled = false;
            }
            int keptCount = beforeCount - merged.Count;
            rendererCount = keptCount + mergedCount;
            triangleCount = keptTriangles + mergedTriangles;
            timer.Stop();
            Debug.Log("[Driver-Sim] field: " + rendererCount + " renderers, " + triangleCount + " triangles (from "
                + beforeCount + ", " + beforeTriangles + "); merged in " + timer.ElapsedMilliseconds + " ms");
        }

        /// <summary>Adds each submesh of a renderer's mesh to the group for its material, shadow mode and cell.</summary>
        /// <param name="groups">Groups to add to, keyed by material, then by shadow mode and grid cell.</param>
        /// <param name="partRenderer">Renderer whose mesh is added, in world space.</param>
        /// <param name="mesh">The renderer's mesh.</param>
        private static void AddPartToGroups(Dictionary<Material, Dictionary<int, List<CombineInstance>>> groups,
            MeshRenderer partRenderer, Mesh mesh)
        {
            int cell = ComputeCellIndex(partRenderer.bounds.center);
            int groupKey = cell * ShadowModeCount + (int)partRenderer.shadowCastingMode;
            Material[] materials = partRenderer.sharedMaterials;
            int submeshCount = Mathf.Min(mesh.subMeshCount, materials.Length);
            for (int i = 0; i < submeshCount; i++)
            {
                Material material = materials[i];
                if (material == null)
                {
                    continue;
                }
                if (!groups.ContainsKey(material))
                {
                    groups[material] = new Dictionary<int, List<CombineInstance>>();
                }
                Dictionary<int, List<CombineInstance>> cells = groups[material];
                if (!cells.ContainsKey(groupKey))
                {
                    cells[groupKey] = new List<CombineInstance>();
                }
                CombineInstance instance = new CombineInstance();
                instance.mesh = mesh;
                instance.subMeshIndex = i;
                instance.transform = partRenderer.localToWorldMatrix;
                cells[groupKey].Add(instance);
            }
        }

        /// <summary>Makes a game object that draws a merged mesh.</summary>
        /// <param name="parent">Parent of the new object.</param>
        /// <param name="material">Material of the merged mesh.</param>
        /// <param name="groupKey">Shadow mode and grid cell of the group, as built by AddPartToGroups.</param>
        /// <param name="mesh">Merged mesh in world space.</param>
        private static void MakeMergedObject(Transform parent, Material material, int groupKey, Mesh mesh)
        {
            int cell = groupKey / ShadowModeCount;
            ShadowCastingMode shadowMode = (ShadowCastingMode)(groupKey % ShadowModeCount);
            GameObject mergedObject = new GameObject("merged " + material.name + " cell " + cell);
            mergedObject.transform.SetParent(parent, false);
            MeshFilter filter = mergedObject.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;
            MeshRenderer mergedRenderer = mergedObject.AddComponent<MeshRenderer>();
            mergedRenderer.sharedMaterial = material;
            mergedRenderer.shadowCastingMode = shadowMode;
        }

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

        // ---- private static pure helpers

        /// <summary>Combines submeshes, given in world space, into one mesh with 32-bit indices.</summary>
        /// <param name="instances">Submeshes with their world transforms.</param>
        /// <returns>The combined mesh, one submesh, vertices in world space.</returns>
        private static Mesh MakeCombinedMesh(List<CombineInstance> instances)
        {
            Mesh combined = new Mesh();
            combined.indexFormat = IndexFormat.UInt32;
            combined.CombineMeshes(instances.ToArray(), true, true);
            return combined;
        }

        /// <summary>Finds the grid cell of the field that holds a point.</summary>
        /// <param name="worldPointM">Point in Unity world space, meters.</param>
        /// <returns>Cell number, 0 to GridColumns * GridRows - 1.</returns>
        private static int ComputeCellIndex(Vector3 worldPointM)
        {
            float columnFraction = (worldPointM.x + FieldFrame.FieldLengthM / 2) / FieldFrame.FieldLengthM;
            float rowFraction = (worldPointM.z + FieldFrame.FieldWidthM / 2) / FieldFrame.FieldWidthM;
            int column = Mathf.Clamp(Mathf.FloorToInt(columnFraction * GridColumns), 0, GridColumns - 1);
            int row = Mathf.Clamp(Mathf.FloorToInt(rowFraction * GridRows), 0, GridRows - 1);
            return row * GridColumns + column;
        }

        /// <summary>Counts the triangles of all submeshes of a mesh.</summary>
        /// <param name="mesh">Mesh to count.</param>
        /// <returns>Triangle count.</returns>
        private static long CountTriangles(Mesh mesh)
        {
            long indexCount = 0;
            for (int i = 0; i < mesh.subMeshCount; i++)
            {
                indexCount += mesh.GetIndexCount(i);
            }
            return indexCount / 3;
        }

        /// <summary>Whether any material of a renderer is transparent.</summary>
        /// <param name="partRenderer">Renderer to check.</param>
        /// <returns>True when the renderer is drawn in the transparent queue and so must keep its own draw order.</returns>
        private static bool HasTransparentMaterial(MeshRenderer partRenderer)
        {
            Material[] materials = partRenderer.sharedMaterials;
            foreach (Material material in materials)
            {
                if (material != null && material.renderQueue >= TransparentRenderQueue)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>Whether a node is, or sits under, driver station acrylic or a bump.</summary>
        /// <param name="node">Field model node to check.</param>
        /// <returns>True when the node's renderers stay as they are.</returns>
        private static bool IsKeptWhole(Transform node)
        {
            Transform ancestor = node;
            while (ancestor != null)
            {
                bool isAcrylic = ancestor.name.StartsWith(DriverStationAcrylicPartNumber);
                bool isBump = ancestor.name.StartsWith(BumpPartNumber);
                if (isAcrylic || isBump)
                {
                    return true;
                }
                ancestor = ancestor.parent;
            }
            return false;
        }
    }
}
