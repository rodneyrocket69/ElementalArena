// ─────────────────────────────────────────────────────────────────────────────
// Facade over the one tuning object. All Arena Ball numbers now live on the
// GameParameters asset (Game.Params.arena) and are editable in the Inspector —
// live during Play mode. These forwarding properties keep every existing
// ArenaConfig.X call site working unchanged.
// Nothing in Classic mode reads this file.
// ─────────────────────────────────────────────────────────────────────────────
public static class ArenaConfig
{
    // ── Towers ──
    public static int TowerHP           => Game.Params.arena.towerHP;
    public static int TowerDamage       => Game.Params.arena.towerDamage;
    public static int TowerDefenseRange => Game.Params.arena.towerDefenseRange;

    // ── Scoring ──
    public static int GoalsToWin     => Game.Params.arena.goalsToWin;
    public static int GoalZoneMinCol => Game.Params.arena.goalZoneMinCol;
    public static int GoalZoneMaxCol => Game.Params.arena.goalZoneMaxCol;

    // ── Ball ──
    public static int CarrierMovePenalty => Game.Params.arena.carrierMovePenalty;
    public static int BallSpawnRow       => Game.Params.arena.ballSpawnRow;
    public static int BallSpawnCol       => Game.Params.arena.ballSpawnCol;

    // ── Respawns ──
    public static int RespawnTurns => Game.Params.arena.respawnTurns;
}
