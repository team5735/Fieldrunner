using DriverSim.Field;
using DriverSim.Net;
using DriverSim.Util;
using UnityEngine;

namespace DriverSim.Game
{
    /// <summary>
    /// Puts the camera where a driver stands: behind the selected driver station, at eye height, looking toward the
    /// center of the field (so side stations angle inward). The station comes from the robot sim's driver station
    /// (chosen in the game's station picker, or in the sim GUI). An overhead view is available for debugging.
    /// Command line:
    /// <c>--view overhead</c> starts in the overhead view; <c>--no-post</c> turns post-processing off.
    /// </summary>
    public sealed class DriverStationCamera : MonoBehaviour
    {
        /// <summary>Camera views.</summary>
        public enum View
        {
            DriverStation,
            Overhead,
        }

        private const float MinFieldOfViewDeg = 30f; // narrowest allowed vertical field of view, degrees
        private const float MaxFieldOfViewDeg = 100f; // widest allowed vertical field of view, degrees
        private const float OverheadHeightM = 17f; // overhead camera height above the field center, meters
        private const float OverheadFieldOfViewDeg = 56f; // overhead vertical field of view, degrees

        [Tooltip("Link to the bridge; the driver station comes from here.")]
        [SerializeField] private DriverSimLink link;

        [Tooltip("Field setup, to hide the driver station acrylic the camera looks through.")]
        [SerializeField] private FieldSetup fieldSetup;

        [Tooltip("Driver eye height above the carpet, meters.")]
        [SerializeField] private float eyeHeightM = 1.7f;

        [Tooltip("How far behind the alliance wall the driver stands, meters.")]
        [SerializeField] private float behindWallM = 0.45f;

        [Tooltip("Downward tilt of the driver's view, degrees.")]
        [SerializeField] private float pitchDownDeg = 9f;

        [Tooltip("Vertical field of view, degrees.")]
        [SerializeField] private float fieldOfViewDeg = 55f;

        private Camera cam; // the camera this drives
        private View view = View.DriverStation; // current view

        /// <summary>Sets the references the camera needs; call before the scene starts.</summary>
        /// <param name="driverSimLink">Link to the bridge.</param>
        /// <param name="setup">Field setup that owns the driver station acrylic.</param>
        public void SetReferences(DriverSimLink driverSimLink, FieldSetup setup)
        {
            link = driverSimLink;
            fieldSetup = setup;
        }

        /// <summary>Current view.</summary>
        public View GetView()
        {
            return view;
        }

        /// <summary>Switches to the next view.</summary>
        public void CycleView()
        {
            if (view == View.DriverStation)
            {
                view = View.Overhead;
            }
            else
            {
                view = View.DriverStation;
            }
        }

        /// <summary>Switches to a view.</summary>
        /// <param name="newView">View to show.</param>
        public void SetView(View newView)
        {
            view = newView;
        }

        /// <summary>The driver's vertical field of view, degrees.</summary>
        public float GetFieldOfViewDeg()
        {
            return fieldOfViewDeg;
        }

        /// <summary>Sets the driver's vertical field of view, within limits.</summary>
        /// <param name="requestedDeg">Vertical field of view, degrees.</param>
        public void SetFieldOfViewDeg(float requestedDeg)
        {
            fieldOfViewDeg = Mathf.Clamp(requestedDeg, MinFieldOfViewDeg, MaxFieldOfViewDeg);
        }

        /// <summary>Widens or narrows the driver's field of view, within limits.</summary>
        /// <param name="deltaDeg">Change in vertical field of view, degrees.</param>
        public void AdjustFieldOfView(float deltaDeg)
        {
            float requestedDeg = fieldOfViewDeg + deltaDeg;
            fieldOfViewDeg = Mathf.Clamp(requestedDeg, MinFieldOfViewDeg, MaxFieldOfViewDeg);
        }

        // ---- Unity messages

        private void Awake()
        {
            cam = GetComponent<Camera>();

            // command-line options
            string viewArgument = CommandLine.GetValue("--view");
            if (viewArgument == "overhead")
            {
                view = View.Overhead;
            }
            if (CommandLine.HasFlag("--no-post"))
            {
                GameObject post = GameObject.Find("PostProcessing");
                if (post != null)
                {
                    post.SetActive(false);
                }
            }
        }

        private void LateUpdate()
        {
            // station chosen in the driver station, or the default
            int station = DriverStations.DefaultStation;
            if (link != null)
            {
                MatchState match = link.GetMatchState();
                if (match.Station >= 0)
                {
                    station = match.Station;
                }
            }

            // place the camera
            if (view == View.Overhead)
            {
                PlaceOverhead(station);
            }
            else
            {
                PlaceAtDriverStation(station);
            }

            // the acrylic right in front of the camera would fog the whole view
            if (fieldSetup != null)
            {
                float nearWallX = 0;
                if (view == View.DriverStation)
                {
                    nearWallX = transform.position.x;
                }
                fieldSetup.HideAcrylicNear(nearWallX);
            }
        }

        // ---- private stateful helpers

        /// <summary>Stands the camera behind a driver station, at eye height, facing the field center.</summary>
        /// <param name="station">Station index, 0..5.</param>
        private void PlaceAtDriverStation(int station)
        {
            // stand behind the wall
            double downfieldRad = DriverStations.GetDownfieldHeadingRad(station);
            double wallXFieldM = DriverStations.GetWallXFieldM(station);
            double behindXFieldM = wallXFieldM - Mathf.Cos((float)downfieldRad) * behindWallM;
            double stationYFieldM = DriverStations.GetYFieldM(station);
            transform.position = FieldFrame.ConvertFieldToWorld(behindXFieldM, stationYFieldM, eyeHeightM);

            // face the center of the field from whichever station the driver is in
            double toCenterXM = FieldFrame.FieldLengthM / 2 - behindXFieldM;
            double toCenterYM = FieldFrame.FieldWidthM / 2 - stationYFieldM;
            double facingFieldRad = System.Math.Atan2(toCenterYM, toCenterXM);
            Quaternion facing = FieldFrame.ConvertHeadingToRotation(facingFieldRad);
            Quaternion pitch = Quaternion.Euler(pitchDownDeg, 0, 0);
            transform.rotation = facing * pitch;
            cam.fieldOfView = fieldOfViewDeg;
        }

        /// <summary>
        /// Puts the camera high above the field center, the selected alliance at the bottom of the screen.
        /// </summary>
        /// <param name="station">Station index, 0..5.</param>
        private void PlaceOverhead(int station)
        {
            transform.position = new Vector3(0, OverheadHeightM, 0);
            double downfieldRad = DriverStations.GetDownfieldHeadingRad(station);
            Quaternion facing = FieldFrame.ConvertHeadingToRotation(downfieldRad);
            Quaternion lookDown = Quaternion.Euler(90, 0, 0);
            transform.rotation = facing * lookDown;
            cam.fieldOfView = OverheadFieldOfViewDeg;
        }
    }
}
