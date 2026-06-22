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

    public List<string> moveHistory = new List<string>(); // Formato: "x1y1x2y2"
    public bool isWhiteTurn;
    public float whiteTime;
    public float blackTime;
}