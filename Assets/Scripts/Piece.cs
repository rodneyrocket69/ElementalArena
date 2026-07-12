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
    public int move;
    public int dmg;

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
