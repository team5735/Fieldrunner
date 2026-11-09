using DriverSim.Field;
using DriverSim.Net;
using UnityEngine;

namespace DriverSim.Game
{
    /// <summary>
    /// Puts the camera where a driver stands: behind the selected driver station, at eye height, looking straight
    /// down the field's long (x) axis. The station comes from the robot sim's driver station (set it from the sim GUI or
    /// the game's 1-6 keys). An overhead view is available for debugging.
    /// </summary>
    public sealed class DriverStationCamera : MonoBehaviour
    {
        /// <summary>Camera views.</summary>
        public enum View
        {
            DriverStation,
            Overhead,
        }

        [Tooltip("Link to the bridge; the driver station comes from here.")]
        public DriverSimLink link;

        [Tooltip("Driver eye height above the carpet, meters.")]
        public float eyeHeightM = 1.7f;

        [Tooltip("How far behind the alliance wall the driver stands, meters.")]
        public float behindWallM = 0.45f;

        [Tooltip("Downward tilt of the driver's view, degrees.")]
        public float pitchDownDeg = 9f;

        [Tooltip("Vertical field of view, degrees.")]
        public float fieldOfViewDeg = 55f;

        private Camera cam; // the camera this drives
        private View view = View.DriverStation; // current view

        /// <summary>Current view.</summary>
        public View CurrentView
        {
            get { return view; }
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

        /// <summary>Widens or narrows the driver's field of view.</summary>
        /// <param name="deltaDeg">Change in vertical field of view, degrees.</param>
        public void AdjustFieldOfView(float deltaDeg)
        {
            fieldOfViewDeg = Mathf.Clamp(fieldOfViewDeg + deltaDeg, 30f, 100f);
        }

        private void Awake()
        {
            cam = GetComponent<Camera>();
            if (Util.CommandLine.GetValue("--view") == "overhead")
            {
                view = View.Overhead;
            }
        }

        private void LateUpdate()
        {
            int station = DriverStations.DefaultStation;
            if (link != null && link.Match.Station >= 0)
            {
                station = link.Match.Station;
            }

            if (view == View.Overhead)
            {
                PlaceOverhead(station);
                return;
            }
            PlaceAtDriverStation(station);
        }

        private void PlaceAtDriverStation(int station)
        {
            // stand behind the wall, facing downfield
            double facingRad = DriverStations.GetFacingRad(station);
            double behindX = DriverStations.GetWallXM(station) - Mathf.Cos((float)facingRad) * behindWallM;
            transform.position = FieldFrame.ToWorld(behindX, DriverStations.GetYM(station), eyeHeightM);
            Quaternion facing = FieldFrame.HeadingToRotation(facingRad);
            transform.rotation = facing * Quaternion.Euler(pitchDownDeg, 0, 0);
            cam.fieldOfView = fieldOfViewDeg;
        }

        private void PlaceOverhead(int station)
        {
            // high above the field center, with the selected alliance at the bottom of the screen
            transform.position = new Vector3(0, 17f, 0);
            Quaternion facing = FieldFrame.HeadingToRotation(DriverStations.GetFacingRad(station));
            transform.rotation = facing * Quaternion.Euler(90, 0, 0);
            cam.fieldOfView = 50f;
        }
    }
}
