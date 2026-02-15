using UnityEngine;
using Editor;

namespace Snake
{
    /// <summary>
    /// IDescribable for Snake game — exposes game state and directional controls to AI agents.
    /// Provides: text-based grid visualization, score/state tracking, direction actions.
    /// </summary>
    public class SnakeDescribable : MonoBehaviour, IDescribable
    {
        [SerializeField] private SnakeGame game;

        private void Reset()
        {
            // Auto-wire in Editor
            game = GetComponent<SnakeGame>();
        }

        public ScreenFragment Describe()
        {
            if (game == null)
            {
                return new ScreenFragment
                {
                    Name = "Snake Game",
                    Description = "ERROR: SnakeGame component not assigned."
                };
            }

            // Use public API instead of reflection
            bool gameOver = game.IsGameOver;
            int score = game.Score;
            var snakePositions = game.SnakePositions;
            Vector2Int foodPosition = game.FoodPosition;
            Vector2Int direction = game.Direction;
            int gridWidth = game.GridWidth;
            int gridHeight = game.GridHeight;

            // Build description
            var desc = new System.Text.StringBuilder();

            if (gameOver)
            {
                desc.AppendLine($"GAME OVER — Final score: {score}");
                desc.AppendLine($"Snake length: {snakePositions.Count}");
            }
            else
            {
                desc.AppendLine($"Score: {score} | Snake length: {snakePositions.Count}");
                desc.AppendLine($"Heading: {DirectionName(direction)}");
                desc.AppendLine();
                desc.AppendLine(RenderGrid(snakePositions, foodPosition, gridWidth, gridHeight));
            }

            // Actions — directional controls
            GameAction[] actions = null;

            if (!gameOver)
            {
                var actionList = new System.Collections.Generic.List<GameAction>();

                // Up
                if (direction != Vector2Int.down)
                    actionList.Add(GameAction.Create("Up", () => SetDirection(Vector2Int.up), "Turn upward"));
                else
                    actionList.Add(GameAction.Disabled("Up", "Can't reverse direction"));

                // Down
                if (direction != Vector2Int.up)
                    actionList.Add(GameAction.Create("Down", () => SetDirection(Vector2Int.down), "Turn downward"));
                else
                    actionList.Add(GameAction.Disabled("Down", "Can't reverse direction"));

                // Left
                if (direction != Vector2Int.right)
                    actionList.Add(GameAction.Create("Left", () => SetDirection(Vector2Int.left), "Turn left"));
                else
                    actionList.Add(GameAction.Disabled("Left", "Can't reverse direction"));

                // Right
                if (direction != Vector2Int.left)
                    actionList.Add(GameAction.Create("Right", () => SetDirection(Vector2Int.right), "Turn right"));
                else
                    actionList.Add(GameAction.Disabled("Right", "Can't reverse direction"));

                actions = actionList.ToArray();
            }
            else
            {
                // Game Over — offer restart
                actions = new[]
                {
                    GameAction.Create("Restart", () => { game.RestartGame(); return "New game started"; }, "Try again")
                };
            }

            return new ScreenFragment
            {
                Name = "Snake",
                Description = desc.ToString().TrimEnd(),
                Actions = actions
            };
        }

        private string DirectionName(Vector2Int dir)
        {
            if (dir == Vector2Int.up) return "↑ North";
            if (dir == Vector2Int.down) return "↓ South";
            if (dir == Vector2Int.left) return "← West";
            if (dir == Vector2Int.right) return "→ East";
            return "?";
        }

        private string RenderGrid(System.Collections.Generic.List<Vector2Int> snake, Vector2Int food, int width, int height)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("```");

            // Top border
            sb.Append("╔");
            for (int x = 0; x < width; x++) sb.Append("═");
            sb.AppendLine("╗");

            // Grid from top to bottom (Unity Y+ is up, but text renders top-down)
            for (int y = height - 1; y >= 0; y--)
            {
                sb.Append("║");
                for (int x = 0; x < width; x++)
                {
                    var pos = new Vector2Int(x, y);

                    if (snake.Count > 0 && snake[0] == pos)
                        sb.Append("@"); // Head
                    else if (snake.Contains(pos))
                        sb.Append("o"); // Body
                    else if (food == pos)
                        sb.Append("*"); // Food
                    else
                        sb.Append(" "); // Empty
                }
                sb.AppendLine("║");
            }

            // Bottom border
            sb.Append("╚");
            for (int x = 0; x < width; x++) sb.Append("═");
            sb.AppendLine("╝");

            sb.AppendLine("```");
            sb.AppendLine("@ = head | o = body | * = food");

            return sb.ToString();
        }

        private string SetDirection(Vector2Int newDir)
        {
            if (game.TrySetDirection(newDir))
                return $"Turning {DirectionName(newDir).ToLower()}";
            return "Direction unchanged";
        }
    }
}
