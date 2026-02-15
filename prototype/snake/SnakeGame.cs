using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using TMPro;

namespace Snake
{
    public class SnakeGame : MonoBehaviour
    {
        [Header("Grid Settings")]
        [SerializeField] private int gridWidth = 20;
        [SerializeField] private int gridHeight = 20;

        [Header("Gameplay")]
        [SerializeField] private float moveInterval = 0.15f;

        [Header("Visuals")]
        [SerializeField] private Color headColor = new Color(0.2f, 0.8f, 0.2f);    // bright green
        [SerializeField] private Color bodyColor = new Color(0.1f, 0.6f, 0.1f);    // darker green
        [SerializeField] private Color foodColor = new Color(0.9f, 0.2f, 0.2f);    // red
        [SerializeField] private Color wallColor = new Color(0.3f, 0.3f, 0.3f);    // grey
        [SerializeField] private Color bgColor = new Color(0.05f, 0.05f, 0.1f);    // dark blue-black

        [Header("UI")]
        [SerializeField] private TextMeshProUGUI scoreText;
        [SerializeField] private TextMeshProUGUI gameOverText;

        // Snake state
        private List<Vector2Int> snakePositions = new List<Vector2Int>();
        private List<GameObject> snakeObjects = new List<GameObject>();
        private Vector2Int direction = Vector2Int.right;
        private Vector2Int nextDirection = Vector2Int.right;
        private bool directionChangedThisTick = false;

        // Food
        private Vector2Int foodPosition;
        private GameObject foodObject;

        // Game state
        private float moveTimer;
        private float currentMoveInterval;
        private int score;
        private bool gameOver;
        private bool gameStarted;

        // Cached sprite
        private Sprite whiteSprite;

        // Public API for AI Play Protocol
        public bool IsGameOver => gameOver;
        public int Score => score;
        public List<Vector2Int> SnakePositions => snakePositions;
        public Vector2Int FoodPosition => foodPosition;
        public Vector2Int Direction => direction;
        public int GridWidth => gridWidth;
        public int GridHeight => gridHeight;

        private void Start()
        {
            CreateWhiteSprite();
            SetupCamera();
            CreateWalls();
            CreateBackground();
            InitializeGame();
        }

        private void CreateWhiteSprite()
        {
            // Create a 1x1 white texture for all sprites
            var tex = new Texture2D(4, 4);
            var pixels = new Color[16];
            for (int i = 0; i < 16; i++) pixels[i] = Color.white;
            tex.SetPixels(pixels);
            tex.Apply();
            tex.filterMode = FilterMode.Point;
            whiteSprite = Sprite.Create(tex, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 4f);
        }

        private void SetupCamera()
        {
            var cam = Camera.main;
            if (cam != null)
            {
                // Center camera on the grid
                cam.transform.position = new Vector3(gridWidth / 2f - 0.5f, gridHeight / 2f - 0.5f, -10f);
                cam.orthographic = true;
                cam.orthographicSize = gridHeight / 2f + 1.5f;
                cam.backgroundColor = bgColor;
            }
        }

        private void CreateBackground()
        {
            // Create a checkerboard-like background for the play area
            var bgParent = new GameObject("Background");
            bgParent.transform.SetParent(transform);

            for (int x = 0; x < gridWidth; x++)
            {
                for (int y = 0; y < gridHeight; y++)
                {
                    var cell = new GameObject($"BG_{x}_{y}");
                    cell.transform.SetParent(bgParent.transform);
                    cell.transform.position = new Vector3(x, y, 1f); // behind everything
                    var sr = cell.AddComponent<SpriteRenderer>();
                    sr.sprite = whiteSprite;
                    // Subtle checkerboard
                    bool dark = (x + y) % 2 == 0;
                    sr.color = dark ? new Color(0.08f, 0.08f, 0.14f) : new Color(0.1f, 0.1f, 0.16f);
                    sr.sortingOrder = -10;
                }
            }
        }

        private void CreateWalls()
        {
            var wallParent = new GameObject("Walls");
            wallParent.transform.SetParent(transform);

            // Bottom and top walls
            for (int x = -1; x <= gridWidth; x++)
            {
                CreateWallBlock(wallParent.transform, x, -1);
                CreateWallBlock(wallParent.transform, x, gridHeight);
            }
            // Left and right walls (excluding corners already placed)
            for (int y = 0; y < gridHeight; y++)
            {
                CreateWallBlock(wallParent.transform, -1, y);
                CreateWallBlock(wallParent.transform, gridWidth, y);
            }
        }

        private void CreateWallBlock(Transform parent, int x, int y)
        {
            var wall = new GameObject($"Wall_{x}_{y}");
            wall.transform.SetParent(parent);
            wall.transform.position = new Vector3(x, y, 0f);
            var sr = wall.AddComponent<SpriteRenderer>();
            sr.sprite = whiteSprite;
            sr.color = wallColor;
            sr.sortingOrder = -5;
        }

        /// <summary>
        /// Public API for AI control — restart game
        /// </summary>
        public void RestartGame()
        {
            InitializeGame();
        }

        private void InitializeGame()
        {
            // Clear previous snake
            foreach (var obj in snakeObjects)
            {
                if (obj != null) Destroy(obj);
            }
            snakeObjects.Clear();
            snakePositions.Clear();

            if (foodObject != null)
            {
                Destroy(foodObject);
                foodObject = null;
            }

            // Initial snake: 3 segments in the middle
            int startX = gridWidth / 2;
            int startY = gridHeight / 2;

            snakePositions.Add(new Vector2Int(startX, startY));       // head
            snakePositions.Add(new Vector2Int(startX - 1, startY));   // body
            snakePositions.Add(new Vector2Int(startX - 2, startY));   // tail

            for (int i = 0; i < snakePositions.Count; i++)
            {
                snakeObjects.Add(CreateSnakeSegment(snakePositions[i], i == 0));
            }

            direction = Vector2Int.right;
            nextDirection = Vector2Int.right;
            directionChangedThisTick = false;
            moveTimer = 0f;
            currentMoveInterval = moveInterval;
            score = 0;
            gameOver = false;
            gameStarted = true;

            SpawnFood();
            UpdateUI();

            if (gameOverText != null)
            {
                // Hide the parent container (GameOverPanel)
                var container = gameOverText.transform.parent;
                if (container != null && container != transform)
                    container.gameObject.SetActive(false);
                gameOverText.gameObject.SetActive(true); // Keep text active, parent controls visibility
            }
        }

        private GameObject CreateSnakeSegment(Vector2Int pos, bool isHead)
        {
            var seg = new GameObject(isHead ? "SnakeHead" : "SnakeBody");
            seg.transform.SetParent(transform);
            seg.transform.position = new Vector3(pos.x, pos.y, 0f);
            var sr = seg.AddComponent<SpriteRenderer>();
            sr.sprite = whiteSprite;
            sr.color = isHead ? headColor : bodyColor;
            // Make segments slightly smaller than grid cell for visual gap
            seg.transform.localScale = new Vector3(0.9f, 0.9f, 1f);
            sr.sortingOrder = 5;
            return seg;
        }

        private void SpawnFood()
        {
            // Find a free cell
            var freeCells = new List<Vector2Int>();
            for (int x = 0; x < gridWidth; x++)
            {
                for (int y = 0; y < gridHeight; y++)
                {
                    var pos = new Vector2Int(x, y);
                    if (!snakePositions.Contains(pos))
                        freeCells.Add(pos);
                }
            }

            if (freeCells.Count == 0)
            {
                // Player wins! (unlikely but handle it)
                Debug.Log("You win! Snake fills the entire grid!");
                return;
            }

            foodPosition = freeCells[Random.Range(0, freeCells.Count)];

            if (foodObject == null)
            {
                foodObject = new GameObject("Food");
                foodObject.transform.SetParent(transform);
                var sr = foodObject.AddComponent<SpriteRenderer>();
                sr.sprite = whiteSprite;
                sr.color = foodColor;
                sr.sortingOrder = 5;
                foodObject.transform.localScale = new Vector3(0.8f, 0.8f, 1f);
            }

            foodObject.transform.position = new Vector3(foodPosition.x, foodPosition.y, 0f);
        }

        private void Update()
        {
            if (!gameStarted) return;

            if (gameOver)
            {
                // Press Space or Enter to restart
                var kb = Keyboard.current;
                if (kb != null && (kb.spaceKey.wasPressedThisFrame || kb.enterKey.wasPressedThisFrame))
                {
                    InitializeGame();
                }
                return;
            }

            HandleInput();

            moveTimer += Time.deltaTime;
            if (moveTimer >= currentMoveInterval)
            {
                moveTimer -= currentMoveInterval;
                MoveSnake();
                directionChangedThisTick = false;
            }
        }

        /// <summary>
        /// Public API for AI control — set direction programmatically
        /// </summary>
        public bool TrySetDirection(Vector2Int newDir)
        {
            if (gameOver) return false;
            if (directionChangedThisTick) return false;

            // Prevent reversing direction (would cause instant self-collision)
            if (newDir + direction == Vector2Int.zero) return false;
            if (newDir == nextDirection) return false;

            nextDirection = newDir;
            directionChangedThisTick = true;
            return true;
        }

        private void HandleInput()
        {
            var kb = Keyboard.current;
            if (kb == null) return;

            // Only allow one direction change per tick to prevent 180-degree turns
            if (directionChangedThisTick) return;

            Vector2Int newDir = nextDirection;

            if (kb.wKey.wasPressedThisFrame || kb.upArrowKey.wasPressedThisFrame)
                newDir = Vector2Int.up;
            else if (kb.sKey.wasPressedThisFrame || kb.downArrowKey.wasPressedThisFrame)
                newDir = Vector2Int.down;
            else if (kb.aKey.wasPressedThisFrame || kb.leftArrowKey.wasPressedThisFrame)
                newDir = Vector2Int.left;
            else if (kb.dKey.wasPressedThisFrame || kb.rightArrowKey.wasPressedThisFrame)
                newDir = Vector2Int.right;

            // Prevent reversing direction (would cause instant self-collision)
            if (newDir + direction != Vector2Int.zero && newDir != nextDirection)
            {
                nextDirection = newDir;
                directionChangedThisTick = true;
            }
        }

        private void MoveSnake()
        {
            direction = nextDirection;
            Vector2Int newHead = snakePositions[0] + direction;

            // Check wall collision
            if (newHead.x < 0 || newHead.x >= gridWidth || newHead.y < 0 || newHead.y >= gridHeight)
            {
                GameOver();
                return;
            }

            // Check self-collision (exclude the tail which will move away, unless we're growing)
            for (int i = 0; i < snakePositions.Count - 1; i++)
            {
                if (snakePositions[i] == newHead)
                {
                    GameOver();
                    return;
                }
            }

            bool ateFood = (newHead == foodPosition);

            // Insert new head
            snakePositions.Insert(0, newHead);
            var headObj = CreateSnakeSegment(newHead, true);
            snakeObjects.Insert(0, headObj);

            // Change old head to body color
            if (snakeObjects.Count > 1)
            {
                var oldHead = snakeObjects[1].GetComponent<SpriteRenderer>();
                if (oldHead != null) oldHead.color = bodyColor;
                snakeObjects[1].name = "SnakeBody";
            }

            if (ateFood)
            {
                score += 10;
                UpdateUI();
                SpawnFood();

                // Speed up slightly as score increases
                currentMoveInterval = Mathf.Max(0.05f, moveInterval - score * 0.001f);
            }
            else
            {
                // Remove tail
                int lastIndex = snakePositions.Count - 1;
                Destroy(snakeObjects[lastIndex]);
                snakeObjects.RemoveAt(lastIndex);
                snakePositions.RemoveAt(lastIndex);
            }
        }

        private void GameOver()
        {
            gameOver = true;
            Debug.Log($"Game Over! Score: {score}");

            if (gameOverText != null)
            {
                // Show the parent container (GameOverPanel) which includes the background
                var container = gameOverText.transform.parent;
                if (container != null && container != transform)
                    container.gameObject.SetActive(true);
                gameOverText.gameObject.SetActive(true);
                gameOverText.text = $"GAME OVER\nScore: {score}\n\nPress SPACE to restart";
            }
        }

        private void UpdateUI()
        {
            if (scoreText != null)
                scoreText.text = $"Score: {score}";
        }
    }
}
