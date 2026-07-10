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

    void Awake() => InitBoard();

    public void InitBoard()
    {
        Board = new Piece[BS, BS];
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

        for (int dr = -effectiveMove; dr <= effectiveMove; dr++)
        for (int dc = -effectiveMove; dc <= effectiveMove; dc++)
        {
            if (dr == 0 && dc == 0) continue;
            int nr = row + dr, nc = col + dc;
            if (InBounds(nr, nc) && Board[nr, nc] == null) result.Add((nr, nc));
        }
        return result;
    }

    public List<(int r, int c)> GetAttacks(int row, int col)
    {
        var piece = Board[row, col];
        var result = new List<(int, int)>();
        if (piece == null || piece.stunned || piece.isDecoy) return result;
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
        if (piece == null || piece.abilityCd > 0 || piece.stunned || piece.isDecoy) return result;
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
                    var t=Board[r,c]; if (t==null) continue;
                    int dr=Math.Sign(row-r), dc=Math.Sign(col-c);
                    int nr=r+dr, nc=c+dc;
                    if (InBounds(nr,nc)&&Board[nr,nc]==null)
                    {
                        Board[nr,nc]=t; Board[r,c]=null;
                        if (t.player!=piece.player) { t.weakened=true; t.weakenedTurns=2; }
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

    void Log(string msg) => OnLog?.Invoke(msg);
}
