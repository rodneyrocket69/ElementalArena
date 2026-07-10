using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

[RequireComponent(typeof(BoardManager))]
public class BoardVisual : MonoBehaviour
{
    [Header("Managers")]
    public TurnManager turnManager;

    [Header("Layout")]
    public float tileSize    = 1.1f;
    public float tileThick   = 0.12f;
    public float pieceRadius = 0.55f;  // cylinder half-height above tile surface

    [Header("Drag & Drop")]
    public float liftHeight = 1.0f;    // how far a carried piece floats above the board

    // ── tile colors ───────────────────────────────────────────────────────────
    static readonly Color TileLight = new(0.91f, 0.88f, 0.81f);
    static readonly Color TileDark  = new(0.83f, 0.78f, 0.69f);
    static readonly Color SelCol    = new(1.00f, 0.88f, 0.40f);
    static readonly Color MoveCol   = new(0.55f, 0.85f, 0.55f);
    static readonly Color HoverOkCol  = new(0.85f, 1.00f, 0.55f); // legal drop tile under carried piece
    static readonly Color HoverBadCol = new(0.90f, 0.55f, 0.45f); // illegal drop tile under carried piece
    static readonly Color AtkCol    = new(0.95f, 0.40f, 0.40f);
    static readonly Color AimHoverCol = new(1.00f, 0.25f, 0.18f); // aimed-at enemy tile
    static readonly Color CastCol   = new(0.72f, 0.50f, 0.95f);
    static readonly Color AoeCol    = new(0.55f, 0.30f, 0.85f);
    static readonly Color P1Color   = new(0.80f, 0.20f, 0.15f);
    static readonly Color P2Color   = new(0.15f, 0.40f, 0.70f);
    static readonly Color DecoyCol  = new(0.85f, 0.44f, 0.84f);

    // ── status tint colors ────────────────────────────────────────────────────
    static readonly Color StunTint    = new(1.00f, 0.90f, 0.10f);
    static readonly Color RootTint    = new(0.20f, 0.80f, 0.20f);
    static readonly Color ShieldTint  = new(0.60f, 0.30f, 0.90f);
    static readonly Color WeakenTint  = new(1.00f, 0.50f, 0.10f);
    static readonly Color FortressTint= new(0.55f, 0.55f, 0.55f);

    // ── scene objects ─────────────────────────────────────────────────────────
    BoardManager board;
    GameObject[,] tiles;
    readonly Dictionary<Piece, PieceView> pieceViews = new();
    GameObject pieceParent;

    // ── drag state ────────────────────────────────────────────────────────────
    PieceView    dragView;    // non-null while carrying a piece
    int[]        dragOrigin;  // tile the carried piece came from
    (int r, int c)? hoverTile;  // tile currently under the carried piece

    // ── attack aim state (hold right-click on your piece, drag onto an enemy) ──
    int[]                aimOrigin;      // attacking piece, non-null while aiming
    List<(int r, int c)> aimAttacks = new();
    (int r, int c)?      aimHoverTile;
    GameObject           aimBeam;        // line from attacker toward the cursor/target
    PieceView            aimTargetView;  // enemy scaled up while aimed at

    // ── interaction state ─────────────────────────────────────────────────────
    enum AbilPhase { None, Cast }
    AbilPhase abilPhase;

    int[]               selected;
    int[]               inspectedCell; // display only — can be any player's piece
    List<(int r, int c)> validMoves   = new();
    List<(int r, int c)> validAttacks = new();
    List<(int r, int c)> castTargets  = new();

    // read by OverlayUI
    public Piece GetSelectedPiece() =>
        inspectedCell != null ? board.Board[inspectedCell[0], inspectedCell[1]] :
        selected      != null ? board.Board[selected[0],      selected[1]]      : null;
    public bool  IsAbilityCasting  => abilPhase == AbilPhase.Cast;

    public event Action OnSelectionChanged;

    // ── lifecycle ─────────────────────────────────────────────────────────────

    void Awake() => board = GetComponent<BoardManager>();

    void Start()
    {
        tiles = new GameObject[BoardManager.BS, BoardManager.BS];

        turnManager.OnPhaseChanged += OnPhaseChanged;

        // Effects player for attacks/abilities — added here so no scene setup is needed
        if (GetComponent<AbilityVFX>() == null) gameObject.AddComponent<AbilityVFX>();

        BuildTiles();
        RefreshAll();
    }

    void Update()
    {
        if (turnManager.Phase != GamePhase.Player)
        {
            if (dragView  != null) CancelDrag();
            if (aimOrigin != null) CancelAim();
            return;
        }

        if (dragView  != null) { UpdateDrag(); return; }
        if (aimOrigin != null) { UpdateAim();  return; }

        if      (Input.GetMouseButtonDown(0)) HandleMouseDown();
        else if (Input.GetMouseButtonDown(1)) HandleRightMouseDown();
    }

    void HandleMouseDown()
    {
        // Ignore clicks that land on the HUD (buttons, sidebar)
        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;

        var ray = Camera.main.ScreenPointToRay(Input.mousePosition);
        if (!Physics.Raycast(ray, out var hit)) return;

        string name = hit.collider.gameObject.name;
        if (!name.StartsWith("Tile_") && !name.StartsWith("Piece_")) return;

        // These objects encode row/col in name as _R_C (first two numeric parts)
        var parts = name.Split('_');
        if (parts.Length < 3 || !int.TryParse(parts[1], out int r) || !int.TryParse(parts[2], out int c)) return;

        bool wasCasting = abilPhase == AbilPhase.Cast;
        OnCellClick(r, c);

        // If that click selected one of our movable pieces, pick it up
        if (!wasCasting && abilPhase == AbilPhase.None &&
            turnManager.Phase == GamePhase.Player && turnManager.AP > 0 &&
            selected != null && selected[0] == r && selected[1] == c &&
            validMoves.Count > 0)
        {
            var piece = board.Board[r, c];
            if (piece != null && piece.player == 1 && pieceViews.TryGetValue(piece, out var view))
                BeginDrag(r, c, view);
        }
    }

    // Right-click: inspect any piece; hold on your own piece to aim an attack
    void HandleRightMouseDown()
    {
        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;

        // Right-click also backs out of ability targeting
        if (abilPhase == AbilPhase.Cast) { CancelAbility(); return; }

        var ray = Camera.main.ScreenPointToRay(Input.mousePosition);
        if (!Physics.Raycast(ray, out var hit)) return;

        string name = hit.collider.gameObject.name;
        if (!name.StartsWith("Tile_") && !name.StartsWith("Piece_")) return;
        var parts = name.Split('_');
        if (parts.Length < 3 || !int.TryParse(parts[1], out int r) || !int.TryParse(parts[2], out int c)) return;

        var piece = board.Board[r, c];
        if (piece == null) return;

        // Show the info panel without selecting or lifting the piece
        inspectedCell = new[] { r, c };
        OnSelectionChanged?.Invoke();

        // Holding on one of our pieces starts attack aiming
        if (piece.player == 1 && !piece.isDecoy && turnManager.AP > 0)
        {
            aimOrigin    = new[] { r, c };
            aimAttacks   = board.GetAttacks(r, c);
            aimHoverTile = null;
            RefreshTileColors();
        }
    }

    // ── attack aiming (right-click drag) ──────────────────────────────────────

    void UpdateAim()
    {
        if (!Input.GetMouseButton(1)) { EndAim(); return; }

        var ray = Camera.main.ScreenPointToRay(Input.mousePosition);

        var tile = TileUnderCursor(ray);
        if (!Nullable.Equals(tile, aimHoverTile))
        {
            aimHoverTile = tile;
            UpdateAimHighlight();
            RefreshTileColors();
        }

        UpdateAimBeam(ray);
    }

    // Scale the aimed-at enemy up a little so the target is unmistakable
    void UpdateAimHighlight()
    {
        if (aimTargetView != null)
        {
            aimTargetView.transform.GetChild(0).localScale = Vector3.one;
            aimTargetView = null;
        }
        if (aimHoverTile.HasValue && aimAttacks.Contains(aimHoverTile.Value))
        {
            var t = board.Board[aimHoverTile.Value.r, aimHoverTile.Value.c];
            if (t != null && pieceViews.TryGetValue(t, out var view))
            {
                aimTargetView = view;
                view.transform.GetChild(0).localScale = Vector3.one * 1.18f;
            }
        }
    }

    // Line from the attacker to the cursor: thin + faint while searching,
    // thick + red when locked onto a legal target
    void UpdateAimBeam(Ray ray)
    {
        bool valid = aimHoverTile.HasValue && aimAttacks.Contains(aimHoverTile.Value);

        Vector3 from = WorldPos(aimOrigin[0], aimOrigin[1], 0.5f);
        Vector3 to;
        if (valid)
        {
            to = WorldPos(aimHoverTile.Value.r, aimHoverTile.Value.c, 0.5f);
        }
        else
        {
            var plane = new Plane(Vector3.up, new Vector3(0, 0.3f, 0));
            to = plane.Raycast(ray, out float d) ? ray.GetPoint(d) : from;
        }

        if (aimBeam == null)
        {
            aimBeam = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            aimBeam.name = "AimBeam";
            Destroy(aimBeam.GetComponent<Collider>());
        }

        float len = Vector3.Distance(from, to);
        if (len < 0.05f) { aimBeam.SetActive(false); return; }

        aimBeam.SetActive(true);
        float w = valid ? 0.10f : 0.045f;
        aimBeam.transform.position   = (from + to) * 0.5f;
        aimBeam.transform.rotation   = Quaternion.FromToRotation(Vector3.up, (to - from).normalized);
        aimBeam.transform.localScale = new Vector3(w, len * 0.5f, w);
        aimBeam.GetComponent<Renderer>().material.color =
            valid ? AimHoverCol : new Color(0.75f, 0.55f, 0.50f);
    }

    void EndAim()
    {
        var origin = aimOrigin;
        var target = aimHoverTile;
        bool valid = target.HasValue && aimAttacks.Contains(target.Value);
        CancelAim();

        if (valid && turnManager.AP > 0 &&
            board.TryAttack(origin[0], origin[1], target.Value.r, target.Value.c))
        {
            turnManager.ConsumeAP();
            if (turnManager.Phase == GamePhase.Player && selected != null)
            {
                // the attack may have changed what the selected piece can do
                var sp = board.Board[selected[0], selected[1]];
                if (sp != null)
                {
                    validMoves   = board.GetMoves(selected[0], selected[1]);
                    validAttacks = board.GetAttacks(selected[0], selected[1]);
                }
                else ResetSelection();
            }
            RefreshAll();
            OnSelectionChanged?.Invoke();
        }
    }

    void CancelAim()
    {
        if (aimTargetView != null)
        {
            aimTargetView.transform.GetChild(0).localScale = Vector3.one;
            aimTargetView = null;
        }
        if (aimBeam != null) { Destroy(aimBeam); aimBeam = null; }
        aimOrigin = null;
        aimAttacks.Clear();
        aimHoverTile = null;
        RefreshTileColors();
    }

    // ── drag & drop ───────────────────────────────────────────────────────────

    void BeginDrag(int r, int c, PieceView view)
    {
        dragView   = view;
        dragOrigin = new[] { r, c };
        hoverTile  = null;

        view.carried   = true;
        view.targetPos = WorldPos(r, c, liftHeight);
        SetViewLayer(view, 2); // built-in "Ignore Raycast" layer — can't block its own raycasts
        RefreshTileColors();
    }

    void UpdateDrag()
    {
        if (!Input.GetMouseButton(0)) { EndDrag(); return; }

        var ray = Camera.main.ScreenPointToRay(Input.mousePosition);

        // Carried piece chases the cursor's position on a plane at lift height
        var plane = new Plane(Vector3.up, new Vector3(0, liftHeight, 0));
        if (plane.Raycast(ray, out float dist))
            dragView.targetPos = ray.GetPoint(dist);

        var tile = TileUnderCursor(ray);
        if (!Nullable.Equals(tile, hoverTile))
        {
            hoverTile = tile;
            RefreshTileColors();
        }
    }

    void EndDrag()
    {
        var view   = dragView;
        var origin = dragOrigin;
        var drop   = hoverTile;

        dragView = null; dragOrigin = null; hoverTile = null;
        view.carried = false;
        SetViewLayer(view, 0);

        bool overUI = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();

        if (!overUI && drop.HasValue && validMoves.Contains(drop.Value) && turnManager.AP > 0 &&
            board.TryMove(origin[0], origin[1], drop.Value.r, drop.Value.c))
        {
            turnManager.ConsumeAP();
            if (turnManager.Phase == GamePhase.Player)
            {
                selected      = new[] { drop.Value.r, drop.Value.c };
                inspectedCell = new[] { drop.Value.r, drop.Value.c };
                validMoves    = board.GetMoves(drop.Value.r, drop.Value.c);
                validAttacks  = board.GetAttacks(drop.Value.r, drop.Value.c);
            }
            RefreshAll();
            OnSelectionChanged?.Invoke();
        }
        else
        {
            // No legal drop — float back home; piece stays selected
            view.targetPos = WorldPos(origin[0], origin[1], 0);
            RefreshTileColors();
        }
    }

    void CancelDrag()
    {
        if (dragView == null) return;
        dragView.carried   = false;
        dragView.targetPos = WorldPos(dragOrigin[0], dragOrigin[1], 0);
        SetViewLayer(dragView, 0);
        dragView = null; dragOrigin = null; hoverTile = null;
        RefreshTileColors();
    }

    // Finds the board tile under the cursor, looking past pieces
    (int r, int c)? TileUnderCursor(Ray ray)
    {
        (int r, int c)? found = null;
        float best = float.MaxValue;
        foreach (var h in Physics.RaycastAll(ray, 200f))
        {
            string n = h.collider.gameObject.name;
            if (!n.StartsWith("Tile_") || h.distance >= best) continue;
            var parts = n.Split('_');
            if (int.TryParse(parts[1], out int r) && int.TryParse(parts[2], out int c))
            {
                best  = h.distance;
                found = (r, c);
            }
        }
        return found;
    }

    static void SetViewLayer(PieceView view, int layer)
    {
        foreach (var t in view.GetComponentsInChildren<Transform>(true))
            t.gameObject.layer = layer;
    }

    // ── tile construction ─────────────────────────────────────────────────────

    void BuildTiles()
    {
        var tileParent = new GameObject("Tiles");

        for (int r = 0; r < BoardManager.BS; r++)
        for (int c = 0; c < BoardManager.BS; c++)
        {
            var tile = GameObject.CreatePrimitive(PrimitiveType.Cube);
            tile.name = $"Tile_{r}_{c}";
            tile.transform.SetParent(tileParent.transform);
            tile.transform.localScale    = new Vector3(tileSize - 0.04f, tileThick, tileSize - 0.04f);
            tile.transform.localPosition = WorldPos(r, c, 0);
            SetColor(tile, BaseTileColor(r, c));
            tiles[r, c] = tile;
        }
    }

    // ── input ─────────────────────────────────────────────────────────────────

    void OnCellClick(int row, int col)
    {
        var piece = board.Board[row, col];

        // Cast phase: click a valid target to fire immediately
        if (abilPhase == AbilPhase.Cast)
        {
            if (castTargets.Contains((row, col)))
                ExecuteAbility(row, col);
            else
                CancelAbility();
            return;
        }

        // Attack
        if (selected != null && validAttacks.Contains((row, col)) && turnManager.AP > 0)
        {
            if (board.TryAttack(selected[0], selected[1], row, col))
            {
                turnManager.ConsumeAP();
                if (turnManager.Phase == GamePhase.Player)
                    validAttacks = board.GetAttacks(selected[0], selected[1]);
                RefreshAll();
                OnSelectionChanged?.Invoke();
            }
            return;
        }

        // (Moving is now done by dragging the piece — see BeginDrag/EndDrag)

        // Select any friendly piece (including decoys — Mimic's phantom can be moved)
        if (piece != null && piece.player == 1)
        {
            selected      = new[] { row, col };
            inspectedCell = new[] { row, col };
            validMoves    = board.GetMoves(row, col);
            validAttacks  = piece.isDecoy ? new List<(int,int)>() : board.GetAttacks(row, col);
            CancelAbility();
            RefreshTileColors();
            OnSelectionChanged?.Invoke();
            return;
        }

        // Inspect enemy piece without selecting for action
        if (piece != null)
        {
            inspectedCell = new[] { row, col };
            selected      = null;
            validMoves.Clear(); validAttacks.Clear();
            CancelAbility();
            RefreshTileColors();
            OnSelectionChanged?.Invoke();
            return;
        }

        // Deselect (empty cell)
        ResetSelection();
        OnSelectionChanged?.Invoke();
    }

    // Called by OverlayUI ability button
    public void OnAbilityButton()
    {
        if (selected == null || turnManager.Phase != GamePhase.Player || turnManager.AP <= 0) return;
        var piece = board.Board[selected[0], selected[1]];
        if (piece == null || piece.isDecoy || piece.abilityCd > 0 || piece.stunned) return;

        if (abilPhase == AbilPhase.Cast) { CancelAbility(); return; }

        var ct = board.GetCastTargets(selected[0], selected[1]);
        if (ct.Count == 0) { return; }

        var ab = piece.ability;
        // Self-cast abilities fire immediately; targeted abilities enter cast phase
        if (ab.type == AbilityType.Shockwave || ab.type == AbilityType.Fortress ||
            ab.type == AbilityType.Pull       || ab.type == AbilityType.Heal)
        {
            ExecuteAbility(selected[0], selected[1]);
        }
        else
        {
            castTargets = ct;
            abilPhase   = AbilPhase.Cast;
            RefreshTileColors();
            OnSelectionChanged?.Invoke();
        }
    }

    void ExecuteAbility(int tr, int tc)
    {
        int[] sel = selected;
        if (board.TryAbility(sel[0], sel[1], tr, tc))
        {
            turnManager.ConsumeAP();
            if (turnManager.Phase == GamePhase.Player)
            {
                abilPhase = AbilPhase.None;
                castTargets.Clear();
                validMoves   = board.GetMoves(sel[0], sel[1]);
                validAttacks = board.GetAttacks(sel[0], sel[1]);
            }
            RefreshAll();
            OnSelectionChanged?.Invoke();
        }
    }

    void CancelAbility()
    {
        abilPhase = AbilPhase.None;
        castTargets.Clear();
        RefreshTileColors();
        OnSelectionChanged?.Invoke();
    }

    public void ResetSelection()
    {
        selected      = null;
        inspectedCell = null;
        validMoves.Clear(); validAttacks.Clear();
        CancelAbility();
    }

    // ── visual refresh ────────────────────────────────────────────────────────

    public void RefreshAll()
    {
        RefreshTileColors();
        RefreshPieces();
    }

    void RefreshTileColors()
    {
        for (int r = 0; r < BoardManager.BS; r++)
        for (int c = 0; c < BoardManager.BS; c++)
        {
            bool isSel   = selected != null && selected[0] == r && selected[1] == c;
            bool isMove  = validMoves.Contains((r, c));
            bool isAtk   = validAttacks.Contains((r, c));
            bool isCast  = castTargets.Contains((r, c));
            bool isHover = dragView != null && hoverTile.HasValue && hoverTile.Value.r == r && hoverTile.Value.c == c;

            bool isAim       = aimOrigin != null && aimAttacks.Contains((r, c));
            bool isAimOrigin = aimOrigin != null && aimOrigin[0] == r && aimOrigin[1] == c;
            bool isAimHover  = isAim && aimHoverTile.HasValue && aimHoverTile.Value.r == r && aimHoverTile.Value.c == c;

            Color col;
            if (isHover)
            {
                bool isOrigin = dragOrigin != null && dragOrigin[0] == r && dragOrigin[1] == c;
                col = isMove ? HoverOkCol : isOrigin ? SelCol : HoverBadCol;
            }
            else if (isAimHover)  col = AimHoverCol;
            else if (isAimOrigin) col = SelCol;
            else if (isAim)       col = AtkCol;
            else if (isSel)  col = SelCol;
            else if (isCast) col = CastCol;
            else if (isAtk)  col = AtkCol;
            else if (isMove) col = MoveCol;
            else
            {
                // Status effect tile tint for occupied tiles
                col = BaseTileColor(r, c);
                var p = board.Board[r, c];
                if (p != null)
                {
                    if (p.stunned)
                        col = Color.Lerp(col, StunTint, 0.45f);
                    else if (p.rooted)
                        col = Color.Lerp(col, RootTint, 0.40f);
                    else if (p.shielded || p.energyShieldActive)
                        col = Color.Lerp(col, ShieldTint, 0.38f);
                    else if (p.weakened)
                        col = Color.Lerp(col, WeakenTint, 0.38f);
                    else if (p.fortress)
                        col = Color.Lerp(col, FortressTint, 0.40f);
                }
            }

            SetColor(tiles[r, c], col);
        }
    }

    void RefreshPieces()
    {
        if (pieceParent == null) pieceParent = new GameObject("Pieces");

        var seen = new HashSet<Piece>();

        for (int r = 0; r < BoardManager.BS; r++)
        for (int c = 0; c < BoardManager.BS; c++)
        {
            var piece = board.Board[r, c];
            if (piece == null) continue;
            seen.Add(piece);

            if (!pieceViews.TryGetValue(piece, out var view))
            {
                view = CreateView(piece);
                view.SnapTo(WorldPos(r, c, 0));
                pieceViews[piece] = view;
            }

            // Body (child 0) carries the row/col name used by click raycasts
            var body = view.transform.GetChild(0).gameObject;
            body.name = $"Piece_{r}_{c}";

            // Tint every shape part that makes up the body
            Color col = PieceColor(piece);
            foreach (var rend in body.GetComponentsInChildren<Renderer>())
                rend.material.color = col;

            // Glide to the current board position — unless the player is carrying it
            if (!view.carried) view.targetPos = WorldPos(r, c, 0);

            RefreshPips(view, piece);
        }

        // Remove views whose piece is no longer on the board (with a shrink-out)
        var gone = new List<Piece>();
        foreach (var kv in pieceViews) if (!seen.Contains(kv.Key)) gone.Add(kv.Key);
        foreach (var p in gone)
        {
            var view = pieceViews[p];
            pieceViews.Remove(p);
            if (view != null) StartCoroutine(ShrinkAway(view.gameObject));
        }
    }

    // Dead pieces shrink to nothing instead of vanishing instantly
    IEnumerator ShrinkAway(GameObject go)
    {
        foreach (var col in go.GetComponentsInChildren<Collider>()) col.enabled = false;
        Vector3 start = go.transform.localScale;
        float t = 0f, dur = 0.25f;
        while (t < dur)
        {
            t += Time.deltaTime;
            if (go == null) yield break;
            go.transform.localScale = start * Mathf.Max(0f, 1f - t / dur);
            yield return null;
        }
        Destroy(go);
    }

    // Builds a piece: root (PieceView, glides/tilts) → body (click collider + shape
    // parts) + shard pips. Pips are children of the root, so they follow the piece.
    PieceView CreateView(Piece piece)
    {
        var root = new GameObject($"PieceRoot_{piece.id}");
        root.transform.SetParent(pieceParent.transform);
        var view = root.AddComponent<PieceView>();

        // Body: empty holder with one box collider for clicks; the visible shape
        // is built from collider-less primitives underneath it
        var bodyGo = new GameObject("Body");
        bodyGo.transform.SetParent(root.transform, false);
        bodyGo.transform.localPosition = new Vector3(0, pieceRadius, 0);
        var box  = bodyGo.AddComponent<BoxCollider>();
        box.size = new Vector3(0.58f, 0.65f, 0.58f);

        BuildShape(bodyGo.transform, piece);

        if (!piece.isDecoy && piece.maxShards > 0)
        {
            float totalW = (piece.maxShards - 1) * 0.16f;
            for (int i = 0; i < piece.maxShards; i++)
            {
                var pip = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                pip.name = "Pip";
                Destroy(pip.GetComponent<SphereCollider>());
                pip.transform.SetParent(root.transform, false);
                pip.transform.localScale    = new Vector3(0.10f, 0.10f, 0.10f);
                pip.transform.localPosition = new Vector3(i * 0.16f - totalW * 0.5f, pieceRadius * 2f + 0.22f, 0);
            }
        }
        return view;
    }

    // Each elemental gets its own silhouette so pieces are readable at a glance
    void BuildShape(Transform body, Piece piece)
    {
        if (piece.isDecoy)
        {   // Phantom: small flat disc, as before
            Part(body, PrimitiveType.Cylinder, new Vector3(0, -0.15f, 0), new Vector3(0.35f, 0.15f, 0.35f));
            return;
        }

        switch (piece.key)
        {
            case "FLARE":     // flame: tall body with a flickering tip
                Part(body, PrimitiveType.Capsule,  new Vector3(0, -0.04f, 0), new Vector3(0.34f, 0.26f, 0.34f));
                Part(body, PrimitiveType.Sphere,   new Vector3(0,  0.30f, 0), new Vector3(0.16f, 0.24f, 0.16f));
                break;
            case "VOLTIX":    // storm orb with a lightning rod
                Part(body, PrimitiveType.Sphere,   new Vector3(0, -0.05f, 0), Vector3.one * 0.44f);
                Part(body, PrimitiveType.Cylinder, new Vector3(0,  0.28f, 0), new Vector3(0.06f, 0.14f, 0.06f));
                break;
            case "ZEPHYROS":  // slim wind spire
                Part(body, PrimitiveType.Cylinder, Vector3.zero,              new Vector3(0.20f, 0.30f, 0.20f));
                break;
            case "FROSTBITE": // ice diamond
                Part(body, PrimitiveType.Cube,     Vector3.zero,              Vector3.one * 0.40f, new Vector3(0, 45, 0));
                break;
            case "BULWARK":   // wide wall block
                Part(body, PrimitiveType.Cube,     new Vector3(0, -0.08f, 0), new Vector3(0.55f, 0.34f, 0.38f));
                break;
            case "AEGIS":     // protective dome
                Part(body, PrimitiveType.Sphere,   new Vector3(0, -0.10f, 0), new Vector3(0.52f, 0.30f, 0.52f));
                break;
            case "VERDANT":   // little tree: trunk + canopy
                Part(body, PrimitiveType.Cylinder, new Vector3(0, -0.18f, 0), new Vector3(0.14f, 0.12f, 0.14f));
                Part(body, PrimitiveType.Sphere,   new Vector3(0,  0.10f, 0), Vector3.one * 0.42f);
                break;
            case "MIMIC":     // ghost blob: two stacked orbs
                Part(body, PrimitiveType.Sphere,   new Vector3(0, -0.10f, 0), Vector3.one * 0.38f);
                Part(body, PrimitiveType.Sphere,   new Vector3(0,  0.16f, 0), Vector3.one * 0.24f);
                break;
            case "SHARDIS":   // tilted crystal
                Part(body, PrimitiveType.Cube,     Vector3.zero,              Vector3.one * 0.34f, new Vector3(45, 45, 0));
                break;
            default:          // fallback: the old squat cylinder
                Part(body, PrimitiveType.Cylinder, Vector3.zero,              new Vector3(0.52f, 0.28f, 0.52f));
                break;
        }
    }

    static GameObject Part(Transform parent, PrimitiveType type, Vector3 pos, Vector3 scale, Vector3 euler = default)
    {
        var go = GameObject.CreatePrimitive(type);
        Destroy(go.GetComponent<Collider>()); // the body's box collider handles clicks
        go.transform.SetParent(parent, false);
        go.transform.localPosition    = pos;
        go.transform.localScale       = scale;
        go.transform.localEulerAngles = euler;
        return go;
    }

    void RefreshPips(PieceView view, Piece piece)
    {
        if (piece.isDecoy || piece.maxShards == 0) return;
        Color full  = piece.player == 1 ? P1Color : P2Color;
        Color empty = new Color(0.55f, 0.55f, 0.55f);

        // Child 0 is the body; children 1..n are the pips
        for (int i = 1; i < view.transform.childCount; i++)
            SetColor(view.transform.GetChild(i).gameObject, (i - 1) < piece.shards ? full : empty);
    }

    // ── events ────────────────────────────────────────────────────────────────

    void OnPhaseChanged(GamePhase phase)
    {
        if (phase != GamePhase.Player)
        {
            ResetSelection();
            RefreshAll();
        }
        else
        {
            RefreshAll();
        }
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    public Vector3 WorldPos(int r, int c, float yOffset)
    {
        float half = (BoardManager.BS - 1) * tileSize * 0.5f;
        return new Vector3(c * tileSize - half, yOffset, r * tileSize - half);
    }

    static Color BaseTileColor(int r, int c) => (r + c) % 2 == 0 ? TileLight : TileDark;

    static Color PieceColor(Piece p)
    {
        if (p.isDecoy) return DecoyCol;
        Color col = p.player == 1 ? P1Color : P2Color;
        // Class tint: Attack = warm, Defense = cool, Support = green
        col = p.cls switch
        {
            "Attack"  => Color.Lerp(col, new Color(1f, 0.7f, 0.6f), 0.25f),
            "Defense" => Color.Lerp(col, new Color(0.7f, 0.8f, 1f), 0.25f),
            "Support" => Color.Lerp(col, new Color(0.7f, 1f, 0.7f), 0.25f),
            _         => col
        };
        // Status blends on the piece itself
        if (p.stunned)  col = Color.Lerp(col, Color.yellow, 0.50f);
        if (p.rooted)   col = Color.Lerp(col, Color.green,  0.40f);
        if (p.shielded || p.energyShieldActive) col = Color.Lerp(col, new Color(0.7f, 0.4f, 1f), 0.45f);
        if (p.weakened) col = Color.Lerp(col, new Color(1f, 0.5f, 0f), 0.35f);
        if (p.fortress) col = Color.Lerp(col, Color.gray, 0.45f);
        return col;
    }

    static void SetColor(GameObject go, Color col)
    {
        var rend = go.GetComponent<Renderer>();
        if (rend != null) rend.material.color = col;
    }
}
