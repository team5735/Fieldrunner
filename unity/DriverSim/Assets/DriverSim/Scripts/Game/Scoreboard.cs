using DriverSim.Net;
using UnityEngine;

namespace DriverSim.Game
{
    /// <summary>
    /// Scores fuel and keeps the numbers the HUD shows. Free practice counts every ball that goes into the player's own
    /// alliance hub. In a match a ball counts one point while that hub is active (autonomous, and the teleop shift
    /// schedule); a ball into an inactive own hub counts nothing and flashes "HUB INACTIVE". The other alliance's hub
    /// counts nothing in either mode. At the end of autonomous it compares the player's autonomous points with a
    /// virtual opponent's score (drawn per match) and tells the bridge who won, which drives the hub shifts; ties go to
    /// the opponent. Resets are explicit (<see cref="ResetScores"/>), called when a session starts.
    /// </summary>
    public sealed class Scoreboard : MonoBehaviour
    {
        private const int OpponentAutoMinPoints = 4; // lowest virtual opponent autonomous score
        private const int OpponentAutoMaxPoints = 12; // highest virtual opponent autonomous score

        [Tooltip("Link to the bridge: the match state and the autoWinner command.")]
        [SerializeField] private DriverSimLink link;

        [Tooltip("The fuel on the field; its scores are counted here.")]
        [SerializeField] private FuelField field;

        private int ourScore; // points scored in the player's own hub
        private int autoScore; // the part of ourScore scored during autonomous
        private int opponentAutoScore; // the virtual opponent's autonomous score
        private bool hasAutoResult; // autonomous has ended and its winner has been decided
        private bool didWinAuto; // the player won autonomous
        private bool hasFinal; // the match has ended and the final score is kept on screen
        private int finalScore; // the score when the match ended
        private float lastPointTimeS = -1000f; // Time.time of the last counted ball
        private float lastInactiveTimeS = -1000f; // Time.time of the last ball into an inactive own hub
        private MatchPhase previousPhase = MatchPhase.FreePlay; // phase at the last frame

        /// <summary>Sets the references the component needs; call before the scene starts.</summary>
        /// <param name="driverSimLink">Link to the bridge.</param>
        /// <param name="fuelField">The fuel on the field.</param>
        public void SetReferences(DriverSimLink driverSimLink, FuelField fuelField)
        {
            link = driverSimLink;
            field = fuelField;
        }

        /// <summary>Points scored in the player's own hub.</summary>
        public int GetOurScore()
        {
            return ourScore;
        }

        /// <summary>The part of the score scored during autonomous.</summary>
        public int GetAutoScore()
        {
            return autoScore;
        }

        /// <summary>The virtual opponent's autonomous score.</summary>
        public int GetOpponentAutoScore()
        {
            return opponentAutoScore;
        }

        /// <summary>Whether autonomous has ended and been decided.</summary>
        public bool HasAutoResult()
        {
            return hasAutoResult;
        }

        /// <summary>Whether the player won autonomous; meaningful once <see cref="HasAutoResult"/> is true.</summary>
        public bool DidWinAuto()
        {
            return didWinAuto;
        }

        /// <summary>Whether the match has ended and its final score is on show.</summary>
        public bool HasFinal()
        {
            return hasFinal;
        }

        /// <summary>The score the match ended with.</summary>
        public int GetFinalScore()
        {
            return finalScore;
        }

        /// <summary>Seconds since the last ball counted.</summary>
        public float GetSinceLastPointS()
        {
            return Time.time - lastPointTimeS;
        }

        /// <summary>Seconds since a ball went into an inactive own hub.</summary>
        public float GetSinceInactiveS()
        {
            return Time.time - lastInactiveTimeS;
        }

        /// <summary>Clears the scores and the final, and draws a new virtual opponent autonomous score.</summary>
        public void ResetScores()
        {
            ourScore = 0;
            autoScore = 0;
            hasAutoResult = false;
            didWinAuto = false;
            hasFinal = false;
            finalScore = 0;
            lastPointTimeS = -1000f;
            lastInactiveTimeS = -1000f;
            opponentAutoScore = Random.Range(OpponentAutoMinPoints, OpponentAutoMaxPoints + 1);
        }

        /// <summary>
        /// Counts or refuses one ball that scored; also what <see cref="FuelField"/> calls through its event.
        /// </summary>
        /// <param name="isRedHub">Whether the ball went into the red alliance's hub.</param>
        public void HandleScored(bool isRedHub)
        {
            MatchState match = link.GetMatchState();
            bool isOurHub = isRedHub == match.IsRed();
            if (!isOurHub)
            {
                return;
            }

            // free practice: every ball counts
            if (match.Phase == MatchPhase.FreePlay)
            {
                ourScore++;
                lastPointTimeS = Time.time;
                return;
            }

            // match: only while the hub is active
            bool isActive = match.IsBlueHubActive;
            if (isRedHub)
            {
                isActive = match.IsRedHubActive;
            }
            if (!isActive)
            {
                lastInactiveTimeS = Time.time;
                return;
            }
            ourScore++;
            if (match.Phase == MatchPhase.Auto)
            {
                autoScore++;
            }
            lastPointTimeS = Time.time;
        }

        // ---- Unity messages

        private void Awake()
        {
            opponentAutoScore = Random.Range(OpponentAutoMinPoints, OpponentAutoMaxPoints + 1);
        }

        private void OnEnable()
        {
            field.FuelScored += HandleScored;
        }

        private void OnDisable()
        {
            field.FuelScored -= HandleScored;
        }

        private void Update()
        {
            MatchState match = link.GetMatchState();
            MatchPhase phase = match.Phase;

            // autonomous ended into the gap or teleop: decide the winner and tell the bridge
            bool isAutoOver = previousPhase == MatchPhase.Auto
                && (phase == MatchPhase.AutoToTeleop || phase == MatchPhase.Teleop);
            if (isAutoOver)
            {
                DecideAutoWinner(match.IsRed());
            }

            // the match ended: keep the final score up
            if (phase == MatchPhase.PostMatch && previousPhase != MatchPhase.PostMatch)
            {
                hasFinal = true;
                finalScore = ourScore;
            }
            previousPhase = phase;
        }

        // ---- private stateful helpers

        /// <summary>Compares the autonomous scores (ties go to the opponent) and sends the winner's alliance.</summary>
        /// <param name="isPlayerRed">Whether the player is on the red alliance.</param>
        private void DecideAutoWinner(bool isPlayerRed)
        {
            didWinAuto = autoScore > opponentAutoScore;
            hasAutoResult = true;
            bool isRedWinner = isPlayerRed == didWinAuto;
            string winner = "B";
            if (isRedWinner)
            {
                winner = "R";
            }
            link.SendCommand("autoWinner " + winner);
        }
    }
}
