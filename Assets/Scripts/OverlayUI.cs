using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

// Add this component to any empty GameObject.
// It builds the entire HUD Canvas at runtime — no prefabs or manual Canvas setup required.
public class OverlayUI : MonoBehaviour
{
    public TurnManager  turnManager;
    public BoardVisual  boardVisual;
    public BoardManager boardManager;

    // ── built UI refs ─────────────────────────────────────────────────────────
    Text       bannerText;
    Text       apText;
    Text       infoText;
    Text       logContent;
    ScrollRect logScrollRect;
    Button     abilityBtn;
    Text       abilityBtnLabel;
    Button     endTurnBtn;
    Button     newGameBtn;

    readonly List<string> logLines = new();

    // ── colors ────────────────────────────────────────────────────────────────
    static readonly Color BgPanel   = new(0.10f, 0.10f, 0.14f, 0.92f);
    static readonly Color BgBanner  = new(0.14f, 0.14f, 0.20f, 1.00f);
    static readonly Color BtnNormal = new(0.22f, 0.22f, 0.30f, 1.00f);
    static readonly Color BtnAbil   = new(0.42f, 0.11f, 0.60f, 1.00f);
    static readonly Color BtnEnd    = new(0.60f, 0.18f, 0.18f, 1.00f);
    static readonly Color BtnNew    = new(0.12f, 0.35f, 0.18f, 1.00f);
    static readonly Color ColAI     = new(0.14f, 0.44f, 0.64f, 1.00f);
    static readonly Color ColOver   = new(0.20f, 0.18f, 0.10f, 1.00f);

    // ── lifecycle ─────────────────────────────────────────────────────────────

    void Start()
    {
        BuildCanvas();

        turnManager.OnLog          += AppendLog;
        turnManager.OnPhaseChanged += OnPhaseChanged;
        turnManager.OnAPChanged    += ap => UpdateAP(ap);
        turnManager.OnGameOver     += _ => { UpdateBanner(); };
        boardVisual.OnSelectionChanged += UpdateInfoPanel;

        UpdateBanner();
        UpdateAP(2);
        UpdateInfoPanel();
        AppendLog(string.Format(TurnManager.PlayerHeaderFmt, 1));
        AppendLog("Game begins. Your move.");
    }

    void Update()
    {
        // Keep ability button state in sync with selection
        bool canAbil = boardVisual.GetSelectedPiece() is { isDecoy: false } p
                    && p.player == 1 && p.abilityCd == 0 && !p.stunned
                    && turnManager.Phase == GamePhase.Player && turnManager.AP > 0;

        abilityBtn.interactable = canAbil || boardVisual.IsAbilityCasting;

        if (abilityBtnLabel != null)
            abilityBtnLabel.text = boardVisual.IsAbilityCasting ? "CANCEL ABILITY" : "USE ABILITY";

        var img = abilityBtn.GetComponent<Image>();
        if (img) img.color = boardVisual.IsAbilityCasting ? BtnAbil * 1.2f : BtnAbil;
    }

    // ── canvas construction ───────────────────────────────────────────────────

    void BuildCanvas()
    {
        if (FindObjectOfType<EventSystem>() == null)
        {
            var esGo = new GameObject("EventSystem");
            esGo.AddComponent<EventSystem>();
            esGo.AddComponent<StandaloneInputModule>();
        }

        var cvGo = new GameObject("HUD_Canvas");
        var cv   = cvGo.AddComponent<Canvas>();
        cv.renderMode = RenderMode.ScreenSpaceOverlay;
        cv.sortingOrder = 10;
        var scaler = cvGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight  = 0.5f;
        cvGo.AddComponent<GraphicRaycaster>();

        // Banner strip (top of screen, full width)
        var bannerGo = MakePanel(cvGo.transform, "Banner",
            new Vector2(0, 1), new Vector2(1, 1),
            new Vector2(0, -25), new Vector2(0, 25),
            BgBanner);
        bannerText = MakeText(bannerGo.transform, "BannerText", 18, TextAnchor.MiddleCenter, Color.white,
            Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

        // Right sidebar
        var sidebar = MakePanel(cvGo.transform, "Sidebar",
            new Vector2(1, 0), new Vector2(1, 1),
            new Vector2(-220, 0), new Vector2(0, 0),
            BgPanel);

        // AP label
        apText = MakeText(sidebar.transform, "APText", 14, TextAnchor.UpperLeft, Color.white,
            new Vector2(0, 1), new Vector2(1, 1),
            new Vector2(10, -50), new Vector2(-10, 0));

        // Info panel
        infoText = MakeText(sidebar.transform, "InfoText", 12, TextAnchor.UpperLeft, new Color(0.9f, 0.9f, 0.9f),
            new Vector2(0, 1), new Vector2(1, 1),
            new Vector2(10, -220), new Vector2(-10, -80));
        infoText.GetComponent<RectTransform>().GetComponent<Text>().text = "Tap a piece to inspect.";

        // Log (scrollable)
        BuildLogScroll(sidebar.transform);

        // Buttons
        abilityBtn      = MakeButton(sidebar.transform, "AbilityBtn",   "USE ABILITY", BtnAbil,  new Vector2(10,-320), out abilityBtnLabel);
        endTurnBtn      = MakeButton(sidebar.transform, "EndTurnBtn",   "END TURN",    BtnEnd,   new Vector2(10,-370), out _);
        newGameBtn      = MakeButton(sidebar.transform, "NewGameBtn",   "NEW GAME",    BtnNew,   new Vector2(10,-420), out _);

        abilityBtn.onClick.AddListener(() => boardVisual.OnAbilityButton());
        endTurnBtn.onClick.AddListener(() => { if (turnManager.Phase == GamePhase.Player) turnManager.EndPlayerTurn(); });
        newGameBtn.onClick.AddListener(() => { boardVisual.ResetSelection(); turnManager.ResetGame(); boardVisual.RefreshAll(); logLines.Clear(); });

        endTurnBtn.interactable = true;
    }

    // ── display ───────────────────────────────────────────────────────────────

    void OnPhaseChanged(GamePhase phase)
    {
        UpdateBanner();
        bool isPlayer = phase == GamePhase.Player;
        endTurnBtn.interactable  = isPlayer;
        endTurnBtn.GetComponent<Image>().color = isPlayer ? BtnEnd : BtnNormal;
    }

    void UpdateBanner()
    {
        switch (turnManager.Phase)
        {
            case GamePhase.Player:
                bannerText.text  = $"TURN {turnManager.TurnNumber} — YOUR TURN — {turnManager.AP} AP remaining";
                bannerText.color = Color.white;
                ParentImage(bannerText).color = BgBanner;
                break;
            case GamePhase.AI:
                bannerText.text  = $"TURN {turnManager.TurnNumber} — AI IS THINKING...";
                bannerText.color = Color.white;
                ParentImage(bannerText).color = ColAI;
                break;
            case GamePhase.Over:
                bannerText.text  = turnManager.Winner == 1 ? "⚑  PLAYER I VICTORIOUS" : "⚑  AI VICTORIOUS";
                bannerText.color = new Color(1f, 0.9f, 0.3f);
                ParentImage(bannerText).color = ColOver;
                break;
        }
    }

    static Image ParentImage(Text t) => t.transform.parent.GetComponent<Image>();

    void UpdateAP(int ap)
    {
        if (apText) apText.text = $"ACTION POINTS:  {ap} / 2\n" + (ap > 0 ? new string('●', ap) + new string('○', 2 - ap) : "○○");
        UpdateBanner();
    }

    void UpdateInfoPanel()
    {
        if (infoText == null) return;
        var piece = boardVisual.GetSelectedPiece();
        if (piece == null) { infoText.text = "Tap a piece to inspect."; return; }

        string status = "";
        if (piece.stunned)            status += "[STUNNED] ";
        if (piece.rooted)             status += "[ROOTED] ";
        if (piece.shielded)           status += "[BARRIER] ";
        if (piece.energyShieldActive) status += "[E.SHIELD] ";
        if (piece.weakened)           status += "[WEAKENED] ";
        if (piece.fortress)           status += "[FORTRESS] ";

        string cd = piece.isDecoy ? "" :
                    piece.abilityCd > 0 ? $"\nAbility CD: {piece.abilityCd} turn(s)" :
                    "\nAbility: READY";

        string charges = piece.key == "VOLTIX" ? $"\nCharges: {piece.staticCharges} / 3" : "";

        string passive = piece.key switch
        {
            "FLARE"     => "\nPassive: Scorch",
            "VOLTIX"    => "\nPassive: Static Charge",
            "ZEPHYROS"  => "\nPassive: Windborn (root immune)",
            "FROSTBITE" => "\nPassive: Ice Armor (phys immune)",
            "BULWARK"   => "\nPassive: Battle Hardened (phys immune)",
            "AEGIS"     => "\nPassive: Energy Shield",
            "VERDANT"   => "\nPassive: Life Bloom",
            "MIMIC"     => "\nPassive: Eerie Aura",
            "SHARDIS"   => "\nPassive: Phased Form (phys immune)",
            _           => ""
        };

        infoText.text =
            $"<b>{piece.pieceName}</b>  [{piece.cls}]  P{piece.player}\n" +
            $"Shards: {piece.shards} / {piece.maxShards}  |  Move: {piece.move}\n" +
            (status.Length > 0 ? status + "\n" : "") +
            (piece.isDecoy ? "" : $"\n{piece.ability.name}\n<size=10>{piece.ability.desc}</size>") +
            cd + charges + passive;
    }

    void AppendLog(string msg)
    {
        // TEMP diagnostic: mirrors every log line to the Unity Console so we can
        // tell whether log events fire at all (remove once the log bug is solved)
        Debug.Log($"[BattleLog] {msg}");

        logLines.Add(msg);
        // Cap history: enough for many turns, but safely under the Text mesh limit
        while (logLines.Count > 150) logLines.RemoveAt(0);
        if (logContent == null) return;

        // Only stick to the bottom if the reader is already there, so scrolling
        // up to review earlier turns doesn't get yanked back down by new lines
        bool wasAtBottom = logScrollRect.verticalNormalizedPosition <= 0.02f
                        || logContent.preferredHeight <= logScrollRect.viewport.rect.height;

        logContent.text = string.Join("\n", logLines);
        var rt = (RectTransform)logContent.transform;
        rt.sizeDelta = new Vector2(rt.sizeDelta.x, logContent.preferredHeight);

        if (wasAtBottom)
        {
            Canvas.ForceUpdateCanvases(); // apply the new height before pinning
            logScrollRect.verticalNormalizedPosition = 0f;
        }
    }

    // ── log scroll ────────────────────────────────────────────────────────────

    void BuildLogScroll(Transform parent)
    {
        var scrollGo = new GameObject("LogScroll", typeof(RectTransform), typeof(Image), typeof(ScrollRect));
        scrollGo.transform.SetParent(parent, false);
        var scrollRt = scrollGo.GetComponent<RectTransform>();
        scrollRt.anchorMin = new Vector2(0, 0);
        scrollRt.anchorMax = new Vector2(1, 0);
        scrollRt.offsetMin = new Vector2(10,  10);
        scrollRt.offsetMax = new Vector2(-10, 400); // taller panel — more turns visible at once
        scrollGo.GetComponent<Image>().color = new Color(0.06f, 0.06f, 0.10f, 0.85f);

        logScrollRect = scrollGo.GetComponent<ScrollRect>();
        logScrollRect.horizontal        = false;
        logScrollRect.vertical          = true;
        logScrollRect.scrollSensitivity = 25f;
        logScrollRect.movementType      = ScrollRect.MovementType.Clamped;

        var vpGo = new GameObject("Viewport", typeof(RectTransform), typeof(Image), typeof(Mask));
        vpGo.transform.SetParent(scrollGo.transform, false);
        var vpRt = vpGo.GetComponent<RectTransform>();
        vpRt.anchorMin = Vector2.zero;
        vpRt.anchorMax = Vector2.one;
        vpRt.offsetMin = new Vector2(4, 4);
        vpRt.offsetMax = new Vector2(-4, -4);
        vpGo.GetComponent<Image>().color = Color.clear;
        vpGo.GetComponent<Mask>().showMaskGraphic = false;

        var contentGo = new GameObject("Content",
            typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
        contentGo.transform.SetParent(vpGo.transform, false);
        var contentRt = contentGo.GetComponent<RectTransform>();
        contentRt.anchorMin = new Vector2(0, 1);
        contentRt.anchorMax = new Vector2(1, 1);
        contentRt.pivot     = new Vector2(0.5f, 1f);
        contentRt.offsetMin = Vector2.zero;
        contentRt.offsetMax = Vector2.zero;
        contentRt.sizeDelta = Vector2.zero;

        logContent = contentGo.GetComponent<Text>();
        logContent.font               = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        logContent.fontSize           = 11;
        logContent.alignment          = TextAnchor.UpperLeft;
        logContent.color              = new Color(0.75f, 0.75f, 0.75f);
        logContent.verticalOverflow   = VerticalWrapMode.Overflow;
        logContent.horizontalOverflow = HorizontalWrapMode.Wrap;
        logContent.supportRichText    = true;

        logScrollRect.viewport = vpRt;
        logScrollRect.content  = contentRt;
    }

    // ── UI factory helpers ────────────────────────────────────────────────────

    static GameObject MakePanel(Transform parent, string name,
        Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax, Color col)
    {
        var go  = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        go.transform.SetParent(parent, false);
        var rt  = go.GetComponent<RectTransform>();
        rt.anchorMin  = anchorMin;
        rt.anchorMax  = anchorMax;
        rt.offsetMin  = offsetMin;
        rt.offsetMax  = offsetMax;
        go.GetComponent<Image>().color = col;
        return go;
    }

    static Text MakeText(Transform parent, string name, int fontSize, TextAnchor anchor, Color col,
        Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
    {
        var go   = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
        go.transform.SetParent(parent, false);
        var rt   = go.GetComponent<RectTransform>();
        rt.anchorMin = anchorMin; rt.anchorMax = anchorMax;
        rt.offsetMin = offsetMin; rt.offsetMax = offsetMax;
        var txt  = go.GetComponent<Text>();
        txt.font      = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        txt.fontSize  = fontSize;
        txt.alignment = anchor;
        txt.color     = col;
        txt.supportRichText = true;
        return txt;
    }

    static Button MakeButton(Transform parent, string name, string label, Color col,
        Vector2 anchoredPos, out Text labelText)
    {
        var go  = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        var rt  = go.GetComponent<RectTransform>();
        rt.anchorMin       = new Vector2(0, 1);
        rt.anchorMax       = new Vector2(1, 1);
        rt.anchoredPosition = anchoredPos;
        rt.sizeDelta       = new Vector2(-20, 36);
        go.GetComponent<Image>().color = col;

        var lblGo = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
        lblGo.transform.SetParent(go.transform, false);
        var lrt = lblGo.GetComponent<RectTransform>();
        lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one;
        lrt.offsetMin = Vector2.zero; lrt.offsetMax = Vector2.zero;
        var txt  = lblGo.GetComponent<Text>();
        txt.font      = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        txt.fontSize  = 13;
        txt.alignment = TextAnchor.MiddleCenter;
        txt.color     = Color.white;
        txt.text      = label;
        txt.fontStyle = FontStyle.Bold;
        labelText = txt;

        var btn = go.GetComponent<Button>();
        var colors = btn.colors;
        colors.normalColor      = col;
        colors.highlightedColor = col * 1.2f;
        colors.pressedColor     = col * 0.8f;
        btn.colors = colors;
        return btn;
    }
}
