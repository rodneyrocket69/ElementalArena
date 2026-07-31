// ─────────────────────────────────────────────────────────────────────────────
// Facade over the one tuning object for shared combat rules (status durations,
// passive/ability amounts, per-piece caps). Values live on the GameParameters
// asset (Game.Params.combat) and are read live, so edits apply instantly.
// ─────────────────────────────────────────────────────────────────────────────
public static class CombatConfig
{
    // ── Turn / attacks ──
    public static int ActionsPerTurn => Game.Params.combat.actionsPerTurn;
    public static int AttackRange    => Game.Params.combat.attackRange;

    // ── Status durations (owner-turns) ──
    public static int RootTurns     => Game.Params.combat.rootTurns;
    public static int StunTurns     => Game.Params.combat.stunTurns;
    public static int FortressTurns => Game.Params.combat.fortressTurns;
    public static int BarrierTurns  => Game.Params.combat.barrierTurns;
    public static int WeakenTurns   => Game.Params.combat.weakenTurns;

    // ── Passive / ability amounts ──
    public static int IceArmorReduction => Game.Params.combat.iceArmorReduction;
    public static int ScorchDamage      => Game.Params.combat.scorchDamage;
    public static int LifeBloomHeal     => Game.Params.combat.lifeBloomHeal;
    public static int HealAmount        => Game.Params.combat.healAmount;

    // ── Piece-specific ──
    public static int VoltixMaxCharges      => Game.Params.combat.voltixMaxCharges;
    public static int FortressRedirectRange => Game.Params.combat.fortressRedirectRange;
}
