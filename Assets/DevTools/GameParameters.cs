using System;
using System.Collections.Generic;
using UnityEngine;

// ─────────────────────────────────────────────────────────────────────────────
// THE single tuning object for Elemental Arena. Every gameplay number lives here.
//
// Select the asset (Assets/Resources/GameParameters.asset) and edit it in the
// Inspector — live during Play mode:
//   • Combat / Arena / AI values are read every time the rules run, so edits
//     apply INSTANTLY (durations, damage, tower stats, scoring, AI pacing…).
//   • Piece stats (HP / move / dmg / ability range / cooldown) are baked into a
//     piece when the board is built, so they apply on the NEXT board build —
//     press "Rebuild Board" (Play mode) or start a new game.
//
// Access from code through the facades: Game.Params, ArenaConfig, CombatConfig.
// ─────────────────────────────────────────────────────────────────────────────
[CreateAssetMenu(fileName = "GameParameters", menuName = "Elemental Arena/Game Parameters", order = 0)]
public class GameParameters : ScriptableObject
{
    // ── Combat / status rules (LIVE) ─────────────────────────────────────────
    [Serializable]
    public class CombatGroup
    {
        [Header("Turn")]
        [Tooltip("Action points each player gets at the start of their turn.")]
        [Range(1, 6)] public int actionsPerTurn = 2;

        [Header("Attacks")]
        [Tooltip("Chebyshev range of a basic attack (1 = adjacent 8 tiles).")]
        [Range(1, 4)] public int attackRange = 1;

        [Header("Status durations (owner-turns)")]
        [Range(0, 6)] public int rootTurns = 2;      // Freeze (Glacial Impact / Vine Snare)
        [Range(0, 6)] public int stunTurns = 1;      // Shockwave (Voltix)
        [Range(0, 6)] public int fortressTurns = 2;  // Magnetic Fortress (Bulwark)
        [Range(0, 6)] public int barrierTurns = 2;   // Barrier (Aegis)
        [Range(0, 6)] public int weakenTurns = 2;    // Gravitic Distortion (Shardis)

        [Header("Passive / ability amounts")]
        [Tooltip("Damage Frostbite's Ice Armor removes from each physical hit.")]
        [Range(0, 5)] public int iceArmorReduction = 1;
        [Tooltip("Damage Flare's Scorch deals to each adjacent enemy at end of turn.")]
        [Range(0, 5)] public int scorchDamage = 1;
        [Tooltip("Shards Verdant's Life Bloom heals each adjacent ally at end of turn.")]
        [Range(0, 5)] public int lifeBloomHeal = 1;
        [Tooltip("Shards healed per target by a Heal-type ability.")]
        [Range(0, 5)] public int healAmount = 1;

        [Header("Piece-specific")]
        [Tooltip("Cap on Voltix Static Charges (also caps Shockwave radius).")]
        [Range(1, 6)] public int voltixMaxCharges = 3;
        [Tooltip("How far Bulwark's Magnetic Fortress redirects attacks on allies.")]
        [Range(1, 4)] public int fortressRedirectRange = 2;
    }

    // ── Arena mode rules (LIVE) ──────────────────────────────────────────────
    [Serializable]
    public class ArenaGroup
    {
        [Header("Towers")]
        public int towerHP = 5;
        public int towerDamage = 1;
        public int towerDefenseRange = 2;

        [Header("Scoring")]
        [Tooltip("First team to this many goals wins.")]
        public int goalsToWin = 2;
        [Tooltip("Goal-zone columns on each back row (inclusive).")]
        public int goalZoneMinCol = 2;
        public int goalZoneMaxCol = 5;

        [Header("Ball")]
        [Tooltip("Move range lost while carrying the ball.")]
        public int carrierMovePenalty = 1;
        public int ballSpawnRow = 3;
        public int ballSpawnCol = 4;

        [Header("Respawns")]
        [Tooltip("Owner-turns a destroyed piece waits before respawning.")]
        public int respawnTurns = 2;
    }

    // ── AI (LIVE) ────────────────────────────────────────────────────────────
    [Serializable]
    public class AIGroup
    {
        [Header("Pacing (seconds)")]
        [Tooltip("Delay before the AI's first action — for feel.")]
        [Range(0f, 3f)] public float thinkDelay = 0.9f;
        [Tooltip("Delay between the AI's two actions.")]
        [Range(0f, 3f)] public float actionDelay = 0.6f;
    }

    // ── Per-piece stats (applied on board build) ─────────────────────────────
    [Serializable]
    public class PieceParams
    {
        [Tooltip("Definition key — must match PieceDefinitions (e.g. FLARE).")]
        public string key;
        public string displayName;
        [Range(1, 12)] public int maxShards;   // HP
        [Range(0, 8)]  public int move;
        [Range(0, 6)]  public int dmg;
        [Range(0, 8)]  public int abilityRange;
        [Range(0, 8)]  public int abilityCooldown;
    }

    public CombatGroup combat = new CombatGroup();
    public ArenaGroup  arena  = new ArenaGroup();
    public AIGroup     ai     = new AIGroup();

    [Tooltip("Numeric stats per piece. Names/descriptions/ability types stay in PieceDefinitions.")]
    public List<PieceParams> pieces = new List<PieceParams>();

    // ── Lookup ───────────────────────────────────────────────────────────────
    Dictionary<string, PieceParams> _lookup;

    public PieceParams GetPiece(string key)
    {
        int count = pieces?.Count ?? 0;
        if (_lookup == null || _lookup.Count != count)
        {
            _lookup = new Dictionary<string, PieceParams>(count);
            if (pieces != null)
                foreach (var p in pieces)
                    if (p != null && !string.IsNullOrEmpty(p.key))
                        _lookup[p.key] = p;
        }
        return _lookup.TryGetValue(key, out var pp) ? pp : null;
    }

    public void InvalidateLookup() => _lookup = null;

    // ── Defaults / seeding ───────────────────────────────────────────────────
    public void EnsureSeeded()
    {
        if (combat == null) combat = new CombatGroup();
        if (arena  == null) arena  = new ArenaGroup();
        if (ai     == null) ai     = new AIGroup();
        if (pieces == null || pieces.Count == 0) SeedPieces();
    }

    public void ResetToDefaults()
    {
        combat = new CombatGroup();
        arena  = new ArenaGroup();
        ai     = new AIGroup();
        SeedPieces();
    }

    // Defaults mirror PieceDefinitions.BASE — keep in sync if a new piece is added.
    void SeedPieces()
    {
        pieces = new List<PieceParams>
        {
            //         key          name          HP move dmg  range  cd
            P("FLARE",     "Flare",     2, 4, 1, 2, 2),
            P("VOLTIX",    "Voltix",    2, 2, 1, 0, 3),
            P("ZEPHYROS",  "Zephyros",  2, 3, 1, 7, 3),
            P("FROSTBITE", "Frostbite", 4, 2, 1, 2, 4),
            P("BULWARK",   "Bulwark",   4, 2, 1, 0, 3),
            P("AEGIS",     "Aegis",     4, 3, 1, 3, 3),
            P("VERDANT",   "Verdant",   3, 2, 1, 2, 3),
            P("MIMIC",     "Mimic",     3, 3, 1, 1, 4),
            P("SHARDIS",   "Shardis",   3, 2, 1, 3, 5),
        };
        _lookup = null;
    }

    static PieceParams P(string key, string name, int hp, int move, int dmg, int range, int cd) =>
        new PieceParams
        {
            key = key, displayName = name,
            maxShards = hp, move = move, dmg = dmg,
            abilityRange = range, abilityCooldown = cd
        };
}
