using DriverSim.Net;
using UnityEngine;

namespace DriverSim.Game
{
    /// <summary>
    /// Sends the virtual controller to the bridge every frame (at most 100 times a second), but only when the bridge
    /// says the game supplies the input. While a menu is open or the window is unfocused it sends the neutral reading,
    /// so the robot never keeps driving on a stale button press.
    /// </summary>
    public sealed class JoystickSender : MonoBehaviour
    {
        private const float MinSendPeriodS = 0.01f; // seconds between sends: 100 Hz at most

        [Tooltip("Link to the bridge.")]
        [SerializeField] private DriverSimLink link;

        [Tooltip("The virtual controller.")]
        [SerializeField] private ControllerInput input;

        [Tooltip("Tells whether the game, rather than a menu, has the player's attention.")]
        [SerializeField] private GameFlow flow;

        private float lastSendS = -1f; // Time.unscaledTime of the last send

        /// <summary>Sets the objects the sender uses; call before the scene starts.</summary>
        /// <param name="driverSimLink">Link to the bridge.</param>
        /// <param name="controllerInput">The virtual controller.</param>
        /// <param name="gameFlow">Game state, to know whether a menu is open.</param>
        public void SetReferences(DriverSimLink driverSimLink, ControllerInput controllerInput, GameFlow gameFlow)
        {
            link = driverSimLink;
            input = controllerInput;
            flow = gameFlow;
        }

        // ---- Unity messages

        private void Update()
        {
            if (!link.IsGameInput())
            {
                return;
            }
            float sinceLastSendS = Time.unscaledTime - lastSendS;
            if (sinceLastSendS < MinSendPeriodS)
            {
                return;
            }
            lastSendS = Time.unscaledTime;

            // the player's controller while playing with the window focused, neutral otherwise
            JoystickState state = JoystickState.Neutral;
            if (flow.IsPlaying() && Application.isFocused)
            {
                state = input.GetRobotState();
            }
            link.SendJoystick(state);
        }
    }
}
