using System;
using System.Collections;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using TMPro;
using Unity.Networking.Transport;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class GameUI : MonoBehaviour
{
    public static GameUI Instance { set; get; }

    [SerializeField] private Animator menuAnimator;
    [SerializeField] private TMP_InputField addressInput;

    private const int TILE_COUNT_X = 8;
    private const int TILE_COUNT_Y = 8;

    public Server server;
    public Client client;
    [SerializeField] private GameObject[] objectsToDestroy;

    [Header("UI References")]
    public Slider volumeSlider;
    public TMP_InputField timerMinutesInput;
    public Toggle tipsToggle, autoSaveToggle, hoverToggle, invertToggle, musicToggle, movesToggle, timerToggle;
    [SerializeField] private TMP_Text ipField;

    [Header("Save Slot Panel")]
    [Tooltip("Cinzel or other rustic TMP font asset. Assign in Inspector so the save-slot panel matches the game's visual theme.")]
    [SerializeField] private TMP_FontAsset rusticFont;

    [Header("AI Panel Configurations")]
    [SerializeField] private GameObject modeSelectionPanel; // Panel con botones: "Online" y "Contra IA"
    [SerializeField] private GameObject aiConfigurationPanel; // Panel con Slider de nivel y botón "Empezar"
    [SerializeField] private Slider aiLevelSlider; // Slider configurado de 1 a 8 (Whole Numbers)
    [SerializeField] private TMP_Text aiLevelValueText;

    // Multi logic
    public int playerCount = -1;
    public int currentTeam = -1;
    public bool localGame = true;
    public bool[] playerRematch = new bool[2];

    public Action<bool> SetLocalGame;

    private string savePath;
    public static SaveSettings dataToLoad;

    // Backing field for the AI level chosen in the config panel — used by OnAIGameSceneLoaded
    private int _pendingAILevel = 1;

    private void Awake()
    {
        Instance = this;
        DontDestroyOnLoad(gameObject);
        savePath = Application.persistentDataPath + "/settings.json";
        RegisterEvents();
        SaveSlotPanel.RusticFont = rusticFont;  // must be set before EnsureExists builds the UI
        SaveSlotPanel.EnsureExists(); // creates the panel singleton if not already present
    }

    private void OnDestroy()
    {
        UnRegisterEvents();
    }

    public void OnApplyButton()
    {
        SaveSettings data = new SaveSettings();

        data.gameplayTips = tipsToggle.isOn;
        data.autoSave = autoSaveToggle.isOn;
        data.volume = volumeSlider.value;
        data.musicEnabled = musicToggle.isOn;

        data.hoverTilesEnabled = hoverToggle.isOn;
        data.invertBoardForBlackEnabled = invertToggle.isOn;
        data.showPossibleMovesEnabled = movesToggle.isOn;
        data.useTimer = timerToggle.isOn;

        if (float.TryParse(timerMinutesInput.text, out float minutes))
        {
            data.playerTimeMinutes = Mathf.Clamp(minutes, 1, 30);
            timerMinutesInput.text = data.playerTimeMinutes.ToString();
        }
        else
        {
            data.playerTimeMinutes = 10;
        }

        string json = JsonUtility.ToJson(data, true);
        EncryptionTool.SaveEncrypted(json, savePath);
        Debug.Log("Configuración guardada. Tiempo: " + data.playerTimeMinutes + " min.");
    }

    // Buttons
    public void OnNewGameButton()
    {
        SaveSlotManager.ClearSessionSlot(); // next game gets a fresh slot
        menuAnimator.SetTrigger("InGameMenu");
        SetLocalGame?.Invoke(true);
        server.Init(8007);
        client.Init("127.0.0.1", 8007);
    }
    /// <summary>
    /// Called by the "Continue / Load Game" button in the main menu.
    /// Opens the slot selection panel so the player can pick which save to load.
    /// </summary>
    public void OnContinueButton()
    {
        SaveSlotPanel.Instance?.Open(slot => LoadFromSlot(slot));
    }

    /// <summary>
    /// Opens the slot panel in Load mode — safe to call from any scene.
    /// </summary>
    public void OpenLoadPanel()
    {
        SaveSlotPanel.Instance?.Open(slot => LoadFromSlot(slot));
    }

    /// <summary>
    /// Loads the save in <paramref name="slot"/> and transitions to the game scene.
    /// Works from both the main menu and from within an active game.
    /// </summary>
    private void LoadFromSlot(int slot)
    {
        SaveSettings data = SaveSlotManager.LoadFromSlot(slot);
        if (data == null)
        {
            Debug.LogWarning($"[SaveSlot] Slot {slot} is empty — nothing to load.");
            return;
        }

        // Pin the session to this slot so auto-saves overwrite it going forward
        SaveSlotManager.SetSessionSlot(slot);

        dataToLoad  = data;
        localGame   = true;
        currentTeam = 0;

        if (data.isAIGame)
        {
            // Inject AI config into the freshly loaded Chessboard after the scene loads
            _pendingAILevel = data.aiLevel;
            SceneManager.sceneLoaded += OnAIGameSceneLoaded;
        }
        else
        {
            // Initialise local networking for two-player local games
            SetLocalGame?.Invoke(true);
            currentTeam = 0; // SetLocalGame resets currentTeam to -1; restore it
            if (server != null && client != null)
            {
                server.ShutDown();
                server.Init(8007);
                client.ShutDown();
                client.Init("127.0.0.1", 8007);
            }
        }

        // Trigger the "entering game" menu animation only when we are on the main menu
        if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name != "GameScene")
            menuAnimator.SetTrigger("InGameMenu");

        SceneManager.sceneLoaded += OnSceneLoaded;
        SceneManager.LoadScene("GameScene");
    }
    public void OnMultiplayerButton()
    {
        // En lugar de ir directo al menú online, mostramos el panel de selección de tipo de multiplayer
        if (modeSelectionPanel != null)
        {
            modeSelectionPanel.SetActive(true);
        }
        else
        {
            // Fail-safe por si no se han asignado los paneles en el Inspector todavía
            menuAnimator.SetTrigger("OnlineMenu");
        }
    }
    public void ChooseLocalNetworkMode()
    {
        modeSelectionPanel.SetActive(false);
        menuAnimator.SetTrigger("OnlineMenu"); // Ejecuta la animación original del juego
    }
    public void ChooseAIMode()
    {
        modeSelectionPanel.SetActive(false);
        aiConfigurationPanel.SetActive(true);
        UpdateAILevelSliderText(); // Sincroniza el texto inicial
    }

    // Conectado al evento OnValueChanged de aiLevelSlider de forma dinámica
    public void UpdateAILevelSliderText()
    {
        if (aiLevelSlider != null && aiLevelValueText != null)
        {
            int level = (int)aiLevelSlider.value;
            // Cálculo estimado para reflejar la progresión hasta ~2500 Elo a nivel 8
            int estimatedElo = 900 + (level * 200);
            aiLevelValueText.text = $"Nivel: {level} (Elo Estimado: ~{estimatedElo})";
        }
    }

    // Conectado al botón "Comenzar Partida" dentro de aiConfigurationPanel
    public void OnStartAIGameButton()
    {
        aiConfigurationPanel.SetActive(false);

        _pendingAILevel = aiLevelSlider != null ? Mathf.Clamp((int)aiLevelSlider.value, 1, 8) : 1;

        localGame   = true;
        currentTeam = 0; // Human always plays White

        SaveSlotManager.ClearSessionSlot(); // fresh slot for this new AI game

        // Use a named handler so it can be properly unregistered after firing once
        SceneManager.sceneLoaded += OnAIGameSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;

        menuAnimator.SetTrigger("InGameMenu");
        SceneManager.LoadScene("GameScene");
    }

    /// <summary>
    /// Fires once when the GameScene finishes loading for a new AI game.
    /// Injects AI configuration into the freshly created Chessboard instance,
    /// then unregisters itself so it never fires again.
    /// </summary>
    private void OnAIGameSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name != "GameScene") return;

        SceneManager.sceneLoaded -= OnAIGameSceneLoaded;

        if (Chessboard.Instance != null)
        {
            Chessboard.Instance.isAIGame = true;
            Chessboard.Instance.aiLevel  = _pendingAILevel;
            Chessboard.Instance.aiTeam   = 1; // AI always plays Black
        }
    }

    public void OnAILobbyBackButton()
    {
        aiConfigurationPanel.SetActive(false);
        modeSelectionPanel.SetActive(true);
    }

    public void OnModeSelectionBackButton()
    {
        modeSelectionPanel.SetActive(false);
    }
    public void OnOptionsButton()
    {
        menuAnimator.SetTrigger("SettingsMenu");
    }

    public void OnQuitGameButton()
    {
        if (server != null) server.ShutDown();
        if (client != null) client.ShutDown();

#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    public void OnOnlineHostButton()
    {
        SetLocalGame?.Invoke(false);

        server.ShutDown();
        client.ShutDown();

        server.Init(8007);
        client.Init("127.0.0.1", 8007);

        string localIP = GetLocalIPAdress();
        ipField.text = "Your IP: " + localIP;

        menuAnimator.SetTrigger("HostMenu");
    }
    public void OnOnlineConnectButton()
    {
        SetLocalGame?.Invoke(false);
        client.Init(addressInput.text, 8007);
    }
    public void OnOnlineBackButton()
    {
        menuAnimator.SetTrigger("StartMenu");
    }

    public void OnHostBackButton()
    {
        server.ShutDown();
        client.ShutDown();
        menuAnimator.SetTrigger("OnlineMenu");
    }
    public void OnOptionsBackButton()
    {
        menuAnimator.SetTrigger("StartMenu");
    }

    public void OnLeaveFromGameMenu()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
        SceneManager.LoadScene("MainMenuScene");
        menuAnimator.SetTrigger("StartMenu");
    }

    // Operations
    string GetLocalIPAdress()
    {
        foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (ni.OperationalStatus == OperationalStatus.Up)
            {
                foreach (UnicastIPAddressInformation ip in ni.GetIPProperties().UnicastAddresses)
                {
                    if (ip.Address.AddressFamily == AddressFamily.InterNetwork &&
                    !ip.Address.ToString().StartsWith("127."))
                        return ip.Address.ToString();
                }
            }
        }

        return "No IPV4 found";
    }

    #region
    private void RegisterEvents()
    {
        NetUtility.S_WELCOME += OnWelcomeServer;
        NetUtility.S_MAKE_MOVE += OnMakeMoveServer;
        NetUtility.S_REMATCH += OnRematchServer;

        NetUtility.C_WELCOME += OnWelcomeClient;
        NetUtility.C_START_GAME += OnStartGameClient;
        NetUtility.C_MAKE_MOVE += OnMakeMoveClient;
        NetUtility.C_REMATCH += OnRematchClient;

        SetLocalGame += OnSetLocalGame;
    }

    private void UnRegisterEvents()
    {
        NetUtility.S_WELCOME -= OnWelcomeServer;
        NetUtility.S_MAKE_MOVE -= OnMakeMoveServer;
        NetUtility.S_REMATCH -= OnRematchServer;

        NetUtility.C_WELCOME -= OnWelcomeClient;
        NetUtility.C_START_GAME -= OnStartGameClient;
        NetUtility.C_MAKE_MOVE -= OnMakeMoveClient;
        NetUtility.C_REMATCH -= OnRematchClient;

        SetLocalGame -= OnSetLocalGame;
    }

    // Server
    private void OnWelcomeServer(NetMessage msg, NetworkConnection cnn)
    {
        // Client has connected, assign a team and return the message back to him
        NetWelcome nw = msg as NetWelcome;

        // Assign a team
        nw.AssignedTeam = ++playerCount;

        // Return back to the client
        Server.Instance.SendToClient(cnn, nw);

        // If full start game
        if (playerCount == 1)
            Server.Instance.Broadcast(new NetStartGame());
    }
    private void OnMakeMoveServer(NetMessage msg, NetworkConnection cnn)
    {
        NetMakeMove mm = msg as NetMakeMove;

        // Make validations here to evitate hacking

        Server.Instance.Broadcast(mm);
    }
    private void OnRematchServer(NetMessage msg, NetworkConnection cnn)
    {
        Server.Instance.Broadcast(msg);
    }

    // Client
    private void OnWelcomeClient(NetMessage msg)
    {
        // Receive the connection message
        NetWelcome nw = msg as NetWelcome;

        // Assign team
        currentTeam = nw.AssignedTeam;

        Debug.Log($"My assigned team is {nw.AssignedTeam}");

        if (localGame && currentTeam == 0)
            Server.Instance.Broadcast(new NetStartGame());
    }
    private void OnStartGameClient(NetMessage msg)
    {
        if (objectsToDestroy != null)
        {
            foreach (GameObject obj in objectsToDestroy)
            {
                if (obj != null)
                {
                    Destroy(obj);
                }
            }
        }

        SceneManager.sceneLoaded += OnSceneLoaded;
        SceneManager.LoadScene("GameScene");
    }
    private void OnMakeMoveClient(NetMessage msg)
    {
        NetMakeMove mm = msg as NetMakeMove;

        if (mm.teamId != currentTeam)
        {
            ChessPiece target = Chessboard.Instance.chessPieces[mm.originalX, mm.originalY];

            Chessboard.Instance.availableMoves = target.GetAvailableMoves(ref Chessboard.Instance.chessPieces, TILE_COUNT_X, TILE_COUNT_Y);
            Chessboard.Instance.specialMove = target.GetSpecialMoves(ref Chessboard.Instance.chessPieces, ref Chessboard.Instance.moveList, ref Chessboard.Instance.availableMoves);

            Chessboard.Instance.MoveTo(mm.originalX, mm.originalY, mm.destinationX, mm.destinaionY);
        }
    }
    private void OnRematchClient(NetMessage msg)
    {
        NetRematch rm = msg as NetRematch;

        playerRematch[rm.teamId] = rm.wantRematch == 1;

        if (rm.teamId != currentTeam)
        {
            Chessboard.Instance.rematchIndicator.transform.GetChild((rm.wantRematch == 1) ? 0 : 1).gameObject.SetActive(true);
            if (rm.wantRematch != 1)
            {
                Chessboard.Instance.rematchButton.interactable = false;
            }
        }

        if (playerRematch[0] && playerRematch[1])
            Chessboard.Instance.GameReset();
    }

    private void OnSetLocalGame(bool v)
    {
        playerCount = -1;
        currentTeam = -1;
        localGame = v;
    }
    private IEnumerator SetupCamera()
    {
        if (Chessboard.Instance == null) yield break;

        // ── Continuing a saved game ────────────────────────────────────────────
        if (dataToLoad != null && dataToLoad.moveHistory != null && dataToLoad.moveHistory.Count > 0)
        {
            // AI games: human is always White — keep the white-side camera
            if (dataToLoad.isAIGame)
            {
                Chessboard.Instance.ChangeCamera(CameraAngle.whiteTeam);
                yield break;
            }

            // Local game: rotate to black's perspective if it was their turn and invert is on
            if (dataToLoad.invertBoardForBlackEnabled && !dataToLoad.isWhiteTurn)
            {
                yield return new WaitForEndOfFrame();
                Chessboard.Instance.StartCoroutine(Chessboard.Instance.RotateCamera(true));
                yield break;
            }

            // Otherwise stay on white's side
            Chessboard.Instance.ChangeCamera(CameraAngle.whiteTeam);
            yield break;
        }

        // ── Starting a new game ────────────────────────────────────────────────
        if (!localGame && currentTeam != -1)
            Chessboard.Instance.SetInvertBoard(false);

        if (currentTeam == 0)
            Chessboard.Instance.ChangeCamera(CameraAngle.whiteTeam);
        else if (currentTeam == 1)
        {
            yield return new WaitForEndOfFrame();
            Chessboard.Instance.StartCoroutine(Chessboard.Instance.RotateCamera(true));
        }
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name == "GameScene")
        {
            Debug.Log($"Escena cargada. Mi equipo guardado en GameUI es: {currentTeam}");
            StartCoroutine(SetupCamera());
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }
    }
    #endregion
}
