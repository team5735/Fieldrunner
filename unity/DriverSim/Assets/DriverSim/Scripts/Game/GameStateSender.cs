using DriverSim.Net;
using UnityEngine;

namespace DriverSim.Game
{
    /// <summary>
    /// Publishes the game's fuel and score state to the bridge, laid out per <see cref="GameStateLayout"/>, a few times
    /// a second. The bridge reads it for the self-test and the probe tool.
    /// </summary>
    public sealed class GameStateSender : MonoBehaviour
    {
        private const float SendPeriodS = 0.05f; // seconds between sends

        [Tooltip("Link to the bridge.")]
        [SerializeField] private DriverSimLink link;

        [Tooltip("Hopper count.")]
        [SerializeField] private RobotFuel robotFuel;

        [Tooltip("Scores.")]
        [SerializeField] private Scoreboard scoreboard;

        [Tooltip("Fuel on the field.")]
        [SerializeField] private FuelField field;

        private float nextSendS; // Time.unscaledTime of the next send

        /// <summary>Sets the references the component needs; call before the scene starts.</summary>
        /// <param name="driverSimLink">Link to the bridge.</param>
        /// <param name="fuel">Hopper count.</param>
        /// <param name="scores">Scores.</param>
        /// <param name="fuelField">Fuel on the field.</param>
        public void SetReferences(DriverSimLink driverSimLink, RobotFuel fuel, Scoreboard scores, FuelField fuelField)
        {
            link = driverSimLink;
            robotFuel = fuel;
            scoreboard = scores;
            field = fuelField;
        }

        // ---- Unity messages

        private void Update()
        {
            if (Time.unscaledTime < nextSendS)
            {
                return;
            }
            nextSendS = Time.unscaledTime + SendPeriodS;
            double[] values = new double[GameStateLayout.Length];
            values[GameStateLayout.ProtocolVersion] = Protocol.Version;
            values[GameStateLayout.HopperCount] = robotFuel.GetHopperCount();
            values[GameStateLayout.OurScore] = scoreboard.GetOurScore();
            values[GameStateLayout.AutoScore] = scoreboard.GetAutoScore();
            values[GameStateLayout.OpponentAutoScore] = scoreboard.GetOpponentAutoScore();
            values[GameStateLayout.FieldFuelCount] = field.GetFieldCount();
            link.SendGameState(values);
        }
    }
}
