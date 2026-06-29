using System.Collections.Generic;

[System.Serializable]
public class SaveSettings
{
    public bool gameplayTips;
    public bool autoSave;
    public float volume;
    public bool musicEnabled;

    public bool hoverTilesEnabled;
    public bool invertBoardForBlackEnabled;
    public bool showPossibleMovesEnabled;
    public bool useTimer;
    public float playerTimeMinutes;

    public List<string> moveHistory = new List<string>(); // Format: "x1y1x2y2"
    public bool isWhiteTurn;
    public float whiteTime;
    public float blackTime;

    // AI game configuration — persisted so "Continue" restores AI mode correctly
    public bool isAIGame;
    public int  aiLevel;
    public int  aiTeam;

    // Slot metadata — written by SaveSlotManager, displayed on slot cards
    public string saveDateTime;  // e.g. "Jan 15, 2025 · 14:32"
    public int    moveCount;     // cached copy of moveHistory.Count for fast display
    public long   saveTimestamp; // UTC Unix seconds — used for oldest-slot eviction
}