using System;
using UnityEngine;

public enum GamePhase { Player, AI, Over }

public class TurnManager : MonoBehaviour
{
    public BoardManager boardManager;
    public AIController aiController;

    public GamePhase Phase      { get; private set; } = GamePhase.Player;
    public int       AP         { get; private set; } = 2;
    public int       Winner     { get; private set; } = 0;
    public int       TurnNumber { get; private set; } = 1;

    // Log header colors (rich text) — gold for the player, blue for the AI.
    // Leading newline puts a blank line above each header so turns read in chunks.
    public const string PlayerHeaderFmt = "\n<color=#ffd24d><b>— TURN {0} — YOUR TURN —</b></color>";
    public const string AIHeaderFmt     = "\n<color=#7fb8e6><b>— TURN {0} — AI —</b></color>";

    public event Action<GamePhase> OnPhaseChanged;
    public event Action<int>       OnAPChanged;
    public event Action<int>       OnGameOver;
    public event Action<string>    OnLog;

    void Awake()
    {
        AP = CombatConfig.ActionsPerTurn;
        boardManager.OnLog += msg => OnLog?.Invoke(msg);

        // Arena: a goal can end the game mid-turn — don't wait for the turn to end
        boardManager.OnGoalScored += () =>
        {
            int w = boardManager.CheckWinner();
            if (w != 0) TriggerGameOver(w);
        };
    }

    public void ConsumeAP(int amount = 1)
    {
        AP = Mathf.Max(0, AP - amount);
        OnAPChanged?.Invoke(AP);
        Log(AP > 0 ? $"{AP} AP remaining." : "0 AP — ending turn.");
        if (AP <= 0) EndPlayerTurn();
    }

    public void EndPlayerTurn()
    {
        if (Phase == GamePhase.Over) return;

        int w = boardManager.CheckWinner();
        if (w != 0) { TriggerGameOver(w); return; }

        boardManager.TickEffects(1);

        // Scorch or other passives may have killed pieces
        w = boardManager.CheckWinner();
        if (w != 0) { TriggerGameOver(w); return; }

        SetPhase(GamePhase.AI);
        Log(string.Format(AIHeaderFmt, TurnNumber));
        boardManager.ResetTurnMovement(2);
        aiController.BeginAITurn();
    }

    public void EndAITurn()
    {
        if (Phase == GamePhase.Over) return;

        int w = boardManager.CheckWinner();
        if (w != 0) { TriggerGameOver(w); return; }

        boardManager.TickEffects(2);

        w = boardManager.CheckWinner();
        if (w != 0) { TriggerGameOver(w); return; }

        AP = CombatConfig.ActionsPerTurn;
        TurnNumber++;
        OnAPChanged?.Invoke(AP);
        boardManager.ResetTurnMovement(1);
        SetPhase(GamePhase.Player);

        Log(string.Format(PlayerHeaderFmt, TurnNumber));
        if (GameModeState.IsArena)
        {
            Log($"2 AP. Goals: You {boardManager.GoalsP1} — {boardManager.GoalsP2} AI (first to {ArenaConfig.GoalsToWin}).");
        }
        else
        {
            int remaining = 0;
            for (int r = 0; r < BoardManager.BS; r++) for (int c = 0; c < BoardManager.BS; c++)
            {
                var p = boardManager.Board[r, c];
                if (p != null && !p.isDecoy && p.player == 2) remaining++;
            }
            Log($"2 AP. Enemies remaining: {remaining}.");
        }
    }

    public void ResetGame()
    {
        boardManager.InitBoard();
        boardManager.ResetTurnMovement(1);
        AP = CombatConfig.ActionsPerTurn; Winner = 0; TurnNumber = 1;
        OnAPChanged?.Invoke(AP);
        SetPhase(GamePhase.Player);
        Log(string.Format(PlayerHeaderFmt, TurnNumber));
        Log("New game. Your move.");
    }

    void TriggerGameOver(int winner)
    {
        if (Phase == GamePhase.Over) return;
        Winner = winner;
        SetPhase(GamePhase.Over);
        OnGameOver?.Invoke(winner);
        OnLog?.Invoke(winner == 1 ? "PLAYER I VICTORIOUS!" : "AI VICTORIOUS!");
    }

    public void Log(string msg) => OnLog?.Invoke(msg);

    void SetPhase(GamePhase p) { Phase = p; OnPhaseChanged?.Invoke(p); }
}
