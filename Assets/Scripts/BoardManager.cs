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

        if (piece.key == "VOLTIX" && piece.staticCharges < 3) piece.staticCharges++;

        Log($"{piece.pieceName} moves ({row},{col})→({nr},{nc}).");

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
                Log($"Fortress: damage redirected to {Board[ar,ac].pieceName}.");
                break;
            }
        }

        OnAttackVfx?.Invoke(row, col, ar, ac);
        Log($"{attacker.pieceName} attacks {Board[ar, ac]?.pieceName}!");
        ApplyDamage(ar, ac, isPhysical: true, killerPlayer: attacker.player);
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
                if (t != null) { Log($"{piece.pieceName} fires {ab.name} at {t.pieceName}!"); ApplyDamage(tr, tc, false, piece.player); }
                break;
            }
            case AbilityType.Line:
            {
                int dr = Math.Sign(tr-row), dc = Math.Sign(tc-col);
                int nr = row+dr, nc = col+dc;
                while (InBounds(nr, nc))
                {
                    var t = Board[nr, nc];
                    if (t != null)
                    {
                        if (t.player != piece.player) { Log($"Gale Slash hits {t.pieceName}!"); ApplyDamage(nr, nc, false, piece.player); }
                        break;
                    }
                    nr += dr; nc += dc;
                }
                break;
            }
            case AbilityType.Freeze:
            {
                var hit = new List<string>();
                for (int ddr=-1; ddr<=1; ddr++) for (int ddc=-1; ddc<=1; ddc++)
                {
                    int nr=tr+ddr, nc=tc+ddc;
                    if (!InBounds(nr,nc)) continue;
                    var t=Board[nr,nc];
                    if (t!=null && t.player!=piece.player)
                    {
                        // Windborn: Zephyros immune to root
                        if (t.key == "ZEPHYROS") { Log($"{t.pieceName}'s Windborn resists the root!"); continue; }
                        t.rooted=true; t.rootedTurns=2; hit.Add(t.pieceName);
                    }
                }
                Log($"{piece.pieceName} uses {ab.name}! Rooted: {(hit.Count>0?string.Join(", ",hit):"none")}");
                break;
            }
            case AbilityType.Shockwave:
            {
                int radius = piece.staticCharges > 0 ? piece.staticCharges : 1;
                var hit = new List<string>();
                for (int ddr=-radius; ddr<=radius; ddr++) for (int ddc=-radius; ddc<=radius; ddc++)
                {
                    int nr=row+ddr, nc=col+ddc;
                    if (!InBounds(nr,nc)||(ddr==0&&ddc==0)) continue;
                    var t=Board[nr,nc];
                    if (t!=null && t.player!=piece.player) { t.stunned=true; t.stunnedTurns=1; hit.Add(t.pieceName); }
                }
                piece.staticCharges=1;
                Log($"{piece.pieceName} releases Shockwave (radius {radius})! Stunned: {(hit.Count>0?string.Join(", ",hit):"none")}");
                break;
            }
            case AbilityType.Fortress:
                piece.fortress=true; piece.fortressTurns=2;
                Log($"{piece.pieceName} activates Magnetic Fortress! Redirecting damage for 2 turns.");
                break;

            case AbilityType.Barrier:
            {
                var t=Board[tr,tc];
                if (t!=null) { t.shielded=true; t.shieldTurns=2; Log($"{piece.pieceName} shields {t.pieceName} with Barrier!"); }
                break;
            }
            case AbilityType.Heal:
            {
                var healed=new List<string>();
                for (int ddr=-1; ddr<=1; ddr++) for (int ddc=-1; ddc<=1; ddc++)
                {
                    int nr=row+ddr, nc=col+ddc;
                    if (!InBounds(nr,nc)) continue;
                    var t=Board[nr,nc];
                    if (t!=null && t.player==piece.player && t.shards<t.maxShards)
                    { t.shards=Mathf.Min(t.shards+1,t.maxShards); healed.Add(t.pieceName); }
                }
                Log($"{piece.pieceName} uses {ab.name}! Healed: {(healed.Count>0?string.Join(", ",healed):"none")}");
                break;
            }
            case AbilityType.Decoy:
                Board[tr,tc]=PieceDefinitions.MakeDecoy(piece);
                Log($"{piece.pieceName} conjures a Phantom Lantern!");
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
                var moved=new List<string>();
                foreach (var (r,c,_) in inRange)
                {
                    var t=Board[r,c]; if (t==null||t.key=="TOWER") continue; // towers don't budge
                    int dr=Math.Sign(row-r), dc=Math.Sign(col-c);
                    int nr=r+dr, nc=c+dc;
                    if (InBounds(nr,nc)&&Board[nr,nc]==null)
                    {
                        Board[nr,nc]=t; Board[r,c]=null;
                        if (t.player!=piece.player) { t.weakened=true; t.weakenedTurns=2; }
                        if (GameModeState.IsArena) OnArenaTileEntered(t, nr, nc, viaOwnMove: false);
                        moved.Add(t.pieceName);
                    }
                }
                Log($"{piece.pieceName} uses Gravitic Distortion! Pulled: {(moved.Count>0?string.Join(", ",moved):"none")}");
                break;
            }
        }
        return true;
    }

    // ── Damage ───────────────────────────────────────────────────────────────

    // Central damage application. Returns true if target was killed.
    // isPhysical: true for regular attacks; false for ability/passive damage.
    bool ApplyDamage(int tr, int tc, bool isPhysical, int killerPlayer)
    {
        var target = Board[tr, tc];
        if (target == null) return false;

        // Physical immunity: Bulwark (Battle Hardened) and Shardis (Phased Form)
        if (isPhysical && (target.key == "BULWARK" || target.key == "SHARDIS"))
        {
            Log($"{target.pieceName} is immune to physical damage!");
            return false;
        }

        // Ice Armor: Frostbite absorbs 1 physical damage
        if (isPhysical && target.key == "FROSTBITE")
        {
            Log($"{target.pieceName}'s Ice Armor absorbs the attack!");
            return false;
        }

        // Aegis personal Energy Shield (passive)
        if (target.energyShieldActive)
        {
            Log($"{target.pieceName}'s Energy Shield absorbs the hit!");
            target.energyShieldActive = false;
            return false;
        }

        // Barrier (ability shield)
        if (target.shielded)
        {
            Log($"{target.pieceName}'s barrier absorbs the hit!");
            target.shielded = false; target.shieldTurns = 0;
            return false;
        }

        // Deal damage
        target.shards -= 1;

        // Voltix: any damage resets static charges to 1
        if (target.key == "VOLTIX")
        {
            target.staticCharges = 1;
            Log($"{target.pieceName}'s Static Charge has been reset!");
        }

        Log($"{target.pieceName}: {target.shards + 1}→{target.shards} shards.");

        if (target.shards <= 0)
        {
            Log($"{target.pieceName} has been destroyed!");
            Board[tr, tc] = null;
            if (GameModeState.IsArena) OnArenaDeath(target, tr, tc);
            if (target.key == "MIMIC") RemoveMimicDecoy(target.id);

            // Aegis Energy Shield regenerates when any friendly kills an enemy
            if (killerPlayer != 0) RegenerateAegisShield(killerPlayer);

            return true;
        }
        return false;
    }

    void RegenerateAegisShield(int player)
    {
        for (int r = 0; r < BS; r++) for (int c = 0; c < BS; c++)
        {
            var p = Board[r, c];
            if (p != null && p.player == player && p.key == "AEGIS" && !p.energyShieldActive)
            {
                p.energyShieldActive = true;
                Log($"Aegis's Energy Shield regenerates!");
            }
        }
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
                Log("Phantom Lantern fades as Mimic falls.");
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

            if (p.abilityCd    > 0) { p.abilityCd--;    if (p.abilityCd == 0) Log($"{p.pieceName}: ability ready."); }
            if (p.shieldTurns   > 0) { p.shieldTurns--;   if (p.shieldTurns   == 0) { p.shielded  = false; Log($"{p.pieceName}: barrier expired."); } }
            if (p.rootedTurns   > 0) { p.rootedTurns--;   if (p.rootedTurns   == 0) { p.rooted    = false; Log($"{p.pieceName}: root cleared."); } }
            if (p.stunnedTurns  > 0) { p.stunnedTurns--;  if (p.stunnedTurns  == 0) { p.stunned   = false; Log($"{p.pieceName}: stun cleared."); } }
            if (p.weakenedTurns > 0) { p.weakenedTurns--; if (p.weakenedTurns == 0) { p.weakened  = false; Log($"{p.pieceName}: weaken cleared."); } }
            if (p.fortressTurns > 0) { p.fortressTurns--; if (p.fortressTurns == 0) { p.fortress  = false; Log($"{p.pieceName}: fortress ended."); } }

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
                        Log($"Scorch singes {enemy.pieceName}!");
                        ApplyDamage(nr, nc, isPhysical: false, killerPlayer: player);
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
                        Log($"Life Bloom heals {ally.pieceName}.");
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
            Log($"{piece.pieceName} picks up the ball!");
        }

        if (viaOwnMove && piece.hasBall && IsGoalTile(piece.player, r, c) && IsGoalOpen(piece.player, r, c))
            ScoreGoal(piece);
    }

    void ScoreGoal(Piece scorer)
    {
        if (scorer.player == 1) GoalsP1++; else GoalsP2++;
        scorer.hasBall = false;
        Log($"<b>GOAL!</b> {scorer.pieceName} scores! ({GoalsP1}–{GoalsP2})");
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
            Log($"{target.pieceName} drops the ball!");
        }

        if (target.key == "TOWER")
            Log("The fallen tower no longer blocks its outer goal tile!");
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
            Log($"{tower.pieceName} volley hits {Board[target.Value.r, target.Value.c].pieceName}!");
            for (int i = 0; i < ArenaConfig.TowerDamage; i++)
                if (ApplyDamage(target.Value.r, target.Value.c, isPhysical: false, killerPlayer: player)) break;
        }
    }

    void TickRespawns(int player)
    {
        for (int i = respawns.Count - 1; i >= 0; i--)
        {
            var e = respawns[i];
            if (e.piece.player != player) continue;
            e.turnsLeft--;
            if (e.turnsLeft > 0) { Log($"{e.piece.pieceName} respawns in {e.turnsLeft} turn(s)."); continue; }

            var spot = FindRespawnTile(e.homeR, e.homeC);
            if (spot == null) { e.turnsLeft = 1; continue; } // board jammed — try again next turn

            RevivePiece(e.piece);
            Board[spot.Value.r, spot.Value.c] = e.piece;
            respawns.RemoveAt(i);
            Log($"{e.piece.pieceName} respawns!");
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
}
