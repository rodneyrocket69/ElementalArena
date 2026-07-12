// ─────────────────────────────────────────────────────────────────────────────
// ALL Arena Ball tuning numbers live here. Change these to balance the mode.
// Nothing in Classic mode reads this file.
// ─────────────────────────────────────────────────────────────────────────────
public static class ArenaConfig
{
    // ── Towers ──
    public static int TowerHP           = 5;  // health of each corner tower
    public static int TowerDamage       = 1;  // damage per defense volley
    public static int TowerDefenseRange = 2;  // tiles (Chebyshev) a tower can shoot

    // ── Scoring ──
    public static int GoalsToWin = 2;         // first team to this many goals wins

    // Goal zone: columns on each back row that count as goal tiles.
    // Cols 2..5 = 4 tiles wide, centered on the 8-wide board.
    // The outer tile on each side is blocked while the tower in that corner
    // is standing; the two center tiles are always open.
    public static int GoalZoneMinCol = 2;
    public static int GoalZoneMaxCol = 5;

    // ── Ball ──
    public static int CarrierMovePenalty = 1; // move range lost while carrying

    // Where the ball starts, and where it returns after each goal.
    public static int BallSpawnRow = 3;
    public static int BallSpawnCol = 4;

    // ── Respawns ──
    public static int RespawnTurns = 2;       // owner-turns a destroyed piece waits
}
