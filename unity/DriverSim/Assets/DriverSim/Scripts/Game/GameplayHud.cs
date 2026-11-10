using DriverSim.Net;
using UnityEngine;

namespace DriverSim.Game
{
    /// <summary>
    /// The gameplay readout under the match clock: hopper fuel and the score (<c>FUEL 12/20</c> with <c>OUR HUB 37</c>
    /// in free practice or <c>SCORE 37</c> in a match), the autonomous comparison (<c>AUTO 9 vs 7 (WON)</c>), a
    /// <c>+1</c> that pops beside the clock on each point, a <c>HUB INACTIVE</c> flash, and <c>FINAL 52</c> after a
    /// match. Drawn with the same 900-point virtual screen as <see cref="DebugHud"/> and hidden with it.
    /// </summary>
    public sealed class GameplayHud : MonoBehaviour
    {
        private const float VirtualHeight = 900f; // virtual screen height the layout is drawn in, points
        private const float PopS = 0.9f; // seconds a +1 stays up
        private const float InactiveFlashS = 1.3f; // seconds HUB INACTIVE stays up

        [Tooltip("Link to the bridge, for the match state.")]
        [SerializeField] private DriverSimLink link;

        [Tooltip("Hopper count.")]
        [SerializeField] private RobotFuel robotFuel;

        [Tooltip("Scores.")]
        [SerializeField] private Scoreboard scoreboard;

        [Tooltip("The overlay whose visibility this follows.")]
        [SerializeField] private DebugHud hud;

        private GUIStyle rowStyle; // fuel and score row
        private GUIStyle autoStyle; // autonomous row
        private GUIStyle popStyle; // +1
        private GUIStyle flashStyle; // HUB INACTIVE

        /// <summary>Sets the references the component needs; call before the scene starts.</summary>
        /// <param name="driverSimLink">Link to the bridge.</param>
        /// <param name="fuel">Hopper count.</param>
        /// <param name="scores">Scores.</param>
        /// <param name="debugHud">The overlay whose visibility this follows.</param>
        public void SetReferences(DriverSimLink driverSimLink, RobotFuel fuel, Scoreboard scores, DebugHud debugHud)
        {
            link = driverSimLink;
            robotFuel = fuel;
            scoreboard = scores;
            hud = debugHud;
        }

        // ---- Unity messages

        private void OnGUI()
        {
            if (link == null || !hud.IsVisible() || !link.IsConnected())
            {
                return;
            }
            if (rowStyle == null)
            {
                MakeStyles();
            }
            float scale = Screen.height / VirtualHeight;
            Vector3 scaleVector = new Vector3(scale, scale, 1);
            GUI.matrix = Matrix4x4.Scale(scaleVector);
            float width = Screen.width / scale;
            MatchState match = link.GetMatchState();

            DrawRow(width, match);
            DrawAutoRow(width, match);
            DrawPop(width);
            DrawInactiveFlash(width);
        }

        // ---- private stateful helpers

        /// <summary>Draws the hopper count and the score (or the final score) under the clock.</summary>
        /// <param name="width">Virtual screen width, points.</param>
        /// <param name="match">Match state.</param>
        private void DrawRow(float width, MatchState match)
        {
            Rect box = new Rect(width / 2 - 230, 64, 460, 36);
            GUI.Box(box, GUIContent.none);
            Rect left = new Rect(box.x, box.y, box.width / 2, box.height);
            Rect right = new Rect(box.x + box.width / 2, box.y, box.width / 2, box.height);

            // hopper, tinted when full
            int hopper = robotFuel.GetHopperCount();
            Color hopperColor = Color.white;
            if (hopper >= RobotFuel.Capacity)
            {
                hopperColor = new Color(1f, 0.6f, 0.15f);
            }
            rowStyle.normal.textColor = hopperColor;
            GUI.Label(left, "FUEL " + hopper + "/" + RobotFuel.Capacity, rowStyle);

            // score: free practice, match, or the final
            string scoreText = "OUR HUB " + scoreboard.GetOurScore();
            if (match.Phase != MatchPhase.FreePlay)
            {
                scoreText = "SCORE " + scoreboard.GetOurScore();
            }
            Color scoreColor = Color.white;
            if (scoreboard.HasFinal())
            {
                scoreText = "FINAL " + scoreboard.GetFinalScore();
                scoreColor = new Color(1f, 0.85f, 0.1f);
            }
            rowStyle.normal.textColor = scoreColor;
            GUI.Label(right, scoreText, rowStyle);
        }

        /// <summary>Draws the autonomous comparison during and after autonomous in a match.</summary>
        /// <param name="width">Virtual screen width, points.</param>
        /// <param name="match">Match state.</param>
        private void DrawAutoRow(float width, MatchState match)
        {
            bool isMatch = match.Phase != MatchPhase.FreePlay;
            bool isAutoShown = match.Phase == MatchPhase.Auto || scoreboard.HasAutoResult();
            if (!isMatch || !isAutoShown)
            {
                return;
            }
            string text = "AUTO " + scoreboard.GetAutoScore() + " vs " + scoreboard.GetOpponentAutoScore();
            if (scoreboard.HasAutoResult())
            {
                string result = "LOST";
                if (scoreboard.DidWinAuto())
                {
                    result = "WON";
                }
                text = text + " (" + result + ")";
            }
            Rect box = new Rect(width / 2 - 150, 102, 300, 28);
            GUI.Box(box, GUIContent.none);
            GUI.Label(box, text, autoStyle);
        }

        /// <summary>Draws a +1 that rises and fades beside the clock after each point.</summary>
        /// <param name="width">Virtual screen width, points.</param>
        private void DrawPop(float width)
        {
            float ageS = scoreboard.GetSinceLastPointS();
            if (ageS < 0 || ageS > PopS)
            {
                return;
            }
            float fraction = ageS / PopS;
            popStyle.normal.textColor = new Color(0.4f, 1f, 0.5f, 1f - fraction);
            Rect box = new Rect(width / 2 + 240, 12 - 14 * fraction, 80, 44);
            GUI.Label(box, "+1", popStyle);
        }

        /// <summary>Flashes HUB INACTIVE after a ball into the player's inactive hub.</summary>
        /// <param name="width">Virtual screen width, points.</param>
        private void DrawInactiveFlash(float width)
        {
            float ageS = scoreboard.GetSinceInactiveS();
            if (ageS < 0 || ageS > InactiveFlashS)
            {
                return;
            }
            float blink = 0.5f + 0.5f * Mathf.Cos(ageS * 18f);
            float alpha = Mathf.Lerp(0.55f, 1f, blink);
            flashStyle.normal.textColor = new Color(1f, 0.25f, 0.2f, alpha);
            Rect box = new Rect(width / 2 - 250, 176, 500, 50);
            GUI.Label(box, "HUB INACTIVE", flashStyle);
        }

        /// <summary>Makes the text styles from the current GUI skin.</summary>
        private void MakeStyles()
        {
            rowStyle = new GUIStyle(GUI.skin.label);
            rowStyle.fontSize = 22;
            rowStyle.fontStyle = FontStyle.Bold;
            rowStyle.alignment = TextAnchor.MiddleCenter;
            autoStyle = new GUIStyle(rowStyle);
            autoStyle.fontSize = 18;
            autoStyle.normal.textColor = Color.white;
            popStyle = new GUIStyle(rowStyle);
            popStyle.fontSize = 34;
            popStyle.alignment = TextAnchor.MiddleLeft;
            flashStyle = new GUIStyle(rowStyle);
            flashStyle.fontSize = 36;
        }
    }
}
