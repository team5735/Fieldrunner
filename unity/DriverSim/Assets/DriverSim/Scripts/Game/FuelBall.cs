using UnityEngine;

namespace DriverSim.Game
{
    /// <summary>Where a fuel ball is in its life.</summary>
    public enum FuelState
    {
        Resting, // a physics ball on the field
        Flying, // a scripted arc after a launch
        Hidden, // out of play: in the hopper, scored and waiting to return, or in the pool
    }

    /// <summary>
    /// One fuel ball. <see cref="FuelField"/> creates, pools and moves them; this component holds a ball's parts and
    /// state, and the plan and clock of its flight while it flies.
    /// </summary>
    public sealed class FuelBall : MonoBehaviour
    {
        [Tooltip("Physics body; kinematic while the ball is not resting.")]
        [SerializeField] private Rigidbody body;

        [Tooltip("Collider; off while the ball flies.")]
        [SerializeField] private SphereCollider ballCollider;

        private FuelState state = FuelState.Hidden; // where the ball is in its life
        private FlightPlan flightPlan; // the arc while Flying
        private float flightS; // seconds since launch while Flying
        private int scoredHubIndex = -1; // hub the ball scored in while waiting to return; -1 otherwise
        private float returnAtS = -1; // Time.time when the ball returns after scoring

        /// <summary>Sets the ball's parts; call when the ball is made.</summary>
        /// <param name="rigidbody">Physics body.</param>
        /// <param name="sphereCollider">Collider.</param>
        public void SetParts(Rigidbody rigidbody, SphereCollider sphereCollider)
        {
            body = rigidbody;
            ballCollider = sphereCollider;
        }

        /// <summary>Physics body.</summary>
        public Rigidbody GetBody()
        {
            return body;
        }

        /// <summary>Collider.</summary>
        public SphereCollider GetCollider()
        {
            return ballCollider;
        }

        /// <summary>Where the ball is in its life.</summary>
        public FuelState GetState()
        {
            return state;
        }

        /// <summary>Sets where the ball is in its life; <see cref="FuelField"/> also switches the parts.</summary>
        /// <param name="newState">New state.</param>
        public void SetState(FuelState newState)
        {
            state = newState;
        }

        /// <summary>Starts a flight at time zero.</summary>
        /// <param name="plan">The arc to follow.</param>
        public void StartFlight(FlightPlan plan)
        {
            flightPlan = plan;
            flightS = 0;
        }

        /// <summary>The arc being followed.</summary>
        public FlightPlan GetFlightPlan()
        {
            return flightPlan;
        }

        /// <summary>Seconds since launch.</summary>
        public float GetFlightS()
        {
            return flightS;
        }

        /// <summary>Sets seconds since launch.</summary>
        /// <param name="timeS">Seconds since launch.</param>
        public void SetFlightS(float timeS)
        {
            flightS = timeS;
        }

        /// <summary>Marks the ball as scored: it returns to the field from a hub at a given time.</summary>
        /// <param name="hubIndex">Index of the hub it scored in.</param>
        /// <param name="returnTimeS">Time.time at which it returns.</param>
        public void SetScored(int hubIndex, float returnTimeS)
        {
            scoredHubIndex = hubIndex;
            returnAtS = returnTimeS;
        }

        /// <summary>Hub the ball scored in while waiting to return; -1 when it is not waiting.</summary>
        public int GetScoredHubIndex()
        {
            return scoredHubIndex;
        }

        /// <summary>Time.time at which a scored ball returns.</summary>
        public float GetReturnTimeS()
        {
            return returnAtS;
        }

        /// <summary>Clears the scored mark.</summary>
        public void ClearScored()
        {
            scoredHubIndex = -1;
            returnAtS = -1;
        }
    }
}
