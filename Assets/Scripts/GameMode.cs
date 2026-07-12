// Which game mode is running. Classic is the original chess-style ability game;
// Arena is the ball-and-towers objective mode. ModeSelectUI sets this at startup.
public enum GameMode { Classic, Arena }

public static class GameModeState
{
    public static GameMode Current = GameMode.Classic;

    public static bool IsArena => Current == GameMode.Arena;
}
