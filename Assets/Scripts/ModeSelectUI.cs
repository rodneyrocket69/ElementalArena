using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Full-screen mode picker shown at startup and again from the NEW GAME button.
// Built entirely at runtime like OverlayUI — OverlayUI adds this component
// automatically, so no scene setup is needed. Picking a mode sets
// GameModeState.Current and resets the game into that mode.
public class ModeSelectUI : MonoBehaviour
{
    public TurnManager turnManager;
    public BoardVisual boardVisual;

    GameObject panel;

    static readonly Color Dim        = new(0.02f, 0.02f, 0.05f, 0.86f);
    static readonly Color CardCol    = new(0.13f, 0.13f, 0.19f, 1.00f);
    static readonly Color ClassicCol = new(0.60f, 0.18f, 0.18f, 1.00f);
    static readonly Color ArenaCol   = new(0.85f, 0.58f, 0.10f, 1.00f);

    void Start()
    {
        BuildPanel();
        Show();
    }

    public void Show() => panel.SetActive(true);

    void Choose(GameMode mode)
    {
        GameModeState.Current = mode;
        boardVisual.ResetSelection();
        turnManager.ResetGame();
        boardVisual.RefreshAll();
        panel.SetActive(false);
    }

    void BuildPanel()
    {
        var cvGo = new GameObject("ModeSelect_Canvas");
        var cv   = cvGo.AddComponent<Canvas>();
        cv.renderMode   = RenderMode.ScreenSpaceOverlay;
        cv.sortingOrder = 20; // above the HUD canvas (10)
        cv.pixelPerfect = true; // whole-pixel snapping — see OverlayUI.BuildCanvas
        var scaler = cvGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight  = 0.5f;
        cvGo.AddComponent<GraphicRaycaster>();

        // Full-screen dim layer — also swallows clicks so the board underneath is inert
        panel = new GameObject("ModeSelectPanel", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        panel.transform.SetParent(cvGo.transform, false);
        var prt = panel.GetComponent<RectTransform>();
        prt.anchorMin = Vector2.zero; prt.anchorMax = Vector2.one;
        prt.offsetMin = Vector2.zero; prt.offsetMax = Vector2.zero;
        panel.GetComponent<Image>().color = Dim;

        // Center card
        var card = new GameObject("Card", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        card.transform.SetParent(panel.transform, false);
        var crt = card.GetComponent<RectTransform>();
        crt.anchorMin = crt.anchorMax = new Vector2(0.5f, 0.5f);
        crt.sizeDelta = new Vector2(560, 420);
        card.GetComponent<Image>().color = CardCol;

        MakeText(card.transform, "ELEMENTAL ARENA", 30, FontStyles.Bold, new Color(1f, 0.9f, 0.5f),
            new Vector2(0, 145), new Vector2(520, 44));
        MakeText(card.transform, "Choose a game mode", 15, FontStyles.Normal, new Color(0.8f, 0.8f, 0.8f),
            new Vector2(0, 108), new Vector2(520, 26));

        MakeModeButton(card.transform, "CLASSIC",
            "Chess-style ability battle.\nDestroy every enemy piece to win.",
            ClassicCol, new Vector2(0, 25), () => Choose(GameMode.Classic));

        MakeModeButton(card.transform, "ARENA BALL",
            $"Grab the ball and carry it into the enemy goal — first to {ArenaConfig.GoalsToWin} goals wins.\n" +
            "Corner towers guard the outer goal tiles and fire back. Fallen pieces respawn.",
            ArenaCol, new Vector2(0, -105), () => Choose(GameMode.Arena));

        panel.SetActive(false);
    }

    static void MakeModeButton(Transform parent, string title, string desc, Color col,
        Vector2 pos, UnityEngine.Events.UnityAction onClick)
    {
        var go = new GameObject($"Btn_{title}", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = new Vector2(480, 110);
        go.GetComponent<Image>().color = col;

        var btn = go.GetComponent<Button>();
        var colors = btn.colors;
        colors.normalColor      = col;
        colors.highlightedColor = col * 1.2f;
        colors.pressedColor     = col * 0.8f;
        btn.colors = colors;
        btn.onClick.AddListener(onClick);

        MakeText(go.transform, title, 20, FontStyles.Bold, Color.white,
            new Vector2(0, 30), new Vector2(450, 30));
        MakeText(go.transform, desc, 12, FontStyles.Normal, new Color(0.95f, 0.95f, 0.95f),
            new Vector2(0, -22), new Vector2(450, 60));
    }

    static TMP_Text MakeText(Transform parent, string content, int size, FontStyles style, Color col,
        Vector2 pos, Vector2 sizeDelta)
    {
        var go = new GameObject("Text", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = sizeDelta;
        var txt = go.AddComponent<TextMeshProUGUI>();
        txt.fontSize  = size;
        txt.fontStyle = style;
        txt.alignment = TextAlignmentOptions.Center;
        txt.color     = col;
        txt.text      = content;
        return txt;
    }
}
