using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using TMPro;
using System.IO;
using Unity.Networking.Transport;
using System;
using Unity.VisualScripting;
using UnityEngine.UI;

public enum SpecialMove
{
    None = 0,
    EnPassant,
    Castling,
    Promotion
}
public enum CameraAngle
{
    whiteTeam = 0,
}

public class Chessboard : MonoBehaviour
{
    public static Chessboard Instance { set; get; }

    [Header("Art Stuff")]
    [SerializeField] private Material tileMaterial;
    [SerializeField] private Material hoverMaterial;
    [SerializeField] private Material highlightMaterial;
    [SerializeField] private Material checkMaterial;
    [SerializeField] private float tileSize = 6.0f;
    [SerializeField] private float yOffset = 0.2f;
    [SerializeField] private Vector3 boardCenter = Vector3.zero;
    [SerializeField] private float dragOffset = 1.0f;
    [SerializeField] private Vector3 blackPiecesRotation;
    [SerializeField] private Vector3 whitePiecesRotation;
    [SerializeField] private GameObject victoryScreen;
    [SerializeField] public Transform rematchIndicator;
    [SerializeField] public Button rematchButton;
    [SerializeField] public Button menuButton;

    [Header("Death Config")]
    [SerializeField] private Transform whiteDeathPivot;
    [SerializeField] private Transform blackDeathPivot;
    [SerializeField] private float yCaptureOffset = 2.0f;
    [SerializeField] private int piecePerRow = 4;
    [SerializeField] private float deathSize;
    [SerializeField] private float deathSpacing;

    [Header("Configuración del Agente IA MCTS")]
    public bool isAIGame = false;
    public int aiLevel = 1;
    public int aiTeam = 1; // 0 = Blanco, 1 = Negro
    private bool isAIThinking = false;

    [Header("Prefabs & Materials")]
    [SerializeField] private GameObject[] prefabs;
    [SerializeField] private Material[] teamMaterials;

    [Header("User Preferences")]
    [SerializeField] private bool hoverTilesEnabled;
    [SerializeField] private bool showPossibleMovesEnabled = true;
    [SerializeField] private bool invertBoardForBlackEnabled;
    // Not working
    private bool smoothPieceMovement = false;
    private float smoothSpeed = 10.0f;
    [SerializeField] private float cameraRotationSpeed = 2.0f;
    [SerializeField] private bool useTimer = false;
    [SerializeField] private float playerTimeMinutes = 10.0f;

    [Header("Debug")]
    [SerializeField] private bool debugRemovePawns = false;
    [SerializeField] private bool testCastling = false;
    [SerializeField] private bool turnsEnabled = true;
    [SerializeField] private bool forceLegalMovesOnly = true;

    [Header("Logic")]
    // Serialized Fields
    [SerializeField] private GameObject cameraPivot;
    [SerializeField] private TextMeshProUGUI timerText;
    [SerializeField] private Canvas gameSceneCanvas;
    [SerializeField] private MeshRenderer boardMesh;

    public ChessPiece[,] chessPieces;
    private ChessPiece currentlyDragging;
    public List<Vector2Int> availableMoves = new List<Vector2Int>();
    private List<ChessPiece> deadWhites = new List<ChessPiece>();
    private List<ChessPiece> deadBlacks = new List<ChessPiece>();
    private const int TILE_COUNT_X = 8;
    private const int TILE_COUNT_Y = 8;
    private GameObject[,] tiles;
    private Camera currentCamera;
    private Vector2Int currentHover;
    private Vector3 bounds;
    private bool isWhiteTurn;
    public SpecialMove specialMove;
    public List<Vector2Int[]> moveList = new List<Vector2Int[]>();
    private List<ChessPiece> movingPieces = new List<ChessPiece>();
    private bool isCameraRotating = false;
    private float whiteTime;
    private float blackTime;

    // Multi logic
    [SerializeField] private GameObject[] cameraAngles;
    private bool isLoadingLevel = false;

    // Cached check state — updated only after real moves on the main thread to avoid
    // a data race with the AI background thread (MakeMove/UnmakeMove mutate currentX/Y
    // on shared ChessPiece objects; reading them every frame in UpdateTileMaterials
    // caused both king tiles to flicker red intermittently).
    private bool _whiteKingInCheck;
    private bool _blackKingInCheck;

    // Cameras
    public void ChangeCamera(CameraAngle index)
    {
        int i = (int)index;

        // Check if the index is valid for the current array size
        if (i >= 0 && i < cameraAngles.Length)
        {
            cameraAngles[i].SetActive(true);
        }
        else
        {
            Debug.LogError($"Chessboard: Camera index {i} is out of bounds! Array size is {cameraAngles.Length}");
        }
    }

    // Monobehaviour
    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(Instance.gameObject); // Limpia instancias viejas si las hubiera
        }
        Instance = this;

        Debug.Log("Chessboard: Instancia asignada correctamente en Awake");

        LoadSettingsFromFile();

        if (boardMesh != null)
        {
            tileSize = boardMesh.bounds.size.x / TILE_COUNT_X;
            boardCenter = boardMesh.bounds.center;
        }

        if (turnsEnabled)
            isWhiteTurn = true;

        if (useTimer)
            gameSceneCanvas.transform.GetChild(0).gameObject.SetActive(true);

        victoryScreen.transform.GetChild(0).gameObject.SetActive(false);
        victoryScreen.transform.GetChild(1).gameObject.SetActive(false);

        GenerateAllTiles(tileSize, TILE_COUNT_X, TILE_COUNT_Y);
        SpawnAllPieces();
        PositionAllPieces();
    }
    private void Start()
    {
        if (GameUI.dataToLoad != null)
        {
            LoadFromSaveData(GameUI.dataToLoad);
            GameUI.dataToLoad = null;
        }
        else
        {
            ResetTimers(true);
        }
    }
    private void Update()
    {
        if (!currentCamera)
        {
            currentCamera = Camera.main;
            return;
        }

        if (isAIGame && !isAIThinking && !victoryScreen.activeSelf)
        {
            // Comprobamos si el turno actual coincide con el equipo de la IA
            if ((isWhiteTurn && aiTeam == 0) || (!isWhiteTurn && aiTeam == 1))
            {
                TriggerAIMove();
                return; // Bloquea el resto del Update (raycasts del jugador) mientras la IA piensa
            }
        }

        // Block all mouse interaction while the camera is animating between sides
        if (isCameraRotating) return;

        RaycastHit info;
        Vector2 mousePosition = Mouse.current.position.ReadValue();
        Ray ray = currentCamera.ScreenPointToRay(mousePosition);

        if (Physics.Raycast(ray, out info, 100, LayerMask.GetMask("Tile", "Hover", "Highlight")))
        {
            Vector2Int hitPosition = LookupTileIndex(info.transform.gameObject);

            if (currentHover == -Vector2Int.one)
            {
                currentHover = hitPosition;
                tiles[hitPosition.x, hitPosition.y].layer = LayerMask.NameToLayer("Hover");
            }

            if (currentHover != hitPosition)
            {
                tiles[currentHover.x, currentHover.y].layer = (ContainsValidMove(ref availableMoves, currentHover))
                ? LayerMask.NameToLayer("Highlight")
                : LayerMask.NameToLayer("Tile");

                if (currentHover != -Vector2Int.one && chessPieces[currentHover.x, currentHover.y] != null)
                    chessPieces[currentHover.x, currentHover.y].SetHover(false);

                currentHover = hitPosition;

                if (hoverTilesEnabled)
                {
                    tiles[hitPosition.x, hitPosition.y].layer = LayerMask.NameToLayer("Hover");

                    if (chessPieces[hitPosition.x, hitPosition.y] != null)
                        chessPieces[hitPosition.x, hitPosition.y].SetHover(true);
                }
            }

            if (Mouse.current.leftButton.wasPressedThisFrame)
            {
                if (chessPieces[hitPosition.x, hitPosition.y] != null)
                {
                    if (!turnsEnabled || (chessPieces[hitPosition.x, hitPosition.y].team == 0 && isWhiteTurn && GameUI.Instance.currentTeam == 0) || (chessPieces[hitPosition.x, hitPosition.y].team == 1 && !isWhiteTurn && GameUI.Instance.currentTeam == 1))
                    {
                        currentlyDragging = chessPieces[hitPosition.x, hitPosition.y];

                        if (forceLegalMovesOnly)
                        {
                            availableMoves = currentlyDragging.GetAvailableMoves(ref chessPieces, TILE_COUNT_X, TILE_COUNT_Y);
                            specialMove = currentlyDragging.GetSpecialMoves(ref chessPieces, ref moveList, ref availableMoves);
                            availableMoves.RemoveAll(m => chessPieces[m.x, m.y] != null && chessPieces[m.x, m.y].type == ChessPieceType.King);
                            PreventCheck();
                        }
                        else
                        {
                            availableMoves.Clear();
                            for (int x = 0; x < TILE_COUNT_X; x++)
                                for (int y = 0; y < TILE_COUNT_Y; y++)
                                    availableMoves.Add(new Vector2Int(x, y));
                        }

                        HighlightTiles();
                    }
                }
            }

            if (currentlyDragging != null && Mouse.current.leftButton.wasReleasedThisFrame)
            {
                Vector2Int previousPosition = new Vector2Int(currentlyDragging.currentX, currentlyDragging.currentY);

                if (ContainsValidMove(ref availableMoves, new Vector2Int(hitPosition.x, hitPosition.y)))
                {
                    MoveTo(previousPosition.x, previousPosition.y, hitPosition.x, hitPosition.y);

                    // Net implementaiton
                    if (!GameUI.Instance.localGame && Client.Instance != null)
                    {
                        NetMakeMove mm = new()
                        {
                            originalX = previousPosition.x,
                            originalY = previousPosition.y,
                            destinationX = hitPosition.x,
                            destinaionY = hitPosition.y,
                            teamId = GameUI.Instance.currentTeam
                        };
                        Client.Instance.SendToServer(mm);
                    }
                    else
                    {
                        Debug.Log("Movimiento local detectado: No se envía al servidor para evitar NullReference.");
                    }
                }
                else
                {
                    currentlyDragging.SetPosition(GetTileCenter(previousPosition.x, previousPosition.y));
                    currentlyDragging = null;
                    RemoveHighlightTiles();
                }
            }
        }
        else
        {
            if (currentHover != -Vector2Int.one)
            {
                tiles[currentHover.x, currentHover.y].layer = (ContainsValidMove(ref availableMoves, currentHover)) ? LayerMask.NameToLayer("Highlight") : LayerMask.NameToLayer("Tile");
                currentHover = -Vector2Int.one;
            }
            if (currentlyDragging != null && Mouse.current.leftButton.wasReleasedThisFrame)
            {
                currentlyDragging.SetPosition(GetTileCenter(currentlyDragging.currentX, currentlyDragging.currentY));
                currentlyDragging = null;
                RemoveHighlightTiles();
            }
        }

        if (currentlyDragging)
        {
            Plane horizontalPlane = new Plane(transform.up, transform.position + transform.up * yOffset);
            float distance = 0.0f;
            if (horizontalPlane.Raycast(ray, out distance))
                currentlyDragging.SetPosition(ray.GetPoint(distance) + Vector3.up * dragOffset);
        }

        foreach (var piece in movingPieces.ToArray())
        {
            Vector3 targetPos = GetTileCenter(piece.currentX, piece.currentY);
            piece.transform.position = Vector3.Lerp(piece.transform.position, targetPos, Time.deltaTime * smoothSpeed);

            if (Vector3.Distance(piece.transform.position, targetPos) < 0.01f)
            {
                piece.SetPosition(targetPos);
                movingPieces.Remove(piece);
            }
        }

        if (useTimer && !victoryScreen.activeSelf)
        {
            if (isWhiteTurn)
                whiteTime -= Time.deltaTime;
            else
                blackTime -= Time.deltaTime;

            if (whiteTime <= 0) { whiteTime = 0; CheckMate(1); }
            if (blackTime <= 0) { blackTime = 0; CheckMate(0); }

            if (timerText != null)
            {
                float actualTime = (isWhiteTurn) ? whiteTime : blackTime;

                timerText.color = GetTimerColor(actualTime);

                timerText.text = string.Format("White: {0} | Black: {1}",
                    FormatTime(whiteTime),
                    FormatTime(blackTime));
            }
        }

        UpdateTileMaterials();
    }

    // Tile Managment
    private void GenerateAllTiles(float tileSize, int tileCountX, int tileCountY)
    {
        bounds = new Vector3((tileCountX / 2f) * tileSize, 0, (tileCountY / 2f) * tileSize) - boardCenter;

        tiles = new GameObject[tileCountX, tileCountY];
        for (int x = 0; x < tileCountX; x++)
            for (int y = 0; y < tileCountY; y++)
                tiles[x, y] = GenerateSingleTile(tileSize, x, y);
    }
    private GameObject GenerateSingleTile(float tileSize, int x, int y)
    {
        GameObject tileObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
        tileObject.name = string.Format("X: {0}, Y: {1}", x, y);
        tileObject.transform.parent = transform;

        // Posicionamiento basado en el centro calculado
        Vector3 position = new Vector3(x * tileSize, yOffset, y * tileSize) - bounds;
        position += new Vector3(tileSize / 2, 0, tileSize / 2);
        tileObject.transform.position = position;

        // Escala del sensor (fino para que no estorbe visualmente)
        tileObject.transform.localScale = new Vector3(tileSize, 0.01f, tileSize);

        tileObject.GetComponent<MeshRenderer>().material = tileMaterial;
        tileObject.layer = LayerMask.NameToLayer("Tile");

        if (tileObject.GetComponent<BoxCollider>() == null)
            tileObject.AddComponent<BoxCollider>();

        return tileObject;
    }
    private void UpdateTileMaterials()
    {
        ChessPiece whiteKing = null;
        ChessPiece blackKing = null;

        foreach (var p in chessPieces)
        {
            if (p != null && p.type == ChessPieceType.King)
                if (p.team == 0) whiteKing = p; else blackKing = p;
        }

        for (int x = 0; x < TILE_COUNT_X; x++)
        {
            for (int y = 0; y < TILE_COUNT_Y; y++)
            {
                GameObject tile = tiles[x, y];
                MeshRenderer renderer = tile.GetComponent<MeshRenderer>();
                int layer = tile.layer;

                // Use the cached check state — never call IsSquareAttacked here.
                // IsSquareAttacked reads ChessPiece.currentX/Y which the AI background
                // thread mutates during search; reading per-frame caused a data race.
                bool isKingInCheck =
                    (whiteKing != null && x == whiteKing.currentX && y == whiteKing.currentY && _whiteKingInCheck) ||
                    (blackKing != null && x == blackKing.currentX && y == blackKing.currentY && _blackKingInCheck);

                if (isKingInCheck)
                {
                    renderer.material = checkMaterial;
                }
                else if (layer == LayerMask.NameToLayer("Highlight") && showPossibleMovesEnabled)
                {
                    renderer.material = highlightMaterial;
                }
                else if (layer == LayerMask.NameToLayer("Hover") && hoverTilesEnabled)
                {
                    renderer.material = hoverMaterial;
                }
                else
                {
                    renderer.material = tileMaterial;
                }
            }
        }
    }

    /// <summary>
    /// Recomputes and caches which kings are in check.
    /// Must only be called from the main thread, after a real move has been
    /// committed to <see cref="chessPieces"/>, so that the AI background thread
    /// is idle and <see cref="IsSquareAttacked"/> reads a stable board state.
    /// </summary>
    private void RefreshCheckState()
    {
        ChessPiece wk = null, bk = null;
        foreach (var p in chessPieces)
        {
            if (p == null) continue;
            if (p.type == ChessPieceType.King)
            {
                if (p.team == 0) wk = p;
                else             bk = p;
            }
        }
        _whiteKingInCheck = wk != null && IsSquareAttacked(new Vector2Int(wk.currentX, wk.currentY), 1);
        _blackKingInCheck = bk != null && IsSquareAttacked(new Vector2Int(bk.currentX, bk.currentY), 0);
    }
    private void HighlightTiles()
    {
        if (!showPossibleMovesEnabled)
            return;

        for (int i = 0; i < availableMoves.Count; i++)
        {
            var tile = tiles[availableMoves[i].x, availableMoves[i].y];
            tile.layer = LayerMask.NameToLayer("Highlight");
        }
    }
    private void RemoveHighlightTiles()
    {
        for (int i = 0; i < availableMoves.Count; i++)
        {
            var tile = tiles[availableMoves[i].x, availableMoves[i].y];
            tile.layer = LayerMask.NameToLayer("Tile");
        }

        availableMoves.Clear();
    }

    // Spawning
    private void SpawnAllPieces()
    {
        chessPieces = new ChessPiece[TILE_COUNT_X, TILE_COUNT_Y];

        int whiteTeam = 0, blackTeam = 1;

        chessPieces[0, 0] = SpawnSinglePiece(ChessPieceType.Rook, whiteTeam);
        if (!testCastling)
        {
            chessPieces[1, 0] = SpawnSinglePiece(ChessPieceType.Knight, whiteTeam);
            chessPieces[2, 0] = SpawnSinglePiece(ChessPieceType.Bishop, whiteTeam);
            chessPieces[3, 0] = SpawnSinglePiece(ChessPieceType.Queen, whiteTeam);
        }
        chessPieces[4, 0] = SpawnSinglePiece(ChessPieceType.King, whiteTeam);
        if (!testCastling)
        {
            chessPieces[5, 0] = SpawnSinglePiece(ChessPieceType.Bishop, whiteTeam);
            chessPieces[6, 0] = SpawnSinglePiece(ChessPieceType.Knight, whiteTeam);
        }
        chessPieces[7, 0] = SpawnSinglePiece(ChessPieceType.Rook, whiteTeam);
        if (!debugRemovePawns)
        {
            for (int i = 0; i < TILE_COUNT_X; i++)
                chessPieces[i, 1] = SpawnSinglePiece(ChessPieceType.Pawn, whiteTeam);
        }

        chessPieces[0, 7] = SpawnSinglePiece(ChessPieceType.Rook, blackTeam);
        if (!testCastling)
        {
            chessPieces[1, 7] = SpawnSinglePiece(ChessPieceType.Knight, blackTeam);
            chessPieces[2, 7] = SpawnSinglePiece(ChessPieceType.Bishop, blackTeam);
            chessPieces[3, 7] = SpawnSinglePiece(ChessPieceType.Queen, blackTeam);
        }
        chessPieces[4, 7] = SpawnSinglePiece(ChessPieceType.King, blackTeam);
        if (!testCastling)
        {
            chessPieces[5, 7] = SpawnSinglePiece(ChessPieceType.Bishop, blackTeam);
            chessPieces[6, 7] = SpawnSinglePiece(ChessPieceType.Knight, blackTeam);
        }
        chessPieces[7, 7] = SpawnSinglePiece(ChessPieceType.Rook, blackTeam);
        if (!debugRemovePawns)
        {
            for (int i = 0; i < TILE_COUNT_X; i++)
                chessPieces[i, 6] = SpawnSinglePiece(ChessPieceType.Pawn, blackTeam);
        }
    }
    private ChessPiece SpawnSinglePiece(ChessPieceType type, int team)
    {
        GameObject pieceObj = Instantiate(prefabs[(int)type - 1]);
        ChessPiece cp = pieceObj.GetComponent<ChessPiece>();

        cp.transform.SetParent(transform);
        cp.transform.localScale = prefabs[(int)type - 1].transform.localScale;

        cp.type = type;
        cp.team = team;
        cp.GetComponent<MeshRenderer>().material = teamMaterials[team];

        Vector3 pieceRotation = (team == 1) ? blackPiecesRotation : whitePiecesRotation;
        cp.transform.localRotation = Quaternion.Euler(pieceRotation);

        return cp;
    }

    // Positioning
    private void PositionAllPieces()
    {
        for (int x = 0; x < TILE_COUNT_X; x++)
            for (int y = 0; y < TILE_COUNT_Y; y++)
                if (chessPieces[x, y] != null)
                    PositionSinglePiece(x, y, true);
    }
    private void PositionSinglePiece(int x, int y, bool force = false)
    {
        chessPieces[x, y].currentX = x;
        chessPieces[x, y].currentY = y;

        Vector3 targetPosition = GetTileCenter(x, y);

        if (force || !smoothPieceMovement)
        {
            chessPieces[x, y].SetPosition(targetPosition, force);
        }
        else
        {
            if (!movingPieces.Contains(chessPieces[x, y]))
                movingPieces.Add(chessPieces[x, y]);
        }
    }
    private Vector3 GetTileCenter(int x, int y)
    {
        return new Vector3(x * tileSize, yOffset, y * tileSize) - bounds + new Vector3(tileSize / 2, 0, tileSize / 2);
    }

    // Checkmate
    private void CheckMate(int team)
    {
        // Free the save slot — a finished game doesn't need to stay saved
        int sessionSlot = SaveSlotManager.GetOrCreateSessionSlot();
        if (SaveSlotManager.SlotExists(sessionSlot))
            SaveSlotManager.DeleteSlot(sessionSlot);

        DisplayVictory(team);
    }
    private void DisplayVictory(int winningTeam)
    {
        victoryScreen.SetActive(true);
        // winningTeam == -1 signals a draw/stalemate: show the overlay without a winner banner
        if (winningTeam >= 0)
            victoryScreen.transform.GetChild(winningTeam).gameObject.SetActive(true);
        victoryScreen.transform.GetChild(2).gameObject.SetActive(true);
        victoryScreen.transform.GetChild(3).gameObject.SetActive(true);

        Time.timeScale = 0.3f;
        Time.fixedDeltaTime = 0.02f * Time.timeScale;
    }
    public void OnRematchButton()
    {
        // AI games never initialise the network stack — trigger rematch directly
        if (isAIGame)
        {
            GameReset();
            return;
        }

        if (GameUI.Instance.localGame)
        {
            NetRematch wrm = new()
            {
                teamId = 0,
                wantRematch = 1
            };
            Client.Instance.SendToServer(wrm);

            NetRematch brm = new()
            {
                teamId = 1,
                wantRematch = 1
            };
            Client.Instance.SendToServer(brm);
        }
        else
        {
            NetRematch rm = new()
            {
                teamId = GameUI.Instance.currentTeam,
                wantRematch = 1
            };
            Client.Instance.SendToServer(rm);
        }
    }
    public void GameReset()
    {
        Time.timeScale = 1.0f;
        Time.fixedDeltaTime = 0.02f;

        // Ensure the AI coroutine flag is cleared so the AI can move again after rematch
        isAIThinking = false;

        rematchButton.interactable = true;
        menuButton.interactable = true;

        if (rematchIndicator != null)
        {
            for (int i = 0; i < rematchIndicator.childCount; i++)
            {
                rematchIndicator.GetChild(i).gameObject.SetActive(false);
            }
        }

        if (victoryScreen != null)
        {
            for (int i = 0; i < victoryScreen.transform.childCount; i++)
            {
                victoryScreen.transform.GetChild(i).gameObject.SetActive(false);
            }
            victoryScreen.SetActive(false);
        }

        currentlyDragging = null;
        availableMoves.Clear();
        moveList.Clear();
        GameUI.Instance.playerRematch[0] = GameUI.Instance.playerRematch[1] = false;

        for (int x = 0; x < TILE_COUNT_X; x++)
        {
            for (int y = 0; y < TILE_COUNT_Y; y++)
            {
                if (chessPieces[x, y] != null)
                    Destroy(chessPieces[x, y].gameObject);
                chessPieces[x, y] = null;
            }
        }

        foreach (var p in deadWhites) { if (p) Destroy(p.gameObject); }
        foreach (var p in deadBlacks) { if (p) Destroy(p.gameObject); }
        deadWhites.Clear();
        deadBlacks.Clear();

        ResetTimers(false);

        SpawnAllPieces();
        PositionAllPieces();
        isWhiteTurn = true;
        _whiteKingInCheck = false;
        _blackKingInCheck = false;
    }
    public void OnMenuButton()
    {
        // AI games never initialise the network stack, so skip all net calls
        if (!isAIGame)
        {
            NetRematch rm = new() { teamId = GameUI.Instance.currentTeam, wantRematch = 0 };
            Client.Instance.SendToServer(rm);
            ShutDownRelay();
        }

        GameReset();

        GameUI.Instance.playerCount = -1;
        GameUI.Instance.currentTeam = -1;

        GameUI.Instance.OnLeaveFromGameMenu();
    }

    // Special Moves
    private void ProcessSpecialMove()
    {
        if (specialMove == SpecialMove.EnPassant)
        {
            var newMove = moveList[moveList.Count - 1];
            ChessPiece myPawn = chessPieces[newMove[1].x, newMove[1].y];
            var targetPawnPosition = moveList[moveList.Count - 2];
            ChessPiece enemyPawn = chessPieces[targetPawnPosition[1].x, targetPawnPosition[1].y];

            if (myPawn.currentX == enemyPawn.currentX)
            {
                if (myPawn.currentY == enemyPawn.currentY - 1
                    || myPawn.currentY == enemyPawn.currentY + 1)
                {
                    if (enemyPawn.team == 0)
                    {
                        deadWhites.Add(enemyPawn);
                        SendToPedestal(enemyPawn, 0);
                        chessPieces[enemyPawn.currentX, enemyPawn.currentY] = null;
                    }
                    else
                    {
                        deadBlacks.Add(enemyPawn);
                        SendToPedestal(enemyPawn, 1);
                        chessPieces[enemyPawn.currentX, enemyPawn.currentY] = null;
                    }
                }
            }
        }
        if (specialMove == SpecialMove.Promotion)
        {
            Vector2Int[] lastMove = moveList[^1];
            ChessPiece targetPawn = chessPieces[lastMove[1].x, lastMove[1].y];

            if (targetPawn.type == ChessPieceType.Pawn)
            {
                if (targetPawn.team == 0 && lastMove[1].y == 7)
                {
                    ChessPiece newQueen = SpawnSinglePiece(ChessPieceType.Queen, 0);
                    newQueen.transform.position = chessPieces[lastMove[1].x, lastMove[1].y].transform.position;
                    Destroy(chessPieces[lastMove[1].x, lastMove[1].y].gameObject);
                    chessPieces[lastMove[1].x, lastMove[1].y] = newQueen;
                    PositionSinglePiece(lastMove[1].x, lastMove[1].y);
                }
                else if (targetPawn.team == 1 && lastMove[1].y == 0)
                {
                    ChessPiece newQueen = SpawnSinglePiece(ChessPieceType.Queen, 1);
                    newQueen.transform.position = chessPieces[lastMove[1].x, lastMove[1].y].transform.position;
                    Destroy(chessPieces[lastMove[1].x, lastMove[1].y].gameObject);
                    chessPieces[lastMove[1].x, lastMove[1].y] = newQueen;
                    PositionSinglePiece(lastMove[1].x, lastMove[1].y);
                }
            }
        }
        if (specialMove == SpecialMove.Castling)
        {
            Vector2Int[] lastMove = moveList[^1];

            // Left Rook
            if (lastMove[1].x == 2)
            {
                if (lastMove[1].y == 0) // White Side
                {
                    ChessPiece rook = chessPieces[0, 0];
                    chessPieces[3, 0] = rook;
                    PositionSinglePiece(3, 0);
                    chessPieces[0, 0] = null;
                }
                else if (lastMove[1].y == 7) // Black Side
                {
                    ChessPiece rook = chessPieces[0, 7];
                    chessPieces[3, 7] = rook;
                    PositionSinglePiece(3, 7);
                    chessPieces[0, 7] = null;
                }
            }
            // Right Rook
            else if (lastMove[1].x == 6)
            {
                if (lastMove[1].y == 0) // White Side
                {
                    ChessPiece rook = chessPieces[7, 0];
                    chessPieces[5, 0] = rook;
                    PositionSinglePiece(5, 0);
                    chessPieces[7, 0] = null;
                }
                else if (lastMove[1].y == 7) // Black Side
                {
                    ChessPiece rook = chessPieces[7, 7];
                    chessPieces[5, 7] = rook;
                    PositionSinglePiece(5, 7);
                    chessPieces[7, 7] = null;
                }
            }
        }
    }
    private void SendToPedestal(ChessPiece piece, int team)
    {
        Transform startPivot = (team == 0) ? whiteDeathPivot : blackDeathPivot;
        List<ChessPiece> deadList = (team == 0) ? deadWhites : deadBlacks;

        int i = deadList.Count - 1;

        Vector3 origin = startPivot.position;

        float xOffset = (i % piecePerRow) * deathSpacing;
        float zOffset = (i / piecePerRow) * deathSpacing;

        Vector3 finalPos = origin + (startPivot.right * xOffset) + (startPivot.forward * zOffset);

        finalPos.y += yCaptureOffset;

        piece.SetScale(Vector3.one * deathSize);
        piece.SetPosition(finalPos);
    }
    private void PreventCheck()
    {
        ChessPiece targetKing = null;
        for (int x = 0; x < TILE_COUNT_X; x++)
            for (int y = 0; y < TILE_COUNT_Y; y++)
                if (chessPieces[x, y] != null)
                    if (chessPieces[x, y].type == ChessPieceType.King)
                        if (chessPieces[x, y].team == currentlyDragging.team)
                            targetKing = chessPieces[x, y];

        SimulateMoveForSinglePiece(currentlyDragging, ref availableMoves, targetKing);
    }
    private void SimulateMoveForSinglePiece(ChessPiece cp, ref List<Vector2Int> moves, ChessPiece targetKing)
    {
        int actualX = cp.currentX;
        int actualY = cp.currentY;
        List<Vector2Int> movesToRemove = new List<Vector2Int>();

        for (int i = 0; i < moves.Count; i++)
        {
            int simX = moves[i].x;
            int simY = moves[i].y;

            ChessPiece targetPiece = chessPieces[simX, simY];

            chessPieces[actualX, actualY] = null;
            cp.currentX = simX;
            cp.currentY = simY;
            chessPieces[simX, simY] = cp;

            Vector2Int kingPos = (cp.type == ChessPieceType.King)
            ? new Vector2Int(simX, simY)
            : new Vector2Int(targetKing.currentX, targetKing.currentY);

            if (IsSquareAttacked(kingPos, (cp.team == 0) ? 1 : 0))
            {
                movesToRemove.Add(moves[i]);
            }

            chessPieces[actualX, actualY] = cp;
            cp.currentX = actualX;
            cp.currentY = actualY;
            chessPieces[simX, simY] = targetPiece;
        }

        foreach (var move in movesToRemove) moves.Remove(move);
    }
    /// <summary>
    /// Checks whether the position after the last move is checkmate or stalemate.
    /// Calls CheckMate with the winning team (or -1 for stalemate/draw) and returns
    /// true when the game has ended so the caller can stop further processing.
    /// </summary>
    private bool CheckGameEnd()
    {
        var lastMove  = moveList[moveList.Count - 1];
        int movedTeam = chessPieces[lastMove[1].x, lastMove[1].y].team;
        int targetTeam = 1 - movedTeam; // Team that must now respond

        List<ChessPiece> attackingPieces = new();
        List<ChessPiece> defendingPieces = new();
        ChessPiece targetKing = null;

        for (int x = 0; x < TILE_COUNT_X; x++)
            for (int y = 0; y < TILE_COUNT_Y; y++)
                if (chessPieces[x, y] != null)
                {
                    if (chessPieces[x, y].team == targetTeam)
                    {
                        defendingPieces.Add(chessPieces[x, y]);
                        if (chessPieces[x, y].type == ChessPieceType.King)
                            targetKing = chessPieces[x, y];
                    }
                    else
                        attackingPieces.Add(chessPieces[x, y]);
                }

        if (targetKing == null) return false;

        // Build all squares the attacker currently controls
        List<Vector2Int> attackedSquares = new();
        for (int i = 0; i < attackingPieces.Count; i++)
        {
            var pieceMoves = attackingPieces[i].GetAvailableMoves(ref chessPieces, TILE_COUNT_X, TILE_COUNT_Y);
            for (int b = 0; b < pieceMoves.Count; b++)
                attackedSquares.Add(pieceMoves[b]);
        }

        bool kingInCheck = ContainsValidMove(ref attackedSquares,
            new Vector2Int(targetKing.currentX, targetKing.currentY));

        // Check whether the defending team has at least one legal move left
        bool hasLegalMove = false;
        for (int i = 0; i < defendingPieces.Count; i++)
        {
            List<Vector2Int> moves = defendingPieces[i].GetAvailableMoves(ref chessPieces, TILE_COUNT_X, TILE_COUNT_Y);
            defendingPieces[i].GetSpecialMoves(ref chessPieces, ref moveList, ref moves);
            SimulateMoveForSinglePiece(defendingPieces[i], ref moves, targetKing);
            if (moves.Count > 0) { hasLegalMove = true; break; }
        }

        if (!hasLegalMove)
        {
            if (kingInCheck)
            {
                Debug.Log($"Checkmate! Team {movedTeam} wins.");
                CheckMate(movedTeam); // The side that just moved wins
            }
            else
            {
                Debug.Log("Stalemate — draw.");
                CheckMate(-1); // Draw: pass -1 so DisplayVictory shows no winner
            }
            return true;
        }

        return false;
    }

    // Operations
    private void LoadSettingsFromFile()
    {
        string path = Application.persistentDataPath + "/settings.json";
        string json = EncryptionTool.LoadDecrypted(path);

        if (!string.IsNullOrEmpty(json))
        {
            SaveSettings data = JsonUtility.FromJson<SaveSettings>(json);

            this.hoverTilesEnabled = data.hoverTilesEnabled;
            this.showPossibleMovesEnabled = data.showPossibleMovesEnabled;
            this.invertBoardForBlackEnabled = this.isAIGame ? false : data.invertBoardForBlackEnabled;
            this.useTimer = data.useTimer;

            this.playerTimeMinutes = data.playerTimeMinutes;

            Debug.Log("Chessboard: Ajustes cargados. Tiempo de juego: " + this.playerTimeMinutes + " min.");
        }
    }
    private bool ContainsValidMove(ref List<Vector2Int> moves, Vector2Int pos)
    {
        for (int i = 0; i < moves.Count; i++)
            if (moves[i].x == pos.x && moves[i].y == pos.y)
                return true;

        return false;
    }
    public void MoveTo(int originalX, int originalY, int x, int y)
    {

        ChessPiece cp = chessPieces[originalX, originalY];
        Vector2Int previousPosition = new Vector2Int(originalX, originalY);

        if (chessPieces[x, y] != null)
        {
            ChessPiece ocp = chessPieces[x, y];

            if (ocp.type == ChessPieceType.King)
                return;

            if (cp.team == ocp.team)
                return;

            if (ocp.team == 0)
            {
                deadWhites.Add(ocp);
                SendToPedestal(ocp, 0);
            }
            else
            {
                deadBlacks.Add(ocp);
                SendToPedestal(ocp, 1);
            }
        }

        chessPieces[x, y] = cp;
        chessPieces[previousPosition.x, previousPosition.y] = null;

        PositionSinglePiece(x, y);

        if (turnsEnabled)
        {
            isWhiteTurn = !isWhiteTurn;
            if (GameUI.Instance.localGame)
                GameUI.Instance.currentTeam = (GameUI.Instance.currentTeam == 0) ? 1 : 0;

            if (invertBoardForBlackEnabled && !isAIGame)
            {
                StopAllCoroutines();
                StartCoroutine(RotateCamera(!isWhiteTurn));
            }
        }

        moveList.Add(new Vector2Int[] { previousPosition, new Vector2Int(x, y) });
        ProcessSpecialMove();

        if (currentlyDragging)
            currentlyDragging = null;
        RemoveHighlightTiles();

        // Skip end-of-game detection while replaying a saved game move-by-move
        if (!isLoadingLevel)
        {
            CheckGameEnd();
            RefreshCheckState(); // update cached king-in-check flags (safe: AI is idle here)
        }

        if (GameUI.Instance.localGame && !isLoadingLevel)
        {
            AutoSaveGame();
            Debug.Log("Partida guardada automáticamente tras el movimiento.");
        }

        return;
    }
    private Vector2Int LookupTileIndex(GameObject hitInfo)
    {
        for (int x = 0; x < TILE_COUNT_X; x++)
            for (int y = 0; y < TILE_COUNT_Y; y++)
                if (tiles[x, y] == hitInfo)
                    return new Vector2Int(x, y);

        return -Vector2Int.one;
    }
    private Color GetTimerColor(float time)
    {
        if (time <= 10f)
        {
            float aplha = (Mathf.Sin(Time.time * 10f) > 0) ? 1f : 0.5f;
            return new Color(1f, 0f, 0f, aplha);
        }
        else if (time <= 30f)
            return new Color(1f, 0.84f, 0f);
        else
            return Color.white;
    }
    public bool IsSquareAttacked(Vector2Int square, int attackingTeam)
    {
        for (int x = 0; x < TILE_COUNT_X; x++)
        {
            for (int y = 0; y < TILE_COUNT_Y; y++)
            {
                if (chessPieces[x, y] != null && chessPieces[x, y].team == attackingTeam)
                {
                    var moves = chessPieces[x, y].GetAvailableMoves(ref chessPieces, TILE_COUNT_X, TILE_COUNT_Y);
                    if (moves.Any(m => m.x == square.x && m.y == square.y))
                        return true;
                }
            }
        }
        return false;
    }
    private string FormatTime(float time)
    {
        int minutes = Mathf.FloorToInt(time / 60);
        int seconds = Mathf.FloorToInt(time % 60);
        return string.Format("{0:00}:{1:00}", minutes, seconds);
    }
    public System.Collections.IEnumerator RotateCamera(bool toBlack)
    {
        Debug.Log("Corrutina RotateCamera iniciada. Hacia negro: " + toBlack);
        isCameraRotating = true;
        float targetAngle = toBlack ? 180 : 0;
        Quaternion targetRotation = Quaternion.Euler(cameraPivot.transform.eulerAngles.x, targetAngle, 0);

        while (Quaternion.Angle(cameraPivot.transform.rotation, targetRotation) > 0.1f)
        {
            cameraPivot.transform.rotation = Quaternion.Slerp(cameraPivot.transform.rotation, targetRotation, Time.deltaTime * cameraRotationSpeed);
            yield return null;
        }

        cameraPivot.transform.rotation = targetRotation;
        isCameraRotating = false;
    }
    private void ResetTimers(bool onStart)
    {
        whiteTime = playerTimeMinutes * 60;
        blackTime = playerTimeMinutes * 60;

        if (timerText != null && !onStart)
        {
            timerText.color = Color.white;
            timerText.text = string.Format("White: {0} | Black: {1}",
                FormatTime(whiteTime),
                FormatTime(blackTime));
        }
    }
    public void SetInvertBoard(bool state)
    {
        invertBoardForBlackEnabled = state;
        Debug.Log("Invertir tablero fijado en: " + state);
    }
    public void ShutDownRelay()
    {
        Client.Instance.ShutDown();
        Server.Instance.ShutDown();
    }

    public SaveSettings GetCurrentGameState()
    {
        SaveSettings data = new()
        {
            hoverTilesEnabled          = this.hoverTilesEnabled,
            showPossibleMovesEnabled   = this.showPossibleMovesEnabled,
            invertBoardForBlackEnabled = this.invertBoardForBlackEnabled,
            useTimer                   = this.useTimer,
            playerTimeMinutes          = this.playerTimeMinutes,

            isWhiteTurn = this.isWhiteTurn,
            whiteTime   = this.whiteTime,
            blackTime   = this.blackTime,

            isAIGame = this.isAIGame,
            aiLevel  = this.aiLevel,
            aiTeam   = this.aiTeam
        };

        foreach (Vector2Int[] move in moveList)
            data.moveHistory.Add($"{move[0].x}{move[0].y}{move[1].x}{move[1].y}");

        return data;
    }

    private void LoadFromSaveData(SaveSettings data)
    {
        isLoadingLevel = true;

        // Restore AI configuration first — move replay may depend on these flags
        this.isAIGame = data.isAIGame;
        this.aiLevel  = data.aiLevel;
        this.aiTeam   = data.aiTeam;

        // Restore timer state
        this.whiteTime   = data.whiteTime;
        this.blackTime   = data.blackTime;
        this.isWhiteTurn = data.isWhiteTurn;

        // Make sure GameUI reflects the restored game type
        if (GameUI.Instance != null)
        {
            GameUI.Instance.localGame   = true;
            // For AI games the human is always team 0; keep currentTeam stable
            if (data.isAIGame)
                GameUI.Instance.currentTeam = 0;
        }

        // Replay each stored move to reconstruct the full board position
        foreach (string moveStr in data.moveHistory)
        {
            if (moveStr.Length < 4) continue;
            int x1 = (int)char.GetNumericValue(moveStr[0]);
            int y1 = (int)char.GetNumericValue(moveStr[1]);
            int x2 = (int)char.GetNumericValue(moveStr[2]);
            int y2 = (int)char.GetNumericValue(moveStr[3]);
            MoveTo(x1, y1, x2, y2);
        }

        // Override the turn that the replay settled on with the authoritative saved value
        isWhiteTurn = data.isWhiteTurn;

        // After replay the currentTeam toggles with each move; restore the correct value
        if (GameUI.Instance != null && data.isAIGame)
            GameUI.Instance.currentTeam = 0;

        isLoadingLevel = false;

        // Recompute check state now that the full board is restored
        RefreshCheckState();
    }

    public void AutoSaveGame()
    {
        SaveSettings data = new SaveSettings();

        // 1. Current user preferences
        data.hoverTilesEnabled          = this.hoverTilesEnabled;
        data.showPossibleMovesEnabled   = this.showPossibleMovesEnabled;
        data.invertBoardForBlackEnabled = this.isAIGame ? false : this.invertBoardForBlackEnabled;
        data.useTimer                   = this.useTimer;
        data.playerTimeMinutes          = this.playerTimeMinutes;

        // 2. Game state
        data.isWhiteTurn = this.isWhiteTurn;
        data.whiteTime   = this.whiteTime;
        data.blackTime   = this.blackTime;

        // 3. AI configuration — required so "Continue" restores AI mode
        data.isAIGame = this.isAIGame;
        data.aiLevel  = this.aiLevel;
        data.aiTeam   = this.aiTeam;

        // 4. Move history
        foreach (Vector2Int[] move in moveList)
            data.moveHistory.Add($"{move[0].x}{move[0].y}{move[1].x}{move[1].y}");

        // 5. Write to the session slot (picks first empty / oldest if all full)
        int slot = SaveSlotManager.GetOrCreateSessionSlot();
        SaveSlotManager.SaveToSlot(slot, data);
    }

    private async void TriggerAIMove()
    {
        isAIThinking = true;
        Debug.Log($"[ChessAI] Searching at level {aiLevel}...");

        Vector2Int[] chosenMove = await ChessAI.Instance.GetBestMoveAsync(chessPieces, aiTeam, aiLevel);

        // The board state may have changed while awaiting (e.g. scene unloaded)
        if (this == null || isLoadingLevel) { isAIThinking = false; return; }

        if (chosenMove != null && chosenMove.Length == 2)
        {
            int oX = chosenMove[0].x;
            int oY = chosenMove[0].y;
            int dX = chosenMove[1].x;
            int dY = chosenMove[1].y;

            ChessPiece aiPiece = chessPieces[oX, oY];
            if (aiPiece == null)
            {
                Debug.LogWarning($"[ChessAI] No piece found at ({oX},{oY}). Skipping move.");
                isAIThinking = false;
                return;
            }

            // Populate specialMove so ProcessSpecialMove handles castling/promotion correctly
            availableMoves = aiPiece.GetAvailableMoves(ref chessPieces, TILE_COUNT_X, TILE_COUNT_Y);
            specialMove    = aiPiece.GetSpecialMoves(ref chessPieces, ref moveList, ref availableMoves);

            Debug.Log($"[ChessAI] Move chosen: ({oX},{oY}) → ({dX},{dY})");
            MoveTo(oX, oY, dX, dY);
        }
        else
        {
            Debug.LogWarning("[ChessAI] No valid move found — possible checkmate or stalemate.");
        }

        isAIThinking = false;
    }
}