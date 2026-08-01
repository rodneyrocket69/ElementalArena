using System;

[Serializable]
public class Piece
{
    public string key;
    public string id;
    public int player;

    public string pieceName;
    public string cls;
    public int maxShards;
    public int move;        // how far the piece may travel along its pattern (sliders only)
    public int dmg;

    // How this piece traverses the board — chess-style lines, blocked by other pieces
    public MovePattern movePattern;

    public AbilityDef ability;

    // runtime state
    public int shards;
    public int abilityCd;

    public bool isDecoy;
    public string decoyOwnerId;

    // status effects
    public bool shielded;      public int shieldTurns;
    public bool weakened;      public int weakenedTurns;
    public bool stunned;       public int stunnedTurns;
    public bool rooted;        public int rootedTurns;
    public bool fortress;      public int fortressTurns;

    // Voltix only
    public int staticCharges;

    // Aegis passive: personal regenerating shield (separate from Barrier ability shield)
    public bool energyShieldActive;

    // Arena mode: true while this piece is carrying the ball
    public bool hasBall;

    // One move per piece per turn — set by TryMove, cleared when its team's turn begins
    public bool hasMovedThisTurn;

    public Piece Clone()
    {
        var c = (Piece)MemberwiseClone();
        c.ability = ability;
        return c;
    }
}

[Serializable]
public struct AbilityDef
{
    public string name;
    public string desc;
    public int range;
    public int cooldown;
    public AbilityType type;
}

// Chess-style movement. Sliders travel in straight lines and are stopped by the
// first occupied tile; Knight leaps over anything. `Default` means "whatever
// PieceDefinitions declares" — it exists so the tuning asset can leave a piece alone.
public enum MovePattern
{
    Default = 0, // tuning-asset only: don't override the definition
    Rook,        // 4 orthogonal lines, up to `move` tiles
    Bishop,      // 4 diagonal lines, up to `move` tiles
    Queen,       // all 8 lines, up to `move` tiles
    Knight,      // L-jumps, ignores blockers, ignores `move`
    Immobile,    // never moves (towers)
}

public enum AbilityType
{
    Damage,
    Line,
    Freeze,   // roots enemies in target 3x3 (Frostbite Glacial Impact, Verdant Vine Snare)
    Shockwave,
    Fortress,
    Barrier,
    Heal,
    Decoy,
    Pull,
}
