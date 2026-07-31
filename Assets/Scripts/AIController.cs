using System.Collections;
using UnityEngine;

public class AIController : MonoBehaviour
{
    public BoardManager boardManager;
    public TurnManager  turnManager;

    // AI pacing now lives on the GameParameters asset (Game.Params.ai) so every
    // tunable sits in one place — edit thinkDelay / actionDelay in the Inspector.

    BoardVisual boardVisual; // found automatically so no scene wiring is needed

    void Start() => boardVisual = FindObjectOfType<BoardVisual>();

    public void BeginAITurn() => StartCoroutine(RunAITurn());

    // One candidate action (move, attack, or ability cast) with a score.
    // Every turn the AI scores every option across ALL its pieces and plays the best one.
    class AIAction
    {
        public enum Kind { Move, Attack, Ability }
        public Kind  kind;
        public int   r, c;    // acting piece
        public int   tr, tc;  // target cell
        public float score;
    }

    IEnumerator RunAITurn()
    {
        yield return new WaitForSeconds(Game.Params.ai.thinkDelay);

        for (int ap = 0; ap < 2; ap++)
        {
            if (boardManager.CheckWinner() != 0) break;

            var action = FindBestAction();
            if (action == null) { Log("The AI passes — it found nothing useful to do."); break; }

            Execute(action);
            if (boardVisual != null) boardVisual.RefreshAll(); // show each action as it happens
            yield return new WaitForSeconds(Game.Params.ai.actionDelay);
        }

        turnManager.EndAITurn();
    }

    void Execute(AIAction a)
    {
        switch (a.kind)
        {
            case AIAction.Kind.Move:    boardManager.TryMove(a.r, a.c, a.tr, a.tc);    break;
            case AIAction.Kind.Attack:  boardManager.TryAttack(a.r, a.c, a.tr, a.tc);  break;
            case AIAction.Kind.Ability: boardManager.TryAbility(a.r, a.c, a.tr, a.tc); break;
        }
    }

    // ── scoring ──────────────────────────────────────────────────────────────

    AIAction FindBestAction()
    {
        var board = boardManager.Board;
        AIAction best = null;

        for (int r = 0; r < BoardManager.BS; r++)
        for (int c = 0; c < BoardManager.BS; c++)
        {
            var p = board[r, c];
            if (p == null || p.player != 2 || p.isDecoy || p.stunned || p.key == "TOWER") continue;

            ScoreAttacks(p, r, c, ref best);
            ScoreAbility(p, r, c, ref best);
            ScoreMoves(p, r, c, ref best);
        }
        return best;
    }

    void Consider(ref AIAction best, AIAction candidate)
    {
        if (candidate.score <= 0f) return;
        if (best == null || candidate.score > best.score) best = candidate;
    }

    void ScoreAttacks(Piece p, int r, int c, ref AIAction best)
    {
        var board = boardManager.Board;
        foreach (var (tr, tc) in boardManager.GetAttacks(r, c))
        {
            var t = board[tr, tc];
            if (t == null) continue;
            Consider(ref best, new AIAction {
                kind = AIAction.Kind.Attack, r = r, c = c, tr = tr, tc = tc,
                score = HitValue(t, isPhysical: true)
            });
        }
    }

    // How valuable is landing one hit on this target? 0 = pointless, skip it.
    float HitValue(Piece t, bool isPhysical)
    {
        if (isPhysical && !t.weakened && (t.key == "BULWARK" || t.key == "FROSTBITE"))
            return 0f;                                    // immune/absorbed — wasted action (weaken disables these)
        if (t.hasBall) return 20f;                        // stop the carrier above all else
        if (t.isDecoy) return 2f;                         // pop the phantom
        if (t.energyShieldActive || t.shielded) return 2f;// burns a shield, no damage yet
        if (t.shards == 1) return 15f;                    // kill shot
        return 8f - t.shards;                             // prefer wounded targets
    }

    void ScoreAbility(Piece p, int r, int c, ref AIAction best)
    {
        if (p.abilityCd > 0) return;
        var board = boardManager.Board;

        foreach (var (tr, tc) in boardManager.GetCastTargets(r, c))
        {
            float score = 0f;

            switch (p.ability.type)
            {
                case AbilityType.Damage:
                {
                    var t = board[tr, tc];
                    if (t != null) score = HitValue(t, isPhysical: false);
                    break;
                }
                case AbilityType.Line:
                {
                    // Walk the line — only worth casting if the first piece hit is an enemy
                    int dr = System.Math.Sign(tr - r), dc = System.Math.Sign(tc - c);
                    int nr = r + dr, nc = c + dc;
                    while (BoardManager.InBounds(nr, nc) && board[nr, nc] == null) { nr += dr; nc += dc; }
                    if (BoardManager.InBounds(nr, nc))
                    {
                        var t = board[nr, nc];
                        if (t != null && t.player == 1) score = HitValue(t, isPhysical: false);
                    }
                    break;
                }
                case AbilityType.Freeze:
                {
                    int caught = 0;
                    for (int ddr = -1; ddr <= 1; ddr++) for (int ddc = -1; ddc <= 1; ddc++)
                    {
                        int nr = tr + ddr, nc = tc + ddc;
                        if (!BoardManager.InBounds(nr, nc)) continue;
                        var t = board[nr, nc];
                        if (t != null && t.player == 1 && !t.isDecoy && !t.rooted && t.key != "ZEPHYROS")
                            caught++;
                    }
                    score = caught * 4f;
                    break;
                }
                case AbilityType.Shockwave:
                {
                    int radius = p.staticCharges > 0 ? p.staticCharges : 1;
                    int caught = 0;
                    for (int ddr = -radius; ddr <= radius; ddr++) for (int ddc = -radius; ddc <= radius; ddc++)
                    {
                        int nr = r + ddr, nc = c + ddc;
                        if (!BoardManager.InBounds(nr, nc) || (ddr == 0 && ddc == 0)) continue;
                        var t = board[nr, nc];
                        if (t != null && t.player == 1 && !t.isDecoy && !t.stunned) caught++;
                    }
                    score = caught * 5f;
                    break;
                }
                case AbilityType.Fortress:
                    score = EnemyWithin(r, c, 3) ? 3f : 0f;
                    break;

                case AbilityType.Barrier:
                {
                    var t = board[tr, tc];
                    if (t != null && !t.isDecoy && !t.shielded && EnemyWithin(tr, tc, 2))
                        score = 3f + (t.maxShards - t.shards);
                    break;
                }
                case AbilityType.Heal:
                {
                    int hurt = 0;
                    for (int ddr = -1; ddr <= 1; ddr++) for (int ddc = -1; ddc <= 1; ddc++)
                    {
                        if (ddr == 0 && ddc == 0) continue;
                        int nr = r + ddr, nc = c + ddc;
                        if (!BoardManager.InBounds(nr, nc)) continue;
                        var t = board[nr, nc];
                        if (t != null && t.player == 2 && t.shards < t.maxShards) hurt++;
                    }
                    score = hurt * 3f;
                    break;
                }
                case AbilityType.Decoy:
                    score = EnemyWithin(r, c, 3) ? 2f : 0f;
                    break;

                case AbilityType.Pull:
                {
                    int caught = 0;
                    for (int rr = 0; rr < BoardManager.BS; rr++) for (int cc = 0; cc < BoardManager.BS; cc++)
                    {
                        if (rr == r && cc == c) continue;
                        var t = board[rr, cc];
                        if (t != null && t.player == 1 && !t.isDecoy &&
                            BoardManager.Cheb(rr, cc, r, c) <= p.ability.range) caught++;
                    }
                    score = caught * 3f;
                    break;
                }
            }

            Consider(ref best, new AIAction {
                kind = AIAction.Kind.Ability, r = r, c = c, tr = tr, tc = tc, score = score
            });
        }
    }

    void ScoreMoves(Piece p, int r, int c, ref AIAction best)
    {
        if (GameModeState.IsArena) { ScoreArenaMoves(p, r, c, ref best); return; }

        int curDist = DistToNearestEnemy(r, c);
        if (curDist == int.MaxValue) return;

        foreach (var (mr, mc) in boardManager.GetMoves(r, c))
        {
            int d = DistToNearestEnemy(mr, mc);
            if (d >= curDist) continue;               // only moves that close distance
            float score = 1f + (curDist - d) * 0.4f;  // kept small — attacks/abilities win ties
            Consider(ref best, new AIAction {
                kind = AIAction.Kind.Move, r = r, c = c, tr = mr, tc = mc, score = score
            });
        }
    }

    // Arena priorities: score a goal > grab the loose ball > chase the enemy carrier
    void ScoreArenaMoves(Piece p, int r, int c, ref AIAction best)
    {
        foreach (var (mr, mc) in boardManager.GetMoves(r, c))
        {
            float score = 0f;

            if (p.hasBall)
            {
                if (boardManager.IsGoalTile(2, mr, mc) && boardManager.IsGoalOpen(2, mr, mc))
                    score = 100f;                          // stepping onto an open goal tile scores
                else
                {
                    int cur = DistToGoal(r, c), d = DistToGoal(mr, mc);
                    if (d < cur) score = 6f + (cur - d) * 2f;
                }
            }
            else if (boardManager.BallLoose)
            {
                if (mr == boardManager.BallR && mc == boardManager.BallC)
                    score = 12f;                           // landing on the ball picks it up
                else
                {
                    int cur = BoardManager.Cheb(r, c, boardManager.BallR, boardManager.BallC);
                    int d   = BoardManager.Cheb(mr, mc, boardManager.BallR, boardManager.BallC);
                    if (d < cur) score = 2f + (cur - d) * 0.5f;
                }
            }
            else
            {
                var (cr, cc) = FindEnemyCarrier();
                if (cr >= 0)
                {
                    int cur = BoardManager.Cheb(r, c, cr, cc);
                    int d   = BoardManager.Cheb(mr, mc, cr, cc);
                    if (d < cur) score = 3f + (cur - d) * 0.8f;
                }
            }

            if (score > 0f)
                Consider(ref best, new AIAction { kind = AIAction.Kind.Move, r = r, c = c, tr = mr, tc = mc, score = score });
        }
    }

    // Chebyshev distance to the closest open goal tile on the player's back row
    int DistToGoal(int r, int c)
    {
        int gr = BoardManager.GoalRowFor(2), best = int.MaxValue;
        for (int gc = ArenaConfig.GoalZoneMinCol; gc <= ArenaConfig.GoalZoneMaxCol; gc++)
        {
            if (!boardManager.IsGoalOpen(2, gr, gc)) continue;
            best = Mathf.Min(best, BoardManager.Cheb(r, c, gr, gc));
        }
        return best;
    }

    (int, int) FindEnemyCarrier()
    {
        for (int r = 0; r < BoardManager.BS; r++) for (int c = 0; c < BoardManager.BS; c++)
        {
            var t = boardManager.Board[r, c];
            if (t != null && t.player == 1 && t.hasBall) return (r, c);
        }
        return (-1, -1);
    }

    int DistToNearestEnemy(int r, int c)
    {
        var board = boardManager.Board;
        int bestD = int.MaxValue;
        for (int rr = 0; rr < BoardManager.BS; rr++) for (int cc = 0; cc < BoardManager.BS; cc++)
        {
            var t = board[rr, cc];
            if (t != null && t.player == 1 && !t.isDecoy)
                bestD = Mathf.Min(bestD, BoardManager.Cheb(rr, cc, r, c));
        }
        return bestD;
    }

    bool EnemyWithin(int r, int c, int range)
    {
        var board = boardManager.Board;
        for (int rr = 0; rr < BoardManager.BS; rr++) for (int cc = 0; cc < BoardManager.BS; cc++)
        {
            var t = board[rr, cc];
            if (t != null && t.player == 1 && !t.isDecoy && BoardManager.Cheb(rr, cc, r, c) <= range)
                return true;
        }
        return false;
    }

    void Log(string msg) => turnManager.Log(msg);
}
