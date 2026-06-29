using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Self-constructing save-slot panel built entirely with UGUI at runtime.
/// Call <see cref="EnsureExists"/> once (e.g. from GameUI.Awake) and the panel
/// will be available in every scene through <see cref="Instance"/>.
///
/// The panel is Load-only. Saving is handled automatically by the game after
/// each move; the player never needs to choose a slot manually.
/// </summary>
public class SaveSlotPanel : MonoBehaviour
{
    public static SaveSlotPanel Instance { get; private set; }

    /// <summary>
    /// Optional TMP font applied to all panel text.
    /// Assign before calling <see cref="EnsureExists"/> so it is available
    /// when the UI is built (e.g. set it in GameUI.Awake before EnsureExists).
    /// </summary>
    public static TMP_FontAsset RusticFont { get; set; }

    // ── layout constants ──────────────────────────────────────────────────────
    private const float PanelW       = 860f;
    private const float PanelH       = 650f;
    private const float TitleBarH    = 64f;
    private const float SlotPadH     = 12f;
    private const float SlotSpacing  = 8f;
    private const float BadgeW       = 48f;
    private const float BorderThick  = 3f;
    private const float ButtonBlockW = 112f;

    // ── rustic colour palette ─────────────────────────────────────────────────
    private static readonly Color ColOverlay      = new Color(0.05f, 0.02f, 0.00f, 0.80f);
    private static readonly Color ColPanelBorder  = new Color(0.62f, 0.40f, 0.12f, 1.00f);
    private static readonly Color ColPanel        = new Color(0.18f, 0.10f, 0.04f, 0.97f);
    private static readonly Color ColTitleBar     = new Color(0.12f, 0.06f, 0.02f, 1.00f);
    private static readonly Color ColTitleBorder  = new Color(0.55f, 0.34f, 0.10f, 1.00f);
    private static readonly Color ColSlotBorder   = new Color(0.50f, 0.32f, 0.10f, 0.70f);
    private static readonly Color ColSlotBg       = new Color(0.27f, 0.16f, 0.06f, 1.00f);
    private static readonly Color ColBadge        = new Color(0.42f, 0.24f, 0.08f, 1.00f);
    private static readonly Color ColBtnLoad      = new Color(0.16f, 0.36f, 0.10f, 1.00f);
    private static readonly Color ColBtnDelete    = new Color(0.45f, 0.09f, 0.09f, 1.00f);
    private static readonly Color ColBtnClose     = new Color(0.38f, 0.14f, 0.04f, 1.00f);
    private static readonly Color ColTextPrimary  = new Color(0.95f, 0.87f, 0.62f, 1.00f);
    private static readonly Color ColTextSub      = new Color(0.80f, 0.65f, 0.38f, 1.00f);
    private static readonly Color ColTextDate     = new Color(0.60f, 0.47f, 0.28f, 1.00f);
    private static readonly Color ColTextEmpty    = new Color(0.50f, 0.38f, 0.20f, 0.75f);
    private static readonly Color ColBadgeNum     = new Color(0.95f, 0.87f, 0.62f, 1.00f);
    private static readonly Color ColOutlineText  = new Color(0.10f, 0.05f, 0.01f, 1.00f);

    // ── runtime state ─────────────────────────────────────────────────────────
    private GameObject      _overlay;
    private Action<int>     _onLoad;

    private readonly List<SlotRow> _rows = new(SaveSlotManager.SlotCount);

    private struct SlotRow
    {
        public TextMeshProUGUI GameTypeTMP;
        public TextMeshProUGUI DetailsTMP;
        public TextMeshProUGUI DateTMP;
        public Button          LoadBtn;
        public Button          DeleteBtn;
    }

    // ── public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates the panel singleton if it does not already exist.
    /// Safe to call every time GameUI.Awake runs.
    /// </summary>
    public static void EnsureExists()
    {
        if (Instance != null) return;
        var go = new GameObject("[SaveSlotPanel]");
        DontDestroyOnLoad(go);
        go.AddComponent<SaveSlotPanel>();
    }

    /// <summary>Opens the panel so the player can load or delete a saved game.</summary>
    /// <param name="onLoad">Invoked with the chosen slot number when Load is pressed.</param>
    public void Open(Action<int> onLoad)
    {
        _onLoad = onLoad;
        Refresh();
        _overlay.SetActive(true);
    }

    /// <summary>Hides the panel without performing any action.</summary>
    public void Close() => _overlay.SetActive(false);

    // ── Unity lifecycle ───────────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        BuildUI();
        _overlay.SetActive(false);
    }

    // ── slot data refresh ─────────────────────────────────────────────────────

    private void Refresh()
    {
        for (int i = 0; i < SaveSlotManager.SlotCount; i++)
        {
            int          slot  = i + 1;
            SaveSettings d     = SaveSlotManager.LoadFromSlot(slot);
            SlotRow      row   = _rows[i];
            bool         empty = d == null;

            if (empty)
            {
                row.GameTypeTMP.text  = "— Empty —";
                row.GameTypeTMP.color = ColTextEmpty;
                row.DetailsTMP.text   = string.Empty;
                row.DateTMP.text      = string.Empty;
            }
            else
            {
                row.GameTypeTMP.color = ColTextPrimary;
                row.GameTypeTMP.text  = d.isAIGame
                    ? $"vs AI  ·  Level {d.aiLevel}"
                    : "Local Game";

                int    moves   = d.moveCount > 0 ? d.moveCount : (d.moveHistory?.Count ?? 0);
                string turn    = d.isWhiteTurn ? "White to move" : "Black to move";
                string details = $"{moves} moves  ·  {turn}";
                if (d.useTimer)
                    details += $"  |  W {Fmt(d.whiteTime)}  B {Fmt(d.blackTime)}";
                row.DetailsTMP.text = details;

                row.DateTMP.text = d.saveDateTime ?? string.Empty;
            }

            row.LoadBtn.interactable   = !empty;
            row.DeleteBtn.interactable = !empty;
        }
    }

    private static string Fmt(float t) =>
        $"{Mathf.FloorToInt(t / 60f):00}:{Mathf.FloorToInt(t % 60f):00}";

    // ── UI construction ───────────────────────────────────────────────────────

    private void BuildUI()
    {
        // ── overlay canvas — always on top ────────────────────────────────
        var canvasGO = new GameObject("Canvas");
        canvasGO.transform.SetParent(transform, false);
        var canvas          = canvasGO.AddComponent<Canvas>();
        canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 200;
        var scaler                 = canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode     = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight  = 0.5f;
        canvasGO.AddComponent<GraphicRaycaster>();

        // ── dim overlay — click anywhere to close ─────────────────────────
        _overlay = MakeImage(canvasGO.transform, "Overlay", ColOverlay);
        Stretch(_overlay.GetComponent<RectTransform>());
        _overlay.AddComponent<Button>().onClick.AddListener(Close);

        // ── amber border (slightly larger than panel) ──────────────────────
        var border   = MakeImage(_overlay.transform, "PanelBorder", ColPanelBorder);
        var borderRT = border.GetComponent<RectTransform>();
        borderRT.anchorMin        = new Vector2(0.5f, 0.5f);
        borderRT.anchorMax        = new Vector2(0.5f, 0.5f);
        borderRT.pivot            = new Vector2(0.5f, 0.5f);
        borderRT.anchoredPosition = Vector2.zero;
        borderRT.sizeDelta        = new Vector2(PanelW + BorderThick * 2f, PanelH + BorderThick * 2f);

        // ── main content panel ────────────────────────────────────────────
        var panel   = MakeImage(_overlay.transform, "Panel", ColPanel);
        var panelRT = panel.GetComponent<RectTransform>();
        panelRT.anchorMin        = new Vector2(0.5f, 0.5f);
        panelRT.anchorMax        = new Vector2(0.5f, 0.5f);
        panelRT.pivot            = new Vector2(0.5f, 0.5f);
        panelRT.anchoredPosition = Vector2.zero;
        panelRT.sizeDelta        = new Vector2(PanelW, PanelH);
        // Swallow clicks so they don't reach the overlay Button
        var noopBtn = panel.AddComponent<Button>();
        noopBtn.transition = Selectable.Transition.None;

        // ── title bar ─────────────────────────────────────────────────────
        var titleBar   = MakeImage(panel.transform, "TitleBar", ColTitleBar);
        var titleBarRT = titleBar.GetComponent<RectTransform>();
        titleBarRT.anchorMin = new Vector2(0f, 1f);
        titleBarRT.anchorMax = new Vector2(1f, 1f);
        titleBarRT.pivot     = new Vector2(0.5f, 1f);
        titleBarRT.offsetMin = new Vector2(0f, -TitleBarH);
        titleBarRT.offsetMax = Vector2.zero;

        // Thin amber accent line at the bottom of the title bar
        var titleLine   = MakeImage(titleBar.transform, "TitleLine", ColTitleBorder);
        var titleLineRT = titleLine.GetComponent<RectTransform>();
        titleLineRT.anchorMin = new Vector2(0f, 0f);
        titleLineRT.anchorMax = new Vector2(1f, 0f);
        titleLineRT.pivot     = new Vector2(0.5f, 1f);
        titleLineRT.offsetMin = Vector2.zero;
        titleLineRT.offsetMax = new Vector2(0f, 2f);

        var titleTMP = MakeTMP(titleBar.transform, "Title", "SAVED  GAMES", 22, true);
        Stretch(titleTMP.rectTransform);
        titleTMP.rectTransform.offsetMin = new Vector2(28f, 0f);
        titleTMP.rectTransform.offsetMax = new Vector2(-68f, 0f);
        titleTMP.alignment  = TextAlignmentOptions.MidlineLeft;
        titleTMP.color      = ColTextPrimary;
        titleTMP.outlineColor = ColOutlineText;
        titleTMP.outlineWidth = 0.25f;

        var closeGO  = MakeButton(titleBar.transform, "CloseBtn", "✕", 18, ColBtnClose);
        var closeRT  = closeGO.GetComponent<RectTransform>();
        closeRT.anchorMin        = new Vector2(1f, 0.5f);
        closeRT.anchorMax        = new Vector2(1f, 0.5f);
        closeRT.pivot            = new Vector2(1f, 0.5f);
        closeRT.sizeDelta        = new Vector2(56f, 52f);
        closeRT.anchoredPosition = new Vector2(-6f, 0f);
        closeGO.GetComponent<Button>().onClick.AddListener(Close);

        // ── slots container ───────────────────────────────────────────────
        var container   = new GameObject("Slots");
        container.transform.SetParent(panel.transform, false);
        var containerRT = container.AddComponent<RectTransform>();
        containerRT.anchorMin = Vector2.zero;
        containerRT.anchorMax = Vector2.one;
        containerRT.offsetMin = new Vector2(14f, SlotPadH);
        containerRT.offsetMax = new Vector2(-14f, -TitleBarH - 8f);

        var vlg = container.AddComponent<VerticalLayoutGroup>();
        vlg.spacing                = SlotSpacing;
        vlg.childForceExpandWidth  = true;
        vlg.childForceExpandHeight = true;
        vlg.childAlignment         = TextAnchor.UpperCenter;

        _rows.Clear();
        for (int i = 0; i < SaveSlotManager.SlotCount; i++)
            _rows.Add(BuildSlotRow(container.transform, i + 1));
    }

    /// <summary>
    /// Builds one save-slot row. A thin border wrapper (amber) wraps the dark-oak row
    /// to create a 1 px inset-border effect without requiring a sprite.
    /// </summary>
    private SlotRow BuildSlotRow(Transform parent, int slotNumber)
    {
        // ── amber border wrapper ──────────────────────────────────────────
        var wrapper    = MakeImage(parent, $"Slot{slotNumber}Frame", ColSlotBorder);
        var wrapperHLG = wrapper.AddComponent<HorizontalLayoutGroup>();
        wrapperHLG.padding                = new RectOffset(1, 1, 1, 1);
        wrapperHLG.childControlWidth      = true;
        wrapperHLG.childControlHeight     = true;
        wrapperHLG.childForceExpandWidth  = true;
        wrapperHLG.childForceExpandHeight = true;

        // ── dark oak row ──────────────────────────────────────────────────
        var row    = MakeImage(wrapper.transform, $"Slot{slotNumber}", ColSlotBg);
        var rowHLG = row.AddComponent<HorizontalLayoutGroup>();
        rowHLG.spacing                = 10;
        rowHLG.childControlWidth      = true;
        rowHLG.childControlHeight     = true;
        rowHLG.childForceExpandWidth  = false;
        rowHLG.childForceExpandHeight = true;
        rowHLG.padding                = new RectOffset(12, 12, 8, 8);
        rowHLG.childAlignment         = TextAnchor.MiddleLeft;

        // ── slot number badge ─────────────────────────────────────────────
        var badge  = MakeImage(row.transform, "Badge", ColBadge);
        badge.AddComponent<LayoutElement>().preferredWidth = BadgeW;
        var numTMP = MakeTMP(badge.transform, "Num", slotNumber.ToString(), 22, true);
        Stretch(numTMP.rectTransform);
        numTMP.alignment  = TextAlignmentOptions.Center;
        numTMP.color      = ColBadgeNum;
        numTMP.outlineColor = ColOutlineText;
        numTMP.outlineWidth = 0.22f;

        // ── info block ────────────────────────────────────────────────────
        var info   = new GameObject("Info");
        info.transform.SetParent(row.transform, false);
        info.AddComponent<RectTransform>();
        var infoLE           = info.AddComponent<LayoutElement>();
        infoLE.flexibleWidth = 1f;
        var infoVLG          = info.AddComponent<VerticalLayoutGroup>();
        infoVLG.childForceExpandWidth  = true;
        infoVLG.childForceExpandHeight = false;
        infoVLG.spacing               = 3f;
        infoVLG.padding               = new RectOffset(4, 0, 6, 6);
        infoVLG.childAlignment        = TextAnchor.MiddleLeft;

        var gameTypeTMP = MakeTMP(info.transform, "GameType", "— Empty —", 14, true);
        gameTypeTMP.color = ColTextEmpty;
        gameTypeTMP.gameObject.AddComponent<LayoutElement>().preferredHeight = 22f;

        var detailsTMP = MakeTMP(info.transform, "Details", string.Empty, 11, false);
        detailsTMP.color            = ColTextSub;
        detailsTMP.textWrappingMode = TextWrappingModes.Normal;
        detailsTMP.gameObject.AddComponent<LayoutElement>().preferredHeight = 30f;

        var dateTMP = MakeTMP(info.transform, "Date", string.Empty, 10, false);
        dateTMP.color     = ColTextDate;
        dateTMP.fontStyle = FontStyles.Italic;
        dateTMP.gameObject.AddComponent<LayoutElement>().preferredHeight = 16f;

        // ── buttons block (Load + Delete only) ────────────────────────────
        var btns  = new GameObject("Buttons");
        btns.transform.SetParent(row.transform, false);
        btns.AddComponent<RectTransform>();
        btns.AddComponent<LayoutElement>().preferredWidth = ButtonBlockW;
        var btnsVLG = btns.AddComponent<VerticalLayoutGroup>();
        btnsVLG.spacing                = 5f;
        btnsVLG.childForceExpandWidth  = true;
        btnsVLG.childForceExpandHeight = true;
        btnsVLG.padding                = new RectOffset(0, 0, 4, 4);

        int s = slotNumber;

        var loadGO   = MakeButton(btns.transform, "LoadBtn",   "Load",   12, ColBtnLoad);
        var deleteGO = MakeButton(btns.transform, "DeleteBtn", "Delete", 12, ColBtnDelete);

        loadGO.GetComponent<Button>().onClick.AddListener(() =>
        {
            _onLoad?.Invoke(s);
            Close();
        });

        deleteGO.GetComponent<Button>().onClick.AddListener(() =>
        {
            SaveSlotManager.DeleteSlot(s);
            Refresh();
        });

        return new SlotRow
        {
            GameTypeTMP = gameTypeTMP,
            DetailsTMP  = detailsTMP,
            DateTMP     = dateTMP,
            LoadBtn     = loadGO.GetComponent<Button>(),
            DeleteBtn   = deleteGO.GetComponent<Button>()
        };
    }

    // ── UGUI factory helpers ──────────────────────────────────────────────────

    private static GameObject MakeImage(Transform parent, string name, Color color)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.AddComponent<RectTransform>();
        go.AddComponent<Image>().color = color;
        return go;
    }

    private static TextMeshProUGUI MakeTMP(Transform parent, string name,
                                           string text, int size, bool bold)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.AddComponent<RectTransform>();
        var tmp              = go.AddComponent<TextMeshProUGUI>();
        tmp.text             = text;
        tmp.fontSize         = size;
        tmp.fontStyle        = bold ? FontStyles.Bold : FontStyles.Normal;
        tmp.color            = Color.white;
        tmp.overflowMode     = TextOverflowModes.Ellipsis;
        tmp.textWrappingMode = TextWrappingModes.NoWrap;
        if (RusticFont != null)
            tmp.font = RusticFont;
        return tmp;
    }

    private static GameObject MakeButton(Transform parent, string name,
                                         string label, int fontSize, Color bgColor)
    {
        var go  = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.AddComponent<RectTransform>();
        var img   = go.AddComponent<Image>();
        img.color = bgColor;

        var btn    = go.AddComponent<Button>();
        var colors = btn.colors;
        colors.normalColor      = Color.white;
        colors.highlightedColor = new Color(1.25f, 1.25f, 1.25f, 1f);
        colors.pressedColor     = new Color(0.60f, 0.60f, 0.60f, 1f);
        colors.disabledColor    = new Color(0.28f, 0.20f, 0.10f, 0.50f);
        colors.colorMultiplier  = 1f;
        btn.colors              = colors;

        var lbl = MakeTMP(go.transform, "Label", label, fontSize, true);
        Stretch(lbl.rectTransform);
        lbl.alignment = TextAlignmentOptions.Center;
        lbl.color     = ColTextPrimary;

        return go;
    }

    private static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
    }
}
