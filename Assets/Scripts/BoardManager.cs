using System;
using System.Collections.Generic;
using UnityEngine;

public class BoardManager : MonoBehaviour
{
    public const int BS = 8;

    public Piece[,] Board { get; private set; }

    public event Action<string> OnLog;

    // Visual hooks — AbilityVFX listens to these to play effects.
    // Both fire BEFORE damage/effects apply, so listeners can read the pre-hit board.
    public event Action<int, int, int, int> OnAttackVfx;               // attacker r,c → damaged target r,c
    public event Action<int, int, int, int, AbilityType> OnAbilityVfx; // caster r,c → target tile

    // ── Arena mode state ──────────────────────────────────────────────────────
    public int  GoalsP1   { get; private set; }
    public int  GoalsP2   { get; private set; }
    public bool BallLoose { get; private set; }
    public int  BallR     { get; private set; }
    public int  BallC     { get; private set; }

    // Fired after a goal so TurnManager can end the game mid-turn
    public event Action OnGoalScored;

    class RespawnEntry { public Piece piece; public int turnsLeft; public int homeR, homeC; }
    readonly List<RespawnEntry> respawns = new();
    readonly Dictionary<string, (int r, int c)> homeTiles = new();

    void Awake() => InitBoard();

    public void InitBoard()
    {
        Board = new Piece[BS, BS];
        respawns.Clear();
        homeTiles.Clear();
        GoalsP1 = 0; GoalsP2 = 0;
        BallLoose = false;

        if (GameModeState.IsArena) { InitArenaBoard(); return; }

        Board[7, 0] = PieceDefinitions.Make("FLARE",     1, "p1_fl");
        Board[7, 2] = PieceDefinitions.Make("FROSTBITE", 1, "p1_fb");
        Board[7, 4] = PieceDefinitions.Make("BULWARK",   1, "p1_bw");
        Board[7, 6] = PieceDefinitions.Make("VERDANT",   1, "p1_vd");
        Board[0, 1] = PieceDefinitions.Make("VOLTIX",    2, "p2_vx");
        Board[0, 3] = PieceDefinitions.Make("AEGIS",     2, "p2_ag");
        Board[0, 5] = PieceDefinitions.Make("MIMIC",     2, "p2_mm");
        Board[0, 7] = PieceDefinitions.Make("SHARDIS",   2, "p2_sd");
    }

    public static bool InBounds(int r, int c) => r >= 0 && r < BS && c >= 0 && c < BS;
    public static int  Cheb(int r1, int c1, int r2, int c2) => Mathf.Max(Mathf.Abs(r1 - r2), Mathf.Abs(c1 - c2));

    // ── Queries ──────────────────────────────────────────────────────────────

    public List<(int r, int c)> GetMoves(int row, int col)
    {
        var piece = Board[row, col];
        var result = new List<(int, int)>();
        if (piece == null || piece.stunned) return result;

        // Windborn (Zephyros): immune to rooted
        bool effectivelyRooted = piece.rooted && piece.key != "ZEPHYROS";
        if (effectivelyRooted) return result;

        // Eerie Aura (Mimic passive): enemies within 1 tile reduce move by 1
        int effectiveMove = piece.move;
        if (!piece.isDecoy)
        {
            for (int er = row - 1; er <= row + 1; er++)
            for (int ec = col - 1; ec <= col + 1; ec++)
            {
                if (!InBounds(er, ec) || (er == row && ec == col)) continue;
                var e = Board[er, ec];
                if (e != null && e.player != piece.player && e.key == "MIMIC" && !e.isDecoy)
                    effectiveMove = Mathf.Max(0, effectiveMove - 1);
            }
        }

        // Arena: carrying the ball slows you down
        if (GameModeState.IsArena && piece.hasBall)
            effectiveMove = Mathf.Max(0, effectiveMove - ArenaConfig.CarrierMovePenalty);

        for (int dr = -effectiveMove; dr <= effectiveMove; dr++)
        for (int dc = -effectiveMove; dc <= effectiveMove; dc++)
        {
            if (dr == 0 && dc == 0) continue;
            int nr = row + dr, nc = col + dc;
            if (!InBounds(nr, nc) || Board[nr, nc] != null) continue;
            if (GameModeState.IsArena)
            {
                // Carrier can't enter outer goal tiles while the corner tower guards them
                if (piece.hasBall && IsGoalTile(piece.player, nr, nc) && !IsGoalOpen(piece.player, nr, nc)) continue;
                // Decoys can't sit on (or pick up) the loose ball
                if (piece.isDecoy && BallLoose && nr == BallR && nc == BallC) continue;
            }
            result.Add((nr, nc));
        }
        return result;
    }

    public List<(int r, int c)> GetAttacks(int row, int col)
    {
        var piece = Board[row, col];
        var result = new List<(int, int)>();
        if (piece == null || piece.stunned || piece.isDecoy || piece.key == "TOWER") return result;
        for (int dr = -1; dr <= 1; dr++)
        for (int dc = -1; dc <= 1; dc++)
        {
            if (dr == 0 && dc == 0) continue;
            int nr = row + dr, nc = col + dc;
            if (!InBounds(nr, nc)) continue;
            var t = Board[nr, nc];
            if (t != null && t.player != piece.player) result.Add((nr, nc));
        }
        return result;
    }

    public List<(int r, int c)> GetCastTargets(int row, int col)
    {
        var piece = Board[row, col];
        var result = new List<(int, int)>();
        if (piece == null || piece.abilityCd > 0 || piece.stunned || piece.isDecoy || piece.key == "TOWER") return result;
        var ab = piece.ability;

        switch (ab.type)
        {
            case AbilityType.Damage:
                for (int r = 0; r < BS; r++) for (int c = 0; c < BS; c++)
                {
                    var t = Board[r, c];
                    if (t != null && t.player != piece.player && !t.isDecoy &&
                        Cheb(r, c, row, col) <= ab.range) result.Add((r, c));
                }
                break;

            case AbilityType.Barrier:
                for (int r = 0; r < BS; r++) for (int c = 0; c < BS; c++)
                {
                    var t = Board[r, c];
                    if (t != null && t.player == piece.player && !(r == row && c == col) && !t.isDecoy &&
                        Cheb(r, c, row, col) <= ab.range) result.Add((r, c));
                }
                break;

            case AbilityType.Freeze:
                for (int r = 0; r < BS; r++) for (int c = 0; c < BS; c++)
                    if (Cheb(r, c, row, col) <= ab.range) result.Add((r, c));
                break;

            case AbilityType.Line:
                int[][] dirs = { new[]{0,1},new[]{0,-1},new[]{1,0},new[]{-1,0},
                                 new[]{1,1},new[]{1,-1},new[]{-1,1},new[]{-1,-1} };
                foreach (var d in dirs)
                {
                    int nr = row + d[0], nc = col + d[1];
                    while (InBounds(nr, nc)) { result.Add((nr, nc)); if (Board[nr, nc] != null) break; nr += d[0]; nc += d[1]; }
                }
                break;

            case AbilityType.Shockwave:
            case AbilityType.Fortress:
            case AbilityType.Pull:
            case AbilityType.Heal:
                result.Add((row, col));
                break;

            case AbilityType.Decoy:
                for (int dr = -1; dr <= 1; dr++) for (int dc = -1; dc <= 1; dc++)
                {
                    if (dr == 0 && dc == 0) continue;
                    int nr = row + dr, nc = col + dc;
                    if (InBounds(nr, nc) && Board[nr, nc] == null) result.Add((nr, nc));
                }
                break;
        }
        return result;
    }

    public List<(int r, int c)> GetAoEPreview(int row, int col, int tr, int tc)
    {
        var piece = Board[row, col];
        var aoe = new List<(int, int)>();
        if (piece == null) return aoe;
        var ab = piece.ability;

        switch (ab.type)
        {
            case AbilityType.Damage:
            case AbilityType.Barrier:
            case AbilityType.Decoy:
                aoe.Add((tr, tc));
                break;

            case AbilityType.Freeze:
                for (int ddr = -1; ddr <= 1; ddr++) for (int ddc = -1; ddc <= 1; ddc++)
                { int nr = tr+ddr, nc = tc+ddc; if (InBounds(nr,nc)) aoe.Add((nr,nc)); }
                break;

            case AbilityType.Line:
                int dr2 = Math.Sign(tr-row), dc2 = Math.Sign(tc-col);
                int r2 = row+dr2, c2 = col+dc2;
                while (InBounds(r2,c2)) { aoe.Add((r2,c2)); if (Board[r2,c2]!=null) break; r2+=dr2; c2+=dc2; }
                break;

            case AbilityType.Shockwave:
                int radius = piece.staticCharges > 0 ? piece.staticCharges : 1;
                for (int ddr=-radius; ddr<=radius; ddr++) for (int ddc=-radius; ddc<=radius; ddc++)
                { int nr=row+ddr, nc=col+ddc; if (InBounds(nr,nc)&&!(ddr==0&&ddc==0)) aoe.Add((nr,nc)); }
                break;

            case AbilityType.Fortress:
                for (int ddr=-2; ddr<=2; ddr++) for (int ddc=-2; ddc<=2; ddc++)
                { int nr=row+ddr, nc=col+ddc; if (InBounds(nr,nc)&&!(ddr==0&&ddc==0)) aoe.Add((nr,nc)); }
                break;

            case AbilityType.Pull:
                for (int ddr=-3; ddr<=3; ddr++) for (int ddc=-3; ddc<=3; ddc++)
                { int nr=row+ddr, nc=col+ddc; if (InBounds(nr,nc)&&!(ddr==0&&ddc==0)) aoe.Add((nr,nc)); }
                break;

            case AbilityType.Heal:
                for (int ddr=-1; ddr<=1; ddr++) for (int ddc=-1; ddc<=1; ddc++)
                { int nr=row+ddr, nc=col+ddc; if (InBounds(nr,nc)) aoe.Add((nr,nc)); }
                break;
        }
        return aoe;
    }

    // ── Actions ──────────────────────────────────────────────────────────────

    public bool TryMove(int row, int col, int nr, int nc)
    {
        var piece = Board[row, col];
        if (piece == null) return false;
        if (!GetMoves(row, col).Contains((nr, nc))) return false;

        Board[nr, nc] = piece;
        Board[row, col] = null;

        Log($"{N(piece)} moves {Cell(row, col)} » {Cell(nr, nc)}.");

        if (piece.key == "VOLTIX" && piece.staticCharges < 3)
        {
            piece.staticCharges++;
            Log($"{Detail}<color=#ffb347>Static Charge builds: {piece.staticCharges}/3.</color>");
        }

        if (GameModeState.IsArena) OnArenaTileEntered(piece, nr, nc, viaOwnMove: true);
        return true;
    }

    public bool TryAttack(int row, int col, int tr, int tc)
    {
        var attacker = Board[row, col];
        if (attacker == null) return false;
        if (!GetAttacks(row, col).Contains((tr, tc))) return false;

        // Fortress redirect: check if defender's team has an active Bulwark fortress nearby
        int ar = tr, ac = tc;
        for (int r = 0; r < BS; r++) for (int c = 0; c < BS; c++)
        {
            var p = Board[r, c];
            if (p != null && p.player == Board[tr, tc].player && p.fortress &&
                Cheb(r, c, tr, tc) <= 2 && !(r == tr && c == tc))
            {
                ar = r; ac = c;
                break;
            }
        }

        OnAttackVfx?.Invoke(row, col, ar, ac);
        bool redirected = ar != tr || ac != tc;
        Log($"{N(attacker)} attacks {N(Board[tr, tc])} at {Cell(tr, tc)}!");
        if (redirected)
            Log($"{Detail}<color=#6bd6e6>Magnetic Fortress drags the blow onto {N(Board[ar, ac])} at {Cell(ar, ac)}!</color>");
        ApplyDamage(ar, ac, isPhysical: true, killer: attacker, amount: attacker.dmg, redirected: redirected);
        return true;
    }

    public bool TryAbility(int row, int col, int tr, int tc)
    {
        var piece = Board[row, col];
        if (piece == null || piece.abilityCd > 0 || piece.stunned) return false;
        if (!GetCastTargets(row, col).Contains((tr, tc))) return false;

        piece.abilityCd = piece.ability.cooldown;
        var ab = piece.ability;

        OnAbilityVfx?.Invoke(row, col, tr, tc, ab.type);

        switch (ab.type)
        {
            case AbilityType.Damage:
            {
                var t = Board[tr, tc];
                if (t != null)
                {
                    Log($"{N(piece)} fires <b>{ab.name}</b> at {N(t)} ({Cell(tr, tc)})!");
                    ApplyDamage(tr, tc, false, piece, piece.dmg);
                }
                break;
            }
            case AbilityType.Line:
            {
                int dr = Math.Sign(tr-row), dc = Math.Sign(tc-col);
                Log($"{N(piece)} unleashes <b>{ab.name}</b> from {Cell(row, col)}!");
                int nr = row+dr, nc = col+dc;
                bool connected = false;
                while (InBounds(nr, nc))
                {
                    var t = Board[nr, nc];
                    if (t != null)
                    {
                        connected = true;
                        if (t.player != piece.player)
                        {
                            Log($"{Detail}The wave strikes {N(t)} at {Cell(nr, nc)}!");
                            ApplyDamage(nr, nc, false, piece, piece.dmg);
                        }
                        else Log($"{Detail}The wave dissipates against friendly {N(t)} at {Cell(nr, nc)}.");
                        break;
                    }
                    nr += dr; nc += dc;
                }
                if (!connected) Log($"{Detail}The wave travels off the board without hitting anything.");
                break;
            }
            case AbilityType.Freeze:
            {
                Log($"{N(piece)} casts <b>{ab.name}</b> on the area around {Cell(tr, tc)}!");
                bool any = false;
                for (int ddr=-1; ddr<=1; ddr++) for (int ddc=-1; ddc<=1; ddc++)
                {
                    int nr=tr+ddr, nc=tc+ddc;
                    if (!InBounds(nr,nc)) continue;
                    var t=Board[nr,nc];
                    if (t!=null && t.player!=piece.player)
                    {
                        any = true;
                        // Windborn: Zephyros immune to root
                        if (t.key == "ZEPHYROS") { Log($"{Detail}{N(t)}'s Windborn resists the root!"); continue; }
                        t.rooted=true; t.rootedTurns=2;
                        Log($"{Detail}<color=#ffb347>{N(t)} at {Cell(nr, nc)} is rooted for 2 turns — it cannot move.</color>");
                    }
                }
                if (!any) Log($"{Detail}No enemies were caught in the area.");
                break;
            }
            case AbilityType.Shockwave:
            {
                int radius = piece.staticCharges > 0 ? piece.staticCharges : 1;
                Log($"{N(piece)} releases <b>Shockwave</b> — radius {radius} from {piece.staticCharges} Static Charge(s)!");
                bool any = false;
                for (int ddr=-radius; ddr<=radius; ddr++) for (int ddc=-radius; ddc<=radius; ddc++)
                {
                    int nr=row+ddr, nc=col+ddc;
                    if (!InBounds(nr,nc)||(ddr==0&&ddc==0)) continue;
                    var t=Board[nr,nc];
                    if (t!=null && t.player!=piece.player)
                    {
                        t.stunned=true; t.stunnedTurns=1; any = true;
                        Log($"{Detail}<color=#ffb347>{N(t)} at {Cell(nr, nc)} is stunned for 1 turn — no moving, attacking, or casting.</color>");
                    }
                }
                if (!any) Log($"{Detail}No enemies were caught in the blast.");
                if (piece.staticCharges > 1) Log($"{Detail}Static Charges reset to 1.");
                piece.staticCharges=1;
                break;
            }
            case AbilityType.Fortress:
                piece.fortress=true; piece.fortressTurns=2;
                Log($"{N(piece)} activates <b>Magnetic Fortress</b>!");
                Log($"{Detail}<color=#6bd6e6>For 2 turns, attacks on allies within 2 tiles strike {N(piece)} instead.</color>");
                break;

            case AbilityType.Barrier:
            {
                var t=Board[tr,tc];
                if (t!=null)
                {
                    t.shielded=true; t.shieldTurns=2;
                    Log($"{N(piece)} casts <b>Barrier</b> on {N(t)} ({Cell(tr, tc)})!");
                    Log($"{Detail}<color=#6bd6e6>The next hit within 2 turns is fully absorbed.</color>");
                }
                break;
            }
            case AbilityType.Heal:
            {
                Log($"{N(piece)} uses <b>{ab.name}</b>!");
                bool any = false;
                for (int ddr=-1; ddr<=1; ddr++) for (int ddc=-1; ddc<=1; ddc++)
                {
                    int nr=row+ddr, nc=col+ddc;
                    if (!InBounds(nr,nc)) continue;
                    var t=Board[nr,nc];
                    if (t!=null && t.player==piece.player && t.shards<t.maxShards)
                    {
                        int before = t.shards;
                        t.shards=Mathf.Min(t.shards+1,t.maxShards); any = true;
                        Log($"{Detail}<color=#7dd87d>{N(t)} heals ({before} » {t.shards} shards).</color>");
                    }
                }
                if (!any) Log($"{Detail}No allies needed healing.");
                break;
            }
            case AbilityType.Decoy:
                Board[tr,tc]=PieceDefinitions.MakeDecoy(piece);
                Log($"{N(piece)} conjures a <b>Phantom Lantern</b> at {Cell(tr, tc)} — a decoy to soak enemy attacks.");
                break;

            case AbilityType.Pull:
            {
                var inRange=new List<(int r,int c,int dist)>();
                for (int r=0; r<BS; r++) for (int c=0; c<BS; c++)
                {
                    if (r==row&&c==col||Board[r,c]==null) continue;
                    int dist=Cheb(r,c,row,col);
                    if (dist<=ab.range) inRange.Add((r,c,dist));
                }
                inRange.Sort((a,b)=>b.dist.CompareTo(a.dist));
                Log($"{N(piece)} casts <b>{ab.name}</b> — everything within {ab.range} tiles is dragged inward!");
                bool anyPulled = false;
                foreach (var (r,c,_) in inRange)
                {
                    var t=Board[r,c]; if (t==null||t.key=="TOWER") continue; // towers don't budge
                    int dr=Math.Sign(row-r), dc=Math.Sign(col-c);
                    int nr=r+dr, nc=c+dc;
                    if (InBounds(nr,nc)&&Board[nr,nc]==null)
                    {
                        Board[nr,nc]=t; Board[r,c]=null;
                        anyPulled = true;
                        Log($"{Detail}{N(t)} is pulled {Cell(r, c)} » {Cell(nr, nc)}.");
                        if (t.player!=piece.player)
                        {
                            t.weakened=true; t.weakenedTurns=2;
                            Log($"{Detail}<color=#ffb347>{N(t)} is weakened for 2 turns — its defensive passives are disabled.</color>");
                        }
                        if (GameModeState.IsArena) OnArenaTileEntered(t, nr, nc, viaOwnMove: false);
                    }
                }
                if (!anyPulled) Log($"{Detail}Nothing was close enough (or free) to pull.");
                break;
            }
        }
        Log($"{Detail}<color=#88889c>{ab.name} is on cooldown for {ab.cooldown} turns.</color>");
        return true;
    }

    // ── Damage ───────────────────────────────────────────────────────────────

    // Central damage application. Returns true if target was killed.
    // isPhysical: true for regular attacks; false for ability/passive damage.
    // redirected: hit was pulled onto this target by Magnetic Fortress — it pierces
    // physical immunity so Bulwark genuinely absorbs the damage he attracts.
    bool ApplyDamage(int tr, int tc, bool isPhysical, Piece killer, int amount = 1, bool redirected = false)
    {
        var target = Board[tr, tc];
        if (target == null || amount <= 0) return false;

        // Weakened (Gravitic Distortion) switches off defensive passives:
        // physical immunity, Ice Armor, and Energy Shield. Barrier still works.
        bool passivesUp = !target.weakened;
        if (target.weakened &&
            (target.energyShieldActive ||
             (isPhysical && (target.key == "BULWARK" || target.key == "SHARDIS" || target.key == "FROSTBITE"))))
            Log($"{Detail}<color=#ffb347>Weakened — {N(target)}'s defensive passive is offline!</color>");

        // Physical immunity: Bulwark (Battle Hardened) and Shardis (Phased Form)
        if (isPhysical && passivesUp && !redirected &&
            (target.key == "BULWARK" || target.key == "SHARDIS"))
        {
            string passive = target.key == "BULWARK" ? "Battle Hardened" : "Phased Form";
            Log($"{Detail}<color=#6bd6e6>{passive} — {N(target)} is immune to physical damage. No effect.</color>");
            return false;
        }

        // Ice Armor: Frostbite reduces physical damage by 1
        if (isPhysical && passivesUp && target.key == "FROSTBITE")
        {
            amount -= 1;
            if (amount <= 0)
            {
                Log($"{Detail}<color=#6bd6e6>Ice Armor blocks the hit — no damage gets through.</color>");
                return false;
            }
            Log($"{Detail}<color=#6bd6e6>Ice Armor blocks 1 damage — {amount} gets through.</color>");
        }

        // Aegis personal Energy Shield (passive)
        if (passivesUp && target.energyShieldActive)
        {
            Log($"{Detail}<color=#6bd6e6>{N(target)}'s Energy Shield shatters and absorbs the hit. It returns when Aegis lands a kill.</color>");
            target.energyShieldActive = false;
            return false;
        }

        // Barrier (ability shield)
        if (target.shielded)
        {
            Log($"{Detail}<color=#6bd6e6>{N(target)}'s Barrier shatters and absorbs the hit.</color>");
            target.shielded = false; target.shieldTurns = 0;
            return false;
        }

        // Deal damage
        int before = target.shards;
        target.shards -= amount;
        Log($"{Detail}{N(target)} takes <color=#ff6b6b>{amount} damage</color> ({before} » {Math.Max(target.shards, 0)} shards).");

        // Voltix: any damage resets static charges to 1
        if (target.key == "VOLTIX")
        {
            if (target.staticCharges > 1)
                Log($"{Detail}<color=#ffb347>{N(target)}'s Static Charge resets to 1.</color>");
            target.staticCharges = 1;
        }

        if (target.shards <= 0)
        {
            Log($"{Detail}<color=#ff6b6b><b>{target.pieceName} is destroyed!</b></color>");
            Board[tr, tc] = null;
            if (GameModeState.IsArena) OnArenaDeath(target, tr, tc);
            if (target.key == "MIMIC") RemoveMimicDecoy(target.id);

            // Aegis Energy Shield regenerates only when Aegis itself lands the kill
            if (killer != null && killer.key == "AEGIS" && !killer.energyShieldActive)
            {
                killer.energyShieldActive = true;
                Log($"{Detail}<color=#6bd6e6>{N(killer)}'s Energy Shield regenerates from the kill!</color>");
            }

            return true;
        }
        return false;
    }

    void RemoveMimicDecoy(string mimicId)
    {
        // Match by owner so every phantom this Mimic ever spawned fades, not just the first
        for (int r = 0; r < BS; r++) for (int c = 0; c < BS; c++)
        {
            var p = Board[r, c];
            if (p != null && p.isDecoy && p.decoyOwnerId == mimicId)
            {
                Board[r, c] = null;
                Log($"{Detail}The Phantom Lantern at {Cell(r, c)} fades as Mimic falls.");
            }
        }
    }

    // ── Tick ─────────────────────────────────────────────────────────────────

    // Call at end of each player's turn. Decrements that player's piece cooldowns/effects
    // and fires passive effects (Scorch, Life Bloom).
    public void TickEffects(int player)
    {
        // Arena first, while status effects are still live — a stunned tower
        // has to miss its volley before the loop below clears the stun
        if (GameModeState.IsArena)
        {
            TowerVolleys(player);
            TickRespawns(player);
        }

        // Collect positions first to avoid modifying board mid-iteration
        var positions = new List<(int r, int c)>();
        for (int r = 0; r < BS; r++) for (int c = 0; c < BS; c++)
        {
            var p = Board[r, c];
            if (p != null && p.player == player) positions.Add((r, c));
        }

        foreach (var (r, c) in positions)
        {
            var p = Board[r, c];
            if (p == null) continue; // may have been killed by Scorch mid-tick

            if (p.abilityCd    > 0) { p.abilityCd--;    if (p.abilityCd == 0) Log($"{N(p)}'s {p.ability.name} is ready again."); }
            if (p.shieldTurns   > 0) { p.shieldTurns--;   if (p.shieldTurns   == 0) { p.shielded  = false; Log($"{N(p)}'s Barrier fades unused."); } }
            if (p.rootedTurns   > 0) { p.rootedTurns--;   if (p.rootedTurns   == 0) { p.rooted    = false; Log($"{N(p)} breaks free of the root and can move again."); } }
            if (p.stunnedTurns  > 0) { p.stunnedTurns--;  if (p.stunnedTurns  == 0) { p.stunned   = false; Log($"{N(p)} recovers from the stun."); } }
            if (p.weakenedTurns > 0) { p.weakenedTurns--; if (p.weakenedTurns == 0) { p.weakened  = false; Log($"{N(p)} is no longer weakened — defensive passives back online."); } }
            if (p.fortressTurns > 0) { p.fortressTurns--; if (p.fortressTurns == 0) { p.fortress  = false; Log($"{N(p)}'s Magnetic Fortress powers down."); } }

            // Flare passive — Scorch: deal 1 damage to each adjacent enemy
            if (p.key == "FLARE")
            {
                for (int ddr = -1; ddr <= 1; ddr++) for (int ddc = -1; ddc <= 1; ddc++)
                {
                    if (ddr == 0 && ddc == 0) continue;
                    int nr = r + ddr, nc = c + ddc;
                    if (!InBounds(nr, nc)) continue;
                    var enemy = Board[nr, nc];
                    if (enemy != null && enemy.player != player)
                    {
                        Log($"{N(p)}'s Scorch singes {N(enemy)} at {Cell(nr, nc)}!");
                        ApplyDamage(nr, nc, isPhysical: false, killer: p);
                    }
                }
            }

            // Verdant passive — Life Bloom: heal adjacent allies 1 shard
            if (p.key == "VERDANT")
            {
                for (int ddr = -1; ddr <= 1; ddr++) for (int ddc = -1; ddc <= 1; ddc++)
                {
                    if (ddr == 0 && ddc == 0) continue;
                    int nr = r + ddr, nc = c + ddc;
                    if (!InBounds(nr, nc)) continue;
                    var ally = Board[nr, nc];
                    if (ally != null && ally.player == player && ally.shards < ally.maxShards)
                    {
                        ally.shards++;
                        Log($"<color=#7dd87d>{N(p)}'s Life Bloom heals {N(ally)} ({ally.shards - 1} » {ally.shards} shards).</color>");
                    }
                }
            }
        }
    }

    // ── Win Check ─────────────────────────────────────────────────────────────

    public int CheckWinner()
    {
        // Arena: first team to the goal target wins; elimination can't happen (respawns)
        if (GameModeState.IsArena)
        {
            if (GoalsP1 >= ArenaConfig.GoalsToWin) return 1;
            if (GoalsP2 >= ArenaConfig.GoalsToWin) return 2;
            return 0;
        }

        int p1 = 0, p2 = 0;
        for (int r = 0; r < BS; r++) for (int c = 0; c < BS; c++)
        {
            var p = Board[r, c];
            if (p == null || p.isDecoy) continue;
            if (p.player == 1) p1++; else p2++;
        }
        if (p1 == 0) return 2;
        if (p2 == 0) return 1;
        return 0;
    }

    // ── Arena mode ────────────────────────────────────────────────────────────

    void InitArenaBoard()
    {
        // Corner towers guard the outer goal tiles on each back row
        Board[7, 0] = PieceDefinitions.MakeTower(1, "p1_t1");
        Board[7, 7] = PieceDefinitions.MakeTower(1, "p1_t2");
        Board[0, 0] = PieceDefinitions.MakeTower(2, "p2_t1");
        Board[0, 7] = PieceDefinitions.MakeTower(2, "p2_t2");

        Board[6, 1] = PieceDefinitions.Make("FLARE",     1, "p1_fl");
        Board[6, 3] = PieceDefinitions.Make("FROSTBITE", 1, "p1_fb");
        Board[6, 4] = PieceDefinitions.Make("BULWARK",   1, "p1_bw");
        Board[6, 6] = PieceDefinitions.Make("VERDANT",   1, "p1_vd");
        Board[1, 1] = PieceDefinitions.Make("VOLTIX",    2, "p2_vx");
        Board[1, 3] = PieceDefinitions.Make("AEGIS",     2, "p2_ag");
        Board[1, 4] = PieceDefinitions.Make("MIMIC",     2, "p2_mm");
        Board[1, 6] = PieceDefinitions.Make("SHARDIS",   2, "p2_sd");

        // Remember where everyone started so respawns come back home
        for (int r = 0; r < BS; r++) for (int c = 0; c < BS; c++)
            if (Board[r, c] != null && Board[r, c].key != "TOWER")
                homeTiles[Board[r, c].id] = (r, c);

        SpawnBall(ArenaConfig.BallSpawnRow, ArenaConfig.BallSpawnCol);
    }

    // Row the given player must reach to score (the opponent's back row)
    public static int GoalRowFor(int player) => player == 1 ? 0 : BS - 1;

    public bool IsGoalTile(int player, int r, int c) =>
        r == GoalRowFor(player) && c >= ArenaConfig.GoalZoneMinCol && c <= ArenaConfig.GoalZoneMaxCol;

    // Outer goal tiles only open up once the corner tower beside them falls
    public bool IsGoalOpen(int player, int r, int c)
    {
        if (!IsGoalTile(player, r, c)) return false;
        if (c == ArenaConfig.GoalZoneMinCol) return !TowerAlive(r, 0);
        if (c == ArenaConfig.GoalZoneMaxCol) return !TowerAlive(r, BS - 1);
        return true;
    }

    bool TowerAlive(int r, int c) => Board[r, c] != null && Board[r, c].key == "TOWER";

    // Called whenever a piece lands on a tile (own move, pull, etc.).
    // Goals only count when the carrier moved there under its own power.
    void OnArenaTileEntered(Piece piece, int r, int c, bool viaOwnMove)
    {
        if (BallLoose && r == BallR && c == BallC && !piece.isDecoy)
        {
            BallLoose = false;
            piece.hasBall = true;
            Log($"{N(piece)} picks up the ball at {Cell(r, c)}! (Move -{ArenaConfig.CarrierMovePenalty} while carrying.)");
        }

        if (viaOwnMove && piece.hasBall && IsGoalTile(piece.player, r, c) && IsGoalOpen(piece.player, r, c))
            ScoreGoal(piece);
    }

    void ScoreGoal(Piece scorer)
    {
        if (scorer.player == 1) GoalsP1++; else GoalsP2++;
        scorer.hasBall = false;
        Log($"<color=#ffd24d><b>GOAL!</b></color> {N(scorer)} carries the ball into the goal! Score: {GoalsP1}–{GoalsP2} (first to {ArenaConfig.GoalsToWin}).");
        SpawnBall(ArenaConfig.BallSpawnRow, ArenaConfig.BallSpawnCol);
        OnGoalScored?.Invoke();
    }

    // Place the ball on the requested tile, or the nearest empty one if occupied
    void SpawnBall(int r, int c)
    {
        for (int radius = 0; radius < BS; radius++)
        for (int rr = r - radius; rr <= r + radius; rr++)
        for (int cc = c - radius; cc <= c + radius; cc++)
        {
            if (!InBounds(rr, cc) || Cheb(rr, cc, r, c) != radius || Board[rr, cc] != null) continue;
            BallLoose = true; BallR = rr; BallC = cc;
            return;
        }
    }

    // Drop the ball where the carrier fell; queue non-tower pieces to respawn
    void OnArenaDeath(Piece target, int tr, int tc)
    {
        if (target.hasBall)
        {
            target.hasBall = false;
            BallLoose = true; BallR = tr; BallC = tc;
            Log($"{Detail}{N(target)} drops the ball at {Cell(tr, tc)} — it's loose for anyone to grab.");
        }

        if (target.key == "TOWER")
            Log($"{Detail}The fallen tower no longer guards its outer goal tile!");
        else if (!target.isDecoy && homeTiles.TryGetValue(target.id, out var home))
            respawns.Add(new RespawnEntry { piece = target, turnsLeft = ArenaConfig.RespawnTurns, homeR = home.r, homeC = home.c });
    }

    // Each of this player's towers fires once at an enemy in range —
    // the ball carrier if it can reach one, otherwise the nearest target
    void TowerVolleys(int player)
    {
        for (int r = 0; r < BS; r++) for (int c = 0; c < BS; c++)
        {
            var tower = Board[r, c];
            if (tower == null || tower.key != "TOWER" || tower.player != player || tower.stunned) continue;

            (int r, int c)? target = null;
            int  bestD = int.MaxValue;
            bool bestCarrier = false;
            for (int rr = 0; rr < BS; rr++) for (int cc = 0; cc < BS; cc++)
            {
                var t = Board[rr, cc];
                if (t == null || t.player == player || t.key == "TOWER") continue;
                int d = Cheb(rr, cc, r, c);
                if (d > ArenaConfig.TowerDefenseRange) continue;
                bool carrier = t.hasBall;
                if ((carrier && !bestCarrier) || (carrier == bestCarrier && d < bestD))
                { target = (rr, cc); bestD = d; bestCarrier = carrier; }
            }
            if (target == null) continue;

            OnAttackVfx?.Invoke(r, c, target.Value.r, target.Value.c);
            Log($"{N(tower)} fires a volley at {N(Board[target.Value.r, target.Value.c])} ({Cell(target.Value.r, target.Value.c)})!");
            ApplyDamage(target.Value.r, target.Value.c, isPhysical: false, killer: tower, amount: tower.dmg);
        }
    }

    void TickRespawns(int player)
    {
        for (int i = respawns.Count - 1; i >= 0; i--)
        {
            var e = respawns[i];
            if (e.piece.player != player) continue;
            e.turnsLeft--;
            if (e.turnsLeft > 0) { Log($"{N(e.piece)} respawns in {e.turnsLeft} turn(s)."); continue; }

            var spot = FindRespawnTile(e.homeR, e.homeC);
            if (spot == null) { e.turnsLeft = 1; continue; } // board jammed — try again next turn

            RevivePiece(e.piece);
            Board[spot.Value.r, spot.Value.c] = e.piece;
            respawns.RemoveAt(i);
            Log($"{N(e.piece)} respawns at {Cell(spot.Value.r, spot.Value.c)} at full strength!");
        }
    }

    // Home tile if free, otherwise the nearest empty tile (never on the loose ball)
    (int r, int c)? FindRespawnTile(int r, int c)
    {
        for (int radius = 0; radius < BS; radius++)
        for (int rr = r - radius; rr <= r + radius; rr++)
        for (int cc = c - radius; cc <= c + radius; cc++)
        {
            if (!InBounds(rr, cc) || Cheb(rr, cc, r, c) != radius || Board[rr, cc] != null) continue;
            if (BallLoose && rr == BallR && cc == BallC) continue;
            return (rr, cc);
        }
        return null;
    }

    static void RevivePiece(Piece p)
    {
        p.shards = p.maxShards;
        p.abilityCd = 0;
        p.shielded = false; p.shieldTurns   = 0;
        p.weakened = false; p.weakenedTurns = 0;
        p.stunned  = false; p.stunnedTurns  = 0;
        p.rooted   = false; p.rootedTurns   = 0;
        p.fortress = false; p.fortressTurns = 0;
        p.hasBall  = false;
        p.staticCharges      = p.key == "VOLTIX" ? 1 : 0;
        p.energyShieldActive = p.key == "AEGIS";
    }

    void Log(string msg) => OnLog?.Invoke(msg);

    // ── Log formatting ────────────────────────────────────────────────────────

    // Cells log as chess-style coordinates: columns a–h, ranks 8 (top) down to 1
    public static string Cell(int r, int c) => $"{(char)('a' + c)}{BS - r}";

    // Piece names tinted by owner — gold for the player, blue for the AI
    static string N(Piece p) => p == null ? "?"
        : p.player == 1 ? $"<color=#ffd24d>{p.pieceName}</color>"
        :                 $"<color=#7fb8e6>{p.pieceName}</color>";

    // Indented continuation line: consequences of the action logged above it
    const string Detail = "   <color=#88889c>·</color> ";
}
