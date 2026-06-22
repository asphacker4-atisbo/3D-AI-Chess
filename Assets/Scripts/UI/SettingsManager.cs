using TMPro;
using UnityEngine;

public class SettingsManager : MonoBehaviour
{
    [SerializeField] private GameObject playerTime;
    [SerializeField] private TextMeshProUGUI title;

    // Tabs
    [SerializeField] private GameObject generalTab;
    [SerializeField] private GameObject boardTab;
    [SerializeField] private GameObject audioTab;
    [SerializeField] private GameObject difficultyTab;

    private void Start()
    {
        ChangeToGeneral();

        playerTime.SetActive(false);
    }

    public void ToggleObject(bool state)
    {
        if (playerTime != null)
            playerTime.SetActive(state);
    }
    public void ChangeToGeneral()
    {
        title.text = "General";

        generalTab.SetActive(true);
        boardTab.SetActive(false);
        audioTab.SetActive(false);
        difficultyTab.SetActive(false);

    }
    public void ChangeToBoard()
    {
        title.text = "Board";

        generalTab.SetActive(false);
        boardTab.SetActive(true);
        audioTab.SetActive(false);
        difficultyTab.SetActive(false);
    }
    public void ChangeToAudio()
    {
        title.text = "Audio";

        generalTab.SetActive(false);
        boardTab.SetActive(false);
        audioTab.SetActive(true);
        difficultyTab.SetActive(false);
    }
    public void ChangeToDifficulty()
    {
        title.text = "Difficulty";

        generalTab.SetActive(false);
        boardTab.SetActive(false);
        audioTab.SetActive(false);
        difficultyTab.SetActive(true);
    }
}
